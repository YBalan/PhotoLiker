using System.Globalization;

namespace PhotoLiker.VideoToGif
{
    /// <summary>
    /// What the front end knows about a video: how long it is, how many frames a second it has (as the file says; 0 when it does not) and the size of the picture as it is seen (a phone's video
    /// that is held upright is stored sideways and shown turned, so these are the shown width and height).
    /// </summary>
    /// <param name="DurationSeconds">0 when the file does not say how long it is.</param>
    public sealed record GifClip(double DurationSeconds, double FramesPerSecond, int Width, int Height)
    {
        /// <summary>What is taken for the frame rate of a video that does not say (the numbering of frames in the window needs one).</summary>
        public const double AssumedFramesPerSecond = 25;

        public double SourceFramesPerSecond => FramesPerSecond > 0 ? FramesPerSecond : AssumedFramesPerSecond;

        /// <summary>How many frames the video has, as the length and the frame rate say; 0 when the length is not known.</summary>
        public int SourceFrames => DurationSeconds > 0 ? Math.Max(1, (int)Math.Round(DurationSeconds * SourceFramesPerSecond)) : 0;
    }

    /// <summary>Which part of the video is used: from a second to a second. All of it unless told.</summary>
    /// <param name="StartSeconds">Where the first frame is.</param>
    /// <param name="EndSeconds">Where the last frame ends; null for the end of the video (which is also how the whole of a video is asked for, so that a length that is a hair off never cuts the last frame).</param>
    public readonly record struct GifRange(double StartSeconds, double? EndSeconds)
    {
        public static GifRange All => new(0, null);

        /// <summary>
        /// The frames <paramref name="firstFrame"/> to <paramref name="lastFrame"/> of the video, both counted from 0 and both included. The last frame of the video (or a video of an unknown length)
        /// leaves the end open.
        /// </summary>
        public static GifRange FromFrames(GifClip clip, int firstFrame, int lastFrame)
        {
            var count = clip.SourceFrames;
            if (count == 0)
                return All;

            var rate = clip.SourceFramesPerSecond;
            var first = Math.Clamp(firstFrame, 0, count - 1);
            var last = Math.Clamp(lastFrame, first, count - 1);
            return new GifRange(first / rate, last >= count - 1 ? null : (last + 1) / rate);
        }
    }

    /// <summary>
    /// What a GIF will be, worked out before ffmpeg is asked: the part of the video that is used, how many frames that gives, the size, and the arguments of the two runs. The numbers shown to the
    /// person ("142 frames, 640 x 360") are these, so what is promised is what is made.
    /// </summary>
    public sealed class GifPlan
    {
        private GifPlan(GifSettings settings, double startSeconds, double? endSeconds, double lengthSeconds, int frameCount, int width, int height)
        {
            Settings = settings;
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            LengthSeconds = lengthSeconds;
            FrameCount = frameCount;
            Width = width;
            Height = height;
        }

        /// <summary>The settings, inside their ranges.</summary>
        public GifSettings Settings { get; }

        /// <summary>Where the part that is used starts, in seconds.</summary>
        public double StartSeconds { get; }

        /// <summary>Where it ends; null when it goes on to the end of the video.</summary>
        public double? EndSeconds { get; }

        /// <summary>How long the part is, in seconds; 0 when the video does not say how long it is and the range does not either.</summary>
        public double LengthSeconds { get; }

        /// <summary>How many frames the GIF will have (<see cref="LengthSeconds"/> times the frame rate); 0 when the length is not known.</summary>
        public int FrameCount { get; }

        /// <summary>How long each frame is shown: the delay that was asked for, else as long as the stretch of the video it stands for (<see cref="GifSettings.FrameSeconds"/>).</summary>
        public double DelaySeconds => Settings.DelaySeconds ?? Settings.FrameSeconds;

        /// <summary>How long the GIF plays once: every frame is shown for its delay, the last one too, so 3 frames of 1.5 seconds are 4.5 seconds although the part of the video is 4.</summary>
        public double PlaySeconds => FrameCount * DelaySeconds;

        /// <summary>How fast the GIF plays compared with the video: 1 is as fast, 5 is five times as fast (frames taken every 0.5 seconds and shown for 0.1), 0.5 is half as fast.</summary>
        public double Speed => Settings.FrameSeconds / DelaySeconds;

        // The frames are shown for a time other than the distance they were taken at
        private bool Retimed => Math.Abs(DelaySeconds - Settings.FrameSeconds) > 1e-9;

        /// <summary>The size of the GIF; 0 when the video did not say how big its picture is.</summary>
        public int Width { get; }

        public int Height { get; }

