using System.Net;
using System.Security.Cryptography;
using System.Text;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("v2.1.0", "2.1.0.0")]
    [InlineData("V2.1.0", "2.1.0.0")]
    [InlineData("2.1.0", "2.1.0.0")]
    [InlineData("2.1", "2.1.0.0")]
    [InlineData("v10.0.3", "10.0.3.0")]
    [InlineData("2.1.0-beta.1", "2.1.0.0")]
    [InlineData("2.1.0+4f2a9c1", "2.1.0.0")]
    [InlineData("2.1.0-rc.1+4f2a9c1", "2.1.0.0")]
    [InlineData(" v1.2.3.4 ", "1.2.3.4")]
    public void AVersion_IsReadFromATagOrAVersionText(string text, string expected)
    {
        Assert.True(UpdateChecker.TryParseVersion(text, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("release-2")]
    [InlineData("2")]
    [InlineData("v2.x.0")]
    public void ATextThatIsNoVersion_IsNotOne(string? text) => Assert.False(UpdateChecker.TryParseVersion(text, out _));

    [Fact]
    public void Versions_AreComparedByTheirNumbers_NotTheirLetters()
    {
        UpdateChecker.TryParseVersion("v2.10.0", out var ten);
        UpdateChecker.TryParseVersion("v2.9.0", out var nine);
        UpdateChecker.TryParseVersion("v2.9", out var nineShort);
        UpdateChecker.TryParseVersion("v2.9.0", out var nineLong);

        Assert.True(ten > nine);
        Assert.Equal(nineShort, nineLong); // 2.9 is 2.9.0
    }

    [Theory]
    [InlineData("2.2.0", "2.1.0", true)]
    [InlineData("2.10.0", "2.9.0", true)]
    [InlineData("3.0.0", "2.99.99", true)]
    [InlineData("2.1.1", "2.1", true)]
    [InlineData("2.1.0", "2.1.0", false)]
    [InlineData("2.1.0.0", "2.1.0", false)] // the Version class alone would put 2.1.0 before 2.1.0.0
    [InlineData("2.1", "2.1.0.0", false)]
    [InlineData("2.0.0", "2.1.0", false)]
    public void ANewerVersion_IsOneThatComesAfter_WhateverHowManyNumbersItHas(string latest, string installed, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(installed)));

    [Theory]
    [InlineData("2.1.0", "2.1.0.0")]
    [InlineData("2.1.0+4f2a9c1", "2.1.0.0")]
    [InlineData("2.1.0-beta+4f2a9c1", "2.1.0.0")]
    public void TheRunningRelease_IsReadFromTheStampOfTheBuild(string stamp, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ReleaseVersionOf(stamp));

    [Theory]
    [InlineData("0.0.0-dev")]
    [InlineData("0.0.0-dev+4f2a9c1")]
    [InlineData("0.0.0")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    public void ABuildThatIsNoRelease_HasNoReleaseVersion(string? stamp) => Assert.Null(UpdateChecker.ReleaseVersionOf(stamp));
}

public class UpdateReleaseTests
{
    private const string Setup = "PhotoLiker-Setup-2.1.0.exe";
    private const string Sha = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";

    private static string Json(string tag = "v2.1.0", string? assets = null) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Photo Liker 2.1.0",
          "html_url": "https://evil.example/not-used",
          "draft": false,
          "prerelease": false,
          "assets": [{{assets ?? SetupAsset()}}]
        }
        """;

    private static string SetupAsset(string name = Setup, string? url = null, string digest = "sha256:" + Sha, long size = 31511771) => $$"""
        {
          "name": "{{name}}",
          "size": {{size}},
          "digest": "{{digest}}",
          "browser_download_url": "{{url ?? $"https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/{name}"}}"
        }
        """;

    private const string ZipAsset = """
        { "name": "PhotoLiker-2.1.0-win-x64.zip", "size": 1000, "browser_download_url": "https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/PhotoLiker-2.1.0-win-x64.zip" }
        """;

    [Fact]
    public void TheSetup_IsTheAssetThatIsNamedLikeOne_AmongTheOthers()
    {
        var release = UpdateChecker.Parse(Json(assets: ZipAsset + "," + SetupAsset()));

        Assert.Equal(new Version(2, 1, 0, 0), release.Version);
        Assert.Equal("v2.1.0", release.Tag);
        Assert.Equal(Setup, release.InstallerName);
        Assert.Equal($"https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/{Setup}", release.InstallerUrl);
        Assert.Equal(31511771, release.InstallerBytes);
        Assert.Equal(Sha, release.InstallerSha256);
    }

    [Fact]
    public void ThePage_IsBuiltFromTheTag_NotTakenFromTheAnswer()
    {
        var release = UpdateChecker.Parse(Json());

        Assert.Equal("https://github.com/YBalan/PhotoLiker/releases/tag/v2.1.0", release.PageUrl);
    }

    [Fact]
    public void AReleaseWithoutASetup_HasNothingToDownload()
    {
        var release = UpdateChecker.Parse(Json(assets: ZipAsset));

        Assert.Null(release.InstallerUrl);
        Assert.Null(release.InstallerName);
    }

    [Fact]
    public void ASetupThatIsNotAtTheReleasesOfTheProject_IsNotOffered()
    {
        var elsewhere = SetupAsset(url: "https://evil.example/PhotoLiker-Setup-2.1.0.exe");
        var otherProject = SetupAsset(url: "https://github.com/someone/else/releases/download/v2.1.0/" + Setup);

        Assert.Null(UpdateChecker.Parse(Json(assets: elsewhere)).InstallerUrl);
        Assert.Null(UpdateChecker.Parse(Json(assets: otherProject)).InstallerUrl);
    }

    [Fact]
    public void ASetupWithoutADigest_IsStillAReleaseToInstall()
    {
        var asset = """
            { "name": "PhotoLiker-Setup-2.1.0.exe", "size": 5, "browser_download_url": "https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/PhotoLiker-Setup-2.1.0.exe" }
            """;

        var release = UpdateChecker.Parse(Json(assets: asset));

        Assert.NotNull(release.InstallerUrl);
        Assert.Null(release.InstallerSha256);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{ "name": "no tag" }""")]
    public void AnAnswerThatIsNotARelease_IsAnInvalidDataError(string json) =>
        Assert.Throws<InvalidDataException>(() => UpdateChecker.Parse(json));

    [Fact]
    public void AReleaseWithoutAssets_HasATagAndNothingToDownload()
    {
        var release = UpdateChecker.Parse("""{ "tag_name": "v2.1.0" }""");

        Assert.Equal(new Version(2, 1, 0, 0), release.Version);
        Assert.Null(release.InstallerUrl);
    }

    [Fact]
    public void ATagThatIsNoVersion_IsAnError()
    {
        var error = Assert.Throws<InvalidDataException>(() => UpdateChecker.Parse(Json(tag: "nightly")));

        Assert.Contains("nightly", error.Message);
    }

    [Fact]
    public void AnotherRepository_IsUsedForTheAddresses()
    {
        var asset = SetupAsset(url: $"https://github.com/me/mine/releases/download/v2.1.0/{Setup}");

        var release = UpdateChecker.Parse(Json(assets: asset), "me/mine");

        Assert.Equal("https://github.com/me/mine/releases/tag/v2.1.0", release.PageUrl);
        Assert.NotNull(release.InstallerUrl);
    }
}

