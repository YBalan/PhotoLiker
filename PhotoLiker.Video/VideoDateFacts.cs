using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>
    /// What the Align dates tool needs to know of a video, in the form it knows photos in (<see cref="PhotoDateFacts"/>): the date the video says it was recorded (the one
    /// <see cref="VideoDates"/> believes, taken from the file's own header, not from the date of the file) and the dates of the file. The tool then makes the dates of the file the
    /// date recorded. A video has no EXIF, so only the dates of the file change, unless the person gave it a date and asked for it to be written: then the date it says it was recorded is overwritten
    /// in place too (see <see cref="VideoContainerDates"/>).
    /// </summary>
    public static class VideoDateFacts
    {
        private const int Readers = 4; // reading the header of a video starts ffmpeg: a few at a time, mostly waiting for the network

        /// <summary>
        /// What is known of the video: its recorded date if the file has one that is believed (else none: the date of the file is no date to align to), and the dates of the
        /// file. A video that could not be read comes back with the reason.
        /// </summary>
        public static PhotoDateFacts Of(PhotoFile file, VideoRecord record)
        {
            var (created, modified) = TimesOf(file);
            if (record.Failure is not null || record.Info is null)
                return new PhotoDateFacts(file.OriginalFilePath, ExifDateSlots.None, null, created, modified, record.Failure ?? "the video could not be read");

            var taken = record.Date.Source == DateSource.Exif ? record.Date.Taken : null;
            return new PhotoDateFacts(file.OriginalFilePath, ExifDateSlots.None, taken, created, modified);
        }

        /// <summary>Reads the video (its header, if the catalog does not know it yet) and says what <see cref="Of"/> says. Never throws for a video that is gone or cannot be read: that is a fact too.</summary>
        public static async Task<PhotoDateFacts> InspectAsync(VideoCatalog catalog, PhotoFile file, CancellationToken cancellationToken)
        {
            try
            {
                return Of(file, await catalog.GetAsync(file.OriginalFilePath, cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                var (created, modified) = SafeTimesOf(file);
                return new PhotoDateFacts(file.OriginalFilePath, ExifDateSlots.None, null, created, modified, ex.Message);
            }
        }

        /// <summary>Looks at all the videos, in the order they are given (a few at a time). A stop throws <see cref="OperationCanceledException"/>.</summary>
        public static async Task<PhotoDateFacts[]> InspectAllAsync(
            VideoCatalog catalog, IReadOnlyList<PhotoFile> files, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            var facts = new PhotoDateFacts[files.Count];
            var done = 0;
            await Parallel.ForEachAsync(
                Enumerable.Range(0, files.Count), new ParallelOptions { MaxDegreeOfParallelism = Readers, CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    facts[i] = await InspectAsync(catalog, files[i], token);
                    var count = Interlocked.Increment(ref done);
                    if (count % 10 == 0 || count == files.Count)
                        progress?.Report(count);
                });
            return facts;
        }

        // The times of the listing the file comes from, or of the file itself when that had none
        private static (DateTime Created, DateTime Modified) TimesOf(PhotoFile file)
        {
            var created = file.CreationTimeUtc;
            var modified = file.LastWriteTimeUtc;
            if (created.Year <= 1700 || modified.Year <= 1700)
            {
                var info = new FileInfo(file.OriginalFilePath);
                created = info.CreationTimeUtc;
                modified = info.LastWriteTimeUtc;
            }
            return (created, modified);
        }

        private static (DateTime Created, DateTime Modified) SafeTimesOf(PhotoFile file)
        {
            try
            {
                return TimesOf(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (default, default);
            }
        }
    }
}
