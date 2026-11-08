using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoLiker.Search
{
    /// <summary>
    /// The people the user has named in a photo folder. Naming a face keeps that face's vector and where it is, as an
    /// example of how the person looks; other faces are recognized by being close to one of the examples.
    /// The examples are stored as plain JSON in the photo folder (<see cref="SearchPaths.PeopleFileName"/>), not in the
    /// index: the index can always be rebuilt, the naming took effort and travels with the photos.
    /// </summary>
    /// <remarks>
    /// The people of all photo folders are also collected in one file in the app data folder
    /// (<see cref="SearchPaths.SharedPeopleFile"/>), so that a folder opened for the first time already knows the people
    /// named in another one: a person is the same person in every folder (names are matched without caring about case),
    /// and the faces named elsewhere are examples for recognizing and searching. They are not "named here": which face of
    /// which photo was named is only known to the folder it is in (<see cref="LabelOf"/>, <see cref="Unlabel"/>).
    /// The file of a folder stays the truth about that folder; the shared file is a copy that each folder brings in line
    /// with its own file when it is opened and whenever it changes, so losing the shared file loses nothing.
    /// The people can be sorted into groups (<see cref="Groups"/>). A group is searched like a person, but it means any one of its people:
    /// its examples are the examples of all its members. A name is a person or a group, never both.
    /// </remarks>
    public sealed partial class PeopleLibrary
    {
        /// <summary>How alike (cosine of the face vectors) two faces have to be to count as the same person.</summary>
        public const float DefaultMatchThreshold = 0.40f;

        /// <summary>Two boxes in the same photo that overlap this much (intersection over union) are the same face.</summary>
        private const float SameFaceOverlap = 0.5f;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed record Example(string Photo, float X, float Y, float Width, float Height, float[] Embedding);

        private readonly object _gate = new();

        /// <summary>The faces named in this folder.</summary>
        private readonly Dictionary<string, List<Example>> _people = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The vectors of faces named in other folders, as the shared file had them when it was last read.</summary>
        private Dictionary<string, List<float[]>> _others = new(StringComparer.OrdinalIgnoreCase);

        private readonly string? _file;
        private readonly string? _sharedFile;
        private readonly string _folder;

        /// <param name="file">The people file of the folder; null keeps the people in memory only.</param>
        /// <param name="sharedFile">The file shared by all folders; null keeps this folder's people to itself.</param>
        /// <param name="folder">The photo folder, which says whose people are whose in the shared file; needed with a shared file.</param>
        /// <param name="groupsFile">The file of the groups of people; null keeps the groups in memory only.</param>
        public PeopleLibrary(string? file = null, string? sharedFile = null, string? folder = null, string? groupsFile = null)
        {
            if (sharedFile is not null && string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A shared people file needs to know which folder this is.", nameof(folder));

            _file = file;
            _sharedFile = sharedFile;
            _folder = folder is null ? string.Empty : Path.TrimEndingDirectorySeparator(folder);
            // The groups are the user's, for all folders; this folder keeps a copy of them once it has people named in it
            Groups = PersonGroups.Load(groupsFile, groupsFile is null || folder is null ? null : SearchPaths.GroupsFile(folder),
                () => _file is not null && File.Exists(_file));
        }

        /// <summary>The groups the people are sorted into; the same in every photo folder, which keeps a copy. Change them through the library (<see cref="CreateGroup"/>), which keeps the names apart.</summary>
        public PersonGroups Groups { get; }

        /// <summary>The people of a photo folder and, from the shared file, the people of the other folders; the groups are the user's own.</summary>
        public static PeopleLibrary Load(string photoFolder) => Load(photoFolder, SearchPaths.SharedPeopleFile, SearchPaths.PeopleGroupsFile);

        /// <summary>
        /// The people of a photo folder; none if the folder has no people file or it can't be read.
        /// With a <paramref name="sharedFile"/> the people of the other folders are known as well, and the folder's own
        /// people are put into it if it does not have them yet.
        /// </summary>
        public static PeopleLibrary Load(string photoFolder, string? sharedFile, string? groupsFile = null)
        {
            var folder = SearchPaths.Normalize(photoFolder);
            var library = new PeopleLibrary(SearchPaths.PeopleFile(photoFolder), sharedFile, folder, groupsFile);
            try
            {
                if (File.Exists(library._file))
                    library.Read(File.ReadAllText(library._file!));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
            {
                library.LoadError = ex.Message;
            }
            library.JoinSharedPeople();
            return library;
        }

        /// <summary>Why the people file could not be read, if it could not.</summary>
        public string? LoadError { get; private set; }

        /// <summary>Why the shared file could not be read or written, if it could not. The people of this folder are safe in its own file all the same.</summary>
        public string? SharedError { get; private set; }

        /// <summary>The people of this folder and of the others, by their names.</summary>
        public IReadOnlyList<string> Names
        {
            get { lock (_gate) return [.. KnownNames().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)]; }
        }

        // The name of this folder's person wins where both have one
        private IEnumerable<string> KnownNames() => _people.Keys.Union(_others.Keys, StringComparer.OrdinalIgnoreCase);

        /// <summary>The name of a person or of a group as it was first typed, if the text is that name (any case). A person wins over a group of the same name.</summary>
        public bool TryFindName(string text, out string name)
        {
            lock (_gate)
            {
                foreach (var known in KnownNames())
                {
                    if (string.Equals(known, text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        name = known;
                        return true;
                    }
                }
            }
            return Groups.TryFind(text, out name);
        }

        /// <summary>Whether a name is a group (and no person has it).</summary>
        public bool IsGroup(string name)
        {
            lock (_gate)
                return !KnownNames().Contains(name.Trim(), StringComparer.OrdinalIgnoreCase) && Groups.TryFind(name, out _);
        }

        /// <summary>The names a search text can contain: the people, and the groups that no person has the name of (the caller holds the lock).</summary>
        private List<string> SearchableNames()
        {
            var names = KnownNames().ToList();
            names.AddRange(Groups.Names.Where(g => !names.Contains(g, StringComparer.OrdinalIgnoreCase)));
            return names;
        }

        // Words that join a name to the rest of a description ("Mom with a guitar", "on the beach with Mom and Dad") and are no use to the search
        private static readonly Regex EdgeLinks = new(
            @"^(?:(?:with|and|in|on|at|near|by|of)(?:\s+|$))+|(?:\s+(?:with|and|in|on|at|near|by|of))+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Takes the names of known people (and groups) out of a description: "Yurii playing guitar" gives the name Yurii and the words "playing guitar".
        /// A name counts as a whole word (also with 's: "Yurii's guitar"), in any case, anywhere in the text; the longest name goes first, so
        /// that "Grandma Olga" is not taken for "Olga". Each name is taken once. Words that only join the names to the rest ("with", "and", "on")
        /// are dropped from the ends of what is left.
        /// </summary>
        /// <param name="names">The names taken out, as they were first typed, in the order they stand in the text.</param>
        /// <returns>The text without the names: a description, or nothing. The text as it was if it has no name.</returns>
        public string TakeNames(string text, List<string> names)
        {
            List<(string Name, int At)> found = [];
            lock (_gate)
            {
                foreach (var known in SearchableNames().Where(n => n.Trim().Length > 0).OrderByDescending(n => n.Length))
                {
                    var name = Regex.Match(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(known.Trim())}(?:['’]s)?(?![\p{{L}}\p{{N}}])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (!name.Success)
                        continue;

                    found.Add((known, name.Index));
                    text = text.Remove(name.Index, name.Length).Insert(name.Index, new string(' ', name.Length)); // the places of the others stay
                }
            }

            if (found.Count == 0)
                return text;

            names.AddRange(found.OrderBy(f => f.At).Select(f => f.Name));
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return EdgeLinks.Replace(text, string.Empty).Trim();
        }

        /// <summary>
        /// The vectors of the faces the user has named <paramref name="name"/>, in this folder and in the others. For a group (a name that no person
        /// has) the faces of all its people: a face close to any of them is a face of the group.
        /// </summary>
        public IReadOnlyList<float[]> ExamplesOf(string name)
        {
            lock (_gate)
            {
                if (!KnownNames().Contains(name, StringComparer.OrdinalIgnoreCase) && Groups.TryFind(name, out var group))
                    return [.. Groups.MembersOf(group).SelectMany(ExamplesOfPerson)];

                return ExamplesOfPerson(name);
            }
        }

        // The caller holds the lock
        private List<float[]> ExamplesOfPerson(string name)
        {
            List<float[]> vectors = [];
            if (_people.TryGetValue(name, out var own))
                vectors.AddRange(own.Select(e => e.Embedding));
            if (_others.TryGetValue(name, out var others))
                vectors.AddRange(others);
            return vectors;
        }

        // ── groups ─────────────────────────────────────────────────────────

        /// <summary>
        /// Makes a group. A group takes no name that a person has (a search for that name would not know which is meant). Returns the name as the group has it.
        /// Throws <see cref="ArgumentException"/> with a message for the person if the name is empty or a person's, and <see cref="IOException"/> if the file can't be written.
        /// </summary>
        public string CreateGroup(string name)
        {
            lock (_gate)
            {
                ThrowIfPerson(name);
                return Groups.Create(name);
            }
        }

        /// <summary>Gives a group another name; the rules are those of <see cref="CreateGroup"/>.</summary>
        public void RenameGroup(string group, string newName)
        {
            lock (_gate)
            {
                ThrowIfPerson(newName);
                Groups.Rename(group, newName);
            }
        }

        /// <summary>Puts people into a group or takes them out of it (the group is made when people are put into one that does not exist). The rules are those of <see cref="CreateGroup"/>.</summary>
        public void SetGroupMembers(string group, IEnumerable<string> people, bool member)
        {
            lock (_gate)
            {
                if (member)
                    ThrowIfPerson(group);
                Groups.SetMembers(group, people, member);
            }
        }

        private void ThrowIfPerson(string name)
        {
            if (WhyNotAGroupName(name) is { } reason)
                throw new ArgumentException(reason);
        }

        /// <summary>
        /// The reason a group can not have this name, or null if it can: the name of a person would make "Mom" in a search mean two things.
        /// <paramref name="alsoTaken"/> is a name that is about to be a person's (a new person made together with the group).
        /// </summary>
        public string? WhyNotAGroupName(string name, string? alsoTaken = null)
        {
            name = name.Trim();
            if (name.Length == 0)
                return null; // nothing to name: the caller decides what an empty name means

            lock (_gate)
            {
                var person = KnownNames().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (person is null && alsoTaken is not null && string.Equals(alsoTaken.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    person = alsoTaken.Trim();

                return person is null ? null : string.Format(SearchLibStrings.GroupIsPersonFormat, person);
            }
        }

        /// <summary>
        /// The reason a face can not be given this name, or null if it can: the name of a group would make "Family" in a search mean two things.
        /// A person who has the name already is fine (the face is one more face of that person).
        /// </summary>
        public string? WhyNotAName(string name)
        {
            name = name.Trim();
            lock (_gate)
            {
                if (name.Length == 0 || KnownNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                    return null;

                return Groups.TryFind(name, out var group) ? string.Format(SearchLibStrings.PersonIsGroupFormat, group) : null;
            }
        }

        /// <summary>The name the user gave this face, if they did. Only faces named in this folder count: another folder may have a photo of the same name.</summary>
        public string? LabelOf(IndexedFace face)
        {
            lock (_gate)
            {
                foreach (var (name, examples) in _people)
                {
                    if (examples.Any(e => IsSameFace(e, face)))
                        return name;
                }
            }
            return null;
        }

        /// <summary>The person this face looks most like, if it looks enough like anyone.</summary>
        public (string Name, float Score)? Recognize(float[] embedding, float threshold = DefaultMatchThreshold)
        {
            lock (_gate)
            {
                string? best = null;
                var bestScore = threshold;
                foreach (var name in KnownNames())
                {
                    var score = BestScore(name, embedding);
                    if (score >= bestScore)
                    {
                        best = name;
                        bestScore = score;
                    }
                }
                return best is null ? null : (best, bestScore);
            }
        }

        private float BestScore(string name, float[] embedding)
        {
            var best = 0f;
            if (_people.TryGetValue(name, out var own))
            {
                foreach (var example in own)
                    best = Math.Max(best, VectorMath.Dot(example.Embedding, embedding));
            }
            if (_others.TryGetValue(name, out var others))
            {
                foreach (var example in others)
                    best = Math.Max(best, VectorMath.Dot(example, embedding));
            }
            return best;
        }

        /// <summary>
        /// Names a face. A face has one name: an earlier one is replaced. Throws if the file of the folder can't be written
        /// (the name stays until the app closes). The shared file is written too, but failing to do that is only reported in <see cref="SharedError"/>.
        /// </summary>
        public void Label(string name, IndexedFace face)
        {
            name = name.Trim();
            if (name.Length == 0)
                throw new ArgumentException("A person needs a name.", nameof(name));

            lock (_gate)
            {
                if (WhyNotAName(name) is { } reason)
                    throw new ArgumentException(reason);

                // Someone named in another folder keeps the spelling they were given there
                if (!_people.ContainsKey(name) && _others.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) is { } known)
                    name = known;

                RemoveExamplesOf(face);
                if (!_people.TryGetValue(name, out var list))
                    _people[name] = list = [];

                list.Add(new Example(face.Photo.RelativePath, face.X, face.Y, face.Width, face.Height, face.Embedding));
                Save();
                SaveShared();
                Groups.MakeFolderCopyIfMissing(); // the first person of a folder is what makes it keep the groups
            }
        }

        // ── renaming a person ──────────────────────────────────────────────

        /// <summary>Whether giving a person the name <paramref name="newName"/> would make two people one: somebody else has that name already.</summary>
        public bool WouldMerge(string oldName, string newName)
        {
            lock (_gate)
            {
                var other = KnownNames().FirstOrDefault(n => string.Equals(n, newName.Trim(), StringComparison.OrdinalIgnoreCase));
                return other is not null && !string.Equals(other, oldName.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Gives a person another name, everywhere: the faces named so in this folder, the same person in the other folders (the shared file is
        /// changed at once and each folder follows when it is opened), and the groups the person is in.
        /// A name that somebody else has makes the two one person (<see cref="WouldMerge"/>), with that name as they spell it; a name that differs
        /// from the old one only in case is another spelling. Returns the name the person has now.
        /// Throws <see cref="ArgumentException"/> with a sentence for the user if the name is empty, nobody has the old name, or the new one is a group's;
        /// <see cref="IOException"/> if the file of this folder can't be written (then nothing was renamed) or the groups can't be saved (then the person
        /// was renamed, the groups still have the old name). The shared file failing is only reported in <see cref="SharedError"/>.
        /// </summary>
        public string RenamePerson(string oldName, string newName)
        {
            oldName = oldName.Trim();
            newName = newName.Trim();
            if (newName.Length == 0)
                throw new ArgumentException(SearchLibStrings.PersonNeedsName);

            lock (_gate)
            {
                var from = KnownNames().FirstOrDefault(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException(string.Format(SearchLibStrings.NoSuchPersonFormat, oldName));
                if (WhyNotAName(newName) is { } reason)
                    throw new ArgumentException(reason);

                var other = KnownNames().FirstOrDefault(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase));
                var to = other is not null && !string.Equals(other, from, StringComparison.OrdinalIgnoreCase) ? other : newName;
                if (string.Equals(from, to, StringComparison.Ordinal))
                    return to;

                if (_people.ContainsKey(from))
                {
                    var before = _people.ToDictionary(p => p.Key, p => new List<Example>(p.Value), StringComparer.OrdinalIgnoreCase);
                    MoveKey(_people, from, to);
                    try
                    {
                        Save();
                    }
                    catch
                    {
                        _people.Clear();
                        foreach (var (name, examples) in before)
                            _people[name] = examples;
                        throw;
                    }
                }

                MoveKey(_others, from, to);
                RenameInShared(from, to);
                Groups.RenameMember(from, to);
                return to;
            }
        }

        // Moves what is kept under one name to another; what is under both is put together. True if there was something under the old name.
        private static bool MoveKey<T>(Dictionary<string, List<T>> map, string from, string to)
        {
            if (!map.TryGetValue(from, out var list))
                return false;

            map.Remove(from);
            if (map.TryGetValue(to, out var target))
                target.AddRange(list);
            else
                map[to] = list;
            return true;
        }

        // The shared file after a rename: the person has the new name in the slices of the other folders (their own files follow when they are
        // opened), the rename is written down, and this folder's slice is put in, stamped later than the rename (the caller holds the lock).
        private void RenameInShared(string from, string to)
        {
            if (_sharedFile is null)
                return;

            SharedError = null;
            try
            {
                var shared = ReadShared();
                foreach (var folder in shared.Folders.Where(f => !SameFolder(f.Path, _folder)))
                {
                    var person = folder.People.FirstOrDefault(p => string.Equals(p.Name, from, StringComparison.OrdinalIgnoreCase));
                    if (person is null)
                        continue;

                    var target = folder.People.FirstOrDefault(p => !ReferenceEquals(p, person) && string.Equals(p.Name, to, StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        person.Name = to;
                    }
                    else
                    {
                        target.Faces.AddRange(person.Faces);
                        folder.People.Remove(person);
                    }
                }

                shared.Renames.Add(new RenameModel { From = from, To = to, At = DateTime.UtcNow });
                if (shared.Renames.Count > MaxRenames)
                    shared.Renames.RemoveRange(0, shared.Renames.Count - MaxRenames);

                WriteShared(shared);
                TakeOthersFrom(shared);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
            {
                SharedError = ex.Message;
            }
        }

        // A folder that has not seen a rename (it last agreed with the shared file before it) makes it in its own file: the people it has under the
        // old name get the new one. A person named later, after the folder agreed with the shared file, is somebody else and is left alone.
        // A folder that has no slice yet has seen nothing. Returns true if the people of the folder changed (the caller holds the lock).
        private bool ApplyPendingRenames(SharedFileModel shared)
        {
            if (LoadError is not null || shared.Renames.Count == 0)
                return false; // a file of the folder that could not be read is not touched

            var synced = shared.Folders.FirstOrDefault(f => SameFolder(f.Path, _folder))?.Synced ?? DateTime.MinValue;
            var changed = false;
            foreach (var rename in shared.Renames.Where(r => r.At > synced).OrderBy(r => r.At))
                changed |= MoveKey(_people, rename.From, rename.To);

            if (changed)
                Save();
            return changed;
        }

        /// <summary>Takes the name off a face. Returns false if it had none.</summary>
        public bool Unlabel(IndexedFace face)
        {
            lock (_gate)
            {
                if (!RemoveExamplesOf(face))
                    return false;

                Save();
                SaveShared();
                return true;
            }
        }

        private bool RemoveExamplesOf(IndexedFace face)
        {
            var removed = false;
            foreach (var name in _people.Keys.ToList())
            {
                removed |= _people[name].RemoveAll(e => IsSameFace(e, face)) > 0;
                if (_people[name].Count == 0)
                    _people.Remove(name);
            }
            return removed;
        }

        private static bool IsSameFace(Example example, IndexedFace face) =>
            IsSamePlace(example.Photo, example.X, example.Y, example.Width, example.Height, face.Photo.RelativePath, face.X, face.Y, face.Width, face.Height);

        /// <summary>Whether two boxes in photos of the same name are the same face (they overlap enough).</summary>
        private static bool IsSamePlace(string photoA, float xA, float yA, float widthA, float heightA, string photoB, float xB, float yB, float widthB, float heightB)
        {
            if (!string.Equals(photoA, photoB, StringComparison.OrdinalIgnoreCase))
                return false;

            var width = Math.Min(xA + widthA, xB + widthB) - Math.Max(xA, xB);
            var height = Math.Min(yA + heightA, yB + heightB) - Math.Max(yA, yB);
            if (width <= 0 || height <= 0)
                return false;

            var intersection = width * height;
            var union = widthA * heightA + widthB * heightB - intersection;
            return intersection / union >= SameFaceOverlap;
        }

        // ── the file ───────────────────────────────────────────────────────

        private sealed class FileModel
        {
            public int Version { get; set; } = 1;
            public List<PersonModel> People { get; set; } = [];
        }

        private sealed class PersonModel
        {
            public string Name { get; set; } = string.Empty;
            public List<ExampleModel> Faces { get; set; } = [];
        }

        private sealed class ExampleModel
        {
            public string Photo { get; set; } = string.Empty;
            public float[] Box { get; set; } = [];
            public string Embedding { get; set; } = string.Empty;
        }

        internal void Read(string json)
        {
            var model = JsonSerializer.Deserialize<FileModel>(json, JsonOptions) ?? new FileModel();
            lock (_gate)
            {
                _people.Clear();
                foreach (var person in model.People.Where(p => p.Name.Length > 0))
                {
                    _people[person.Name] =
                    [
                        .. person.Faces
                            .Where(f => f.Box.Length == 4 && f.Embedding.Length > 0)
                            .Select(f => new Example(f.Photo, f.Box[0], f.Box[1], f.Box[2], f.Box[3],
                                VectorMath.ToFloats(Convert.FromBase64String(f.Embedding)))),
                    ];
                }
            }
        }

        internal string ToJson()
        {
            lock (_gate)
                return JsonSerializer.Serialize(new FileModel { People = OwnPeople() }, JsonOptions);
        }

        /// <summary>The people of this folder as they are written, in a fixed order (the caller holds the lock).</summary>
        private List<PersonModel> OwnPeople() =>
        [
            .. _people.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase).Select(p => new PersonModel
            {
                Name = p.Key,
                Faces = [.. p.Value.Select(e => new ExampleModel
                {
                    Photo = e.Photo,
                    Box = [e.X, e.Y, e.Width, e.Height],
                    Embedding = Convert.ToBase64String(VectorMath.ToBytes(e.Embedding)),
                })],
            }),
        ];

        private void Save()
        {
            if (_file is null)
                return;

            WriteAtomically(_file, ToJson());
        }

        // Written next to the file first, so that a failure half way never leaves a broken file
        private static void WriteAtomically(string file, string json) => AtomicFile.Write(file, json);

        // ── the shared file ────────────────────────────────────────────────

        // The people of every folder, folder by folder. Each folder owns its slice and replaces it as a whole, so a person
        // that was un-named or a folder that was emptied disappears from the file too.
        private sealed class SharedFileModel
        {
            public int Version { get; set; } = 1;

            /// <summary>The version of the face pipeline that made the vectors (<see cref="IndexingJob.FaceVersion"/>): vectors of another one can't be compared.</summary>
            public int FaceVersion { get; set; } = IndexingJob.FaceVersion;

            public List<FolderModel> Folders { get; set; } = [];

            /// <summary>The people who were given another name, oldest first. A folder that has not seen a rename yet makes it when it is opened (see <see cref="PeopleLibrary.RenamePerson"/>).</summary>
            public List<RenameModel> Renames { get; set; } = [];
        }

        private sealed class FolderModel
        {
            public string Path { get; set; } = string.Empty;

            /// <summary>When the folder last put its people here (UTC): the renames since then are not in the file of the folder yet. Missing: the folder has seen none.</summary>
            public DateTime? Synced { get; set; }

            public List<PersonModel> People { get; set; } = [];
        }

        private sealed class RenameModel
        {
            public string From { get; set; } = string.Empty;
            public string To { get; set; } = string.Empty;
            public DateTime At { get; set; }
        }

        /// <summary>How many renames the shared file remembers: a folder that was not opened while more renames than this were made has missed the oldest ones.</summary>
        private const int MaxRenames = 100;

        private static bool SameFolder(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The shared file as it is now; an empty one if there is none, if it was made with other face models, or if it is
        /// damaged (it is only a copy, so a damaged one is set aside as .bad and started over).
        /// </summary>
        private SharedFileModel ReadShared()
        {
            if (!File.Exists(_sharedFile))
                return new SharedFileModel();

            try
            {
                var model = JsonSerializer.Deserialize<SharedFileModel>(File.ReadAllText(_sharedFile!), JsonOptions);
                if (model is not null && model.FaceVersion == IndexingJob.FaceVersion)
                    return model;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                File.Move(_sharedFile!, _sharedFile + ".bad", overwrite: true);
                SharedError = string.Format(SearchLibStrings.SharedPeopleDamagedFormat, ex.Message, Path.GetFileName(_sharedFile));
            }
            return new SharedFileModel();
        }

        /// <summary>Learns the people of the other folders from the shared file (the caller holds the lock).</summary>
        private void TakeOthersFrom(SharedFileModel shared)
        {
            var others = new Dictionary<string, List<float[]>>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in shared.Folders.Where(f => !SameFolder(f.Path, _folder)))
            {
                foreach (var person in folder.People.Where(p => p.Name.Length > 0))
                {
                    if (!others.TryGetValue(person.Name, out var list))
                        others[person.Name] = list = [];

                    list.AddRange(person.Faces
                        .Where(f => f.Embedding.Length > 0)
                        .Select(f => VectorMath.ToFloats(Convert.FromBase64String(f.Embedding))));
                }
            }
            _others = others;
        }

        /// <summary>Whether the shared file already holds exactly the people of this folder.</summary>
        private bool HoldsMyPeople(SharedFileModel shared)
        {
            var mine = shared.Folders.Where(f => SameFolder(f.Path, _folder)).ToList();
            var own = OwnPeople();
            if (own.Count == 0)
                return mine.Count == 0;

            return mine.Count == 1 && JsonSerializer.Serialize(mine[0].People, JsonOptions) == JsonSerializer.Serialize(own, JsonOptions);
        }

        private void WriteShared(SharedFileModel shared)
        {
            shared.Folders.RemoveAll(f => SameFolder(f.Path, _folder));
            var own = OwnPeople();
            if (own.Count > 0)
                shared.Folders.Add(new FolderModel { Path = _folder, People = own, Synced = DateTime.UtcNow });

            shared.Folders.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(Path.GetDirectoryName(_sharedFile)!);
            WriteAtomically(_sharedFile!, JsonSerializer.Serialize(shared, JsonOptions));
        }

        /// <summary>
        /// When a folder is opened: learns the people of the other folders, and puts the people of this one into the shared
        /// file if it does not have them as they are. The file of the folder decides what they are, but not while it could not be read.
        /// </summary>
        private void JoinSharedPeople()
        {
            if (_sharedFile is null)
                return;

            lock (_gate)
            {
                try
                {
                    var shared = ReadShared();
                    var renamed = ApplyPendingRenames(shared);
                    TakeOthersFrom(shared);
                    if (LoadError is null && (renamed || !HoldsMyPeople(shared)))
                        WriteShared(shared);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
                {
                    SharedError = ex.Message;
                }
            }
        }

        /// <summary>
        /// After a name was given or taken off: the shared file gets the people of this folder. The file is read again first,
        /// another copy of the app may have added people to it meanwhile (and they are known here from now on).
        /// </summary>
        private void SaveShared()
        {
            if (_sharedFile is null)
                return;

            SharedError = null;
            try
            {
                var shared = ReadShared();
                WriteShared(shared);
                TakeOthersFrom(shared);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
            {
                SharedError = ex.Message;
            }
        }
    }
}
