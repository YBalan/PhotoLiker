using System.Drawing.Imaging;
using System.IO.Compression;
using PhotoLiker.Core;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Pages;
using PhotoLiker.Stickers;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Scan manuscripts window, as a person uses it: pages, the kind of writing and the language, scanning (with a stand-in for the models, which the machines that build the program do not
/// have), the photo and the text side by side, turning the photo, the work that is kept with the folder, and the book. No test leaves a question on the screen: each answers its own.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptDialogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public ManuscriptDialogTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string Photo(string name, int width = 320, int height = 240, bool png = false)
    {
        var path = _folder.File(name);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(240, 236, 220));
            graphics.DrawString("Hello", new Font("Segoe UI", 20), Brushes.Black, 20, 20);
        }

        bitmap.Save(path, png ? ImageFormat.Png : ImageFormat.Jpeg);
        var date = new DateTime(2021, 3, 4, 10, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, date);
        File.SetLastWriteTimeUtc(path, date);
        return path;
    }

    private ManuscriptDialog Dialog(IReadOnlyList<string> photos, Func<ScanKind, ScanLanguage, Task<bool>>? ensure = null, Action<string, int?>? changed = null, Func<Task<bool>>? ensureCutout = null)
    {
        var dialog = new ManuscriptDialog(_folder.Path, photos, new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, ensure ?? ((_, _) => Task.FromResult(true)), changed, ensureCutout);
        Quiet(dialog);
        return dialog;
    }

    /// <summary>Every question the window can ask is answered, so that none is left on the screen.</summary>
    private static void Quiet(ManuscriptDialog dialog)
    {
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => false;
        dialog.AskReplaceText = () => false;
        dialog.AskBookPath = () => null;
    }

    private static TextLine Line(string text, float x, float y, float confidence = 0.9f) =>
        new(text, confidence, TextRegion.FromCentre(new PointF(x, y), 200, 30, 0));

    private static ScanResult Three() => new([Line("перший рядок", 160, 40), Line("другий рядок", 160, 80, 0.6f), Line("третій рядок", 160, 120, 0.3f)]);

    private static void StandIn(ManuscriptDialog dialog, Func<ScanResult>? result = null) =>
        dialog.ScanOverride = (_, _, _, _, progress) =>
        {
            progress?.Report((1, 1));
            return (result ?? Three)();
        };

    private static string TextOf(ManuscriptDialog dialog) => dialog.Editor.PlainText;

    // ── The window and its pages ────────────────────────────────────────────

    [Fact]
    public void TheWindowStartsWithTheStartingPhotoAsItsFirstPage_AndOpensIt()
    {
        var a = Photo("a.jpg");
        Photo("b.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Single(dialog.Pages);
            Assert.Equal(a, dialog.Pages[0].File, ignoreCase: true);
            Assert.Equal(0, dialog.CurrentIndex);
            Assert.Single(dialog.PageList.Items.Cast<ListViewItem>());
            Assert.True(await Ui.Until(() => dialog.View.HasImage), "the photo is shown");
            Assert.Equal(ManuscriptStrings.DialogTitle, dialog.Text);
            Assert.Equal(new DirectoryInfo(_folder.Path).Name, dialog.TitleBox.Text); // the book is called after the folder until it is named
            Assert.True(dialog.ShowLinesButton.Checked);
        });
    }

    [Fact]
    public void AddPages_AddsThePhotosInOrder_SkipsThoseThatAreThereAlready_AndOpensTheFirstNewOne()
    {
        var a = Photo("a.jpg");
        var c = Photo("c.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AskPagePaths = () => [c, a, b];

            dialog.AddPagesButton.PerformClick();

            Assert.Equal(3, dialog.Pages.Count);
            Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray());
            Assert.Equal(1, dialog.CurrentIndex); // b.jpg is the first that was added
            Assert.Equal(3, dialog.PageList.Items.Count);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
        });
    }

    [Fact]
    public void RemovingAPageThatHasText_AsksFirst_AndANoKeepsIt()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            var asked = new List<string>();

            dialog.AskRemovePage = file => { asked.Add(file); return false; };
            dialog.RemovePageButton.PerformClick();
            Assert.Equal(2, dialog.Pages.Count);
            Assert.Single(asked);

            dialog.AskRemovePage = _ => true;
            dialog.RemovePageButton.PerformClick();
            Assert.Single(dialog.Pages);
            Assert.Equal(b, dialog.Pages[0].File, ignoreCase: true);
            Assert.True(File.Exists(a), "the photo file is not touched");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ARemovedPageWithNoText_NeedsNoQuestion_AndTheLastPageLeavesTheWindowEmpty()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AskRemovePage = _ => throw new InvalidOperationException("no question is needed for a page with no text");

            dialog.RemovePageButton.PerformClick();

            Assert.Empty(dialog.Pages);
            Assert.Equal(-1, dialog.CurrentIndex);
            Assert.False(dialog.ScanPageButton.Enabled);
            Assert.False(dialog.RemovePageButton.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void MovingAPage_ChangesTheOrder_AndKeepsItOpen_AndTheButtonsKnowTheEnds()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AddPagePaths([b, c]);
            dialog.SelectPage(0);
            Assert.False(dialog.MoveUpButton.Enabled);
            Assert.True(dialog.MoveDownButton.Enabled);

            dialog.MoveDownButton.PerformClick();

            Assert.Equal(["b.jpg", "a.jpg", "c.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray());
            Assert.Equal(1, dialog.CurrentIndex);
            Assert.True(dialog.MoveUpButton.Enabled);

            dialog.MoveDownButton.PerformClick();
            Assert.False(dialog.MoveDownButton.Enabled);
            Assert.Equal(["b.jpg", "c.jpg", "a.jpg"], dialog.Pages.Select(page => Path.GetFileName(page.File)!).ToArray());
            await Task.CompletedTask;
        });
    }

    // ── The kind of writing and the language ────────────────────────────────

    [Fact]
    public void BothKindsOfWriting_OfferTheSameFourLanguages_AndTheLanguageThatWasPickedStaysWhenTheKindChanges()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.Equal(ScanKind.Handwriting, dialog.Kind);
            Assert.Equal(4, dialog.LanguageCombo.Items.Count);
            Assert.Equal(ScanLanguage.Ukrainian, dialog.Language);

            dialog.LanguageCombo.SelectedIndex = 1; // Russian
            dialog.KindCombo.SelectedIndex = 1;     // printed
            Assert.Equal(ScanKind.Printed, dialog.Kind);
            Assert.Equal(4, dialog.LanguageCombo.Items.Count);
            Assert.Equal(ScanLanguage.Russian, dialog.Language);

            dialog.LanguageCombo.SelectedIndex = 2; // English
            Assert.Equal(ScanLanguage.English, dialog.Language);
            dialog.KindCombo.SelectedIndex = 0;     // handwriting reads English too
            Assert.Equal(ScanKind.Handwriting, dialog.Kind);
            Assert.Equal(4, dialog.LanguageCombo.Items.Count);
            Assert.Equal(ScanLanguage.English, dialog.Language);
            await Task.CompletedTask;
        });
    }

    // ── Scanning ────────────────────────────────────────────────────────────

    [Fact]
    public void ScanPage_PutsTheLinesInTheText_OneToAParagraph_ShowsTheirBoxes_AndMarksThePageScanned()
    {
        var a = Photo("a.jpg");
        ScanKind? kind = null;
        ScanLanguage? language = null;
        Size? size = null;

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.KindCombo.SelectedIndex = 1;
            dialog.LanguageCombo.SelectedIndex = 2;
            dialog.ScanOverride = (picture, scanKind, scanLanguage, _, _) =>
            {
                (kind, language, size) = (scanKind, scanLanguage, new Size(picture.Width, picture.Height));
                return Three();
            };
            Assert.True(await Ui.Until(() => dialog.View.HasImage));

            await dialog.ScanPageAsync();

            Assert.Equal(ScanKind.Printed, kind);
            Assert.Equal(ScanLanguage.English, language);
            Assert.Equal(new Size(320, 240), size);
            Assert.Equal("перший рядок\nдругий рядок\nтретій рядок", TextOf(dialog));
            Assert.Equal(3, dialog.View.Lines.Count);
            var page = dialog.Pages[0];
            Assert.Equal(3, page.Lines!.Count);
            Assert.Equal((320, 240), (page.Width!.Value, page.Height!.Value));
            Assert.Equal("перший рядок\nдругий рядок\nтретій рядок", page.Scanned);
            Assert.Equal("Printed", page.Kind);
            Assert.NotNull(page.ScannedUtc);
            Assert.Equal(PageState.Scanned, ManuscriptText.StateOf(page));
            Assert.StartsWith("✓", dialog.PageList.Items[0].Text);
            Assert.Contains("60%", dialog.StatusText); // the lines were 90, 60 and 30 percent sure
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void ScanPage_OnAPageWithTextThePersonWrote_AsksFirst_AndANoLeavesTheTextAlone()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var calls = 0;
            dialog.ScanOverride = (_, _, _, _, _) => { calls++; return Three(); };
            dialog.Editor.Box.Text = "my own text";
            var asked = 0;

            dialog.AskReplaceText = () => { asked++; return false; };
            await dialog.ScanPageAsync();
            Assert.Equal(1, asked);
            Assert.Equal(0, calls);
            Assert.Equal("my own text", TextOf(dialog));

            dialog.AskReplaceText = () => true;
            await dialog.ScanPageAsync();
            Assert.Equal(1, calls);
            Assert.Equal("перший рядок\nдругий рядок\nтретій рядок", TextOf(dialog));
        });
    }

    [Fact]
    public void ScanAll_ReadsOnlyThePagesThatHaveNoText_AndSaysHowManyWereRead()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var c = Photo("c.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AddPagePaths([b, c]);
            dialog.SelectPage(1);
            dialog.Editor.Box.Text = "b has its text already";
            dialog.SelectPage(0);
            var read = 0;
            dialog.ScanOverride = (_, _, _, _, _) => { read++; return read == 2 ? new ScanResult([]) : Three(); };

            await dialog.ScanAllAsync();

            Assert.Equal(2, read); // a and c; b was left as it is
            Assert.Equal("b has its text already", ManuscriptText.PlainOf(dialog.Pages[1].Rtf));
            Assert.True(dialog.Pages[0].IsScanned);
            Assert.False(dialog.Pages[2].IsScanned); // the second read found nothing
            Assert.Contains("2", dialog.StatusText);
            Assert.False(dialog.IsBusy);

            await dialog.ScanAllAsync();
            Assert.Equal(3, read); // only c has no text still
        });
    }

    [Fact]
    public void ScanAll_WithEveryPageReadAlready_SaysThereIsNothingToRead()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();

            await dialog.ScanAllAsync();

            Assert.Equal(ManuscriptStrings.StatusScanAllNothing, dialog.StatusText);
        });
    }

    [Fact]
    public void AScanThatFindsNothing_SaysSo_AndKeepsTheTextThatWasThere()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AskReplaceText = () => true;
            dialog.Editor.Box.Text = "kept";
            StandIn(dialog, () => new ScanResult([]));

            await dialog.ScanPageAsync();

            Assert.Equal(ManuscriptStrings.StatusScanNothing, dialog.StatusText);
            Assert.Equal("kept", TextOf(dialog));
            Assert.Empty(dialog.View.Lines);
        });
    }

    [Fact]
    public void WhenTheModelsAreDeclined_NothingIsRead_AndTheStatusSaysSo()
    {
        var a = Photo("a.jpg");
        var asked = new List<(ScanKind, ScanLanguage)>();

        Ui.Run(() => Dialog([a], (kind, language) => { asked.Add((kind, language)); return Task.FromResult(false); }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.LanguageCombo.SelectedIndex = 1; // Russian

            await dialog.ScanPageAsync();

            Assert.Equal([(ScanKind.Handwriting, ScanLanguage.Russian)], asked);
            Assert.Equal(ManuscriptStrings.StatusScanDeclined, dialog.StatusText);
            Assert.Equal(string.Empty, TextOf(dialog));
            Assert.False(dialog.IsBusy);
            Assert.True(dialog.ScanPageButton.Enabled, "the person can try again");
        });
    }

    [Fact]
    public void AScannerThatFails_IsToldInTheStatusBar_NotInACrash()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.ScanOverride = (_, _, _, _, _) => throw new InvalidDataException("the model is damaged");

            await dialog.ScanPageAsync();

            Assert.Equal(string.Format(ManuscriptStrings.StatusScanFailedFormat, "the model is damaged"), dialog.StatusText);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void TheStopButton_StopsAScanAtTheNextLine()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var reads = 0;
            dialog.ScanOverride = (_, _, _, cancel, _) =>
            {
                reads++;
                // The first page is being read when the person presses Stop; the scanner notices at its next line
                dialog.BeginInvoke(() => dialog.StopButton.PerformClick());
                SpinWait.SpinUntil(() => cancel.IsCancellationRequested, 5000);
                cancel.ThrowIfCancellationRequested();
                return Three();
            };

            await dialog.ScanAllAsync();

            Assert.Equal(1, reads);
            Assert.Equal(ManuscriptStrings.StatusScanStopped, dialog.StatusText);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Pages[0].IsScanned);
        });
    }

    // ── The boxes on the photo and the lines of the text ────────────────────

    [Fact]
    public void ClickingTheBoxOfALine_SelectsItsParagraph_AndMovingTheCaret_SelectsItsBox()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            await dialog.ScanPageAsync();

            // The view fits the 320 x 240 photo into its window: the box of the second line is where the point (160, 80) of the photo is
            var scale = dialog.View.ZoomOfPhoto;
            Assert.True(scale > 0);
            var second = dialog.View.LineAt(PointOf(dialog.View, 160, 80));
            Assert.Equal(1, second);

            // The click of a person: the view says which line, the text selects it
            typeof(ManuscriptView).GetMethod("ClickAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(dialog.View, [PointOf(dialog.View, 160, 80)]);
            Assert.Equal("другий рядок", dialog.Editor.Box.SelectedText);

            // And the other way: the caret in the third paragraph selects the third box
            var text = TextOf(dialog);
            dialog.Editor.Box.Select(text.IndexOf("третій", StringComparison.Ordinal) + 2, 0);
            Assert.Equal(2, dialog.View.SelectedLine);
        });
    }

    /// <summary>Where a point of the photo is on the view (the inverse of what the view draws).</summary>
    private static Point PointOf(ManuscriptView view, float photoX, float photoY)
    {
        var scale = view.ZoomOfPhoto;
        var width = 320 * scale;
        var height = 240 * scale;
        var originX = (view.ClientSize.Width - width) / 2f;
        var originY = (view.ClientSize.Height - height) / 2f;
        return new Point((int)Math.Round(originX + photoX * scale), (int)Math.Round(originY + photoY * scale));
    }

    [Fact]
    public void WhenTheTextHasMoreOrFewerParagraphsThanTheScanHadLines_TheBoxesAndTheTextAreNoLongerPaired()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            await dialog.ScanPageAsync();

            dialog.Editor.Box.Select(dialog.Editor.Box.TextLength, 0);
            dialog.Editor.Box.SelectedText = "\nand one more paragraph";

            dialog.Editor.Box.Select(2, 0);
            Assert.Equal(-1, dialog.View.SelectedLine);
        });
    }

    // ── Joining lines ───────────────────────────────────────────────────────

    [Fact]
    public void JoinLines_JoinsTheLinesOfAParagraph_KeepsTheBlankLine_AndKeepsTheLookOfTheText()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var box = dialog.Editor.Box;
            box.Text = "one\ntwo\nthree\n\nfour\nfive";
            box.Select(0, 3);
            box.SelectionFont = new Font(box.Font, FontStyle.Bold);
            box.Select(0, 0);

            dialog.JoinLinesButton.PerformClick();

            Assert.Equal("one two three\n\nfour five", box.Text);
            box.Select(0, 3);
            Assert.True(box.SelectionFont!.Bold, "'one' is still bold");
            Assert.Equal(string.Format(ManuscriptStrings.StatusLinesJoinedFormat, 3), dialog.StatusText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void JoinLines_OnlyJoinsTheSelection_AndSaysSoWhenThereIsNothingToJoin()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            var box = dialog.Editor.Box;
            box.Text = "one\ntwo\nthree\nfour";
            box.Select(box.Text.IndexOf("two", StringComparison.Ordinal), "two\nthree".Length);

            dialog.JoinLinesButton.PerformClick();

            Assert.Equal("one\ntwo three\nfour", box.Text);

            box.Text = "a single line";
            dialog.JoinLinesButton.PerformClick();
            Assert.Equal(ManuscriptStrings.StatusNoLinesToJoin, dialog.StatusText);
            await Task.CompletedTask;
        });
    }

    // ── Turning the photo ───────────────────────────────────────────────────

    [Fact]
    public void TurningAJpeg_WritesTheTurnIntoItsExif_KeepsItsDates_AndTurnsTheBoxesWithIt()
    {
        var a = Photo("a.jpg");
        var before = File.GetLastWriteTimeUtc(a);
        var changed = new List<(string File, int? Turns)>();

        Ui.Run(() => Dialog([a], changed: (file, turns) => changed.Add((file, turns))), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog, () => new ScanResult([Line("a line", 100, 40)]));
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            await dialog.ScanPageAsync();

            await dialog.RotateCurrentAsync(1);

            Assert.Equal([(a, (int?)1)], changed);
            using (var stream = File.OpenRead(a))
                Assert.Equal(6, JpegOrientation.Read(stream)); // a quarter clockwise
            Assert.True(await Ui.FileIsWritten(a));
            Assert.True((File.GetLastWriteTimeUtc(a) - before).Duration() < TimeSpan.FromSeconds(3), "the date of the file stays");
            var page = dialog.Pages[0];
            Assert.Equal((240, 320), (page.Width!.Value, page.Height!.Value)); // the photo is on its side now
            // The point (100, 40) of a 320 x 240 photo is at (240 - 40, 100) = (200, 100) after a quarter turn clockwise
            var corners = page.Lines![0].Box!;
            var centre = (X: (corners[0] + corners[4]) / 2, Y: (corners[1] + corners[5]) / 2);
            Assert.Equal(200f, centre.X, 1);
            Assert.Equal(100f, centre.Y, 1);
            Assert.Equal(string.Format(ManuscriptStrings.StatusRotatedFormat, 1), dialog.StatusText);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void TurningLeft_TurnsTheOtherWay()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            await dialog.RotateCurrentAsync(-1);

            using (var stream = File.OpenRead(a))
                Assert.Equal(8, JpegOrientation.Read(stream)); // a quarter counterclockwise
            Assert.Equal(ManuscriptStrings.RotateLeftTip, dialog.RotateLeftButton.ToolTipText);
        });
    }

    [Fact]
    public void TurningAPng_TurnsItsPixels_AndKeepsTheDatesOfTheFile()
    {
        var a = Photo("a.png", 320, 240, png: true);
        var before = File.GetLastWriteTimeUtc(a);
        var changed = new List<(string File, int? Turns)>();

        Ui.Run(() => Dialog([a], changed: (file, turns) => changed.Add((file, turns))), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            await dialog.RotateCurrentAsync(1);

            Assert.Equal([(a, (int?)null)], changed); // the pixels were written: not a lossless turn
            using (var image = Image.FromFile(a))
                Assert.Equal((240, 320), (image.Width, image.Height));
            Assert.True((File.GetLastWriteTimeUtc(a) - before).Duration() < TimeSpan.FromSeconds(3), "the date of the file stays");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePhotoThatIsGone_IsNotTurned_AndNothingIsBroken()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            File.Delete(a);

            await dialog.RotateCurrentAsync(1);

            Assert.False(dialog.IsBusy);
            Assert.Single(dialog.Pages);
        });
    }

    [Fact]
    public void TheTurnOfTheBoxes_FollowsThePixels_ForEveryQuarter()
    {
        ManuscriptPage Page() => new()
        {
            Width = 320,
            Height = 240,
            Lines = [new StoredScanLine { Box = [10, 20, 30, 20, 30, 40, 10, 40], Text = "x" }],
        };

        var right = Page();
        ManuscriptDialog.TurnLines(right, 1);
        Assert.Equal([220f, 10f, 220f, 30f, 200f, 30f, 200f, 10f], right.Lines![0].Box!);
        Assert.Equal((240, 320), (right.Width!.Value, right.Height!.Value));

        var left = Page();
        ManuscriptDialog.TurnLines(left, -1);
        Assert.Equal([20f, 310f, 20f, 290f, 40f, 290f, 40f, 310f], left.Lines![0].Box!);

        var half = Page();
        ManuscriptDialog.TurnLines(half, 2);
        Assert.Equal([310f, 220f, 290f, 220f, 290f, 200f, 310f, 200f], half.Lines![0].Box!);
        Assert.Equal((320, 240), (half.Width!.Value, half.Height!.Value));

        var full = Page();
        ManuscriptDialog.TurnLines(full, 4);
        Assert.Equal([10f, 20f, 30f, 20f, 30f, 40f, 10f, 40f], full.Lines![0].Box!);

        var four = Page();
        for (var i = 0; i < 4; i++)
            ManuscriptDialog.TurnLines(four, 1);
        Assert.Equal([10f, 20f, 30f, 20f, 30f, 40f, 10f, 40f], four.Lines![0].Box!);
        Assert.Equal((320, 240), (four.Width!.Value, four.Height!.Value));
    }

    // ── The work that is kept ───────────────────────────────────────────────

    [Fact]
    public void WhatWasDone_IsKeptWithTheFolder_AndAComesBackWhenTheWindowIsOpenedAgain()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.KindCombo.SelectedIndex = 1;
            dialog.LanguageCombo.SelectedIndex = 1; // Russian
            dialog.TitleBox.Text = "Letters";
            dialog.AuthorBox.Text = "Olya";
            dialog.IncludePhotosCheck.Checked = true;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.Editor.Box.Select(0, 0);
            dialog.Editor.Box.SelectedText = "EDITED ";
            Assert.True(dialog.SaveWork());

            var stored = ManuscriptFile.Load(_folder.Path)!;
            Assert.Equal("Letters", stored.Title);
            Assert.Equal("Olya", stored.Author);
            Assert.Equal(ScanKind.Printed, stored.GetKind());
            Assert.Equal(ScanLanguage.Russian, stored.GetLanguage());
            Assert.True(stored.IncludePhotos);
            Assert.Equal(2, stored.Pages!.Count);
            Assert.StartsWith("EDITED перший", ManuscriptText.PlainOf(stored.Pages[0].Rtf));
            Assert.Equal("перший рядок\nдругий рядок\nтретій рядок", stored.Pages[0].Scanned);
            Assert.Equal(3, stored.Pages[0].Lines!.Count);
            Assert.Equal(PageState.Edited, ManuscriptText.StateOf(stored.Pages[0]));
            await Task.CompletedTask;
        });

        // The next day: a window for the same folder finds all of it, even with no starting photo
        Ui.Run(() => Dialog([]), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(2, dialog.Pages.Count);
            Assert.Equal("Letters", dialog.TitleBox.Text);
            Assert.Equal("Olya", dialog.AuthorBox.Text);
            Assert.True(dialog.IncludePhotosCheck.Checked);
            Assert.Equal(ScanKind.Printed, dialog.Kind);
            Assert.Equal(ScanLanguage.Russian, dialog.Language);
            Assert.StartsWith("EDITED перший", TextOf(dialog));
            Assert.StartsWith("✎", dialog.PageList.Items[0].Text); // edited
            Assert.True(await Ui.Until(() => dialog.View.Lines.Count == 3), "the boxes of the scan are back on the photo");
        });
    }

    [Fact]
    public void ClosingTheWindow_KeepsTheTextThatWasTypedLast()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.Editor.Box.Text = "typed a moment before closing";

            dialog.CloseButton.PerformClick();
            await Task.CompletedTask;
        });

        Assert.Equal("typed a moment before closing", ManuscriptText.PlainOf(ManuscriptFile.Load(_folder.Path)!.Pages![0].Rtf));
    }

    [Fact]
    public void AStartingPhotoThatIsNotYetAPage_IsAddedToTheOnesThatAre_AndOpened()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        ManuscriptFile.TrySave(_folder.Path, new ManuscriptSettings { Pages = [new ManuscriptPage { File = a, Rtf = @"{\rtf1\ansi first}" }] }, out _);

        Ui.Run(() => Dialog([b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(2, dialog.Pages.Count);
            Assert.Equal(1, dialog.CurrentIndex);
            Assert.Equal(b, dialog.Pages[1].File, ignoreCase: true);
            Assert.Equal("first", ManuscriptText.PlainOf(dialog.Pages[0].Rtf));
            await Task.CompletedTask;
        });
    }

    // ── The book ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveBook_MakesAPdfOfTheText_WhenThePersonPicksAPdfFile()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("book.pdf");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.True(File.Exists(path), dialog.StatusText);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
            Assert.Equal(path, dialog.MadeBookPath);
            Assert.Contains("book.pdf", dialog.StatusText);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void SaveBook_MakesAnEpubOfTheText_WithTheTitleAndTheLanguage_AndNoPicture()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("book.epub");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.TitleBox.Text = "Letters";
            dialog.KindCombo.SelectedIndex = 1;
            dialog.LanguageCombo.SelectedIndex = 1; // Russian
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            using var zip = ZipFile.OpenRead(path);
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("OEBPS/images/", StringComparison.Ordinal));
            using var package = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
            var opf = package.ReadToEnd();
            Assert.Contains("<dc:language>ru</dc:language>", opf);
            Assert.Contains("Letters", opf);
            using var page = new StreamReader(zip.GetEntry("OEBPS/photo0001.xhtml")!.Open());
            Assert.Contains("перший рядок", page.ReadToEnd());
        });
    }

    [Fact]
    public void SaveBook_WithThePhotosOn_PutsThePhotoOfEachPageInTheBook()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var path = _folder.File("with photos.epub");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync(); // only the first page has text
            dialog.IncludePhotosCheck.Checked = true;
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("OEBPS/images/photo0001.jpg"));
            Assert.NotNull(zip.GetEntry("OEBPS/images/photo0002.jpg")); // the second page is just its photo
        });
    }

    [Fact]
    public void SaveBook_WithNoTextAnywhere_SaysSoAndAsksForNoFile()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.AskBookPath = () => throw new InvalidOperationException("no file is asked for when there is nothing to save");

            await dialog.SaveBookAsync();

            Assert.Equal(ManuscriptStrings.StatusNoText, dialog.StatusText);
        });
    }

    [Fact]
    public void SaveBook_WithoutThePhotos_LeavesOutThePagesThatHaveNoText()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var path = _folder.File("text only.epub");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("OEBPS/photo0001.xhtml"));
            Assert.Null(zip.GetEntry("OEBPS/photo0002.xhtml"));
        });
    }

    // ── Cutting the pages out of their photos ───────────────────────────────

    /// <summary>A map (the model of the stickers gives 1024 by 1024; a test makes one of 160 by 120) that is 1 inside the polygon.</summary>
    private static SubjectMap SheetMap(params PointF[] polygon)
    {
        var map = new float[160 * 120];
        for (var y = 0; y < 120; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var inside = false;
                for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                {
                    if ((polygon[i].Y > y + 0.5f) != (polygon[j].Y > y + 0.5f)
                        && x + 0.5f < (polygon[j].X - polygon[i].X) * (y + 0.5f - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                        inside = !inside;
                }

                map[y * 160 + x] = inside ? 1f : 0f;
            }
        }

        return new SubjectMap(160, 120, map);
    }

    private static int CutFolders() => Directory.GetDirectories(Path.GetTempPath(), "PhotoLikerCut_*").Length;

    private static Size ImageSizeInBook(string epub, string entry)
    {
        using var zip = ZipFile.OpenRead(epub);
        using var stream = zip.GetEntry(entry)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        using var image = Image.FromStream(memory);
        return image.Size;
    }

    [Fact]
    public void TheCutPagesBox_IsOnlyForABookWithPhotos_AndIsKeptWithTheFolder()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.False(dialog.CutPagesCheck.Enabled);
            Assert.False(dialog.CutPagesCheck.Checked);

            dialog.IncludePhotosCheck.Checked = true;
            Assert.True(dialog.CutPagesCheck.Enabled);
            dialog.CutPagesCheck.Checked = true;
            Assert.True(dialog.SaveWork());
            Assert.True(ManuscriptFile.Load(_folder.Path)!.CutPages);

            dialog.IncludePhotosCheck.Checked = false;
            Assert.False(dialog.CutPagesCheck.Enabled); // there are no photos in the book for it to cut
            dialog.IncludePhotosCheck.Checked = true;   // and as it was left, it is kept
            await Task.CompletedTask;
        });

        Ui.Run(() => Dialog([]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(dialog.IncludePhotosCheck.Checked);
            Assert.True(dialog.CutPagesCheck.Checked);
            Assert.True(dialog.CutPagesCheck.Enabled);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void SaveBook_WithCutPages_PutsTheStraightSheetInTheBook_NotTheWholePhoto_AndLeavesNothingInTheTempFolder()
    {
        var a = Photo("a.jpg"); // 320 x 240
        var path = _folder.File("cut.epub");
        var before = CutFolders();

        Ui.Run(() => Dialog([a]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.IncludePhotosCheck.Checked = true;
            dialog.CutPagesCheck.Checked = true;
            // The sheet is a tilted quadrilateral about 190 by 140 points of the photo
            dialog.CutterOverride = () => new PageCutter(_ => SheetMap(new PointF(30, 20), new PointF(125, 25), new PointF(120, 95), new PointF(25, 90)));
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.True(File.Exists(path), dialog.StatusText);
            var size = ImageSizeInBook(path, "OEBPS/images/photo0001.jpg");
            Assert.InRange(size.Width, 180, 200);
            Assert.InRange(size.Height, 130, 150);
            Assert.NotEqual(new Size(320, 240), size);
            Assert.Contains("cut.epub", dialog.StatusText);
            Assert.DoesNotContain("had no sheet", dialog.StatusText);
            Assert.False(dialog.IsBusy);
        });

        Assert.Equal(before, CutFolders());
        Assert.True(File.Exists(Path.Combine(_folder.Path, "a.jpg")), "the photo itself is not touched");
    }

    [Fact]
    public void SaveBook_WithCutPages_WhenNoSheetIsFound_KeepsThePhotoAsItIs_AndSaysHowManyWereNotCut()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        var path = _folder.File("uncut.epub");

        Ui.Run(() => Dialog([a, b]), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.IncludePhotosCheck.Checked = true;
            dialog.CutPagesCheck.Checked = true;
            dialog.CutterOverride = () => new PageCutter(_ => new SubjectMap(160, 120, new float[160 * 120]));
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.Equal(new Size(320, 240), ImageSizeInBook(path, "OEBPS/images/photo0001.jpg"));
            Assert.Equal(new Size(320, 240), ImageSizeInBook(path, "OEBPS/images/photo0002.jpg"));
            Assert.Equal(string.Format(ManuscriptStrings.StatusBookMadeCutFormat, path, 3, 2), dialog.StatusText); // the title page and the two pages
        });
    }

    [Fact]
    public void SaveBook_WithCutPages_WhenTheModelIsDeclined_MakesNoBook_AndSaysSo()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("declined.pdf");
        var asked = 0;

        Ui.Run(() => Dialog([a], ensureCutout: () => { asked++; return Task.FromResult(false); }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.IncludePhotosCheck.Checked = true;
            dialog.CutPagesCheck.Checked = true;
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.Equal(1, asked);
            Assert.False(File.Exists(path));
            Assert.Equal(ManuscriptStrings.StatusCutDeclined, dialog.StatusText);
            Assert.False(dialog.IsBusy);
            Assert.True(dialog.SaveBookButton.Enabled, "the person can turn the option off and try again");
        });
    }

    [Fact]
    public void SaveBook_WithoutCutPages_NeverAsksForTheModel()
    {
        var a = Photo("a.jpg");
        var path = _folder.File("plain.epub");

        Ui.Run(() => Dialog([a], ensureCutout: () => throw new InvalidOperationException("the model is only asked for when pages are cut out")), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            StandIn(dialog);
            await dialog.ScanPageAsync();
            dialog.IncludePhotosCheck.Checked = true;
            dialog.AskBookPath = () => path;

            await dialog.SaveBookAsync();

            Assert.True(File.Exists(path), dialog.StatusText);
        });
    }

    // ── Looks ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_IsBuiltInTheDarkTheme_WithEveryTextOfTheBarInTheLanguageThatIsOn()
    {
        var a = Photo("a.jpg");

        Ui.Run(() => new ManuscriptDialog(_folder.Path, [a], new EffectModels(Path.Combine(_folder.Path, "models")), dark: true, (_, _) => Task.FromResult(true)), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Quiet(dialog);

            Assert.Equal(ManuscriptStrings.ScanPage, dialog.ScanPageButton.Text);
            Assert.Equal(ManuscriptStrings.ScanAll, dialog.ScanAllButton.Text);
            Assert.Equal(ManuscriptStrings.AddPages, dialog.AddPagesButton.Text);
            Assert.Equal(ManuscriptStrings.SaveBook, dialog.SaveBookButton.Text);
            Assert.Equal(ManuscriptStrings.IncludePhotos, dialog.IncludePhotosCheck.Text);
            Assert.NotNull(dialog.Icon);
            foreach (var button in new[] { dialog.RotateLeftButton, dialog.RotateRightButton })
                Assert.NotNull(button.Image);
            Assert.NotEqual(SystemColors.Control, dialog.BackColor);
            await Task.CompletedTask;
        });
    }
}

