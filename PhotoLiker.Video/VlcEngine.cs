using System.Net.Http;
using PhotoLiker.Core;

namespace PhotoLiker.Video
{
    /// <summary>
    /// The engine that plays videos (VLC's libvlc): where it is, and getting it. A build that bundles it (the default for a build from the source) has it in libvlc\win-x64 next
    /// to the program. A release does not (-p:BundleVlc=false, see installer\build-installer.ps1; the engine is 23 MB of the installer): the program downloads the engine
    /// the first time videos are turned on or played, into %LocalAppData%\PhotoLiker\libvlc\&lt;version&gt;, and finds it there from then on.
    /// </summary>
    public static class VlcEngine
    {
        /// <summary>The version of libvlc that is downloaded. The same as the version of VideoLAN.LibVLC.Windows in PhotoLikerUI.csproj (installer\pack-libvlc.ps1 reads it from there).</summary>
        public const string Version = "3.0.24";

        /// <summary>The file that tells that the engine is there.</summary>
        public const string MainFile = "libvlc.dll";

        private static readonly HttpClient Http = NewClient();

        /// <summary>
        /// Where the engine can be downloaded from, the best first. Both zips hold the same 280 files (byte for byte; <c>libvlc-files.txt</c> lists them, and only those are
        /// unpacked). The first is the engine alone, 40 MB: made by installer\pack-libvlc.ps1 and attached to a GitHub release of this project (a pre-release, so that "Check for
        /// updates" does not take it for a version of the program). The second is VideoLAN's own zip of the whole player, 83 MB, which is there whether or not the first has been
        /// published. The SHA-256 of a zip is what makes it trusted: the first is the one that pack-libvlc.ps1 printed, the second is the one VideoLAN publishes beside its zip.
        /// </summary>
        public static IReadOnlyList<ComponentSource> Sources { get; } =
        [
            new($"https://github.com/{UpdateChecker.Repository}/releases/download/libvlc-{Version}/libvlc-{Version}-win-x64.zip",
                "20f77b4846d36191b10d42f74e5e4d044681d6018f35b33b001fcee410b5a77b", 41_686_049),
            new($"https://download.videolan.org/pub/videolan/vlc/{Version}/win64/vlc-{Version}-win64.zip",
                "fcf30850371ad10c9373cc4f0f4501e7dee49e3e9ae9f20c72fb2661a1ca6323", 82_765_861),
        ];

        /// <summary>The engine as a package: its 280 files (listed in libvlc-files.txt, which is part of this library) and where to get them.</summary>
        public static ComponentPackage Package { get; } = new("The video player", Version, MainFile, ReadFileList(), Sources);

        /// <summary>The folder of the engine that comes with the program (it is not there in a release).</summary>
        public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");

        /// <summary>The folder the engine is downloaded to.</summary>
        public static string DownloadedFolder => Path.Combine(AppPaths.DataFolder, "libvlc", Version);

        /// <summary>The folder that has libvlc.dll, the one that comes with the program first, else the downloaded one; null if there is none (it has to be downloaded).</summary>
        public static string? Find() => Find(BundledFolder, DownloadedFolder);

        internal static string? Find(string bundledFolder, string downloadedFolder) =>
            File.Exists(Path.Combine(bundledFolder, MainFile)) ? bundledFolder
            : File.Exists(Path.Combine(downloadedFolder, MainFile)) ? downloadedFolder
            : null;

        /// <summary>The source that will be downloaded from (the first that answers), or null if none does.</summary>
        public static Task<ComponentSource?> ResolveAsync(CancellationToken cancellationToken) =>
            ComponentDownloader.ResolveAsync(Package, Http, cancellationToken);

        /// <summary>Downloads the engine into <see cref="DownloadedFolder"/> and returns the folder. See <see cref="ComponentDownloader.InstallAsync"/>.</summary>
        public static Task<string> DownloadAsync(IProgress<ComponentProgress>? progress, CancellationToken cancellationToken, ComponentSource? preferred = null) =>
            ComponentDownloader.InstallAsync(Package, DownloadedFolder, Http, progress, cancellationToken, preferred);

        private static HttpClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoLiker");
            return http;
        }

        private static string[] ReadFileList()
        {
            using var stream = typeof(VlcEngine).Assembly.GetManifestResourceStream("libvlc-files.txt")
                ?? throw new InvalidOperationException("The list of the files of the video player is not in this library.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
