using System.IO.Compression;
using System.Xml.Linq;

namespace PhotoLiker.PhotoBook.Tests;

public sealed class EpubBookWriterTests : IDisposable
{
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private readonly BookFiles _files = new();

    public void Dispose() => _files.Dispose();

    private static BookOptions Options(string title = "", string author = "", string language = "en") => new(title, author, language);

    private string Write(BookOptions options, params BookPage[] pages)
    {
        var path = _files.Path_("book.epub");
        EpubBookWriter.Write(path, options, pages);
        return path;
    }

    private static string Text(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)?.Open() ?? throw new InvalidOperationException($"{name} is not in the file"));
        return reader.ReadToEnd();
    }

    private static XDocument Xml(ZipArchive zip, string name) => XDocument.Parse(Text(zip, name));

    [Fact]
    public void TheFirstFile_IsTheMimetype_StoredAsItIs()
    {
        var path = Write(Options(), _files.Page("a"));

        // The zip as a reader that does not know zip looks at it: "PK", the method of the first file (0: stored), its name, and its text straight after
        var bytes = File.ReadAllBytes(path);
        Assert.Equal([(byte)'P', (byte)'K', 3, 4], bytes[..4]);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 8));
        var nameLength = BitConverter.ToUInt16(bytes, 26);
        var extraLength = BitConverter.ToUInt16(bytes, 28);
        Assert.Equal("mimetype", System.Text.Encoding.ASCII.GetString(bytes, 30, nameLength));
        Assert.Equal(0, extraLength);
        Assert.Equal("application/epub+zip", System.Text.Encoding.ASCII.GetString(bytes, 30 + nameLength, "application/epub+zip".Length));
    }

    [Fact]
    public void EveryPartThatIsXml_IsWellFormed()
    {
        var path = Write(Options("Title", "Author"), _files.Page("a", "above", "below"), _files.Page("b"));

        using var zip = ZipFile.OpenRead(path);
        var parts = zip.Entries.Where(entry => entry.Name.EndsWith(".xhtml") || entry.Name.EndsWith(".opf") || entry.Name.EndsWith(".xml")).ToList();
        Assert.True(parts.Count >= 5);
        foreach (var part in parts)
            Xml(zip, part.FullName);
    }

    [Fact]
    public void TheContainer_PointsToThePackageFile()
    {
        var path = Write(Options(), _files.Page("a"));

        using var zip = ZipFile.OpenRead(path);
        var rootfile = Xml(zip, "META-INF/container.xml").Descendants().First(element => element.Name.LocalName == "rootfile");
        Assert.Equal("OEBPS/content.opf", rootfile.Attribute("full-path")!.Value);
        Assert.NotNull(zip.GetEntry("OEBPS/content.opf"));
    }

    [Fact]
    public void ThePackage_ListsEveryFile_AndTheSpineIsInTheOrderOfThePhotos()
    {
        var path = Write(Options("Summer", "Yurii", "uk"), _files.Page("a"), _files.Page("b"), _files.Page("c"));

        using var zip = ZipFile.OpenRead(path);
        var package = Xml(zip, "OEBPS/content.opf").Root!;
        var manifest = package.Element(Opf + "manifest")!.Elements(Opf + "item").ToList();

        foreach (var item in manifest)
            Assert.NotNull(zip.GetEntry("OEBPS/" + item.Attribute("href")!.Value));

        Assert.Single(manifest, item => item.Attribute("properties")?.Value == "nav");

        var ids = manifest.Select(item => item.Attribute("id")!.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        var spine = package.Element(Opf + "spine")!.Elements(Opf + "itemref").Select(item => item.Attribute("idref")!.Value).ToList();
        Assert.All(spine, id => Assert.Contains(id, ids));
        Assert.Equal(["title", "photo0001", "photo0002", "photo0003"], spine);

        var metadata = package.Element(Opf + "metadata")!;
        Assert.Equal("Summer", metadata.Element(Dc + "title")!.Value);
        Assert.Equal("Yurii", metadata.Element(Dc + "creator")!.Value);
        Assert.Equal("uk", metadata.Element(Dc + "language")!.Value);
        Assert.StartsWith("urn:uuid:", metadata.Element(Dc + "identifier")!.Value);
        Assert.Contains(metadata.Elements(Opf + "meta"), meta => meta.Attribute("property")?.Value == "dcterms:modified");
        Assert.Equal("book-id", package.Attribute("unique-identifier")!.Value);
        Assert.Equal("3.0", package.Attribute("version")!.Value);
    }

    [Fact]
    public void NoTitle_NoTitlePage_AndTheBookIsCalledSomething()
    {
        var path = Write(Options(), _files.Page("a"));

        using var zip = ZipFile.OpenRead(path);
        Assert.Null(zip.GetEntry("OEBPS/title.xhtml"));
        var package = Xml(zip, "OEBPS/content.opf").Root!;
        Assert.False(string.IsNullOrWhiteSpace(package.Element(Opf + "metadata")!.Element(Dc + "title")!.Value));
        Assert.Null(package.Element(Opf + "metadata")!.Element(Dc + "creator"));
    }

    [Fact]
    public void ThePictures_AreTheFilesAsTheyWere()
    {
        var path = Write(Options(), _files.Page("a"), _files.Page("b", portrait: true));

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(TestImages.LandscapeJpeg(), ReadBytes(zip, "OEBPS/images/photo0001.jpg"));
        Assert.Equal(TestImages.PortraitJpeg(), ReadBytes(zip, "OEBPS/images/photo0002.jpg"));
    }

    private static byte[] ReadBytes(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [Fact]
    public void APage_HasTheTextAbove_ThePhoto_AndTheTextBelow_InThatOrder()
    {
        var path = Write(Options(), _files.Page("a", above: "First line\nSecond line", below: "Under the photo"));

        using var zip = ZipFile.OpenRead(path);
        var page = Xml(zip, "OEBPS/photo0001.xhtml");
        var body = page.Descendants(Xhtml + "div").First(div => div.Attribute("class")?.Value == "page");

        var kinds = body.Elements().Select(element => element.Name.LocalName == "p" ? "p:" + element.Value : element.Name.LocalName + ":" + (element.Element(Xhtml + "img")?.Attribute("src")?.Value ?? "")).ToList();
        Assert.Equal(["p:First line", "p:Second line", "div:images/photo0001.jpg", "p:Under the photo"], kinds);
    }

    [Fact]
    public void TheLook_IsInTheStyleOfTheSpans()
    {
        var document = new RichDocument([
            new RichParagraph(TextAlign.Center, [
                new RichRun("bold", new RichStyle(Bold: true)),
                new RichRun("italic", new RichStyle(Italic: true)),
                new RichRun("both", new RichStyle(Underline: true, Strike: true)),
                new RichRun("big", new RichStyle(Size: 24)),
                new RichRun("small", new RichStyle(Size: 9)),
                new RichRun("red", new RichStyle(Color: 0xCC0000)),
                new RichRun("plain", default),
                new RichRun("twelve", new RichStyle(Size: 12))]),
            new RichParagraph(TextAlign.Right, [new RichRun("right", default)]),
            new RichParagraph(TextAlign.Justify, [new RichRun("justified", default)])]);
        var path = Write(Options(), _files.Page("a", document, RichDocument.Empty));

        using var zip = ZipFile.OpenRead(path);
        var page = Xml(zip, "OEBPS/photo0001.xhtml");
        string StyleOf(string text) => page.Descendants(Xhtml + "span").Single(span => span.Value == text).Attribute("style")!.Value;

        Assert.Contains("font-weight: bold", StyleOf("bold"));
        Assert.Contains("font-style: italic", StyleOf("italic"));
        Assert.Contains("underline", StyleOf("both"));
        Assert.Contains("line-through", StyleOf("both"));
        Assert.Contains("font-size: 2em", StyleOf("big"));
        Assert.Contains("font-size: 0.75em", StyleOf("small"));
        Assert.Contains("color: #CC0000", StyleOf("red"));
        Assert.DoesNotContain(page.Descendants(Xhtml + "span"), span => span.Value is "plain" or "twelve"); // the usual look needs no style

        var paragraphs = page.Descendants(Xhtml + "p").ToList();
        Assert.Equal("text-align: center", paragraphs[0].Attribute("style")!.Value);
        Assert.Equal("text-align: right", paragraphs[1].Attribute("style")!.Value);
        Assert.Equal("text-align: justify", paragraphs[2].Attribute("style")!.Value);
    }

    [Fact]
    public void ALineBreak_IsABr_AndABlankLineInTheMiddle_IsALineOfSpace()
    {
        var document = new RichDocument([
            new RichParagraph(TextAlign.Left, [new RichRun("one\ntwo", default)]),
            new RichParagraph(TextAlign.Left, []),
            new RichParagraph(TextAlign.Left, [new RichRun("three", default)])]);
        var path = Write(Options(), _files.Page("a", document, RichDocument.Empty));

        using var zip = ZipFile.OpenRead(path);
        var paragraphs = Xml(zip, "OEBPS/photo0001.xhtml").Descendants(Xhtml + "p").ToList();

        Assert.Equal(3, paragraphs.Count);
        Assert.Single(paragraphs[0].Descendants(Xhtml + "br"));
        Assert.Equal("one\ntwo".Replace("\n", ""), paragraphs[0].Value);
        Assert.Equal(" ".Replace(" ", "\x00A0"), paragraphs[1].Value);
    }

    [Fact]
    public void Cyrillic_AndMarkupInTheText_AreKeptAsText()
    {
        var path = Write(Options("Літо & <сонце>", "Юрій"), _files.Page("a", above: "Привіт, світе <b>not bold</b> & more", below: "Київ"));

        using var zip = ZipFile.OpenRead(path);
        var page = Xml(zip, "OEBPS/photo0001.xhtml");
        Assert.Equal("Привіт, світе <b>not bold</b> & more", page.Descendants(Xhtml + "p").First().Value);
        Assert.Empty(page.Descendants(Xhtml + "b"));
        Assert.Equal("Літо & <сонце>", Xml(zip, "OEBPS/title.xhtml").Descendants(Xhtml + "h1").Single().Value);
    }

    [Fact]
    public void CharactersXmlCannotHold_AreLeftOut()
    {
        var path = Write(Options(), _files.Page("a", above: "bell" + (char)7 + "and" + (char)1 + "null" + (char)0));

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("belland" + "null", Xml(zip, "OEBPS/photo0001.xhtml").Descendants(Xhtml + "p").First().Value);
    }

    [Fact]
    public void TheContents_NameEachPhotoByTheFirstWordsAboveIt_OrByItsNumber()
    {
        var path = Write(
            Options("Book"),
            _files.Page("a", above: "A day at the sea\nmore"),
            _files.Page("b", above: null, below: "Only below"),
            _files.Page("c"),
            _files.Page("d", above: new string('x', 100)));

        using var zip = ZipFile.OpenRead(path);
        var nav = Xml(zip, "OEBPS/nav.xhtml");
        var entries = nav.Descendants(Xhtml + "a").Select(link => (link.Attribute("href")!.Value, link.Value)).ToList();

        Assert.Equal("title.xhtml", entries[0].Item1);
        Assert.Equal("Book", entries[0].Item2);
        Assert.Equal(("photo0001.xhtml", "A day at the sea"), entries[1]);
        Assert.Equal(("photo0002.xhtml", "Only below"), entries[2]);
        Assert.Equal("photo0003.xhtml", entries[3].Item1);
        Assert.Contains("3", entries[3].Item2); // "Photo 3"
        Assert.True(entries[4].Item2.Length <= 60);
        Assert.EndsWith("…", entries[4].Item2); // a label that is cut ends in an ellipsis
        Assert.Equal("toc", nav.Descendants(Xhtml + "nav").Single().Attributes().Single(attribute => attribute.Name.LocalName == "type").Value);
    }

    [Fact]
    public void TheLanguage_IsOnEveryPage()
    {
        var path = Write(Options("T", language: "uk"), _files.Page("a"));

        using var zip = ZipFile.OpenRead(path);
        foreach (var name in new[] { "OEBPS/title.xhtml", "OEBPS/photo0001.xhtml", "OEBPS/nav.xhtml" })
            Assert.Equal("uk", Xml(zip, name).Root!.Attribute("lang")!.Value);
    }

    [Fact]
    public void ThePhotoHasAnAltText_ItsDescriptionOrItsNumber()
    {
        var path = Write(Options(), _files.Page("holiday"), _files.Page("b") with { Description = "" });

        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("holiday", Xml(zip, "OEBPS/photo0001.xhtml").Descendants(Xhtml + "img").Single().Attribute("alt")!.Value);
        Assert.Contains("2", Xml(zip, "OEBPS/photo0002.xhtml").Descendants(Xhtml + "img").Single().Attribute("alt")!.Value);
    }

    [Fact]
    public void TheFileIsWrittenOverAnOldOne_AndNoPartialFileIsLeft()
    {
        File.WriteAllText(_files.Path_("book.epub"), "old");

        Write(Options(), _files.Page("a"));

        Assert.Equal(["a.jpg", "book.epub"], Directory.GetFiles(_files.Folder).Select(Path.GetFileName).Order());
        using var zip = ZipFile.OpenRead(_files.Path_("book.epub"));
        Assert.NotNull(zip.GetEntry("mimetype"));
    }

    [Fact]
    public void ACancelledBook_LeavesNothingBehind()
    {
        var path = _files.Path_("book.epub");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => EpubBookWriter.Write(path, Options(), [_files.Page("a")], cancellation: cancel.Token));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void ABookWithNoPhotos_IsStillAnEpub()
    {
        var path = Write(Options("Empty"));

        using var zip = ZipFile.OpenRead(path);
        Assert.NotNull(zip.GetEntry("OEBPS/title.xhtml"));
        Xml(zip, "OEBPS/content.opf");
    }

    [Fact]
    public void Progress_GoesUpToAHundred()
    {
        var reported = new Reported();

        EpubBookWriter.Write(_files.Path_("book.epub"), Options(), [_files.Page("a"), _files.Page("b")], reported);

        Assert.Equal(100, reported.Values.Max());
        Assert.Equal(reported.Values.Order(), reported.Values); // it only goes up
    }
}
