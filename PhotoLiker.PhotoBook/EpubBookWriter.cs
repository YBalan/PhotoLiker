using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace PhotoLiker.PhotoBook
{
    /// <summary>
    /// Writes the book as an EPUB 3 file (which is a zip of XHTML pages, a style sheet, the pictures and a few files that say what is in it): a page for each photo, with the description above
    /// it, the photo and the description below. The reader of the book sets the text to its screen and chooses its font, so the paper, margins and font of the dialog are not used. A new page
    /// starts for each photo; the contents list the photos by the first words written above them.
    /// </summary>
    public static class EpubBookWriter
    {
        private const string Xhtml = "http://www.w3.org/1999/xhtml";
        private const string Epub = "http://www.idpf.org/2007/ops";

        /// <summary>A font size of the description in points is written as a share of this: the size of the text of the book, which the reader of the book can change.</summary>
        private const double BaseSize = RichStyle.DefaultSize;

        public static void Write(string path, BookOptions options, IReadOnlyList<BookPage> pages, IProgress<int>? progress = null, CancellationToken cancellation = default)
        {
            var partial = path + ".part";
            try
            {
                using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    WriteArchive(zip, options, pages, progress, cancellation);

                File.Move(partial, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }

            progress?.Report(100);
        }

        private static void WriteArchive(ZipArchive zip, BookOptions options, IReadOnlyList<BookPage> pages, IProgress<int>? progress, CancellationToken cancellation)
        {
            // The first file says what the zip is, and is stored as it is (no squeezing, no extra field): readers look for it at a fixed place
            AddText(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            AddText(zip, "META-INF/container.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
                "  <rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles>\n" +
                "</container>\n");
            AddText(zip, "OEBPS/style.css", StyleSheet);

            var language = string.IsNullOrWhiteSpace(options.Language) ? "en" : options.Language.Trim();
            var title = string.IsNullOrWhiteSpace(options.Title) ? PhotoBookStrings.UntitledBook : options.Title.Trim();
            var hasTitlePage = !string.IsNullOrWhiteSpace(options.Title);

            var pageFiles = new List<(string File, string Label)>();
            if (hasTitlePage)
            {
                AddText(zip, "OEBPS/title.xhtml", TitlePage(options, title, language));
                pageFiles.Add(("title.xhtml", title));
            }

            for (var i = 0; i < pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var page = pages[i];
                var number = i + 1;
                var imageName = $"images/photo{number:D4}.jpg";
                var pageName = $"photo{number:D4}.xhtml";

                var entry = zip.CreateEntry("OEBPS/" + imageName, CompressionLevel.NoCompression); // a JPEG does not get smaller
                using (var target = entry.Open())
                using (var source = File.OpenRead(page.ImageFile))
                    source.CopyTo(target);

                AddText(zip, "OEBPS/" + pageName, PhotoPage(page, number, imageName, language, title));
                pageFiles.Add((pageName, Label(page, number)));
                progress?.Report((i + 1) * 95 / pages.Count);
            }

            AddText(zip, "OEBPS/nav.xhtml", NavigationPage(pageFiles, language, title));
            AddText(zip, "OEBPS/content.opf", PackageFile(options, title, language, pageFiles, pages.Count));
        }

        /// <summary>The words that name a photo in the contents: the first line written above it (or below it), else "Photo 3".</summary>
        private static string Label(BookPage page, int number)
        {
            var text = FirstLine(page.Above) ?? FirstLine(page.Below);
            if (text is null)
                return string.Format(CultureInfo.CurrentCulture, PhotoBookStrings.PhotoNumberFormat, number);

            return text.Length > 60 ? text[..59].TrimEnd() + "…" : text;
        }

        private static string? FirstLine(RichDocument document) =>
            document.Paragraphs.Select(paragraph => paragraph.PlainText.Replace('\n', ' ').Trim()).FirstOrDefault(line => line.Length > 0);

        private const string StyleSheet =
            "body { margin: 0; padding: 0; }\n" +
            ".page { text-align: center; page-break-after: always; break-after: page; padding: 0.5em 0; }\n" +
            ".page p { margin: 0 0 0.4em 0; text-align: left; line-height: 1.3; }\n" +
            ".picture { margin: 0.6em 0; }\n" +
            ".picture img { max-width: 100%; max-height: 80vh; height: auto; width: auto; }\n" +
            ".title { margin-top: 30%; }\n" +
            ".title h1 { font-size: 2.2em; margin: 0 0 0.6em 0; }\n" +
            ".title p.author { font-size: 1.3em; color: #606060; text-align: center; }\n";

        private static string TitlePage(BookOptions options, string title, string language) => Document(language, title, writer =>
        {
            writer.WriteStartElement("div", Xhtml);
            writer.WriteAttributeString("class", "page title");
            writer.WriteElementString("h1", Xhtml, title);
            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                writer.WriteStartElement("p", Xhtml);
                writer.WriteAttributeString("class", "author");
                writer.WriteString(options.Author.Trim());
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        });

        private static string PhotoPage(BookPage page, int number, string imageName, string language, string bookTitle) => Document(language, bookTitle, writer =>
        {
            writer.WriteStartElement("div", Xhtml);
            writer.WriteAttributeString("class", "page");
            WriteDescription(writer, page.Above);

            writer.WriteStartElement("div", Xhtml);
            writer.WriteAttributeString("class", "picture");
            writer.WriteStartElement("img", Xhtml);
            writer.WriteAttributeString("src", imageName);
            writer.WriteAttributeString("alt", string.IsNullOrWhiteSpace(page.Description) ? string.Format(CultureInfo.CurrentCulture, PhotoBookStrings.PhotoNumberFormat, number) : page.Description);
            writer.WriteEndElement();
            writer.WriteEndElement();

            WriteDescription(writer, page.Below);
            writer.WriteEndElement();
        });

        /// <summary>The paragraphs of a description: each as a paragraph with its alignment, the runs as spans that carry their look.</summary>
        private static void WriteDescription(XmlWriter writer, RichDocument document)
        {
            foreach (var paragraph in document.WithoutTrailingBlank().Paragraphs)
            {
                writer.WriteStartElement("p", Xhtml);
                if (paragraph.Align != TextAlign.Left)
                {
                    writer.WriteAttributeString("style", "text-align: " + paragraph.Align switch
                    {
                        TextAlign.Center => "center",
                        TextAlign.Right => "right",
                        _ => "justify",
                    });
                }

                if (paragraph.IsBlank && paragraph.Runs.Count == 0)
                {
                    writer.WriteString("\x00A0"); // an empty paragraph has no height in a reader: it is a line of space
                }
                else
                {
                    foreach (var run in paragraph.Runs)
                        WriteRun(writer, run);
                }

                writer.WriteEndElement();
            }
        }

        private static void WriteRun(XmlWriter writer, RichRun run)
        {
            var css = StyleOf(run.Style);
            if (css.Length > 0)
            {
                writer.WriteStartElement("span", Xhtml);
                writer.WriteAttributeString("style", css);
            }

            // A line break inside a paragraph is a <br/>; the characters XML cannot hold are left out
            var parts = run.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    writer.WriteElementString("br", Xhtml, null);
                writer.WriteString(Printable(parts[i].Replace("\t", "    ")));
            }

            if (css.Length > 0)
                writer.WriteEndElement();
        }

        private static string StyleOf(RichStyle style)
        {
            var css = new StringBuilder();
            if (style.Bold)
                css.Append("font-weight: bold; ");
            if (style.Italic)
                css.Append("font-style: italic; ");
            if (style.Underline || style.Strike)
                css.Append("text-decoration:").Append(style.Underline ? " underline" : "").Append(style.Strike ? " line-through" : "").Append("; ");
            if (style.Size > 0 && Math.Abs(style.Size - BaseSize) > 0.01)
                css.Append("font-size: ").Append((style.Size / BaseSize).ToString("0.###", CultureInfo.InvariantCulture)).Append("em; ");
            if (style.Color is { } color)
                css.Append("color: #").Append(color.ToString("X6", CultureInfo.InvariantCulture)).Append("; ");

            return css.ToString().TrimEnd();
        }

        /// <summary>The text without the control characters an XML file may not hold.</summary>
        private static string Printable(string text) => new([.. text.Where(c => !char.IsControl(c) || c is '\t')]);

        private static string NavigationPage(List<(string File, string Label)> pages, string language, string title)
        {
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, settings))
            {
                writer.WriteStartDocument();
                writer.WriteDocType("html", null, null, null);
                writer.WriteStartElement("html", Xhtml);
                writer.WriteAttributeString("xmlns", "epub", null, Epub);
                writer.WriteAttributeString("lang", language);
                writer.WriteAttributeString("xml", "lang", null, language);
                writer.WriteStartElement("head", Xhtml);
                writer.WriteElementString("title", Xhtml, title);
                writer.WriteEndElement();
                writer.WriteStartElement("body", Xhtml);
                writer.WriteStartElement("nav", Xhtml);
                writer.WriteAttributeString("epub", "type", Epub, "toc");
                writer.WriteElementString("h1", Xhtml, PhotoBookStrings.Contents);
                writer.WriteStartElement("ol", Xhtml);
                foreach (var (file, label) in pages)
                {
                    writer.WriteStartElement("li", Xhtml);
                    writer.WriteStartElement("a", Xhtml);
                    writer.WriteAttributeString("href", file);
                    writer.WriteString(Printable(label));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\xFEFF');
        }

        private static string PackageFile(BookOptions options, string title, string language, List<(string File, string Label)> pageFiles, int photoCount)
        {
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, settings))
            {
                const string Opf = "http://www.idpf.org/2007/opf";
                const string Dc = "http://purl.org/dc/elements/1.1/";

                writer.WriteStartDocument();
                writer.WriteStartElement("package", Opf);
                writer.WriteAttributeString("version", "3.0");
                writer.WriteAttributeString("unique-identifier", "book-id");
                writer.WriteAttributeString("xml", "lang", null, language);

                writer.WriteStartElement("metadata", Opf);
                writer.WriteAttributeString("xmlns", "dc", null, Dc);
                writer.WriteStartElement("identifier", Dc);
                writer.WriteAttributeString("id", "book-id");
                writer.WriteString("urn:uuid:" + Guid.NewGuid().ToString("D"));
                writer.WriteEndElement();
                writer.WriteElementString("title", Dc, title);
                writer.WriteElementString("language", Dc, language);
                if (!string.IsNullOrWhiteSpace(options.Author))
                    writer.WriteElementString("creator", Dc, options.Author.Trim());
                writer.WriteStartElement("meta", Opf);
                writer.WriteAttributeString("property", "dcterms:modified");
                writer.WriteString(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("manifest", Opf);
                ManifestItem(writer, Opf, "nav", "nav.xhtml", "application/xhtml+xml", "nav");
                ManifestItem(writer, Opf, "style", "style.css", "text/css", null);
                foreach (var (file, _) in pageFiles)
                    ManifestItem(writer, Opf, Path.GetFileNameWithoutExtension(file), file, "application/xhtml+xml", null);
                for (var number = 1; number <= photoCount; number++)
                    ManifestItem(writer, Opf, $"image{number:D4}", $"images/photo{number:D4}.jpg", "image/jpeg", number == 1 ? "cover-image" : null);
                writer.WriteEndElement();

                writer.WriteStartElement("spine", Opf);
                foreach (var (file, _) in pageFiles)
                {
                    writer.WriteStartElement("itemref", Opf);
                    writer.WriteAttributeString("idref", Path.GetFileNameWithoutExtension(file));
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\xFEFF');
        }

        private static void ManifestItem(XmlWriter writer, string ns, string id, string href, string mediaType, string? properties)
        {
            writer.WriteStartElement("item", ns);
            writer.WriteAttributeString("id", id);
            writer.WriteAttributeString("href", href);
            writer.WriteAttributeString("media-type", mediaType);
            if (properties is not null)
                writer.WriteAttributeString("properties", properties);
            writer.WriteEndElement();
        }

        /// <summary>A page of the book: the XHTML document round what <paramref name="body"/> writes into the body.</summary>
        private static string Document(string language, string title, Action<XmlWriter> body)
        {
            var settings = new XmlWriterSettings { Indent = false, Encoding = new UTF8Encoding(false) };
            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, settings))
            {
                writer.WriteStartDocument();
                writer.WriteDocType("html", null, null, null);
                writer.WriteStartElement("html", Xhtml);
                writer.WriteAttributeString("xmlns", "epub", null, Epub);
                writer.WriteAttributeString("lang", language);
                writer.WriteAttributeString("xml", "lang", null, language);
                writer.WriteStartElement("head", Xhtml);
                writer.WriteStartElement("meta", Xhtml);
                writer.WriteAttributeString("charset", "utf-8");
                writer.WriteEndElement();
                writer.WriteElementString("title", Xhtml, Printable(title));
                writer.WriteStartElement("link", Xhtml);
                writer.WriteAttributeString("rel", "stylesheet");
                writer.WriteAttributeString("type", "text/css");
                writer.WriteAttributeString("href", "style.css");
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteStartElement("body", Xhtml);
                body(writer);
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\xFEFF');
        }

        private static void AddText(ZipArchive zip, string name, string text, CompressionLevel level = CompressionLevel.Optimal)
        {
            var entry = zip.CreateEntry(name, level);
            using var stream = entry.Open();
            var bytes = new UTF8Encoding(false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
