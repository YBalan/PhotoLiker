using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Search
{
    /// <summary>The photo half of CLIP: a photo becomes a unit vector of 512 numbers.</summary>
    public sealed class ClipImageEncoder : IDisposable
    {
        public const int Dimensions = 512;
        public const int Size = 224;
        public const int InputLength = 3 * Size * Size;

        private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
        private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly string _output;

        public ClipImageEncoder(string modelPath, int threads)
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // several models share the processor

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
            _output = _session.OutputMetadata.ContainsKey("image_embeds") ? "image_embeds" : _session.OutputMetadata.Keys.First();
        }

        /// <summary>
        /// The model input of a photo: the shortest side scaled to 224 pixels, the centre 224 x 224 square,
        /// normalized, as planar RGB floats.
        /// </summary>
        public static float[] Prepare(DecodedPhoto photo)
        {
            var scale = Size / (double)Math.Min(photo.Width, photo.Height);
            var width = Math.Max(Size, (int)Math.Round(photo.Width * scale));
            var height = Math.Max(Size, (int)Math.Round(photo.Height * scale));
            var resized = Resampler.ResizeBgr(photo.Bgr, photo.Width, photo.Height, width, height);

            var left = (width - Size) / 2;
            var top = (height - Size) / 2;
            var plane = Size * Size;
            var data = new float[InputLength];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var p = ((top + y) * width + left + x) * 3;
                    var i = y * Size + x;
                    data[i] = (resized[p + 2] / 255f - Mean[0]) / Std[0];
                    data[plane + i] = (resized[p + 1] / 255f - Mean[1]) / Std[1];
                    data[2 * plane + i] = (resized[p] / 255f - Mean[2]) / Std[2];
                }
            }
            return data;
        }

        /// <summary>Embeds <paramref name="count"/> prepared photos that lie one after another in <paramref name="inputs"/>.</summary>
        public float[][] Encode(float[] inputs, int count)
        {
            var tensor = new DenseTensor<float>(inputs.AsMemory(0, count * InputLength), [count, 3, Size, Size]);
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)]);
            var flat = results.First(r => r.Name == _output).AsTensor<float>().ToArray();

            var rows = new float[count][];
            for (var i = 0; i < count; i++)
            {
                rows[i] = flat.AsSpan(i * Dimensions, Dimensions).ToArray();
                VectorMath.Normalize(rows[i]);
            }
            return rows;
        }

        public void Dispose() => _session.Dispose();
    }
}
