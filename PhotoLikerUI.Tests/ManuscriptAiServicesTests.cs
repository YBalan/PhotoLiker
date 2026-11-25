using System.Drawing.Imaging;
using System.Net;
using System.Text.Json;
using PhotoLiker.Core;
using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Which AI the AI button of the Scan manuscripts window uses: the box next to it (the model on this computer, and the services that have a key), the button for the keys, what is kept of the pick, and
/// what the AI button does when a service is picked (the text of the page goes to that service, nothing is downloaded, a refusal or a stop is told). The Internet is a stand-in in every test: no
/// test can call a real service, and the keys live in a temporary file.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ManuscriptAiServicesTests : IDisposable
{
    private const string Page = "Wensday\tpainting\nThursdav\tmusic\n\nthe lbrary was shut";
    private const string Fixed = "Wednesday\tpainting\nThursday\tmusic\n\nthe library was shut";
    private const string Key = "sk-test-0123456789abcdef";

    private readonly TempFolder _folder = new();

    public ManuscriptAiServicesTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string SettingsFile => _folder.File("ai-services.json");

    private AiServiceSettings Settings() => AiServiceSettings.Load(SettingsFile, new MaskingProtector());

    private string Photo(string name)
    {
        var path = _folder.File(name);
        using var bitmap = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(240, 236, 220));

        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private ManuscriptDialog Dialog(AiServiceSettings settings, StandInInternet? internet = null, Func<Task<bool>>? ensureFix = null)
    {
        var dialog = new ManuscriptDialog(
            _folder.Path, [Photo("a.jpg")], new EffectModels(Path.Combine(_folder.Path, "models")), dark: false, (_, _) => Task.FromResult(true), ensureFixAsync: ensureFix, aiServices: settings);
        dialog.AskPagePaths = () => null;
        dialog.AskRemovePage = _ => true;
        dialog.AskReplaceText = () => true;
        dialog.AskBookPath = () => null;
        dialog.ScanPicturesCheck.Checked = false;
        dialog.CloudHandler = internet ?? new StandInInternet(); // never the real Internet
        return dialog;
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);

    private static string ClaudeSays(string text) => "{\"content\":[{\"type\":\"text\",\"text\":" + Json(text) + "}],\"stop_reason\":\"end_turn\"}";

    private static string OpenAiSays(string text) => "{\"choices\":[{\"message\":{\"content\":" + Json(text) + "},\"finish_reason\":\"stop\"}]}";

    private static string GeminiSays(string text) => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + Json(text) + "}]},\"finishReason\":\"STOP\"}]}";

    /// <summary>A stand-in Internet that corrects the two pieces of <see cref="Page"/> the way a service of that vendor would answer.</summary>
    private static StandInInternet Corrects(AiVendor vendor)
    {
        Func<string, string> says = vendor switch
        {
            AiVendor.OpenAI => OpenAiSays,
            AiVendor.Gemini => GeminiSays,
            _ => ClaudeSays,
        };
        return new StandInInternet().Answer(HttpStatusCode.OK, says("Wednesday\tpainting\nThursday\tmusic")).Answer(HttpStatusCode.OK, says("the library was shut"));
    }

    private AiServiceSettings WithKey(AiVendor vendor, bool picked = true)
    {
        var settings = Settings();
        settings.Set(vendor, Key, null);
        if (picked)
            settings.Engine = vendor;
        return settings;
    }

    // ── The box and the button ──────────────────────────────────────────────

    [Fact]
    public void TheAiBoxAndTheButtonForTheKeys_AreInARowOverTheText_ByTheAiButton()
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(ManuscriptStrings.LabelAi, dialog.AiLabel.Text);
            Assert.Equal(ManuscriptStrings.AiServices, dialog.AiServicesButton.Text);

            // The three are one row, in the panel of the text, above the editor that has the AI button (the toolbar has no room: it is wider than the window)
            var row = dialog.AiCombo.Parent!;
            Assert.Same(row, dialog.AiLabel.Parent);
            Assert.Same(row, dialog.AiServicesButton.Parent);
            Assert.Same(dialog.Editor.Parent, row.Parent);
            Assert.True(row.Visible && dialog.AiCombo.Visible && dialog.AiServicesButton.Visible);
            Assert.True(row.Bottom <= dialog.Editor.Top);
            Assert.True(dialog.Editor.FixVisible);

            // And the row is inside the window at its smallest, whatever the width of the text
            Assert.True(dialog.AiServicesButton.Right <= dialog.ClientSize.Width);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WithoutKeys_TheBoxHasTheModelOnThisComputerOnly()
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal([ManuscriptStrings.AiLocal], dialog.AiCombo.Items.Cast<object>().Select(item => item.ToString()));
            Assert.Equal(0, dialog.AiCombo.SelectedIndex);
            Assert.Null(dialog.PickedAi);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheBox_ListsEachServiceThatHasAKey_WithTheModelItIsAsked_AndTheOnePickedIsSelected()
    {
        var settings = Settings();
        settings.Set(AiVendor.Claude, Key, "claude-x");
        settings.Set(AiVendor.OpenAI, Key, null);
        settings.Engine = AiVendor.Claude;
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(
                [ManuscriptStrings.AiLocal, $"OpenAI (ChatGPT) - {AiVendors.DefaultModel(AiVendor.OpenAI)}", "Anthropic Claude - claude-x"],
                dialog.AiCombo.Items.Cast<object>().Select(item => item.ToString()));
            Assert.Equal(AiVendor.Claude, dialog.PickedAi);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhatIsPicked_IsKeptForTheNextTime()
    {
        var settings = Settings();
        settings.Set(AiVendor.Gemini, Key, null);
        settings.Set(AiVendor.Claude, Key, null);
        settings.Save();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            dialog.AiCombo.SelectedIndex = 2; // Claude
            Assert.Equal(AiVendor.Claude, Settings().Engine);

            dialog.AiCombo.SelectedIndex = 0; // this computer
            Assert.Null(Settings().Engine);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ANewWindow_StartsWithWhatWasPickedLast()
    {
        var settings = WithKey(AiVendor.Gemini);
        settings.Save();
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (ManuscriptDialog)form;

            Assert.Equal(AiVendor.Gemini, dialog.PickedAi);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AServiceThatLostItsKey_IsNotInTheBox_AndTheModelOnThisComputerIsPicked()
    {
        var settings = WithKey(AiVendor.Claude);
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.Equal(AiVendor.Claude, dialog.PickedAi);

            settings.Remove(AiVendor.Claude);
            dialog.FillAiCombo();

            Assert.Null(dialog.PickedAi);
            Assert.Single(dialog.AiCombo.Items);
            await Task.CompletedTask;
        });
    }

    // ── The window of the keys ──────────────────────────────────────────────

    [Fact]
    public void TheFirstKeyThatIsGiven_IsPicked_BecauseThePersonCameToUseIt()
    {
        var settings = Settings();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.ShowAiServices = s =>
            {
                s.Set(AiVendor.Claude, Key, "claude-x");
                return DialogResult.OK;
            };

            dialog.AiServicesButton.PerformClick();

            Assert.Equal(AiVendor.Claude, dialog.PickedAi);
            Assert.Equal(AiVendor.Claude, Settings().Engine); // and kept
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AnotherKeyLater_DoesNotChangeWhatIsPicked()
    {
        var settings = WithKey(AiVendor.Claude);
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.ShowAiServices = s =>
            {
                s.Set(AiVendor.OpenAI, Key, null);
                return DialogResult.OK;
            };

            dialog.AiServicesButton.PerformClick();

            Assert.Equal(AiVendor.Claude, dialog.PickedAi);
            Assert.Equal(3, dialog.AiCombo.Items.Count); // but it is in the box
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ACancelledWindow_ChangesNothingInTheBox()
    {
        var settings = Settings();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            dialog.ShowAiServices = _ => DialogResult.Cancel;

            dialog.AiServicesButton.PerformClick();

            Assert.Null(dialog.PickedAi);
            Assert.Single(dialog.AiCombo.Items);
            Assert.False(File.Exists(SettingsFile));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheKeysAreAsked_ForTheWindowOfTheKeys_WhichGetsTheSettingsOfTheProgram()
    {
        var settings = Settings();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            AiServiceSettings? given = null;
            dialog.ShowAiServices = s =>
            {
                given = s;
                return DialogResult.Cancel;
            };

            dialog.AiServicesButton.PerformClick();

            Assert.Same(settings, given);
            await Task.CompletedTask;
        });
    }

    // ── The AI button with a service picked ─────────────────────────────────

    [Theory]
    [InlineData(AiVendor.OpenAI, "api.openai.com")]
    [InlineData(AiVendor.Gemini, "generativelanguage.googleapis.com")]
    [InlineData(AiVendor.Claude, "api.anthropic.com")]
    public void WithAServicePicked_TheTextIsCorrectedByIt_PieceByPiece_AndNothingIsDownloaded(AiVendor vendor, string host)
    {
        var downloads = 0;
        var internet = Corrects(vendor);
        Ui.Run(() => Dialog(WithKey(vendor), internet, ensureFix: () =>
        {
            downloads++;
            return Task.FromResult(true);
        }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.Equal(Fixed, dialog.Editor.PlainText);
            Assert.Equal(string.Format(ManuscriptStrings.StatusFixedFormat, 3), dialog.StatusText);
            Assert.Equal(0, downloads); // the model of 1.8 GB is not asked for
            Assert.Equal(2, internet.Requests.Count); // a paragraph at a time
            Assert.All(internet.Requests, request => Assert.Equal(host, request.Uri.Host));
            Assert.Contains("Wensday", internet.Requests[0].Body);
            Assert.Contains("the lbrary was shut", internet.Requests[1].Body);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Editor.Box.ReadOnly);

            // One Ctrl+Z takes it all back, as for the model on this computer
            dialog.Editor.Box.Undo();
            Assert.Equal(Page, dialog.Editor.PlainText);
        });
    }

    [Fact]
    public void TheServiceIsToldTheLanguage_OnlyTheTextIsSent_AndTheKeyIsNotInTheBody()
    {
        var internet = Corrects(AiVendor.Claude);
        Ui.Run(() => Dialog(WithKey(AiVendor.Claude), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.SelectLanguage(ScanLanguage.Ukrainian);
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.Contains("The text is in Ukrainian.", internet.Requests[0].Body);
            Assert.DoesNotContain(Key, internet.Requests[0].Body);
            Assert.DoesNotContain("image/", internet.Requests[0].Body); // the photo does not leave the computer
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void OnlyTheSelectedText_IsSentToTheService()
    {
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeSays("the library was shut"));
        Ui.Run(() => Dialog(WithKey(AiVendor.Claude), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;
            var start = Page.IndexOf("the lbrary", StringComparison.Ordinal);
            dialog.Editor.Box.Select(start, "the lbrary was shut".Length);

            await dialog.FixTextAsync();

            Assert.Single(internet.Requests);
            Assert.DoesNotContain("Thursdav", internet.Requests[0].Body);
            Assert.Equal(Page.Replace("lbrary", "library"), dialog.Editor.PlainText);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AServiceThatDoesNotAcceptTheKey_IsToldInTheStatusBar_WithItsWords_AndTheTextStays()
    {
        var internet = new StandInInternet().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid x-api-key"}}""");
        Ui.Run(() => Dialog(WithKey(AiVendor.Claude), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.StartsWith(string.Format(ManuscriptStrings.StatusFixFailedFormat, string.Empty).TrimEnd(), dialog.StatusText);
            Assert.Contains("Anthropic Claude", dialog.StatusText);
            Assert.Contains("401", dialog.StatusText);
            Assert.Contains("invalid x-api-key", dialog.StatusText);
            Assert.DoesNotContain(Key, dialog.StatusText);
            Assert.Equal(Page, dialog.Editor.PlainText);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.Editor.Box.ReadOnly);
            Assert.True(dialog.AiCombo.Enabled);
            Assert.True(dialog.AiServicesButton.Enabled);
        });
    }

    [Fact]
    public void WhileAServiceIsAsked_TheBoxAndTheButtonOfTheKeysAreOff_AndNothingIsHeldAfterwards()
    {
        (bool Combo, bool Button, bool Busy, bool Stop) seen = default;
        ManuscriptDialog? window = null;
        var internet = new StandInInternet().Answer((_, _) =>
        {
            seen = window!.Invoke(() => (window.AiCombo.Enabled, window.AiServicesButton.Enabled, window.IsBusy, window.StopButton.Enabled));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ClaudeSays("the library was shut"), System.Text.Encoding.UTF8, "application/json") });
        });
        Ui.Run(() => window = Dialog(WithKey(AiVendor.Claude), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = "the lbrary was shut";

            await dialog.FixTextAsync();

            Assert.Equal((false, false, true, true), seen);
            Assert.True(dialog.AiCombo.Enabled);
            Assert.True(dialog.AiServicesButton.Enabled);
            Assert.False(dialog.HasCorrector);
        });
    }

    [Fact]
    public void TheStopButton_StopsTheAsk_AndNothingIsChanged()
    {
        using var asked = new ManualResetEventSlim();
        var internet = new StandInInternet().Answer(async (_, cancel) =>
        {
            asked.Set();
            await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage();
        });
        Ui.Run(() => Dialog(WithKey(AiVendor.Gemini), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;

            var running = dialog.FixTextAsync();
            Assert.True(await Ui.Until(() => asked.IsSet));
            dialog.StopButton.PerformClick();
            await running;

            Assert.Equal(ManuscriptStrings.StatusFixStopped, dialog.StatusText);
            Assert.Equal(Page, dialog.Editor.PlainText);
            Assert.False(dialog.IsBusy);
        });
    }

    [Fact]
    public void ATestsStandInForTheModel_StillWins_SoNoTestAsksAServiceByAccident()
    {
        var internet = new StandInInternet();
        Ui.Run(() => Dialog(WithKey(AiVendor.Claude), internet), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.FixerOverride = (text, _, _) => text.Replace("lbrary", "library");
            dialog.Editor.Box.Text = "the lbrary was shut";

            await dialog.FixTextAsync();

            Assert.Equal("the library was shut", dialog.Editor.PlainText);
            Assert.Empty(internet.Requests);
        });
    }

    [Fact]
    public void WithTheModelOnThisComputerPicked_TheModelIsAskedFor_AndNoServiceIsCalled()
    {
        var asked = 0;
        var internet = new StandInInternet();
        var settings = Settings();
        settings.Set(AiVendor.Claude, Key, null); // it has a key, but it is not the one that is picked
        Ui.Run(() => Dialog(settings, internet, ensureFix: () =>
        {
            asked++;
            return Task.FromResult(false);
        }), async form =>
        {
            var dialog = (ManuscriptDialog)form;
            Assert.True(await Ui.Until(() => dialog.View.HasImage));
            dialog.Editor.Box.Text = Page;

            await dialog.FixTextAsync();

            Assert.Equal(1, asked);
            Assert.Equal(ManuscriptStrings.StatusFixDeclined, dialog.StatusText);
            Assert.Empty(internet.Requests);
        });
    }
}
