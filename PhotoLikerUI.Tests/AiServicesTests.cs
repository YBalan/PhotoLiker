using System.Net;
using System.Text;
using PhotoLiker.Core;
using PhotoLiker.Manuscripts.Cloud;

namespace PhotoLikerUI.Tests;

/// <summary>Stands in for the Internet when the windows ask an AI service: it records what was sent and answers with what the test says, in order. Nothing leaves the computer.</summary>
internal sealed class StandInInternet : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _answers = new();
    private readonly object _lock = new();

    public List<(Uri Uri, string Body)> Requests { get; } = [];

    public StandInInternet Answer(HttpStatusCode status, string body)
    {
        _answers.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        return this;
    }

    public StandInInternet Answer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;
        lock (_lock)
        {
            Requests.Add((request.RequestUri!, body));
            answer = _answers.Count > 0 ? _answers.Dequeue() : throw new InvalidOperationException("The test did not expect another request.");
        }

        return await answer(request, cancellationToken);
    }
}

/// <summary>Hides a secret in a way that can be told from the secret and is undone: the tests do not need Windows to keep a key, and must not put one in the data of the person who runs them.</summary>
internal sealed class MaskingProtector(string mask = "M1") : ISecretProtector
{
    public string Protect(string secret) => mask + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));

    public string? Unprotect(string protectedText) =>
        protectedText.StartsWith(mask + ":", StringComparison.Ordinal) ? Encoding.UTF8.GetString(Convert.FromBase64String(protectedText[(mask.Length + 1)..])) : null;
}

/// <summary>What the real client of the AI services is made of: nothing that could carry a key to another address.</summary>
public sealed class CloudHttpTests
{
    [Fact]
    public void TheRealClient_DoesNotFollowARedirect_SoNoKeyHeaderCanGoWhereAServiceSentItOn()
    {
        using var handler = CloudHttp.NewHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void TheClient_HasNoTimeOfItsOwn_TheRequestsLimitThemselves()
    {
        Assert.Equal(Timeout.InfiniteTimeSpan, CloudHttp.Shared.Timeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, CloudHttp.Create(new StandInInternet()).Timeout);
    }
}

/// <summary>
/// The window of the keys of the AI services (ChatGPT, Gemini, Claude): what it shows of what is kept, how a key is tried, what OK keeps and what it refuses. The Internet is a stand-in and the keys are
/// kept in a temporary file, so nothing of the person who runs the tests is touched.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class AiServicesDialogTests : IDisposable
{
    private const string Key = "sk-test-0123456789abcdef";

    private readonly TempFolder _folder = new();

    public AiServicesDialogTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string SettingsFile => _folder.File("ai-services.json");

    private AiServiceSettings Settings(ISecretProtector? protector = null) => AiServiceSettings.Load(SettingsFile, protector ?? new MaskingProtector());

    private static AiServicesDialog Dialog(AiServiceSettings settings, StandInInternet? internet = null, bool dark = false)
    {
        var dialog = new AiServicesDialog(settings, dark, CloudHttp.Create(internet ?? new StandInInternet()));
        dialog.OpenPage = _ => null;
        return dialog;
    }

    private const string ClaudeModels = """{"data":[{"id":"claude-sonnet-5-5"},{"id":"claude-opus-5-5"}]}""";

    // ── What it shows ───────────────────────────────────────────────────────

    [Fact]
    public void ThereIsABoxForEachService_WithItsNameAndTheModelItStartsWith()
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (AiServicesDialog)form;

            Assert.Equal(["OpenAI (ChatGPT)", "Google Gemini", "Anthropic Claude"], AiVendors.All.Select(vendor => dialog.Rows[vendor].Group.Text));
            foreach (var vendor in AiVendors.All)
            {
                var row = dialog.Rows[vendor];
                Assert.Equal(string.Empty, row.KeyBox.Text);
                Assert.Equal(AiVendors.DefaultModel(vendor), row.ModelCombo.Text);
                Assert.False(row.CheckButton.Enabled); // nothing to try yet
                Assert.False(row.RemoveButton.Enabled);
            }

            Assert.Equal(AiServicesStrings.Title, dialog.Text);
            Assert.Equal(AiServicesStrings.Privacy, dialog.PrivacyLabel.Text);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhatIsKept_IsShown_AndTheKeyIsHiddenUntilItIsAskedFor()
    {
        var settings = Settings();
        settings.Set(AiVendor.Claude, Key, "claude-x");
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.Claude];

            Assert.Equal(Key, row.KeyBox.Text);
            Assert.Equal("claude-x", row.ModelCombo.Text);
            Assert.True(row.KeyBox.UseSystemPasswordChar);
            Assert.True(row.CheckButton.Enabled);
            Assert.True(row.RemoveButton.Enabled);

            row.ShowCheck.Checked = true;
            Assert.False(row.KeyBox.UseSystemPasswordChar);
            row.ShowCheck.Checked = false;
            Assert.True(row.KeyBox.UseSystemPasswordChar);

            Assert.Equal(string.Empty, dialog.Rows[AiVendor.OpenAI].KeyBox.Text); // the others have none
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheWindow_IsMadeInBothThemes(bool dark)
    {
        Ui.Run(() => Dialog(Settings(), dark: dark), async form =>
        {
            var dialog = (AiServicesDialog)form;

            Assert.All(dialog.Rows.Values, row => Assert.Equal(dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt, row.KeyBox.BackColor));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TypingAKey_TurnsOnTheButtonsThatNeedOne()
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.Gemini];

            row.KeyBox.Text = "abc";
            Assert.True(row.CheckButton.Enabled);
            Assert.True(row.RemoveButton.Enabled);

            row.KeyBox.Text = "   ";
            Assert.False(row.CheckButton.Enabled);
            await Task.CompletedTask;
        });
    }

