namespace PhotoLiker.PhotoBook.Tests;

public class RtfWriterTests
{
    private static RichDocument RoundTrip(RichDocument document) => RtfParser.Parse(RtfWriter.Write(document));

    [Fact]
    public void ANoDocument_IsNoRtfText()
    {
        Assert.True(RoundTrip(RichDocument.Empty).IsEmpty);
        Assert.StartsWith(@"{\rtf1", RtfWriter.Write(RichDocument.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextAndItsLook_AreReadBackAsTheyWereWritten()
    {
        var document = new RichDocument([
            new RichParagraph(TextAlign.Center, [new RichRun("Title ", new RichStyle(Bold: true, Size: 20)), new RichRun("and more", new RichStyle(Italic: true, Underline: true, Strike: true, Color: 0xC81E1E))]),
            new RichParagraph(TextAlign.Right, [new RichRun("plain", default)]),
            new RichParagraph(TextAlign.Justify, []),
            new RichParagraph(TextAlign.Left, [new RichRun("last", new RichStyle(Color: 0x1E5AB4))])]);

        var back = RoundTrip(document);

        Assert.Equal(4, back.Paragraphs.Count);
        Assert.Equal([TextAlign.Center, TextAlign.Right, TextAlign.Justify, TextAlign.Left], back.Paragraphs.Select(paragraph => paragraph.Align));
        Assert.Equal(document.PlainText, back.PlainText);

        var first = back.Paragraphs[0].Runs;
        Assert.Equal(new RichStyle(Bold: true, Size: 20), first[0].Style);
        Assert.Equal(new RichStyle(Italic: true, Underline: true, Strike: true, Size: 12, Color: 0xC81E1E), first[1].Style);
        Assert.Equal(12, back.Paragraphs[1].Runs[0].Style.PointSize); // a text of no size of its own is written in the size of the book and read back as that
        Assert.Equal(0x1E5AB4, back.Paragraphs[3].Runs[0].Style.Color);
    }

    [Fact]
    public void TheCharactersOfEveryLanguage_TheBracesTheBackslashAndTheTab_SurviveTheTrip()
    {
        var text = "Привіт, їжак! Ґанок \\ { } \t tab\nnew line — “quoted” 日本 😀";
        var document = new RichDocument([new RichParagraph(TextAlign.Left, [new RichRun(text, default)])]);

        var back = RoundTrip(document);

        Assert.Equal(text, back.PlainText);
    }

    [Fact]
    public void AnIndentTheFirstLineAndTheTabStops_AreKept()
    {
        var document = new RichDocument([
            new RichParagraph(TextAlign.Left, [new RichRun("a\tb", default)]) { LeftIndent = 36, FirstLineIndent = -18, TabStops = [100, 250.5] },
            new RichParagraph(TextAlign.Left, [new RichRun("c", default)])]);

        var back = RoundTrip(document);

        Assert.Equal((36.0, -18.0), (back.Paragraphs[0].LeftIndent, back.Paragraphs[0].FirstLineIndent));
        Assert.Equal([100.0, 250.5], back.Paragraphs[0].TabStops);
        Assert.Equal((0.0, 0.0), (back.Paragraphs[1].LeftIndent, back.Paragraphs[1].FirstLineIndent)); // the next paragraph starts from \pard
        Assert.Empty(back.Paragraphs[1].TabStops);
    }

    [Fact]
    public void APicture_IsKeptByteForByte_WithItsWidthAndItsPlace()
    {
        var png = TestImages.SeeThroughPng(40, 30);
        var jpeg = TestImages.LandscapeJpeg();
        var document = new RichDocument([
            new RichParagraph(TextAlign.Left, [new RichRun("before", default)]),
            RichParagraph.OfPicture(RichPicture.FromFile(png, 0.3)!, TextAlign.Right),
            RichParagraph.OfPicture(RichPicture.FromFile(jpeg, 0.8)!),
            new RichParagraph(TextAlign.Left, [new RichRun("after", default)])]);

        var back = RoundTrip(document);

        Assert.Equal(4, back.Paragraphs.Count);
        Assert.Equal(png, back.Paragraphs[1].Picture!.Data);
        Assert.Equal(0.3, back.Paragraphs[1].Picture!.WidthShare, 3);
        Assert.Equal(TextAlign.Right, back.Paragraphs[1].Align);
        Assert.Equal(jpeg, back.Paragraphs[2].Picture!.Data);
        Assert.False(back.Paragraphs[2].Picture!.IsPng);
        Assert.Equal(0.8, back.Paragraphs[2].Picture!.WidthShare, 3);
        Assert.Equal("after", back.Paragraphs[3].PlainText);
    }

    [Fact]
    public void ThePicturesCanBeTakenOut_AndThePlainTextStaysTheSame()
    {
        var document = new RichDocument([
            RichParagraph.OfPicture(RichPicture.FromFile(TestImages.SeeThroughPng(), 0.5)!),
            new RichParagraph(TextAlign.Left, [new RichRun("text", default)])]);

        Assert.True(document.HasPictures);
        var without = document.WithoutPictures();

        Assert.False(without.HasPictures);
        Assert.Equal("text", Assert.Single(without.Paragraphs).PlainText);
        Assert.Same(without, without.WithoutPictures());
    }

    [Fact]
    public void ADocumentOfOnlyAPicture_IsNotEmpty()
    {
        var document = new RichDocument([RichParagraph.OfPicture(RichPicture.FromFile(TestImages.SeeThroughPng(), 0.5)!)]);

        Assert.False(document.IsEmpty);
        Assert.Single(document.WithoutTrailingBlank().Paragraphs);
    }

    [Fact]
    public void TheSizeOfAPictureIsReadFromItsBytes_AndOnlyAPngOrAJpegIsAPicture()
    {
        Assert.Equal((40, 30), SizeOf(RichPicture.FromFile(TestImages.SeeThroughPng(40, 30), 0.5)));
        Assert.Equal((TestImages.PortraitWidth, TestImages.PortraitHeight), SizeOf(RichPicture.FromFile(TestImages.PortraitJpeg(), 0.5)));
        Assert.Null(RichPicture.FromFile([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 0.5));
        Assert.Null(RichPicture.FromFile([], 0.5));
        Assert.Null(RichPicture.FromFile(TestImages.SeeThroughPng()[..20], 0.5)); // a PNG that stops in its header

        static (int, int) SizeOf(RichPicture? picture) => (picture!.PixelWidth, picture.PixelHeight);
    }
}
