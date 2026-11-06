using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoLiker.Video
{
    /// <summary>
    /// Reads what a video file says about itself. The ffmpeg that comes with the program has no ffprobe next to it (it would double the size of the
    /// program), but <c>ffmpeg -i file</c> prints the same facts as text before it complains that it has nothing to write, and that text is read here.
    /// Only the header of the file is read, so it is quick, also from a share.
    /// </summary>
    public static partial class VideoProbe
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        /// <summary>Reads the header of a video. Throws <see cref="InvalidDataException"/> (with the reason ffmpeg gives) for a file that is not a video it can read.</summary>
        public static async Task<VideoInfo> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            var result = await Ffmpeg.RunAsync(["-hide_banner", "-nostdin", "-i", path], Timeout, cancellationToken);
            return Parse(result.Messages) ?? throw new InvalidDataException(string.Format(VideoLibStrings.UnreadableFormat, LastMessage(result.Messages)));
        }

        /// <summary>The facts in the text ffmpeg printed for a file; null if it printed no video in it.</summary>
        internal static VideoInfo? Parse(string text)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            // What stands before "Duration:" is the metadata of the whole file; the metadata of a stream comes after its "Stream" line
            string? created = null, location = null, encoder = null;
            double duration = 0;
            long bitrate = 0;
            var inFile = true;
            var seenFileInfo = false;

            string? video = null, audio = null;
            int width = 0, height = 0, rotation = 0;
            double fps = 0;
            var inVideo = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (DurationLine().Match(trimmed) is { Success: true } header)
                {
                    seenFileInfo = true;
                    inFile = false;
                    duration = header.Groups["n"].Success ? 0 : Seconds(header);
                    if (header.Groups["rate"].Success)
                        bitrate = long.Parse(header.Groups["rate"].Value, CultureInfo.InvariantCulture) * 1000;
                    continue;
                }

                if (StreamLine().Match(trimmed) is { Success: true } stream)
                {
                    inVideo = false;
                    var kind = stream.Groups["kind"].Value;
                    var details = stream.Groups["details"].Value;
                    if (kind == "Video" && video is null && !IsCoverPicture(trimmed))
                    {
                        inVideo = true;
                        video = details.Split(' ', 2)[0].Trim(',');
                        (width, height) = SizeOf(details);
                        fps = FramesPerSecond(details);
                    }
                    else if (kind == "Audio" && audio is null)
                    {
                        audio = AudioOf(details);
                    }
                    continue;
                }

                if (Field().Match(trimmed) is { Success: true } field)
                {
                    var name = field.Groups["name"].Value;
                    var value = field.Groups["value"].Value.Trim();
                    if (inFile)
                    {
                        if (name == "creation_time") created ??= value;
                        else if (name is "location" or "location-eng") location ??= value;
                        else if (name == "encoder") encoder ??= value;
                    }
                    else if (name == "creation_time" && created is null)
                    {
                        created = value; // a file that has the date on its streams only
                    }
                    else if (name == "displaymatrix" && inVideo && Rotation().Match(value) is { Success: true } turn)
                    {
                        rotation = (((int)Math.Round(double.Parse(turn.Groups["degrees"].Value, CultureInfo.InvariantCulture)) % 360) + 360) % 360;
                    }
                }
            }

            if (!seenFileInfo || video is null)
                return null;

            var (latitude, longitude) = LocationOf(location);
            return new VideoInfo(duration, width, height, rotation, fps, video, audio, bitrate, ParseDate(created), latitude, longitude, encoder);
        }

        /// <summary>
        /// The date ffmpeg printed: "2021-08-20T16:01:04.000000Z" is UTC (phones), "2010-05-01 16:49:25" has no time zone and is the clock of
        /// the camera (Canon), taken as it is. The result is local time.
        /// </summary>
        internal static DateTime? ParseDate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
                ? DateTime.SpecifyKind(date.LocalDateTime, DateTimeKind.Unspecified)
                : null;
        }

        /// <summary>"+46.4362+030.7044/" (ISO 6709, what phones write): latitude and longitude, the height if there is one is left out.</summary>
        internal static (double? Latitude, double? Longitude) LocationOf(string? text)
        {
            if (text is null || Iso6709().Match(text) is not { Success: true } match)
                return (null, null);

            var latitude = double.Parse(match.Groups["lat"].Value, CultureInfo.InvariantCulture);
            var longitude = double.Parse(match.Groups["lon"].Value, CultureInfo.InvariantCulture);
            return Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180 && (latitude != 0 || longitude != 0) ? (latitude, longitude) : (null, null);
        }

        private static double Seconds(Match header) =>
            int.Parse(header.Groups["h"].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(header.Groups["m"].Value, CultureInfo.InvariantCulture) * 60
            + double.Parse(header.Groups["s"].Value, CultureInfo.InvariantCulture);

        // "mjpeg (Baseline) (MJPG / 0x47504A4D), yuvj422p(pc, ...), 640x480, 14200 kb/s, 30 fps": the size is the first "WxH" that stands alone
        // (not the "0x4750..." of a codec tag)
        private static (int Width, int Height) SizeOf(string details) =>
            FrameSize().Match(details) is { Success: true } size
                ? (int.Parse(size.Groups["w"].Value, CultureInfo.InvariantCulture), int.Parse(size.Groups["h"].Value, CultureInfo.InvariantCulture))
                : (0, 0);

        private static double FramesPerSecond(string details)
        {
            foreach (var pattern in new[] { Fps(), Tbr() })
            {
                if (pattern.Match(details) is { Success: true } match &&
                    double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate is > 0 and < 1000)
                    return rate;
            }
            return 0;
        }

        // "pcm_s16le ([1][0][0][0] / 0x0001), 44100 Hz, mono, s16, 705 kb/s"  ->  "pcm_s16le, 44100 Hz, mono"
        private static string AudioOf(string details)
        {
            var parts = new List<string> { details.Split(' ', 2)[0].Trim(',') };
            if (Hertz().Match(details) is { Success: true } hertz)
                parts.Add(hertz.Value);
            if (Layout().Match(details) is { Success: true } layout)
                parts.Add(layout.Value);
            return string.Join(", ", parts);
        }

        // A music file's cover or a still picture is a "video" to ffmpeg; it is not what is meant
        private static bool IsCoverPicture(string streamLine) =>
            streamLine.Contains("attached pic", StringComparison.OrdinalIgnoreCase)
            || (streamLine.Contains("Video: png", StringComparison.Ordinal) && streamLine.Contains("1 fps", StringComparison.Ordinal));

        private static string LastMessage(string text) =>
            text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? string.Empty;

        [GeneratedRegex(@"^Duration:\s*(?:(?<n>N/A)|(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?))(?:.*?bitrate:\s*(?<rate>\d+)\s*kb/s)?", RegexOptions.CultureInvariant)]
        private static partial Regex DurationLine();

        [GeneratedRegex(@"^Stream #\d+:\d+(?:\[[^\]]*\])?(?:\([^)]*\))?:\s*(?<kind>Video|Audio|Subtitle|Data|Attachment):\s*(?<details>.*)$", RegexOptions.CultureInvariant)]
        private static partial Regex StreamLine();

        [GeneratedRegex(@"^(?<name>[A-Za-z_][\w\-]*)\s*:\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
        private static partial Regex Field();

        [GeneratedRegex(@"rotation of (?<degrees>-?\d+(?:\.\d+)?) degrees", RegexOptions.CultureInvariant)]
        private static partial Regex Rotation();

        [GeneratedRegex(@"(?<![\w.])(?<w>\d{2,5})x(?<h>\d{2,5})(?![\w.])", RegexOptions.CultureInvariant)]
        private static partial Regex FrameSize();

        [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*fps", RegexOptions.CultureInvariant)]
        private static partial Regex Fps();

        [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*tbr", RegexOptions.CultureInvariant)]
        private static partial Regex Tbr();

        [GeneratedRegex(@"\d+ Hz", RegexOptions.CultureInvariant)]
        private static partial Regex Hertz();

        [GeneratedRegex(@"\b(mono|stereo|quad|\d\.\d(?:\(\w+\))?)\b", RegexOptions.CultureInvariant)]
        private static partial Regex Layout();

        [GeneratedRegex(@"^(?<lat>[+-]\d+(?:\.\d+)?)(?<lon>[+-]\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
        private static partial Regex Iso6709();
    }
}
