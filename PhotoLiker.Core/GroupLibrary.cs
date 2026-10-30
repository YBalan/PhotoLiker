using System.Diagnostics;

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
    /// What it takes to save many photos into a group, worked out before anything is copied (see <see cref="GroupLibrary.PlanAdd"/>).
    /// </summary>
    /// <param name="Folder">The folder of the group; if <paramref name="FolderIsNew"/> it does not exist yet and is made when the photos are copied.</param>
    /// <param name="ToCopy">The photos that have no copy in the group yet.</param>
    /// <param name="AlreadyThere">Photos that are in the group already (or are the same copy as another of the photos); they are left alone.</param>
    /// <param name="Bytes">The size of the photos to copy.</param>
    /// <param name="Request">The search the photos are the results of; a folder that is made is named after it.</param>
    public sealed record GroupAddPlan(
        char Key, string Folder, IReadOnlyList<PhotoFile> ToCopy, int AlreadyThere, long Bytes, bool FolderIsNew = false, string? Request = null);

    /// <summary>How far <see cref="GroupLibrary.AddAll"/> is.</summary>
    public readonly record struct GroupAddProgress(int Done, int Total, long BytesDone, long BytesTotal, string FileName);

    public readonly record struct GroupAddFailure(string Path, string Message);

    /// <summary>What <see cref="GroupLibrary.AddAll"/> did, for the UI thread to apply with <see cref="GroupLibrary.Update(GroupAddResult)"/>.</summary>
    /// <param name="Skipped">Photos that turned out to have a copy in the group already when their turn came.</param>
    /// <param name="Cancelled">Stopped by the caller; the copies made so far stay.</param>
    /// <param name="GaveUp">Stopped after <see cref="GroupLibrary.MaxFailuresInARow"/> failures in a row (a share that went away, a full disk).</param>
    public sealed record GroupAddResult(
        char Key,
        string Folder,
        int Copied,
        long Bytes,
        int Skipped,
        IReadOnlyList<GroupAddFailure> Failures,
        bool Cancelled,
        bool GaveUp,
        IReadOnlyDictionary<string, (char Key, LikedLibrary Library)> Folders);

    /// <summary>
    /// Knows which photos have a copy in the group folders under the liked folder ("A - Group", "1 - Group", ...),
    /// so the UI can list them without asking the (possibly network) file system.
    ///
    /// Every group folder is indexed like the liked folder is (see <see cref="LikedLibrary"/>), and its copies are named
    /// the same way (see <see cref="CopyName"/>). The file system work (<see cref="Scan"/>, <see cref="Toggle"/>) can run on
    /// any thread: it never changes the index, it returns a new one that the UI thread - the only one that reads the
    /// index - swaps in with <see cref="Update"/>.
    /// </summary>
    public sealed class GroupLibrary
    {
        public const int MaxFailuresInARow = 10;

        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

        private IReadOnlyDictionary<string, (char Key, LikedLibrary Library)> _folders;

        private GroupLibrary(string likedFolder, IReadOnlyDictionary<string, (char Key, LikedLibrary Library)> folders)
        {
            LikedFolder = likedFolder;
            _folders = folders;
        }

        public string LikedFolder { get; }

        /// <summary>Lists the group folders under the liked folder.</summary>
        public static GroupLibrary Scan(string likedFolder)
        {
            var folders = new Dictionary<string, (char Key, LikedLibrary Library)>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in PhotoGroups.FindFolders(likedFolder))
                folders[group.Path] = (group.Key, LikedLibrary.Scan(group.Path));

            return new GroupLibrary(likedFolder, folders);
        }

        /// <summary>
        /// Saves a copy of the photo in the folder of the group, or removes the copy again if the folder already has one.
        /// The folder is a renamed one if there is any ("A - Group Family"), else it is created ("A - Group").
        /// </summary>
        public GroupToggleResult Toggle(char key, PhotoFile photo)
        {
            key = PhotoGroups.NormalizeKey(key);
            var folder = PhotoGroups.EnsureFolder(LikedFolder, key);

            // A fresh listing: the folder may have been emptied or added to since the last scan
            var library = LikedLibrary.Scan(folder);

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

            return new GroupToggleResult(key, folder, filePath, added, RefreshFolders(folder, library));
        }

        /// <summary>
        /// Works out what saving the photos into the group would take: which of them have no copy there yet, and how many
        /// bytes those are. Looks at the folder as it is now (not at what the library knew), so what the user is told
        /// before agreeing is right. Creates nothing. Can run on any thread.
        /// </summary>
        /// <param name="request">The search the photos are the results of. If the group has no folder yet, the folder that is made is named after it.</param>
        public GroupAddPlan PlanAdd(char key, IReadOnlyList<PhotoFile> photos, string? request = null)
        {
            key = PhotoGroups.NormalizeKey(key);
            var existing = PhotoGroups.FindFolder(LikedFolder, key);
            var folder = existing ?? Path.Combine(LikedFolder, PhotoGroups.GetFolderName(key, request));
            var library = LikedLibrary.Scan(folder);

            var toCopy = new List<PhotoFile>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alreadyThere = 0;
            long bytes = 0;
            foreach (var photo in photos)
            {
                // A copy that lies in the folder is there; photos that would be saved under one name are saved once
                if (IsInside(folder, photo.OriginalFilePath) || library.Find(photo) is not null || !names.Add(CopyName.For(photo)))
                {
                    alreadyThere++;
                    continue;
                }

                toCopy.Add(photo);
                bytes += photo.Length;
            }

            return new GroupAddPlan(key, folder, toCopy, alreadyThere, bytes, FolderIsNew: existing is null, request);
        }

        /// <summary>
        /// Saves a copy of every photo of the plan in the folder of its group (made, named after the search, if it is not there), the way
        /// <see cref="Toggle"/> does one photo, but only ever adds: a photo that has a copy already is left alone and not
        /// taken out. A photo that cannot be copied is noted and the others go on; after <see cref="MaxFailuresInARow"/>
        /// failures in a row it stops. Cancelling stops between two photos and keeps what is copied. Can run on any thread;
        /// like <see cref="Toggle"/> it returns the new state, which the UI thread swaps in with <see cref="Update(GroupAddResult)"/>.
        /// </summary>
        public GroupAddResult AddAll(GroupAddPlan plan, IProgress<GroupAddProgress>? progress, CancellationToken cancellationToken)
        {
            var folder = PhotoGroups.EnsureFolder(LikedFolder, plan.Key, plan.Request);
            var library = LikedLibrary.Scan(folder);

            var failures = new List<GroupAddFailure>();
            int copied = 0, skipped = 0, failedInARow = 0, done = 0;
            long copiedBytes = 0, bytesDone = 0;
            bool cancelled = false, gaveUp = false;
            var clock = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;

            foreach (var photo in plan.ToCopy)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                if (library.Find(photo) is not null)
                {
                    skipped++;
                }
                else
                {
                    var destination = library.GetNewCopyPath(photo);
                    try
                    {
                        LikedLibrary.CopyPhoto(photo.OriginalFilePath, destination);
                        library.Add(destination, photo);
                        copied++;
                        copiedBytes += photo.Length;
                        failedInARow = 0;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(new GroupAddFailure(photo.OriginalFilePath, ex.Message));
                        failedInARow++;
                    }
                }

                done++;
                bytesDone += photo.Length;
                if (failedInARow >= MaxFailuresInARow)
                {
                    gaveUp = true;
                    break;
                }

                // A local disk copies thousands of small photos a second: the UI is told a few times a second, not each time
                if (progress is not null && (done == plan.ToCopy.Count || clock.Elapsed - lastReport >= ProgressInterval))
                {
                    lastReport = clock.Elapsed;
                    progress.Report(new GroupAddProgress(done, plan.ToCopy.Count, bytesDone, plan.Bytes, Path.GetFileName(photo.OriginalFilePath)));
                }
            }

            return new GroupAddResult(plan.Key, folder, copied, copiedBytes, skipped, failures, cancelled, gaveUp, RefreshFolders(folder, library));
        }

        private static bool IsInside(string folder, string path)
        {
            var directory = Path.GetDirectoryName(path);
            return directory is not null &&
                string.Equals(Path.TrimEndingDirectorySeparator(directory), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The known folders, brought up to date: folders that were renamed or deleted in the meantime are dropped,
        /// new or renamed ones are scanned, and the toggled folder gets its fresh index.
        /// </summary>
        private Dictionary<string, (char Key, LikedLibrary Library)> RefreshFolders(string toggledFolder, LikedLibrary toggledLibrary)
        {
            var folders = new Dictionary<string, (char Key, LikedLibrary Library)>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in PhotoGroups.FindFolders(LikedFolder))
            {
                if (string.Equals(group.Path, toggledFolder, StringComparison.OrdinalIgnoreCase))
                    folders[group.Path] = (group.Key, toggledLibrary);
                else if (_folders.TryGetValue(group.Path, out var known))
                    folders[group.Path] = known;
                else
                    folders[group.Path] = (group.Key, LikedLibrary.Scan(group.Path));
            }

            return folders;
        }

        public void Update(GroupToggleResult result) =>
            _folders = result.Folders;

        public void Update(GroupAddResult result) =>
            _folders = result.Folders;

        /// <summary>A copy in a group folder was renamed (see <see cref="LikedLibrary.RenameCopy"/>): the index follows. UI thread only, like every read of the index.</summary>
        public void Replace(string oldCopyPath, string newCopyPath, PhotoFile photo)
        {
            foreach (var (folder, (_, library)) in _folders)
            {
                if (!IsInside(folder, oldCopyPath))
                    continue;

                library.Remove(oldCopyPath);
                library.Add(newCopyPath, photo);
            }
        }

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
