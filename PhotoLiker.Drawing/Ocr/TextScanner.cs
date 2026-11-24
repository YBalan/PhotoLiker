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

        /// <summary>
        /// Which characters the reader may answer with (it is asked about each of its characters, as written: a letter, a digit, a mark); null for all of them. A language of the text is
        /// a limit like this: Ukrainian text has no "ы" and English text no Cyrillic, so the reader cannot choose them, whatever the writing looks like.
        /// </summary>
        public Func<string, bool>? AllowCharacter { get; init; }
    }

    /// <summary>
    /// Reads the text in a picture, with PaddleOCR's PP-OCR models on this computer (ONNX Runtime): one model finds the lines of text (a map of how likely each point of the picture is to be text,
    /// which <see cref="LineFinder"/> turns into boxes), another reads each line (<see cref="CharacterSet"/> turns its answer into letters). A line may be turned a little, as the text of a photo
    /// often is; text upside down, and text in many columns that the reading order does not follow, are not handled. Safe to call from one thread at a time; a scan holds the scanner until it is over.
    /// </summary>
    public sealed class TextScanner : IDisposable
    {
        private static readonly float[] ReaderMean = [0.5f, 0.5f, 0.5f];
        private static readonly float[] ReaderDeviation = [0.5f, 0.5f, 0.5f];

        private readonly TextFinder _finder;
        private readonly InferenceSession _reader;
        private readonly string _readerInput;
        private readonly bool[]? _allowed;
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
            // The finder checks its own file; the first model that is not there is the one that is named
            if (!File.Exists(finderPath))
                throw new FileNotFoundException("A model that reads text is not on this computer.", finderPath);
            if (!File.Exists(readerPath))
                throw new FileNotFoundException("A model that reads text is not on this computer.", readerPath);

            _characters = characters;
            _settings = settings ?? new ScanSettings();
            _allowed = _settings.AllowCharacter is { } allow ? characters.Allowing(allow) : null;
            _finder = new TextFinder(finderPath, threads);
            _reader = OnnxSessions.Open(readerPath, threads);
            _readerInput = _reader.InputMetadata.Keys.First();
        }

        /// <summary>
        /// The text in the picture, or in <paramref name="area"/> of it (the area is cut to the picture). The lines come in the order they are read: from the top, a row of the picture from its left.
        /// </summary>
        /// <exception cref="OperationCanceledException">The scan was cancelled.</exception>
        /// <exception cref="ObjectDisposedException">The scanner was let go of.</exception>
        /// <param name="progress">Told, from the thread that scans, how many lines are read of how many were found.</param>
        public ScanResult Scan(Bitmap32 picture, Rectangle? area = null, CancellationToken cancel = default, IProgress<(int Done, int Total)>? progress = null)
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

                var boxes = _finder.Find(picture, _settings.FinderSide, _settings.Finder, _settings.MaximumLines, cancel);
                var lines = new List<TextLine>();
                var done = 0;
                foreach (var (box, _) in boxes)
                {
                    cancel.ThrowIfCancellationRequested();
                    var (text, confidence) = Read(picture, box);
                    progress?.Report((++done, boxes.Count));
                    if (text.Trim().Length == 0 || confidence < _settings.MinimumConfidence)
                        continue;

                    lines.Add(new TextLine(text.Trim(), confidence, box.Transformed(1, 1, origin.X, origin.Y)));
                }

                return new ScanResult(InReadingOrder(lines));
            }
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

            return _characters.Decode(output.ToArray(), dimensions[1], _allowed);
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
