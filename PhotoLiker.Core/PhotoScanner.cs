using System.IO.Enumeration;

namespace PhotoLiker.Core
{
    /// <summary>
    /// Lists the photos of a folder (optionally including subfolders). The folder of deleted photos (<see cref="DeletedPhotos"/>)
    /// directly inside it is left out.
    /// </summary>
    public static class PhotoScanner
    {
        // Folders that hold system data or NAS-generated thumbnails rather than photos
        private static readonly string[] SkippedFolderNames =
            ["@eaDir", "#recycle", "$RECYCLE.BIN", "System Volume Information", ".AppleDouble"];

        /// <param name="excludedFolder">A folder to leave out, e.g. the liked folder when it sits inside the photo folder.</param>
        /// <param name="sortOrder">
        /// By the time the file was created, which comes with the directory listing, so sorting costs no extra round trip
        /// per file (the date taken would mean opening every photo). The default is the order of the file system.
        /// </param>
        public static List<PhotoFile> Scan(string folder, bool recursive, IReadOnlyCollection<string> extensions, string? excludedFolder,
            PhotoSortOrder sortOrder = PhotoSortOrder.None)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return [];

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                // One unreadable subfolder shouldn't fail the whole listing; SMB shares often have a few
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System,
            };

            // Only the Deleted folder of this very folder: a subfolder that happens to be called Deleted further down is the user's own
            var excluded = new List<string> { Path.TrimEndingDirectorySeparator(DeletedPhotos.FolderOf(Path.GetFullPath(folder))) };
            if (!string.IsNullOrWhiteSpace(excludedFolder))
                excluded.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(excludedFolder)));

            // Length and timestamp come straight from the directory listing, no extra round trip per file
            var photos = new FileSystemEnumerable<PhotoFile>(
                folder,
                (ref FileSystemEntry e) => new PhotoFile(e.ToFullPath(), string.Empty)
                {
                    Length = e.Length,
                    LastWriteTimeUtc = e.LastWriteTimeUtc.UtcDateTime,
                    CreationTimeUtc = e.CreationTimeUtc.UtcDateTime,
                },
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && IsPhoto(e.FileName, extensions),
                ShouldRecursePredicate = (ref FileSystemEntry e) => !IsSkippedFolder(ref e, excluded),
            };

            var found = photos.ToList();
            switch (sortOrder)
            {
                case PhotoSortOrder.OldestFirst:
                    found.Sort(OldestFirst);
                    break;
                case PhotoSortOrder.NewestFirst:
                    found.Sort((a, b) => OldestFirst(b, a));
                    break;
            }
            return found;
        }

        // Photos created at the same moment (a burst copied in one go) keep a fixed order, by path
        private static int OldestFirst(PhotoFile a, PhotoFile b)
        {
            var byCreated = a.CreationTimeUtc.CompareTo(b.CreationTimeUtc);
            return byCreated != 0 ? byCreated : string.Compare(a.OriginalFilePath, b.OriginalFilePath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPhoto(ReadOnlySpan<char> fileName, IReadOnlyCollection<string> extensions)
        {
            // "._name.jpg" are macOS resource-fork stubs, not images
            if (fileName.StartsWith("._", StringComparison.Ordinal))
                return false;

            var extension = Path.GetExtension(fileName);
            foreach (var candidate in extensions)
            {
                if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsSkippedFolder(ref FileSystemEntry entry, List<string> excludedFolders)
        {
            foreach (var name in SkippedFolderNames)
            {
                if (entry.FileName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var path = Path.TrimEndingDirectorySeparator(entry.ToFullPath());
            foreach (var excluded in excludedFolders)
            {
                if (string.Equals(path, excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
