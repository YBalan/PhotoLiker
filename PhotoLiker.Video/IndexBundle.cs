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
    /// Each of the three jobs can say how far it is (an index of a big library is hundreds of megabytes, a minute or more over a network share).
    /// </summary>
    public static class IndexBundle
    {
        private const string PhotosEntry = "photos.plindex";
        private const string VideosEntry = "videos.plvideo";

        /// <summary>
        /// Writes the index of the folder to <paramref name="file"/>: the photo index, with the video catalog of the folder if any video was analyzed or any speech read.
        /// Returns what was added of the videos, or null if the file holds the photos alone.
        /// </summary>
        /// <param name="progress">Told how much of the work is done, 0 to 1: the copies of the two indexes, then the packing of them into one file.</param>
        public static VideoIndexFileInfo? Export(PhotoIndex photos, string file, IProgress<double>? progress = null)
        {
            var photosPart = file + ".photos.tmp";
            var videosPart = file + ".videos.tmp";
            var bundle = file + ".tmp";
            DeleteAll(photosPart, videosPart, bundle);

            // The weights are the sizes of the files: the copy of each index, and then both are written once more into one file
            var photoBytes = SizeOf(SearchPaths.IndexFile(photos.Root));
            var videoBytes = SizeOf(VideoPaths.CatalogFile(photos.Root));
            var stages = new StagedProgress(progress, photoBytes, videoBytes, photoBytes + videoBytes);
            try
            {
                photos.ExportTo(photosPart, stages.Stage(0));
                var videos = VideoIndexFile.CopyOf(photos.Root, videosPart, stages.Stage(1));
                if (videos is null)
                {
                    File.Move(photosPart, file, overwrite: true);
                    stages.Complete();
                    return null;
                }

                var packing = new StagedProgress(stages.Stage(2), SizeOf(photosPart), SizeOf(videosPart));
                using (var zip = ZipFile.Open(bundle, ZipArchiveMode.Create))
                {
                    AddFile(zip, photosPart, PhotosEntry, packing.Stage(0));
                    AddFile(zip, videosPart, VideosEntry, packing.Stage(1));
                }
                File.Move(bundle, file, overwrite: true);
                stages.Complete();
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
        /// <param name="progress">Told how much of the work is done, 0 to 1: a file of both indexes is taken apart to look inside, which is what takes the time.</param>
        public static IndexBundleInfo Inspect(string file, IProgress<double>? progress = null)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("The index file was not found.", file);
            if (!IsBundle(file))
            {
                var plain = new IndexBundleInfo(PhotoIndex.Inspect(file), null);
                progress?.Report(1);
                return plain;
            }

            var parts = Unpack(file, progress);
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
        /// <param name="progress">Told how much of the work is done, 0 to 1: the file taken apart, then the two indexes put in place.</param>
        /// <returns>Whether the video catalog was replaced.</returns>
        public static bool Import(string folder, string file, IProgress<double>? progress = null)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("The index file was not found.", file);
            if (!IsBundle(file))
            {
                PhotoIndex.Import(folder, file, progress);
                progress?.Report(1);
                return false;
            }

            // Taking the file apart and putting the parts in place move the same bytes
            var stages = new StagedProgress(progress, 1, 1);
            var parts = Unpack(file, stages.Stage(0));
            try
            {
                PhotoIndex.Inspect(parts.Photos);
                VideoIndexFile.Inspect(parts.Videos);

                var installing = new StagedProgress(stages.Stage(1), SizeOf(parts.Photos), SizeOf(parts.Videos));
                PhotoIndex.Import(folder, parts.Photos, installing.Stage(0));
                VideoIndexFile.Import(folder, parts.Videos, installing.Stage(1));
                stages.Complete();
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
        private static (string Photos, string Videos) Unpack(string file, IProgress<double>? progress = null)
        {
            var photos = file + ".photos.part";
            var videos = file + ".videos.part";
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var parts = new[] { (Name: PhotosEntry, Target: photos), (Name: VideosEntry, Target: videos) }
                    .Select(part => (part.Target, Entry: zip.GetEntry(part.Name) ?? throw new InvalidDataException("This is not a PhotoLiker index file: " + part.Name + " is missing.")))
                    .ToArray();

                var stages = new StagedProgress(progress, parts.Select(part => (double)part.Entry.Length).ToArray());
                for (var i = 0; i < parts.Length; i++)
                {
                    using var from = parts[i].Entry.Open();
                    using var to = new FileStream(parts[i].Target, FileMode.Create, FileAccess.Write, FileShare.None);
                    ProgressCopy.CopyStream(from, to, parts[i].Entry.Length, stages.Stage(i));
                }

                stages.Complete();
                return (photos, videos);
            }
            catch (InvalidDataException)
            {
                DeleteAll(photos, videos);
                throw;
            }
        }

        // One file into a zip, without compression (the vectors and the thumbnails do not shrink), saying how far it is
        private static void AddFile(ZipArchive zip, string path, string entryName, IProgress<double> progress)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var from = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var to = entry.Open();
            ProgressCopy.CopyStream(from, to, from.Length, progress);
        }

        // The size of a database file with what SQLite keeps beside it that is not in it yet; 0 if there is no such file. It only decides how much of a bar a stage takes.
        private static long SizeOf(string file)
        {
            long total = 0;
            foreach (var path in new[] { file, file + "-wal" })
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                        total += info.Length;
                }
                catch (IOException)
                {
                    // a file that can not be measured counts for nothing
                }
            }
            return total;
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
        public static VideoIndexFileInfo? CopyOf(string folder, string destination, IProgress<double>? progress = null)
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
                    SqliteBackup.Copy(from, to, progress);

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
        public static void Import(string folder, string file, IProgress<double>? progress = null)
        {
            Inspect(file);

            var target = VideoPaths.CatalogFile(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var temporary = target + ".import";
            ProgressCopy.CopyFile(file, temporary, overwrite: true, progress);
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
