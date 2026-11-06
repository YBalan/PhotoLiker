using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PhotoLiker.Video
{
    /// <summary>One place a package can be downloaded from: a zip at <see cref="Url"/> whose SHA-256 is <see cref="Sha256"/> (it is not used if it is anything else).</summary>
    public sealed record ComponentSource(string Url, string Sha256, long Bytes)
    {
        /// <summary>The server the zip comes from, to tell the person where it is downloaded from.</summary>
        public string Host => new Uri(Url).Host;
    }

    /// <summary>
    /// Files the program does not carry in its installer but downloads the first time they are needed (the VLC engine that plays videos): the files that make
    /// the package (<see cref="Files"/>, paths with /; the one named <see cref="MainFile"/> tells that it is whole), and the places to get them from, the best
    /// first. Each place has a zip of at least these files, in any layout that has one folder around them or none; only the listed files are unpacked.
    /// </summary>
    public sealed record ComponentPackage(string Title, string Version, string MainFile, IReadOnlyList<string> Files, IReadOnlyList<ComponentSource> Sources);

    public enum ComponentStage
    {
        Downloading,
        Unpacking,
    }

    /// <summary>How far a download is: the bytes of the zip, then the files unpacked out of it.</summary>
    public sealed record ComponentProgress(ComponentStage Stage, long Done, long Total);

    /// <summary>Downloads a <see cref="ComponentPackage"/> and puts it in a folder, whole or not at all.</summary>
    public static class ComponentDownloader
    {
        /// <summary>
        /// The first source that answers (the headers of its zip are read; nothing is downloaded), so that the person is told what will be downloaded, how big, and from where,
        /// before it is. Null if none answers (no network).
        /// </summary>
        public static async Task<ComponentSource?> ResolveAsync(ComponentPackage package, HttpClient http, CancellationToken cancellationToken)
        {
            foreach (var source in package.Sources)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return source;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    // this one does not answer: the next
                }
            }
            return null;
        }

        /// <summary>
        /// Installs the package into <paramref name="destination"/> and returns it. The zip is downloaded to a file beside it and checked against the SHA-256 of its source
        /// (a source that gives anything else, or lacks a file, or is not there, is left for the next one), the files are unpacked into a folder of their own, and that
        /// folder becomes <paramref name="destination"/> in one move: the folder is never half there. Other versions in the folder around it are removed afterwards.
        /// Throws <see cref="IOException"/> saying what went wrong with each source if none worked; <see cref="OperationCanceledException"/> if stopped (nothing is left behind).
        /// </summary>
        /// <param name="preferred">The source to try first (the one <see cref="ResolveAsync"/> found).</param>
        public static async Task<string> InstallAsync(
            ComponentPackage package, string destination, HttpClient http, IProgress<ComponentProgress>? progress, CancellationToken cancellationToken, ComponentSource? preferred = null)
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(destination))!;
            Directory.CreateDirectory(parent);

            var sources = preferred is null ? package.Sources : [preferred, .. package.Sources.Where(s => s != preferred)];
            var failures = new List<string>();
            foreach (var source in sources)
            {
                var zip = Path.Combine(parent, $"download-{Guid.NewGuid():N}.zip.part");
                var unpacked = destination + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await DownloadZipAsync(source, zip, http, progress, cancellationToken).ConfigureAwait(false); // and from here on off the thread of the window: the unpacking must not hold it
                    Unpack(package, zip, unpacked, progress, cancellationToken);

                    if (Directory.Exists(destination))
                        Directory.Delete(destination, recursive: true);
                    Directory.Move(unpacked, destination);

                    RemoveOthers(parent, destination);
                    return destination;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    failures.Add($"{source.Host}: {ex.Message}");
                }
                finally
                {
                    TryDelete(zip);
                    TryDeleteFolder(unpacked);
                }
            }

            throw new IOException(string.Format("{0} could not be downloaded ({1}).", package.Title, string.Join("; ", failures)));
        }

        private static async Task DownloadZipAsync(ComponentSource source, string zip, HttpClient http, IProgress<ComponentProgress>? progress, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? source.Bytes;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long done = 0;
            await using (var from = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var to = File.Create(zip))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    progress?.Report(new ComponentProgress(ComponentStage.Downloading, done, Math.Max(total, done)));
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actual, source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the file is not the one expected (its SHA-256 is different)");
        }

        private static void Unpack(ComponentPackage package, string zip, string folder, IProgress<ComponentProgress>? progress, CancellationToken cancellationToken)
        {
            using var archive = ZipFile.OpenRead(zip);
            var prefix = PrefixOf(archive, package.MainFile);

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length > 0 && entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    entries.TryAdd(entry.FullName[prefix.Length..].Replace('\\', '/'), entry);
            }

            var missing = package.Files.Where(file => !entries.ContainsKey(file)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException($"the zip lacks {missing.Count} of the files ({missing[0]} ...)");

            Directory.CreateDirectory(folder);
            var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            for (var i = 0; i < package.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Where a file goes comes from the list of the package, never from the zip
                var target = Path.GetFullPath(Path.Combine(folder, package.Files[i].Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"'{package.Files[i]}' is not a path inside the package");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entries[package.Files[i]].ExtractToFile(target, overwrite: true);
                progress?.Report(new ComponentProgress(ComponentStage.Unpacking, i + 1, package.Files.Count));
            }
        }

        // The folder around the files in the zip ("vlc-3.0.24/"), or none: told by where the main file is
        private static string PrefixOf(ZipArchive archive, string mainFile)
        {
            var main = archive.Entries
                .Where(e => string.Equals(e.FullName, mainFile, StringComparison.OrdinalIgnoreCase)
                    || e.FullName.EndsWith("/" + mainFile, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName.Length)
                .FirstOrDefault() ?? throw new InvalidDataException($"the zip has no {mainFile}");
            return main.FullName[..^mainFile.Length];
        }

        // Another version of the same package, and what a download that was killed left: not needed any more
        private static void RemoveOthers(string parent, string keep)
        {
            foreach (var other in Directory.EnumerateDirectories(parent))
            {
                // Only what is a version of a package (a folder named by its version) or a half-made one: whatever else is in the folder is not ours to remove
                var name = Path.GetFileName(other);
                var ours = name.Length > 0 && (char.IsDigit(name[0]) || name.Contains(".tmp-", StringComparison.Ordinal));
                if (ours && !string.Equals(Path.TrimEndingDirectorySeparator(other), Path.TrimEndingDirectorySeparator(keep), StringComparison.OrdinalIgnoreCase))
                    TryDeleteFolder(other);
            }
            foreach (var part in Directory.EnumerateFiles(parent, "download-*.zip.part"))
                TryDelete(part);
        }

        private static void TryDelete(string file)
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a leftover in the app's own folder */ }
        }

        private static void TryDeleteFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a leftover in the app's own folder */ }
        }
    }
}