    // ── Trying a key ────────────────────────────────────────────────────────

    [Fact]
    public void TryingAKey_AsksTheServiceForItsModels_AndFillsTheList()
    {
        var internet = new StandInInternet().Answer(HttpStatusCode.OK, ClaudeModels);
        Ui.Run(() => Dialog(Settings(), internet), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.Claude];
            row.KeyBox.Text = Key;
            row.ModelCombo.Text = "claude-sonnet-5-5";

            await dialog.CheckAsync(row);

            Assert.Equal(string.Format(AiServicesStrings.StatusCheckedFormat, 2), row.StatusLabel.Text);
            Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5"], row.ModelCombo.Items.Cast<string>());
            Assert.Equal("claude-sonnet-5-5", row.ModelCombo.Text); // what was chosen stays
            Assert.True(row.CheckButton.Enabled);
            Assert.Equal("https://api.anthropic.com/v1/models?limit=1000", internet.Requests[0].Uri.AbsoluteUri);
        });
    }

    [Fact]
    public void AKeyThatIsNotAccepted_ShowsWhatTheServiceSaid_AndTheListStaysAsItWas()
    {
        var internet = new StandInInternet().Answer(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid x-api-key"}}""");
        Ui.Run(() => Dialog(Settings(), internet), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.Claude];
            row.KeyBox.Text = Key;

            await dialog.CheckAsync(row);

            Assert.StartsWith(string.Format(AiServicesStrings.StatusCheckFailedFormat, string.Empty).TrimEnd(), row.StatusLabel.Text);
            Assert.Contains("invalid x-api-key", row.StatusLabel.Text);
            Assert.DoesNotContain(Key, row.StatusLabel.Text);
            Assert.Empty(row.ModelCombo.Items);
            Assert.True(row.CheckButton.Enabled); // it can be tried again with another key
        });
    }

    [Theory]
    [InlineData("", "", nameof(AiServicesStrings.StatusNoKey))]
    [InlineData("two words", "", nameof(AiServicesStrings.StatusKeyBad))]
    [InlineData(Key, "bad model!", nameof(AiServicesStrings.StatusModelBad))]
    public void AKeyOrModelThatCanNotBeOne_IsSaidAtOnce_WithoutAskingTheService(string key, string model, string status)
    {
        var internet = new StandInInternet();
        Ui.Run(() => Dialog(Settings(), internet), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.OpenAI];
            row.KeyBox.Text = key;
            row.ModelCombo.Text = model;

            await dialog.CheckAsync(row);

            var expected = status switch
            {
                nameof(AiServicesStrings.StatusNoKey) => AiServicesStrings.StatusNoKey,
                nameof(AiServicesStrings.StatusKeyBad) => AiServicesStrings.StatusKeyBad,
                _ => AiServicesStrings.StatusModelBad,
            };
            Assert.Equal(expected, row.StatusLabel.Text);
            Assert.Empty(internet.Requests);
        });
    }

    [Fact]
    public void WhileTheServiceIsAsked_TheButtonIsOff_AndClosingTheWindowStopsTheAsk()
    {
        using var asked = new ManualResetEventSlim();
        var internet = new StandInInternet().Answer(async (_, cancel) =>
        {
            asked.Set();
            await Task.Delay(Timeout.Infinite, cancel);
            return new HttpResponseMessage();
        });
        Ui.Run(() => Dialog(Settings(), internet), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.Gemini];
            row.KeyBox.Text = Key;

            var checking = dialog.CheckAsync(row);
            Assert.True(await Ui.Until(() => asked.IsSet));
            Assert.False(row.CheckButton.Enabled);
            Assert.Equal(AiServicesStrings.StatusChecking, row.StatusLabel.Text);

            dialog.Close();
            await checking; // it ends, and does not touch the window that is gone
        });
    }

    // ── The page where a key is made ────────────────────────────────────────

    [Theory]
    [InlineData(AiVendor.OpenAI, "https://platform.openai.com/api-keys")]
    [InlineData(AiVendor.Gemini, "https://aistudio.google.com/apikey")]
    [InlineData(AiVendor.Claude, "https://platform.claude.com/settings/keys")]
    public void TheLink_OpensThePageWhereTheServiceMakesAKey(AiVendor vendor, string page)
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (AiServicesDialog)form;
            string? opened = null;
            dialog.OpenPage = url =>
            {
                opened = url;
                return null;
            };

            Assert.Equal(AiServicesStrings.GetKey, dialog.Rows[vendor].GetLink.Text);
            dialog.GetKey(dialog.Rows[vendor]);

            Assert.Equal(page, opened);
            Assert.StartsWith("https://", page); // ShellOpen.Url hands over nothing but https
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhenThePageCanNotBeOpened_TheBoxSaysWhy()
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.OpenAI];
            dialog.OpenPage = _ => "no browser";

            dialog.GetKey(row);

            Assert.Contains("no browser", row.StatusLabel.Text);
            await Task.CompletedTask;
        });
    }

    // ── OK, Cancel, Remove ──────────────────────────────────────────────────

    [Fact]
    public void OK_KeepsTheKeys_EncryptedInTheFile_AndTheModelsThatAreNotTheDefault()
    {
        var settings = Settings();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            dialog.Rows[AiVendor.OpenAI].KeyBox.Text = "  " + Key + " ";
            dialog.Rows[AiVendor.OpenAI].ModelCombo.Text = "gpt-chosen";
            dialog.Rows[AiVendor.Claude].KeyBox.Text = "claude-key";
            // the model of Claude is left as it is: the default one

            dialog.Accept();

            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            var again = Settings();
            Assert.Equal(new AiAccount(AiVendor.OpenAI, Key, "gpt-chosen"), again.Get(AiVendor.OpenAI));
            Assert.Equal("claude-key", again.Get(AiVendor.Claude)!.ApiKey);
            Assert.False(again.HasKey(AiVendor.Gemini));
            var text = File.ReadAllText(SettingsFile);
            Assert.DoesNotContain(Key, text);
            Assert.DoesNotContain("claude-key", text);
            Assert.DoesNotContain(AiVendors.DefaultModel(AiVendor.Claude), text); // "the default" is not written: a later version of the program may have another
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABoxWithOnlySpaces_HasNoKey_AndIsNotAnError()
    {
        var settings = Settings();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            dialog.Rows[AiVendor.Claude].KeyBox.Text = "     ";
            dialog.Rows[AiVendor.Gemini].KeyBox.Text = Key;

            dialog.Accept();

            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            var again = Settings();
            Assert.False(again.HasKey(AiVendor.Claude));
            Assert.True(again.HasKey(AiVendor.Gemini));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABadKey_KeepsNothing_AndTheWindowStaysOpenWithTheReason()
    {
        var settings = Settings();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            dialog.Rows[AiVendor.OpenAI].KeyBox.Text = Key;
            dialog.Rows[AiVendor.Gemini].KeyBox.Text = "has a space";

            dialog.Accept();

            Assert.NotEqual(DialogResult.OK, dialog.DialogResult);
            Assert.Equal(AiServicesStrings.StatusKeyBad, dialog.Rows[AiVendor.Gemini].StatusLabel.Text);
            Assert.False(File.Exists(SettingsFile));
            Assert.False(settings.HasKey(AiVendor.OpenAI)); // the good one was not kept either: it is all or nothing
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ABadModel_KeepsNothing_AndTheWindowStaysOpenWithTheReason()
    {
        Ui.Run(() => Dialog(Settings()), async form =>
        {
            var dialog = (AiServicesDialog)form;
            dialog.Rows[AiVendor.Claude].KeyBox.Text = Key;
            dialog.Rows[AiVendor.Claude].ModelCombo.Text = "not a model?";

            dialog.Accept();

            Assert.Equal(AiServicesStrings.StatusModelBad, dialog.Rows[AiVendor.Claude].StatusLabel.Text);
            Assert.False(File.Exists(SettingsFile));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Cancel_ChangesNothing()
    {
        var settings = Settings();
        settings.Set(AiVendor.Claude, Key, null);
        settings.Save();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            dialog.Rows[AiVendor.Claude].KeyBox.Text = "changed";
            dialog.Rows[AiVendor.OpenAI].KeyBox.Text = "new";

            dialog.CancelButtonControl.PerformClick();

            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
            Assert.Equal(Key, Settings().Get(AiVendor.Claude)!.ApiKey);
            Assert.False(Settings().HasKey(AiVendor.OpenAI));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void Remove_ForgetsTheKey_WhenOkIsPressed_AndNotBefore()
    {
        var settings = Settings();
        settings.Set(AiVendor.Claude, Key, "claude-x");
        settings.Set(AiVendor.Gemini, "gemini-key", null);
        settings.Engine = AiVendor.Claude;
        settings.Save();
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            var row = dialog.Rows[AiVendor.Claude];

            row.RemoveButton.PerformClick();

            Assert.Equal(string.Empty, row.KeyBox.Text);
            Assert.Equal(AiVendors.DefaultModel(AiVendor.Claude), row.ModelCombo.Text);
            Assert.Equal(AiServicesStrings.StatusRemoved, row.StatusLabel.Text);
            Assert.True(Settings().HasKey(AiVendor.Claude)); // still kept: nothing is final until OK

            dialog.Accept();

            var again = Settings();
            Assert.False(again.HasKey(AiVendor.Claude));
            Assert.True(again.HasKey(AiVendor.Gemini));
            Assert.Null(again.Engine); // the service that was picked has no key any more
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AKeyOfAnotherWindowsAccount_IsNotShown_AndIsNotThrownAwayByOk()
    {
        var other = Settings(new MaskingProtector("OTHER"));
        other.Set(AiVendor.OpenAI, Key, null);
        other.Save();
        var mine = Settings(new MaskingProtector("MINE"));
        Ui.Run(() => Dialog(mine), async form =>
        {
            var dialog = (AiServicesDialog)form;
            Assert.Equal(string.Empty, dialog.Rows[AiVendor.OpenAI].KeyBox.Text); // it can not be read

            dialog.Rows[AiVendor.Claude].KeyBox.Text = "claude-key";
            dialog.Accept();

            Assert.Equal(Key, Settings(new MaskingProtector("OTHER")).Get(AiVendor.OpenAI)!.ApiKey);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void WhenTheFileCanNotBeWritten_TheWindowSaysSo_AndStaysOpen()
    {
        // A file where the folder of the settings should be: the folder can not be made
        File.WriteAllText(_folder.File("in-the-way"), "a file, not a folder");
        var settings = AiServiceSettings.Load(Path.Combine(_folder.File("in-the-way"), "ai-services.json"), new MaskingProtector());
        Ui.Run(() => Dialog(settings), async form =>
        {
            var dialog = (AiServicesDialog)form;
            string? told = null;
            dialog.Warn = text => told = text; // a message box could not be answered by a test
            dialog.Rows[AiVendor.Claude].KeyBox.Text = Key;

            dialog.Accept();

            Assert.NotEqual(DialogResult.OK, dialog.DialogResult);
            Assert.NotNull(told);
            Assert.StartsWith(string.Format(AiServicesStrings.SaveFailedFormat, string.Empty).TrimEnd(), told);
            await Task.CompletedTask;
        });
    }
}
