namespace PhotoLiker.PhotoBook.Tests;

public class PdfFontsTests
{
    [Theory]
    [InlineData("Segoe UI (TrueType)", "Segoe UI", false, false)]
    [InlineData("Segoe UI Bold (TrueType)", "Segoe UI", true, false)]
    [InlineData("Segoe UI Italic (TrueType)", "Segoe UI", false, true)]
    [InlineData("Segoe UI Bold Italic (TrueType)", "Segoe UI", true, true)]
    [InlineData("Times New Roman Italic (TrueType)", "Times New Roman", false, true)]
    [InlineData("Arial Bold Italic (OpenType)", "Arial", true, true)]
    [InlineData("Georgia Regular (TrueType)", "Georgia", false, false)]
    [InlineData("Courier New Bold Oblique (TrueType)", "Courier New", true, true)]
    [InlineData("Segoe UI Light (TrueType)", "Segoe UI Light", false, false)]      // a weight of its own is a family of its own
    [InlineData("  Verdana   (TrueType)", "Verdana", false, false)]
    public void ANameOfTheFontsOfWindows_IsTheFamilyAndTheFace(string registryName, string family, bool bold, bool italic) =>
        Assert.Equal((family, bold, italic), PdfFonts.ParseName(registryName));

    [Theory]
    [InlineData("Cambria & Cambria Math (TrueType)")] // a collection in one file
    [InlineData("Segoe UI")]                          // no kind: not a font file entry
    [InlineData("Wingdings (Type 1)")]
    [InlineData("(TrueType)")]
    [InlineData("")]
    public void ANameThatIsNoSingleFace_IsNone(string registryName) => Assert.Null(PdfFonts.ParseName(registryName));

    [Fact]
    public void TheFontsOfTheDialog_AreInstalledOnes_InTheOrderOfTheList()
    {
        var available = PdfFonts.Available();

        Assert.Contains("Arial", available);
        Assert.Equal(available.Count, available.Distinct().Count());
        Assert.All(available, family => Assert.True(PdfFonts.IsInstalled(family), family));
        var inTheOrderOfTheList = available.OrderBy(family => Array.IndexOf(["Segoe UI", "Arial", "Calibri", "Cambria", "Candara", "Constantia", "Corbel", "Georgia", "Palatino Linotype", "Book Antiqua", "Times New Roman", "Tahoma", "Trebuchet MS", "Verdana", "Courier New", "Consolas", "Comic Sans MS"], family));
        Assert.Equal(inTheOrderOfTheList, available);
    }

    [Fact]
    public void AFontThatIsNotThere_IsNotInstalled()
    {
        Assert.False(PdfFonts.IsInstalled("No Such Font Anywhere"));
        Assert.False(PdfFonts.IsInstalled(""));
        Assert.False(PdfFonts.IsInstalled("   "));
    }

    [Fact]
    public void EveryFontOfTheList_MakesAPdf_WithItsOwnFaces()
    {
        using var files = new BookFiles();
        var rich = new RichDocument([new RichParagraph(TextAlign.Left, [
            new RichRun("Plain ", default), new RichRun("bold ", new RichStyle(Bold: true)), new RichRun("italic ", new RichStyle(Italic: true)),
            new RichRun("both Привіт", new RichStyle(Bold: true, Italic: true))])]);

        foreach (var family in PdfFonts.Available())
        {
            var path = files.Path_(family.Replace(' ', '_') + ".pdf");

            var count = PdfBookWriter.Write(path, new BookOptions("", "", "en", FontFamily: family), [files.Page("p", rich, rich)]);

            Assert.Equal(1, count);
            Assert.True(new FileInfo(path).Length > 1000, family);
        }
    }

    /// <summary>The names of the fonts in a PDF: where they are written (<c>/BaseFont /ABCDEF+Segoe#20UI,Bold</c>; a subset has a tag of six letters in front of its name, and a space is written as #20).</summary>
    private static string FontNames(string path) =>
        string.Join(" ", System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)), @"/BaseFont\s*/\w{6}\+([^\s/>\]]+)").Select(match => match.Groups[1].Value));

    [Fact]
    public void ASegoeUiBook_IsSetInSegoeUi_NotInTheFallback()
    {
        if (!PdfFonts.IsInstalled("Segoe UI"))
            return; // a computer without it (not Windows 10 or 11): the other fonts are tested above

        using var files = new BookFiles();
        var segoe = files.Path_("segoe.pdf");
        var arial = files.Path_("arial.pdf");
        var rich = RichDocument.FromPlainText("Some text");

        PdfBookWriter.Write(segoe, new BookOptions("", "", "en", FontFamily: "Segoe UI"), [files.Page("p", rich, rich)]);
        PdfBookWriter.Write(arial, new BookOptions("", "", "en", FontFamily: "Arial"), [files.Page("p", rich, rich)]);

        Assert.Contains("Segoe#20UI", FontNames(segoe));
        Assert.DoesNotContain("Segoe", FontNames(arial));
        Assert.Contains("Arial", FontNames(arial));
    }
}
