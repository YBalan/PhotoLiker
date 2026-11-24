using System.Drawing;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;

namespace PhotoLiker.Manuscripts.Handwriting
{
    /// <summary>What reading a page of handwriting is like.</summary>
    public sealed record HandwritingSettings
    {
        /// <summary>The longer side of the page the finder of lines looks at (a photo of 24 million points is made smaller for it, not for the reading).</summary>
        public int FinderSide { get; init; } = 1536;

        /// <summary>
        /// How much the finder's strip of each line is made larger to get the line (see <see cref="FinderSettings.Unclip"/>). Handwriting is read from a strip that is taller than print's: the
        /// handwriting networks learned from boxes that hold a whole line with its tall and long letters, and a crop that is much taller than that, or much shorter, reads far worse
        /// (measured on the Rukopys photos of school work: adding a quarter of the height above and below doubled the errors). Between 1.6 and 2.2 the errors hardly change.
        /// </summary>
        public float Unclip { get; init; } = 2.0f;

        /// <summary>The most lines read on one page.</summary>
        public int MaximumLines { get; init; } = 300;

        /// <summary>The widest a line is read at (a line that is longer for its height is squeezed to it): the network reads a line in time that grows with its width.</summary>
        public int MaximumLineWidth { get; init; } = 8192;

        /// <summary>Crops whose height in the photo is more than this many times the height the network reads at are first averaged down, so that thin strokes are not lost in the sampling.</summary>
        public float ShrinkAbove { get; init; } = 2f;
    }

    /// <summary>
    /// Reads the handwriting of a page: the finder of lines of PhotoLiker.Drawing (the PP-OCR detection model, which gives a thin strip along each line of handwriting even on ruled and squared paper)
    /// finds the lines, each line is cut out straight at the height the handwriting network reads at, and the network (<see cref="HandwritingReader"/>) reads it. Lines that run into
    /// each other (two columns, a note in the margin on the same row) are read as they come and put in reading order; a page that is turned on its side or upside down is not read as it
    /// is, it has to be turned first. One thread at a time.
    /// </summary>
    public sealed class HandwritingScanner : IDisposable
    {
        private readonly TextFinder _finder;
        private readonly HandwritingReader _reader;
        private readonly HandwritingSettings _settings;
        private readonly FinderSettings _finderSettings;
        private readonly object _gate = new();
        private bool _disposed;

        /// <param name="finderPath">The PP-OCR detection model.</param>
        /// <param name="onnxPath">The handwriting network as an ONNX file (see <see cref="HandwritingReader.EnsureOnnx"/>).</param>
        /// <param name="allowCharacter">Whether the network may answer with a character (English is read with the Latin letters only); null: any it knows.</param>
        public HandwritingScanner(string finderPath, string onnxPath, int threads = 0, HandwritingSettings? settings = null, CrnnConfig? config = null, Func<string, bool>? allowCharacter = null)
        {
            _settings = settings ?? new HandwritingSettings();
            _finderSettings = new FinderSettings { Unclip = _settings.Unclip };
            _finder = new TextFinder(finderPath, threads);
            try
            {
                _reader = new HandwritingReader(onnxPath, config, threads, allowCharacter);
            }
            catch
            {
                _finder.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The text of the page, or of <paramref name="area"/> of it (cut to the page). A line the network can read nothing of has no text and is left out; the sureness of each line is in
        /// the result, so a front end can show the lines that need a look.
        /// </summary>
        /// <param name="progress">Told, from the thread that scans, how many lines are read of how many were found.</param>
        /// <exception cref="OperationCanceledException">The scan was cancelled.</exception>
        public ScanResult Scan(Bitmap32 page, Rectangle? area = null, CancellationToken cancel = default, IProgress<(int Done, int Total)>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(page);

            var origin = Point.Empty;
            if (area is { } wanted)
            {
                var inside = Rectangle.Intersect(wanted, page.Bounds);
                if (inside.Width < 1 || inside.Height < 1)
                    return new ScanResult([]);

                if (inside != page.Bounds)
                {
                    page = page.Crop(inside);
                    origin = inside.Location;
                }
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var found = _finder.Find(page, _settings.FinderSide, _finderSettings, _settings.MaximumLines, cancel);
                var boxes = found.Select(f => f.Box).ToList();

                // A photo in which a line is far taller than the network reads at is averaged down first: sampling it as it is would lose the thin strokes
                var source = page;
                var shrunk = 1;
                if (boxes.Count > 0)
                {
                    var typical = boxes.Select(b => b.Height).Order().ElementAt(boxes.Count / 2);
                    shrunk = (int)Math.Floor(typical / (_settings.ShrinkAbove * _reader.Config.ImageHeight / 2f));
                    if (shrunk >= 2)
                        source = Sampler.Shrink(page, shrunk);
                    else
                        shrunk = 1;
                }

                var lines = new List<TextLine>();
                var done = 0;
                foreach (var box in boxes)
                {
                    cancel.ThrowIfCancellationRequested();
                    var scaled = shrunk == 1 ? box : box.Transformed(1f / shrunk, 1f / shrunk, 0, 0);
                    var (text, confidence) = ReadLine(source, scaled);
                    progress?.Report((++done, boxes.Count));
                    if (text.Trim().Length == 0)
                        continue;

                    lines.Add(new TextLine(text.Trim(), confidence, box.Transformed(1, 1, origin.X, origin.Y)));
                }

                return new ScanResult(TextScanner.InReadingOrder(lines));
            }
        }

        /// <summary>One line, cut out straight (the sampler of the finder's boxes) as grey numbers: the weights of the luma, white 1 and black -1 as the network was trained on.</summary>
        private (string Text, float Confidence) ReadLine(Bitmap32 page, TextRegion box)
        {
            var height = _reader.Config.ImageHeight;
            var (planes, width) = Sampler.Line(page, box, height, _settings.MaximumLineWidth, [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f]);
            var plane = width * height;

            // The planes are blue, green and red, each already (value / 255 - 0.5) / 0.5, so the same weights give the grey of that
            var grey = new float[plane];
            for (var i = 0; i < plane; i++)
                grey[i] = 0.114f * planes[i] + 0.587f * planes[plane + i] + 0.299f * planes[2 * plane + i];

            return _reader.Read(grey, width);
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
