using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>
    /// The model that finds the lines of text in a picture (PP-OCR text detection, ONNX Runtime): it gives, for each point of the picture, how likely it is part of a line of text, and
    /// <see cref="LineFinder"/> turns that into boxes. Reading print (<see cref="TextScanner"/>) and reading handwriting (the handwriting scanner of PhotoLiker.Manuscripts) both find their lines with it.
    /// Safe to call from one thread at a time.
    /// </summary>
    public sealed class TextFinder : IDisposable
    {
        private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
        private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly object _gate = new();
        private bool _disposed;

        /// <exception cref="FileNotFoundException">The model's file is not there.</exception>
        public TextFinder(string modelPath, int threads = 0)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException("A model that reads text is not on this computer.", modelPath);

            _session = OnnxSessions.Open(modelPath, threads);
            _input = _session.InputMetadata.Keys.First();
        }

        /// <summary>
        /// The lines of text in the picture as boxes in its pixels, the surest first. The picture is looked at no larger than <paramref name="side"/> on its longer side (a photo of 24 million points
        /// is far more than the model needs, and takes a gigabyte of memory).
        /// </summary>
        /// <exception cref="OperationCanceledException">The search was cancelled.</exception>
        public List<(TextRegion Box, float Score)> Find(Bitmap32 picture, int side, FinderSettings settings, int maximumLines, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(picture);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var scale = Math.Min(1f, (float)side / Math.Max(picture.Width, picture.Height));
                var width = Math.Max(32, (int)Math.Round(picture.Width * scale / 32f) * 32);
                var height = Math.Max(32, (int)Math.Round(picture.Height * scale / 32f) * 32);

                var input = Sampler.Resized(picture, width, height, Mean, Deviation);
                cancel.ThrowIfCancellationRequested();

                float[] map;
                using (var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, new DenseTensor<float>(input, [1, 3, height, width]))]))
                {
                    var output = results.First().AsTensor<float>();
                    map = output.ToArray();
                    if (map.Length != width * height)
                        throw new InvalidDataException($"The model gave {map.Length} numbers for a picture of {width} by {height}.");
                }

                cancel.ThrowIfCancellationRequested();

                // The picture was made the size of the model's grid, a little differently in the two directions: the boxes are put back with each direction's own factor
                var scaleX = (float)picture.Width / width;
                var scaleY = (float)picture.Height / height;
                return LineFinder.Find(map, width, height, settings)
                    .OrderByDescending(found => found.Score)
                    .Take(maximumLines)
                    .Select(found => (found.Box.Transformed(scaleX, scaleY, 0, 0), found.Score))
                    .ToList();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _session.Dispose();
            }
        }
    }

    /// <summary>The way the models of PhotoLiker.Drawing are opened.</summary>
    internal static class OnnxSessions
    {
        public static InferenceSession Open(string path, int threads)
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            if (threads > 0)
                options.IntraOpNumThreads = threads;
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0"); // the program has other things to do with the processor

            // No memory arena: with it a session keeps for good what its busiest moment used, and the window that asked for the scan gives the memory back to nobody until the program ends
            options.EnableCpuMemArena = false;
            options.EnableMemoryPattern = false;
            return new InferenceSession(path, options);
        }
    }
}
