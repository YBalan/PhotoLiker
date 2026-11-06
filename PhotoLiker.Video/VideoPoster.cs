using System.Globalization;

namespace PhotoLiker.Video
{
    /// <summary>A still picture of a video, as a JPEG: what is shown for the video until it is played, and in the strip of thumbnails.</summary>
    public static class VideoPoster
    {
        /// <summary>The longer side of the picture, in pixels (a video that is smaller than that is not enlarged).</summary>
        public const int LongSide = 960;

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Where in a video of this length the poster is taken: a tenth in, at most 5 seconds in. The very first picture of a video is often black or
        /// not yet in focus, and a long video should not be read far into just for a picture.
        /// </summary>
        public static double SecondsFor(double durationSeconds) =>
            durationSeconds <= 1 ? 0 : Math.Round(Math.Min(durationSeconds * 0.1, 5), 1);

        /// <summary>
        /// The picture of the video at <paramref name="seconds"/>, upright (ffmpeg turns what a phone stored sideways), its longer side at most
        /// <paramref name="longSide"/>. Seeks to the place instead of reading the video up to it, so it takes a fraction of a second.
        /// Throws <see cref="InvalidDataException"/> if there is no picture there.
        /// </summary>
        public static async Task<byte[]> ExtractAsync(string path, double seconds, int longSide = LongSide, CancellationToken cancellationToken = default)
        {
            // The commas are inside the quotes of the expression: they would end the filter otherwise
            var scale = $"scale='if(gt(iw,ih),min({longSide},iw),-2)':'if(gt(iw,ih),-2,min({longSide},ih))'";
            string[] arguments =
            [
                "-hide_banner", "-loglevel", "error", "-nostdin",
                "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", path,
                "-frames:v", "1", "-vf", scale, "-q:v", "4",
                "-f", "image2pipe", "-c:v", "mjpeg", "pipe:1",
            ];

            var result = await Ffmpeg.RunAsync(arguments, Timeout, cancellationToken);
            if (result.Output.Length > 2 && result.Output[0] == 0xFF && result.Output[1] == 0xD8)
                return result.Output;

            // Asked for a place beyond the end (a length that was not true): the first picture is better than none
            if (seconds > 0)
                return await ExtractAsync(path, 0, longSide, cancellationToken);

            throw new InvalidDataException(string.Format(VideoLibStrings.UnreadableFormat, result.Messages.Trim()));
        }
    }
}
