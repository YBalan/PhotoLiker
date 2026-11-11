using System.Text.Json;

namespace PhotoLiker.Search
{
    /// <summary>
    /// The groups the user has sorted the people into ("Family", "Friends", "Work"). A group is a name and the names of its people; a person can be
    /// in several groups, or in none. Groups are about the people, not about a photo folder: a person is the same person in every folder, so the
    /// groups are one set for all folders, kept in the app data folder (<see cref="SearchPaths.PeopleGroupsFile"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The people are named by their names, in any case (the way <see cref="PeopleLibrary"/> knows them). A name in a group that nobody carries (the
    /// last face of that person was un-named, or the person lives in a folder this machine has not seen) stays in the file: when the person comes
    /// back, the group is still there. Nothing here knows who exists; <see cref="PeopleLibrary"/> checks that a group does not take the name of a person.
    /// Every change reads the file again first and writes it whole afterwards, so that another copy of the app does not lose its changes.
    /// </para>
    /// <para>
    /// The photo folder gets a copy of the groups (<see cref="SearchPaths.GroupsFile"/>), so that the groups travel with the photos, to another
    /// computer or into a backup, the way the names do. The two are one set that was saved at a time, and the one that was saved later is the set:
    /// when the folder is opened (and when the groups are read again) a folder copy that is newer than the groups of this computer replaces them
    /// (a new computer, or a change made on another one), and an older or missing one is written again. A change is written to both at once.
    /// So a group that was deleted stays deleted, in every folder that is opened afterwards. The price: two computers that both changed the
    /// groups since they last met do not merge, the later change wins as a whole. The copy is only made in a folder that has people named in it
    /// (<c>folderCopyWanted</c>) and where there are groups, and a failure to write it never stops a change (<see cref="FolderCopyError"/>).
    /// </para>
    /// </remarks>
    public sealed class PersonGroups
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed class FileModel
        {
            public int Version { get; set; } = 1;

            /// <summary>When these groups were saved (UTC). A file without it counts as saved when the file was last written.</summary>
            public DateTime? Saved { get; set; }

            public List<GroupModel> Groups { get; set; } = [];
        }

        private sealed class GroupModel
        {
            public string Name { get; set; } = string.Empty;
            public List<string> Members { get; set; } = [];
        }

        private enum Read { Missing, Found, Failed }

        private readonly object _gate = new();
        private readonly string? _file;
        private readonly string? _folderCopy;
        private readonly Func<bool>? _folderCopyWanted;
        private List<GroupModel> _groups = [];
        private DateTime _saved = DateTime.MinValue;

        /// <param name="file">The file the groups are kept in; null keeps them in memory only.</param>
        /// <param name="folderCopy">The copy of the groups in a photo folder; null for none. Needs a <paramref name="file"/>.</param>
        /// <param name="folderCopyWanted">Whether the folder is one that should get a copy (it has people named in it); null: always.</param>
        public PersonGroups(string? file = null, string? folderCopy = null, Func<bool>? folderCopyWanted = null)
        {
            _file = file;
            _folderCopy = file is null ? null : folderCopy;
            _folderCopyWanted = folderCopyWanted;
        }

        /// <summary>The groups kept in a file (and in the copy of a photo folder); none if there is no file or it can't be read.</summary>
        public static PersonGroups Load(string? file, string? folderCopy = null, Func<bool>? folderCopyWanted = null)
        {
            var groups = new PersonGroups(file, folderCopy, folderCopyWanted);
            groups.Reload();
            return groups;
        }

        /// <summary>Why the file could not be read, or was set aside because it was damaged; null when it was fine.</summary>
        public string? LoadError { get; private set; }

        /// <summary>
        /// Why the copy in the photo folder could not be read, or was set aside because it was damaged, or could not be written (a read-only folder,
        /// a network drive that is off); null when it was fine. The groups of this computer are not affected.
        /// </summary>
        public string? FolderCopyError { get; private set; }

        /// <summary>The names of the groups, in alphabetical order.</summary>
        public IReadOnlyList<string> Names
        {
            get { lock (_gate) return [.. _groups.Select(g => g.Name).Order(StringComparer.CurrentCultureIgnoreCase)]; }
        }

        public int Count
        {
            get { lock (_gate) return _groups.Count; }
        }

        /// <summary>The name of the group as it was first typed, if the text is the name of a group (any case).</summary>
        public bool TryFind(string text, out string name)
        {
            lock (_gate)
            {
                if (Find(text) is { } group)
                {
                    name = group.Name;
                    return true;
                }
            }
            name = string.Empty;
            return false;
        }

        /// <summary>The people of a group, in alphabetical order; none for a group that does not exist.</summary>
        public IReadOnlyList<string> MembersOf(string group)
        {
            lock (_gate)
                return Find(group) is { } found ? [.. found.Members.Order(StringComparer.CurrentCultureIgnoreCase)] : [];
        }