        public static GifPlan Make(GifClip clip, GifRange range, GifSettings settings)
        {
            ArgumentNullException.ThrowIfNull(clip);
            var s = (settings ?? new GifSettings()).Normalized();
            var shortest = 1.0 / clip.SourceFramesPerSecond; // a range is at least one frame of the video (the frame at its start is always taken, however slowly the GIF goes)

            var start = double.IsFinite(range.StartSeconds) ? Math.Max(0, range.StartSeconds) : 0;
            double? end = range.EndSeconds is { } asked && double.IsFinite(asked) ? asked : null;
            double length = 0;

            if (clip.DurationSeconds > 0)
            {
                start = Math.Min(start, Math.Max(0, clip.DurationSeconds - shortest));
                if (end is { } stop)
                {
                    stop = Math.Min(stop, clip.DurationSeconds);
                    if (stop - start < shortest)
                        stop = Math.Min(clip.DurationSeconds, start + shortest);

                    // So close to the end that it is the end: the video is read to its last frame, whatever its length says to a thousandth
                    end = stop >= clip.DurationSeconds - 0.0005 ? null : stop;
                }

                length = (end ?? clip.DurationSeconds) - start;
            }
            else if (end is { } open)
            {
                end = Math.Max(open, start + shortest);
                length = end.Value - start;
            }

            // A frame is taken at the start and then at every frame time (the fps filter rounds up, see Chain), so a part of 5 seconds at 2 seconds a frame has 3: at 0, 2 and 4. The small
            // amount is for a division that comes out a hair over a whole number (0.56 / 0.08 is 7.000000000000001 in a double, and the part has 7 frames, not 8).
            var frames = length > 0 ? Math.Max(1, (int)Math.Ceiling(length / s.FrameSeconds - 1e-6)) : 0;

            int width = 0, height = 0;
            if (clip.Width > 0 && clip.Height > 0)
            {
                width = s.OriginalSize ? clip.Width : Math.Min(s.Width, clip.Width);
                height = Math.Max(1, (int)Math.Round(clip.Height * (double)width / clip.Width));
            }

            return new GifPlan(s, start, end, length, frames, width, height);
        }

        /// <summary>
        /// The frames are taken one frame time apart (<c>fps=1/0.5</c> is a frame every half second; ffmpeg works the fraction out) and made as wide as asked (never wider than the video: a GIF that is
        /// stretched is only bigger). <c>round=up</c> takes the frame at the start of every frame time: with the usual rounding a part that is shorter than half a frame time (2 seconds at 5 seconds a
        /// frame) gets no frame at all, and one of 4 seconds at 3 seconds a frame only the first.
        /// </summary>
        internal string Chain => Sample + Scale;

        private string Sample => $"fps=1/{Seconds(Settings.FrameSeconds)}:round=up";

        private string Scale => Settings.OriginalSize ? string.Empty : $",scale='min({Number(Settings.Width)},iw)':-1:flags=lanczos";

        /// <summary>
        /// What the frames go through for the GIF: <see cref="Chain"/>, and when they are to be shown for another time than they were taken apart, a new time for each (<c>setpts=N*0.1/TB</c> puts frame
        /// number N at N times 0.1 seconds, whatever the distance was): the GIF muxer writes the distance between two frames as the time the first is shown. The times are whole numbers of the time
        /// base of the stream, and the one that comes out of the fps filter is as coarse as the frames are far apart (1 second for a frame every second, so 0.2 seconds would be 0): <c>settb=1/1000</c>
        /// makes them milliseconds first.
        /// </summary>
        internal string GifChain => Sample + (Retimed ? $",settb=1/1000,setpts=N*{Seconds(DelaySeconds)}/TB" : string.Empty) + Scale;

        /// <summary>The first run: the best palette for the frames that are used, as a one-picture PNG.</summary>
        internal string[] PaletteArguments(string videoPath, string palettePath) =>
        [
            ..CommonArguments(), ..InputArguments(videoPath),
            "-vf", $"{Chain},palettegen=max_colors={Number(Settings.Colors)}:stats_mode=full",
            "-frames:v", "1", "-update", "1", "-f", "image2", "-c:v", "png", ..ProgressArguments(), palettePath,
        ];

        /// <summary>The second run: the frames, drawn with that palette. <c>diff_mode=rectangle</c> keeps what did not change from one frame to the next out of the file, which is most of a screen recording.</summary>
        internal string[] GifArguments(string videoPath, string palettePath, string gifPath) =>
        [
            ..CommonArguments(), ..InputArguments(videoPath), "-i", palettePath,
            "-lavfi", $"{GifChain}[x];[x][1:v]paletteuse=dither={DitherName}:diff_mode=rectangle",
            "-loop", Settings.Loop == GifLoop.Once ? "-1" : "0",
            "-final_delay", Math.Round(DelaySeconds * 100).ToString(CultureInfo.InvariantCulture), // how long the last frame is shown: it has no frame after it to be measured against
            // The frames keep the times they were given: left to itself ffmpeg holds the output to the frame rate the frames were taken at, and drops the ones that are closer together than that (a GIF
            // that is to play faster than the video, frames every 0.5 seconds and shown for 0.1, would lose four frames of every five)
            "-fps_mode", "passthrough",
            ..ProgressArguments(), "-f", "gif", gifPath,
        ];

        private string DitherName => Settings.Dither switch
        {
            GifDither.None => "none",
            GifDither.Bayer => "bayer:bayer_scale=3",
            _ => "sierra2_4a",
        };

        private static string[] CommonArguments() => ["-y", "-hide_banner", "-nostdin", "-loglevel", "error"];

        // Seeking before the input is exact when the video is decoded (as it is here), and does not read the video up to the place
        private string[] InputArguments(string videoPath)
        {
            var arguments = new List<string>();
            if (StartSeconds > 0)
                arguments.AddRange(["-ss", Seconds(StartSeconds)]);
            if (EndSeconds is not null)
                arguments.AddRange(["-t", Seconds(LengthSeconds)]);
            arguments.AddRange(["-i", videoPath]);
            return [.. arguments];
        }

        // A GIF has no sound, subtitles or data: only the pictures are asked for. The progress goes to the standard output, a line a second or so.
        private static string[] ProgressArguments() => ["-an", "-sn", "-dn", "-progress", "pipe:1", "-nostats"];

        private static string Seconds(double seconds) => seconds.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
