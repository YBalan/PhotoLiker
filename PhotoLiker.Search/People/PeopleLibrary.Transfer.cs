using System.Text.Json;

namespace PhotoLiker.Search
{
    /// <summary>What a people file (<see cref="PeopleLibrary.Export"/>) holds, as <see cref="PeopleLibrary.Inspect"/> and an export tell it.</summary>
    /// <param name="People">How many different people (names, in any case) have at least one face in the file.</param>
    /// <param name="Faces">How many named faces.</param>
    /// <param name="Folders">From how many photo folders the faces come.</param>
    /// <param name="Groups">How many groups.</param>
    /// <param name="ExportedAt">When it was made (UTC), if it says.</param>
    /// <param name="Folder">The photo folder it was made from, if it says.</param>
    /// <param name="FacesUsable">False when the faces were found by other face models than this version's: their vectors can't be compared with ours, only the groups can be used.</param>
    public sealed record PeopleFileInfo(int People, int Faces, int Folders, int Groups, DateTime? ExportedAt, string? Folder, bool FacesUsable);

    /// <summary>What <see cref="PeopleLibrary.Import"/> did.</summary>
    /// <param name="NewPeople">People nobody here had a name for until now.</param>
    /// <param name="FacesAdded">Faces that were put among the examples of a person.</param>
    /// <param name="FacesKnown">Faces left out because this computer has that face named already (under this name or another one: what is named here stays).</param>
    /// <param name="FacesUnusable">Faces left out because they could not be used: damaged, or found by other face models, or of a person whose name a group has here (see <paramref name="Refused"/>), or from a folder that can't be kept without the shared file.</param>
    /// <param name="NewGroups">Groups that did not exist here.</param>
    /// <param name="GroupMembersAdded">People put into groups (in groups that were there and in new ones).</param>
    /// <param name="Refused">A sentence for each person or group that was left out because its name is taken by the other kind (a group called like a person, a person called like a group).</param>
    public sealed record PeopleImportResult(int NewPeople, int FacesAdded, int FacesKnown, int FacesUnusable, int NewGroups, int GroupMembersAdded, IReadOnlyList<string> Refused);

    /// <summary>
    /// Moving the people and the groups to another computer, or into a backup, through one file: <see cref="Export"/> writes it, <see cref="Import"/> adds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file holds every person this library knows with the faces named (the faces of this photo folder and, from the shared file, of the other folders,
    /// each folder as a part of its own) and every group with its people. It is plain JSON like the people files of the folders, with the vectors as
    /// base64.
    /// </para>
    /// <para>
    /// An import only adds, it never replaces and never removes: a face that is named here already stays as it is, a group that is there gets the
    /// people it did not have, a group that is not there is made. So importing the same file twice changes nothing the second time, and a file from a
    /// computer that has diverged from this one can be taken in without losing what was done here. The faces of the part of the file that comes from
    /// this very folder (the same path) are added to the people of this folder; the parts of other folders are kept in the shared file like the people
    /// of any other folder, as examples of how the person looks. A folder that is opened later, and has a people file of its own, puts its own people
    /// in place of the part kept for it, as it always does.
    /// </para>
    /// </remarks>
    public sealed partial class PeopleLibrary
    {
        /// <summary>The ending of a people file, without the dot.</summary>
        public const string PeopleFileExtension = "plpeople";

        private const string BundleFormat = "PhotoLiker people";
        private const int BundleVersion = 1;

        private sealed class BundleModel
        {
            public string Format { get; set; } = string.Empty;
            public int Version { get; set; }
            public int FaceVersion { get; set; }
            public DateTime? ExportedAt { get; set; }
            public string? Folder { get; set; }
            public List<FolderModel>? Folders { get; set; } = [];
            public List<BundleGroupModel>? Groups { get; set; } = [];
        }

        private sealed class BundleGroupModel
        {
            public string Name { get; set; } = string.Empty;
            public List<string>? Members { get; set; } = [];
        }

        // ── looking at a file ──────────────────────────────────────────────

        /// <summary>
        /// What a people file holds, to tell the person before it is imported.
        /// Throws <see cref="InvalidDataException"/> with a sentence for the person if the file is not a people file (or is from a newer version),
        /// <see cref="IOException"/> if it can't be read.
        /// </summary>
        public static PeopleFileInfo Inspect(string file) => Describe(ReadBundle(file));

