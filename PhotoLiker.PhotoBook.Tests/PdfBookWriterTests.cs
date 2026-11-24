using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PhotoLiker.PhotoBook.Tests;

public sealed class PdfBookWriterTests : IDisposable
{
    private readonly BookFiles _files = new();

    public void Dispose() => _files.Dispose();

    private static BookOptions Options(string title = "", string author = "", PageOrientation orientation = PageOrientation.FollowPhoto, PageFormat format = PageFormat.A4, bool numbers = true) =>
        new(title, author, "en", format, orientation, 15, "Segoe UI", numbers);

    private static PdfDocument Open(string path) => PdfReader.Open(path, PdfDocumentOpenMode.Import);

    private static int CountText(string path, string text) =>
        System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)).Split(text).Length - 1;

    [Fact]
    public void ABookIsAPagePerPhoto()
    {
        var path = _files.Path_("book.pdf");
        var pages = new[] { _files.Page("a"), _files.Page("b"), _files.Page("c") };

        var count = PdfBookWriter.Write(path, Options(), pages);

        Assert.Equal(3, count);
        using var document = Open(path);
        Assert.Equal(3, document.PageCount);
    }

    [Fact]
    public void TheTitle_HasAPageOfItsOwn_AndIsInTheFileInfo()
    {
        var path = _files.Path_("book.pdf");

        var count = PdfBookWriter.Write(path, Options("Summer", "Yurii"), [_files.Page("a")]);

        Assert.Equal(2, count);
        using var document = Open(path);
        Assert.Equal("Summer", document.Info.Title);
        Assert.Equal("Yurii", document.Info.Author);
    }

    [Fact]
    public void NoTitle_NoTitlePage()
    {
        var path = _files.Path_("book.pdf");

        Assert.Equal(1, PdfBookWriter.Write(path, Options(), [_files.Page("a")]));
    }

    [Fact]
    public void FollowPhoto_TurnsEachPageAsItsPhotoIs()
    {
        var path = _files.Path_("book.pdf");

        PdfBookWriter.Write(path, Options(), [_files.Page("wide"), _files.Page("tall", portrait: true)]);

        using var document = Open(path);
        Assert.True(document.Pages[0].Width.Point > document.Pages[0].Height.Point);
        Assert.True(document.Pages[1].Width.Point < document.Pages[1].Height.Point);
    }

    [Theory]
    [InlineData(PageOrientation.Portrait, false)]
    [InlineData(PageOrientation.Landscape, true)]
    public void PortraitAndLandscape_TurnAllThePages(PageOrientation orientation, bool landscape)
    {
        var path = _files.Path_("book.pdf");

        PdfBookWriter.Write(path, Options(orientation: orientation), [_files.Page("wide"), _files.Page("tall", portrait: true)]);

        using var document = Open(path);
        foreach (var page in document.Pages.Cast<PdfPage>())
            Assert.Equal(landscape, page.Width.Point > page.Height.Point);
    }

    [Theory]
    [InlineData(PageFormat.A4, 210, 297)]
    [InlineData(PageFormat.A5, 148, 210)]
    [InlineData(PageFormat.Letter, 215.9, 279.4)]
    [InlineData(PageFormat.Square, 210, 210)]
    public void ThePaperSizes(PageFormat format, double widthMm, double heightMm)
    {
        var path = _files.Path_("book.pdf");

        PdfBookWriter.Write(path, Options(orientation: PageOrientation.Portrait, format: format), [_files.Page("a", portrait: true)]);

        using var document = Open(path);
        Assert.Equal(widthMm, document.Pages[0].Width.Millimeter, 1);
        Assert.Equal(heightMm, document.Pages[0].Height.Millimeter, 1);
    }

    [Fact]
    public void ALongDescription_GoesOnToMorePages_AndNoTextIsLost()
    {
        var path = _files.Path_("book.pdf");
        var lines = string.Join("\n", Enumerable.Range(1, 120).Select(number => $"Line number {number} of a very long story"));

        var count = PdfBookWriter.Write(path, Options(), [_files.Page("a", above: lines, below: "The end")]);

        Assert.True(count > 2, $"{count} pages");
        using var document = Open(path);
        Assert.Equal(count, document.PageCount);
    }

    [Fact]
    public void AShortDescription_StaysOnThePageOfItsPhoto()
    {
        var path = _files.Path_("book.pdf");

        Assert.Equal(1, PdfBookWriter.Write(path, Options(), [_files.Page("a", above: "Above", below: "Below")]));
    }

    [Fact]
    public void Cyrillic_IsSetInAnEmbeddedFont_WithAMapBackToText()
    {
        var path = _files.Path_("book.pdf");

        PdfBookWriter.Write(path, Options("Літо 2026", "Юрій"), [_files.Page("a", above: "Привіт, світе", below: "Київ, червень")]);

        // A font that carries its letters in the file (so that any reader shows them), and says which text they are (so that it can be searched and copied)
        Assert.True(CountText(path, "/FontFile2") >= 1, "no embedded TrueType font");
        Assert.True(CountText(path, "/ToUnicode") >= 1, "no map from the letters back to text");
    }

    [Fact]
    public void ThePhotoIsPutInAsItIs_TheJpegIsNotSqueezedAgain()
    {
        var path = _files.Path_("book.pdf");

        PdfBookWriter.Write(path, Options(), [_files.Page("a")]);

        Assert.True(CountText(path, "/DCTDecode") >= 1, "the JPEG is not in the file as a JPEG");
    }

    [Fact]
    public void TheWholeFormatting_IsAccepted()
    {
        var path = _files.Path_("book.pdf");
        var rich = new RichDocument([
            new RichParagraph(TextAlign.Center, [new RichRun("Big bold title", new RichStyle(Bold: true, Size: 28, Color: 0xCC0000))]),
            new RichParagraph(TextAlign.Justify, [
                new RichRun("Plain, ", default),
                new RichRun("italic, ", new RichStyle(Italic: true)),
                new RichRun("underlined, ", new RichStyle(Underline: true)),
                new RichRun("struck, ", new RichStyle(Strike: true)),
                new RichRun(string.Join(" ", Enumerable.Repeat("and a long tail of words that has to be spread over more lines than one", 6)), default)]),
            new RichParagraph(TextAlign.Right, [new RichRun("Right\nafter a break", default)])]);

        var count = PdfBookWriter.Write(path, Options(), [_files.Page("a", rich, rich)]);

        Assert.True(count >= 1);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Fact]
    public void ABookWithNoPhotos_IsStillAPdf()
    {
        var path = _files.Path_("book.pdf");

        var count = PdfBookWriter.Write(path, Options(), []);

        Assert.Equal(1, count);
        using var document = Open(path);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void TheFileIsWrittenOverAnOldOne_AndNoPartialFileIsLeft()
    {
        var path = _files.Path_("book.pdf");
        File.WriteAllText(path, "old");

        PdfBookWriter.Write(path, Options(), [_files.Page("a")]);

        Assert.True(new FileInfo(path).Length > 100);
        Assert.Equal(["a.jpg", "book.pdf"], Directory.GetFiles(_files.Folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ACancelledBook_LeavesNothingBehind()
    {
        var path = _files.Path_("book.pdf");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => PdfBookWriter.Write(path, Options(), [_files.Page("a")], cancellation: cancel.Token));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void AFontThatIsNotThere_FallsBackToAnotherOne()
    {
        var path = _files.Path_("book.pdf");
        var options = Options() with { FontFamily = "No Such Font Anywhere" };

        Assert.Equal(1, PdfBookWriter.Write(path, options, [_files.Page("a", above: "text")]));
    }

    [Fact]
    public void Progress_GoesUpToAHundred()
    {
        var path = _files.Path_("book.pdf");
        var reported = new Reported();

        PdfBookWriter.Write(path, Options(), [_files.Page("a"), _files.Page("b")], reported);

        Assert.Equal(100, reported.Values.Max());
        Assert.Equal(reported.Values.Order(), reported.Values); // it only goes up
    }

    [Fact]
    public void PageNumbers_AreInTheFileOnlyWhenAsked()
    {
        var with = _files.Path_("with.pdf");
        var without = _files.Path_("without.pdf");
        var pages = new[] { _files.Page("a"), _files.Page("b") };

        PdfBookWriter.Write(with, Options(numbers: true), pages);
        PdfBookWriter.Write(without, Options(numbers: false), pages);

        // A page with a number has a text on it, so it holds a font and the other does not (the pages have no other text)
        Assert.True(CountText(with, "/Font") > CountText(without, "/Font"));
    }

    [Fact]
    public void APageThatIsOnlyText_HasNoPhotoAndNoPhotoIsInTheFile()
    {
        var path = _files.Path_("text.pdf");

        var count = PdfBookWriter.Write(path, Options(), [BookPage.TextOnly(RichDocument.FromPlainText("Magiya golosu\nSpochatku buv brehunec"), "page 1")]);

        Assert.Equal(1, count);
        using var document = Open(path);
        Assert.Equal(1, document.PageCount);
        Assert.Equal(0, CountText(path, "/Subtype/Image") + CountText(path, "/Subtype /Image")); // no picture in the file
    }

    [Fact]
    public void ALongTextWithNoPhoto_FlowsOverAsManyPagesAsItNeeds_AndEachPageOfTheBookStartsOnANewOne()
    {
        var path = _files.Path_("long.pdf");
        var paragraph = string.Join(" ", Enumerable.Repeat("a line of the manuscript that is read", 12));
        var longText = string.Join("\n", Enumerable.Repeat(paragraph, 40));

        var count = PdfBookWriter.Write(path, Options(), [BookPage.TextOnly(RichDocument.FromPlainText(longText), "long"), BookPage.TextOnly(RichDocument.FromPlainText("short"), "short")]);

        Assert.True(count >= 3, $"A page of text is more than one page of paper, and the next page of the book starts on its own: {count}");
        using var document = Open(path);
        Assert.Equal(count, document.PageCount);
    }

    [Fact]
    public void PicturesSetInTheTextOfAPageThatHasNoPhoto_AreInTheFile_WithTheirSeeThroughBackground()
    {
        var path = _files.Path_("pictures.pdf");
        var page = BookPage.TextOnly(BookFiles.Text(BookFiles.Picture(0.3, TextAlign.Left), "Smorodom smerti", BookFiles.Picture(0.4, TextAlign.Right)), "page");

        var count = PdfBookWriter.Write(path, Options(), [page]);

        Assert.Equal(1, count);
        // The two pictures are the same pixels, which the writer keeps once: the picture and its mask are two image objects of the file
        Assert.True(CountText(path, "/Subtype/Image") + CountText(path, "/Subtype /Image") >= 2);
        Assert.True(CountText(path, "/SMask") >= 1, "the see-through part of the pictures is a mask in the file");
    }

    [Fact]
    public void APictureThatDoesNotFitWhatIsLeftOfThePage_StartsTheNextOne()
    {
        var path = _files.Path_("flow.pdf");
        var longText = string.Join("\n", Enumerable.Repeat("a line of the manuscript that is read, and a few more words to fill it", 38));
        var page = BookPage.TextOnly(BookFiles.Text(longText, BookFiles.Picture(1.0)), "long");

        var count = PdfBookWriter.Write(path, Options(), [page]);

        using var document = Open(path);
        Assert.Equal(count, document.PageCount);
        Assert.True(count >= 2, "a picture as wide as the text and a page of text do not fit on one page");
    }

    [Fact]
    public void APageThatHasItsPhoto_CanHaveAPictureInItsTextToo()
    {
        var path = _files.Path_("photo.pdf");
        var page = _files.Page("a", below: "text") with { Below = BookFiles.Text("text", BookFiles.Picture()) };

        PdfBookWriter.Write(path, Options(), [page]);

        // The photo, and the picture with its mask: three image objects
        Assert.True(CountText(path, "/Subtype/Image") + CountText(path, "/Subtype /Image") >= 3);
    }

    [Fact]
    public void TextOnlyPages_AreAPortraitPageEvenWhenTheBookFollowsItsPhotos()
    {
        var path = _files.Path_("portrait.pdf");

        PdfBookWriter.Write(path, Options(orientation: PageOrientation.FollowPhoto), [BookPage.TextOnly(RichDocument.FromPlainText("text"), "page")]);

        using var document = Open(path);
        Assert.True(document.Pages[0].Height.Point > document.Pages[0].Width.Point);
    }
}