        /// <summary>The groups a person is in, in alphabetical order.</summary>
        public IReadOnlyList<string> GroupsOf(string person)
        {
            lock (_gate)
                return [.. _groups.Where(g => Contains(g, person)).Select(g => g.Name).Order(StringComparer.CurrentCultureIgnoreCase)];
        }

        public bool IsMember(string group, string person)
        {
            lock (_gate)
                return Find(group) is { } found && Contains(found, person);
        }

        /// <summary>
        /// Reads the file again, and the copy in the photo folder: the later saved of the two is the set, and the other is brought in line
        /// (a no-op for groups kept in memory only). A file that can't be read leaves what is known.
        /// </summary>
        public void Reload()
        {
            lock (_gate)
            {
                ReadFile();
                Reconcile();
            }
        }

        /// <summary>Makes the copy in the photo folder if the folder wants one and has none yet (the first person was just named in it).</summary>
        public void MakeFolderCopyIfMissing()
        {
            lock (_gate)
            {
                if (_folderCopy is not null && !File.Exists(_folderCopy))
                    SaveFolderCopy();
            }
        }

        // ── changing the groups ────────────────────────────────────────────

        /// <summary>
        /// Makes a group; the same one if there is a group of that name already (any case). Returns the name as the group has it.
        /// Throws if the file can't be written (the group stays until the app closes).
        /// </summary>
        public string Create(string name)
        {
            name = name.Trim();
            if (name.Length == 0)
                throw new ArgumentException(SearchLibStrings.GroupNeedsName);

            lock (_gate)
            {
                var created = name;
                Change(() =>
                {
                    if (Find(name) is { } existing)
                    {
                        created = existing.Name;
                        return false;
                    }
                    _groups.Add(new GroupModel { Name = name });
                    return true;
                });
                return created;
            }
        }

        /// <summary>Gives a group another name (also just another spelling of its own). Throws if another group has that name.</summary>
        public void Rename(string group, string newName)
        {
            newName = newName.Trim();
            if (newName.Length == 0)
                throw new ArgumentException(SearchLibStrings.GroupNeedsName);

            lock (_gate)
            {
                Change(() =>
                {
                    if (Find(group) is not { } found)
                        return false;
                    if (Find(newName) is { } other && !ReferenceEquals(other, found))
                        throw new ArgumentException(string.Format(SearchLibStrings.GroupExistsFormat, other.Name));

                    found.Name = newName;
                    return true;
                });
            }
        }

        /// <summary>Takes a group away; its people stay (and stay named). Nothing happens for a group that does not exist.</summary>
        public void Delete(string group)
        {
            lock (_gate)
                Change(() => _groups.RemoveAll(g => SameName(g.Name, group)) > 0);
        }

        /// <summary>Puts a person into a group or takes them out of it. A group that does not exist yet is made when a person is put into it.</summary>
        public void SetMember(string group, string person, bool member) => SetMembers(group, [person], member);

        /// <summary>The same for several people at once, written once.</summary>
        public void SetMembers(string group, IEnumerable<string> people, bool member)
        {
            var names = people.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            lock (_gate)
            {
                Change(() =>
                {
                    var found = Find(group);
                    var changed = false;
                    if (found is null)
                    {
                        if (!member || names.Count == 0 || group.Trim().Length == 0)
                            return false;
                        _groups.Add(found = new GroupModel { Name = group.Trim() });
                        changed = true;
                    }

                    foreach (var person in names)
                    {
                        if (member && !Contains(found, person))
                        {
                            found.Members.Add(person);
                            changed = true;
                        }
                        else if (!member)
                        {
                            changed |= found.Members.RemoveAll(m => SameName(m, person)) > 0;
                        }
                    }
                    return changed;
                });
            }
        }

        /// <summary>
        /// A person was given another name: the new name takes the place of the old one in every group (somebody who is in a group under both names
        /// is there once). Throws if the file can't be written.
        /// </summary>
        public void RenameMember(string from, string to)
        {
            lock (_gate)
            {
                Change(() =>
                {
                    var changed = false;
                    foreach (var group in _groups)
                    {
                        if (group.Members.RemoveAll(m => SameName(m, from)) == 0)
                            continue;

                        if (!Contains(group, to))
                            group.Members.Add(to.Trim());
                        changed = true;
                    }
                    return changed;
                });
            }
        }

        /// <summary>
        /// People were deleted: they leave every group (the groups stay, even empty; somebody who is named so later starts with no group).
        /// Throws if the file can't be written.
        /// </summary>
        public void RemoveMembers(IEnumerable<string> people)
        {
            var names = people.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            lock (_gate)
            {
                Change(() => _groups.Sum(group => group.Members.RemoveAll(m => names.Any(name => SameName(m, name)))) > 0);
            }
        }

        // Reads the file, lets the change happen on what it holds, and writes it back if the change did something: to the file (which must work),
        // then to the copy in the photo folder (which may fail). The caller holds the lock.
        private void Change(Func<bool> change)
        {
            ReadFile();
            if (!change())
                return;

            var now = DateTime.UtcNow;
            _saved = now > _saved ? now : _saved.AddTicks(1); // later than what was there, even on a clock that stands still or went back
            Write(_file, createFolder: true);
            SaveFolderCopy();
        }

