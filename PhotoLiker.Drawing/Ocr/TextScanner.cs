using System.Drawing;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>What the scan of a picture for text is like.</summary>
    public sealed record ScanSettings
    {
        /// <summary>The longer side of the picture the finder looks at; a larger picture is made smaller (a photo of 24 million points is far more than the finder needs, and takes a gigabyte of memory).</summary>
        public int FinderSide { get; init; } = 1536;

        /// <summary>A line that the reader is less sure of than this (0 to 1) is not kept.</summary>
        public float MinimumConfidence { get; init; } = 0.5f;

        /// <summary>The height a line of text is read at: what the reading model was trained on.</summary>
        public int LineHeight { get; init; } = 48;

        /// <summary>The longest a line of text is read at; a longer line is squeezed.</summary>
        public int MaximumLineWidth { get; init; } = 3200;

        /// <summary>The most lines read in one scan (the surest first): a picture full of leaves is not a book.</summary>
        public int MaximumLines { get; init; } = 300;

        public FinderSettings Finder { get; init; } = new();
    }

    /// <summary>
    /// Reads the text in a picture, with PaddleOCR's PP-OCR models on this computer (ONNX Runtime): one model finds the lines of text (a map of how likely each point of the picture is to be text,
    /// which <see cref="LineFinder"/> turns into boxes), another reads each line (<see cref="CharacterSet"/> turns its answer into letters). A line may be turned a little, as the text of a photo
    /// often is; text upside down, and text in many columns that the reading order does not follow, are not handled. Safe to call from one thread at a time; a scan holds the scanner until it is over.
    /// </summary>
    public sealed class TextScanner : IDisposable
    {
        private static readonly float[] FinderMean = [0.485f, 0.456f, 0.406f];
        private static readonly float[] FinderDeviation = [0.229f, 0.224f, 0.225f];
        private static readonly float[] ReaderMean = [0.5f, 0.5f, 0.5f];
        private static readonly float[] ReaderDeviation = [0.5f, 0.5f, 0.5f];

        private readonly InferenceSession _finder;
        private readonly InferenceSession _reader;
        private readonly string _finderInput;
        private readonly string _readerInput;
        private readonly CharacterSet _characters;
        private readonly ScanSettings _settings;
        private readonly object _gate = new();
        private bool _disposed;

        /// <exception cref="FileNotFoundException">A model's file is not there.</exception>
        public TextScanner(string finderPath, string readerPath, int threads = 0, ScanSettings? settings = null)
            : this(finderPath, readerPath, CharacterSet.EastSlavic, threads, settings)
        {
        }

        internal TextScanner(string finderPath, string readerPath, CharacterSet characters, int threads, ScanSettings? settings)
        {
            foreach (var path in new[] { finderPath, readerPath })
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("A model that reads text is not on this computer.", path);
            }

            _characters = characters;
            _settings = settings ?? new ScanSettings();
            _finder = Open(finderPath, threads);
            _reader = Open(readerPath, threads);
            _finderInput = _finder.InputMetadata.Keys.First();
            _readerInput = _reader.InputMetadata.Keys.First();
        }

        private static InferenceSession Open(string path, int threads)
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

        /// <summary>
        /// The text in the picture, or in <paramref name="area"/> of it (the area is cut to the picture). The lines come in the order they are read: from the top, a row of the picture from its left.
        /// </summary>
        /// <exception cref="OperationCanceledException">The scan was cancelled.</exception>
        /// <exception cref="ObjectDisposedException">The scanner was let go of.</exception>
        public ScanResult Scan(Bitmap32 picture, Rectangle? area = null, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(picture);

            var origin = Point.Empty;
            if (area is { } wanted)
            {
                var inside = Rectangle.Intersect(wanted, picture.Bounds);
                if (inside.Width < 1 || inside.Height < 1)
                    return new ScanResult([]);

                if (inside != picture.Bounds)
                {
                    picture = picture.Crop(inside);
                    origin = inside.Location;
                }
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var boxes = FindLines(picture, cancel);
                var lines = new List<TextLine>();
                foreach (var (box, _) in boxes)
                {
                    cancel.ThrowIfCancellationRequested();
                    var (text, confidence) = Read(picture, box);
                    if (text.Trim().Length == 0 || confidence < _settings.MinimumConfidence)
                        continue;

                    lines.Add(new TextLine(text.Trim(), confidence, box.Transformed(1, 1, origin.X, origin.Y)));
                }

                return new ScanResult(InReadingOrder(lines));
            }
        }

        /// <summary>The lines of text in the picture as boxes in its pixels, the surest first.</summary>
        private List<(TextRegion Box, float Score)> FindLines(Bitmap32 picture, CancellationToken cancel)
        {
            var scale = Math.Min(1f, (float)_settings.FinderSide / Math.Max(picture.Width, picture.Height));
            var width = Math.Max(32, (int)Math.Round(picture.Width * scale / 32f) * 32);
            var height = Math.Max(32, (int)Math.Round(picture.Height * scale / 32f) * 32);

            var input = Sampler.Resized(picture, width, height, FinderMean, FinderDeviation);
            cancel.ThrowIfCancellationRequested();

            float[] map;
            using (var results = _finder.Run([NamedOnnxValue.CreateFromTensor(_finderInput, new DenseTensor<float>(input, [1, 3, height, width]))]))
            {
                var output = results.First().AsTensor<float>();
                map = output.ToArray();
                if (map.Length != width * height)
                    throw new InvalidDataException($"The model gave {map.Length} numbers for a picture of {width} by {height}.");
            }

            cancel.ThrowIfCancellationRequested();
            var scaleX = (float)picture.Width / width;
            var scaleY = (float)picture.Height / height;
            return LineFinder.Find(map, width, height, _settings.Finder)
                .OrderByDescending(found => found.Score)
                .Take(_settings.MaximumLines)
                .Select(found => (found.Box.Transformed(scaleX, scaleY, 0, 0), found.Score))
                .ToList();
        }

        /// <summary>What one line of text says, and how sure the reader is.</summary>
        private (string Text, float Confidence) Read(Bitmap32 picture, TextRegion box)
        {
            var (planes, width) = Sampler.Line(picture, box, _settings.LineHeight, _settings.MaximumLineWidth, ReaderMean, ReaderDeviation);
            using var results = _reader.Run([NamedOnnxValue.CreateFromTensor(_readerInput, new DenseTensor<float>(planes, [1, 3, _settings.LineHeight, width]))]);
            var output = results.First().AsTensor<float>();
            var dimensions = output.Dimensions;
            if (dimensions.Length != 3 || dimensions[2] != _characters.Count)
                throw new InvalidDataException($"The reading model gives {string.Join(" by ", dimensions.ToArray())} answers; {_characters.Count} for each slice of a line were expected.");

            return _characters.Decode(output.ToArray(), dimensions[1]);
        }

        /// <summary>The lines as they are read: from the top; the lines that are on one row of the picture from its left.</summary>
        internal static List<TextLine> InReadingOrder(IEnumerable<TextLine> lines)
        {
            var rows = new List<List<TextLine>>();
            foreach (var line in lines.OrderBy(l => l.Box.Centre.Y))
            {
                var row = rows.Count > 0 ? rows[^1] : null;
                if (row is not null && Math.Abs(line.Box.Centre.Y - row.Average(l => l.Box.Centre.Y)) <= Math.Min(line.Box.Height, row.Min(l => l.Box.Height)) / 2f)
                    row.Add(line);
                else
                    rows.Add([line]);
            }

            return rows.SelectMany(row => row.OrderBy(l => l.Box.Centre.X)).ToList();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _finder.Dispose();
                _reader.Dispose();
            }
        }
    }
}
