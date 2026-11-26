using System.Drawing.Imaging;
using System.IO.Compression;
using PdfSharp.Pdf.IO;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI.Tests;

/// <summary>Making a book of real photos: the pictures are prepared (turned upright, made the size the book needs) and put in a PDF or an EPUB with their texts, and a photo that cannot be read is left out.</summary>
public sealed class BookBuilderTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>A JPEG of a size, the left half red and the right half blue.</summary>
    private string Photo(string name, int width = 320, int height = 200)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillRectangle(Brushes.Crimson, 0, 0, width / 2, height);
            graphics.FillRectangle(Brushes.RoyalBlue, width / 2, 0, width - width / 2, height);
        }

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static BookOptions Options(string title = "", BookFormat format = BookFormat.Pdf) => new(title, "", "en");

    private Task<BookResult> Build(IReadOnlyList<BookEntry> entries, BookFormat format, string title = "", PictureQuality quality = PictureQuality.Good, CancellationToken cancellation = default,
        Action<int, int>? preparing = null, Action<int>? percent = null, string? output = null, bool withPhotos = true) =>
        BookBuilder.BuildAsync(entries, format, Options(title), quality, _folder.File(output ?? (format == BookFormat.Pdf ? "book.pdf" : "book.epub")), preparing, null, percent, cancellation, withPhotos);

    [Fact]
    public async Task APdf_HasAPagePerPhoto_InTheOrderOfTheList()
    {
        var entries = new List<BookEntry> { new(Photo("a.jpg")), new(Photo("b.jpg", 200, 320)), new(Photo("c.jpg")) };

        var result = await Build(entries, BookFormat.Pdf);

        Assert.Equal(3, result.Photos);
        Assert.Equal(3, result.Pages);
        Assert.Empty(result.Unreadable);
        Assert.True(result.Bytes > 1000);
        using var document = PdfReader.Open(result.Path, PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
        // Pages follow their photos: landscape, portrait, landscape
        Assert.True(document.Pages[0].Width.Point > document.Pages[0].Height.Point);
        Assert.True(document.Pages[1].Width.Point < document.Pages[1].Height.Point);
    }

    [Fact]
    public async Task AnEpub_HasThePhotosAndTheirTexts_InTheOrderOfTheList()
    {
        var first = new BookEntry(Photo("a.jpg")) { AboveRtf = @"{\rtf1\ansi first\par}" };
        var second = new BookEntry(Photo("b.jpg")) { BelowRtf = @"{\rtf1\ansi second\par}" };

        var result = await Build([second, first], BookFormat.Epub, title: "T");

        Assert.Equal(2, result.Photos);
        Assert.Equal(3, result.Pages); // with the page of the title
        using var zip = ZipFile.OpenRead(result.Path);
        string Page(string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();
        Assert.Contains("second", Page("OEBPS/photo0001.xhtml"));
        Assert.Contains("first", Page("OEBPS/photo0002.xhtml"));
    }

    [Fact]
    public async Task APhotoThatIsLeftOut_IsNotInTheBook()
    {
        var entries = new List<BookEntry> { new(Photo("a.jpg")), new(Photo("b.jpg")) { LeftOut = true }, new(Photo("c.jpg")) };

        var result = await Build(entries, BookFormat.Pdf);

        Assert.Equal(2, result.Photos);
        Assert.Empty(result.Unreadable);
    }

    [Fact]
    public async Task APhotoThatCannotBeRead_IsLeftOut_AndNamed_AndTheRestIsMade()
    {
        var bad = _folder.File("bad.jpg");
        File.WriteAllText(bad, "this is not a picture");
        var gone = _folder.File("gone.jpg");
        var entries = new List<BookEntry> { new(Photo("a.jpg")), new(bad), new(gone), new(Photo("c.jpg")) };

        var result = await Build(entries, BookFormat.Pdf);

        Assert.Equal(2, result.Photos);
        Assert.Equal([bad, gone], result.Unreadable);
        using var document = PdfReader.Open(result.Path, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }

    // ── A page that is its photo ────────────────────────────────────────────

    private static string PageOf(ZipArchive zip, string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();

    [Fact]
    public async Task APageThatIsItsPhoto_IsInABookOfTextOnly_WithItsWholePhoto_AndNoText()
    {
        var text = new BookEntry(Photo("a.jpg")) { AboveRtf = @"{\rtf1\ansi the text of the first page\par}" };
        var photo = new BookEntry(Photo("b.jpg", 300, 180)) { AboveRtf = @"{\rtf1\ansi hidden words\par}", BelowRtf = @"{\rtf1\ansi more hidden words\par}", AsPhoto = true };
        var last = new BookEntry(Photo("c.jpg")) { AboveRtf = @"{\rtf1\ansi the text of the last page\par}" };

        var result = await Build([text, photo, last], BookFormat.Epub, withPhotos: false);

        Assert.Equal(3, result.Photos);
        Assert.Empty(result.Unreadable);
        using var zip = ZipFile.OpenRead(result.Path);
        Assert.Contains("the text of the first page", PageOf(zip, "OEBPS/photo0001.xhtml"));
        Assert.Contains("the text of the last page", PageOf(zip, "OEBPS/photo0003.xhtml"));
        var middle = PageOf(zip, "OEBPS/photo0002.xhtml");
        Assert.DoesNotContain("hidden words", middle);
        Assert.Contains("images/photo0002.jpg", middle);
        Assert.Equal(["OEBPS/images/photo0002.jpg"], zip.Entries.Select(entry => entry.FullName).Where(name => name.StartsWith("OEBPS/images/", StringComparison.Ordinal)).ToArray()); // only the page that is its photo has a picture
        using var picture = Image.FromStream(new MemoryStream(ReadAll(zip.GetEntry("OEBPS/images/photo0002.jpg")!)));
        Assert.Equal(new Size(300, 180), picture.Size); // whole, as it is
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [Fact]
    public async Task APdfOfTextOnly_HasThePhotoPageAsALandscapePage_BetweenThePagesOfText()
    {
        var entries = new List<BookEntry>
        {
            new(Photo("a.jpg")) { AboveRtf = @"{\rtf1\ansi first\par}" },
            new(Photo("b.jpg", 320, 200)) { AsPhoto = true },
            new(Photo("c.jpg")) { AboveRtf = @"{\rtf1\ansi third\par}" },
        };

        var result = await Build(entries, BookFormat.Pdf, withPhotos: false);

        Assert.Equal(3, result.Pages);
        using var document = PdfReader.Open(result.Path, PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
        Assert.True(document.Pages[0].Width.Point < document.Pages[0].Height.Point, "text: portrait");
        Assert.True(document.Pages[1].Width.Point > document.Pages[1].Height.Point, "the photo: as the photo is");
    }

    [Fact]
    public async Task InABookWithThePhotos_APageThatIsItsPhoto_HasNoTextOnIt()
    {
        var plain = new BookEntry(Photo("a.jpg")) { BelowRtf = @"{\rtf1\ansi below the first photo\par}" };
        var alone = new BookEntry(Photo("b.jpg")) { BelowRtf = @"{\rtf1\ansi below the second photo\par}", AsPhoto = true };

        var result = await Build([plain, alone], BookFormat.Epub);

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.Contains("below the first photo", PageOf(zip, "OEBPS/photo0001.xhtml"));
        Assert.DoesNotContain("below the second photo", PageOf(zip, "OEBPS/photo0002.xhtml"));
        Assert.NotNull(zip.GetEntry("OEBPS/images/photo0002.jpg"));
    }

    [Fact]
    public async Task OnlyThePhotosThatAreWanted_AreReadForABookOfText_AndOneThatCannotBeRead_IsNamed()
    {
        var bad = _folder.File("bad.jpg");
        File.WriteAllText(bad, "this is not a picture");
        var entries = new List<BookEntry>
        {
            new(bad) { AboveRtf = @"{\rtf1\ansi text of a page whose photo is not read\par}" }, // not read: the book is text
            new(Photo("b.jpg")) { AsPhoto = true },
            new(bad) { AsPhoto = true }, // wanted, and cannot be read
        };
        var prepared = new List<(int, int)>();

        var result = await Build(entries, BookFormat.Epub, withPhotos: false, preparing: (done, total) => { lock (prepared) prepared.Add((done, total)); });

        Assert.Equal([bad], result.Unreadable);
        Assert.Equal(2, result.Photos);
        Assert.All(prepared, step => Assert.Equal(2, step.Item2)); // two photos are made into pictures, not three
        using var zip = ZipFile.OpenRead(result.Path);
        Assert.Contains("text of a page whose photo is not read", PageOf(zip, "OEBPS/photo0001.xhtml"));
    }

    [Fact]
    public async Task ABookOfTextOnly_WithNoPageThatIsItsPhoto_StillReadsNoPhoto()
    {
        var gone = _folder.File("gone.jpg"); // not there: a book of text never opens it
        var entries = new List<BookEntry> { new(gone) { AboveRtf = @"{\rtf1\ansi only text\par}" } };

        var result = await Build(entries, BookFormat.Epub, withPhotos: false);

        Assert.Empty(result.Unreadable);
        Assert.Equal(1, result.Photos);
    }

    [Fact]
    public async Task TheQuality_SetsTheSizeOfThePictures()
    {
        var entries = new List<BookEntry> { new(Photo("big.jpg", 4000, 2500)) };

        var screen = await Build(entries, BookFormat.Epub, quality: PictureQuality.Screen, output: "screen.epub");
        var print = await Build(entries, BookFormat.Epub, quality: PictureQuality.Print, output: "print.epub");

        static Size ImageIn(string epub)
        {
            using var zip = ZipFile.OpenRead(epub);
            using var stream = zip.GetEntry("OEBPS/images/photo0001.jpg")!.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            using var image = Image.FromStream(memory);
            return image.Size;
        }

        Assert.Equal(new Size(1600, 1000), ImageIn(screen.Path));
        Assert.Equal(new Size(3600, 2250), ImageIn(print.Path));
        Assert.True(screen.Bytes < print.Bytes);
    }

    [Fact]
    public async Task AWorkThatIsCancelled_LeavesNoBook_AndNoTemporaryFiles()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "PhotoLikerBook_*").Length;
        var entries = new List<BookEntry> { new(Photo("a.jpg")), new(Photo("b.jpg")) };
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Build(entries, BookFormat.Pdf, cancellation: cancel.Token));

        Assert.False(File.Exists(_folder.File("book.pdf")));
        Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "PhotoLikerBook_*").Length);
    }

    [Fact]
    public async Task AfterABook_NoTemporaryFolderIsLeft()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "PhotoLikerBook_*").Length;

        await Build([new BookEntry(Photo("a.jpg"))], BookFormat.Epub);

        Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "PhotoLikerBook_*").Length);
    }

    [Fact]
    public async Task TheProgress_IsReported_UpToAHundred()
    {
        var entries = Enumerable.Range(1, 6).Select(i => new BookEntry(Photo($"p{i}.jpg"))).ToList();
        var percents = new List<int>();
        var prepared = new List<int>();

        await Build(entries, BookFormat.Pdf, preparing: (done, total) =>
        {
            lock (prepared)
                prepared.Add(done * 100 + total);
        }, percent: value =>
        {
            lock (percents)
                percents.Add(value);
        });

        Assert.Equal(100, percents.Max());
        Assert.All(percents, value => Assert.InRange(value, 0, 100));
        Assert.Equal(6, prepared.Count);
        Assert.Contains(606, prepared); // 6 of 6
    }

    [Fact]
    public async Task ANoteInRtf_ReachesThePdfAndTheEpub()
    {
        var rtf = @"{\rtf1\ansi{\fonttbl{\f0 Segoe UI;}}\pard\qc\b Title\b0\par}";
        var entries = new List<BookEntry> { new(Photo("a.jpg")) { AboveRtf = rtf } };

        var pdf = await Build(entries, BookFormat.Pdf);
        var epub = await Build(entries, BookFormat.Epub);

        Assert.True(pdf.Bytes > 0);
        using var zip = ZipFile.OpenRead(epub.Path);
        var page = new StreamReader(zip.GetEntry("OEBPS/photo0001.xhtml")!.Open()).ReadToEnd();
        Assert.Contains("font-weight: bold", page);
        Assert.Contains("text-align: center", page);
    }

    [Fact]
    public async Task APhotoThatIsTurnedInTheBook_IsTurnedInTheFile_AndTheFileOfThePhotoIsNot()
    {
        var path = Photo("wide.jpg", 400, 200);
        var before = File.ReadAllBytes(path);
        var entries = new List<BookEntry> { new(path) { Rotation = 90 }, new(Photo("plain.jpg", 400, 200)) };

        var pdf = await Build(entries, BookFormat.Pdf);
        var epub = await Build(entries, BookFormat.Epub);

        // On its side: a landscape photo is a portrait page; the one that is not turned stays landscape
        using var document = PdfReader.Open(pdf.Path, PdfDocumentOpenMode.Import);
        Assert.True(document.Pages[0].Width.Point < document.Pages[0].Height.Point);
        Assert.True(document.Pages[1].Width.Point > document.Pages[1].Height.Point);

        using var zip = ZipFile.OpenRead(epub.Path);
        Size SizeOf(string name)
        {
            using var stream = zip.GetEntry(name)!.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            using var image = Image.FromStream(memory);
            return image.Size;
        }
        Assert.Equal(new Size(200, 400), SizeOf("OEBPS/images/photo0001.jpg"));
        Assert.Equal(new Size(400, 200), SizeOf("OEBPS/images/photo0002.jpg"));
        Assert.Equal(before, File.ReadAllBytes(path)); // the photo itself is as it was
    }
}
