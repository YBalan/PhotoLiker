using Microsoft.Data.Sqlite;

namespace PhotoLiker.Video
{
    /// <summary>The speech of one video for one model, for a search to look through.</summary>
    public sealed record SpeechSet(VideoRecord Video, string FullPath, IReadOnlyList<SpeechSegment> Segments);

    /// <summary>What was done for a video's speech with one model: the version of the way it is done, the language that was heard, how many segments, and why not if none.</summary>
    public sealed record TranscriptState(int Version, string Language, int Segments, string? Failure);

    /// <summary>What one model heard in a video: how many stretches of speech it found (0: it listened and heard none) and the language it was read as.</summary>
    public sealed record ModelHeard(string ModelId, int Segments, string Language);

    /// <summary>Where a video stands as far as its speech goes.</summary>
    public enum SpeechStatus
    {
        /// <summary>Not listened to yet (or listened to with a way that has been replaced since).</summary>
        NotRead,
        /// <summary>The video has no sound track: nothing to listen to.</summary>
        NoSound,
        /// <summary>The file cannot be read at all (it is damaged, or not a video).</summary>
        Unreadable,
        /// <summary>The sound could not be taken out, or the model failed on it.</summary>
        Failed,
        /// <summary>Listened to, and there is nothing said that the model could hear (silence, music, noise).</summary>
        NothingHeard,
        /// <summary>Listened to, and there is speech.</summary>
        Heard,
    }

    /// <summary>
    /// A video of the catalog as the table of the speech window and the export show it: what is said in it, in which language it was heard, and where its moments are.
    /// </summary>
    /// <param name="Language">The language the transcript was made for (what was asked, or what the model heard when it was left to hear it); empty if none.</param>
    /// <param name="Failure">Why the sound could not be heard, or the video read; null if nothing went wrong.</param>
    /// <param name="MomentTimesMs">The places (milliseconds) the pictures of the video were taken at: where its picture changes. Empty if it was not analyzed.</param>
    /// <param name="HeardBy">Every model that has listened to the video without failing, whichever model the row's own status and text are of (null: not told).</param>
    public sealed record VideoSpeechRow(
        VideoRecord Video, string FullPath, SpeechStatus Status, string Language, string? Failure, IReadOnlyList<SpeechSegment> Segments, IReadOnlyList<long> MomentTimesMs,
        IReadOnlyList<ModelHeard>? HeardBy = null)
    {
        /// <summary>What is said, as one line.</summary>
        public string Text => string.Join(' ', Segments.Select(s => s.Text.Trim()));

        /// <summary>The models that have listened to the video (see <see cref="HeardBy"/>), as the catalog knows them.</summary>
        public IReadOnlyList<ModelHeard> Models => HeardBy ?? [];

        /// <summary>The model whose words are this video's captions when <paramref name="preferredModelId"/> is the one that is set, see <see cref="SpeechCaptions.ModelFor"/>.</summary>
        public string? CaptionModel(string? preferredModelId) => SpeechCaptions.ModelFor(preferredModelId, Models);
    }

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