/// <summary>The photo of a page with the boxes of its lines.</summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptViewTests
{
    private static Bitmap Picture(int width = 400, int height = 300) => new(width, height);

    private static TextLine Line(float x, float y, float confidence = 0.9f) =>
        new("t", confidence, TextRegion.FromCentre(new PointF(x, y), 100, 20, 0));

    [Fact]
    public void ThePhotoIsFittedToTheWindow_AndCenteredInIt()
    {
        using var view = new ManuscriptView { Size = new Size(800, 600) };

        view.SetImage(Picture(400, 300), 400);

        Assert.True(view.IsFitted);
        Assert.Equal(2f, view.ZoomOfPhoto, 3); // 800 / 400 and 600 / 300
    }

    [Fact]
    public void TheBoxesAreInThePixelsOfThePhoto_EvenWhenThePictureShownIsSmallerThanIt()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };

        view.SetImage(Picture(400, 300), photoWidth: 1600); // a photo four times as wide, shown from a quarter-size picture

        Assert.Equal(0.25f, view.ZoomOfPhoto, 3);
        view.Lines = [Line(800, 600)]; // the middle of the photo
        Assert.Equal(0, view.LineAt(new Point(200, 150)));
        Assert.Equal(-1, view.LineAt(new Point(20, 20)));
    }

    [Fact]
    public void AClickOnABox_SaysWhichLine_AndAClickElsewhereSaysNothing()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(Picture(), 400);
        view.Lines = [Line(100, 60), Line(100, 120), Line(100, 180)];
        var clicked = new List<int>();
        view.LineClicked += clicked.Add;
        var click = typeof(ManuscriptView).GetMethod("ClickAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        click.Invoke(view, [new Point(100, 120)]);
        click.Invoke(view, [new Point(350, 250)]);

        Assert.Equal([1], clicked);
        Assert.Equal(1, view.SelectedLine);
    }

    [Fact]
    public void WithTheLinesOff_NoBoxIsHit()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(Picture(), 400);
        view.Lines = [Line(100, 60)];

        view.ShowLines = false;

        Assert.Equal(-1, view.LineAt(new Point(100, 60)));
    }

    [Fact]
    public void TheWheelZoomsAboutThePointItIsOver_AndFitComesBack()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(Picture(), 400);
        var fitted = view.ZoomOfPhoto;

        view.ZoomBy(2f, new Point(100, 100));

        Assert.False(view.IsFitted);
        Assert.Equal(fitted * 2, view.ZoomOfPhoto, 3);
        view.Fit();
        Assert.True(view.IsFitted);
        Assert.Equal(fitted, view.ZoomOfPhoto, 3);
    }

    [Fact]
    public void TheZoomHasLimits()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(Picture(), 400);

        for (var i = 0; i < 50; i++)
            view.ZoomBy(2f);
        Assert.True(view.ZoomOfPhoto <= 16.01f);

        for (var i = 0; i < 100; i++)
            view.ZoomBy(0.5f);
        Assert.True(view.ZoomOfPhoto >= 0.019f);
    }

    [Fact]
    public void ASelectedLineThatIsOutsideTheWindowIsBroughtIntoView()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(Picture(2000, 1500), 2000);
        view.Lines = [Line(1800, 1400)];
        view.ZoomBy(8f); // far in: the line is far outside

        view.SelectedLine = 0;

        var box = view.LineAt(new Point(view.ClientSize.Width / 2, view.ClientSize.Height / 2));
        Assert.Equal(0, box);
    }

    [Theory]
    [InlineData(0.95f, 46, 184, 92)]
    [InlineData(0.8f, 46, 184, 92)]
    [InlineData(0.79f, 240, 160, 30)]
    [InlineData(0.5f, 240, 160, 30)]
    [InlineData(0.49f, 230, 70, 60)]
    [InlineData(0f, 230, 70, 60)]
    public void TheColourOfABox_SaysHowSureTheReaderWas(float confidence, int r, int g, int b)
    {
        var colour = ManuscriptView.ColorOf(confidence);

        Assert.Equal((r, g, b), (colour.R, colour.G, colour.B));
    }

    [Fact]
    public void ANewPicture_ForgetsTheLinesOfTheOldOne_AndTheViewWithNoPictureDrawsNothing()
    {
        using var view = new ManuscriptView { Size = new Size(400, 300) };
        view.SetImage(Picture(), 400);
        view.Lines = [Line(100, 60)];

        view.SetImage(null, 0);

        Assert.False(view.HasImage);
        Assert.Empty(view.Lines);
        using var bitmap = new Bitmap(400, 300);
        view.DrawToBitmap(bitmap, new Rectangle(0, 0, 400, 300)); // paints without a picture: no crash
    }
}

