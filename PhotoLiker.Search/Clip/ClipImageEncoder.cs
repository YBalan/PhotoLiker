using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Search
{
    /// <summary>The photo half of a CLIP-like model: a photo becomes a unit vector (see <see cref="ClipModelInfo.Dimensions"/>).</summary>
    public sealed class ClipImageEncoder : IDisposable
    {
        private readonly ClipModelInfo _model;
        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly string _output;
        private readonly bool _oneAtATime; // the model was exported for a batch of exactly one photo

        public ClipImageEncoder(string modelPath, int threads, ClipModelInfo model)
        {
            _model = model;

            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // several models share the processor

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
            _output = _session.OutputMetadata.ContainsKey("image_embeds") ? "image_embeds"
                : _session.OutputMetadata.ContainsKey("embedding") ? "embedding"
                : _session.OutputMetadata.Keys.First();
            _oneAtATime = _session.InputMetadata[_input].Dimensions is [> 0, ..];
        }

        /// <summary>
        /// The model input of a photo: planar RGB floats, normalized with the mean and deviation of the model, either the centre square of the
        /// photo scaled to the model's size or the whole photo squeezed into the square (<see cref="ClipModelInfo.Fit"/>).
        /// </summary>
        public static float[] Prepare(DecodedPhoto photo, ClipModelInfo model)
        {
            var size = model.ImageSize;
            int width, height;
            if (model.Fit == PhotoFit.Squash)
            {
                (width, height) = (size, size);
            }
            else
            {
                var scale = size / (double)Math.Min(photo.Width, photo.Height);
                width = Math.Max(size, (int)Math.Round(photo.Width * scale));
                height = Math.Max(size, (int)Math.Round(photo.Height * scale));
            }
            var resized = Resampler.ResizeBgr(photo.Bgr, photo.Width, photo.Height, width, height);

            var left = (width - size) / 2;
            var top = (height - size) / 2;
            var plane = size * size;
            var data = new float[model.InputLength];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = ((top + y) * width + left + x) * 3;
                    var i = y * size + x;
                    data[i] = (resized[p + 2] / 255f - model.Mean[0]) / model.Std[0];
                    data[plane + i] = (resized[p + 1] / 255f - model.Mean[1]) / model.Std[1];
                    data[2 * plane + i] = (resized[p] / 255f - model.Mean[2]) / model.Std[2];
                }
            }
            return data;
        }

        /// <summary>Embeds <paramref name="count"/> prepared photos that lie one after another in <paramref name="inputs"/>.</summary>
        public float[][] Encode(float[] inputs, int count)
        {
            var length = _model.InputLength;
            var size = _model.ImageSize;
            var rows = new float[count][];

            // A model exported for one photo at a time takes them one by one; the others take all of them at once
            var step = _oneAtATime ? 1 : count;
            for (var first = 0; first < count; first += step)
            {
                var tensor = new DenseTensor<float>(inputs.AsMemory(first * length, step * length), [step, 3, size, size]);
                using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)]);
                var flat = results.First(r => r.Name == _output).AsTensor<float>().ToArray();
                if (flat.Length != step * _model.Dimensions)
                    throw new InvalidDataException($"The photo model gave {flat.Length / step} numbers per photo, {_model.Title} has {_model.Dimensions}.");

                for (var i = 0; i < step; i++)
                {
                    rows[first + i] = flat.AsSpan(i * _model.Dimensions, _model.Dimensions).ToArray();
                    VectorMath.Normalize(rows[first + i]);
                }
            }
            return rows;
        }

        public void Dispose() => _session.Dispose();
    }
}
