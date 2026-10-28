namespace PhotoLiker.Core
{
    /// <summary>
    /// Deleting a photo moves it into the folder "Deleted" inside the photo folder; nothing is erased. The path below the photo
    /// folder is kept (<c>Deleted\314CANON\IMG_0001.jpg</c>), so photos of different subfolders that share a name don't meet,
    /// and a photo is put back by moving it up again. <see cref="PhotoScanner"/> leaves that folder out.
    /// </summary>
    public static class DeletedPhotos
    {
        /// <summary>The folder that holds the deleted photos of a photo folder.</summary>
        public static string FolderOf(string photoFolder) => Path.Combine(photoFolder, AppStrings.DeletedFolderName);

        /// <summary>
        /// Moves the photo into the Deleted folder of <paramref name="photoFolder"/>, and returns where it is now. A photo with
        /// that name and place in the Deleted folder is not replaced: the new one gets "name (2).ext", "name (3).ext", ...
        /// </summary>
        public static string Move(string photoFolder, string photoPath, string? subfolder = null)
        {
            var destination = DestinationOf(photoFolder, photoPath, subfolder);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            File.Move(photoPath, destination);
            return destination;
        }

        /// <summary>The folder inside Deleted that holds the copies that were moved away as duplicates of a photo that stays.</summary>
        public static string DuplicatesFolderOf(string photoFolder) => Path.Combine(FolderOf(photoFolder), DuplicatesFolderName);

        public const string DuplicatesFolderName = "Duplicated";

        /// <summary>The folder inside Deleted for photos that can not be read (see <see cref="UnreadablePhotos"/>).</summary>
        public const string UnreadableFolderName = "Unreadable";

        /// <summary>
        /// Where <see cref="Move"/> would put the photo: below the Deleted folder (or the <paramref name="subfolder"/> of it, as for the duplicates),
        /// with the path it has in the photo folder, and a name that is free.
        /// </summary>
        public static string DestinationOf(string photoFolder, string photoPath, string? subfolder = null)
        {
            var relative = Path.GetRelativePath(photoFolder, photoPath);
            var outside = Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);

            var folder = subfolder is null ? FolderOf(photoFolder) : Path.Combine(FolderOf(photoFolder), subfolder);
            return FreeName(Path.Combine(folder, outside ? Path.GetFileName(photoPath) : relative));
        }

        private static string FreeName(string path)
        {
            if (!File.Exists(path))
                return path;

            var folder = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            for (var i = 2; ; i++)
            {
                var candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }
    }
}