        private static BundleModel ReadBundle(string file)
        {
            BundleModel? bundle;
            try
            {
                bundle = JsonSerializer.Deserialize<BundleModel>(File.ReadAllText(file), JsonOptions);
            }
            catch (JsonException)
            {
                bundle = null;
            }

            if (bundle is null || bundle.Format != BundleFormat)
                throw new InvalidDataException(SearchLibStrings.NotPeopleFile);
            if (bundle.Version > BundleVersion)
                throw new InvalidDataException(string.Format(SearchLibStrings.PeopleFileNewerFormat, bundle.Version));
            return bundle;
        }

        private static PeopleFileInfo Describe(BundleModel bundle)
        {
            HashSet<string> people = new(StringComparer.OrdinalIgnoreCase);
            var faces = 0;
            var folders = 0;
            foreach (var folder in bundle.Folders ?? [])
            {
                var held = 0;
                foreach (var person in folder.People ?? [])
                {
                    var count = (person.Faces ?? []).Count(face => TryReadFace(face, out _));
                    if (count == 0 || person.Name is not { Length: > 0 })
                        continue;

                    people.Add(person.Name.Trim());
                    held += count;
                }
                faces += held;
                folders += held > 0 ? 1 : 0;
            }

            var groups = (bundle.Groups ?? []).Count(g => !string.IsNullOrWhiteSpace(g.Name));
            return new PeopleFileInfo(people.Count, faces, folders, groups, bundle.ExportedAt, bundle.Folder, bundle.FaceVersion == IndexingJob.FaceVersion);
        }

        // A face the file describes completely: where it is and its vector
        private static bool TryReadFace(ExampleModel face, out float[] embedding)
        {
            embedding = [];
            if (face.Box is not { Length: 4 } || string.IsNullOrEmpty(face.Embedding) || string.IsNullOrEmpty(face.Photo))
                return false;

            try
            {
                embedding = VectorMath.ToFloats(Convert.FromBase64String(face.Embedding));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return false;
            }
            return embedding.Length > 0;
        }

        // ── export ─────────────────────────────────────────────────────────

        /// <summary>
        /// Writes every person this library knows, with the faces named, and every group to a file (see <see cref="Inspect"/> for what it says about itself).
        /// The people of the other folders are taken from the shared file as it is now. Throws <see cref="IOException"/> if the file can't be written or
        /// the shared file can't be read (then nothing is half written: the file is made whole or not at all).
        /// </summary>
        public PeopleFileInfo Export(string file)
        {
            BundleModel bundle;
            lock (_gate)
            {
                List<FolderModel> folders = [];
                var own = OwnPeople();
                if (own.Count > 0)
                    folders.Add(new FolderModel { Path = _folder, People = own });

                if (_sharedFile is not null)
                {
                    folders.AddRange(ReadShared().Folders
                        .Where(f => !SameFolder(f.Path, _folder) && f.People.Count > 0)
                        .Select(f => new FolderModel { Path = f.Path, People = f.People })); // when each folder last agreed with the shared file means nothing elsewhere
                }
                folders.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

                bundle = new BundleModel
                {
                    Format = BundleFormat,
                    Version = BundleVersion,
                    FaceVersion = IndexingJob.FaceVersion,
                    ExportedAt = DateTime.UtcNow,
                    Folder = _folder.Length > 0 ? _folder : null,
                    Folders = folders,
                    Groups = [.. Groups.Names.Select(g => new BundleGroupModel { Name = g, Members = [.. Groups.MembersOf(g)] })],
                };
            }

            AtomicFile.Write(file, JsonSerializer.Serialize(bundle, JsonOptions));
            return Describe(bundle);
        }

        // ── import ─────────────────────────────────────────────────────────

        /// <summary>
        /// Adds the people and the groups of a people file to the ones this library has (see the remarks of the class: nothing is replaced or removed).
        /// Throws <see cref="InvalidDataException"/> with a sentence for the person if the file is not a people file, <see cref="IOException"/> if it can't
        /// be read, or if what has to be written can't be: the people file of this folder (then nothing was added, the file of this folder is also
        /// refused while it can't be read itself, so that it is not written over) or the groups (then the people were added, the groups were not, or
        /// only some). The shared file failing is only reported in <see cref="SharedError"/>.
        /// </summary>
        public PeopleImportResult Import(string file)
        {
            var bundle = ReadBundle(file);
            lock (_gate)
            {
                var tally = new ImportTally();
                if (bundle.FaceVersion == IndexingJob.FaceVersion)
                    ImportPeople(bundle, tally);
                else
                    tally.Unusable += Describe(bundle).Faces;

                ImportGroups(bundle, tally);
                return new PeopleImportResult(tally.NewPeople, tally.Added, tally.Known, tally.Unusable, tally.NewGroups, tally.GroupMembers, tally.Refused);
            }
        }

