using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PhotoLiker.Core;

namespace SmokeTour;

/// <summary>
/// The "Check for updates" button of the About box, in the real box. GitHub is a stand-in (a handler that answers with a release or with an
/// error), the setup is not started (the tour takes that call) and no question is shown: nothing goes to the network and nothing is installed.
/// </summary>
internal sealed partial class Tour
{
    private const string FakeRepository = "https://github.com/YBalan/PhotoLiker/releases";

    /// <summary>The last step of the dialogs run: installing closes the window of the program, as it does for a person.</summary>
    private async Task AboutUpdate()
    {
        checks.Step("3b. About: Check for updates (GitHub is a stand-in, no setup is started)");

        var launcher = typeof(PhotoLikerUI.MainForm).Assembly.GetType("PhotoLikerUI.UpdateLauncher")!;
        void Set(string name, object? value) => launcher.GetProperty(name)!.SetValue(null, value);

        var setupBytes = new byte[300_000];
        new Random(7).NextBytes(setupBytes);
        var sha = Convert.ToHexString(SHA256.HashData(setupBytes)).ToLowerInvariant();
        var downloadFolder = Path.Combine(Path.GetTempPath(), "PhotoLikerSmokeUpdate_" + Guid.NewGuid().ToString("N"));
        var opened = new List<string>();
        var questions = new List<string>();
        var answer = false;
        var stage = "release"; // what the stand-in says: "release", "same", "down"

        var requests = new List<string>();
        var handler = new StandInGitHub(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            if (request.RequestUri.Host == "github.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(setupBytes) };

            var version = stage == "same" ? "2.1.0" : "2.2.0";
            return stage == "down"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        { "tag_name": "v{{version}}", "assets": [ {
                          "name": "PhotoLiker-Setup-{{version}}.exe", "size": {{setupBytes.Length}}, "digest": "sha256:{{sha}}",
                          "browser_download_url": "{{FakeRepository}}/download/v{{version}}/PhotoLiker-Setup-{{version}}.exe" } ] }
                        """, Encoding.UTF8, "application/json")
                };
        });

        Set("Checker", new UpdateChecker(new HttpClient(handler)));
        Set("InstalledVersion", new Version(2, 1, 0));
        Set("InstalledBySetup", true);
        Set("DownloadFolder", downloadFolder);
        Set("OpenPage", (Func<string, string?>)(url => { opened.Add(url); return null; }));
        Set("Confirm", (Func<IWin32Window, string, bool>)((_, text) => { questions.Add(text); return answer; }));

        var help = form.ToolbarItem("helpToolStripButton");
        form.BeginInvoke(() => help.PerformClick());
        Form? dialog = null;
        checks.That(await Until(() => (dialog = OpenDialog(AboutDialogType)) is not null), "the Help button opens the About box");
        if (dialog is null)
            return;

        var check = Field<Button>(dialog, "_check");
        var install = Field<Button>(dialog, "_install");
        var releasePage = Field<Button>(dialog, "_releasePage");
        var status = Field<Label>(dialog, "_status");

        checks.That(check.Visible && check.Enabled && !install.Visible && !releasePage.Visible && status.Text.Length == 0,
            "at first there is the button and nothing else, and GitHub has not been asked");
        checks.That(requests.Count == 0, "nothing was asked of GitHub just by opening the box");

        // 1. the same version
        stage = "same";
        check.PerformClick();
        checks.That(await Until(() => status.Text.Contains("latest version (2.1.0)")), $"the same version is the latest ('{status.Text}')");
        checks.That(!install.Visible && !releasePage.Visible && check.Enabled, "nothing to install, and the button can be pressed again");
        checks.That(requests.Single().EndsWith("/repos/YBalan/PhotoLiker/releases/latest"), $"it asked the API of the project once ({string.Join(", ", requests)})");

        // 2. GitHub is down
        stage = "down";
        check.PerformClick();
        checks.That(await Until(() => status.Text.StartsWith("Could not check")), $"an error of GitHub is said in words ('{status.Text}')");
        checks.That(!install.Visible && check.Enabled, "and the button can be pressed again");

        // 3. a newer version, this copy came from the setup
        stage = "release";
        check.PerformClick();
        checks.That(await Until(() => status.Text.Contains("Version 2.2.0 is available (you have 2.1.0)")), $"a newer version is announced ('{status.Text}')");
        checks.That(install.Visible && install.Text == "Install 2.2.0" && releasePage.Visible, $"with the buttons '{install.Text}' and '{releasePage.Text}'");
        await DialogShot(dialog, "27-about-update");

        releasePage.PerformClick();
        checks.That(opened.SequenceEqual([FakeRepository + "/tag/v2.2.0"]), $"the release page opens the page of the tag ({string.Join(", ", opened)})");

        // 4. a newer version, this copy is a zip or a build from source
        Set("InstalledBySetup", false);
        check.PerformClick();
        checks.That(await Until(() => status.Text.Contains("not put here by the setup")), $"a copy that is not installed is sent to the release page ('{status.Text}')");
        checks.That(!install.Visible && releasePage.Visible, "no Install button for it");
        Set("InstalledBySetup", true);

        // 5. a build that is no release
        Set("InstalledVersion", null);
        check.PerformClick();
        checks.That(await Until(() => status.Text.Contains("development build")), $"a build from source says what it is ('{status.Text}')");
        checks.That(!install.Visible && releasePage.Visible, "and is not offered a setup");
        Set("InstalledVersion", new Version(2, 1, 0));

        // 6. install: No to the question first, then Yes
        check.PerformClick();
        checks.That(await Until(() => install.Visible), "the newer version is found again");
        var downloadsBefore = requests.Count(url => url.Contains("/download/"));
        install.PerformClick();
        await Settle(300);
        checks.That(questions.Count == 1 && questions[0].Contains("2.2.0") && questions[0].Contains("closes"), "Install asks first, and says that the program closes");
        checks.That(requests.Count(url => url.Contains("/download/")) == downloadsBefore && !Directory.Exists(downloadFolder), "and answering No downloads nothing");
        checks.That(OpenDialog(AboutDialogType) is not null && install.Visible, "and the box stays as it was");

        var ran = false;
        var answeredAt = DateTime.UtcNow;
        Set("RunSetup", (Func<string, string?>)(path =>
        {
            // Called by the window as the last thing it does, when it is closed and everything is saved (so the checks are made here: the tour's own
            // code does not run any more, the window it runs in is gone)
            ran = true;
            var file = new FileInfo(path);
            checks.That(file.Exists && file.Length == setupBytes.Length && File.ReadAllBytes(path).SequenceEqual(setupBytes), "Yes: the setup was downloaded, whole, to " + path);
            checks.That(Path.GetFileName(path) == "PhotoLiker-Setup-2.2.0.exe" && Path.GetDirectoryName(path) == downloadFolder, "under the name GitHub gave it");
            checks.That(!Directory.EnumerateFiles(downloadFolder, "*.part").Any(), "nothing half-downloaded is left");
            checks.That(File.GetLastWriteTimeUtc(Path.Combine(AppPaths.DataFolder, "global-config.json")) >= answeredAt, "the window had saved its settings (as it closed) before the setup was started");
            var arguments = (string)launcher.GetField("SetupArguments")!.GetRawConstantValue()!;
            checks.That(arguments.Contains("/SILENT") && arguments.Contains("/Relaunch=1"), $"and it is run silently, to start the program again ({arguments})");
            try { Directory.Delete(downloadFolder, recursive: true); } catch { /* temp files */ }
            return null;
        }));

        answer = true;
        install.PerformClick();
        checks.That(await Until(() => ran, 20000), "the box closed, then the window, which started the setup");
        checks.That(questions.Count == 2, "after a second question");
    }

    private sealed class StandInGitHub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