        private GroupModel? Find(string name) => _groups.FirstOrDefault(g => SameName(g.Name, name.Trim()));

        private static bool Contains(GroupModel group, string person) => group.Members.Any(m => SameName(m, person.Trim()));

        private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SameGroups(List<GroupModel> a, List<GroupModel> b) =>
            a.Count == b.Count && a.All(group => b.FirstOrDefault(other => SameName(other.Name, group.Name)) is { } match
                && match.Members.Count == group.Members.Count && group.Members.All(member => Contains(match, member)));

        // ── the file and the copy ──────────────────────────────────────────

        // The caller holds the lock
        private void ReadFile()
        {
            if (_file is null)
                return;

            var read = ReadModel(_file, out var groups, out var saved, out var problem);
            LoadError = problem;
            if (read == Read.Found)
            {
                _groups = groups;
                _saved = saved;
            }
            else if (read == Read.Missing)
            {
                _groups = [];
                _saved = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Reads a groups file. A damaged one is set aside (as .bad, so that it can still be looked at) and counts as missing; one that can't be
        /// opened is <see cref="Read.Failed"/> (it may be locked for a moment), and what is known stays.
        /// </summary>
        private static Read ReadModel(string file, out List<GroupModel> groups, out DateTime saved, out string? problem)
        {
            groups = [];
            saved = DateTime.MinValue;
            problem = null;
            try
            {
                if (!File.Exists(file))
                    return Read.Missing;

                var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(file), JsonOptions) ?? new FileModel();
                groups = Normalized(model.Groups);
                saved = model.Saved ?? File.GetLastWriteTimeUtc(file);
                return Read.Found;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                try
                {
                    File.Move(file, file + ".bad", overwrite: true);
                    problem = string.Format(SearchLibStrings.GroupsDamagedFormat, ex.Message, Path.GetFileName(file));
                }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
                {
                    problem = ex.Message;
                }
                return Read.Missing;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = ex.Message;
                return Read.Failed;
            }
        }

        // The later saved of the file and the copy is the set; the other one is brought in line. The caller holds the lock.
        private void Reconcile()
        {
            if (_file is null || _folderCopy is null)
                return;

            var read = ReadModel(_folderCopy, out var theirs, out var theirSaved, out var problem);
            FolderCopyError = problem;
            if (read == Read.Failed)
                return;
            if (read == Read.Found && SameGroups(theirs, _groups))
                return; // the same groups: whatever the stamps say, nothing is lost or gained

            if (read == Read.Found && theirSaved > _saved)
            {
                // Saved later than ours: a change from another computer (or a computer that has no groups yet) — ours are replaced
                _groups = theirs;
                _saved = theirSaved;
                try
                {
                    Write(_file, createFolder: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LoadError = ex.Message; // the groups are right in memory; the file is written again with the next change
                }
                return;
            }

            SaveFolderCopy(); // missing, damaged (set aside) or older than the file
            FolderCopyError ??= problem;
        }

        // The copy is made where the folder wants one and there are groups to keep; one that is there is always kept up to date, also when it
        // is time to say that the last group is gone. The caller holds the lock.
        private void SaveFolderCopy()
        {
            if (_folderCopy is null)
                return;
            if (!File.Exists(_folderCopy) && !(_groups.Count > 0 && (_folderCopyWanted?.Invoke() ?? true)))
                return;

            try
            {
                Write(_folderCopy, createFolder: false);
                FolderCopyError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FolderCopyError = ex.Message;
            }
        }

        // Names and members without the spaces around them, nothing empty, and no group or member twice (the first spelling counts)
        private static List<GroupModel> Normalized(IEnumerable<GroupModel> groups)
        {
            List<GroupModel> result = [];
            foreach (var group in groups.Where(g => !string.IsNullOrWhiteSpace(g.Name)))
            {
                var target = result.FirstOrDefault(g => SameName(g.Name, group.Name));
                if (target is null)
                    result.Add(target = new GroupModel { Name = group.Name.Trim() });

                foreach (var member in (group.Members ?? []).Where(m => !string.IsNullOrWhiteSpace(m)))
                {
                    if (!Contains(target, member))
                        target.Members.Add(member.Trim());
                }
            }
            return result;
        }

        // Written next to the file first, so that a failure half way never leaves a broken file. The folder of a photo folder is never made
        // by this (a folder that is gone is not brought back for a copy).
        private void Write(string? file, bool createFolder)
        {
            if (file is null)
                return;

            var json = JsonSerializer.Serialize(
                new FileModel
                {
                    Saved = _saved == DateTime.MinValue ? null : _saved,
                    Groups = [.. _groups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(g => new GroupModel { Name = g.Name, Members = [.. g.Members.Order(StringComparer.CurrentCultureIgnoreCase)] })],
                },
                JsonOptions);

            AtomicFile.Write(file, json, createFolder);
        }
    }
}
