namespace PhotoLiker.Core
{
    /// <summary>
    /// The folders that were opened last, newest first: what the right-click menu of the Open button lists, and what the Jump List of the taskbar button shows.
    /// Only the rules of the list are here (no folder is looked at on the disk: a folder on a router that is off would hold the window for seconds);
    /// <see cref="GlobalConfig.RecentFolders"/> keeps it from one start to the next.
    /// </summary>
    public static class RecentFolders
    {
        /// <summary>How many folders the list holds: as many as a Jump List shows by default.</summary>
        public const int Capacity = 10;

        /// <summary>
        /// The folder as the list knows it: the full path, without the separator at its end (a drive stays "Y:\"), so that the same folder written two ways is one entry.
        /// Empty if there is no folder in <paramref name="path"/>.
        /// </summary>
        public static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                var full = Path.GetFullPath(path.Trim());
                var root = Path.GetPathRoot(full) ?? string.Empty;
                return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return string.Empty; // not a path at all
            }
        }

        /// <summary>The same folder, whatever the case it is written in and whatever separator ends it.</summary>
        public static bool Same(string? a, string? b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase) && Normalize(a).Length > 0;

        /// <summary>
        /// The list with <paramref name="folder"/> put first (taken from where it was, if it was there), cut to <paramref name="capacity"/>. Entries that are not a
        /// folder path, or are there twice, are left out. The list that is given is not changed.
        /// </summary>
        public static List<string> Remember(IEnumerable<string>? list, string? folder, int capacity = Capacity)
        {
            var result = new List<string>();
            var first = Normalize(folder);
            if (first.Length > 0)
                result.Add(first);

            foreach (var entry in list ?? [])
            {
                var normal = Normalize(entry);
                if (normal.Length > 0 && !result.Contains(normal, StringComparer.OrdinalIgnoreCase))
                    result.Add(normal);
            }

            return result.Take(Math.Max(0, capacity)).ToList();
        }

        /// <summary>The list without <paramref name="folder"/>. The list that is given is not changed.</summary>
        public static List<string> Forget(IEnumerable<string>? list, string? folder) =>
            Remember(list, null, int.MaxValue).Where(entry => !Same(entry, folder)).ToList();

        /// <summary>
        /// A name for each folder of the list, to put in a narrow place (the Jump List): the name of the folder, and where two folders have the same name, the whole path of
        /// both, so that they can be told apart. A drive has no name, so it is written whole.
        /// </summary>
        public static List<string> Titles(IReadOnlyList<string> folders)
        {
            var names = folders.Select(Leaf).ToList();
            return folders.Select((folder, index) =>
                names[index].Length == 0 || names.Count(name => string.Equals(name, names[index], StringComparison.CurrentCultureIgnoreCase)) > 1
                    ? folder
                    : names[index]).ToList();
        }

        /// <summary>The name of the folder itself ("Trip" for "D:\Photos\Trip"); empty for a drive.</summary>
        public static string Leaf(string folder) => Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        /// <summary>
        /// The path written in at most <paramref name="maxLength"/> characters for a menu: a long path keeps its drive (or share) and its last folders, and the middle
        /// is "...". The name of the folder itself is never cut.
        /// </summary>
        public static string Shorten(string folder, int maxLength)
        {
            if (folder.Length <= maxLength)
                return folder;

            var root = Path.GetPathRoot(folder) ?? string.Empty;
            var parts = folder[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return folder;

            // From the end: as many folders as fit next to the root and the dots
            var tail = new List<string>();
            var length = root.Length + 3 + 1; // root, "...", and one separator
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var added = parts[i].Length + 1;
                if (tail.Count > 0 && length + added > maxLength)
                    break;

                tail.Insert(0, parts[i]);
                length += added;
            }

            // A share has no separator at the end of its root ("\\router\share"), a drive has ("D:\")
            var start = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return tail.Count == parts.Length ? folder : start + "..." + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, tail);
        }
    }
}
