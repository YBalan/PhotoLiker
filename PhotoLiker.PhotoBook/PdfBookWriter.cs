using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PhotoLiker.PhotoBook
{
    /// <summary>
    /// Writes the book as a PDF: a page for each photo, the description above it, the photo as large as the page leaves room for, the description below it. The text is set by
    /// <see cref="TextLayout"/> and drawn in an embedded font, so Cyrillic and every other script the font has is shown (and can be copied) as it is. A description too long for a page goes on
    /// to the next page. The photos are JPEG files that are put in as they are (no second squeezing).
    /// </summary>
    public static class PdfBookWriter
    {
        /// <summary>The photo is given at least this share of the height of a page; a description that leaves less moves to a flow over more pages.</summary>
        private const double MinPhotoShare = 0.3;

        /// <summary>The room between the text and the photo, in points.</summary>
        private const double Gap = 8;

        /// <summary>A picture that is set in the text is drawn no taller than this share of the height of the text on a page.</summary>
        private const double MaxPictureShare = 0.7;

        /// <summary>The fonts tried, in this order, when the one asked for is not on this computer.</summary>
        private static readonly string[] FallbackFonts = ["Segoe UI", "Arial", "Calibri", "Tahoma", "Times New Roman"];

        static PdfBookWriter() => PdfFonts.Register(); // the fonts of the computer; the file carries the letters it uses

        /// <summary>
        /// Writes the file: a page for each photo of <paramref name="pages"/> (and one for the title before them, when the book has a title). The file is written under another name and moved into
        /// place, so a book is whole or not there. Returns the number of pages of the PDF.
        /// </summary>
        /// <param name="progress">Percent done, from any thread.</param>
        public static int Write(string path, BookOptions options, IReadOnlyList<BookPage> pages, IProgress<int>? progress = null, CancellationToken cancellation = default)
        {
            var family = ChooseFont(options.FontFamily);
            var fonts = new FontCache(family);
            using var measure = XGraphics.CreateMeasureContext(new XSize(20000, 20000), XGraphicsUnit.Point, XPageDirection.Downwards);
            var measurer = new PdfMeasurer(measure, fonts);

            var margin = Math.Clamp(options.MarginMillimetres, 0, 60) * PageSizes.PointsPerMillimetre;
            using var document = new PdfDocument();
            document.Info.Title = options.Title;
            document.Info.Author = options.Author;
            document.Info.Creator = "PhotoLiker";

            var pageNumber = 0;
            var images = new List<XImage>();
            using var pictures = new PictureCache(); // the pictures that are set in the text, made once each and let go of after the file is written
            try
            {
                if (!string.IsNullOrWhiteSpace(options.Title))
                    AddTitlePage(document, options, margin, measurer, fonts);

                for (var i = 0; i < pages.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var page = pages[i];
                    XImage? image = null;
                    if (page.HasImage)
                    {
                        image = XImage.FromFile(page.ImageFile);
                        images.Add(image);
                    }

                    pageNumber = AddPhotoPages(document, options, margin, measurer, fonts, pictures, page, image, pageNumber);
                    progress?.Report((i + 1) * 90 / pages.Count);
                }

                if (document.PageCount == 0)
                    document.AddPage(); // a PDF without a page is no PDF; the book had nothing in it

                var pageCount = document.PageCount; // a document that is saved cannot be asked anything
                var partial = path + ".part";
                try
                {
                    document.Save(partial);
                    File.Move(partial, path, overwrite: true);
                }
                catch
                {
                    try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    throw;
                }

                progress?.Report(100);
                return pageCount;
            }
            finally
            {
                foreach (var image in images)
                    image.Dispose();
            }
        }

        /// <summary>The font asked for, or the first of the usual ones that this computer has.</summary>
        private static string ChooseFont(string requested)
        {
            foreach (var family in new[] { requested }.Concat(FallbackFonts).Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                try
                {
                    _ = new XFont(family, 10, XFontStyleEx.Regular);
                    return family;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotImplementedException or FileNotFoundException)
                {
                    // not on this computer: the next one
                }
            }

            throw new InvalidOperationException(PhotoBookStrings.NoFont);
        }

        private static void AddTitlePage(PdfDocument document, BookOptions options, double margin, PdfMeasurer measurer, FontCache fonts)
        {
            var (width, height) = PageSizes.Points(options.Format, options.Orientation == PageOrientation.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait, photoIsPortrait: true);
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);

            List<RichParagraph> paragraphs = [new(TextAlign.Center, [new RichRun(options.Title.Trim(), new RichStyle(Bold: true, Size: 30))])];
            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                paragraphs.Add(new RichParagraph(TextAlign.Center, []));
                paragraphs.Add(new RichParagraph(TextAlign.Center, [new RichRun(options.Author.Trim(), new RichStyle(Size: 16, Color: 0x606060))]));
            }

            var contentWidth = Math.Max(40, width - 2 * margin);
            var lines = TextLayout.Layout(new RichDocument(paragraphs), contentWidth, measurer);
            using var graphics = XGraphics.FromPdfPage(page);
            DrawLines(graphics, fonts, null, lines, margin, Math.Max(margin, height * 0.35 - TextLayout.Height(lines) / 2));
        }

        /// <summary>Adds the page of a photo (more than one if its text is long); returns the number of the last page.</summary>
        private static int AddPhotoPages(
            PdfDocument document, BookOptions options, double margin, PdfMeasurer measurer, FontCache fonts, PictureCache pictures, BookPage book, XImage? image, int pageNumber)
        {
            var (pageWidth, pageHeight) = PageSizes.Points(options.Format, options.Orientation, photoIsPortrait: !book.HasImage || book.ImageHeight > book.ImageWidth);
            var contentWidth = Math.Max(40, pageWidth - 2 * margin);
            var contentHeight = Math.Max(40, pageHeight - 2 * margin);

            // A picture in the text is drawn no taller than most of a page
            var above = TextLayout.Layout(book.Above, contentWidth, measurer, contentHeight * MaxPictureShare);
            var below = TextLayout.Layout(book.Below, contentWidth, measurer, contentHeight * MaxPictureShare);
            var aboveHeight = TextLayout.Height(above);
            var belowHeight = TextLayout.Height(below);
            var gapAbove = above.Count > 0 ? Gap : 0;
            var gapBelow = below.Count > 0 ? Gap : 0;

            var ratio = book.ImageWidth > 0 && book.ImageHeight > 0 ? (double)book.ImageHeight / book.ImageWidth : 0.75;
            var room = contentHeight - aboveHeight - belowHeight - gapAbove - gapBelow;

            PdfPage NewPage()
            {
                var created = document.AddPage();
                created.Width = XUnit.FromPoint(pageWidth);
                created.Height = XUnit.FromPoint(pageHeight);
                pageNumber++;
                return created;
            }

            if (image is null)
            {
                // A page that is only text: it flows from page to page as far as it goes
                var textFlow = new Flow(NewPage, document, options, fonts, pictures, pageWidth, pageHeight, margin);
                textFlow.Start();
                foreach (var line in above)
                    textFlow.Line(line);
                foreach (var line in below)
                    textFlow.Line(line);

                textFlow.Finish();
                return pageNumber;
            }

            if (room >= contentHeight * MinPhotoShare)
            {
                // Everything on one page: the photo is the largest that fits the width and what the text leaves of the height, and the whole is in the middle of the page
                var scale = Math.Min(contentWidth, room / ratio);
                var photoWidth = scale;
                var photoHeight = scale * ratio;
                var blockHeight = aboveHeight + gapAbove + photoHeight + gapBelow + belowHeight;

                var page = NewPage();
                using var graphics = XGraphics.FromPdfPage(page);
                var top = margin + (contentHeight - blockHeight) / 2;
                DrawLines(graphics, fonts, pictures, above, margin, top);
                graphics.DrawImage(image, margin + (contentWidth - photoWidth) / 2, top + aboveHeight + gapAbove, photoWidth, photoHeight);
                DrawLines(graphics, fonts, pictures, below, margin, top + aboveHeight + gapAbove + photoHeight + gapBelow);
                DrawPageNumber(graphics, options, fonts, pageWidth, pageHeight, margin, pageNumber);
                return pageNumber;
            }

            // The text is too long to leave the photo room on the page: it all flows on from page to page, the photo (at most a share of a page) where it comes
            var flow = new Flow(NewPage, document, options, fonts, pictures, pageWidth, pageHeight, margin);
            flow.Start();
            foreach (var line in above)
                flow.Line(line);

            var flowWidth = Math.Min(contentWidth, contentHeight * 0.6 / ratio);
            var flowHeight = flowWidth * ratio;
            flow.Image(image, flowWidth, flowHeight, margin + (contentWidth - flowWidth) / 2);
            foreach (var line in below)
                flow.Line(line);

            flow.Finish();
            return pageNumber;
        }

        /// <summary>The pictures of the text as images of the PDF: one for each, made when it is first drawn (from the bytes of its file) and let go of with the cache.</summary>
        private sealed class PictureCache : IDisposable
        {
            private readonly Dictionary<RichPicture, (XImage Image, MemoryStream Stream)> _images = new(ReferenceEqualityComparer.Instance);

            public XImage Get(RichPicture picture)
            {
                if (_images.TryGetValue(picture, out var made))
                    return made.Image;

                var stream = new MemoryStream(picture.Data, writable: false);
                var image = XImage.FromStream(stream);
                _images[picture] = (image, stream);
                return image;
            }

            public void Dispose()
            {
                foreach (var (image, stream) in _images.Values)
                {
                    image.Dispose();
                    stream.Dispose();
                }

                _images.Clear();
            }
        }

        /// <summary>Lays things one under the other, from page to page: a thing that does not fit what is left of a page starts the next.</summary>
        private sealed class Flow(Func<PdfPage> newPage, PdfDocument document, BookOptions options, FontCache fonts, PictureCache pictures, double pageWidth, double pageHeight, double margin)
        {
            private XGraphics? _graphics;
            private double _y;
            private int _number;

            public void Start() => Begin();

            private void Begin()
            {
                _graphics?.Dispose();
                var page = newPage();
                _graphics = XGraphics.FromPdfPage(page);
                _y = margin;
                _number = document.PageCount - (!string.IsNullOrWhiteSpace(options.Title) ? 1 : 0);
            }

            private void Ensure(double height)
            {
                if (_y + height > pageHeight - margin + 0.01 && _y > margin + 0.01)
                {
                    Close();
                    Begin();
                }
            }

            private void Close()
            {
                if (_graphics is null)
                    return;

                DrawPageNumber(_graphics, options, fonts, pageWidth, pageHeight, margin, _number);
                _graphics.Dispose();
                _graphics = null;
            }

            public void Line(TextLine line)
            {
                Ensure(line.Height);
                DrawLines(_graphics!, fonts, pictures, [line], margin, _y);
                _y += line.Height;
            }

            public void Image(XImage image, double width, double height, double x)
            {
                _y += Gap;
                Ensure(height);
                _graphics!.DrawImage(image, x, _y, width, height);
                _y += height + Gap;
            }

            public void Finish() => Close();
        }

        private static void DrawLines(XGraphics graphics, FontCache fonts, PictureCache? pictures, IReadOnlyList<TextLine> lines, double left, double top)
        {
            var y = top;
            foreach (var line in lines)
            {
                if (line.Picture is { } picture && pictures is not null)
                    graphics.DrawImage(pictures.Get(picture), left + line.PictureX, y + line.PictureTop, line.PictureWidth, line.PictureHeight);

                foreach (var piece in line.Pieces)
                {
                    var brush = new XSolidBrush(piece.Style.Color is { } color
                        ? XColor.FromArgb((color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF)
                        : XColors.Black);
                    graphics.DrawString(piece.Text, fonts.Get(piece.Style), brush, new XPoint(left + piece.X, y + line.Baseline), XStringFormats.BaseLineLeft);
                }

                y += line.Height;
            }
        }

        private static void DrawPageNumber(XGraphics graphics, BookOptions options, FontCache fonts, double pageWidth, double pageHeight, double margin, int number)
        {
            if (!options.PageNumbers || number <= 0)
                return;

            var style = new RichStyle(Size: 9, Color: 0x808080);
            var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var baseline = pageHeight - Math.Max(10, margin / 2 - 2);
            graphics.DrawString(text, fonts.Get(style), new XSolidBrush(XColor.FromArgb(128, 128, 128)), new XPoint(pageWidth / 2, baseline), XStringFormats.BaseLineCenter);
        }

        /// <summary>The fonts of the book: one of a family for each look (bold, italic, underline, strike-through) and size, made once.</summary>
        private sealed class FontCache(string family)
        {
            private readonly Dictionary<(bool, bool, bool, bool, double), XFont> _fonts = [];

            public XFont Get(RichStyle style)
            {
                var key = (style.Bold, style.Italic, style.Underline, style.Strike, style.PointSize);
                if (_fonts.TryGetValue(key, out var font))
                    return font;

                var look = XFontStyleEx.Regular;
                if (style.Bold)
                    look |= XFontStyleEx.Bold;
                if (style.Italic)
                    look |= XFontStyleEx.Italic;
                if (style.Underline)
                    look |= XFontStyleEx.Underline;
                if (style.Strike)
                    look |= XFontStyleEx.Strikeout;

                return _fonts[key] = new XFont(family, style.PointSize, look);
            }
        }

        private sealed class PdfMeasurer(XGraphics graphics, FontCache fonts) : ITextMeasurer
        {
            public double Width(string text, RichStyle style) => text.Length == 0 ? 0 : graphics.MeasureString(text, fonts.Get(style), XStringFormats.Default).Width;
        }
    }
}
