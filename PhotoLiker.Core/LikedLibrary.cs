namespace PhotoLiker.Core
{
    /// <summary>
    /// Knows which photos already have a copy in the liked folder, from a single directory listing, so the UI
    /// never has to ask the (possibly network) file system whether a photo is liked.
    ///
    /// Copies stay flat in the liked folder and carry an id of their photo in the name, "name [id].ext" (see
    /// <see cref="CopyName"/>), so photos that share a file name (typical with subfolders) never meet. Copies made before
    /// the ids ("name.ext", "name (2).ext") are still recognised, by size and timestamp.
    /// </summary>
    public sealed class LikedLibrary
    {
        // File.Copy keeps the timestamp, but not every file system stores it with the same precision
        private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

        private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _copies = new(StringComparer.OrdinalIgnoreCase);

        // Photo file name -> the copies that can belong to a photo of that name
        private readonly Dictionary<string, List<string>> _copiesByPhotoName = new(StringComparer.OrdinalIgnoreCase);

        private LikedLibrary(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        /// <summary>Lists the liked folder. Doesn't touch any <see cref="PhotoFile"/>, so it can run on any thread.</summary>
        public static LikedLibrary Scan(string likedFolder)
        {
            var library = new LikedLibrary(likedFolder);

            if (!string.IsNullOrWhiteSpace(likedFolder) && Directory.Exists(likedFolder))
            {
                foreach (var copy in new DirectoryInfo(likedFolder).EnumerateFiles())
                    library.AddCopy(copy.Name, copy.Length, copy.LastWriteTimeUtc);
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
            var photoName = Path.GetFileName(photo.OriginalFilePath);
            if (!_copiesByPhotoName.TryGetValue(photoName, out var candidates))
                return null;

            // A copy carries the id of its photo. Otherwise, or if the time of the copy was changed on the way (a sync tool, or
            // the dates of the photo aligned), the copy belongs to the photo it is identical to.
            var id = CopyName.IdOf(photo);
            string? identical = null;
            foreach (var candidate in candidates)
            {
                if (CopyName.TryGetId(candidate, out var copyOf, out var copyId) &&
                    string.Equals(copyOf, photoName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(copyId, id, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(Folder, candidate);

                if (identical is null && IsCopyOf(_copies[candidate], photo))
                    identical = candidate;
            }

            return identical is null ? null : Path.Combine(Folder, identical);
        }

        /// <summary>Where a new copy of the photo should go. It can't be another photo's copy: the name has the id of this one.</summary>
        public string GetNewCopyPath(PhotoFile photo) =>
            Path.Combine(Folder, CopyName.For(photo));

        public void Add(string likedPath, PhotoFile photo) =>
            AddCopy(Path.GetFileName(likedPath), photo.Length, photo.LastWriteTimeUtc);

        public void Remove(string likedPath)
        {
            var fileName = Path.GetFileName(likedPath);
            if (!_copies.Remove(fileName))
                return;

            foreach (var photoName in CopyName.PhotoNamesOf(fileName))
            {
                if (_copiesByPhotoName.TryGetValue(photoName, out var names))
                {
                    names.RemoveAll(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));
                    if (names.Count == 0)
                        _copiesByPhotoName.Remove(photoName);
                }
            }
        }

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

        private void AddCopy(string fileName, long length, DateTime lastWriteUtc)
        {
            if (_copies.ContainsKey(fileName))
                Remove(fileName);
            _copies[fileName] = (length, lastWriteUtc);

            foreach (var photoName in CopyName.PhotoNamesOf(fileName))
            {
                if (!_copiesByPhotoName.TryGetValue(photoName, out var names))
                    _copiesByPhotoName[photoName] = names = [];
                names.Add(fileName);
            }
        }

        private static bool IsCopyOf((long Length, DateTime LastWriteUtc) copy, PhotoFile photo) =>
            copy.Length == photo.Length &&
            (copy.LastWriteUtc - photo.LastWriteTimeUtc).Duration() <= TimestampTolerance;
    }
}
