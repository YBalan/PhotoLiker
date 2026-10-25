namespace PhotoLikerUI
{
    /// <summary>
    /// The settings file lives in the photo folder, so the paths in it are stored relative to that folder. The folder
    /// can then be opened from another machine, drive letter or share and everything in the file still points into it.
    /// </summary>
    internal static class StoredPaths
    {
        /// <summary>
        /// The path as it goes into the settings file: relative to <paramref name="baseFolder"/> (the folder itself is ".").
        /// Left as it is when it is blank, already relative, or has no relative form (another drive).
        /// </summary>
        public static string ToStored(string baseFolder, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
                string.IsNullOrWhiteSpace(baseFolder) || !Path.IsPathRooted(baseFolder))
                return path;

            return Path.GetRelativePath(baseFolder, path);
        }

        /// <summary>
        /// The full path of a path read from the settings file: a relative one is resolved against <paramref name="baseFolder"/>.
        /// Older versions stored full paths of whatever machine wrote the file. Those below
        /// <paramref name="legacyRoot"/> (the folder the file was written for) are moved to <paramref name="baseFolder"/>
        /// instead; any other full path is left alone.
        /// </summary>
        public static string FromStored(string baseFolder, string stored, string? legacyRoot = null)
        {
            if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(baseFolder))
                return stored;

            if (Path.IsPathRooted(stored))
            {
                if (legacyRoot is null || !TryGetRelativeInside(legacyRoot, stored, out var inside))
                    return stored;

                stored = inside;
            }

            return Path.GetFullPath(Path.Combine(baseFolder, stored));
        }

        private static bool TryGetRelativeInside(string root, string path, out string relative)
        {
            relative = string.Empty;
            if (!Path.IsPathRooted(root))
                return false;

            var candidate = Path.GetRelativePath(root, path);
            var outside = Path.IsPathRooted(candidate) ||
                          candidate == ".." ||
                          candidate.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            if (outside)
                return false;

            relative = candidate;
            return true;
        }
    }
}
