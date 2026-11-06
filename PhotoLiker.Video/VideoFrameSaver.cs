using System.Globalization;

namespace PhotoLiker.Video
{
    /// <summary>
    /// Saves one picture of a video as a photo: a JPEG in the size of the video itself, at the place asked for, with the date it was recorded at that place as its date taken
    /// (the date the video was recorded and how far into it the picture is). A frame of a family video becomes a photo that the rest of the program (dates, search, groups)
    /// treats like any other.
    /// </summary>
    public static class VideoFrameSaver
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        /// <summary>"MVI_4069 1m23s.jpg": the name of the video and how far into it the picture is.</summary>
        public static string NameFor(string videoPath, long timeMs)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, timeMs));
            var place = time.TotalHours >= 1
                ? string.Format(CultureInfo.InvariantCulture, "{0}h{1:00}m{2:00}s", (int)time.TotalHours, time.Minutes, time.Seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}m{1:00}s", time.Minutes, time.Seconds);
            return $"{Path.GetFileNameWithoutExtension(videoPath)} {place}.jpg";
        }

        /// <summary>
        /// Writes the picture of the video at <paramref name="timeMs"/> into <paramref name="folder"/> (made if it is not there) and returns the path. A name that is taken gets
        /// " (2)", " (3)" ...: nothing is overwritten. <paramref name="recorded"/> is when the video was recorded; without it the photo gets no date taken.
        /// Throws <see cref="InvalidDataException"/> if the video has no picture there.
        /// </summary>
        public static async Task<string> SaveAsync(string videoPath, long timeMs, string folder, DateTime? recorded, CancellationToken cancellationToken = default)
        {
            string[] arguments =
            [
                "-hide_banner", "-loglevel", "error", "-nostdin",
                "-ss", (Math.Max(0, timeMs) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
                "-i", videoPath,
                "-frames:v", "1", "-q:v", "2", "-f", "image2pipe", "-c:v", "mjpeg", "pipe:1",
            ];

            var result = await Ffmpeg.RunAsync(arguments, Timeout, cancellationToken);
            if (result.Output.Length < 3 || result.Output[0] != 0xFF || result.Output[1] != 0xD8)
                throw new InvalidDataException(string.Format(VideoLibStrings.UnreadableFormat, result.Messages.Trim()));

            Directory.CreateDirectory(folder);
            var path = Unique(Path.Combine(folder, NameFor(videoPath, timeMs)));
            await File.WriteAllBytesAsync(path, result.Output, cancellationToken);

            if (recorded is { } start)
            {
                var taken = start + TimeSpan.FromMilliseconds(Math.Max(0, timeMs));
                var change = new ExifChange { Original = taken, Digitized = taken };
                try
                {
                    JpegExif.Save(path, change, fileDatesUtc: taken.ToUniversalTime());
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    // The picture is saved; it just has no date of the video in it
                }
            }
            return path;
        }

        private static string Unique(string path)
        {
            if (!File.Exists(path))
                return path;

            var folder = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            for (var n = 2; ; n++)
            {
                var candidate = Path.Combine(folder, $"{name} ({n}){extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }
    }
}
