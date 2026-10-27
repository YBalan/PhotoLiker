using System.IO.Enumeration;

namespace PhotoLiker.Core
{
    /// <summary>Lists the photos of a folder (optionally including subfolders).</summary>
    public static class PhotoScanner
    {
        // Folders that hold system data or NAS-generated thumbnails rather than photos
        private static readonly string[] SkippedFolderNames =
            ["@eaDir", "#recycle", "$RECYCLE.BIN", "System Volume Information", ".AppleDouble"];

        /// <param name="excludedFolder">A folder to leave out, e.g. the liked folder when it sits inside the photo folder.</param>
        public static List<PhotoFile> Scan(string folder, bool recursive, IReadOnlyCollection<string> extensions, string? excludedFolder)
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

            var excluded = string.IsNullOrWhiteSpace(excludedFolder)
                ? null
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(excludedFolder));

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

            return photos.ToList();
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

        private static bool IsSkippedFolder(ref FileSystemEntry entry, string? excludedFolder)
        {
            foreach (var name in SkippedFolderNames)
            {
                if (entry.FileName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return excludedFolder is not null &&
                   string.Equals(Path.TrimEndingDirectorySeparator(entry.ToFullPath()), excludedFolder, StringComparison.OrdinalIgnoreCase);
        }
    }
}
