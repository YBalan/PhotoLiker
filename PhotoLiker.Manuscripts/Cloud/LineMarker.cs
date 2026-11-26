using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>
    /// Finds the lines of a page on this computer, to mark them on the photo that goes to an AI service: the service reads well but does not say where, and its guesses of where a line is are
    /// too rough to draw (measured: a page of ten lines came back with the boxes squeezed into its upper half), while the finder of lines (the PP-OCR detection model, the one a scan uses) puts a box
    /// on each line to within a few points. The service is sent the photo with the boxes numbered and says what is in each box. Nothing is read here, and the model is small.
    /// One thread at a time.
    /// </summary>
    public sealed class LineMarker : IDisposable
    {
        /// <summary>The most boxes that are marked on a page: a page with more is a table, a grid or a ruled sheet, which the service could not tell the boxes of apart.</summary>
        public const int MaximumLines = 120;

        /// <summary>The longer side of the page the finder looks at.</summary>
        private const int FinderSide = 1536;

        private readonly TextFinder _finder;

        /// <param name="finderPath">The PP-OCR detection model.</param>
        /// <exception cref="FileNotFoundException">The model is not on this computer.</exception>
        public LineMarker(string finderPath, int threads = 0) => _finder = new TextFinder(finderPath, threads);

        /// <summary>The lines of the page as boxes in its pixels, in reading order: from the top, the lines that are on one row from the left. A ruled line, a margin or a stain may be taken for a line.</summary>
        public IReadOnlyList<TextRegion> Find(Bitmap32 page, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            var found = _finder.Find(page, FinderSide, new FinderSettings(), MaximumLines, cancel);
            return [.. TextScanner.InReadingOrder(found.Select(line => new TextLine(string.Empty, line.Score, line.Box))).Select(line => line.Box)];
        }

        public void Dispose() => _finder.Dispose();
    }
}
