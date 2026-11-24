using System.Drawing.Imaging;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The writing of a page in the Scan manuscripts window: the language list (English and Multilanguage as well), the kind of writing and the language each page was scanned in, which are picked again
/// when the page is opened, and the text laid out like the page (indents, tabs between columns) with its rows paired with the boxes on the photo. A stand-in reads the pages: the machines that build the
/// program have no models, and no test leaves a question on the screen.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptWritingTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public ManuscriptWritingTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string Photo(string name)
    {
        var path = _folder.File(name);
        using var bitmap = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(240, 236, 220));

        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private ManuscriptDialog Dialog(IReadOnlyList<string> photos)
    {
        var dialog = new ManuscriptDialog(_folder.Path, photos, new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true));
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => true;
        dialog.AskReplaceText = () => true;
        dialog.AskBookPath = () => null;
        dialog.ScanPicturesCheck.Checked = false; // these tests are about the text
        return dialog;
    }

    private static TextLine L(string text, float left, float y, float width) => new(text, 0.9f, TextRegion.FromCentre(new PointF(left + width / 2, y), width, 24, 0));

    private static ScanResult Simple() => new([L("one", 20, 40, 200), L("two", 20, 80, 200)]);

    /// <summary>Lets the scan of the dialog read what a test says, and records how it was asked to read it.</summary>
    private static List<(ScanKind Kind, ScanLanguage Language)> StandIn(ManuscriptDialog dialog, Func<ScanResult>? result = null)
    {
        var asked = new List<(ScanKind, ScanLanguage)>();
        dialog.ScanOverride = (_, kind, language, _, progress) =>
        {
            asked.Add((kind, language));
            progress?.Report((1, 1));
            return (result ?? Simple)();
        };
        return asked;
    }

    private static string[] Names(ManuscriptDialog dialog) => [.. dialog.LanguageCombo.Items.Cast<object>().Select(item => item.ToString()!)];

    // ── The language list ───────────────────────────────────────────────────

    [Fact]
    public void EveryKindOfWriting_OffersEveryLanguage_EnglishAndMultilanguageToo_AndThePickedLanguageStaysWhenTheKindChanges()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), form =>
        {
            var dialog = (ManuscriptDialog)form;
            string[] expected = [ManuscriptStrings.LanguageUkrainian, ManuscriptStrings.LanguageRussian, ManuscriptStrings.LanguageEnglish, ManuscriptStrings.LanguageMultilingual];

            Assert.Equal(ScanKind.Handwriting, dialog.Kind);
            Assert.Equal(expected, Names(dialog));
            Assert.Equal(ScanLanguage.Ukrainian, dialog.Language);

            dialog.LanguageCombo.SelectedIndex = 3; // Multilanguage
            dialog.KindCombo.SelectedIndex = 1;     // printed
            Assert.Equal(ScanKind.Printed, dialog.Kind);
            Assert.Equal(expected, Names(dialog));
            Assert.Equal(ScanLanguage.Multilingual, dialog.Language);

            dialog.LanguageCombo.SelectedIndex = 2; // English
            dialog.KindCombo.SelectedIndex = 0;     // handwriting reads English now (the Ukrainian network knows the Latin letters)
            Assert.Equal(ScanLanguage.English, dialog.Language);
            Assert.Equal(expected, Names(dialog));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(ScanKind.Handwriting, ScanLanguage.English)]
    [InlineData(ScanKind.Handwriting, ScanLanguage.Multilingual)]
    [InlineData(ScanKind.Printed, ScanLanguage.English)]
    [InlineData(ScanKind.Printed, ScanLanguage.Multilingual)]
    public void APageIsScannedInTheLanguageThatIsPicked_AndRemembersIt(ScanKind kind, ScanLanguage language)
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            dialog.KindCombo.SelectedIndex = kind == ScanKind.Handwriting ? 0 : 1;
            dialog.SelectLanguage(language);

            await dialog.ScanPageAsync();

            Assert.Equal([(kind, language)], asked);
            Assert.Equal(kind.ToString(), dialog.Pages[0].Kind);
            Assert.Equal(language.ToString(), dialog.Pages[0].Language);
        });
    }

    // ── What a page was scanned in ──────────────────────────────────────────

    [Fact]
    public void WhenAPageIsOpened_TheKindOfWritingAndTheLanguageItWasScannedInArePickedAgain()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");

        Ui.Run(() => Dialog([a, b, c]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);

            await dialog.ScanPageAsync(); // page 1: handwriting, Ukrainian
            dialog.SelectPage(1);
            dialog.KindCombo.SelectedIndex = 1; // printed
            dialog.SelectLanguage(ScanLanguage.English);
            await dialog.ScanPageAsync();       // page 2: printed, English
            dialog.SelectPage(2);
            Assert.Equal((ScanKind.Printed, ScanLanguage.English), (dialog.Kind, dialog.Language)); // never scanned: the boxes stay as they are

            dialog.SelectPage(0);
            Assert.Equal((ScanKind.Handwriting, ScanLanguage.Ukrainian), (dialog.Kind, dialog.Language));
            dialog.SelectPage(1);
            Assert.Equal((ScanKind.Printed, ScanLanguage.English), (dialog.Kind, dialog.Language));
            dialog.SelectPage(0);
            Assert.Equal((ScanKind.Handwriting, ScanLanguage.Ukrainian), (dialog.Kind, dialog.Language));
            Assert.Equal(ManuscriptStrings.LanguageUkrainian, dialog.LanguageCombo.Text);
        });
    }

    [Fact]
    public void ChangingTheBoxesOnAPage_DoesNotChangeWhatItWasScannedIn_UntilItIsScannedAgain()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            await dialog.ScanPageAsync(); // Ukrainian
            dialog.SelectLanguage(ScanLanguage.Russian);
            Assert.Equal(ScanLanguage.Ukrainian.ToString(), dialog.Pages[0].Language);

            dialog.SelectPage(1);
            dialog.SelectPage(0);
            Assert.Equal(ScanLanguage.Ukrainian, dialog.Language); // what the page was scanned in, not what was picked since

            dialog.SelectLanguage(ScanLanguage.Russian);
            await dialog.ScanPageAsync(); // asked, for it has text: answered yes
            Assert.Equal(ScanLanguage.Russian.ToString(), dialog.Pages[0].Language);
            Assert.Equal(ScanLanguage.Russian, asked[^1].Language);
        });
    }

    [Fact]
    public void TheLanguageOfEachPage_IsKeptWithTheFolder_AndPickedAgainWhenTheWindowIsOpenedAnotherDay()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.SelectLanguage(ScanLanguage.Russian);
            await dialog.ScanPageAsync();
            dialog.SelectPage(1);
            dialog.SelectLanguage(ScanLanguage.Multilingual);
            await dialog.ScanPageAsync();
        });

        var stored = ManuscriptFile.Load(_folder.Path)!;
        Assert.Equal(["Russian", "Multilingual"], stored.Pages!.Select(page => page.Language));

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.SelectPage(1);
            Assert.Equal(ScanLanguage.Multilingual, dialog.Language);
            dialog.SelectPage(0);
            Assert.Equal(ScanLanguage.Russian, dialog.Language);
        });
    }

    [Fact]
    public void AllPagesScan_ReadsEachPageWithTheLanguageThatIsPicked_AndEachRemembersIt()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            dialog.SelectLanguage(ScanLanguage.Multilingual);

            await dialog.ScanAllAsync();

            Assert.Equal([(ScanKind.Handwriting, ScanLanguage.Multilingual), (ScanKind.Handwriting, ScanLanguage.Multilingual)], asked);
            Assert.All(dialog.Pages, page => Assert.Equal("Multilingual", page.Language));
        });
    }

    // ── The text laid out like the page ─────────────────────────────────────

    /// <summary>A title, a paragraph whose first line starts further in, a line at the margin, and a table of two columns, on a photo of 320 by 240.</summary>
    private static ScanResult Page() => new(
    [
        L("Title", 20, 30, 280),
        L("Indented first line of a paragraph", 50, 70, 250),
        L("second line at the margin", 20, 100, 280),
        L("cat", 20, 150, 60), L("кіт", 170, 151, 60),
        L("dog", 20, 180, 60), L("пес", 170, 181, 60),
    ]);

    private static Point PointOf(ManuscriptView view, float photoX, float photoY)
    {
        var scale = view.ZoomOfPhoto;
        var originX = (view.ClientSize.Width - 320 * scale) / 2f;
        var originY = (view.ClientSize.Height - 240 * scale) / 2f;
        return new Point((int)Math.Round(originX + photoX * scale), (int)Math.Round(originY + photoY * scale));
    }

    private static void Click(ManuscriptDialog dialog, float photoX, float photoY) =>
        typeof(ManuscriptView).GetMethod("ClickAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(dialog.View, [PointOf(dialog.View, photoX, photoY)]);

    [Fact]
    public void TheScannedText_IsIndentedAsThePageIs_AndHasATabBetweenTheColumnsOfATable()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog, Page);

            await dialog.ScanPageAsync();

            Assert.Equal("Title\nIndented first line of a paragraph\nsecond line at the margin\ncat\tкіт\ndog\tпес", dialog.Editor.PlainText.Replace("\r", string.Empty));
            var document = RtfParser.Parse(dialog.Editor.Rtf);
            Assert.Equal(5, document.Paragraphs.Count);
            Assert.Equal(0, document.Paragraphs[0].LeftIndent);
            Assert.Equal(48, document.Paragraphs[1].LeftIndent); // 30 of the 280 pixels of the text are 46 points of the 432 of the reference column, as a multiple of 4
            Assert.Equal(0, document.Paragraphs[2].LeftIndent);
            var stop = Assert.Single(document.Paragraphs[3].TabStops);
            Assert.Equal(231, stop, 0);
            Assert.Equal(document.Paragraphs[3].TabStops, document.Paragraphs[4].TabStops); // the same column in every row

            // What the scan read is that text: nothing was changed by the person
            Assert.Equal(PageState.Scanned, ManuscriptText.StateOf(dialog.Pages[0]));
            Assert.Equal(7, dialog.View.Lines.Count); // and each line of the scan has its box on the photo
        });
    }

    [Fact]
    public void ARowOfTwoColumns_IsOneLineOfTheText_ClickingEitherBoxSelectsItAndTheCaretSelectsItsFirstBox()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog, Page);
            await dialog.ScanPageAsync();

            Click(dialog, 200, 151); // the box of the translation, the 5th line of the scan
            Assert.Equal("cat\tкіт", dialog.Editor.Box.SelectedText);
            Click(dialog, 50, 150); // the box of the word
            Assert.Equal("cat\tкіт", dialog.Editor.Box.SelectedText);
            Click(dialog, 160, 70); // the second row
            Assert.Equal("Indented first line of a paragraph", dialog.Editor.Box.SelectedText);

            var text = dialog.Editor.PlainText;
            dialog.Editor.Box.Select(text.IndexOf("пес", StringComparison.Ordinal) + 1, 0);
            Assert.Equal(5, dialog.View.SelectedLine); // the box of "dog", the first of its row
            dialog.Editor.Box.Select(text.IndexOf("Title", StringComparison.Ordinal) + 1, 0);
            Assert.Equal(0, dialog.View.SelectedLine);
        });
    }

    [Fact]
    public void ThePageOpenedAgain_HasItsRowsPairedWithItsBoxesStill_AsTheTextWasLaidOutFromTheStoredLines()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog, Page);
            await dialog.ScanPageAsync();
            dialog.SelectPage(1);
            Assert.True(await Ui.Until(() => dialog.View.Lines.Count == 0 && dialog.View.HasImage));
            dialog.SelectPage(0);
            Assert.True(await Ui.Until(() => dialog.View.Lines.Count == 7));

            Click(dialog, 200, 181);
            Assert.Equal("dog\tпес", dialog.Editor.Box.SelectedText);
        });
    }

    [Fact]
    public void ARowThatHasMoreRoomAbove_IsAfterABlankLine_AndTheBlankLineIsNoRowOfTheText()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog, () => new ScanResult([L("one", 20, 20, 200), L("two", 20, 50, 200), L("three", 20, 80, 200), L("four", 20, 170, 200), L("five", 20, 200, 200)]));

            await dialog.ScanPageAsync();

            Assert.Equal("one\ntwo\nthree\n\nfour\nfive", dialog.Editor.PlainText.Replace("\r", string.Empty));
            Click(dialog, 100, 170);
            Assert.Equal("four", dialog.Editor.Box.SelectedText);
        });
    }
}
