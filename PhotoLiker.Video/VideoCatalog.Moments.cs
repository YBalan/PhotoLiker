using Microsoft.Data.Sqlite;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>A moment of a video as it is stored: where it is, the vector the model made of its picture, and a small picture of it.</summary>
    public sealed record MomentData(long TimeMs, float[] Vector, byte[]? Thumbnail, IReadOnlyList<MomentFace>? Faces = null);

    /// <summary>A face in the picture of a moment: where (fractions of the picture) and the vector the face model made of it, which is what people are recognized by.</summary>
    public sealed record MomentFace(float X, float Y, float Width, float Height, float Score, float[] Embedding);

    /// <summary>The moments of one video for one model, in order of time, for a search to look through.</summary>
    public sealed record MomentSet(VideoRecord Video, string FullPath, long[] TimesMs, float[][] Vectors, IReadOnlyList<MomentFace>[] Faces);

    /// <summary>The small picture of a moment.</summary>
    public sealed record MomentThumbnail(long TimeMs, byte[] Jpeg);

    /// <summary>What was done for a video with one model: the version of the way it is done, how many moments came out, and why not if none did.</summary>
    public sealed record AnalysisState(int Version, int Moments, string? Failure);

    public sealed partial class VideoCatalog
    {
        /// <summary>Raised when the way moments are made changes so that the old ones are useless: each video is analyzed again when it is next indexed.</summary>
        public const int AnalysisVersion = 2;

        // A model's moments in memory, with the version of the catalog they were read at: another connection (the indexing job has its own) changes the version
        private readonly Dictionary<string, (long Version, IReadOnlyList<MomentSet> Sets)> _momentCache = new(StringComparer.Ordinal);

        private void PrepareMoments()
        {
            // The pictures taken out of a video, one set for every model that has been through it (vectors of different models cannot be compared)
            Execute("""
                CREATE TABLE IF NOT EXISTS moments (
                    id INTEGER PRIMARY KEY,
                    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
                    model TEXT NOT NULL,
                    t_ms INTEGER NOT NULL,
                    vector BLOB NOT NULL,
                    thumb BLOB)
                """);
            Execute("CREATE INDEX IF NOT EXISTS moments_video ON moments(video_id, model)");
            Execute("""
                CREATE TABLE IF NOT EXISTS moment_faces (
                    id INTEGER PRIMARY KEY,
                    moment_id INTEGER NOT NULL REFERENCES moments(id) ON DELETE CASCADE,
                    x REAL NOT NULL, y REAL NOT NULL, w REAL NOT NULL, h REAL NOT NULL,
                    score REAL NOT NULL,
                    embedding BLOB NOT NULL)
                """);
            Execute("CREATE INDEX IF NOT EXISTS moment_faces_moment ON moment_faces(moment_id)");
            PrepareSpeech();
            Execute("""
                CREATE TABLE IF NOT EXISTS analyses (
                    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
                    model TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    moments INTEGER NOT NULL,
                    failure TEXT,
                    PRIMARY KEY (video_id, model))
                """);
        }

        // ── what is stored ─────────────────────────────────────────────────

        /// <summary>
        /// Stores what became of a video with a model, replacing what was before: its moments, or why there are none (a video whose pictures cannot be
        /// taken is not tried again until it changes).
        /// </summary>
        public void SaveAnalysis(long videoId, string modelId, IReadOnlyCollection<MomentData> moments, string? failure = null)
        {
            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                Run(transaction, "DELETE FROM moments WHERE video_id = $v AND model = $m", ("$v", videoId), ("$m", modelId));
                foreach (var moment in moments)
                {
                    Run(transaction, "INSERT INTO moments (video_id, model, t_ms, vector, thumb) VALUES ($v, $m, $t, $vector, $thumb)",
                        ("$v", videoId), ("$m", modelId), ("$t", moment.TimeMs), ("$vector", VectorMath.ToBytes(moment.Vector)), ("$thumb", (object?)moment.Thumbnail ?? DBNull.Value));
                    if (moment.Faces is not { Count: > 0 } faces)
                        continue;

                    long momentId;
                    using (var last = _db.CreateCommand())
                    {
                        last.Transaction = transaction;
                        last.CommandText = "SELECT last_insert_rowid()";
                        momentId = Convert.ToInt64(last.ExecuteScalar());
                    }
                    foreach (var face in faces)
                    {
                        Run(transaction, "INSERT INTO moment_faces (moment_id, x, y, w, h, score, embedding) VALUES ($id, $x, $y, $w, $h, $score, $embedding)",
                            ("$id", momentId), ("$x", face.X), ("$y", face.Y), ("$w", face.Width), ("$h", face.Height), ("$score", face.Score), ("$embedding", VectorMath.ToBytes(face.Embedding)));
                    }
                }
                Run(transaction, "INSERT OR REPLACE INTO analyses (video_id, model, version, moments, failure) VALUES ($v, $m, $version, $count, $failure)",
                    ("$v", videoId), ("$m", modelId), ("$version", AnalysisVersion), ("$count", moments.Count), ("$failure", (object?)failure ?? DBNull.Value));
                BumpMomentsVersion(transaction);
                transaction.Commit();
            }
        }

        /// <summary>What was done for the videos with this model, by the id of the video (a video that is not in it has not been analyzed).</summary>
        public IReadOnlyDictionary<long, AnalysisState> AnalysisStates(string modelId)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT video_id, version, moments, failure FROM analyses WHERE model = $m";
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();
                var states = new Dictionary<long, AnalysisState>();
                while (reader.Read())
                    states[reader.GetInt64(0)] = new AnalysisState(reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3));
                return states;
            }
        }

        /// <summary>How many videos have moments for this model, and how many moments that is.</summary>
        public (int Videos, int Moments) CountAnalyzed(string modelId)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT COUNT(*), COALESCE(SUM(moments), 0) FROM analyses WHERE model = $m AND failure IS NULL AND moments > 0";
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();
                reader.Read();
                return (reader.GetInt32(0), reader.GetInt32(1));
            }
        }

        /// <summary>How many videos could not be analyzed (their pictures could not be taken), whatever the model.</summary>
        public int CountUnanalyzable()
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT COUNT(DISTINCT video_id) FROM analyses WHERE failure IS NOT NULL";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        // ── what is searched ───────────────────────────────────────────────

        /// <summary>
        /// The moments of every video that has them for this model, for a search to go through; each video once, its moments in order of time. Kept in memory
        /// and read again when the catalog has changed (also by another connection to the same file, such as a job that is indexing).
        /// </summary>
        public IReadOnlyList<MomentSet> MomentSets(string modelId)
        {
            lock (_gate)
            {
                var version = MomentsVersion();
                if (_momentCache.TryGetValue(modelId, out var cached) && cached.Version == version)
                    return cached.Sets;

                using var command = _db.CreateCommand();
                command.CommandText = $"""
                    SELECT v.id, v.length, v.modified, v.probe_version, v.duration, v.width, v.height, v.rotation, v.fps, v.vcodec, v.acodec, v.bitrate, v.container_date, v.lat, v.lon, v.encoder,
                           v.taken, v.taken_source, v.date_version, v.failure, v.path, m.t_ms, m.vector, m.id
                    FROM moments m JOIN videos v ON v.id = m.video_id
                    WHERE m.model = $m AND v.failure IS NULL
                    ORDER BY v.id, m.t_ms
                    """;
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();

                var sets = new List<MomentSet>();
                VideoRecord? current = null;
                var times = new List<long>();
                var vectors = new List<float[]>();
                var moments = new List<long>();
                var faces = FacesOfMoments(modelId);
                void Close()
                {
                    if (current is not null && times.Count > 0)
                    {
                        var faced = moments.Select(id => faces.TryGetValue(id, out var list) ? (IReadOnlyList<MomentFace>)list : []).ToArray();
                        sets.Add(new MomentSet(current, FullPathOf(current), [.. times], [.. vectors], faced));
                    }
                    times.Clear();
                    vectors.Clear();
                    moments.Clear();
                }

                while (reader.Read())
                {
                    if (current is null || reader.GetInt64(0) != current.Id)
                    {
                        Close();
                        current = ReadKept(reader).Record;
                    }
                    times.Add(reader.GetInt64(21));
                    vectors.Add(VectorMath.ToFloats((byte[])reader.GetValue(22)));
                    moments.Add(reader.GetInt64(23));
                }
                Close();

                _momentCache[modelId] = (version, sets);
                return sets;
            }
        }

        // The faces of the moments of a model, by the id of the moment (called with the lock held, in the same read as the moments)
        private Dictionary<long, List<MomentFace>> FacesOfMoments(string modelId)
        {
            using var command = _db.CreateCommand();
            command.CommandText = """
                SELECT f.moment_id, f.x, f.y, f.w, f.h, f.score, f.embedding
                FROM moment_faces f JOIN moments m ON m.id = f.moment_id
                WHERE m.model = $m
                """;
            command.Parameters.AddWithValue("$m", modelId);
            using var reader = command.ExecuteReader();
            var faces = new Dictionary<long, List<MomentFace>>();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!faces.TryGetValue(id, out var list))
                    faces[id] = list = [];
                list.Add(new MomentFace(reader.GetFloat(1), reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4), reader.GetFloat(5), VectorMath.ToFloats((byte[])reader.GetValue(6))));
            }
            return faces;
        }

        /// <summary>How many faces are known in the moments of videos for this model.</summary>
        public int CountFaces(string modelId)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM moment_faces f JOIN moments m ON m.id = f.moment_id WHERE m.model = $m";
                command.Parameters.AddWithValue("$m", modelId);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>The records of the videos that have been analyzed with any model: the videos a search by date can list (their dates are known).</summary>
        public IReadOnlyList<VideoRecord> AnalyzedRecords()
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = $"""
                    SELECT {RecordColumns} FROM videos
                    WHERE failure IS NULL AND id IN (SELECT video_id FROM analyses WHERE failure IS NULL AND moments > 0)
                    ORDER BY path
                    """;
                using var reader = command.ExecuteReader();
                var records = new List<VideoRecord>();
                while (reader.Read())
                    records.Add(ReadKept(reader).Record);
                return records;
            }
        }

        /// <summary>The small pictures of the moments of a video, in order of time, for the strip under the player. Empty if the video has no moments for this model.</summary>
        public IReadOnlyList<MomentThumbnail> Thumbnails(string fullPath, string modelId)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = """
                    SELECT m.t_ms, m.thumb FROM moments m JOIN videos v ON v.id = m.video_id
                    WHERE v.path = $path AND m.model = $m AND m.thumb IS NOT NULL
                    ORDER BY m.t_ms
                    """;
                command.Parameters.AddWithValue("$path", RelativeOf(fullPath));
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();
                var thumbnails = new List<MomentThumbnail>();
                while (reader.Read())
                    thumbnails.Add(new MomentThumbnail(reader.GetInt64(0), (byte[])reader.GetValue(1)));
                return thumbnails;
            }
        }

        // ── bookkeeping ────────────────────────────────────────────────────

        /// <summary>A number that changes whenever moments are stored or dropped, by whatever connection to the catalog.</summary>
        private long MomentsVersion()
        {
            using var command = _db.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = 'moments_version'";
            return command.ExecuteScalar() is string text && long.TryParse(text, out var version) ? version : 0;
        }

        private void BumpMomentsVersion(SqliteTransaction? transaction = null)
        {
            using var command = _db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO meta (key, value) VALUES ('moments_version', '1')
                ON CONFLICT(key) DO UPDATE SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT)
                """;
            command.ExecuteNonQuery();
        }

        // The video is another file now: what was made of the old one goes
        private void DropAnalysisOf(SqliteTransaction transaction, long videoId)
        {
            Run(transaction, "DELETE FROM moments WHERE video_id = $v", ("$v", videoId));
            Run(transaction, "DELETE FROM analyses WHERE video_id = $v", ("$v", videoId));
            Run(transaction, "DELETE FROM speech WHERE video_id = $v", ("$v", videoId));
            Run(transaction, "DELETE FROM transcripts WHERE video_id = $v", ("$v", videoId));
            BumpMomentsVersion(transaction);
            BumpSpeechVersion(transaction);
        }

        private void Run(SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = _db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
    }
}
