using System.Net;
using System.Reflection;
using System.Text;
using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The About box when "Check for updates" finds a newer version: the sentences of the changelog that say what is new, in the real box. GitHub is a stand-in (nothing goes to the network).
/// Each test is something a person would have run into: the list not there, there with the sentences of the version they already have, a box that grew past the bottom of a small monitor.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class AboutDialogChangesTests : IDisposable
{
    private const string Downloads = "https://github.com/YBalan/PhotoLiker/releases/download/v2.2.0/";
    private static readonly string NewLine = Environment.NewLine;

    private readonly UpdateChecker _checkerBefore = UpdateLauncher.Checker;
    private readonly Version? _installedBefore = UpdateLauncher.InstalledVersion;
    private readonly bool _bySetupBefore = UpdateLauncher.InstalledBySetup;

    private readonly List<string> _requests = [];
    private string? _changelog;           // what the stand-in sends as CHANGELOG.md; null: the release carries none
    private HttpStatusCode _changelogStatus = HttpStatusCode.OK;
    private string _latest = "2.2.0";
    private TaskCompletionSource? _gate;  // while set, the stand-in does not answer

    public AboutDialogChangesTests()
    {
        UpdateLauncher.InstalledBySetup = true;
        UpdateLauncher.Checker = new UpdateChecker(new HttpClient(new StandIn(async request =>
        {
            if (_gate is { } gate)
                await gate.Task; // a slow GitHub: the answer comes when the test lets it
            return Answer(request);
        })));
    }

    public void Dispose()
    {
        UpdateLauncher.Checker = _checkerBefore;
        UpdateLauncher.InstalledVersion = _installedBefore;
        UpdateLauncher.InstalledBySetup = _bySetupBefore;
    }

    private sealed class StandIn(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);
    }

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        _requests.Add(url);

        if (url.EndsWith("/CHANGELOG.md"))
        {
            return _changelogStatus == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new UTF8Encoding(false).GetBytes(_changelog ?? string.Empty)) }
                : new HttpResponseMessage(_changelogStatus);
        }

        var changelogAsset = _changelog is null ? "" : $$""", { "name": "CHANGELOG.md", "size": 100, "browser_download_url": "{{Downloads}}CHANGELOG.md" }""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                { "tag_name": "v{{_latest}}", "assets": [
                    { "name": "PhotoLiker-Setup-{{_latest}}.exe", "size": 1000, "browser_download_url": "{{Downloads}}PhotoLiker-Setup-{{_latest}}.exe" }{{changelogAsset}} ] }
                """,
                Encoding.UTF8, "application/json")
        };
    }

    private static string Changelog(params (string Version, string[] Sentences)[] sections) =>
        "# Changelog\n\n" + string.Concat(sections.Select(s => $"## {s.Version} - 2026-11-30\n\n" + string.Concat(s.Sentences.Select(sentence => $"- {sentence}\n")) + "\n"));

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    // The box is made to fit the working area it is given, and Windows does not let a window be taller than the real screen: a test that is not about small monitors gives the area of the screen it runs on
    // (the build machine of the release has a 1024x768 one: a made-up 1920x1040 let the box grow past it, and its lower rows were cut off)
    private static Rectangle RealScreen => Screen.PrimaryScreen!.WorkingArea;

    /// <summary>A monitor of this working-area size, but not bigger than the one the test runs on: Windows cuts a window to the real screen, so a made-up 1920x1040 asked of a 1024x768 screen proves nothing about the box (it failed on the build machine of the release).</summary>
    private static Rectangle Monitor(int width, int height) => new(0, 0, Math.Min(width, RealScreen.Width), Math.Min(height, RealScreen.Height));

    /// <summary>Opens the About box on the given part of the screen, as the version <paramref name="installed"/>, presses Check for updates and runs the body once it has answered.</summary>
    private void Run(Version? installed, Func<AboutDialog, Task> body, Rectangle? area = null, bool dark = false)
    {
        UpdateLauncher.InstalledVersion = installed;
        Ui.Run(() => new AboutDialog(dark, area ?? RealScreen), async form =>
        {
            var dialog = (AboutDialog)form;
            var check = Field<Button>(dialog, "_check");
            check.PerformClick();
            Assert.True(await Ui.Until(() => check.Enabled && Field<Label>(dialog, "_status").Text.Length > 0 && !Field<Label>(dialog, "_status").Text.StartsWith("Asking")), "the check answers");
            await body(dialog);
        });
    }

    [Fact]
    public void ANewVersion_ShowsWhatIsNewInIt_UnderTheAnswer()
    {
        _changelog = Changelog(("2.2.0", ["Added the book.", "Fixed the crash."]), ("2.1.0", ["Added the thing you have."]));

        Run(new Version(2, 1, 0), dialog =>
        {
            var notes = Field<TextBox>(dialog, "_changes");
            var heading = Field<Label>(dialog, "_changesHeading");

            Assert.Contains("Version 2.2.0 is available", Field<Label>(dialog, "_status").Text);
            Assert.True(notes.Visible && heading.Visible, "the list is shown");
            Assert.Equal("What's new", heading.Text);
            Assert.Equal("• Added the book." + NewLine + "• Fixed the crash.", notes.Text); // the one version that is new: no heading of its own, and not what 2.1.0 had
            Assert.Equal(ScrollBars.None, notes.ScrollBars);

            // From the top: the answer, the heading, the sentences, (the progress bar, when the setup comes down), the manual buttons
            var status = Field<Label>(dialog, "_status");
            var progress = Field<ProgressBar>(dialog, "_progress");
            var buttons = Ui.ButtonStartingWith(dialog, "User manual");
            Assert.True(status.Bottom <= heading.Top && heading.Bottom <= notes.Top, $"the heading is between the answer and the sentences: {status.Bounds} {heading.Bounds} {notes.Bounds}");
            Assert.True(notes.Bottom <= dialog.ClientSize.Height, "the sentences are inside the box");

            progress.Visible = true;
            Assert.True(notes.Bottom <= progress.Top && progress.Bottom <= buttons.Parent!.Top + buttons.Top, $"the progress bar is under the sentences and above the buttons: {notes.Bounds} {progress.Bounds}");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void SeveralNewVersions_AreListedNewestFirst_EachUnderItsNumber_AndNotTheOneThatIsInstalled()
    {
        _changelog = Changelog(("2.2.0", ["Added c."]), ("2.1.0", ["Added b."]), ("2.0.0", ["Added a."]), ("1.9.0", ["Added z."]));

        Run(new Version(2, 0, 0), dialog =>
        {
            var text = Field<TextBox>(dialog, "_changes").Text;

            Assert.Equal($"Version 2.2.0{NewLine}• Added c.{NewLine}{NewLine}Version 2.1.0{NewLine}• Added b.", text);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheLatestVersion_NeedsNoList()
    {
        _latest = "2.1.0";
        _changelog = Changelog(("2.1.0", ["Added b."]));

        Run(new Version(2, 1, 0), dialog =>
        {
            Assert.Contains("latest version", Field<Label>(dialog, "_status").Text);
            Assert.False(Field<TextBox>(dialog, "_changes").Visible || Field<Label>(dialog, "_changesHeading").Visible);
            Assert.DoesNotContain(_requests, url => url.EndsWith("/CHANGELOG.md")); // not even asked for
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ABuildThatIsNoRelease_IsToldWhatIsNewInTheLatestVersionAlone()
    {
        _changelog = Changelog(("2.2.0", ["Added c."]), ("2.1.0", ["Added b."]));

        Run(null, dialog =>
        {
            Assert.Equal("• Added c.", Field<TextBox>(dialog, "_changes").Text);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AReleaseWithoutAChangelog_IsStillOffered_WithoutTheList()
    {
        _changelog = null;

        Run(new Version(2, 1, 0), dialog =>
        {
            Assert.True(Field<Button>(dialog, "_install").Visible, "the setup is offered");
            Assert.False(Field<TextBox>(dialog, "_changes").Visible || Field<Label>(dialog, "_changesHeading").Visible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AChangelogThatCannotBeGot_IsStillOffered_WithoutTheList_AndWithoutAnError()
    {
        _changelog = "x";
        _changelogStatus = HttpStatusCode.NotFound;

        Run(new Version(2, 1, 0), dialog =>
        {
            var status = Field<Label>(dialog, "_status").Text;

            Assert.Contains("Version 2.2.0 is available", status);
            Assert.True(Field<Button>(dialog, "_install").Visible, "the setup is offered");
            Assert.False(Field<TextBox>(dialog, "_changes").Visible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void AChangelogWithNothingNewInIt_ShowsNoEmptyBox()
    {
        _changelog = Changelog(("2.1.0", ["Added b."]));

        Run(new Version(2, 1, 0), dialog =>
        {
            Assert.True(Field<Button>(dialog, "_install").Visible);
            Assert.False(Field<TextBox>(dialog, "_changes").Visible);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ALongList_ScrollsInsteadOfGrowingTheBox_AndIsAllThere()
    {
        var sentences = Enumerable.Range(1, 40).Select(i => $"Added feature number {i}, which is described in one sentence that is long enough to be wrapped in the box.").ToArray();
        _changelog = Changelog(("2.2.0", sentences));

        Run(new Version(2, 1, 0), dialog =>
        {
            var notes = Field<TextBox>(dialog, "_changes");
            var row = TextRenderer.MeasureText("Ag", notes.Font).Height;

            Assert.Equal(ScrollBars.Vertical, notes.ScrollBars);
            Assert.InRange(notes.ClientSize.Height, row * 5, row * 9); // some rows, not forty
            Assert.Equal(40, notes.Lines.Length); // every sentence is in the box (one line each before wrapping)
            Assert.Contains("number 40,", notes.Text);
            return Task.CompletedTask;
        }, dark: true);
    }

    [Theory]
    [InlineData(1024, 728)] // a 1024x768 screen less its taskbar: what the build machine of the release has
    [InlineData(1280, 680)]
    [InlineData(1366, 728)]
    [InlineData(1920, 1040)]
    public void WhateverTheMonitor_TheBoxStaysInItsWorkingArea_WithTheList(int width, int height)
    {
        var area = Monitor(width, height); // a row that is bigger than this machine's screen is tried at the size of the screen
        _changelog = Changelog(("2.2.0", Enumerable.Range(1, 12).Select(i => $"Added feature number {i}, described in a sentence that goes on for a good while so that it wraps.").ToArray()));

        Run(new Version(2, 1, 0), dialog =>
        {
            Assert.True(Field<TextBox>(dialog, "_changes").Visible);
            Assert.True(area.Contains(dialog.Bounds), $"the box {dialog.Bounds} is inside the working area {area}");

            // And nothing of it is cut off: the last row (the buttons) is inside the window, which is what a box taller than the screen does not have
            var lastRow = Ui.ButtonStartingWith(dialog, "OK").Parent!;
            Assert.True(lastRow.Bottom <= dialog.ClientSize.Height, $"the buttons end at {lastRow.Bottom}, the window at {dialog.ClientSize.Height}");
            return Task.CompletedTask;
        }, area);
    }

    [Fact]
    public void WhenTheListIsHiddenAgain_TheKeysAreAsTheyWere()
    {
        // On a small monitor the list of keys gave way to the sentences (it scrolls); a second check that finds nothing new must give it all its room back
        var area = Monitor(1280, 680);
        _changelog = Changelog(("2.2.0", Enumerable.Range(1, 12).Select(i => $"Added feature number {i}, described in a sentence that goes on for a good while so that it wraps.").ToArray()));
        Size withoutList = default;
        int heightWithout = 0;

        UpdateLauncher.InstalledVersion = new Version(2, 1, 0);
        Ui.Run(() => new AboutDialog(dark: false, area), async form =>
        {
            var dialog = (AboutDialog)form;
            var keys = Ui.Controls<TextBox>(dialog).First();
            var check = Field<Button>(dialog, "_check");
            var status = Field<Label>(dialog, "_status");
            withoutList = keys.ClientSize;
            heightWithout = dialog.Height;

            check.PerformClick();
            Assert.True(await Ui.Until(() => check.Enabled && status.Text.Contains("is available")), "the first check finds the new version");
            Assert.True(Field<TextBox>(dialog, "_changes").Visible);
            Assert.True(dialog.Height > heightWithout - 5, "the box did not shrink because of the list");

            _latest = "2.1.0"; // the second check finds nothing new, and GitHub is slow to say so
            _gate = new TaskCompletionSource();
            check.PerformClick();
            Assert.True(await Ui.Until(() => status.Text.StartsWith("Asking")), "the second check is waiting for GitHub");
            Assert.False(Field<TextBox>(dialog, "_changes").Visible || Field<Label>(dialog, "_changesHeading").Visible, "the old list is gone while it waits: it is not an answer to this question");
            _gate.SetResult();
            Assert.True(await Ui.Until(() => check.Enabled && status.Text.Contains("latest version")), "the second check finds nothing new");

            Assert.False(Field<TextBox>(dialog, "_changes").Visible);
            Assert.Equal(withoutList, keys.ClientSize);
            Assert.InRange(dialog.Height, heightWithout - 3, heightWithout + 3);
        });
    }
}