        private sealed class ImportTally
        {
            public int NewPeople, Added, Known, Unusable, NewGroups, GroupMembers;
            public List<string> Refused { get; } = [];

            /// <summary>Every name the library has (and the ones this import adds), by its name in any case: the spelling it was first given.</summary>
            public Dictionary<string, string> Spellings { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed record Place(string Photo, float X, float Y, float Width, float Height);

        private static bool IsSamePlace(Place a, Place b) => IsSamePlace(a.Photo, a.X, a.Y, a.Width, a.Height, b.Photo, b.X, b.Y, b.Width, b.Height);

        private static Place PlaceOf(ExampleModel face) => new(face.Photo, face.Box[0], face.Box[1], face.Box[2], face.Box[3]);

        private static Place PlaceOf(Example example) => new(example.Photo, example.X, example.Y, example.Width, example.Height);

        /// <summary>The faces that are named already, by photo, to tell quickly whether a face of the file is one of them.</summary>
        private sealed class PlaceIndex
        {
            private readonly Dictionary<string, List<Place>> _byPhoto = new(StringComparer.OrdinalIgnoreCase);

            public void Add(Place place)
            {
                if (!_byPhoto.TryGetValue(place.Photo, out var list))
                    _byPhoto[place.Photo] = list = [];
                list.Add(place);
            }

            public bool Contains(Place place) => _byPhoto.TryGetValue(place.Photo, out var list) && list.Any(other => IsSamePlace(other, place));
        }

        // The caller holds the lock
        private void ImportPeople(BundleModel bundle, ImportTally tally)
        {
            var slices = (bundle.Folders ?? []).Where(f => f.People is { Count: > 0 } && !string.IsNullOrWhiteSpace(f.Path)).ToList();
            var ownSlices = slices.Where(f => SameFolder(f.Path, _folder)).ToList();
            var otherSlices = slices.Except(ownSlices).ToList();
            if (slices.Count > 0 && LoadError is not null)
                throw new IOException(string.Format(SearchLibStrings.PeopleFileUnreadableFormat, LoadError)); // writing now would put the few over the many

            // What the other folders have now, so that a person keeps the spelling they were given anywhere
            var shared = _sharedFile is null ? null : ReadShared();
            if (shared is not null)
                TakeOthersFrom(shared);
            foreach (var known in KnownNames())
                tally.Spellings[known] = known;

            var ownBefore = _people.ToDictionary(p => p.Key, p => new List<Example>(p.Value), StringComparer.OrdinalIgnoreCase);
            var ownChanged = false;
            var sharedChanged = false;

            // Faces of this folder: every place named here already, under any name, stays as it is
            var named = new PlaceIndex();
            foreach (var example in _people.Values.SelectMany(list => list))
                named.Add(PlaceOf(example));
            List<(string Name, Example Face)> toAdd = [];
            foreach (var person in ownSlices.SelectMany(s => s.People))
            {
                foreach (var face in person.Faces ?? [])
                {
                    if (!TryReadFace(face, out var embedding))
                    {
                        tally.Unusable++;
                        continue;
                    }

                    var place = PlaceOf(face);
                    if (named.Contains(place))
                    {
                        tally.Known++;
                        continue;
                    }

                    if (TakeName(person.Name, tally) is not { } name)
                    {
                        tally.Unusable++;
                        continue;
                    }
                    named.Add(place);
                    toAdd.Add((name, new Example(face.Photo, place.X, place.Y, place.Width, place.Height, embedding)));
                }
            }

            foreach (var (name, face) in toAdd)
            {
                if (!_people.TryGetValue(name, out var list))
                    _people[name] = list = [];
                list.Add(face);
                tally.Added++;
                ownChanged = true;
            }

            // Faces of the other folders go into the shared file, each into the part of its folder
            if (otherSlices.Count > 0 && shared is null)
            {
                tally.Unusable += otherSlices.Sum(s => s.People.Sum(p => (p.Faces ?? []).Count));
            }
            else if (shared is not null)
            {
                foreach (var slice in otherSlices)
                    sharedChanged |= ImportSlice(slice, shared, tally);
            }

            if (ownChanged)
            {
                try
                {
                    Save();
                }
                catch
                {
                    _people.Clear();
                    foreach (var (name, examples) in ownBefore)
                        _people[name] = examples;
                    throw; // nothing was added: the file of the folder is what the people are
                }
            }

            if ((ownChanged || sharedChanged) && shared is not null)
            {
                SharedError = null;
                try
                {
                    WriteShared(shared);
                    TakeOthersFrom(shared);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
                {
                    SharedError = ex.Message;
                }
            }

            if (ownChanged)
                Groups.MakeFolderCopyIfMissing(); // the first person of a folder is what makes it keep the groups
        }

        // The faces of another folder go into the part of the shared file that belongs to it (made if there is none). True if any was added.
        private bool ImportSlice(FolderModel slice, SharedFileModel shared, ImportTally tally)
        {
            var target = shared.Folders.FirstOrDefault(f => SameFolder(f.Path, slice.Path));
            var named = new PlaceIndex();
            foreach (var example in (target?.People ?? []).SelectMany(p => p.Faces).Where(f => f.Box is { Length: 4 } && f.Photo.Length > 0))
                named.Add(PlaceOf(example));
            List<(string Name, ExampleModel Face)> toAdd = [];
            foreach (var person in slice.People)
            {
                foreach (var face in person.Faces ?? [])
                {
                    if (!TryReadFace(face, out _))
                    {
                        tally.Unusable++;
                        continue;
                    }

                    var place = PlaceOf(face);
                    if (named.Contains(place))
                    {
                        tally.Known++;
                        continue;
                    }

                    if (TakeName(person.Name, tally) is not { } name)
                    {
                        tally.Unusable++;
                        continue;
                    }
                    named.Add(place);
                    toAdd.Add((name, new ExampleModel { Photo = face.Photo, Box = [.. face.Box], Embedding = face.Embedding }));
                }
            }

            if (toAdd.Count == 0)
                return false;

            // Not synced by a rename log it has never seen: the names in the file are taken as they are
            target ??= AddFolder(shared, slice.Path);
            foreach (var (name, face) in toAdd)
            {
                var person = target.People.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (person is null)
                    target.People.Add(person = new PersonModel { Name = name });
                person.Faces.Add(face);
                tally.Added++;
            }
            return true;
        }

        private static FolderModel AddFolder(SharedFileModel shared, string path)
        {
            var folder = new FolderModel { Path = Path.TrimEndingDirectorySeparator(path), Synced = DateTime.UtcNow };
            shared.Folders.Add(folder);
            return folder;
        }

        // The name a face from the file is kept under: the spelling the library knows the person by, or the file's. Null when a group has the name.
        // A person who is new is counted when the first of their faces is taken.
        private string? TakeName(string? name, ImportTally tally)
        {
            name = name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                return null;

            if (tally.Spellings.TryGetValue(name, out var spelling))
                return spelling;

            if (WhyNotAName(name) is { } reason)
            {
                if (!tally.Refused.Contains(reason))
                    tally.Refused.Add(reason);
                return null;
            }

            tally.Spellings[name] = name;
            tally.NewPeople++;
            return name;
        }

        // The caller holds the lock. The people are in already, so a group can't take the name of one of them.
        private void ImportGroups(BundleModel bundle, ImportTally tally)
        {
            foreach (var group in (bundle.Groups ?? []).Where(g => !string.IsNullOrWhiteSpace(g.Name)))
            {
                var name = group.Name.Trim();
                if (WhyNotAGroupName(name) is { } reason)
                {
                    if (!tally.Refused.Contains(reason))
                        tally.Refused.Add(reason);
                    continue;
                }

                var members = (group.Members ?? [])
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => tally.Spellings.TryGetValue(m.Trim(), out var spelling) ? spelling : m.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var exists = Groups.TryFind(name, out var existing);
                var missing = exists ? members.Where(m => !Groups.IsMember(existing, m)).ToList() : members;
                if (missing.Count > 0)
                    SetGroupMembers(exists ? existing : name, missing, member: true);
                else if (!exists)
                    CreateGroup(name);

                tally.NewGroups += exists ? 0 : 1;
                tally.GroupMembers += missing.Count;
            }
        }
    }
}
