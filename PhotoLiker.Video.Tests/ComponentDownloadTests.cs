using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace PhotoLiker.Video.Tests;

/// <summary>Getting the files that the installer does not carry (the VLC engine): where they come from, that only what is trusted is used, and that a folder is whole or not there.</summary>
public sealed class ComponentDownloadTests : IDisposable
{
    private const string Ours = "https://ours.example/engine.zip";
    private const string Theirs = "https://theirs.example/vlc-1.0-win64.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLComponent_" + Guid.NewGuid().ToString("N"));
    private string Destination => Path.Combine(_root, "libvlc", "1.0");

    public ComponentDownloadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static readonly string[] Files = ["main.dll", "plugins/a.dll", "plugins/sub/b.dll"];

    private static byte[] Content(string name) => System.Text.Encoding.UTF8.GetBytes("content of " + name);

    private static byte[] Zip(string prefix, IEnumerable<string> names)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in names)
            {
                using var entry = archive.CreateEntry(prefix + name).Open();
                entry.Write(Content(name));
            }
        }
        return memory.ToArray();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ComponentSource Source(string url, byte[] zip, string? sha = null) => new(url, sha ?? Sha(zip), zip.Length);

    private static ComponentPackage Package(params ComponentSource[] sources) => new("The test package", "1.0", "main.dll", Files, sources);

    private sealed class Server : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, byte[] Body)> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.ToString());
            if (!Routes.TryGetValue(request.RequestUri.ToString(), out var route))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(route.Status) { Content = new ByteArrayContent(route.Body) });
        }
    }

    private static Server Serving(params (string Url, byte[] Zip)[] zips)
    {
        var server = new Server();
        foreach (var (url, zip) in zips)
            server.Routes[url] = (HttpStatusCode.OK, zip);
        return server;
    }

    private async Task<string> Install(ComponentPackage package, Server server, IProgress<ComponentProgress>? progress = null, CancellationToken token = default, ComponentSource? preferred = null)
    {
        using var http = new HttpClient(server);
        return await ComponentDownloader.InstallAsync(package, Destination, http, progress, token, preferred);
    }

    private IEnumerable<string> LeftBesideIt() => Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(Destination)!).Select(Path.GetFileName)!;

    // ── a good download ────────────────────────────────────────────────

    [Fact]
    public async Task TheFilesOfThePackage_AreInTheFolder_AndNothingElseIsLeftBesideIt()
    {
        var zip = Zip("", Files);

        var folder = await Install(Package(Source(Ours, zip)), Serving((Ours, zip)));

        Assert.Equal(Destination, folder);
        foreach (var file in Files)
            Assert.Equal(Content(file), File.ReadAllBytes(Path.Combine(Destination, file.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(["1.0"], LeftBesideIt()); // no zip part, no half-made folder
    }

    [Fact]
    public async Task AZipWithOneFolderAroundTheFiles_GivesTheSameFolder_AndOnlyTheListedFilesAreUnpacked()
    {
        var zip = Zip("vlc-1.0/", [.. Files, "vlc.exe", "plugins/unlisted.dll", "skins/default.vlt"]);

        await Install(Package(Source(Theirs, zip)), Serving((Theirs, zip)));

        Assert.Equal(Files.Order(StringComparer.Ordinal), Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Destination, f).Replace('\\', '/')).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnOlderCopyOfThePackage_IsReplaced()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "old.txt"), "old");
        var zip = Zip("", Files);

        await Install(Package(Source(Ours, zip)), Serving((Ours, zip)));

        Assert.False(File.Exists(Path.Combine(Destination, "old.txt")));
        Assert.True(File.Exists(Path.Combine(Destination, "main.dll")));
    }

    [Fact]
    public async Task OtherVersions_AndHalfMadeFolders_AreRemoved_ButWhatIsNoVersionOfThePackageIsNot()
    {
        var parent = Path.GetDirectoryName(Destination)!;
        foreach (var name in new[] { "0.9", "1.0.tmp-abc", "notes" })
            Directory.CreateDirectory(Path.Combine(parent, name));
        File.WriteAllText(Path.Combine(parent, "download-xyz.zip.part"), "part");
        var zip = Zip("", Files);

        await Install(Package(Source(Ours, zip)), Serving((Ours, zip)));

        Assert.Equal(["1.0", "notes"], LeftBesideIt().Order(StringComparer.Ordinal));
    }

    // ── which source is used ───────────────────────────────────────────

    [Fact]
    public async Task ASourceThatIsNotThere_LeavesItToTheNext()
    {
        var zip = Zip("vlc-1.0/", Files);
        var server = Serving((Theirs, zip)); // Ours answers 404

        await Install(Package(Source(Ours, zip), Source(Theirs, zip)), server);

        Assert.Equal([Ours, Theirs], server.Requested);
        Assert.True(File.Exists(Path.Combine(Destination, "main.dll")));
    }

    [Fact]
    public async Task AFileWhoseHashIsNotTheOneExpected_IsNotUsed_TheNextSourceIs()
    {
        var good = Zip("", Files);
        var tampered = Zip("", Files.Append("evil.dll"));
        var server = Serving((Ours, tampered), (Theirs, good));

        await Install(Package(Source(Ours, good), Source(Theirs, good)), server);

        Assert.Equal([Ours, Theirs], server.Requested);
        Assert.False(File.Exists(Path.Combine(Destination, "evil.dll")));
        Assert.Equal(Content("main.dll"), File.ReadAllBytes(Path.Combine(Destination, "main.dll")));
    }

    [Fact]
    public async Task AZipThatLacksAFile_IsNotUsed_TheNextSourceIs()
    {
        var incomplete = Zip("", Files.Take(2));
        var whole = Zip("vlc-1.0/", Files);
        var server = Serving((Ours, incomplete), (Theirs, whole));

        await Install(Package(Source(Ours, incomplete), Source(Theirs, whole)), server);

        Assert.True(File.Exists(Path.Combine(Destination, "plugins", "sub", "b.dll")));
    }

    [Fact]
    public async Task TheSourceThatWasFound_IsTriedFirst()
    {
        var zip = Zip("", Files);
        var server = Serving((Ours, zip), (Theirs, zip));
        var package = Package(Source(Ours, zip), Source(Theirs, zip));

        await Install(package, server, preferred: package.Sources[1]);

        Assert.Equal([Theirs], server.Requested);
    }

    [Fact]
    public async Task WhenNoSourceWorks_ItIsSaid_WhichOnesFailedAndWhy_AndNothingIsLeft()
    {
        var zip = Zip("", Files);
        var server = Serving((Theirs, zip));
        var package = Package(Source(Ours, zip), Source(Theirs, zip, sha: new string('0', 64)));

        var ex = await Assert.ThrowsAsync<IOException>(() => Install(package, server));

        Assert.Contains("ours.example", ex.Message);
        Assert.Contains("theirs.example", ex.Message);
        Assert.Contains("SHA-256", ex.Message);
        Assert.False(Directory.Exists(Destination));
        Assert.Empty(LeftBesideIt());
    }

    // ── never half there ───────────────────────────────────────────────

    [Fact]
    public async Task StoppedWhileUnpacking_LeavesNothing_AndKeepsTheCopyThatWasThere()
    {
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "main.dll"), "the old one");
        var zip = Zip("", Files);
        using var stop = new CancellationTokenSource();
        var progress = new Callback(p => { if (p.Stage == ComponentStage.Unpacking) stop.Cancel(); }); // reported where the work is, so the stop comes at that moment

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Install(Package(Source(Ours, zip)), Serving((Ours, zip)), progress, stop.Token));

        Assert.Equal("the old one", File.ReadAllText(Path.Combine(Destination, "main.dll")));
        Assert.Equal(["1.0"], LeftBesideIt());
    }

    private sealed class Callback(Action<ComponentProgress> action) : IProgress<ComponentProgress>
    {
        public void Report(ComponentProgress value) => action(value);
    }

    [Fact]
    public async Task APathOutOfTheFolder_InTheList_IsRefused()
    {
        string[] files = ["main.dll", "../escaped.dll"];
        var zip = Zip("", files);
        var package = new ComponentPackage("The test package", "1.0", "main.dll", files, [Source(Ours, zip)]);

        await Assert.ThrowsAsync<IOException>(() => Install(package, Serving((Ours, zip))));

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Destination)!, "escaped.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "libvlc", "escaped.dll")));
    }

    // ── what is reported ───────────────────────────────────────────────

    [Fact]
    public async Task TheProgress_SaysTheBytesDownloaded_ThenTheFilesUnpacked()
    {
        var zip = Zip("", Files);
        var seen = new List<ComponentProgress>();

        await Install(Package(Source(Ours, zip)), Serving((Ours, zip)), new Collect(seen));

        var downloading = seen.Where(p => p.Stage == ComponentStage.Downloading).ToList();
        var unpacking = seen.Where(p => p.Stage == ComponentStage.Unpacking).ToList();
        Assert.Equal(zip.Length, downloading[^1].Done);
        Assert.Equal(zip.Length, downloading[^1].Total);
        Assert.Equal([1L, 2L, 3L], unpacking.Select(p => p.Done));
        Assert.All(unpacking, p => Assert.Equal(3, p.Total));
        Assert.True(seen.IndexOf(unpacking[0]) > seen.IndexOf(downloading[^1]));
    }

    private sealed class Collect(List<ComponentProgress> seen) : IProgress<ComponentProgress>
    {
        public void Report(ComponentProgress value) => seen.Add(value);
    }

    // ── finding out where it would come from ───────────────────────────

    [Fact]
    public async Task TheFirstSourceThatAnswers_IsTheOneToBeTold()
    {
        var zip = Zip("", Files);
        var package = Package(Source(Ours, zip), Source(Theirs, zip));
        using var http = new HttpClient(Serving((Theirs, zip)));

        var found = await ComponentDownloader.ResolveAsync(package, http, default);

        Assert.Equal(package.Sources[1], found);
        Assert.Equal("theirs.example", found!.Host);
    }

    [Fact]
    public async Task WhenNoSourceAnswers_ThereIsNone()
    {
        var zip = Zip("", Files);
        using var http = new HttpClient(Serving());

        Assert.Null(await ComponentDownloader.ResolveAsync(Package(Source(Ours, zip), Source(Theirs, zip)), http, default));
    }

    // ── the VLC engine ─────────────────────────────────────────────────

    [Fact]
    public void TheEnginePackage_HasItsFileList_AndTwoTrustedSources()
    {
        var package = VlcEngine.Package;

        Assert.Equal(280, package.Files.Count);
        Assert.Equal(package.Files.Count, package.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("libvlc.dll", package.Files);
        Assert.Contains("libvlccore.dll", package.Files);
        Assert.All(package.Files, f => Assert.False(f.Contains("..") || f.StartsWith('/') || f.Contains('\\') || f.Contains(':'), f));
        Assert.DoesNotContain(package.Files, f => f.EndsWith(".lib") || f.Contains("NOTICE") || f.Contains("COPYING"));

        Assert.Equal(2, package.Sources.Count);
        Assert.All(package.Sources, s =>
        {
            Assert.StartsWith("https://", s.Url);
            Assert.Matches("^[0-9a-f]{64}$", s.Sha256);
            Assert.True(s.Bytes > 10_000_000);
            Assert.Contains(VlcEngine.Version, s.Url);
        });
        Assert.Equal("github.com", package.Sources[0].Host);
        Assert.Equal("download.videolan.org", package.Sources[1].Host);
        Assert.True(package.Sources[0].Bytes < package.Sources[1].Bytes); // the one that is only the engine is the smaller
    }

    [Fact]
    public void TheEngine_IsFoundWhereItIs_TheOneThatComesWithTheProgramFirst()
    {
        var bundled = Path.Combine(_root, "bundled");
        var downloaded = Path.Combine(_root, "downloaded");
        Assert.Null(VlcEngine.Find(bundled, downloaded));

        Directory.CreateDirectory(downloaded);
        File.WriteAllText(Path.Combine(downloaded, "libvlc.dll"), "x");
        Assert.Equal(downloaded, VlcEngine.Find(bundled, downloaded));

        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "libvlc.dll"), "x");
        Assert.Equal(bundled, VlcEngine.Find(bundled, downloaded));
    }

    [Fact]
    public void AFolderWithOnlyTheNotices_IsNoEngine()
    {
        var bundled = Path.Combine(_root, "libvlc", "win-x64");
        Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "VLC-NOTICE.txt"), "notice");

        Assert.Null(VlcEngine.Find(bundled, Path.Combine(_root, "none")));
    }
}
