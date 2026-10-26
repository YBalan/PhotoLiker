using Microsoft.Data.Sqlite;

namespace PhotoLiker.Search
{
    /// <summary>
    /// The index of one photo folder: an SQLite file on this machine plus a copy of everything in memory, so that a
    /// search over 20,000 photos is a loop over arrays. Safe to use from several threads.
    /// </summary>
    public sealed class PhotoIndex : IDisposable
    {
        /// <summary>Bump when the layout of the file changes: an index is only a cache, an old one is simply rebuilt.</summary>
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
            var db = new SqliteConnection($"Data Source={indexFile}");
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
                    failure TEXT)
                """);
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

        private void Load()
        {
            var byId = new Dictionary<long, IndexedPhoto>();

            using (var photos = _db.CreateCommand())
            {
                photos.CommandText = "SELECT id, path, length, modified, clip, clip_version, face_version, failure FROM photos";
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
                INSERT INTO photos (path, length, modified, clip, clip_version, face_version, failure)
                VALUES ($path, $length, $modified, $clip, $clipVersion, $faceVersion, $failure)
                ON CONFLICT(path) DO UPDATE SET
                    length = excluded.length, modified = excluded.modified, clip = excluded.clip,
                    clip_version = excluded.clip_version, face_version = excluded.face_version, failure = excluded.failure
                RETURNING id
                """;
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
}
