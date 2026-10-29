namespace PhotoLiker.Core
{
    /// <summary>A group folder found under the liked folder.</summary>
    public readonly record struct GroupFolder(char Key, string Path);

    /// <summary>
    /// Group folders: pressing 1-9 or A-Z on a photo saves a copy of it into a "&lt;key&gt; - Group" folder under the
    /// liked folder. The folder may be renamed ("A - Group Family"): any folder whose name starts with "A - Group"
    /// is the folder of group A.
    /// </summary>
    public static class PhotoGroups
    {
        public const string FolderSuffix = " - Group";

        /// <summary>True for 1-9 and A-Z (either case).</summary>
        public static bool IsGroupKey(char key) => key is >= '1' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

        /// <summary>The key as it appears in folder names: letters in upper case.</summary>
        public static char NormalizeKey(char key)
        {
            if (!IsGroupKey(key))
                throw new ArgumentOutOfRangeException(nameof(key), key, "A group key is 1-9 or A-Z.");

            return char.ToUpperInvariant(key);
        }

        /// <summary>The longest search request that is put into the name of a group folder; a path is only so long.</summary>
        public const int MaxRequestLength = 50;

        /// <summary>The name a group folder gets when it is created, e.g. "A - Group".</summary>
        public static string GetFolderName(char key) => NormalizeKey(key) + FolderSuffix;

        /// <summary>
        /// The name a group folder gets when it is created for the results of a search, with the request after the usual
        /// name: "A - Group beach, September 2009". That is a renamed folder as far as everything else is concerned (see
        /// <see cref="TryGetKey"/>). Without a request, or with nothing of it left after <see cref="CleanRequest"/>, the usual name.
        /// </summary>
        public static string GetFolderName(char key, string? request)
        {
            var name = GetFolderName(key);
            var text = CleanRequest(request);
            return text.Length == 0 ? name : name + " " + text;
        }

        /// <summary>
        /// The request as part of a folder name: quotes are dropped, what else Windows does not allow in a name (colons, slashes...)
        /// and line breaks become spaces, runs of spaces one space, the end is cut at <see cref="MaxRequestLength"/> characters, and
        /// a name must not end in a space or a dot (nor does a comma at the end look like anything).
        /// </summary>
        public static string CleanRequest(string? request)
        {
            if (string.IsNullOrWhiteSpace(request))
                return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            // Quotes only mark the words off ("beach", 2009), they do not make a space
            var spaced = string.Concat(request.Replace("\"", string.Empty).Select(c => char.IsWhiteSpace(c) || char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? ' ' : c));
            var text = string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > MaxRequestLength)
                text = text[..MaxRequestLength];
            return text.TrimEnd(' ', '.', ',');
        }

        /// <summary>Which group a folder name belongs to, e.g. "a - group family" is group A.</summary>
        public static bool TryGetKey(string? folderName, out char key)
        {
            key = default;

            if (folderName is null ||
                folderName.Length < 1 + FolderSuffix.Length ||
                !IsGroupKey(folderName[0]) ||
                !folderName.AsSpan(1).StartsWith(FolderSuffix, StringComparison.OrdinalIgnoreCase))
                return false;

            key = char.ToUpperInvariant(folderName[0]);
            return true;
        }

        /// <summary>All group folders directly under the liked folder, ordered by key (1-9, then A-Z) and name.</summary>
        public static IReadOnlyList<GroupFolder> FindFolders(string likedFolder)
        {
            if (string.IsNullOrWhiteSpace(likedFolder) || !Directory.Exists(likedFolder))
                return [];

            var folders = new List<GroupFolder>();
            foreach (var path in Directory.EnumerateDirectories(likedFolder))
            {
                if (TryGetKey(Path.GetFileName(path), out var key))
                    folders.Add(new GroupFolder(key, path));
            }

            return folders
                .OrderBy(f => f.Key)
                .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The folder of a group, or null if there is none yet. If a group has several folders, the one that still
        /// has the default name wins, otherwise the first by name.
        /// </summary>
        public static string? FindFolder(string likedFolder, char key)
        {
            key = NormalizeKey(key);
            var matches = FindFolders(likedFolder).Where(f => f.Key == key).ToList();

            var defaultName = GetFolderName(key);
            foreach (var match in matches)
            {
                if (string.Equals(Path.GetFileName(match.Path), defaultName, StringComparison.OrdinalIgnoreCase))
                    return match.Path;
            }

            return matches.Count > 0 ? matches[0].Path : null;
        }

        /// <summary>
        /// The folder of a group; created, together with the liked folder, if it doesn't exist yet. A new folder is named
        /// after the <paramref name="request"/> if there is one (see <see cref="GetFolderName(char, string?)"/>); a folder that is there is used as it is.
        /// </summary>
        public static string EnsureFolder(string likedFolder, char key, string? request = null)
        {
            if (string.IsNullOrWhiteSpace(likedFolder))
                throw new ArgumentException("The liked folder is not set.", nameof(likedFolder));

            var folder = FindFolder(likedFolder, key);
            if (folder is not null)
                return folder;

            folder = Path.Combine(likedFolder, GetFolderName(key, request));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
