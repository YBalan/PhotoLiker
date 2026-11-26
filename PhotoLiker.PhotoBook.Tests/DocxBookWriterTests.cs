using System.IO.Compression;
using System.IO.Packaging;
using System.Xml.Linq;

namespace PhotoLiker.PhotoBook.Tests;

/// <summary>
/// The Word document: the file is a package whose parts all exist and are listed, a page is a section with the paper of its photo, the text keeps its look (and its indents, tabs and pictures), the photo
/// is the JPEG as it is, the title and the page numbers are as the book asks. What is checked is the XML that Word reads (the files were also opened in Word itself, which no test can do).
/// </summary>
public sealed class DocxBookWriterTests : IDisposable
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private readonly BookFiles _files = new();

    public void Dispose() => _files.Dispose();

    private static BookOptions Options(string title = "", string author = "", string language = "en", PageFormat format = PageFormat.A4, PageOrientation orientation = PageOrientation.FollowPhoto,
        double margin = 15, bool numbers = true, string font = "Segoe UI") => new(title, author, language, format, orientation, margin, font, numbers);

    private string Write(BookOptions options, params BookPage[] pages) => Write(options, pages, "book.docx");

    private string Write(BookOptions options, IReadOnlyList<BookPage> pages, string name, IProgress<int>? progress = null, CancellationToken cancellation = default)
    {
        var path = _files.Path_(name);
        DocxBookWriter.Write(path, options, pages, progress, cancellation);
        AssertWholePackage(path); // every file a test makes is checked as Word checks a package
        return path;
    }

    /// <summary>
    /// What Word needs of a package: the part that lists the types is first; each part has a type (its own, or that of its extension); each part that is listed is there; each relationship points at a
    /// part that is there; and each part is well-formed XML (those that are XML).
    /// </summary>
    private static void AssertWholePackage(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("[Content_Types].xml", zip.Entries[0].FullName);
        var names = zip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var types = Part(path, "[Content_Types].xml").Root!;
        var defaults = types.Elements().Where(element => element.Name.LocalName == "Default").Select(element => (string)element.Attribute("Extension")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = types.Elements().Where(element => element.Name.LocalName == "Override").Select(element => (string)element.Attribute("PartName")!).ToList();
        foreach (var name in names.Where(name => name != "[Content_Types].xml"))
            Assert.True(overrides.Contains("/" + name) || defaults.Contains(Path.GetExtension(name).TrimStart('.')), $"{name} has no content type");
        foreach (var part in overrides)
            Assert.Contains(part.TrimStart('/'), names);

        foreach (var relationships in names.Where(name => name.EndsWith(".rels", StringComparison.Ordinal)))
        {
            var folder = relationships == "_rels/.rels" ? string.Empty : relationships[..relationships.IndexOf("_rels/", StringComparison.Ordinal)];
            foreach (var relationship in Part(path, relationships).Root!.Elements())
            {
                var target = new Uri(new Uri("http://package/" + folder), (string)relationship.Attribute("Target")!).AbsolutePath.TrimStart('/');
                Assert.Contains(target, names);
            }
        }

        foreach (var name in names.Where(name => name.EndsWith(".xml", StringComparison.Ordinal) || name.EndsWith(".rels", StringComparison.Ordinal)))
            Assert.NotNull(Part(path, name).Root);
    }

    private static XDocument Part(string path, string name)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    private static XDocument Document(string path) => Part(path, "word/document.xml");

    private static List<XElement> Sections(XDocument document) => [.. document.Descendants(W + "sectPr")];

    private static string Text(XElement paragraph) =>
        string.Concat(paragraph.Descendants().Where(element => element.Name == W + "t" || element.Name == W + "tab" || element.Name == W + "br")
            .Select(element => element.Name == W + "t" ? element.Value : element.Name == W + "tab" ? "<TAB>" : "<BR>"));

    private static List<XElement> Paragraphs(XDocument document) => [.. document.Descendants(W + "p")];

    /// <summary>The paragraphs that are text (a photo or a picture is a paragraph that holds a drawing).</summary>
    private static List<XElement> TextParagraphs(XDocument document) => [.. Paragraphs(document).Where(paragraph => !paragraph.Descendants(W + "drawing").Any())];

    private static int Twips(XElement section, string attribute, string element = "pgSz") => (int)section.Element(W + element)!.Attribute(W + attribute)!;

    private static RichRun Run(string text, RichStyle? style = null) => new(text, style ?? default);

    private static RichParagraph Para(params RichRun[] runs) => new(TextAlign.Left, runs);

    // ── The package ─────────────────────────────────────────────────────────

    [Fact]
    public void TheFile_IsAPackage_WhosePartsAreAllThereAndListed_AndEveryPartThatIsUsedIsRelated()
    {
        var text = BookFiles.Text("one", BookFiles.Picture(0.4));
        var path = Write(Options("Title", "Author", "ua"), _files.Page("a", text, RichDocument.Empty, portrait: true), _files.Page("b", "x", "y"));

        using (var zip = ZipFile.OpenRead(path)) // the package as a whole is checked by Write; here, what is in it
        {
            var parts = zip.Entries.Select(entry => entry.FullName).Order().ToList();
            Assert.Contains("word/document.xml", parts);
            Assert.Contains("word/styles.xml", parts);
            Assert.Contains("word/settings.xml", parts);
            Assert.Contains("word/footer1.xml", parts);
            Assert.Contains("docProps/core.xml", parts);
            Assert.Equal(3, parts.Count(part => part.StartsWith("word/media/", StringComparison.Ordinal))); // two photos and a picture of the text
        }

        var relationships = Part(path, "word/_rels/document.xml.rels").Root!.Elements().ToList();
        Assert.Equal(3, relationships.Count(relationship => ((string)relationship.Attribute("Type")!).EndsWith("/image", StringComparison.Ordinal)));
        var types = Part(path, "[Content_Types].xml").Root!.Elements().ToList();
        Assert.Contains(types, element => (string?)element.Attribute("Extension") == "jpg" && (string?)element.Attribute("ContentType") == "image/jpeg");
        Assert.Contains(types, element => (string?)element.Attribute("Extension") == "png" && (string?)element.Attribute("ContentType") == "image/png");

        // Every picture that is in the zip is used by the document, and every id it uses is in the relationships
        var xml = Document(path);
        var used = xml.Descendants(A + "blip").Select(blip => (string)blip.Attribute(R + "embed")!).ToList();
        var related = Part(path, "word/_rels/document.xml.rels").Root!.Elements().Select(element => (string)element.Attribute("Id")!).ToList();
        Assert.All(used, id => Assert.Contains(id, related));
        Assert.Equal(used.Count, used.Distinct().Count());
    }

    [Fact]
    public void TheCoreProperties_HaveTheTitleAndTheAuthor_AndTheStylesTheFontAndTheLanguage()
    {
        var path = Write(Options("Щоденник", "Олеся", "ua", font: "Georgia"), _files.Page("a", "x", null));

        var core = Part(path, "docProps/core.xml");
        Assert.Equal("Щоденник", core.Descendants().Single(element => element.Name.LocalName == "title").Value);
        Assert.Equal("Олеся", core.Descendants().Single(element => element.Name.LocalName == "creator").Value);

        var styles = Part(path, "word/styles.xml");
        var defaults = styles.Descendants(W + "rPrDefault").Single();
        Assert.Equal("Georgia", (string)defaults.Descendants(W + "rFonts").Single().Attribute(W + "ascii")!);
        Assert.Equal("Georgia", (string)defaults.Descendants(W + "rFonts").Single().Attribute(W + "cs")!);
        Assert.Equal("24", (string)defaults.Descendants(W + "sz").Single().Attribute(W + "val")!); // 12 points
        Assert.Equal("uk-UA", (string)defaults.Descendants(W + "lang").Single().Attribute(W + "val")!);
    }

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("ua", "uk-UA")]
    [InlineData("ru", "ru-RU")]
    [InlineData("mul", null)]
    [InlineData("", null)]
    public void TheLanguageOfTheBook_IsTheOneWordNames_ForItsSpellChecker(string code, string? expected) =>
        Assert.Equal(expected, DocxBookWriter.LanguageTag(code));

    [Fact]
    public void ASettingsPart_MakesWordOpenTheFileAsADocumentOfTheCurrentVersion()
    {
        var settings = Part(Write(Options(), _files.Page("a", "x", null)), "word/settings.xml");

        var mode = settings.Descendants(W + "compatSetting").Single(element => (string)element.Attribute(W + "name")! == "compatibilityMode");
        Assert.Equal("15", (string)mode.Attribute(W + "val")!);
    }

    // ── The pages ───────────────────────────────────────────────────────────

    [Fact]
    public void EachPage_IsASection_WithThePaperOfItsPhoto_AndTheTitleIsOneToo()
    {
        var path = Write(Options("T"), _files.Page("a", "x", null, portrait: true), _files.Page("b", "y", null), BookPage.TextOnly(RichDocument.FromPlainText("only text"), "c"));

        var sections = Sections(Document(path));

        Assert.Equal(4, sections.Count); // the title, a portrait photo, a landscape photo, a page of text
        var (portraitWidth, portraitHeight) = PageSizes.Points(PageFormat.A4, PageOrientation.Portrait, true);
        Assert.Equal((int)Math.Round(portraitWidth * 20), Twips(sections[0], "w"));
        Assert.Equal((int)Math.Round(portraitHeight * 20), Twips(sections[0], "h"));
        Assert.Null(sections[0].Element(W + "pgSz")!.Attribute(W + "orient"));
        Assert.Equal((int)Math.Round(portraitWidth * 20), Twips(sections[1], "w")); // a portrait photo
        Assert.Equal("landscape", (string?)sections[2].Element(W + "pgSz")!.Attribute(W + "orient")); // a landscape photo
        Assert.True(Twips(sections[2], "w") > Twips(sections[2], "h"));
        Assert.Null(sections[3].Element(W + "pgSz")!.Attribute(W + "orient")); // text is portrait
    }

    [Fact]
    public void TheOrientationAndThePaperOfTheBook_AreTheOnesAsked()
    {
        var path = Write(Options(format: PageFormat.A5, orientation: PageOrientation.Landscape, margin: 10), _files.Page("a", "x", null, portrait: true));

        var section = Sections(Document(path)).Single();

        Assert.Equal("landscape", (string?)section.Element(W + "pgSz")!.Attribute(W + "orient"));
        Assert.Equal((int)Math.Round(210 * PageSizes.PointsPerMillimetre * 20), Twips(section, "w"));
        Assert.Equal((int)Math.Round(148 * PageSizes.PointsPerMillimetre * 20), Twips(section, "h"));
        var margin = (int)Math.Round(10 * PageSizes.PointsPerMillimetre * 20);
        foreach (var side in new[] { "top", "right", "bottom", "left" })
            Assert.Equal(margin, Twips(section, side, "pgMar"));
    }

    [Fact]
    public void TheTitlePage_HasTheTitleLargeAndBold_TheAuthorUnderIt_NoPageNumber_AndTheFirstPhotoIsPageOne()
    {
        var path = Write(Options("My book", "Me"), _files.Page("a", "text", null));

        var document = Document(path);
        var sections = Sections(document);
        Assert.Equal(2, sections.Count);
        Assert.Null(sections[0].Element(W + "footerReference")); // no number on the title page
        Assert.Null(sections[0].Element(W + "pgNumType"));
        Assert.NotNull(sections[1].Element(W + "footerReference"));
        Assert.Equal("1", (string)sections[1].Element(W + "pgNumType")!.Attribute(W + "start")!);

        var paragraphs = Paragraphs(document);
        Assert.Equal("My book", Text(paragraphs[0]));
        Assert.Equal("60", (string)paragraphs[0].Descendants(W + "sz").Single().Attribute(W + "val")!); // 30 points
        Assert.NotNull(paragraphs[0].Descendants(W + "b").SingleOrDefault());
        Assert.Equal("center", (string)paragraphs[0].Descendants(W + "jc").Single().Attribute(W + "val")!);
        Assert.Contains(paragraphs, paragraph => Text(paragraph) == "Me");
    }

    [Fact]
    public void ABookWithNoTitle_HasNoTitlePage_AndItsFirstPageIsNumberOne()
    {
        var path = Write(Options(), _files.Page("a", "text", null), _files.Page("b", "more", null));

        var sections = Sections(Document(path));

        Assert.Equal(2, sections.Count);
        Assert.Equal("1", (string)sections[0].Element(W + "pgNumType")!.Attribute(W + "start")!);
        Assert.Null(sections[1].Element(W + "pgNumType"));
    }

    [Fact]
    public void ThePageNumbers_AreAFooterThatHoldsThePageField_WhenAsked_AndNothingWhenNot()
    {
        var with = Write(Options(), [_files.Page("a", "x", null)], "with.docx");
        var without = Write(Options(numbers: false), [_files.Page("a", "x", null)], "without.docx");

        var footer = Part(with, "word/footer1.xml");
        Assert.Equal(" PAGE ", (string)footer.Descendants(W + "fldSimple").Single().Attribute(W + "instr")!);
        Assert.Equal("center", (string)footer.Descendants(W + "jc").Single().Attribute(W + "val")!);
        Assert.NotNull(Sections(Document(with)).Single().Element(W + "footerReference"));

        using var zip = ZipFile.OpenRead(without);
        Assert.Null(zip.GetEntry("word/footer1.xml"));
        Assert.Null(Sections(Document(without)).Single().Element(W + "footerReference"));
    }

    [Fact]
    public void ABookWithNothingInIt_IsStillADocumentWithAPage()
    {
        var path = Write(Options(), []);

        var document = Document(path);
        Assert.Single(Sections(document));
        Assert.NotEmpty(Paragraphs(document));
    }

    // ── The text ────────────────────────────────────────────────────────────

    [Fact]
    public void TheLookOfTheText_IsKept_ItsOrderOfPropertiesIsTheOneWordAccepts()
    {
        var style = new RichStyle(Bold: true, Italic: true, Underline: true, Strike: true, Size: 18, Color: 0xCC2222);
        var page = _files.Page("a", new RichDocument([Para(Run("plain "), Run("styled", style))]), RichDocument.Empty);

        var runs = TextParagraphs(Document(Write(Options(), page))).Single().Elements(W + "r").ToList();

        Assert.Equal(2, runs.Count);
        Assert.Null(runs[0].Element(W + "rPr")); // plain text carries no properties
        Assert.Equal("styled", runs[1].Element(W + "t")!.Value);
        // The order the schema gives them in: bold, italic, strike, colour, size, underline
        Assert.Equal(["b", "bCs", "i", "iCs", "strike", "color", "sz", "szCs", "u"], runs[1].Element(W + "rPr")!.Elements().Select(element => element.Name.LocalName).ToArray());
        Assert.Equal("CC2222", (string)runs[1].Element(W + "rPr")!.Element(W + "color")!.Attribute(W + "val")!);
        Assert.Equal("36", (string)runs[1].Element(W + "rPr")!.Element(W + "sz")!.Attribute(W + "val")!);
        Assert.Equal("single", (string)runs[1].Element(W + "rPr")!.Element(W + "u")!.Attribute(W + "val")!);
    }

    [Fact]
    public void ATextWithSpacesAtItsEnds_KeepsThem_ATabIsATab_AndALineBreakIsABreak()
    {
        var page = _files.Page("a", new RichDocument([Para(Run("  one\ttwo  "), Run("three\nfour"))]), RichDocument.Empty);

        var paragraph = TextParagraphs(Document(Write(Options(), page))).Single();

        Assert.Equal("  one<TAB>two  three<BR>four", Text(paragraph));
        Assert.All(paragraph.Descendants(W + "t"), text => Assert.Equal("preserve", (string?)text.Attribute(XNamespace.Xml + "space")));
    }

    [Fact]
    public void TheAlignmentOfAParagraph_IsKept()
    {
        RichParagraph At(TextAlign align) => new(align, [Run(align.ToString())]);
        var page = _files.Page("a", new RichDocument([At(TextAlign.Left), At(TextAlign.Center), At(TextAlign.Right), At(TextAlign.Justify)]), RichDocument.Empty);

        var alignments = TextParagraphs(Document(Write(Options(), page))).Select(paragraph => (string?)paragraph.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val")).ToArray();

        Assert.Equal(new string?[] { null, "center", "right", "both" }, alignments); // left is what a paragraph is
    }

    [Fact]
    public void TheIndentsAndTheTabStops_AreKeptInTheShareOfTheColumnTheyWereSetIn()
    {
        // The text area of an A4 page with 15 mm margins is 510.3 points wide; the paragraph was set in a column of 432
        var scale = (210 - 30) * PageSizes.PointsPerMillimetre / RichDocument.ReferenceWidth;
        var indented = new RichParagraph(TextAlign.Left, [Run("a")]) { LeftIndent = 72, FirstLineIndent = 18 };
        var hanging = new RichParagraph(TextAlign.Left, [Run("b")]) { LeftIndent = 72, FirstLineIndent = -36 };
        var tabbed = new RichParagraph(TextAlign.Left, [Run("c\td")]) { TabStops = [260, 120] };
        var page = _files.Page("a", new RichDocument([indented, hanging, tabbed]), RichDocument.Empty, portrait: true);

        var paragraphs = TextParagraphs(Document(Write(Options(), page)));

        var first = paragraphs[0].Element(W + "pPr")!.Element(W + "ind")!;
        Assert.Equal((int)Math.Round(72 * scale * 20), (int)first.Attribute(W + "left")!);
        Assert.Equal((int)Math.Round(18 * scale * 20), (int)first.Attribute(W + "firstLine")!);
        var second = paragraphs[1].Element(W + "pPr")!.Element(W + "ind")!;
        Assert.Equal((int)Math.Round(36 * scale * 20), (int)second.Attribute(W + "hanging")!);
        Assert.Null(second.Attribute(W + "firstLine"));
        var stops = paragraphs[2].Element(W + "pPr")!.Element(W + "tabs")!.Elements(W + "tab").Select(tab => (int)tab.Attribute(W + "pos")!).ToArray();
        Assert.Equal([(int)Math.Round(120 * scale * 20), (int)Math.Round(260 * scale * 20)], stops); // from the nearest
        Assert.All(paragraphs[2].Element(W + "pPr")!.Element(W + "tabs")!.Elements(W + "tab"), tab => Assert.Equal("left", (string)tab.Attribute(W + "val")!));
    }

    [Fact]
    public void AListItem_IsItsMarkerATabAndTheText_HangingFromTheIndent_WithAStopWhereTheTextStarts()
    {
        var scale = (210 - 30) * PageSizes.PointsPerMillimetre / RichDocument.ReferenceWidth;
        var bullet = new RichParagraph(TextAlign.Left, [Run("first", new RichStyle(Bold: true))]) { LeftIndent = 36, FirstLineIndent = -18, Marker = "•" };
        var number = new RichParagraph(TextAlign.Left, [Run("second")]) { LeftIndent = 36, FirstLineIndent = -18, Marker = "2." };
        var empty = new RichParagraph(TextAlign.Left, []) { LeftIndent = 36, FirstLineIndent = -18, Marker = "3." };
        var page = _files.Page("a", new RichDocument([bullet, number, empty, Para(Run("after"))]), RichDocument.Empty, portrait: true);

        var paragraphs = TextParagraphs(Document(Write(Options(), page)));

        Assert.Equal(["•<TAB>first", "2.<TAB>second", "", "after"], paragraphs.Select(paragraph => string.Concat(paragraph.Elements(W + "r").Select(Text)))); // (the runs: the stops are in the properties)
        var properties = paragraphs[0].Element(W + "pPr")!;
        Assert.Equal(["tabs", "ind"], properties.Elements().Select(element => element.Name.LocalName).ToArray());
        var left = (int)properties.Element(W + "ind")!.Attribute(W + "left")!;
        Assert.Equal((int)Math.Round(36 * scale * 20), left);
        Assert.Equal((int)Math.Round(18 * scale * 20), (int)properties.Element(W + "ind")!.Attribute(W + "hanging")!);
        Assert.Equal([left], properties.Element(W + "tabs")!.Elements(W + "tab").Select(tab => (int)tab.Attribute(W + "pos")!).ToArray()); // the text starts at the stop
        Assert.True(paragraphs[0].Elements(W + "r").First().Element(W + "rPr")!.Element(W + "b") is not null); // the marker is as bold as the text
    }

    [Fact]
    public void TheElementsOfAParagraph_AreInTheOrderTheSchemaGivesThem()
    {
        var paragraph = new RichParagraph(TextAlign.Justify, [Run("x")]) { LeftIndent = 20, TabStops = [100] };
        var page = _files.Page("a", new RichDocument([paragraph]), RichDocument.Empty);

        var properties = TextParagraphs(Document(Write(Options(), page))).Single().Element(W + "pPr")!;

        Assert.Equal(["tabs", "ind", "jc"], properties.Elements().Select(element => element.Name.LocalName).ToArray());
    }

    [Fact]
    public void CharactersThatXmlCannotHold_AreLeftOut_AMarkOfTheLanguageAndAnEmojiAreNot_AndTheFileIsWellFormed()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var control = ((char)1).ToString();
        var page = _files.Page("a", new RichDocument([Para(Run("a" + control + "b" + emoji + "ї" + ((char)0xFFFE) + "c"))]), RichDocument.Empty);

        var path = Write(Options("T" + control), page);

        var paragraphs = TextParagraphs(Document(path));
        Assert.Equal("T", Text(paragraphs[0])); // the title, without the control character
        Assert.Equal("ab" + emoji + "їc", Text(paragraphs[1]));
        Assert.Equal("T", Part(path, "docProps/core.xml").Descendants().Single(element => element.Name.LocalName == "title").Value);
    }

    // ── The photos and the pictures ─────────────────────────────────────────

    [Fact]
    public void ThePhoto_IsTheJpegFileAsItIs_InlineAtTheMiddle_AsWideAsTheRatioOfItsPixelsSays()
    {
        var page = _files.Page("a", portrait: true); // 48 x 64
        var bytes = File.ReadAllBytes(page.ImageFile);

        var path = Write(Options(), page);

        using (var zip = ZipFile.OpenRead(path))
        {
            using var stored = new MemoryStream();
            using (var entry = zip.GetEntry("word/media/photo0001.jpg")!.Open())
                entry.CopyTo(stored);
            Assert.Equal(bytes, stored.ToArray());
        }

        var drawing = Document(path).Descendants(W + "drawing").Single();
        var extent = drawing.Descendants(Wp + "extent").Single();
        var cx = (long)extent.Attribute("cx")!;
        var cy = (long)extent.Attribute("cy")!;
        Assert.Equal(64.0 / 48.0, (double)cy / cx, 3);
        Assert.Equal("center", (string)drawing.Ancestors(W + "p").Single().Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val")!);
        Assert.NotNull(drawing.Descendants(Wp + "inline").SingleOrDefault());
    }

    [Fact]
    public void APhotoAloneOnAPage_IsAsLargeAsTheTextAreaLetsItBe_AndNoLargerThanIt()
    {
        var path = Write(Options(), _files.Page("tall", portrait: true), _files.Page("wide"));

        var extents = Document(path).Descendants(Wp + "extent").Select(extent => ((long)extent.Attribute("cx")! / 12700.0, (long)extent.Attribute("cy")! / 12700.0)).ToList();

        // A portrait photo (48 x 64) on an A4 page is limited by the width of the text area (180 mm), and fits its height
        var portraitArea = ((210 - 30) * PageSizes.PointsPerMillimetre, (297 - 30) * PageSizes.PointsPerMillimetre);
        Assert.InRange(extents[0].Item1, portraitArea.Item1 - 1, portraitArea.Item1);
        Assert.True(extents[0].Item2 < portraitArea.Item2 - 10, "it leaves the line that holds it");
        // A landscape photo (64 x 48) is on a landscape page (267 by 180 mm of text area) and limited by its height, less the line that holds it
        var landscapeHeight = (210 - 30) * PageSizes.PointsPerMillimetre;
        Assert.InRange(extents[1].Item2, landscapeHeight - 15, landscapeHeight - 13);
    }

    [Fact]
    public void WhenTheTextIsShort_ThePhotoIsGivenWhatItLeaves_AndWhenItIsLong_ThePhotoIsNoMoreThanASharedOfAPage()
    {
        var shortText = BookFiles.Text("one", "two");
        var longText = new RichDocument([.. Enumerable.Range(0, 90).Select(i => Para(Run("line " + i)))]);
        var alone = Write(Options(), [_files.Page("a", portrait: true)], "alone.docx");
        var withShort = Write(Options(), [_files.Page("a", shortText, RichDocument.Empty, portrait: true)], "short.docx");
        var withLong = Write(Options(), [_files.Page("a", longText, RichDocument.Empty, portrait: true)], "long.docx");

        double Height(string path) => (long)Document(path).Descendants(Wp + "extent").Single().Attribute("cy")! / 12700.0;

        Assert.True(Height(withShort) < Height(alone), "the text takes room from the photo");
        Assert.True(Height(withLong) < (297 - 30) * PageSizes.PointsPerMillimetre * 0.61, "a page of text that is long leaves the photo at most 60 % of a page");
        Assert.True(Height(withLong) > 100);
    }

    [Fact]
    public void APictureInTheText_IsAnInlineImageOfTheWidthItWasAskedFor_InItsOwnParagraph_AtItsSide()
    {
        var left = BookFiles.Picture(0.4, TextAlign.Left);
        var right = BookFiles.Picture(0.3, TextAlign.Right);
        var page = _files.Page("a", BookFiles.Text("before", left, right, "after"), RichDocument.Empty, portrait: true);

        var path = Write(Options(), page);

        var drawings = Document(path).Descendants(W + "drawing").ToList();
        Assert.Equal(3, drawings.Count); // two pictures and the photo
        var area = (210 - 30) * PageSizes.PointsPerMillimetre;
        Assert.Equal(area * 0.4, (long)drawings[0].Descendants(Wp + "extent").Single().Attribute("cx")! / 12700.0, 0);
        Assert.Equal(area * 0.3, (long)drawings[1].Descendants(Wp + "extent").Single().Attribute("cx")! / 12700.0, 0);
        Assert.Equal("left", (string)drawings[0].Ancestors(W + "p").Single().Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val")!);
        Assert.Equal("right", (string)drawings[1].Ancestors(W + "p").Single().Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val")!);

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("word/media/picture0001.png")); // a PNG keeps its alpha: it is the bytes of the picture as they are
        Assert.NotNull(zip.GetEntry("word/media/picture0002.png"));
    }

    [Fact]
    public void EveryPictureHasAnIdOfItsOwn_AndAName()
    {
        var page = _files.Page("a", BookFiles.Text(BookFiles.Picture(), BookFiles.Picture(0.3)), RichDocument.Empty);

        var drawings = Document(Write(Options(), page)).Descendants(Wp + "docPr").ToList();

        Assert.Equal(3, drawings.Count);
        Assert.Equal(3, drawings.Select(docPr => (string)docPr.Attribute("id")!).Distinct().Count());
        Assert.All(drawings, docPr => Assert.StartsWith("Picture ", (string)docPr.Attribute("name")!));
    }

    [Fact]
    public void ThePagesOfTextOnly_HaveNoPhoto_AndAreInTheOrderOfTheBook()
    {
        var path = Write(Options(), BookPage.TextOnly(RichDocument.FromPlainText("first"), "1"), _files.Page("b", "second", null), BookPage.TextOnly(RichDocument.FromPlainText("third"), "3"));

        var document = Document(path);

        Assert.Equal(["first", "second", "third"], Paragraphs(document).Select(Text).Where(text => text.Length > 0).ToArray());
        Assert.Single(document.Descendants(W + "drawing"));
    }

    // ── Writing the file ────────────────────────────────────────────────────

    [Fact]
    public void TheFileIsWrittenUnderAnotherName_ThatIsGoneWhenItIsDone_AndWhenItWasStopped_NoFileIsLeft()
    {
        var path = _files.Path_("stopped.docx");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => DocxBookWriter.Write(path, Options(), [_files.Page("a", "x", null)], null, cancel.Token));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));

        var made = Write(Options(), _files.Page("a", "x", null));
        Assert.True(File.Exists(made));
        Assert.False(File.Exists(made + ".part"));
    }

    [Fact]
    public void AFileThatIsThereAlready_IsWrittenOver()
    {
        var path = _files.Path_("book.docx");
        File.WriteAllText(path, "not a document");

        DocxBookWriter.Write(path, Options(), [_files.Page("a", "x", null)]);

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("word/document.xml"));
    }

    [Fact]
    public void TheProgress_GrowsToAHundred_AndWriteSaysHowManySectionsThereAre()
    {
        var progress = new Reported();

        var count = DocxBookWriter.Write(_files.Path_("p.docx"), Options("T"), [_files.Page("a", "x", null), _files.Page("b", "y", null)], progress);

        Assert.Equal(3, count); // the title and two pages
        Assert.Equal(100, progress.Values[^1]);
        Assert.Equal(progress.Values.Order().ToList(), progress.Values);
    }
}
