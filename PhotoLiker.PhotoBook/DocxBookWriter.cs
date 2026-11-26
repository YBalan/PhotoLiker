using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PhotoLiker.PhotoBook
{
    /// <summary>
    /// Writes the book as a Word document (a DOCX file, which is a zip of XML parts): a section for each photo, so that a page can have the shape of its photo as in the PDF, with the description above
    /// it, the photo and the description below, and one for the title before them. The text keeps what the Rich Text Box had (bold, italic, underline, strike-through, size, colour, alignment, indents,
    /// tab stops, pictures that stand in the text), the paper, the margins, the font and the page numbers are those of the dialog. Word sets the text itself, so what is only an estimate here is how much
    /// room a photo is given on a page that also has text. Nothing but the Framework is used: the parts are written as the standard says, one by one.
    /// </summary>
    public static class DocxBookWriter
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

        private const string RelationshipBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

        /// <summary>The room between the text and the photo, in points (as in the PDF).</summary>
        private const double Gap = 8;

        /// <summary>
        /// The photo is given at least this share of the height of a page; text that leaves less is taken to flow over more pages, and the photo is then set after it at most 60 % of a page high. It is
        /// less than in the PDF (0.3): Word moves a photo that does not fit to the next page, and a small photo under the text on its page is better than a large one alone on the next.
        /// </summary>
        private const double MinPhotoShare = 0.2;

        /// <summary>A picture that stands in the text is made no taller than this share of the height of the text on a page (as in the PDF).</summary>
        private const double MaxPictureShare = 0.7;

        /// <summary>English Word sets a photo alone on a page this much short of the height of the text area, in points: a line of the paragraph that holds it is a little taller than the picture.</summary>
        private const double AloneRoom = 14;

        private const double EmusPerPoint = 12700;

        /// <summary>
        /// Word sets a line as tall as its font is (about 1.33 times the size of the letters for the fonts of the book) and the layout of the PDF 1.25 times: the lines of the text are taken to be this much
        /// taller than the layout says when the room that is left for a photo is worked out (measured against Word: with this a page of text and a photo comes out on one page).
        /// </summary>
        private const double WordLineFactor = 1.07;

        /// <summary>
        /// Writes the file: a section for each page of <paramref name="pages"/> (and one for the title before them, when the book has a title). It is written under another name and moved into place, so
        /// a book is whole or not there. Returns the number of sections, which are the pages as the book counts them (Word sets the length of a long text itself).
        /// </summary>
        /// <param name="progress">Percent done, from any thread.</param>
        public static int Write(string path, BookOptions options, IReadOnlyList<BookPage> pages, IProgress<int>? progress = null, CancellationToken cancellation = default)
        {
            var partial = path + ".part";
            try
            {
                int count;
                using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    count = WriteArchive(zip, options, pages, progress, cancellation);

                File.Move(partial, path, overwrite: true);
                progress?.Report(100);
                return count;
            }
            catch
            {
                try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        /// <summary>A picture that goes into the file: where it is in the zip, the id the document calls it by, and where its bytes are.</summary>
        private sealed record Media(string Id, string Name, string? File, byte[]? Data);

        private sealed class State(BookOptions options)
        {
            public BookOptions Options { get; } = options;

            public List<Media> Media { get; } = [];

            public int DrawingIds { get; set; }

            public bool Footer => Options.PageNumbers;
        }

        private static int WriteArchive(ZipArchive zip, BookOptions options, IReadOnlyList<BookPage> pages, IProgress<int>? progress, CancellationToken cancellation)
        {
            var state = new State(options);
            var margin = Math.Clamp(options.MarginMillimetres, 0, 60) * PageSizes.PointsPerMillimetre;
            var hasTitle = !string.IsNullOrWhiteSpace(options.Title);
            var measurer = new EstimatingMeasurer();

            // Each section of the book as its paragraphs and the shape of its paper; what is written of them is made when the section is the last of its kind to be known
            var sections = new List<(List<XElement> Content, double Width, double Height, bool Numbered, bool FirstNumbered)>();
            if (hasTitle)
            {
                var (width, height) = PageSizes.Points(options.Format, options.Orientation == PageOrientation.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait, photoIsPortrait: true);
                sections.Add((TitleSection(options, height), width, height, Numbered: false, FirstNumbered: false));
            }

            for (var i = 0; i < pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var page = pages[i];
                var (width, height) = PageSizes.Points(options.Format, options.Orientation, photoIsPortrait: !page.HasImage || page.ImageHeight > page.ImageWidth);
                sections.Add((PageSection(state, page, i + 1, Math.Max(40, width - 2 * margin), Math.Max(40, height - 2 * margin), measurer), width, height, Numbered: true, FirstNumbered: i == 0));
                progress?.Report((i + 1) * 85 / Math.Max(1, pages.Count));
            }

            if (sections.Count == 0)
            {
                // A book with nothing in it: a document is not a document without a section, as a PDF is not without a page
                var (emptyWidth, emptyHeight) = PageSizes.Points(options.Format, options.Orientation == PageOrientation.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait, photoIsPortrait: true);
                sections.Add(([], emptyWidth, emptyHeight, Numbered: false, FirstNumbered: false));
            }

            // The body: the sections one after another; the properties of a section go in its last paragraph, those of the last section at the end of the body
            var body = new XElement(W + "body");
            for (var s = 0; s < sections.Count; s++)
            {
                var (content, width, height, numbered, firstNumbered) = sections[s];
                if (content.Count == 0)
                    content.Add(new XElement(W + "p"));

                var properties = SectionProperties(state, width, height, margin, numbered, firstNumbered);
                if (s < sections.Count - 1)
                {
                    var last = content[^1];
                    var paragraphProperties = last.Element(W + "pPr");
                    if (paragraphProperties is null)
                    {
                        paragraphProperties = new XElement(W + "pPr");
                        last.AddFirst(paragraphProperties);
                    }

                    paragraphProperties.Add(properties);
                    body.Add(content);
                }
                else
                {
                    body.Add(content);
                    body.Add(properties);
                }
            }

            var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R), new XAttribute(XNamespace.Xmlns + "wp", Wp),
                new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "pic", Pic), body));

            // The parts: the one that lists them first, then the document and what it uses
            AddXml(zip, "[Content_Types].xml", ContentTypesPart(state));
            AddXml(zip, "_rels/.rels", Relationships(
                ("rId1", RelationshipBase + "officeDocument", "word/document.xml"),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")));
            AddXml(zip, "docProps/core.xml", CoreProperties(options));
            AddXml(zip, "word/document.xml", document);
            AddXml(zip, "word/styles.xml", Styles(options));
            AddXml(zip, "word/settings.xml", Settings());

            var relationships = new List<(string Id, string Type, string Target)>
            {
                ("rIdStyles", RelationshipBase + "styles", "styles.xml"),
                ("rIdSettings", RelationshipBase + "settings", "settings.xml"),
            };
            if (state.Footer)
            {
                AddXml(zip, "word/footer1.xml", FooterPart());
                relationships.Add(("rIdFooter", RelationshipBase + "footer", "footer1.xml"));
            }

            for (var m = 0; m < state.Media.Count; m++)
            {
                cancellation.ThrowIfCancellationRequested();
                var media = state.Media[m];
                var entry = zip.CreateEntry("word/" + media.Name, CompressionLevel.NoCompression); // a JPEG or a PNG is already squeezed
                using (var target = entry.Open())
                {
                    if (media.Data is { } data)
                    {
                        target.Write(data, 0, data.Length);
                    }
                    else
                    {
                        using var source = File.OpenRead(media.File!);
                        source.CopyTo(target);
                    }
                }

                relationships.Add((media.Id, RelationshipBase + "image", media.Name));
                progress?.Report(85 + (m + 1) * 14 / state.Media.Count);
            }

            AddXml(zip, "word/_rels/document.xml.rels", Relationships([.. relationships]));
            return sections.Count;
        }

        // ── The sections ────────────────────────────────────────────────────

        /// <summary>The title page: the title large and in the middle, the author under it, about a third of the way down the page (as in the PDF).</summary>
        private static List<XElement> TitleSection(BookOptions options, double pageHeight)
        {
            var content = new List<XElement>
            {
                new(W + "p",
                    new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "before", Twips(Math.Max(0, pageHeight * 0.35 - 40))), new XAttribute(W + "after", "0")), new XElement(W + "jc", new XAttribute(W + "val", "center"))),
                    Run(options.Title.Trim(), new RichStyle(Bold: true, Size: 30))),
            };

            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                content.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "center")))));
                content.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "jc", new XAttribute(W + "val", "center"))), Run(options.Author.Trim(), new RichStyle(Size: 16, Color: 0x606060))));
            }

            return content;
        }

        /// <summary>The section of a page: the description above, the photo as large as the page leaves room for, the description below. A page that is only text is the paragraphs of its text.</summary>
        private static List<XElement> PageSection(State state, BookPage page, int number, double contentWidth, double contentHeight, ITextMeasurer measurer)
        {
            var above = page.Above.WithoutTrailingBlank();
            var below = page.Below.WithoutTrailingBlank();
            var content = new List<XElement>();
            foreach (var paragraph in above.Paragraphs)
                content.Add(Paragraph(state, paragraph, contentWidth, contentHeight, page.Description));

            if (page.HasImage)
            {
                var aboveLines = TextLayout.Layout(page.Above, contentWidth, measurer, contentHeight * MaxPictureShare);
                var belowLines = TextLayout.Layout(page.Below, contentWidth, measurer, contentHeight * MaxPictureShare);
                var hasText = aboveLines.Count + belowLines.Count > 0;
                var ratio = page.ImageWidth > 0 && page.ImageHeight > 0 ? (double)page.ImageHeight / page.ImageWidth : 0.75;

                // How wide the photo is: on a page of its own the largest that fits; with text, what the text leaves (an estimate: Word sets the lines), and not less than a share of the page
                double photoWidth;
                if (!hasText)
                {
                    photoWidth = Math.Min(contentWidth, (contentHeight - AloneRoom) / ratio);
                }
                else
                {
                    var gaps = (aboveLines.Count > 0 ? Gap : 0) + (belowLines.Count > 0 ? Gap : 0) + 2 * Gap; // the room of the photo itself is in it
                    var room = contentHeight - TextHeight(aboveLines) - TextHeight(belowLines) - gaps - 0.025 * contentHeight - 10;
                    photoWidth = room >= contentHeight * MinPhotoShare ? Math.Min(contentWidth, room / ratio) : Math.Min(contentWidth, contentHeight * 0.6 / ratio);
                }

                photoWidth = Math.Max(10, photoWidth);
                var photo = AddMedia(state, $"media/photo{number:D4}.jpg", page.ImageFile, null);
                content.Add(PicturePart(state, photo, photoWidth, photoWidth * ratio, "center", hasText ? Gap : 0, string.IsNullOrWhiteSpace(page.Description) ? $"Photo {number}" : page.Description, aloneOnPage: !hasText));
            }

            foreach (var paragraph in below.Paragraphs)
                content.Add(Paragraph(state, paragraph, contentWidth, contentHeight, page.Description));

            return content;
        }

        /// <summary>How tall the lines of a text are in Word, roughly: the layout's height, the lines of text a little taller (see <see cref="WordLineFactor"/>), the pictures as they are.</summary>
        private static double TextHeight(IReadOnlyList<TextLine> lines) => lines.Sum(line => line.Picture is null ? line.Height * WordLineFactor : line.Height);

        /// <summary>The properties of a section: the paper, the margins, the number of the page in the foot, and the number the first photo is.</summary>
        private static XElement SectionProperties(State state, double width, double height, double margin, bool numbered, bool firstNumbered)
        {
            var properties = new XElement(W + "sectPr");
            if (numbered && state.Footer)
                properties.Add(new XElement(W + "footerReference", new XAttribute(W + "type", "default"), new XAttribute(R + "id", "rIdFooter")));

            var size = new XElement(W + "pgSz", new XAttribute(W + "w", Twips(width)), new XAttribute(W + "h", Twips(height)));
            if (width > height + 0.5)
                size.Add(new XAttribute(W + "orient", "landscape"));
            properties.Add(size);

            var footer = Math.Max(10, margin / 2 - 2);
            properties.Add(new XElement(W + "pgMar", new XAttribute(W + "top", Twips(margin)), new XAttribute(W + "right", Twips(margin)), new XAttribute(W + "bottom", Twips(margin)), new XAttribute(W + "left", Twips(margin)),
                new XAttribute(W + "header", Twips(Math.Min(margin, 36))), new XAttribute(W + "footer", Twips(footer)), new XAttribute(W + "gutter", "0")));

            if (firstNumbered)
                properties.Add(new XElement(W + "pgNumType", new XAttribute(W + "start", "1"))); // the title page is not counted

            return properties;
        }

        // ── Paragraphs and runs ─────────────────────────────────────────────

        /// <summary>A paragraph of a description: its alignment, indents and tab stops (kept in the share of the column they were set in), its runs; a picture in the text as a paragraph that holds it.</summary>
        private static XElement Paragraph(State state, RichParagraph paragraph, double contentWidth, double contentHeight, string description)
        {
            if (paragraph.Picture is { } picture)
            {
                var drawnWidth = Math.Clamp(contentWidth * picture.WidthShare, 1, Math.Max(1, contentWidth));
                var drawnHeight = drawnWidth * picture.Ratio;
                var limit = contentHeight * MaxPictureShare;
                if (drawnHeight > limit)
                {
                    drawnHeight = Math.Max(1, limit);
                    drawnWidth = drawnHeight / picture.Ratio;
                }

                var name = $"media/picture{state.Media.Count + 1:D4}{picture.Extension}";
                var media = AddMedia(state, name, null, picture.Data);
                var align = paragraph.Align switch { TextAlign.Center => "center", TextAlign.Right => "right", _ => "left" };
                return PicturePart(state, media, drawnWidth, drawnHeight, align, space: 4, description, aloneOnPage: false);
            }

            var scale = contentWidth / RichDocument.ReferenceWidth;
            var properties = new XElement(W + "pPr");
            if (paragraph.TabStops.Count > 0)
            {
                properties.Add(new XElement(W + "tabs", paragraph.TabStops.Order().Select(stop =>
                    new XElement(W + "tab", new XAttribute(W + "val", "left"), new XAttribute(W + "pos", Twips(stop * scale))))));
            }

            if (paragraph.LeftIndent > 0 || paragraph.FirstLineIndent != 0)
            {
                var indent = new XElement(W + "ind", new XAttribute(W + "left", Twips(Math.Max(0, paragraph.LeftIndent * scale))));
                if (paragraph.FirstLineIndent > 0)
                    indent.Add(new XAttribute(W + "firstLine", Twips(paragraph.FirstLineIndent * scale)));
                else if (paragraph.FirstLineIndent < 0)
                    indent.Add(new XAttribute(W + "hanging", Twips(-paragraph.FirstLineIndent * scale)));

                properties.Add(indent);
            }

            if (paragraph.Align != TextAlign.Left)
                properties.Add(new XElement(W + "jc", new XAttribute(W + "val", paragraph.Align switch { TextAlign.Center => "center", TextAlign.Right => "right", _ => "both" })));

            var element = new XElement(W + "p");
            if (properties.HasElements)
                element.Add(properties);

            foreach (var run in paragraph.Runs)
            {
                if (run.Text.Length > 0)
                    element.Add(Run(run.Text, run.Style));
            }

            return element;
        }

        /// <summary>A run: a piece of text in one style; a tab is a tab, a line break (Shift+Enter) a break of the line.</summary>
        private static XElement Run(string text, RichStyle style)
        {
            var run = new XElement(W + "r");
            var properties = new XElement(W + "rPr");
            if (style.Bold)
                properties.Add(new XElement(W + "b"), new XElement(W + "bCs"));
            if (style.Italic)
                properties.Add(new XElement(W + "i"), new XElement(W + "iCs"));
            if (style.Strike)
                properties.Add(new XElement(W + "strike"));
            if (style.Color is { } color)
                properties.Add(new XElement(W + "color", new XAttribute(W + "val", (color & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture))));
            if (style.Size > 0)
            {
                var halfPoints = Math.Clamp((int)Math.Round(style.Size * 2), 2, 3276).ToString(CultureInfo.InvariantCulture);
                properties.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)), new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
            }

            if (style.Underline)
                properties.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));

            if (properties.HasElements)
                run.Add(properties);

            var piece = new StringBuilder();
            void Flush()
            {
                if (piece.Length == 0)
                    return;

                run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), piece.ToString()));
                piece.Clear();
            }

            var clean = Printable(text);
            foreach (var c in clean)
            {
                if (c == '\t')
                {
                    Flush();
                    run.Add(new XElement(W + "tab"));
                }
                else if (c == '\n')
                {
                    Flush();
                    run.Add(new XElement(W + "br"));
                }
                else
                {
                    piece.Append(c);
                }
            }

            Flush();
            return run;
        }

        /// <summary>The text without what XML cannot hold (control characters and half of a surrogate pair); line breaks and tabs stay.</summary>
        private static string Printable(string text)
        {
            var clean = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                    continue;

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    clean.Append(c).Append(text[++i]);
                }
                else if (c is '\t' or '\n' || (XmlConvert.IsXmlChar(c) && c >= ' '))
                {
                    clean.Append(c);
                }
            }

            return clean.ToString();
        }

        // ── Pictures ────────────────────────────────────────────────────────

        private static Media AddMedia(State state, string name, string? file, byte[]? data)
        {
            var media = new Media($"rIdImage{state.Media.Count + 1}", name, file, data);
            state.Media.Add(media);
            return media;
        }

        /// <summary>
        /// A paragraph that holds a picture, inline: <paramref name="width"/> by <paramref name="height"/> points, at the left, the middle or the right. A photo that is alone on its page has single
        /// line spacing and nothing before or after it (the spacing of the text would make its line taller than the page).
        /// </summary>
        private static XElement PicturePart(State state, Media media, double width, double height, string align, double space, string description, bool aloneOnPage)
        {
            var id = (++state.DrawingIds).ToString(CultureInfo.InvariantCulture);
            var cx = ((long)Math.Round(width * EmusPerPoint)).ToString(CultureInfo.InvariantCulture);
            var cy = ((long)Math.Round(height * EmusPerPoint)).ToString(CultureInfo.InvariantCulture);
            var name = Path.GetFileName(media.Name);

            var drawing = new XElement(W + "drawing",
                new XElement(Wp + "inline", new XAttribute("distT", "0"), new XAttribute("distB", "0"), new XAttribute("distL", "0"), new XAttribute("distR", "0"),
                    new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                    new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", $"Picture {id}"), new XAttribute("descr", Printable(description).Replace('\n', ' '))),
                    new XElement(Wp + "cNvGraphicFramePr", new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
                    new XElement(A + "graphic",
                        new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName),
                            new XElement(Pic + "pic",
                                new XElement(Pic + "nvPicPr", new XElement(Pic + "cNvPr", new XAttribute("id", id), new XAttribute("name", name)), new XElement(Pic + "cNvPicPr")),
                                new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", media.Id)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                new XElement(Pic + "spPr",
                                    new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")), new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));

            var spacing = new XElement(W + "spacing", new XAttribute(W + "before", aloneOnPage ? "0" : Twips(space)), new XAttribute(W + "after", aloneOnPage ? "0" : Twips(space)),
                new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto"));
            return new XElement(W + "p", new XElement(W + "pPr", spacing, new XElement(W + "jc", new XAttribute(W + "val", align))), new XElement(W + "r", drawing));
        }

        // ── The other parts ─────────────────────────────────────────────────

        private static string Twips(double points) => ((long)Math.Round(points * 20)).ToString(CultureInfo.InvariantCulture);

        private static XDocument ContentTypesPart(State state) =>
            new(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(ContentTypes + "Types",
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                    new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                    new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
                    new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/settings.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml")),
                    state.Footer
                        ? new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/footer1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"))
                        : null,
                    new XElement(ContentTypes + "Override", new XAttribute("PartName", "/docProps/core.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml"))));

        private static XDocument Relationships(params (string Id, string Type, string Target)[] items) =>
            new(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(PackageRelationships + "Relationships",
                    items.Select(item => new XElement(PackageRelationships + "Relationship", new XAttribute("Id", item.Id), new XAttribute("Type", item.Type), new XAttribute("Target", item.Target)))));

        private static XDocument CoreProperties(BookOptions options)
        {
            XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XNamespace terms = "http://purl.org/dc/terms/";
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(cp + "coreProperties", new XAttribute(XNamespace.Xmlns + "cp", cp), new XAttribute(XNamespace.Xmlns + "dc", dc), new XAttribute(XNamespace.Xmlns + "dcterms", terms), new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XElement(dc + "title", Printable(options.Title).Replace('\n', ' ')),
                    new XElement(dc + "creator", Printable(options.Author).Replace('\n', ' ')),
                    new XElement(cp + "lastModifiedBy", "PhotoLiker"),
                    new XElement(terms + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"), now),
                    new XElement(terms + "modified", new XAttribute(xsi + "type", "dcterms:W3CDTF"), now)));
        }

        /// <summary>The styles: the font of the book and 12 points as the text of a description is by default, single line spacing as Word has it, no room between paragraphs (a blank line is a blank line, as in the PDF).</summary>
        private static XDocument Styles(BookOptions options)
        {
            var font = string.IsNullOrWhiteSpace(options.FontFamily) ? "Segoe UI" : options.FontFamily.Trim();
            var fonts = new XElement(W + "rFonts", new XAttribute(W + "ascii", font), new XAttribute(W + "hAnsi", font), new XAttribute(W + "eastAsia", font), new XAttribute(W + "cs", font));
            var defaults = new XElement(W + "rPr", fonts,
                new XElement(W + "sz", new XAttribute(W + "val", (RichStyle.DefaultSize * 2).ToString("0", CultureInfo.InvariantCulture))),
                new XElement(W + "szCs", new XAttribute(W + "val", (RichStyle.DefaultSize * 2).ToString("0", CultureInfo.InvariantCulture))));
            if (LanguageTag(options.Language) is { } tag)
                defaults.Add(new XElement(W + "lang", new XAttribute(W + "val", tag), new XAttribute(W + "eastAsia", tag), new XAttribute(W + "bidi", "ar-SA")));

            return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W),
                    new XElement(W + "docDefaults",
                        new XElement(W + "rPrDefault", defaults),
                        new XElement(W + "pPrDefault", new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "0"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto"))))),
                    new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", "1"), new XAttribute(W + "styleId", "Normal"), new XElement(W + "name", new XAttribute(W + "val", "Normal")), new XElement(W + "qFormat")),
                    new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", "Footer"), new XElement(W + "name", new XAttribute(W + "val", "footer")),
                        new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")), new XElement(W + "uiPriority", new XAttribute(W + "val", "99")))));
        }

        /// <summary>The settings of the document: only that it is a document of the current Word (without it Word opens the file in compatibility mode, as one made by Word 2007 or earlier).</summary>
        private static XDocument Settings() =>
            new(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "settings", new XAttribute(XNamespace.Xmlns + "w", W),
                    new XElement(W + "zoom", new XAttribute(W + "percent", "100")),
                    new XElement(W + "compat",
                        new XElement(W + "compatSetting", new XAttribute(W + "name", "compatibilityMode"), new XAttribute(W + "uri", "http://schemas.microsoft.com/office/word"), new XAttribute(W + "val", "15")))));

        /// <summary>The language of the text as Word names it (for its spell checker); null for a language it has no name for here.</summary>
        internal static string? LanguageTag(string? code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ua" or "uk" => "uk-UA",
            "en" => "en-US",
            "ru" => "ru-RU",
            _ => null,
        };

        /// <summary>The foot of a page: its number in the middle, small and grey (as in the PDF).</summary>
        private static XDocument FooterPart() =>
            new(new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "ftr", new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R),
                    new XElement(W + "p",
                        new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", "Footer")), new XElement(W + "jc", new XAttribute(W + "val", "center"))),
                        new XElement(W + "fldSimple", new XAttribute(W + "instr", " PAGE "),
                            new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "color", new XAttribute(W + "val", "808080")), new XElement(W + "sz", new XAttribute(W + "val", "18")), new XElement(W + "szCs", new XAttribute(W + "val", "18"))),
                                new XElement(W + "t", "1"))))));

        private static void AddXml(ZipArchive zip, string name, XDocument document)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var target = entry.Open();
            using var writer = XmlWriter.Create(target, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
            document.Save(writer);
        }

        /// <summary>How wide text is, roughly: half the size of the letters for each, a little more in bold. It is only used to say how tall the text on a page is, so that a photo is not made taller than what is left.</summary>
        private sealed class EstimatingMeasurer : ITextMeasurer
        {
            public double Width(string text, RichStyle style) => text.Length * style.PointSize * (style.Bold ? 0.58 : 0.54);
        }
    }
}
