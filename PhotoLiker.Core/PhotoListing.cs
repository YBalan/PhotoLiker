using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;

namespace PhotoLiker.Core
{
    /// <summary>
    /// What decides which photos a listing holds. A saved listing is only used again for the same key, so changing the
    /// extensions, the subfolders or the liked folder lists the folder afresh.
    /// </summary>
    public sealed record ListingKey(string Folder, bool Recursive, IReadOnlyCollection<string> Extensions, string? ExcludedFolder)
    {
        public bool IsBlank => string.IsNullOrWhiteSpace(Folder);

        internal string Root => IsBlank ? string.Empty : Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder));

        /// <summary>The key as text; the saved file starts with it, and is not used for a listing whose text differs.</summary>
        internal string Text
        {
            get
            {
                var excluded = string.IsNullOrWhiteSpace(ExcludedFolder) ? string.Empty : Path.TrimEndingDirectorySeparator(Path.GetFullPath(ExcludedFolder));
                var extensions = string.Join(',', Extensions.Select(e => e.ToLowerInvariant()).Order(StringComparer.Ordinal));
                return $"{Root.ToLowerInvariant()}|{Recursive}|{extensions}|{excluded.ToLowerInvariant()}";
            }
        }

        /// <summary>
        /// The saved listing of this key, one file per key. Like the search index it lives on this machine: it is a cache
        /// that can always be made again, and a share is slow to read and the wrong place to write often.
        /// </summary>
        public string CacheFile
        {
            get
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text)))[..10];
                var name = new string(Path.GetFileName(Root.ToLowerInvariant()).Where(char.IsLetterOrDigit).Take(24).ToArray());
                return Path.Combine(AppPaths.DataFolder, "listing", $"{(name.Length > 0 ? name : "root")}-{hash}.bin");
            }
        }
    }

    /// <summary>A photo as the folder listing describes it; times are UTC ticks.</summary>
    public readonly record struct ListedPhoto(string Name, long Length, long WrittenTicks, long CreatedTicks);

    /// <summary>One folder of a listing. <paramref name="WrittenTicks"/> is the time the folder had when it was listed; 0 means "list it again next time".</summary>
    internal sealed record ListedFolder(string RelativePath, long WrittenTicks, string[] Subfolders, ListedPhoto[] Photos);

    /// <summary>What <see cref="PhotoListing.Verify"/> found.</summary>
    /// <param name="Incomplete">Folders that could not be read this time; they keep what was known of them and are looked at again next time.</param>
    /// <param name="Reachable">False when the folder itself could not be reached (a share that is off): nothing was changed.</param>
    public sealed record ListingChanges(int Added, int Removed, int Changed, int Incomplete, bool Reachable)
    {
        public static readonly ListingChanges Unreachable = new(0, 0, 0, 0, false);

        public bool HasChanges => Added + Removed + Changed > 0;
    }

    /// <summary>
    /// The photos of a folder as a list that is kept on this machine between runs, so a folder with tens of thousands
    /// of photos on a network share opens at once instead of being listed again, which costs a round trip per subfolder
    /// (6.5 s for 16,000 photos in 985 folders on a router, 0.15 s on a local disk).
    ///
    /// The listing is a tree of folders, each with the time the folder had when it was listed. A folder's time changes when
    /// something is created, renamed, moved or deleted in it, but not when a file is changed in place and not when something
    /// changes deeper down. So <see cref="Verify"/> only asks every known folder for its time (a router answers in about
    /// 2 ms), lists again the ones that differ, and finds new subfolders through their parent. A photo that another program
    /// changes in place is not noticed, which suits an archive nobody else touches; <see cref="Scan"/> makes a new listing.
    /// (A folder listed by its parent can show an older time than it has itself, which NTFS does for a folder that was just
    /// changed: the first <see cref="Verify"/> then lists it once more, finds nothing, and keeps the time it asked for.)
    ///
    /// Everything is safe to call from any thread. The file system work (<see cref="Scan"/>, <see cref="Verify"/>) and
    /// <see cref="Save"/> run for as long as they run, the rest is quick.
    /// </summary>
    public sealed class PhotoListing
    {
        // A router answers about 1.5 ms per folder with eight asking at once and 3 ms with one
        private const int StatWorkers = 8;
        // Listing 4, 8 or 16 folders at once took the same 4.6 s on a router (6.5 s one at a time): it is the router that is slow
        private const int ListWorkers = 4;

        private readonly object _gate = new();
        private Dictionary<string, ListedFolder> _folders;
        private HashSet<string>? _removedWhileVerifying;
        private bool _needsSave;

        private PhotoListing(ListingKey key, Dictionary<string, ListedFolder> folders, bool needsSave)
        {
            Key = key;
            _folders = folders;
            _needsSave = needsSave;
        }

        public ListingKey Key { get; }

        /// <summary>The listing changed since it was loaded or saved.</summary>
        public bool NeedsSave
        {
            get { lock (_gate) return _needsSave; }
        }

        // ── making and loading ─────────────────────────────────────────────

        /// <summary>The saved listing for this key, or null if there is none, it is for other settings or it cannot be read.</summary>
        public static PhotoListing? Load(ListingKey key)
        {
            if (key.IsBlank)
                return null;

            var folders = ListingFile.Read(key.CacheFile, key.Text);
            return folders is null ? null : new PhotoListing(key, folders, needsSave: false);
        }

        /// <summary>Lists the folder from scratch. Throws if it cannot be read, as <see cref="PhotoScanner.Scan"/> does.</summary>
        public static PhotoListing Scan(ListingKey key, CancellationToken cancellationToken = default)
        {
            if (key.IsBlank)
                return new PhotoListing(key, new Dictionary<string, ListedFolder>(StringComparer.OrdinalIgnoreCase), needsSave: false);

            var root = new DirectoryInfo(key.Root);
            if (!root.Exists)
                throw new DirectoryNotFoundException($"Could not find a part of the path '{key.Root}'.");

            var excluded = PhotoScanner.ExcludedFolders(key.Folder, key.ExcludedFolder);
            var listed = ListMany(key, excluded, [(string.Empty, root.LastWriteTimeUtc.Ticks)], isKnown: _ => false, cancellationToken);
            if (listed[string.Empty] is null)
                throw new IOException($"The folder '{key.Root}' could not be read.");

            var folders = new Dictionary<string, ListedFolder>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relative, folder) in listed)
            {
                if (folder is not null)
                    folders[relative] = folder;
            }
            return new PhotoListing(key, folders, needsSave: true);
        }

        // ── reading ────────────────────────────────────────────────────────

        /// <summary>The photos, in the given order. The photos are new objects each time (the liked path is not part of a listing).</summary>
        public List<PhotoFile> ToPhotoFiles(PhotoSortOrder sortOrder)
        {
            var photos = new List<PhotoFile>();
            lock (_gate)
            {
                if (_folders.Count == 0)
                    return photos;

                var pending = new Stack<string>();
                pending.Push(string.Empty);
                while (pending.Count > 0)
                {
                    var relative = pending.Pop();
                    if (!_folders.TryGetValue(relative, out var folder))
                        continue;

                    var path = AbsolutePath(Key.Root, relative);
                    foreach (var photo in folder.Photos)
                    {
                        photos.Add(new PhotoFile(Path.Combine(path, photo.Name), string.Empty)
                        {
                            Length = photo.Length,
                            LastWriteTimeUtc = new DateTime(photo.WrittenTicks, DateTimeKind.Utc),
                            CreationTimeUtc = new DateTime(photo.CreatedTicks, DateTimeKind.Utc),
                        });
                    }

                    for (var i = folder.Subfolders.Length - 1; i >= 0; i--)
                        pending.Push(Join(relative, folder.Subfolders[i]));
                }
            }

            PhotoScanner.Sort(photos, sortOrder);
            return photos;
        }

        // ── changing ───────────────────────────────────────────────────────

        /// <summary>
        /// The app moved this photo away itself. The folder keeps the time it had, so the next <see cref="Verify"/> still
        /// finds it changed and lists it again; until then (a share can show a folder's old time for about ten seconds
        /// after a change) the listing is right already.
        /// </summary>
        public void Remove(string photoPath)
        {
            var relative = RelativeFolderOf(photoPath);
            var name = Path.GetFileName(photoPath);
            lock (_gate)
            {
                _removedWhileVerifying?.Add(photoPath);
                if (relative is not null)
                    RemovePhoto(_folders, relative, name);
            }
        }

        private bool RemovePhoto(Dictionary<string, ListedFolder> folders, string relative, string name)
        {
            if (!folders.TryGetValue(relative, out var folder))
                return false;

            var kept = folder.Photos.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (kept.Length == folder.Photos.Length)
                return false;

            folders[relative] = folder with { Photos = kept };
            _needsSave = true;
            return true;
        }

        private string? RelativeFolderOf(string photoPath)
        {
            var directory = Path.GetDirectoryName(photoPath);
            if (directory is null)
                return null;

            var relative = Path.GetRelativePath(Key.Root, directory);
            if (relative == ".")
                return string.Empty;
            return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? null
                : relative;
        }

        /// <summary>
        /// Brings the listing up to date: asks every known folder for its time, lists again the folders whose time is not the
        /// one they were listed with (and the subfolders found in them), forgets folders that are gone. Cancelling leaves
        /// the listing as it was.
        /// </summary>
        public ListingChanges Verify(CancellationToken cancellationToken = default)
        {
            if (Key.IsBlank)
                return ListingChanges.Unreachable;

            Dictionary<string, ListedFolder> known;
            lock (_gate)
            {
                known = new Dictionary<string, ListedFolder>(_folders, StringComparer.OrdinalIgnoreCase);
                _removedWhileVerifying = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var result = VerifyKnown(known, cancellationToken, out var folders);
                if (folders is null)
                    return result;

                lock (_gate)
                {
                    // Photos the app moved away while this ran may be in the new listing: it was read before they went
                    foreach (var gone in _removedWhileVerifying)
                    {
                        if (RelativeFolderOf(gone) is { } relative)
                            RemovePhoto(folders, relative, Path.GetFileName(gone));
                    }

                    _folders = folders;
                    _needsSave = true;
                }
                return result;
            }
            finally
            {
                lock (_gate)
                    _removedWhileVerifying = null;
            }
        }

        /// <summary>The new folders, or null (and the result to return) when there is nothing to change.</summary>
        private ListingChanges VerifyKnown(Dictionary<string, ListedFolder> known, CancellationToken cancellationToken, out Dictionary<string, ListedFolder>? folders)
        {
            folders = null;
            if (!known.ContainsKey(string.Empty) || !Directory.Exists(Key.Root))
                return ListingChanges.Unreachable;

            // 1. The time of every known folder: one question each, many at once
            var written = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(known.Keys, new ParallelOptions { MaxDegreeOfParallelism = StatWorkers, CancellationToken = cancellationToken }, relative =>
            {
                var info = new DirectoryInfo(AbsolutePath(Key.Root, relative));
                if (info.Exists)
                    written[relative] = info.LastWriteTimeUtc.Ticks;
            });
            if (!written.ContainsKey(string.Empty))
                return ListingChanges.Unreachable; // the folder went away while it was being asked about

            // 2. Which folders to list again. A folder that is not there any more is not listed: its parent is, and says whether it is really gone
            var relist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relative, folder) in known)
            {
                if (!written.TryGetValue(relative, out var now))
                    relist.Add(ParentOf(relative));
                else if (folder.WrittenTicks == 0 || folder.WrittenTicks != now)
                    relist.Add(relative);

                foreach (var name in folder.Subfolders)
                {
                    var child = Join(relative, name);
                    if (!known.ContainsKey(child))
                        relist.Add(child); // its listing failed last time
                }
            }
            relist.RemoveWhere(relative => known.ContainsKey(relative) && !written.ContainsKey(relative));

            // 3. List them, and whatever is new below them
            var excluded = PhotoScanner.ExcludedFolders(Key.Folder, Key.ExcludedFolder);
            var start = relist.Select(relative => (relative, written.TryGetValue(relative, out var ticks) ? ticks : (long?)null)).ToList();
            var listed = ListMany(Key, excluded, start, isKnown: child => known.ContainsKey(child) || relist.Contains(child), cancellationToken);

            // 4. The new tree: the old folders, the listed ones in their place, what no longer hangs off the root dropped
            var updated = new Dictionary<string, ListedFolder>(known, StringComparer.OrdinalIgnoreCase);
            int added = 0, removed = 0, changed = 0, incomplete = 0;
            foreach (var (relative, folder) in listed)
            {
                known.TryGetValue(relative, out var before);
                if (folder is null)
                {
                    incomplete++;
                    if (before is not null)
                        updated[relative] = before with { WrittenTicks = 0 }; // keep what is known, look again next time
                    continue;
                }

                var (a, r, c) = Compare(before?.Photos ?? [], folder.Photos);
                added += a;
                removed += r;
                changed += c;
                updated[relative] = folder;
            }

            foreach (var relative in updated.Keys.Except(Reachable(updated), StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (known.TryGetValue(relative, out var gone))
                    removed += gone.Photos.Length;
                updated.Remove(relative);
            }

            // Nothing to store when every folder is as it was (not even a new time)
            if (listed.Count == 0 && updated.Count == known.Count)
                return new ListingChanges(0, 0, 0, 0, true);

            folders = updated;
            return new ListingChanges(added, removed, changed, incomplete, true);
        }

        private static HashSet<string> Reachable(Dictionary<string, ListedFolder> folders)
        {
            var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(string.Empty);
            while (pending.Count > 0)
            {
                var relative = pending.Pop();
                if (!folders.TryGetValue(relative, out var folder) || !reached.Add(relative))
                    continue;

                foreach (var name in folder.Subfolders)
                    pending.Push(Join(relative, name));
            }
            return reached;
        }

        private static (int Added, int Removed, int Changed) Compare(ListedPhoto[] before, ListedPhoto[] after)
        {
            var old = new Dictionary<string, ListedPhoto>(before.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var photo in before)
                old[photo.Name] = photo;

            int added = 0, changed = 0;
            foreach (var photo in after)
            {
                if (!old.Remove(photo.Name, out var previous))
                    added++;
                else if (previous != photo)
                    changed++;
            }
            return (added, old.Count, changed);
        }

        // ── saving ─────────────────────────────────────────────────────────

        /// <summary>Writes the listing to its file (a new file is moved over the old one, so a crash leaves the old). Throws on a file error.</summary>
        public void Save()
        {
            if (Key.IsBlank)
                return;

            Dictionary<string, ListedFolder> folders;
            lock (_gate)
            {
                folders = new Dictionary<string, ListedFolder>(_folders, StringComparer.OrdinalIgnoreCase);
                _needsSave = false;
            }

            try
            {
                ListingFile.Write(Key.CacheFile, Key.Text, folders);
            }
            catch
            {
                lock (_gate)
                    _needsSave = true;
                throw;
            }
        }

        // ── listing folders ────────────────────────────────────────────────

        /// <summary>
        /// Lists the given folders, and every folder found below them that <paramref name="isKnown"/> does not already know of,
        /// a level at a time. The time a folder has is the one asked for before it was listed (or the one its parent listed it
        /// with), so a change while it is being listed makes it look changed next time and not unchanged. A folder that
        /// could not be read is null.
        /// </summary>
        private static ConcurrentDictionary<string, ListedFolder?> ListMany(
            ListingKey key, List<string> excluded, IEnumerable<(string Relative, long? WrittenTicks)> start, Func<string, bool> isKnown, CancellationToken cancellationToken)
        {
            var results = new ConcurrentDictionary<string, ListedFolder?>(StringComparer.OrdinalIgnoreCase);
            var level = start.ToList();
            while (level.Count > 0)
            {
                var next = new ConcurrentBag<(string Relative, long? WrittenTicks)>();
                Parallel.ForEach(level, new ParallelOptions { MaxDegreeOfParallelism = ListWorkers, CancellationToken = cancellationToken }, item =>
                {
                    var written = item.WrittenTicks ?? StatTicks(key.Root, item.Relative);
                    var listing = ListOne(key, excluded, item.Relative);
                    if (listing is null)
                    {
                        results[item.Relative] = null;
                        return;
                    }

                    var (photos, subfolders) = listing.Value;
                    results[item.Relative] = new ListedFolder(item.Relative, written, [.. subfolders.Select(s => s.Name)], photos);
                    foreach (var (name, subWritten) in subfolders)
                    {
                        var child = Join(item.Relative, name);
                        if (!isKnown(child))
                            next.Add((child, subWritten));
                    }
                });
                level = [.. next];
            }
            return results;
        }

        private static long StatTicks(string root, string relative)
        {
            var info = new DirectoryInfo(AbsolutePath(root, relative));
            return info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
        }

        /// <summary>The photos and the subfolders of one folder, or null if it cannot be read. Same rules as <see cref="PhotoScanner.Scan"/>.</summary>
        private static (ListedPhoto[] Photos, (string Name, long WrittenTicks)[] Subfolders)? ListOne(ListingKey key, List<string> excluded, string relative)
        {
            var options = new EnumerationOptions
            {
                // One unreadable entry shouldn't fail the whole listing; SMB shares often have a few
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System,
            };

            var entries = new FileSystemEnumerable<Entry>(
                AbsolutePath(key.Root, relative),
                (ref FileSystemEntry e) => new Entry(
                    e.IsDirectory, e.FileName.ToString(), e.IsDirectory ? 0 : e.Length,
                    e.LastWriteTimeUtc.UtcDateTime.Ticks, e.IsDirectory ? 0 : e.CreationTimeUtc.UtcDateTime.Ticks),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => e.IsDirectory
                    ? key.Recursive && !PhotoScanner.IsSkippedFolder(ref e, excluded) && !IsLinkToAncestor(ref e)
                    : PhotoScanner.IsPhoto(e.FileName, key.Extensions),
            };

            try
            {
                var photos = new List<ListedPhoto>();
                var subfolders = new List<(string, long)>();
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory)
                        subfolders.Add((entry.Name, entry.WrittenTicks));
                    else
                        photos.Add(new ListedPhoto(entry.Name, entry.Length, entry.WrittenTicks, entry.CreatedTicks));
                }
                return ([.. photos], [.. subfolders]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private readonly record struct Entry(bool IsDirectory, string Name, long Length, long WrittenTicks, long CreatedTicks);

        /// <summary>A link to a folder above it would be followed for ever; a link to anything else is followed like a folder.</summary>
        private static bool IsLinkToAncestor(ref FileSystemEntry entry)
        {
            if (!entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;

            var path = Path.TrimEndingDirectorySeparator(entry.ToFullPath());
            try
            {
                if (new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true) is not { } target)
                    return false;

                var linked = Path.TrimEndingDirectorySeparator(target.FullName);
                return path.StartsWith(linked + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, linked, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true; // a link that cannot be read is not followed
            }
        }

        // ── paths ──────────────────────────────────────────────────────────

        private static string Join(string relative, string name) =>
            relative.Length == 0 ? name : relative + Path.DirectorySeparatorChar + name;

        private static string ParentOf(string relative)
        {
            var cut = relative.LastIndexOf(Path.DirectorySeparatorChar);
            return cut < 0 ? string.Empty : relative[..cut];
        }

        private static string AbsolutePath(string root, string relative) =>
            relative.Length == 0 ? root : Path.Combine(root, relative);
    }
}
