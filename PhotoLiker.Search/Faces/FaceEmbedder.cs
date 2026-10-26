using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Search
{
    /// <summary>
    /// Turns an aligned face into a unit vector of 512 numbers with InsightFace's ArcFace model (w600k_r50):
    /// the faces of one person end up close together, different people far apart.
    /// </summary>
    public sealed class FaceEmbedder : IDisposable
    {
        public const int Dimensions = 512;

        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly bool _singleFaceOnly;

        public FaceEmbedder(string modelPath, int threads)
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // several models share the processor

            _session = new InferenceSession(modelPath, options);
            _input = _session.InputMetadata.Keys.First();
            _singleFaceOnly = _session.InputMetadata[_input].Dimensions[0] == 1; // a fixed batch of one
        }

        /// <summary>Embeds <paramref name="count"/> aligned faces that lie one after another in <paramref name="faces"/>.</summary>
        public float[][] Embed(float[] faces, int count)
        {
            var rows = new float[count][];
            if (_singleFaceOnly)
            {
                for (var i = 0; i < count; i++)
                    rows[i] = Run(faces.AsMemory(i * FaceAligner.Length, FaceAligner.Length), 1)[0];
                return rows;
            }
            return Run(faces.AsMemory(0, count * FaceAligner.Length), count);
        }

        private float[][] Run(Memory<float> faces, int count)
        {
            var tensor = new DenseTensor<float>(faces, [count, 3, FaceAligner.Size, FaceAligner.Size]);
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)]);
            var flat = results.First().AsTensor<float>().ToArray();

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
