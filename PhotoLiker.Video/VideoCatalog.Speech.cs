using Microsoft.Data.Sqlite;

namespace PhotoLiker.Video
{
    /// <summary>The speech of one video for one model, for a search to look through.</summary>
    public sealed record SpeechSet(VideoRecord Video, string FullPath, IReadOnlyList<SpeechSegment> Segments);

    /// <summary>What was done for a video's speech with one model: the version of the way it is done, the language that was heard, how many segments, and why not if none.</summary>
    public sealed record TranscriptState(int Version, string Language, int Segments, string? Failure);

    public sealed partial class VideoCatalog
    {
        /// <summary>Raised when the way speech is read changes so that old transcripts are useless: each video is listened to again when it is next indexed.</summary>
        public const int TranscriptVersion = 1;

        private (long Version, IReadOnlyList<SpeechSet> Sets)? _speechCache;
        private string? _speechCacheModel;

        private void PrepareSpeech()
        {
            Execute("""
                CREATE TABLE IF NOT EXISTS transcripts (
                    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
                    model TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    language TEXT NOT NULL,
                    segments INTEGER NOT NULL,
                    failure TEXT,
                    PRIMARY KEY (video_id, model))
                """);
            Execute("""
                CREATE TABLE IF NOT EXISTS speech (
                    id INTEGER PRIMARY KEY,
                    video_id INTEGER NOT NULL REFERENCES videos(id) ON DELETE CASCADE,
                    model TEXT NOT NULL,
                    start_ms INTEGER NOT NULL,
                    end_ms INTEGER NOT NULL,
                    text TEXT NOT NULL)
                """);
            Execute("CREATE INDEX IF NOT EXISTS speech_video ON speech(video_id, model)");
        }

        /// <summary>Stores what was heard in a video with a model, replacing what was before; or why nothing was (a video with no sound is not listened to again until it changes).</summary>
        public void SaveTranscript(long videoId, string modelId, string language, IReadOnlyCollection<SpeechSegment> segments, string? failure = null)
        {
            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                Run(transaction, "DELETE FROM speech WHERE video_id = $v AND model = $m", ("$v", videoId), ("$m", modelId));
                foreach (var segment in segments)
                {
                    Run(transaction, "INSERT INTO speech (video_id, model, start_ms, end_ms, text) VALUES ($v, $m, $s, $e, $t)",
                        ("$v", videoId), ("$m", modelId), ("$s", segment.StartMs), ("$e", segment.EndMs), ("$t", segment.Text));
                }
                Run(transaction, "INSERT OR REPLACE INTO transcripts (video_id, model, version, language, segments, failure) VALUES ($v, $m, $version, $l, $n, $f)",
                    ("$v", videoId), ("$m", modelId), ("$version", TranscriptVersion), ("$l", language), ("$n", segments.Count), ("$f", (object?)failure ?? DBNull.Value));
                BumpSpeechVersion(transaction);
                transaction.Commit();
            }
        }

        /// <summary>What was done for the videos' speech with this model, by the id of the video.</summary>
        public IReadOnlyDictionary<long, TranscriptState> TranscriptStates(string modelId)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT video_id, version, language, segments, failure FROM transcripts WHERE model = $m";
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();
                var states = new Dictionary<long, TranscriptState>();
                while (reader.Read())
                    states[reader.GetInt64(0)] = new TranscriptState(reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4));
                return states;
            }
        }

        /// <summary>How many videos have something said in them for this model, and how many segments that is.</summary>
        public (int Videos, int Segments) CountTranscribed(string modelId)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT COUNT(*), COALESCE(SUM(segments), 0) FROM transcripts WHERE model = $m AND failure IS NULL AND segments > 0";
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();
                reader.Read();
                return (reader.GetInt32(0), reader.GetInt32(1));
            }
        }

        /// <summary>The speech of every video that has any for this model, for a search to go through. Kept in memory and read again when the catalog has changed.</summary>
        public IReadOnlyList<SpeechSet> SpeechSets(string modelId)
        {
            lock (_gate)
            {
                var version = SpeechVersion();
                if (_speechCache is { } cached && _speechCacheModel == modelId && cached.Version == version)
                    return cached.Sets;

                using var command = _db.CreateCommand();
                command.CommandText = $"""
                    SELECT v.id, v.length, v.modified, v.probe_version, v.duration, v.width, v.height, v.rotation, v.fps, v.vcodec, v.acodec, v.bitrate, v.container_date, v.lat, v.lon, v.encoder,
                           v.taken, v.taken_source, v.date_version, v.failure, v.path, s.start_ms, s.end_ms, s.text
                    FROM speech s JOIN videos v ON v.id = s.video_id
                    WHERE s.model = $m AND v.failure IS NULL
                    ORDER BY v.id, s.start_ms
                    """;
                command.Parameters.AddWithValue("$m", modelId);
                using var reader = command.ExecuteReader();

                var sets = new List<SpeechSet>();
                VideoRecord? current = null;
                var segments = new List<SpeechSegment>();
                void Close()
                {
                    if (current is not null && segments.Count > 0)
                        sets.Add(new SpeechSet(current, FullPathOf(current), [.. segments]));
                    segments.Clear();
                }

                while (reader.Read())
                {
                    if (current is null || reader.GetInt64(0) != current.Id)
                    {
                        Close();
                        current = ReadKept(reader).Record;
                    }
                    segments.Add(new SpeechSegment(reader.GetInt64(21), reader.GetInt64(22), reader.GetString(23)));
                }
                Close();

                _speechCache = (version, sets);
                _speechCacheModel = modelId;
                return sets;
            }
        }

        /// <summary>What is said in one video, for the line under the player: of the model that heard most in it, whichever it is. Empty if nothing is known.</summary>
        public IReadOnlyList<SpeechSegment> SpeechOf(string fullPath)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = """
                    SELECT s.start_ms, s.end_ms, s.text FROM speech s JOIN videos v ON v.id = s.video_id
                    WHERE v.path = $path AND s.model = (
                        SELECT t.model FROM transcripts t WHERE t.video_id = v.id AND t.failure IS NULL ORDER BY t.segments DESC LIMIT 1)
                    ORDER BY s.start_ms
                    """;
                command.Parameters.AddWithValue("$path", RelativeOf(fullPath));
                using var reader = command.ExecuteReader();
                var segments = new List<SpeechSegment>();
                while (reader.Read())
                    segments.Add(new SpeechSegment(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
                return segments;
            }
        }

        private long SpeechVersion()
        {
            using var command = _db.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = 'speech_version'";
            return command.ExecuteScalar() is string text && long.TryParse(text, out var version) ? version : 0;
        }

        private void BumpSpeechVersion(SqliteTransaction? transaction = null)
        {
            using var command = _db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO meta (key, value) VALUES ('speech_version', '1')
                ON CONFLICT(key) DO UPDATE SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT)
                """;
            command.ExecuteNonQuery();
        }
    }
}
