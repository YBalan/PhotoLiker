namespace PhotoLiker.Core
{
    /// <summary>A folder that holds nothing worth keeping.</summary>
    /// <param name="Junk">The files in it that go with it (a thumbnail cache and the like), by name.</param>
    /// <param name="Subfolders">How many of its subfolders are in the list as well: they have to go first.</param>
    public sealed record EmptyFolder(string Path, IReadOnlyList<string> Junk, int Subfolders);

    public sealed record EmptyFolderFailure(string Path, string Message);

    /// <param name="Folders">The folders that can go, the deepest first.</param>
    /// <param name="EmptyPhotos">Photos that are empty files (full paths).</param>
    public sealed record EmptyFoldersScan(IReadOnlyList<EmptyFolder> Folders, IReadOnlyList<string> EmptyPhotos);

    /// <param name="Removed">The folders that are gone.</param>
    /// <param name="JunkFiles">How many of the files that went with them were deleted.</param>
    public sealed record EmptyFoldersResult(IReadOnlyList<string> Removed, int JunkFiles, IReadOnlyList<EmptyFolderFailure> Failures, bool Cancelled);

    /// <summary>
    /// Finds and removes the folders of a photo folder that hold no photo, nor anything else of use: a folder with only thumbnail
    /// caches (<c>Thumbs.db</c> and the like) in it, or only such folders, however deep. The rule is strict on purpose: a file
    /// that is not on the list of <see cref="IsJunk">junk</see> makes its folder and every folder above it stay, a folder that can not be
    /// read is not guessed at, and a folder that is a link to somewhere else, or one of the folders the app keeps, is left alone.
    /// </summary>
    public static class EmptyFolders
    {
        /// <summary>Files that Windows, macOS and the cameras' software leave in a folder to remember thumbnails and its look. Not photos, and made again when needed.</summary>
        private static readonly string[] JunkNames = ["Thumbs.db", "ehthumbs.db", "ehthumbs_vista.db", "desktop.ini", ".DS_Store"];

        // Hidden and system files are the ones to see here: that is what Thumbs.db is
        private static readonly EnumerationOptions All = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

        public static bool IsJunk(string fileName) =>
            JunkNames.Contains(fileName, StringComparer.OrdinalIgnoreCase) || fileName.StartsWith("._", StringComparison.Ordinal); // "._name" are macOS resource-fork stubs

        /// <summary>
        /// The folders below <paramref name="root"/> that can go, the deepest first (a folder comes after the folders in it). The root itself is never in the list.
        /// </summary>
        /// <param name="leave">Folders that stay whatever they hold (the Deleted and the Liked folder); so do the folders that hold them.</param>
        /// <param name="progress">The number of folders looked at so far.</param>
        public static IReadOnlyList<EmptyFolder> Find(string root, IReadOnlyCollection<string> leave, IProgress<int>? progress, CancellationToken token) =>
            Scan(root, leave, [], progress, token).Folders;

        /// <summary>
        /// <see cref="Find"/>, and on the same walk the photos that are empty files (0 bytes): nothing can be read from them, whatever program is asked. They
        /// are no junk: a folder that holds one stays, and they are listed for a person to deal with.
        /// </summary>
        /// <param name="photoExtensions">The extensions that make a file a photo (".jpg"); a file of another kind that is empty is not looked at.</param>
        public static EmptyFoldersScan Scan(string root, IReadOnlyCollection<string> leave, IReadOnlyCollection<string> photoExtensions, IProgress<int>? progress, CancellationToken token)
        {
            var found = new List<EmptyFolder>();
            var emptyPhotos = new List<string>();
            var stay = leave
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var looked = 0;

            bool Visit(string folder, bool isRoot)
            {
                token.ThrowIfCancellationRequested();
                if (++looked % 25 == 0)
                    progress?.Report(looked);

                List<FileSystemInfo> entries;
                try
                {
                    entries = [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", All)];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return false; // what is in it is not known
                }

                var junk = new List<string>();
                var subfolders = 0;
                var removable = true;
                foreach (var entry in entries)
                {
                    if (entry is DirectoryInfo directory)
                    {
                        if (IsLeftAlone(directory, stay))
                            removable = false;
                        else if (Visit(directory.FullName, isRoot: false))
                            subfolders++;
                        else
                            removable = false;
                    }
                    else if (IsJunk(entry.Name))
                    {
                        junk.Add(entry.Name);
                    }
                    else
                    {
                        removable = false;
                        if (entry is FileInfo { Length: 0 } empty && IsPhotoName(empty.Name, photoExtensions))
                            emptyPhotos.Add(empty.FullName);
                    }
                }

                if (removable && !isRoot)
                    found.Add(new EmptyFolder(folder, junk, subfolders));
                return removable;
            }

            Visit(Path.GetFullPath(root), isRoot: true);
            progress?.Report(looked);
            return new EmptyFoldersScan(found, emptyPhotos);
        }

        private static bool IsPhotoName(string fileName, IReadOnlyCollection<string> extensions)
        {
            var extension = Path.GetExtension(fileName);
            return extensions.Any(e => extension.Equals(e, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsLeftAlone(DirectoryInfo directory, HashSet<string> stay) =>
            directory.Attributes.HasFlag(FileAttributes.ReparsePoint) // a link: what is behind it is not this folder's
            || PhotoScanner.SkippedFolderNames.Contains(directory.Name, StringComparer.OrdinalIgnoreCase) // NAS and system folders
            || stay.Contains(Path.TrimEndingDirectorySeparator(directory.FullName));

        /// <summary>
        /// Deletes the folders, in the order given (so the folders in a folder before the folder), and the junk files in them. A folder is looked at
        /// again just before: if something else is in it by now it stays, and a folder is never deleted with what is in it (no recursive delete).
        /// A stop comes between two folders.
        /// </summary>
        public static EmptyFoldersResult Remove(IReadOnlyList<string> folders, IProgress<int>? progress, CancellationToken token)
        {
            var removed = new List<string>();
            var failures = new List<EmptyFolderFailure>();
            var junkFiles = 0;
            var cancelled = false;

            foreach (var folder in folders)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                try
                {
                    junkFiles += RemoveOne(folder);
                    removed.Add(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new EmptyFolderFailure(folder, ex.Message));
                }
                progress?.Report(removed.Count + failures.Count);
            }

            return new EmptyFoldersResult(removed, junkFiles, failures, cancelled);
        }

        private static int RemoveOne(string folder)
        {
            var junk = new List<string>();
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", All).ToList())
            {
                if (entry is FileInfo file && IsJunk(file.Name))
                    junk.Add(file.FullName);
                else
                    throw new IOException(entry is DirectoryInfo ? $"It still holds the folder '{entry.Name}'." : $"It holds '{entry.Name}'.");
            }

            foreach (var file in junk)
            {
                File.SetAttributes(file, FileAttributes.Normal); // Thumbs.db is hidden and a system file, some are read-only too
                File.Delete(file);
            }

            var attributes = File.GetAttributes(folder);
            if (attributes.HasFlag(FileAttributes.ReadOnly)) // a folder with a custom look often is: Windows refuses to delete it
                File.SetAttributes(folder, attributes & ~FileAttributes.ReadOnly);

            Directory.Delete(folder, recursive: false);
            return junk.Count;
        }
    }
}