public sealed class UpdateServerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PhotoLikerUpdate_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static ReleaseInfo ReleaseOf(byte[] content, long? size = null, string? sha = null) => new(
        new Version(2, 1, 0, 0), "v2.1.0", "https://github.com/YBalan/PhotoLiker/releases/tag/v2.1.0",
        "PhotoLiker-Setup-2.1.0.exe", "https://github.com/YBalan/PhotoLiker/releases/download/v2.1.0/PhotoLiker-Setup-2.1.0.exe",
        size ?? content.Length, sha ?? Sha256(content));

    [Fact]
    public async Task TheLatestRelease_IsAskedOfTheApiOfTheProject_WithAUserAgent()
    {
        var fake = new FakeGitHub(_ => Ok("""{ "tag_name": "v2.1.0", "assets": [] }"""));
        var checker = new UpdateChecker(new HttpClient(fake));

        var release = await checker.GetLatestAsync(CancellationToken.None);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("https://api.github.com/repos/YBalan/PhotoLiker/releases/latest", request.RequestUri!.ToString());
        Assert.NotEmpty(request.Headers.UserAgent); // GitHub refuses a request without one
        Assert.Equal(new Version(2, 1, 0, 0), release!.Version);
    }

    [Fact]
    public async Task NoReleaseYet_IsNull_NotAnError()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        Assert.Null(await checker.GetLatestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] // the rate limit
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task GitHubRefusing_IsAnHttpError(HttpStatusCode status)
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(status))));

        await Assert.ThrowsAsync<HttpRequestException>(() => checker.GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnAnswerThatIsNotJson_IsAnInvalidDataError()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Ok("<html>maintenance</html>"))));

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ACancelledCheck_IsCancelled()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Ok("{}"))));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.GetLatestAsync(cancelled.Token));
    }

    [Fact]
    public async Task TheSetup_IsDownloadedChecked_AndItsProgressReported()
    {
        var content = Enumerable.Range(0, 400_000).Select(i => (byte)(i * 31)).ToArray();
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));
        var steps = new List<(long Done, long Total)>();

        var path = await checker.DownloadInstallerAsync(ReleaseOf(content), _folder, new SyncProgress(steps.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_folder, "PhotoLiker-Setup-2.1.0.exe"), path);
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal((content.Length, content.Length), steps[^1]);
        Assert.Equal(steps.Select(step => step.Done).Order(), steps.Select(step => step.Done)); // only forward
    }

    [Fact]
    public async Task ASetupOfTheWrongSize_IsThrownAway()
    {
        var content = new byte[10_000];
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            checker.DownloadInstallerAsync(ReleaseOf(content, size: content.Length + 1), _folder, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task ASetupWithTheWrongChecksum_IsThrownAway()
    {
        var content = new byte[10_000];
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            checker.DownloadInstallerAsync(ReleaseOf(content, sha: new string('0', 64)), _folder, null, CancellationToken.None));

        Assert.Contains("damaged", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task TheChecksum_IsComparedWhateverItsCase()
    {
        var content = new byte[1_000];
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));

        var path = await checker.DownloadInstallerAsync(ReleaseOf(content, sha: Sha256(content).ToUpperInvariant()), _folder, null, CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ASetupWithoutAChecksum_IsCheckedBySizeAlone()
    {
        var content = new byte[1_000];
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));
        var release = ReleaseOf(content) with { InstallerSha256 = null };

        Assert.True(File.Exists(await checker.DownloadInstallerAsync(release, _folder, null, CancellationToken.None)));
    }

    [Fact]
    public async Task OlderSetups_AreDeletedBeforeANewOneIsDownloaded_AndOtherFilesAreLeftAlone()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "PhotoLiker-Setup-2.0.0.exe"), "old");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "mine");
        var content = new byte[1_000];
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));

        await checker.DownloadInstallerAsync(ReleaseOf(content), _folder, null, CancellationToken.None);

        Assert.Equal(["PhotoLiker-Setup-2.1.0.exe", "notes.txt"], Directory.EnumerateFiles(_folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ADownloadThatFails_LeavesNothingBehind()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            checker.DownloadInstallerAsync(ReleaseOf(new byte[10]), _folder, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task AReleaseWithoutASetup_CannotBeDownloaded()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes([]))));
        var release = ReleaseOf(new byte[10]) with { InstallerUrl = null, InstallerName = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() => checker.DownloadInstallerAsync(release, _folder, null, CancellationToken.None));
    }

    [Fact]
    public async Task TheNameOfTheSetup_CannotLeaveTheFolder()
    {
        var content = new byte[100];
        var checker = new UpdateChecker(new HttpClient(new FakeGitHub(_ => Bytes(content))));
        var release = ReleaseOf(content) with { InstallerName = @"..\..\PhotoLiker-Setup-evil.exe" };

        var path = await checker.DownloadInstallerAsync(release, _folder, null, CancellationToken.None);

        Assert.Equal(_folder, Path.GetDirectoryName(path));
    }

    /// <summary>Progress that reports on the spot (Progress&lt;T&gt; posts to another thread, which a test cannot wait for).</summary>
    private sealed class SyncProgress(Action<(long Done, long Total)> report) : IProgress<(long Done, long Total)>
    {
        public void Report((long Done, long Total) value) => report(value);
    }
}
