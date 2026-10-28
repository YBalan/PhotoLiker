namespace PhotoLiker.Core
{
    /// <summary>What <see cref="GroupLibrary.Toggle"/> did, for the UI thread to apply with <see cref="GroupLibrary.Update"/>.</summary>
    public sealed record GroupToggleResult(
        char Key,
        string Folder,
        string FilePath,
        bool Added,
        IReadOnlyDictionary<string, (char Key, LikedLibrary Library)> Folders);

    /// <summary>One line of the legend: a group folder and whether the current photo has a copy in it.</summary>
    public sealed record GroupLegendEntry(char Key, string Name, bool HasPhoto);

    /// <summary>
    /// Knows which photos have a copy in the group folders under the liked folder ("A - Group", "1 - Group", ...),
    /// so the UI can list them without asking the (possibly network) file system.
    ///
    /// Every group folder is indexed like the liked folder is (see <see cref="LikedLibrary"/>). The file system work
    /// (<see cref="Scan"/>, <see cref="Toggle"/>) can run on any thread: it never changes the index, it returns a new
    /// one that the UI thread - the only one that reads the index - swaps in with <see cref="Update"/>.
    /// </summary>
    public sealed class GroupLibrary
    {
        private IReadOnlyDictionary<string, (char Key, LikedLibrary Library)> _folders;

        private GroupLibrary(string likedFolder, IReadOnlyDictionary<string, (char Key, LikedLibrary Library)> folders)
        {
            LikedFolder = likedFolder;
            _folders = folders;
        }

        public string LikedFolder { get; }

        /// <summary>Lists the group folders under the liked folder. Doesn't touch <paramref name="files"/>.</summary>
        public static GroupLibrary Scan(string likedFolder, IReadOnlyList<PhotoFile> files)
        {
            var folders = new Dictionary<string, (char Key, LikedLibrary Library)>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in PhotoGroups.FindFolders(likedFolder))
                folders[group.Path] = (group.Key, LikedLibrary.Scan(group.Path, files));

            return new GroupLibrary(likedFolder, folders);
        }

        /// <summary>
        /// Saves a copy of the photo in the folder of the group, or removes the copy again if the folder already has one.
        /// The folder is a renamed one if there is any ("A - Group Family"), else it is created ("A - Group").
        /// </summary>
        public GroupToggleResult Toggle(char key, PhotoFile photo, IReadOnlyList<PhotoFile> files)
        {
            key = PhotoGroups.NormalizeKey(key);
            var folder = PhotoGroups.EnsureFolder(LikedFolder, key);

            // A fresh listing: the folder may have been emptied or added to since the last scan
            var library = LikedLibrary.Scan(folder, files);

            string filePath;
            bool added;
            var existing = library.Find(photo);
            if (existing is not null)
            {
                File.Delete(existing);
                library.Remove(existing);
                filePath = existing;
                added = false;
            }
            else
            {
                filePath = library.GetNewCopyPath(photo);
                LikedLibrary.CopyPhoto(photo.OriginalFilePath, filePath);
                library.Add(filePath, photo);
                added = true;
            }

            return new GroupToggleResult(key, folder, filePath, added, RefreshFolders(folder, library, files));
        }

        /// <summary>
        /// The known folders, brought up to date: folders that were renamed or deleted in the meantime are dropped,
        /// new or renamed ones are scanned, and the toggled folder gets its fresh index.
        /// </summary>
        private Dictionary<string, (char Key, LikedLibrary Library)> RefreshFolders(
            string toggledFolder, LikedLibrary toggledLibrary, IReadOnlyList<PhotoFile> files)
        {
            var folders = new Dictionary<string, (char Key, LikedLibrary Library)>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in PhotoGroups.FindFolders(LikedFolder))
            {
                if (string.Equals(group.Path, toggledFolder, StringComparison.OrdinalIgnoreCase))
                    folders[group.Path] = (group.Key, toggledLibrary);
                else if (_folders.TryGetValue(group.Path, out var known))
                    folders[group.Path] = known;
                else
                    folders[group.Path] = (group.Key, LikedLibrary.Scan(group.Path, files));
            }

            return folders;
        }

        public void Update(GroupToggleResult result) =>
            _folders = result.Folders;

        /// <summary>The group folders holding a copy of the photo, ordered by key (1-9, then A-Z).</summary>
        public IEnumerable<string> FoldersOf(PhotoFile photo) =>
            OrderedFolders()
                .Where(f => f.Value.Library.Find(photo) is not null)
                .Select(f => f.Key);

        /// <summary>The copies of the photo in the group folders, as full paths (the copy in the liked folder itself is <see cref="PhotoFile.LikedFilePath"/>).</summary>
        public IReadOnlyList<string> CopiesOf(PhotoFile photo) =>
            [.. _folders.Values.Select(f => f.Library.Find(photo)).OfType<string>()];

        /// <summary>
        /// Every group folder there is, ordered by key (1-9, then A-Z), with the ones holding a copy of the photo marked.
        /// Renamed folders appear under their new name; a group that has no folder yet isn't listed.
        /// </summary>
        public IReadOnlyList<GroupLegendEntry> GetLegend(PhotoFile photo) =>
            OrderedFolders()
                .Select(f => new GroupLegendEntry(f.Value.Key, NameOf(f.Key), f.Value.Library.Find(photo) is not null))
                .ToList();

        private IEnumerable<KeyValuePair<string, (char Key, LikedLibrary Library)>> OrderedFolders() =>
            _folders
                .OrderBy(f => f.Value.Key)
                .ThenBy(f => f.Key, StringComparer.OrdinalIgnoreCase);

        public bool IsInAnyGroup(PhotoFile photo) =>
            _folders.Values.Any(f => f.Library.Find(photo) is not null);

        /// <summary>Names of all folders holding a copy of the photo: the liked folder (if liked), then its groups.</summary>
        public IReadOnlyList<string> GetSavedInNames(PhotoFile photo)
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(photo.LikedFilePath))
                names.Add(NameOf(LikedFolder));

            foreach (var folder in FoldersOf(photo))
                names.Add(NameOf(folder));

            return names;
        }

        /// <summary>
        /// The names with each folder wrapped in brackets, for the badge on the photo: "[Liked] [3 - Group]".
        /// With <paramref name="keepNamesTogether"/> the spaces inside a name don't break, so a long list wraps between folders.
        /// </summary>
        public static string FormatFolderList(IEnumerable<string> names, bool keepNamesTogether = false) =>
            string.Join(" ", names.Select(name => $"[{(keepNamesTogether ? name.Replace(' ', '\u00A0') : name)}]"));

        private static string NameOf(string folder)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            return string.IsNullOrEmpty(name) ? folder : name;
        }
    }
}