/// <summary>Where the Scan manuscripts window is opened from: the last button of the toolbar of the main window and Ctrl+Shift+M, and the models it asks for.</summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptIntegrationTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public ManuscriptIntegrationTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;

    private string Photo(string name)
    {
        var path = _folder.File(name);
        using var bitmap = new Bitmap(320, 240);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static void Press(Form form, Keys keys) =>
        form.BeginInvoke(() =>
        {
            typeof(MainForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(form, [new Message(), keys]);
        });

    private static ToolStrip Toolbar(Form form) =>
        Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items["manuscriptToolStripButton"] is not null);

    [Fact]
    public void TheButton_IsTheVeryLastOneOfTheToolbar_AfterDraw()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var names = Toolbar(form).Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal("manuscriptToolStripButton", names[^1]);
            Assert.Equal("drawToolStripButton", names[^2]);
            var button = Toolbar(form).Items["manuscriptToolStripButton"]!;
            Assert.Equal(ManuscriptStrings.ButtonText, button.Text);
            Assert.Equal(ManuscriptStrings.ButtonTooltip, button.ToolTipText);
            Assert.Contains("Ctrl+Shift+M", button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void CtrlShiftM_OpensTheWindow_WithThePhotoThatIsShownAsItsPage_AndTheButtonDoesToo()
    {
        var photo = Photo("a.jpg");

        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => Field<PictureBox>(form, "pictureBox1").Tag is string), "the photo is shown");

            Press(form, Keys.Control | Keys.Shift | Keys.M);
            Assert.True(await Ui.Until(() => Ui.Find("ManuscriptDialog") is not null), "the window opened");
            var dialog = (ManuscriptDialog)Ui.Find("ManuscriptDialog")!;
            dialog.AskRemovePage = _ => false;
            Assert.Equal(photo, dialog.Pages.Single().File, ignoreCase: true);
            dialog.CloseButton.PerformClick();
            Assert.True(await Ui.Until(() => Ui.Find("ManuscriptDialog") is null), "the window closed");

            form.BeginInvoke(() => Toolbar(form).Items["manuscriptToolStripButton"]!.PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("ManuscriptDialog") is not null), "the button opened it too");
            Ui.Find("ManuscriptDialog")!.Close();
        }, seconds: 90);
    }

    [Fact]
    public void WithNothingShown_TheWindowStillOpens_ForThePersonToAddPages()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Press(form, Keys.Control | Keys.Shift | Keys.M);

            Assert.True(await Ui.Until(() => Ui.Find("ManuscriptDialog") is not null), "the window opened");
            var dialog = (ManuscriptDialog)Ui.Find("ManuscriptDialog")!;
            Assert.Empty(dialog.Pages);
            Assert.True(dialog.AddPagesButton.Enabled);
            Assert.False(dialog.ScanPageButton.Enabled);
            dialog.Close();
        }, seconds: 60);
    }

    /// <summary>The window with the question about downloading a model answered "no" and written down (a test must not download anything, nor wait for a person).</summary>
    private sealed class AskingMainForm(string folder) : MainForm(folder)
    {
        public readonly List<string> Asked = [];

        protected override bool ConfirmToolModelDownload(string feature, ToolModel model)
        {
            Asked.Add($"{model.Id}|{feature}");
            return false;
        }
    }

    private static void TakeTheModelsAway()
    {
        foreach (var model in new[] { ToolModels.OcrFinder, ToolModels.OcrReader, ToolModels.HandwritingUkrainian, ToolModels.HandwritingRussian })
        {
            var target = model.PathIn(Path.Combine(AppPaths.DataFolder, "models"));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Delete(target);
        }
    }

    [Fact]
    public void TheModelsOfScanManuscripts_AreAskedForOnce_ForTheFirst_AndNothingIsAskedAfterANo()
    {
        TakeTheModelsAway();

        Ui.Run(() => new AskingMainForm(_folder.Path), async form =>
        {
            var main = (AskingMainForm)form;
            var ensure = typeof(MainForm).GetMethod("EnsureManuscriptModelsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var ready = await (Task<bool>)ensure.Invoke(form, [ScanKind.Handwriting, ScanLanguage.Russian])!;

            Assert.False(ready);
            Assert.Equal([$"{ToolModels.OcrFinder.Id}|{ToolModelStrings.ScanManuscriptFeature}"], main.Asked);
        }, seconds: 60);
    }

    [Fact]
    public void TheModelOfTheAi_IsAskedForFirst_WithItsSizeAndOrigin_AndNothingIsDownloadedAfterANo()
    {
        foreach (var model in new[] { ToolModels.TextFixer, ToolModels.TextFixerTokenizer })
        {
            var target = model.PathIn(Path.Combine(AppPaths.DataFolder, "models")); // the folder of the tests, not the person's
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Delete(target);
        }

        Ui.Run(() => new AskingMainForm(_folder.Path), async form =>
        {
            var main = (AskingMainForm)form;
            var ensure = typeof(MainForm).GetMethod("EnsureManuscriptFixModelAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            var ready = await (Task<bool>)ensure.Invoke(form, [])!;

            Assert.False(ready);
            Assert.Equal([$"{ToolModels.TextFixer.Id}|{ToolModelStrings.FixTextFeature}"], main.Asked); // the model is asked for; the list of its words goes with it
        }, seconds: 60);
    }

    [Fact]
    public void WhileTheWindowIsOpen_WhatTheMainWindowSaysAboutAModel_IsInItsStatusBarToo_AndNotAfterItIsClosed()
    {
        foreach (var model in new[] { ToolModels.TextFixer, ToolModels.TextFixerTokenizer })
        {
            var target = model.PathIn(Path.Combine(AppPaths.DataFolder, "models")); // the folder of the tests, not the person's
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Delete(target);
        }

        Ui.Run(() => new AskingMainForm(_folder.Path), async form =>
        {
            var ensure = typeof(MainForm).GetMethod("EnsureManuscriptFixModelAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            form.BeginInvoke(() => Toolbar(form).Items["manuscriptToolStripButton"]!.PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("ManuscriptDialog") is not null), "the window opened");
            var dialog = (ManuscriptDialog)Ui.Find("ManuscriptDialog")!;

            await (Task<bool>)ensure.Invoke(form, [])!; // the question is answered "no": the main window says so
            Assert.Equal(string.Format(ToolModelStrings.StatusDeclinedFormat, ToolModels.TextFixer.Title), dialog.StatusText);

            dialog.Close();
            Assert.True(await Ui.Until(() => Ui.Find("ManuscriptDialog") is null), "the window closed");
            await (Task<bool>)ensure.Invoke(form, [])!; // nothing is told to a window that is gone
            var subscribers = typeof(MainForm).GetField("ToolModelStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(form);
            Assert.Null(subscribers); // and the main window holds no reference to it
        }, seconds: 90);
    }

    [Fact]
    public void TheWindowThatTheMainWindowMakes_HasTheAiButton()
    {
        Ui.Run(() => new AskingMainForm(_folder.Path), async form =>
        {
            var main = (AskingMainForm)form;
            using var dialog = main.CreateManuscriptDialog();

            Assert.True(dialog.Editor.FixVisible);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Theory]
    [InlineData(ScanLanguage.Ukrainian, "crnn-ctc-ukrainian")]
    [InlineData(ScanLanguage.Russian, "crnn-ctc-russian")]
    public void EachLanguageOfHandwriting_HasItsOwnModel(ScanLanguage language, string id) =>
        Assert.Equal(id, ManuscriptDialog.HandwritingModel(language).Id);
}
