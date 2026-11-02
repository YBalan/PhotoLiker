using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace PhotoLiker.Core
{
    /// <summary>
    /// A release of the program on GitHub: its version, its page, and the setup to download (<see cref="InstallerUrl"/> is null if the
    /// release has no setup file, or one that is not at the address of this project's releases).
    /// </summary>
    public sealed record ReleaseInfo(
        Version Version, string Tag, string PageUrl, string? InstallerName, string? InstallerUrl, long InstallerBytes, string? InstallerSha256);

    /// <summary>
    /// Asks GitHub which release of the program is the latest and downloads its setup. It runs only when the person asks for it (the
    /// About box), nothing is checked in the background. A release is a tag <c>v2.1.0</c> with a file <c>PhotoLiker-Setup-2.1.0.exe</c>
    /// (see .github\workflows\release.yml); the version the running program knows about itself comes from the same tag.
    /// </summary>
    public sealed class UpdateChecker
    {
        public const string Repository = "YBalan/PhotoLiker";
        public const string InstallerPrefix = "PhotoLiker-Setup-";

        private readonly HttpClient _http;
        private readonly string _repository;

        public UpdateChecker(HttpClient http, string repository = Repository)
        {
            _http = http;
            _repository = repository;
        }

        /// <summary>The latest release, or null if the project has not published one yet. A failure (no network, GitHub refusing) is an exception.</summary>
        public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
        {
            using var request = NewRequest($"https://api.github.com/repos/{_repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null; // GitHub's answer when there is no release (and none that is not a draft or a pre-release)

            response.EnsureSuccessStatusCode();
            return Parse(await response.Content.ReadAsStringAsync(cancellationToken), _repository);
        }

        /// <summary>
        /// Downloads the setup of <paramref name="release"/> into <paramref name="folder"/> and returns its path. The file is checked against the
        /// size and the SHA-256 that GitHub gives for it, and thrown away if either is not right. Older setups in the folder are deleted.
        /// </summary>
        public async Task<string> DownloadInstallerAsync(ReleaseInfo release, string folder, IProgress<(long Done, long Total)>? progress, CancellationToken cancellationToken)
        {
            if (release.InstallerUrl is null || release.InstallerName is null)
                throw new InvalidOperationException("The release has no setup to download.");

            Directory.CreateDirectory(folder);
            foreach (var old in Directory.EnumerateFiles(folder, InstallerPrefix + "*"))
            {
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { } // one that is running now stays
            }

            var target = Path.Combine(folder, Path.GetFileName(release.InstallerName));
            var partial = target + ".part";
            try
            {
                using var request = NewRequest(release.InstallerUrl);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? release.InstallerBytes;

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long done = 0;
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var file = File.Create(partial))
                {
                    var buffer = new byte[1 << 17];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        hash.AppendData(buffer, 0, read);
                        done += read;
                        progress?.Report((done, total));
                    }
                }

                var wrongSize = (release.InstallerBytes > 0 && done != release.InstallerBytes) || (total > 0 && done != total);
                var wrongHash = release.InstallerSha256 is { Length: > 0 } expected
                    && !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase);
                if (wrongSize || wrongHash || done == 0)
                    throw new InvalidDataException(CoreStrings.UpdateDownloadDamaged);

                File.Move(partial, target, overwrite: true);
                return target;
            }
            finally
            {
                try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { } // there only if the download did not finish
            }
        }

        /// <summary>The release that GitHub's JSON describes. The pages are built here from the tag, not taken from the answer, so only github.com of this project is ever opened.</summary>
        public static ReleaseInfo Parse(string json, string repository = Repository)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                if (!TryParseVersion(tag, out var version))
                    throw new InvalidDataException(string.Format(CoreStrings.UpdateTagUnreadableFormat, tag));

                var page = $"https://github.com/{repository}/releases/tag/{Uri.EscapeDataString(tag)}";
                var downloads = $"https://github.com/{repository}/releases/download/";

                string? name = null, url = null, sha = null;
                long bytes = 0;
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var assetName = asset.GetProperty("name").GetString() ?? string.Empty;
                        var assetUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                        if (!assetName.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                            || !assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            || !assetUrl.StartsWith(downloads, StringComparison.Ordinal))
                            continue;

                        name = assetName;
                        url = assetUrl;
                        bytes = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var length) ? length : 0;
                        sha = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } text && text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                            ? text["sha256:".Length..]
                            : null;
                        break;
                    }
                }

                return new ReleaseInfo(version, tag, page, name, url, bytes, sha);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new InvalidDataException(CoreStrings.UpdateAnswerUnreadable, ex);
            }
        }

        /// <summary>The version of a tag or of a version text: <c>v2.1.0</c>, <c>2.1</c>, <c>2.1.0-beta+abc</c> (whatever follows - or + is left out).</summary>
        public static bool TryParseVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return false;

            if (trimmed[0] is 'v' or 'V')
                trimmed = trimmed[1..];

            var cut = trimmed.IndexOfAny(['-', '+']);
            if (cut >= 0)
                trimmed = trimmed[..cut];

            if (!Version.TryParse(trimmed, out var parsed))
                return false;

            version = Normalize(parsed);
            return true;
        }

        /// <summary>Whether <paramref name="latest"/> comes after <paramref name="installed"/>. 2.1 is the same version as 2.1.0 and 2.1.0.0 (the Version class puts 2.1.0 before 2.1.0.0).</summary>
        public static bool IsNewer(Version latest, Version installed) => Normalize(latest) > Normalize(installed);

        private static Version Normalize(Version version) =>
            new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

        /// <summary>
        /// The release a running program is, from the version its build was stamped with (the tag, see release.yml); null for a build that was
        /// not made for a release (the project's default, 0.0.0).
        /// </summary>
        public static Version? ReleaseVersionOf(string? informationalVersion) =>
            TryParseVersion(informationalVersion, out var version) && version > new Version(0, 0, 0, 0) ? version : null;

        private static HttpRequestMessage NewRequest(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PhotoLiker"); // GitHub's API refuses a request without one
            return request;
        }
    }
}
