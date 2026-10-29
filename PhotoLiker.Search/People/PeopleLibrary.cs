using System.Text.Json;

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
    /// </remarks>
    public sealed class PeopleLibrary
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
        public PeopleLibrary(string? file = null, string? sharedFile = null, string? folder = null)
        {
            if (sharedFile is not null && string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("A shared people file needs to know which folder this is.", nameof(folder));

            _file = file;
            _sharedFile = sharedFile;
            _folder = folder is null ? string.Empty : Path.TrimEndingDirectorySeparator(folder);
        }

        /// <summary>The people of a photo folder and, from the shared file, the people of the other folders.</summary>
        public static PeopleLibrary Load(string photoFolder) => Load(photoFolder, SearchPaths.SharedPeopleFile);

        /// <summary>
        /// The people of a photo folder; none if the folder has no people file or it can't be read.
        /// With a <paramref name="sharedFile"/> the people of the other folders are known as well, and the folder's own
        /// people are put into it if it does not have them yet.
        /// </summary>
        public static PeopleLibrary Load(string photoFolder, string? sharedFile)
        {
            var folder = SearchPaths.Normalize(photoFolder);
            var library = new PeopleLibrary(SearchPaths.PeopleFile(photoFolder), sharedFile, folder);
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

        /// <summary>The name of a person as it was first typed, if the text is the name of a person (any case).</summary>
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
            name = string.Empty;
            return false;
        }

        /// <summary>The vectors of the faces the user has named <paramref name="name"/>, in this folder and in the others.</summary>
        public IReadOnlyList<float[]> ExamplesOf(string name)
        {
            lock (_gate)
            {
                List<float[]> vectors = [];
                if (_people.TryGetValue(name, out var own))
                    vectors.AddRange(own.Select(e => e.Embedding));
                if (_others.TryGetValue(name, out var others))
                    vectors.AddRange(others);
                return vectors;
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
                // Someone named in another folder keeps the spelling they were given there
                if (!_people.ContainsKey(name) && _others.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) is { } known)
                    name = known;

                RemoveExamplesOf(face);
                if (!_people.TryGetValue(name, out var list))
                    _people[name] = list = [];

                list.Add(new Example(face.Photo.RelativePath, face.X, face.Y, face.Width, face.Height, face.Embedding));
                Save();
                SaveShared();
            }
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

        private static bool IsSameFace(Example example, IndexedFace face)
        {
            if (!string.Equals(example.Photo, face.Photo.RelativePath, StringComparison.OrdinalIgnoreCase))
                return false;

            var width = Math.Min(example.X + example.Width, face.X + face.Width) - Math.Max(example.X, face.X);
            var height = Math.Min(example.Y + example.Height, face.Y + face.Height) - Math.Max(example.Y, face.Y);
            if (width <= 0 || height <= 0)
                return false;

            var intersection = width * height;
            var union = example.Width * example.Height + face.Width * face.Height - intersection;
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
        private static void WriteAtomically(string file, string json)
        {
            var temporary = file + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, file, overwrite: true);
            }
            catch
            {
                try { File.Delete(temporary); } catch { /* best effort */ }
                throw;
            }
        }

        // ── the shared file ────────────────────────────────────────────────

        // The people of every folder, folder by folder. Each folder owns its slice and replaces it as a whole, so a person
        // that was un-named or a folder that was emptied disappears from the file too.
        private sealed class SharedFileModel
        {
            public int Version { get; set; } = 1;

            /// <summary>The version of the face pipeline that made the vectors (<see cref="IndexingJob.FaceVersion"/>): vectors of another one can't be compared.</summary>
            public int FaceVersion { get; set; } = IndexingJob.FaceVersion;

            public List<FolderModel> Folders { get; set; } = [];
        }

        private sealed class FolderModel
        {
            public string Path { get; set; } = string.Empty;
            public List<PersonModel> People { get; set; } = [];
        }

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
                SharedError = $"The shared people file was damaged ({ex.Message}). It was set aside as {Path.GetFileName(_sharedFile)}.bad and started again.";
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
                shared.Folders.Add(new FolderModel { Path = _folder, People = own });

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
                    TakeOthersFrom(shared);
                    if (LoadError is null && !HoldsMyPeople(shared))
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
