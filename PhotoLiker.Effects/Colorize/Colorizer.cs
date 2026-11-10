using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Colorize
{
    /// <summary>
    /// Gives colours to a black and white photo with DDColor tiny (<see cref="ToolModels.Colorizer"/>), on this computer: 1.5 to 2 s for a photo on six cores, whatever its size.
    ///
    /// The model sees the photo as a 512 by 512 picture of grey and says what colour (the a and b of Lab) each point of it should have. The lightness of the result is that of the photo
    /// itself, at the size it has, so nothing of its detail is lost: only the colours come from the model, and they are smooth enough to be stretched to the size of the photo.
    /// </summary>
    public sealed class Colorizer : IDisposable
    {
        /// <summary>The side of the square the model works on.</summary>
        public const int ModelSide = 512;

        private readonly InferenceSession _session;
        private readonly string _input;

        /// <exception cref="FileNotFoundException">The model's file is not there (see <see cref="EffectModels.ColorizerDownloaded"/>).</exception>
        public Colorizer(string modelPath, int threads)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("The colorizing model is not on this computer.", modelPath);

            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
        }

        /// <summary>
        /// The photo with colours: the same size, the lightness of every point as it was. A photo that already has colours is not made grey first for it: only its lightness is
        /// looked at, so its colours are replaced by the model's, whatever they were.
        /// </summary>
        /// <param name="saturation">How strong the colours are: 1 as the model gives them, less is paler, more is stronger (the Lab a and b are multiplied by it).</param>
        public DecodedPhoto Colorize(DecodedPhoto photo, float saturation = 1f)
        {
            var chroma = Chroma(photo);
            return Combine(photo, chroma, saturation);
        }

        /// <summary>The a and b the model gives for every point of a square 512 by 512 picture of the photo: first all the a's, then all the b's.</summary>
        private float[] Chroma(DecodedPhoto photo)
        {
            var small = Resampler.ResizeBgr(photo.Bgr, photo.Width, photo.Height, ModelSide, ModelSide);
            var plane = ModelSide * ModelSide;
            var input = new float[3 * plane];
            for (var i = 0; i < plane; i++)
            {
                // The model is given the picture as a grey one: a point with the lightness of the photo's and no colour, which is that light in all three channels
                var grey = LabColor.FromLinear(LabColor.Luminance(small[i * 3], small[i * 3 + 1], small[i * 3 + 2]));
                input[i] = grey;
                input[plane + i] = grey;
                input[2 * plane + i] = grey;
            }

            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<float>(input, [1, 3, ModelSide, ModelSide]))]);
            var chroma = results.First().AsTensor<float>().ToArray();
            if (chroma.Length != 2 * plane)
                throw new InvalidDataException($"The colorizing model gave {chroma.Length} numbers, not {2 * plane}.");
            return chroma;
        }

        /// <summary>The photo's own lightness with the colours of the model, stretched (smoothly) to the size of the photo.</summary>
        internal static DecodedPhoto Combine(DecodedPhoto photo, float[] chroma, float saturation)
        {
            var width = photo.Width;
            var height = photo.Height;
            var plane = ModelSide * ModelSide;
            var result = new byte[photo.Bgr.Length];

            var columns = new (int From, int To, float Weight)[width];
            for (var x = 0; x < width; x++)
            {
                var at = Math.Clamp((x + 0.5) * ModelSide / width - 0.5, 0, ModelSide - 1);
                var from = (int)at;
                columns[x] = (from, Math.Min(from + 1, ModelSide - 1), (float)(at - from));
            }

            Parallel.For(0, height, y =>
            {
                var at = Math.Clamp((y + 0.5) * ModelSide / height - 0.5, 0, ModelSide - 1);
                var top = (int)at * ModelSide;
                var bottom = Math.Min((int)at + 1, ModelSide - 1) * ModelSide;
                var wy = (float)(at - (int)at);

                for (var x = 0; x < width; x++)
                {
                    var (from, to, wx) = columns[x];
                    float Sample(int offset)
                    {
                        var upper = chroma[offset + top + from] * (1 - wx) + chroma[offset + top + to] * wx;
                        var lower = chroma[offset + bottom + from] * (1 - wx) + chroma[offset + bottom + to] * wx;
                        return upper * (1 - wy) + lower * wy;
                    }

                    var p = (y * width + x) * 3;
                    var lightness = LabColor.LightnessOf(LabColor.Luminance(photo.Bgr[p], photo.Bgr[p + 1], photo.Bgr[p + 2]));
                    LabColor.ToBgr(lightness, Sample(0) * saturation, Sample(plane) * saturation, out result[p], out result[p + 1], out result[p + 2]);
                }
            });

            return new DecodedPhoto(width, height, result);
        }

        public void Dispose() => _session.Dispose();
    }
}
