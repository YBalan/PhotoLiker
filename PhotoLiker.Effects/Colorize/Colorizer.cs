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
        private readonly RunGuard _guard;

        /// <exception cref="FileNotFoundException">The model's file is not there (see <see cref="EffectModels.ColorizerDownloaded"/>).</exception>
        public Colorizer(string modelPath, int threads)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("The colorizing model is not on this computer.", modelPath);

            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");

            // No memory arena: with it (the default) the session keeps everything its runs have used for as long as it lives, 2.1 GB after the first run and 4.6 GB after the second (the memory
            // pattern it plans from the first run is put beside the first run's blocks), and nothing gives it back, so a model that is kept open for a person who colours photos one after the other
            // sits on 4.6 GB. Without it the runs take their memory from Windows and give it back when they end: measured on the DDColor tiny model, 0.33 GB open or running idle (as the
            // 270 MB of this class's description says), 2.4 s a run instead of 1.8 s. (The pattern is off with it: it needs the arena.)
            options.EnableCpuMemArena = false;
            options.EnableMemoryPattern = false;

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
            _guard = new RunGuard(_session.Dispose, nameof(Colorizer));
        }

        /// <summary>
        /// The photo with colours: the same size, the lightness of every point as it was. A photo that already has colours is not made grey first for it: only its lightness is
        /// looked at, so its colours are replaced by the model's, whatever they were.
        /// </summary>
        /// <param name="saturation">How strong the colours are: 1 as the model gives them, less is paler, more is stronger (the Lab a and b are multiplied by it).</param>
        public DecodedPhoto Colorize(DecodedPhoto photo, float saturation = 1f) =>
            Apply(photo, Analyze(photo), new ColorTuning(saturation));

        /// <summary>
        /// Runs the model on the photo (the two seconds of it) and gives the colours it thinks the photo should have. They can then be put on the photo with <see cref="Apply"/>, as often as wanted and
        /// with any <see cref="ColorTuning"/>, without running the model again: this is how a person can change the colours and see the photo change as they do.
        /// </summary>
        public ModelColours Analyze(DecodedPhoto photo) => new(Chroma(photo));

        /// <summary>
        /// The photo with the colours that <see cref="Analyze"/> gave it (for this photo, or a larger or smaller picture of it), changed as <paramref name="tuning"/> says. Takes tens of milliseconds
        /// for a few megapixels, on all the cores, and needs no model. Safe to call from any thread.
        /// </summary>
        public static DecodedPhoto Apply(DecodedPhoto photo, ModelColours colours, ColorTuning tuning) => Combine(photo, colours.Values, tuning);

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

            _guard.Enter(); // the program may be closing: the session is not freed under a run that has begun
            try
            {
                using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<float>(input, [1, 3, ModelSide, ModelSide]))]);
                var chroma = results.First().AsTensor<float>().ToArray();
                if (chroma.Length != 2 * plane)
                    throw new InvalidDataException($"The colorizing model gave {chroma.Length} numbers, not {2 * plane}.");
                return chroma;
            }
            finally
            {
                _guard.Leave();
            }
        }

        /// <summary>The photo's own lightness with the colours of the model, stretched (smoothly) to the size of the photo.</summary>
        internal static DecodedPhoto Combine(DecodedPhoto photo, float[] chroma, float saturation) =>
            Combine(photo, chroma, new ColorTuning(saturation));

        internal static DecodedPhoto Combine(DecodedPhoto photo, float[] chroma, ColorTuning tuning)
        {
            var saturation = tuning.Saturation;
            var warmth = tuning.Warmth;
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
                    var a = Sample(0) * saturation;
                    var b = Sample(plane) * saturation;
                    if (warmth != 0f)
                    {
                        var shift = warmth * ColorTuning.WarmthWeight(lightness);
                        a += shift * ColorTuning.WarmthA;
                        b += shift * ColorTuning.WarmthB;
                    }

                    LabColor.ToBgr(lightness, a, b, out result[p], out result[p + 1], out result[p + 2]);
                }
            });

            return new DecodedPhoto(width, height, result);
        }

        /// <summary>Lets go of the model: at once, or when the last run that is going on has ended (see <see cref="RunGuard"/>). A run begun after this is refused.</summary>
        public void Dispose() => _guard.Dispose();
    }

    /// <summary>The colours that the colorizing model gave a photo (see <see cref="Colorizer.Analyze"/>): kept, so that they can be put on the photo again with other <see cref="ColorTuning"/>s.</summary>
    public sealed class ModelColours
    {
        internal ModelColours(float[] values)
        {
            if (values.Length != 2 * Colorizer.ModelSide * Colorizer.ModelSide)
                throw new ArgumentException($"The colours of the model are {2 * Colorizer.ModelSide * Colorizer.ModelSide} numbers, not {values.Length}.", nameof(values));
            Values = values;
        }

        /// <summary>First all the a's, then all the b's, of a square picture of <see cref="Colorizer.ModelSide"/> points a side.</summary>
        internal float[] Values { get; }
    }
}
