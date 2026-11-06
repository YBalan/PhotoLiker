using System.IO.Compression;
using Microsoft.Data.Sqlite;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>What the videos in an exported index are: how many were analyzed, into how many moments, and how many have what is said in them read.</summary>
    public sealed record VideoIndexFileInfo(int Videos, int Moments, int Speech);

    /// <summary>What an exported index file holds: the photo index, and the video catalog when the folder had videos analyzed.</summary>
    public sealed record IndexBundleInfo(IndexFileInfo Photos, VideoIndexFileInfo? Videos);

    /// <summary>
    /// Moving the index of a folder to another computer as one file. The photo index and the video catalog are two SQLite files of their own on this computer (see
    /// <see cref="VideoPaths.CatalogFile"/>); when the folder has analyzed videos the exported file is a zip of the two (no compression: the vectors and the
    /// thumbnails do not shrink), and when it has none it is the photo index alone, exactly as <see cref="PhotoIndex.ExportTo"/> writes it, so an index
    /// of photos is still a plain file that an older version reads. Importing takes either. Paths in both are relative, so the folder may be anywhere on the other computer.
    /// </summary>
    public static class IndexBundle
    {
        private const string PhotosEntry = "photos.plindex";
        private const string VideosEntry = "videos.plvideo";

        /// <summary>
        /// Writes the index of the folder to <paramref name="file"/>: the photo index, with the video catalog of the folder if any video was analyzed or any speech read.
        /// Returns what was added of the videos, or null if the file holds the photos alone.
        /// </summary>
        public static VideoIndexFileInfo? Export(PhotoIndex photos, string file)
        {
            var photosPart = file + ".photos.tmp";
            var videosPart = file + ".videos.tmp";
            var bundle = file + ".tmp";
            DeleteAll(photosPart, videosPart, bundle);
            try
            {
                photos.ExportTo(photosPart);
                var videos = VideoIndexFile.CopyOf(photos.Root, videosPart);
                if (videos is null)
                {
                    File.Move(photosPart, file, overwrite: true);
                    return null;
                }

                using (var zip = ZipFile.Open(bundle, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(photosPart, PhotosEntry, CompressionLevel.NoCompression);
                    zip.CreateEntryFromFile(videosPart, VideosEntry, CompressionLevel.NoCompression);
                }
                File.Move(bundle, file, overwrite: true);
                return videos;
            }
            finally
            {
                DeleteAll(photosPart, videosPart, bundle);
            }
        }

        /// <summary>
        /// What is in an exported index file, to show before it replaces anything. Throws <see cref="InvalidDataException"/> if the file is not a usable index
        /// (or a part of it is not).
        /// </summary>
        public static IndexBundleInfo Inspect(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("The index file was not found.", file);
            if (!IsBundle(file))
                return new IndexBundleInfo(PhotoIndex.Inspect(file), null);

            var parts = Unpack(file);
            try
            {
                return new IndexBundleInfo(PhotoIndex.Inspect(parts.Photos), VideoIndexFile.Inspect(parts.Videos));
            }
            finally
            {
                DeleteAll(parts.Photos, parts.Videos);
            }
        }

        /// <summary>
        /// Makes an exported index the index of a folder, replacing the one it has: the photo index, and the video catalog if the file has one (a file of photos alone
        /// leaves the videos of the folder as they are). Both parts are checked before either replaces anything. Neither may be open.
        /// </summary>
        /// <returns>Whether the video catalog was replaced.</returns>
        public static bool Import(string folder, string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("The index file was not found.", file);
            if (!IsBundle(file))
            {
                PhotoIndex.Import(folder, file);
                return false;
            }

            var parts = Unpack(file);
            try
            {
                PhotoIndex.Inspect(parts.Photos);
                VideoIndexFile.Inspect(parts.Videos);
                PhotoIndex.Import(folder, parts.Photos);
                VideoIndexFile.Import(folder, parts.Videos);
                return true;
            }
            finally
            {
                DeleteAll(parts.Photos, parts.Videos);
            }
        }

        /// <summary>Whether the file is a bundle of the two (a zip) and not a plain photo index (an SQLite file).</summary>
        public static bool IsBundle(string file)
        {
            using var stream = File.OpenRead(file);
            var start = new byte[4];
            return stream.Read(start, 0, 4) == 4 && start[0] == (byte)'P' && start[1] == (byte)'K' && start[2] == 3 && start[3] == 4;
        }

        // The two parts as files next to the bundle (SQLite opens files, not streams)
        private static (string Photos, string Videos) Unpack(string file)
        {
            var photos = file + ".photos.part";
            var videos = file + ".videos.part";
            try
            {
                using var zip = ZipFile.OpenRead(file);
                foreach (var (entry, target) in new[] { (PhotosEntry, photos), (VideosEntry, videos) })
                {
                    var found = zip.GetEntry(entry) ?? throw new InvalidDataException("This is not a PhotoLiker index file: " + entry + " is missing.");
                    found.ExtractToFile(target, overwrite: true);
                }
                return (photos, videos);
            }
            catch (InvalidDataException)
            {
                DeleteAll(photos, videos);
                throw;
            }
        }

        private static void DeleteAll(params string[] files)
        {
            foreach (var file in files)
            {
                try { File.Delete(file); } catch (IOException) { /* a leftover temp file */ }
            }
        }
    }

    /// <summary>The video catalog of a folder as a file to carry (see <see cref="IndexBundle"/>).</summary>
    internal static class VideoIndexFile
    {
        /// <summary>
        /// A consistent copy of the catalog of the folder in one plain file (SQLite's online backup, so it is whole even while videos are being analyzed). Null, and no
        /// file, if the folder has no video analyzed and no speech read: what is left is only what the headers said, which is quickly read again.
        /// </summary>
        public static VideoIndexFileInfo? CopyOf(string folder, string destination)
        {
            var source = VideoPaths.CatalogFile(folder);
            if (!File.Exists(source))
                return null;

            File.Delete(destination);
            try
            {
                using (var from = new SqliteConnection($"Data Source={source};Pooling=False"))
                using (var to = new SqliteConnection($"Data Source={destination};Pooling=False"))
                {
                    from.Open();
                    to.Open();
                    from.BackupDatabase(to);

                    using var plain = to.CreateCommand(); // one file, no write-ahead log next to it
                    plain.CommandText = "PRAGMA journal_mode = DELETE";
                    plain.ExecuteNonQuery();
                }

                var info = Read(destination);
                if (info.Videos > 0 || info.Speech > 0)
                    return info;
            }
            catch (SqliteException)
            {
                // a catalog that cannot be copied is not exported: the photos still are
            }

            File.Delete(destination);
            return null;
        }

        /// <summary>What is in a catalog file. Throws <see cref="InvalidDataException"/> if the file is not a usable one.</summary>
        public static VideoIndexFileInfo Inspect(string file)
        {
            try
            {
                return Read(file);
            }
            catch (SqliteException ex)
            {
                throw new InvalidDataException("The videos in this file are not a usable catalog.", ex);
            }
        }

        private static VideoIndexFileInfo Read(string file)
        {
            using var db = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COUNT(DISTINCT video_id) FROM analyses WHERE failure IS NULL AND moments > 0),
                       (SELECT COALESCE(SUM(moments), 0) FROM analyses WHERE failure IS NULL AND moments > 0),
                       (SELECT COUNT(DISTINCT video_id) FROM transcripts WHERE failure IS NULL AND segments > 0)
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            return new VideoIndexFileInfo(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }

        /// <summary>Makes the catalog file the video catalog of the folder, replacing the one it has. The catalog of the folder must not be open.</summary>
        public static void Import(string folder, string file)
        {
            Inspect(file);

            var target = VideoPaths.CatalogFile(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var temporary = target + ".import";
            File.Copy(file, temporary, overwrite: true);
            try
            {
                // Opened once the way the program opens it: a catalog of an older layout is brought up to date before it is used
                using (var catalog = VideoCatalog.Open(folder, temporary))
                    _ = catalog.Count;
            }
            catch (SqliteException ex)
            {
                File.Delete(temporary);
                throw new InvalidDataException("The videos in this file are not a usable catalog.", ex);
            }

            File.Delete(target + "-wal");
            File.Delete(target + "-shm");
            File.Move(temporary, target, overwrite: true);
        }
    }
}
