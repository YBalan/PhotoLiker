using Microsoft.Data.Sqlite;
using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>
    /// The index of one photo folder: an SQLite file on this machine plus a copy of everything in memory, so that a
    /// search over 20,000 photos is a loop over arrays. Safe to use from several threads.
    /// </summary>
    public sealed class PhotoIndex : IDisposable
    {
        /// <summary>
        /// Bump when the layout of the file changes in a way that can not be added to: an index is only a cache, an old one is
        /// simply rebuilt. Columns that are only added (like the date of a photo) are added to an existing file instead, see
        /// <see cref="AddMissingColumns"/>, so that an index that took hours to make survives.
        /// </summary>
        private const int SchemaVersion = 1;

        private readonly SqliteConnection _db;
        private readonly object _gate = new();
        private readonly Dictionary<string, IndexedPhoto> _photos = new(StringComparer.OrdinalIgnoreCase);

        private PhotoIndex(string root, SqliteConnection db)
        {
            Root = root;
            _db = db;
        }

        /// <summary>The photo folder this index is of.</summary>
        public string Root { get; }

        /// <summary>The index file of a photo folder, or an empty index if there is none yet.</summary>
        public static PhotoIndex Open(string photoFolder) => Open(photoFolder, SearchPaths.IndexFile(photoFolder));

        public static PhotoIndex Open(string photoFolder, string indexFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexFile)!);
            var db = new SqliteConnection($"Data Source={indexFile};Pooling=False");
            db.Open();

            var index = new PhotoIndex(SearchPaths.Normalize(photoFolder), db);
            index.PrepareSchema();
            index.Load();
            return index;
        }

        // ── what is in the index ───────────────────────────────────────────

        public int PhotoCount { get { lock (_gate) return _photos.Values.Count(p => p.Clip is not null); } }

        public int FaceCount { get { lock (_gate) return _photos.Values.Sum(p => p.Faces.Count); } }

        public int FailedCount { get { lock (_gate) return _photos.Values.Count(p => p.Failure is not null); } }

        /// <summary>
        /// The photos the indexer could not decode (damaged, cut off), with the full path, the size and why: the unreadable photos of the folder, as far as it is
        /// indexed. A photo that could not be read only for now (a network error) is not in it: that is tried again, not remembered.
        /// </summary>
        public IReadOnlyList<UnreadablePhoto> FailedPhotos()
        {
            lock (_gate)
                return [.. _photos.Values.Where(p => p.Failure is not null).Select(p => new UnreadablePhoto(ToFullPath(p.RelativePath), p.Length, p.Failure!)).OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>Photos whose date has not been worked out by the current rules yet (an index made before dates were kept): "Update index" reads them.</summary>
        public int DatesPending { get { lock (_gate) return _photos.Values.Count(p => p.Clip is not null && p.Date.Version < PhotoDates.Version); } }

        /// <summary>All photos at this moment; later changes do not show in the array.</summary>
        public IndexedPhoto[] Snapshot()
        {
            lock (_gate)
                return [.. _photos.Values];
        }

        public IndexedPhoto? Find(string relativePath)
        {
            lock (_gate)
                return _photos.GetValueOrDefault(relativePath);
        }

        /// <summary>The photo with this full path, if it lies in the indexed folder and has been indexed.</summary>
        public IndexedPhoto? FindFullPath(string fullPath) =>
            TryGetRelativePath(fullPath, out var relative) ? Find(relative) : null;

        public bool TryGetRelativePath(string fullPath, out string relativePath)
        {
            relativePath = string.Empty;
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            var relative = Path.GetRelativePath(Root, fullPath);
            if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                return false;

            relativePath = relative.Replace('\\', '/');
            return true;
        }

        public string ToFullPath(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        // ── changing the index ─────────────────────────────────────────────

        /// <summary>Stores photos (and their faces), replacing the earlier version of the same photo.</summary>
        public void Save(IReadOnlyCollection<IndexedPhoto> photos)
        {
            if (photos.Count == 0)
                return;

            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                foreach (var photo in photos)
                {
                    photo.Id = UpsertPhoto(photo);
                    Execute("DELETE FROM faces WHERE photo_id = $photo", ("$photo", photo.Id));
                    foreach (var face in photo.Faces)
                        face.Id = InsertFace(photo.Id, face);

                    _photos[photo.RelativePath] = photo;
                }
                transaction.Commit();
            }
        }

        /// <summary>Stores the dates of photos that are already in the index, leaving everything else about them alone.</summary>
        public void SaveDates(IReadOnlyCollection<(IndexedPhoto Photo, PhotoDate Date)> dates)
        {
            if (dates.Count == 0)
                return;

            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                foreach (var (photo, date) in dates)
                {
                    Execute(
                        "UPDATE photos SET taken = $taken, taken_source = $source, date_version = $version WHERE id = $id",
                        ("$taken", date.Taken is { } taken ? (object)taken.Ticks : DBNull.Value),
                        ("$source", (int)date.Source), ("$version", date.Version), ("$id", photo.Id));
                    photo.Date = date;
                }
                transaction.Commit();
            }
        }

        /// <summary>
        /// Photos whose file got another date of change while the picture stayed the same (see <see cref="DateAligner"/>) are still
        /// the indexed ones: without this "Update index" would take them for changed photos and analyze them again, which takes hours.
        /// A photo is only taken over if the index knew it by <c>PreviousTicks</c>; one that was already out of date stays so.
        /// </summary>
        public void UpdateStamps(IReadOnlyCollection<(string FullPath, long PreviousTicks, long ModifiedTicks)> stamps)
        {
            if (stamps.Count == 0)
                return;

            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                foreach (var (fullPath, previous, modified) in stamps)
                {
                    if (!TryGetRelativePath(fullPath, out var relative) || !_photos.TryGetValue(relative, out var photo) || photo.ModifiedTicks != previous)
                        continue;

                    Execute("UPDATE photos SET modified = $modified WHERE id = $id", ("$modified", modified), ("$id", photo.Id));
                    photo.ModifiedTicks = modified;
                }
                transaction.Commit();
            }
        }

        /// <summary>
        /// A photo was turned by the app (only its EXIF orientation changed, see <see cref="JpegOrientation"/>): the boxes of its faces, given
        /// as fractions of the upright photo, turn with it, so they stay on the faces. If the file got another size (the orientation had to
        /// be added) and the index knew the old one, the index takes the new one over: the picture is the one that was analyzed, and "Update
        /// index" would otherwise take the photo for a changed one and analyze it again.
        /// </summary>
        public void Turn(string fullPath, int clockwiseQuarterTurns, long previousLength, long newLength)
        {
            if (!TryGetRelativePath(fullPath, out var relative))
                return;

            var turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
            lock (_gate)
            {
                if (!_photos.TryGetValue(relative, out var photo))
                    return;

                var boxes = photo.Faces.Select(face => (Face: face, Box: TurnBox(face.X, face.Y, face.Width, face.Height, turns))).ToList();
                var takeLength = photo.Length == previousLength && newLength != previousLength;

                using var transaction = _db.BeginTransaction();
                foreach (var (face, box) in boxes)
                {
                    Execute(
                        "UPDATE faces SET x = $x, y = $y, w = $w, h = $h WHERE id = $id",
                        ("$x", box.X), ("$y", box.Y), ("$w", box.Width), ("$h", box.Height), ("$id", face.Id));
                }
                if (takeLength)
                    Execute("UPDATE photos SET length = $length WHERE id = $id", ("$length", newLength), ("$id", photo.Id));
                transaction.Commit();

                foreach (var (face, box) in boxes)
                    (face.X, face.Y, face.Width, face.Height) = (box.X, box.Y, box.Width, box.Height);
                if (takeLength)
                    photo.Length = newLength;
            }
        }

        /// <summary>The box (fractions of the photo) after the photo is turned clockwise by quarter turns: the point (x, y) goes to (1 - y, x).</summary>
        internal static (float X, float Y, float Width, float Height) TurnBox(float x, float y, float width, float height, int quarterTurns)
        {
            for (var i = 0; i < quarterTurns; i++)
                (x, y, width, height) = (1f - (y + height), x, height, width);
            return (x, y, width, height);
        }

        public void Remove(IReadOnlyCollection<string> relativePaths)
        {
            if (relativePaths.Count == 0)
                return;

            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                foreach (var path in relativePaths)
                {
                    Execute("DELETE FROM photos WHERE path = $path", ("$path", path));
                    _photos.Remove(path);
                }
                transaction.Commit();
            }
        }

        /// <summary>Forgets the photo with this full path, e.g. because it was moved away. Nothing happens if it is not in the index.</summary>
        public void RemoveFile(string fullPath)
        {
            if (TryGetRelativePath(fullPath, out var relative))
                Remove([relative]);
        }

        /// <summary>Adds a face to an indexed photo.</summary>
        public void AddFace(IndexedFace face)
        {
            lock (_gate)
            {
                face.Id = InsertFace(face.Photo.Id, face);
                face.Photo.Faces = [.. face.Photo.Faces, face];
            }
        }

        public void RemoveFace(IndexedFace face)
        {
            lock (_gate)
            {
                Execute("DELETE FROM faces WHERE id = $id", ("$id", face.Id));
                face.Photo.Faces = [.. face.Photo.Faces.Where(f => !ReferenceEquals(f, face))];
            }
        }

        // ── moving an index to another machine ─────────────────────────────

        /// <summary>
        /// Writes a copy of the index to one file (SQLite's online backup, so it is consistent even while photos are being
        /// indexed). Such a file can be brought to another machine and loaded there with <see cref="Import"/>.
        /// </summary>
        public void ExportTo(string file)
        {
            var temporary = file + ".tmp";
            File.Delete(temporary);

            lock (_gate)
            {
                using (var copy = new SqliteConnection($"Data Source={temporary};Pooling=False"))
                {
                    copy.Open();
                    _db.BackupDatabase(copy);

                    // One plain file, no write-ahead log next to it; and a note of where it comes from
                    using var finish = copy.CreateCommand();
                    finish.CommandText = """
                        PRAGMA journal_mode = DELETE;
                        INSERT OR REPLACE INTO meta (key, value) VALUES ('exported_root', $root);
                        INSERT OR REPLACE INTO meta (key, value) VALUES ('exported_at', $at);
                        """;
                    finish.Parameters.AddWithValue("$root", Root);
                    finish.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
                    finish.ExecuteNonQuery();
                }
            }

            File.Move(temporary, file, overwrite: true);
        }

        /// <summary>What is in an index file, to show before it replaces an index. Throws <see cref="InvalidDataException"/> if the file is not a usable index.</summary>
        public static IndexFileInfo Inspect(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("The index file was not found.", file);

            try
            {
                using var db = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
                db.Open();

                if (Scalar(db, "SELECT value FROM meta WHERE key = 'schema'") as string != SchemaVersion.ToString())
                    throw new InvalidDataException("This index was made by another version of PhotoLiker and can not be used.");

                var photos = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM photos WHERE clip IS NOT NULL"));
                var faces = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM faces"));
                var from = Scalar(db, "SELECT value FROM meta WHERE key = 'exported_root'") as string;
                DateTime? at = DateTime.TryParse(Scalar(db, "SELECT value FROM meta WHERE key = 'exported_at'") as string, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed.ToLocalTime() : null;
                return new IndexFileInfo(photos, faces, from, at);
            }
            catch (SqliteException ex)
            {
                throw new InvalidDataException("This is not a PhotoLiker index file.", ex);
            }
        }

        /// <summary>
        /// Makes an exported index the index of a photo folder, replacing the one it has. Paths inside an index are relative, so
        /// it does not matter that the folder is at another place (another drive letter or share) than where it was made.
        /// The folder's index must not be open.
        /// </summary>
        public static void Import(string photoFolder, string file)
        {
            Inspect(file);

            var target = SearchPaths.IndexFile(photoFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var temporary = target + ".import";
            File.Copy(file, temporary, overwrite: true);
            File.Delete(target + "-wal");
            File.Delete(target + "-shm");
            File.Move(temporary, target, overwrite: true);
        }

        private static object? Scalar(SqliteConnection db, string sql)
        {
            using var command = db.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        // ── SQLite ─────────────────────────────────────────────────────────

        private void PrepareSchema()
        {
            Execute("PRAGMA journal_mode = WAL");
            Execute("PRAGMA synchronous = NORMAL");
            Execute("PRAGMA foreign_keys = ON");
            Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");

            using var read = _db.CreateCommand();
            read.CommandText = "SELECT value FROM meta WHERE key = 'schema'";
            var current = read.ExecuteScalar() as string;
            if (current != SchemaVersion.ToString())
            {
                Execute("DROP TABLE IF EXISTS faces");
                Execute("DROP TABLE IF EXISTS photos");
                Execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('schema', $v)", ("$v", SchemaVersion.ToString()));
            }

            Execute("""
                CREATE TABLE IF NOT EXISTS photos (
                    id INTEGER PRIMARY KEY,
                    path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    length INTEGER NOT NULL,
                    modified INTEGER NOT NULL,
                    clip BLOB,
                    clip_version INTEGER NOT NULL,
                    face_version INTEGER NOT NULL,
                    failure TEXT,
                    taken INTEGER,
                    taken_source INTEGER NOT NULL DEFAULT 0,
                    date_version INTEGER NOT NULL DEFAULT 0)
                """);
            AddMissingColumns();
            Execute("""
                CREATE TABLE IF NOT EXISTS faces (
                    id INTEGER PRIMARY KEY,
                    photo_id INTEGER NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
                    x REAL NOT NULL, y REAL NOT NULL, w REAL NOT NULL, h REAL NOT NULL,
                    score REAL NOT NULL,
                    manual INTEGER NOT NULL,
                    embedding BLOB NOT NULL)
                """);
            Execute("CREATE INDEX IF NOT EXISTS faces_photo ON faces(photo_id)");
        }

        /// <summary>An index file made before the dates were kept has no columns for them: they are added, the photos keep everything they have.</summary>
        private void AddMissingColumns()
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var info = _db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(photos)";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                    have.Add(reader.GetString(1));
            }

            if (!have.Contains("taken"))
                Execute("ALTER TABLE photos ADD COLUMN taken INTEGER");
            if (!have.Contains("taken_source"))
                Execute("ALTER TABLE photos ADD COLUMN taken_source INTEGER NOT NULL DEFAULT 0");
            if (!have.Contains("date_version"))
                Execute("ALTER TABLE photos ADD COLUMN date_version INTEGER NOT NULL DEFAULT 0");
        }

        private void Load()
        {
            var byId = new Dictionary<long, IndexedPhoto>();

            using (var photos = _db.CreateCommand())
            {
                photos.CommandText = "SELECT id, path, length, modified, clip, clip_version, face_version, failure, taken, taken_source, date_version FROM photos";
                using var reader = photos.ExecuteReader();
                while (reader.Read())
                {
                    var photo = new IndexedPhoto(reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3))
                    {
                        Id = reader.GetInt64(0),
                        Clip = reader.IsDBNull(4) ? null : VectorMath.ToFloats((byte[])reader[4]),
                        ClipVersion = reader.GetInt32(5),
                        FaceVersion = reader.GetInt32(6),
                        Failure = reader.IsDBNull(7) ? null : reader.GetString(7),
                        Date = new PhotoDate(reader.IsDBNull(8) ? null : new DateTime(reader.GetInt64(8)), (DateSource)reader.GetInt32(9), reader.GetInt32(10)),
                    };
                    byId[photo.Id] = photo;
                    _photos[photo.RelativePath] = photo;
                }
            }

            var faces = new Dictionary<long, List<IndexedFace>>();
            using (var command = _db.CreateCommand())
            {
                command.CommandText = "SELECT id, photo_id, x, y, w, h, score, manual, embedding FROM faces";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!byId.TryGetValue(reader.GetInt64(1), out var photo))
                        continue;

                    var face = new IndexedFace(photo, reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5),
                        reader.GetFloat(6), reader.GetInt32(7) != 0, VectorMath.ToFloats((byte[])reader[8]))
                    { Id = reader.GetInt64(0) };

                    if (!faces.TryGetValue(photo.Id, out var list))
                        faces[photo.Id] = list = [];
                    list.Add(face);
                }
            }

            foreach (var (photoId, list) in faces)
                byId[photoId].Faces = list;
        }

        private long UpsertPhoto(IndexedPhoto photo)
        {
            using var command = _db.CreateCommand();
            command.CommandText = """
                INSERT INTO photos (path, length, modified, clip, clip_version, face_version, failure, taken, taken_source, date_version)
                VALUES ($path, $length, $modified, $clip, $clipVersion, $faceVersion, $failure, $taken, $takenSource, $dateVersion)
                ON CONFLICT(path) DO UPDATE SET
                    length = excluded.length, modified = excluded.modified, clip = excluded.clip,
                    clip_version = excluded.clip_version, face_version = excluded.face_version, failure = excluded.failure,
                    taken = excluded.taken, taken_source = excluded.taken_source, date_version = excluded.date_version
                RETURNING id
                """;
            command.Parameters.AddWithValue("$taken", photo.Date.Taken is { } taken ? (object)taken.Ticks : DBNull.Value);
            command.Parameters.AddWithValue("$takenSource", (int)photo.Date.Source);
            command.Parameters.AddWithValue("$dateVersion", photo.Date.Version);
            command.Parameters.AddWithValue("$path", photo.RelativePath);
            command.Parameters.AddWithValue("$length", photo.Length);
            command.Parameters.AddWithValue("$modified", photo.ModifiedTicks);
            command.Parameters.AddWithValue("$clip", photo.Clip is null ? DBNull.Value : VectorMath.ToBytes(photo.Clip));
            command.Parameters.AddWithValue("$clipVersion", photo.ClipVersion);
            command.Parameters.AddWithValue("$faceVersion", photo.FaceVersion);
            command.Parameters.AddWithValue("$failure", (object?)photo.Failure ?? DBNull.Value);
            return (long)command.ExecuteScalar()!;
        }

        private long InsertFace(long photoId, IndexedFace face)
        {
            using var command = _db.CreateCommand();
            command.CommandText = """
                INSERT INTO faces (photo_id, x, y, w, h, score, manual, embedding)
                VALUES ($photo, $x, $y, $w, $h, $score, $manual, $embedding)
                RETURNING id
                """;
            command.Parameters.AddWithValue("$photo", photoId);
            command.Parameters.AddWithValue("$x", face.X);
            command.Parameters.AddWithValue("$y", face.Y);
            command.Parameters.AddWithValue("$w", face.Width);
            command.Parameters.AddWithValue("$h", face.Height);
            command.Parameters.AddWithValue("$score", face.Score);
            command.Parameters.AddWithValue("$manual", face.Manual ? 1 : 0);
            command.Parameters.AddWithValue("$embedding", VectorMath.ToBytes(face.Embedding));
            return (long)command.ExecuteScalar()!;
        }

        private void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = _db.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            lock (_gate)
                _db.Dispose();
        }
    }

    /// <summary>What an index file holds.</summary>
    /// <param name="Root">The photo folder the file was exported from, if it says.</param>
    public sealed record IndexFileInfo(int Photos, int Faces, string? Root, DateTime? ExportedAt);
}
