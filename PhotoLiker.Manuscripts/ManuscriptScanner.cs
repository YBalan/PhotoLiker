using System.Drawing;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Manuscripts.Handwriting;

namespace PhotoLiker.Manuscripts
{
    /// <summary>
    /// Reads the text of a page in the language and the kind of writing it was told: a page of handwriting with the handwriting network of its language (<see cref="HandwritingScanner"/>), a page of
    /// print with the PP-OCR reader of PhotoLiker.Drawing, which knows Ukrainian, Russian and English and is held to the language that was asked for (<see cref="ScanLanguages.Allows"/>).
    /// One thread at a time; a scanner that is let go of cannot be used again.
    /// </summary>
    public sealed class ManuscriptScanner : IDisposable
    {
        private readonly Func<Bitmap32, Rectangle?, CancellationToken, IProgress<(int Done, int Total)>?, ScanResult> _scan;
        private readonly IDisposable _owned;

        private ManuscriptScanner(ScanKind kind, ScanLanguage language, Func<Bitmap32, Rectangle?, CancellationToken, IProgress<(int, int)>?, ScanResult> scan, IDisposable owned)
        {
            Kind = kind;
            Language = language;
            _scan = scan;
            _owned = owned;
        }

        public ScanKind Kind { get; }

        public ScanLanguage Language { get; }

        /// <summary>
        /// A scanner of handwriting: the PP-OCR finder of lines and the checkpoint of the handwriting network (it is made into an ONNX file next to it the first time): the one of Russian for Russian,
        /// the Ukrainian one (see <see cref="ScanLanguages.HandwritingModelOf"/>) for the rest. English is held to the Latin letters it knows; the other languages are not held to any.
        /// </summary>
        /// <exception cref="ArgumentException">There is no handwriting for the language.</exception>
        /// <exception cref="InvalidDataException">The checkpoint is not a network this program knows.</exception>
        public static ManuscriptScanner Handwriting(string finderPath, string checkpointPath, ScanLanguage language, int threads = 0, HandwritingSettings? settings = null)
        {
            if (!ScanLanguages.IsSupported(ScanKind.Handwriting, language))
                throw new ArgumentException($"Handwriting is not read in {language}.", nameof(language));

            var onnx = HandwritingReader.EnsureOnnx(checkpointPath);
            Func<string, bool>? allow = language == ScanLanguage.English ? character => ScanLanguages.Allows(ScanLanguage.English, character) : null;
            var scanner = new HandwritingScanner(finderPath, onnx, threads, settings, allowCharacter: allow);
            return new ManuscriptScanner(ScanKind.Handwriting, language, (page, area, cancel, progress) => Normalized(scanner.Scan(page, area, cancel, progress), language), scanner);
        }

        /// <summary>A scanner of print: the PP-OCR finder of lines and the PP-OCR East Slavic reader, held to the language.</summary>
        public static ManuscriptScanner Printed(string finderPath, string readerPath, ScanLanguage language, int threads = 0)
        {
            var scanner = new TextScanner(finderPath, readerPath, threads, new ScanSettings { AllowCharacter = character => ScanLanguages.Allows(language, character) });
            return new ManuscriptScanner(ScanKind.Printed, language, (page, area, cancel, progress) => Normalized(scanner.Scan(page, area, cancel, progress), language), scanner);
        }

        /// <summary>The lines with the Latin letters that only look like Cyrillic ones put right (see <see cref="ScanLanguages.Normalize"/>).</summary>
        internal static ScanResult Normalized(ScanResult result, ScanLanguage language) =>
            language == ScanLanguage.English ? result : new ScanResult([.. result.Lines.Select(line => line with { Text = ScanLanguages.Normalize(language, line.Text) })]);

        /// <summary>The text of the page, or of <paramref name="area"/> of it, with the lines it was read from.</summary>
        /// <param name="progress">Told, from the thread that scans, how many lines are read of how many were found.</param>
        public ScanResult Scan(Bitmap32 page, Rectangle? area = null, CancellationToken cancel = default, IProgress<(int Done, int Total)>? progress = null) =>
            _scan(page, area, cancel, progress);

        public void Dispose() => _owned.Dispose();
    }
}
