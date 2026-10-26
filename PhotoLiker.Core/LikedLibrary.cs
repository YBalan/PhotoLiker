namespace PhotoLiker.Core
{
    /// <summary>
    /// Knows which photos already have a copy in the liked folder, from a single directory listing, so the UI
    /// never has to ask the (possibly network) file system whether a photo is liked.
    ///
    /// Copies stay flat in the liked folder. Only when several photos share a file name (typical with subfolders)
    /// are their copies told apart by size and timestamp and named "name (2).ext", "name (3).ext", ...
    /// </summary>
    public sealed class LikedLibrary
    {
        // File.Copy keeps the timestamp, but not every file system stores it with the same precision
        private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

        private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _copies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _nameCounts = new(StringComparer.OrdinalIgnoreCase);

        private LikedLibrary(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        /// <summary>Lists the liked folder. Doesn't touch <paramref name="files"/>, so it can run on any thread.</summary>
        public static LikedLibrary Scan(string likedFolder, IReadOnlyList<PhotoFile> files)
        {
            var library = new LikedLibrary(likedFolder);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file.OriginalFilePath);
                library._nameCounts[name] = library._nameCounts.GetValueOrDefault(name) + 1;
            }

            if (!string.IsNullOrWhiteSpace(likedFolder) && Directory.Exists(likedFolder))
            {
                foreach (var copy in new DirectoryInfo(likedFolder).EnumerateFiles())
                    library._copies[copy.Name] = (copy.Length, copy.LastWriteTimeUtc);
            }

            return library;
        }

        /// <summary>Sets <see cref="PhotoFile.LikedFilePath"/> on every photo.</summary>
        public void Apply(IReadOnlyList<PhotoFile> files)
        {
            foreach (var file in files)
                file.LikedFilePath = Find(file) ?? string.Empty;
        }

        /// <summary>Path of the photo's copy in the liked folder, or null if it isn't liked.</summary>
        public string? Find(PhotoFile photo)
        {
            var name = Path.GetFileName(photo.OriginalFilePath);

            if (!_nameCounts.TryGetValue(name, out var count) || count <= 1)
                return _copies.ContainsKey(name) ? Path.Combine(Folder, name) : null;

            // Several photos share this name: a copy belongs to the photo it is identical to
            var candidates = 0;
            foreach (var candidate in CandidateNames(name))
            {
                if (_copies.TryGetValue(candidate, out var copy) && IsCopyOf(copy, photo))
                    return Path.Combine(Folder, candidate);

                if (++candidates >= count)
                    break;
            }

            return null;
        }

        /// <summary>Where a new copy of the photo should go without replacing another photo's copy.</summary>
        public string GetNewCopyPath(PhotoFile photo)
        {
            var name = Path.GetFileName(photo.OriginalFilePath);

            if (!_nameCounts.TryGetValue(name, out var count) || count <= 1)
                return Path.Combine(Folder, name);

            foreach (var candidate in CandidateNames(name))
            {
                if (!_copies.ContainsKey(candidate))
                    return Path.Combine(Folder, candidate);
            }

            throw new InvalidOperationException("Unreachable: CandidateNames never ends.");
        }

        public void Add(string likedPath, PhotoFile photo) =>
            _copies[Path.GetFileName(likedPath)] = (photo.Length, photo.LastWriteTimeUtc);

        public void Remove(string likedPath) =>
            _copies.Remove(Path.GetFileName(likedPath));

        public static void CopyPhoto(string source, string destination)
        {
            var existed = File.Exists(destination);
            try
            {
                File.Copy(source, destination, overwrite: true);
            }
            catch
            {
                // Don't leave a half-written copy behind that would later look like a liked photo
                if (!existed)
                {
                    try { File.Delete(destination); } catch { /* best effort */ }
                }
                throw;
            }
        }

        private static bool IsCopyOf((long Length, DateTime LastWriteUtc) copy, PhotoFile photo) =>
            copy.Length == photo.Length &&
            (copy.LastWriteUtc - photo.LastWriteTimeUtc).Duration() <= TimestampTolerance;

        private static IEnumerable<string> CandidateNames(string fileName)
        {
            yield return fileName;

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var i = 2; ; i++)
                yield return $"{stem} ({i}){extension}";
        }
    }
}
