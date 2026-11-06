using Microsoft.Data.Sqlite;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <param name="Info">What the file says about itself; null for a file ffmpeg could not read (see <paramref name="Failure"/>).</param>
    /// <param name="Date">When it was recorded, see <see cref="VideoDates"/>.</param>
    /// <param name="Failure">Why the file could not be read; it is not tried again until the file changes.</param>
    public sealed record VideoRecord(long Id, string RelativePath, long Length, long ModifiedTicks, VideoInfo? Info, PhotoDate Date, string? Failure);

    /// <summary>
    /// What is known of the videos of one folder, in an SQLite file of its own (see <see cref="VideoPaths.CatalogFile"/>): what each video says about itself
    /// and a picture to show for it, kept so that browsing a folder of videos on a network share does not read every video again (reading the
    /// header of one takes a fraction of a second, a picture about the same, and a folder holds hundreds). A video that changed on disk is read
    /// again. Safe to use from several threads; the methods that read a video block for as long as ffmpeg takes, so they belong on a background thread.
    /// </summary>
    public sealed partial class VideoCatalog : IDisposable
    {
        /// <summary>Raised when the way a video is read changes so that what was kept of it is wrong: it is read again when it is next asked for.</summary>
        public const int ProbeVersion = 1;

        private const int SchemaVersion = 1;

        private readonly SqliteConnection _db;
        private readonly object _gate = new();

        private VideoCatalog(string root, SqliteConnection db)
        {
            Root = root;
            _db = db;
        }

        /// <summary>The folder the catalog is of; the videos are known by their path below it.</summary>
        public string Root { get; }

        public static VideoCatalog Open(string folder) => Open(folder, VideoPaths.CatalogFile(folder));

        public static VideoCatalog Open(string folder, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var db = new SqliteConnection($"Data Source={file};Pooling=False");
            db.Open();

            var catalog = new VideoCatalog(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), db);
            catalog.PrepareSchema();
            return catalog;
        }

        /// <summary>How many videos are known.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    using var command = _db.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM videos";
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// What is known of a video: kept if the file is the same as when it was read, else read now (the header only; ffmpeg runs for a moment).
        /// A file ffmpeg cannot read gives a record with <see cref="VideoRecord.Failure"/> and is not read again until it changes.
        /// Throws <see cref="FileNotFoundException"/> if the file is gone, and what <see cref="VideoProbe.ReadAsync"/> throws for a read that did not
        /// finish (no ffmpeg, a share that does not answer): that is tried again next time, not remembered.
        /// </summary>
        public async Task<VideoRecord> GetAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists)
                throw new FileNotFoundException(null, fullPath);

            var relative = RelativeOf(fullPath);
            var length = file.Length;
            var modified = file.LastWriteTimeUtc.Ticks;
            var now = DateTime.Now;

            if (Lookup(relative) is { } kept && kept.Length == length && kept.ModifiedTicks == modified && kept.ProbeVersion == ProbeVersion)
                return Dated(kept.Record, file, now);

            VideoInfo? info = null;
            string? failure = null;
            try
            {
                info = await VideoProbe.ReadAsync(fullPath, cancellationToken);
            }
            catch (InvalidDataException ex)
            {
                failure = ex.Message;
            }

            var date = info is null ? PhotoDate.NotRead : VideoDates.Resolve(info, file.CreationTimeUtc, file.LastWriteTimeUtc, now);
            return Save(relative, length, modified, info, date, failure);
        }

        /// <summary>
        /// The picture to show for a video (a JPEG, see <see cref="VideoPoster"/>), kept like the rest. Throws <see cref="InvalidDataException"/>
        /// for a video that cannot be read.
        /// </summary>
        public async Task<byte[]> GetPosterAsync(string fullPath, CancellationToken cancellationToken = default)
        {
            var record = await GetAsync(fullPath, cancellationToken);
            if (record.Info is null)
                throw new InvalidDataException(record.Failure);

            if (LoadPoster(record.Id) is { } kept)
                return kept;

            var seconds = VideoPoster.SecondsFor(record.Info.DurationSeconds);
            var jpeg = await VideoPoster.ExtractAsync(fullPath, seconds, VideoPoster.LongSide, cancellationToken);
            StorePoster(record.Id, seconds, jpeg);
            return jpeg;
        }

        /// <summary>Forgets a video (it was moved away or deleted). Nothing happens for one that is not known.</summary>
        public void Forget(string fullPath)
        {
            lock (_gate)
            {
                Execute("DELETE FROM videos WHERE path = $path", ("$path", RelativeOf(fullPath)));
                BumpMomentsVersion();
                BumpSpeechVersion();
            }
        }

        public void Dispose()
        {
            lock (_gate)
                _db.Dispose();
        }

        // ── the file ───────────────────────────────────────────────────────

        private string RelativeOf(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

        private sealed record Kept(VideoRecord Record, int ProbeVersion)
        {
            public long Length => Record.Length;
            public long ModifiedTicks => Record.ModifiedTicks;
        }

        // The date rules can change without the file being read again: the date inside the video is kept as it was written, the rest is worked out
        private static VideoRecord Dated(VideoRecord record, FileInfo file, DateTime now) =>
            record.Info is { } info && record.Date.Version != PhotoDates.Version
                ? record with { Date = VideoDates.Resolve(info, file.CreationTimeUtc, file.LastWriteTimeUtc, now) }
                : record;

        private const string RecordColumns = """
            id, length, modified, probe_version, duration, width, height, rotation, fps, vcodec, acodec, bitrate, container_date, lat, lon, encoder,
            taken, taken_source, date_version, failure, path
            """;

        private Kept? Lookup(string relative)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = $"SELECT {RecordColumns} FROM videos WHERE path = $path";
                command.Parameters.AddWithValue("$path", relative);
                using var reader = command.ExecuteReader();
                return reader.Read() ? ReadKept(reader) : null;
            }
        }

        private static Kept ReadKept(SqliteDataReader reader)
        {
            var failure = reader.IsDBNull(19) ? null : reader.GetString(19);
            VideoInfo? info = failure is not null ? null : new VideoInfo(
                reader.GetDouble(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetDouble(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt64(11),
                reader.IsDBNull(12) ? null : new DateTime(reader.GetInt64(12), DateTimeKind.Unspecified),
                reader.IsDBNull(13) ? null : reader.GetDouble(13), reader.IsDBNull(14) ? null : reader.GetDouble(14),
                reader.IsDBNull(15) ? null : reader.GetString(15));
            var date = new PhotoDate(
                reader.IsDBNull(16) ? null : new DateTime(reader.GetInt64(16), DateTimeKind.Unspecified), (DateSource)reader.GetInt32(17), reader.GetInt32(18));

            return new Kept(new VideoRecord(reader.GetInt64(0), reader.GetString(20), reader.GetInt64(1), reader.GetInt64(2), info, date, failure), reader.GetInt32(3));
        }

        /// <summary>Every video the catalog knows, as it was read (also the ones that could not be), by path.</summary>
        public IReadOnlyList<VideoRecord> Records()
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = $"SELECT {RecordColumns} FROM videos ORDER BY path";
                using var reader = command.ExecuteReader();
                var records = new List<VideoRecord>();
                while (reader.Read())
                    records.Add(ReadKept(reader).Record);
                return records;
            }
        }

        /// <summary>The full path of a video of this catalog.</summary>
        public string FullPathOf(VideoRecord record) => Path.Combine(Root, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        private VideoRecord Save(string relative, long length, long modified, VideoInfo? info, PhotoDate date, string? failure)
        {
            lock (_gate)
            {
                using var transaction = _db.BeginTransaction();
                using (var command = _db.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO videos (path, length, modified, probe_version, duration, width, height, rotation, fps, vcodec, acodec, bitrate, container_date, lat, lon, encoder,
                                            taken, taken_source, date_version, failure)
                        VALUES ($path, $length, $modified, $probe, $duration, $width, $height, $rotation, $fps, $vcodec, $acodec, $bitrate, $container, $lat, $lon, $encoder,
                                $taken, $source, $dateversion, $failure)
                        ON CONFLICT(path) DO UPDATE SET length = $length, modified = $modified, probe_version = $probe, duration = $duration, width = $width, height = $height,
                            rotation = $rotation, fps = $fps, vcodec = $vcodec, acodec = $acodec, bitrate = $bitrate, container_date = $container, lat = $lat, lon = $lon,
                            encoder = $encoder, taken = $taken, taken_source = $source, date_version = $dateversion, failure = $failure
                        """;
                    var p = command.Parameters;
                    p.AddWithValue("$path", relative);
                    p.AddWithValue("$length", length);
                    p.AddWithValue("$modified", modified);
                    p.AddWithValue("$probe", ProbeVersion);
                    p.AddWithValue("$duration", info?.DurationSeconds ?? 0);
                    p.AddWithValue("$width", info?.Width ?? 0);
                    p.AddWithValue("$height", info?.Height ?? 0);
                    p.AddWithValue("$rotation", info?.Rotation ?? 0);
                    p.AddWithValue("$fps", info?.FramesPerSecond ?? 0);
                    p.AddWithValue("$vcodec", info?.VideoCodec ?? string.Empty);
                    p.AddWithValue("$acodec", (object?)info?.Audio ?? DBNull.Value);
                    p.AddWithValue("$bitrate", info?.BitsPerSecond ?? 0);
                    p.AddWithValue("$container", info?.Recorded is { } recorded ? recorded.Ticks : DBNull.Value);
                    p.AddWithValue("$lat", (object?)info?.Latitude ?? DBNull.Value);
                    p.AddWithValue("$lon", (object?)info?.Longitude ?? DBNull.Value);
                    p.AddWithValue("$encoder", (object?)info?.Encoder ?? DBNull.Value);
                    p.AddWithValue("$taken", date.Taken is { } taken ? taken.Ticks : DBNull.Value);
                    p.AddWithValue("$source", (int)date.Source);
                    p.AddWithValue("$dateversion", date.Version);
                    p.AddWithValue("$failure", (object?)failure ?? DBNull.Value);
                    command.ExecuteNonQuery();
                }

                long id;
                using (var read = _db.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = "SELECT id FROM videos WHERE path = $path";
                    read.Parameters.AddWithValue("$path", relative);
                    id = Convert.ToInt64(read.ExecuteScalar());
                }

                // The picture belonged to the file as it was
                using (var drop = _db.CreateCommand())
                {
                    drop.Transaction = transaction;
                    drop.CommandText = "DELETE FROM posters WHERE video_id = $id";
                    drop.Parameters.AddWithValue("$id", id);
                    drop.ExecuteNonQuery();
                }

                // So did what was made of its pictures: the moments and the faces in them are of another video now
                DropAnalysisOf(transaction, id);

                transaction.Commit();
                return new VideoRecord(id, relative, length, modified, info, date, failure);
            }
        }

        private byte[]? LoadPoster(long id)
        {
            lock (_gate)
            {
                using var command = _db.CreateCommand();
                command.CommandText = "SELECT jpeg FROM posters WHERE video_id = $id";
                command.Parameters.AddWithValue("$id", id);
                return command.ExecuteScalar() as byte[];
            }
        }

        private void StorePoster(long id, double seconds, byte[] jpeg)
        {
            lock (_gate)
                Execute("INSERT OR REPLACE INTO posters (video_id, at_seconds, jpeg) VALUES ($id, $at, $jpeg)", ("$id", id), ("$at", seconds), ("$jpeg", jpeg));
        }

        // ── the schema ─────────────────────────────────────────────────────

        private void PrepareSchema()
        {
            Execute("PRAGMA journal_mode = WAL");
            Execute("PRAGMA synchronous = NORMAL");
            Execute("PRAGMA foreign_keys = ON");
            Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");

            using var read = _db.CreateCommand();
            read.CommandText = "SELECT value FROM meta WHERE key = 'schema'";
            if (read.ExecuteScalar() as string != SchemaVersion.ToString())
            {
                // Only a cache: a layout that cannot be added to is thrown away and made again from the videos
                Execute("DROP TABLE IF EXISTS posters");
                Execute("DROP TABLE IF EXISTS videos");
                Execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('schema', $v)", ("$v", SchemaVersion.ToString()));
            }

            Execute("""
                CREATE TABLE IF NOT EXISTS videos (
                    id INTEGER PRIMARY KEY,
                    path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    length INTEGER NOT NULL,
                    modified INTEGER NOT NULL,
                    probe_version INTEGER NOT NULL,
                    duration REAL NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, rotation INTEGER NOT NULL, fps REAL NOT NULL,
                    vcodec TEXT NOT NULL, acodec TEXT, bitrate INTEGER NOT NULL,
                    container_date INTEGER, lat REAL, lon REAL, encoder TEXT,
                    taken INTEGER, taken_source INTEGER NOT NULL, date_version INTEGER NOT NULL,
                    failure TEXT)
                """);
            Execute("""
                CREATE TABLE IF NOT EXISTS posters (
                    video_id INTEGER PRIMARY KEY REFERENCES videos(id) ON DELETE CASCADE,
                    at_seconds REAL NOT NULL,
                    jpeg BLOB NOT NULL)
                """);
            PrepareMoments();
        }

        private void Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = _db.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
    }
}