        /// <summary>
        /// What is said in one video, for the line under the player and the captions of the slideshow: the words of <paramref name="preferredModelId"/> (the model that is set) if it heard
        /// anything in the video, else of the model that heard the most (see <see cref="SpeechCaptions.ModelFor"/>). Empty if nothing is known.
        /// </summary>
        public IReadOnlyList<SpeechSegment> SpeechOf(string fullPath, string? preferredModelId = null)
        {
            lock (_gate)
            {
                var path = RelativeOf(fullPath);
                var heard = new List<ModelHeard>();
                using (var command = _db.CreateCommand())
                {
                    command.CommandText = """
                        SELECT t.model, t.segments, t.language FROM transcripts t JOIN videos v ON v.id = t.video_id
                        WHERE v.path = $path AND t.failure IS NULL
                        """;
                    command.Parameters.AddWithValue("$path", path);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        heard.Add(new ModelHeard(reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
                }

                if (SpeechCaptions.ModelFor(preferredModelId, heard) is not { } model)
                    return [];

                using var words = _db.CreateCommand();
                words.CommandText = """
                    SELECT s.start_ms, s.end_ms, s.text FROM speech s JOIN videos v ON v.id = s.video_id
                    WHERE v.path = $path AND s.model = $m
                    ORDER BY s.start_ms
                    """;
                words.Parameters.AddWithValue("$path", path);
                words.Parameters.AddWithValue("$m", model);
                using var rows = words.ExecuteReader();
                var segments = new List<SpeechSegment>();
                while (rows.Read())
                    segments.Add(new SpeechSegment(rows.GetInt64(0), rows.GetInt64(1), rows.GetString(2)));
                return segments;
            }
        }

        /// <summary>
        /// Every video the catalog knows (by path) with what is said in it for this model, and where its moments are (those of the model that took most). Not kept in memory:
        /// it is for a window or an export that asks now and then, and it reads the catalog as it is.
        /// </summary>
        public IReadOnlyList<VideoSpeechRow> SpeechTable(string modelId)
        {
            lock (_gate)
            {
                var records = Records();
                var states = TranscriptStates(modelId);
                var segments = new Dictionary<long, List<SpeechSegment>>();
                var moments = new Dictionary<long, List<long>>();

                using (var command = _db.CreateCommand())
                {
                    command.CommandText = "SELECT video_id, start_ms, end_ms, text FROM speech WHERE model = $m ORDER BY video_id, start_ms";
                    command.Parameters.AddWithValue("$m", modelId);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt64(0);
                        if (!segments.TryGetValue(id, out var list))
                            segments[id] = list = [];
                        list.Add(new SpeechSegment(reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3)));
                    }
                }

                // The moments of the model that found most in the video: a video may have been through more than one
                using (var command = _db.CreateCommand())
                {
                    command.CommandText = """
                        SELECT m.video_id, m.t_ms FROM moments m
                        WHERE m.model = (SELECT a.model FROM analyses a WHERE a.video_id = m.video_id AND a.failure IS NULL ORDER BY a.moments DESC, a.model LIMIT 1)
                        ORDER BY m.video_id, m.t_ms
                        """;
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt64(0);
                        if (!moments.TryGetValue(id, out var list))
                            moments[id] = list = [];
                        list.Add(reader.GetInt64(1));
                    }
                }

                // Every model that listened to each video, not only the one the table is of
                var heardBy = new Dictionary<long, List<ModelHeard>>();
                using (var command = _db.CreateCommand())
                {
                    command.CommandText = "SELECT video_id, model, segments, language FROM transcripts WHERE failure IS NULL AND version >= $version ORDER BY video_id, model";
                    command.Parameters.AddWithValue("$version", TranscriptVersion);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt64(0);
                        if (!heardBy.TryGetValue(id, out var list))
                            heardBy[id] = list = [];
                        list.Add(new ModelHeard(reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
                    }
                }

                var rows = new List<VideoSpeechRow>(records.Count);
                foreach (var record in records)
                {
                    states.TryGetValue(record.Id, out var state);
                    var status = StatusOf(record, state);
                    var language = status is SpeechStatus.Heard or SpeechStatus.NothingHeard ? state!.Language : string.Empty;
                    var failure = record.Failure ?? (status == SpeechStatus.Failed ? state!.Failure : null);
                    rows.Add(new VideoSpeechRow(
                        record, FullPathOf(record), status, language, failure,
                        status == SpeechStatus.Heard && segments.TryGetValue(record.Id, out var said) ? said : [],
                        moments.TryGetValue(record.Id, out var times) ? times : [],
                        heardBy.TryGetValue(record.Id, out var models) ? models : []));
                }
                return rows;
            }
        }

        private static SpeechStatus StatusOf(VideoRecord record, TranscriptState? state)
        {
            if (record.Failure is not null || record.Info is null)
                return SpeechStatus.Unreadable;
            if (record.Info.Audio is null)
                return SpeechStatus.NoSound;
            if (state is null || state.Version < TranscriptVersion)
                return SpeechStatus.NotRead;
            if (state.Failure is not null)
                return SpeechStatus.Failed;
            return state.Segments > 0 ? SpeechStatus.Heard : SpeechStatus.NothingHeard;
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
