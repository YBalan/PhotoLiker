using System.Drawing.Imaging;
using PhotoLiker.Core;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.PhotoBook;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The AI button of the text box in the Scan manuscripts window: what it gives the corrector (the page, or the selection, and the language), what is done with the answer (word by word, the lines, tabs
/// and pictures stay, one Ctrl+Z takes it back), and what happens when the model is not there, the person stops it or it fails. A stand-in is the corrector: the machines that build the program have no
/// model, and no test leaves a question on the screen; the real model is tried in <see cref="RealTextFixerTests"/>.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptFixTests : IDisposable
{
    private const string Page = "Wensday\tpainting\nThursdav\tmusic\n\nthe lbrary was shut";
    private const string Fixed = "Wednesday\tpainting\nThursday\tmusic\n\nthe library was shut";

    private readonly TempFolder _folder = new();

    public ManuscriptFixTests() => new GlobalConfig { ColorizeBar = false }.Save();

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

    private ManuscriptDialog Dialog(Func<Task<bool>>? ensureFix = null)
    {
        var photo = Photo("a.jpg");
        var dialog = new ManuscriptDialog(
            _folder.Path, [photo], new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), ensureFixAsync: ensureFix);
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => true;
        dialog.AskReplaceText = () => true;
        dialog.AskBookPath = () => null;
        dialog.ScanPicturesCheck.Checked = false;
        return dialog;
    }

    /// <summary>A corrector that puts right the words of <see cref="Page"/> and records what it was given and in which language.</summary>
    private static List<(string Text, ScanLanguage Language)> StandIn(ManuscriptDialog dialog)
    {
        var asked = new List<(string, ScanLanguage)>();
        dialog.FixerOverride = (text, language, _) =>
        {
            lock (asked)
                asked.Add((text, language));
            return text.Replace("Wensday", "Wednesday").Replace("Thursdav", "Thursday").Replace("lbrary", "library");
        };
        return asked;
    }

    // ── The button ──────────────────────────────────────────────────────────

    [Fact]
    public void TheAiButton_IsAtTheEndOfTheButtonsOfTheText_WithATipAndAName()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.True(dialog.Editor.FixVisible);
            Assert.Equal(ManuscriptStrings.FixTextTip, dialog.Editor.FixButton.ToolTipText);
            Assert.Equal(ManuscriptStrings.FixText, dialog.Editor.FixButton.AccessibleName);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void PressingTheButton_CorrectsTheText()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.Editor.Box.Text = Page;

            dialog.Editor.FixButton.PerformClick();
            Assert.True(await Ui.Until(() => dialog.Editor.PlainText == Fixed));
        });
    }

    // ── The text ────────────────────────────────────────────────────────────

    [Fact]
    public void ThePage_IsCorrectedWordByWord_TheLinesAndTabsStay_ItIsToldAndKept()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.Equal(Fixed, dialog.Editor.PlainText);
            Assert.Equal(string.Format(ManuscriptStrings.StatusFixedFormat, 3), dialog.StatusText);
            Assert.Equal(["Wensday\tpainting\nThursdav\tmusic", "the lbrary was shut"], asked.Select(a => a.Text)); // a paragraph at a time
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Editor.Box.ReadOnly);

            // Kept with the folder
            Assert.Contains("Wednesday", dialog.Pages[0].Rtf);
            var stored = ManuscriptFile.Load(_folder.Path)!.Pages![0];
            Assert.Equal(Fixed, ManuscriptText.PlainOf(stored.Rtf!));
        });
    }

    [Fact]
    public void OnlyTheSelectedText_IsCorrected()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            dialog.Editor.Box.Text = Page;
            var start = Page.IndexOf("the lbrary", StringComparison.Ordinal);
            dialog.Editor.Box.Select(start, "the lbrary was shut".Length);

            await dialog.FixTextAsync();

            Assert.Equal("Wensday\tpainting\nThursdav\tmusic\n\nthe library was shut", dialog.Editor.PlainText); // the rest was not given to the AI
            Assert.Equal(["the lbrary was shut"], asked.Select(a => a.Text));
            Assert.Equal(string.Format(ManuscriptStrings.StatusFixedFormat, 1), dialog.StatusText);
        });
    }

    [Theory]
    [InlineData(ScanLanguage.Ukrainian)]
    [InlineData(ScanLanguage.English)]
    [InlineData(ScanLanguage.Multilingual)]
    public void TheCorrector_IsToldTheLanguageThatIsPicked(ScanLanguage language)
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            dialog.SelectLanguage(language);
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.All(asked, a => Assert.Equal(language, a.Language));
        });
    }

    [Fact]
    public void ATextThatIsRight_SaysNothingWasFound_AndIsNotChanged()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.FixerOverride = (text, _, _) => text;
            dialog.Editor.Box.Text = Fixed;
            dialog.Editor.Box.ClearUndo();

            await dialog.FixTextAsync();

            Assert.Equal(ManuscriptStrings.StatusFixNothing, dialog.StatusText);
            Assert.Equal(Fixed, dialog.Editor.PlainText);
            Assert.False(dialog.Editor.Box.CanUndo); // nothing was done to the text
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n")]
    [InlineData("12 + 34 = 46")]
    public void WithNoWordsToCorrect_TheCorrectorIsNotAsked(string text)
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            var asked = StandIn(dialog);
            dialog.Editor.Box.Text = text;

            await dialog.FixTextAsync();

            Assert.Equal(ManuscriptStrings.StatusFixNoText, dialog.StatusText);
            Assert.Empty(asked);
        });
    }

    [Fact]
    public void OneCtrlZ_TakesAllTheChangesBack()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            dialog.Editor.Box.Text = Page;
            dialog.Editor.Box.ClearUndo();

            await dialog.FixTextAsync();
            Assert.Equal(Fixed, dialog.Editor.PlainText);

            dialog.Editor.Box.Undo();
            Assert.Equal(Page, dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void ThePicturesOfThePageAndItsIndents_StayWhenItsTextIsCorrected()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            StandIn(dialog);
            using var bitmap = new Bitmap(40, 30);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            dialog.Editor.Rtf = RtfWriter.Write(new RichDocument(
            [
                new RichParagraph(TextAlign.Left, [new RichRun("Wensday\tpainting", default)]) { LeftIndent = 36 },
                RichParagraph.OfPicture(RichPicture.FromFile(stream.ToArray(), 0.4)!),
                new RichParagraph(TextAlign.Left, [new RichRun("the lbrary", default)]),
            ]));

            await dialog.FixTextAsync();

            var document = RtfParser.Parse(dialog.Editor.Rtf);
            Assert.Equal("Wednesday\tpainting", document.Paragraphs[0].PlainText);
            Assert.Equal(36, document.Paragraphs[0].LeftIndent, 1);
            Assert.NotNull(document.Paragraphs[1].Picture);
            Assert.Equal("the library", document.Paragraphs[2].PlainText);
        });
    }

    // ── While it works, and when it does not ────────────────────────────────

    [Fact]
    public void WhileTheAiWorks_TheWindowIsBusy_TheTextCannotBeEdited_AndTheStopButtonIsThere()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            (bool Busy, bool ReadOnly, bool ScanEnabled, bool StopEnabled, bool FixEnabled, bool Held) seen = default;
            dialog.FixerOverride = (text, _, _) =>
            {
                seen = dialog.Invoke(() => (dialog.IsBusy, dialog.Editor.Box.ReadOnly, dialog.ScanPageButton.Enabled, dialog.StopButton.Enabled, dialog.Editor.FixButton.Enabled, dialog.HasCorrector));
                return text;
            };
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.Equal((true, true, false, true, false, true), seen); // the model is held while it works...
            Assert.False(dialog.HasCorrector); // ...and let go of when it is done: it is more than a gigabyte
            Assert.False(dialog.IsBusy);
            Assert.True(dialog.ScanPageButton.Enabled);
            Assert.False(dialog.StopButton.Enabled);
            Assert.True(dialog.Editor.FixButton.Enabled);
        });
    }

    [Fact]
    public void TheStopButton_StopsTheAi_AndNothingIsChanged()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            using var started = new ManualResetEventSlim();
            dialog.FixerOverride = (text, _, cancel) =>
            {
                started.Set();
                cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                cancel.ThrowIfCancellationRequested();
                return text.Replace("Wensday", "Wednesday");
            };
            dialog.Editor.Box.Text = Page;

            var running = dialog.FixTextAsync();
            Assert.True(await Ui.Until(() => started.IsSet));
            dialog.StopButton.PerformClick();
            await running;

            Assert.Equal(Page, dialog.Editor.PlainText);
            Assert.Equal(ManuscriptStrings.StatusFixStopped, dialog.StatusText);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void AFailureOfTheCorrector_IsToldInTheStatusBar_AndTheWindowIsUsableAgain()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.FixerOverride = (_, _, _) => throw new InvalidOperationException("the model broke");
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.Equal(string.Format(ManuscriptStrings.StatusFixFailedFormat, "the model broke"), dialog.StatusText);
            Assert.Equal(Page, dialog.Editor.PlainText);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Editor.Box.ReadOnly);
        });
    }

    [Fact]
    public void WhenThePersonSaysNoToTheDownload_NothingIsCorrected_AndItIsAskedAgainTheNextTime()
    {
        var asked = 0;
        Ui.Run(() => Dialog(() =>
        {
            asked++;
            return Task.FromResult(false);
        }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();
            Assert.Equal(ManuscriptStrings.StatusFixDeclined, dialog.StatusText);
            Assert.Equal(Page, dialog.Editor.PlainText);
            Assert.False(dialog.IsBusy);

            await dialog.FixTextAsync(); // a button the person pressed: asked again
            Assert.Equal(2, asked);
        });
    }

    [Fact]
    public void WhenTheFilesOfTheModelAreNotThereAfterAll_ItIsToldAndTheWindowGoesOn()
    {
        Ui.Run(() => Dialog(() => Task.FromResult(true)), async form => // "downloaded", but the folder has nothing
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.StartsWith(string.Format(ManuscriptStrings.StatusFixFailedFormat, string.Empty).TrimEnd(), dialog.StatusText);
            Assert.Equal(Page, dialog.Editor.PlainText);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void WhileSomethingElseIsGoingOn_TheButtonDoesNothing()
    {
        Ui.Run(() => Dialog(), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;
            using var release = new ManualResetEventSlim();
            using var inside = new ManualResetEventSlim();
            var calls = 0;
            dialog.FixerOverride = (text, _, cancel) =>
            {
                Interlocked.Increment(ref calls);
                inside.Set();
                release.Wait(TimeSpan.FromSeconds(20));
                return text;
            };

            var first = dialog.FixTextAsync();
            Assert.True(await Ui.Until(() => inside.IsSet));
            await dialog.FixTextAsync(); // a second press while the first is at work: nothing
            release.Set();
            await first;

            Assert.Equal(2, calls); // the two paragraphs of the first press, and none of the second
        });
    }
}
