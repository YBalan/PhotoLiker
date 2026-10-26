using System.Text.Json;

namespace PhotoLiker.Search
{
    /// <summary>
    /// The people the user has named in a photo folder. Naming a face keeps that face's vector and where it is, as an
    /// example of how the person looks; other faces are recognized by being close to one of the examples.
    /// The examples are stored as plain JSON in the photo folder (<see cref="SearchPaths.PeopleFileName"/>), not in the
    /// index: the index can always be rebuilt, the naming took effort and travels with the photos.
    /// </summary>
    public sealed class PeopleLibrary
    {
        /// <summary>How alike (cosine of the face vectors) two faces have to be to count as the same person.</summary>
        public const float DefaultMatchThreshold = 0.40f;

        /// <summary>Two boxes in the same photo that overlap this much (intersection over union) are the same face.</summary>
        private const float SameFaceOverlap = 0.5f;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private sealed record Example(string Photo, float X, float Y, float Width, float Height, float[] Embedding);

        private readonly object _gate = new();
        private readonly Dictionary<string, List<Example>> _people = new(StringComparer.OrdinalIgnoreCase);
        private readonly string? _file;

        public PeopleLibrary(string? file = null)
        {
            _file = file;
        }

        /// <summary>The people of a photo folder; none if the folder has no people file or it can't be read.</summary>
        public static PeopleLibrary Load(string photoFolder)
        {
            var library = new PeopleLibrary(SearchPaths.PeopleFile(photoFolder));
            try
            {
                if (File.Exists(library._file))
                    library.Read(File.ReadAllText(library._file!));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
            {
                library.LoadError = ex.Message;
            }
            return library;
        }

        /// <summary>Why the people file could not be read, if it could not.</summary>
        public string? LoadError { get; private set; }

        public IReadOnlyList<string> Names
        {
            get { lock (_gate) return [.. _people.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)]; }
        }

        /// <summary>The name of a person as it was first typed, if the text is the name of a person (any case).</summary>
        public bool TryFindName(string text, out string name)
        {
            lock (_gate)
            {
                foreach (var known in _people.Keys)
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

        /// <summary>The vectors of the faces the user has named <paramref name="name"/>.</summary>
        public IReadOnlyList<float[]> ExamplesOf(string name)
        {
            lock (_gate)
                return _people.TryGetValue(name, out var list) ? [.. list.Select(e => e.Embedding)] : [];
        }

        /// <summary>The name the user gave this face, if they did.</summary>
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
                foreach (var (name, examples) in _people)
                {
                    var score = examples.Count == 0 ? 0 : examples.Max(e => VectorMath.Dot(e.Embedding, embedding));
                    if (score >= bestScore)
                    {
                        best = name;
                        bestScore = score;
                    }
                }
                return best is null ? null : (best, bestScore);
            }
        }

        /// <summary>Names a face. A face has one name: an earlier one is replaced. Throws if the file can't be written (the name stays until the app closes).</summary>
        public void Label(string name, IndexedFace face)
        {
            name = name.Trim();
            if (name.Length == 0)
                throw new ArgumentException("A person needs a name.", nameof(name));

            lock (_gate)
            {
                RemoveExamplesOf(face);
                if (!_people.TryGetValue(name, out var list))
                    _people[name] = list = [];

                list.Add(new Example(face.Photo.RelativePath, face.X, face.Y, face.Width, face.Height, face.Embedding));
                Save();
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
            {
                var model = new FileModel
                {
                    People =
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
                    ],
                };
                return JsonSerializer.Serialize(model, JsonOptions);
            }
        }

        private void Save()
        {
            if (_file is null)
                return;

            // Written next to the file first, so that a failure half way never leaves a broken people file
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, ToJson());
            File.Move(temporary, _file, overwrite: true);
        }
    }
}
