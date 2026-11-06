using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>Which pictures of a video are taken out of it.</summary>
    public enum FrameSampling
    {
        /// <summary>Only the key frames are decoded (a phone's H.264 has one every second): the quickest way, 60 times faster than playing the video.</summary>
        KeyFrames,

        /// <summary>Every picture is decoded and the ones at the right times are kept: for a video whose pictures are all whole ones (the Motion JPEG of a camera), where every frame is a key frame and skipping buys nothing.</summary>
        EveryFrame,
    }

    /// <summary>A picture of a video: where it is (seconds from the start) and the pixels.</summary>
    public sealed record VideoFrame(double Seconds, DecodedPhoto Picture);

    /// <summary>
    /// Takes pictures out of a video, one every <see cref="IntervalSeconds"/> or so, as raw pixels that go straight to the models: ffmpeg decodes, scales and
    /// writes them to a pipe, and says where each one was (its <c>showinfo</c> filter); a picture is not written to a file and read again.
    /// </summary>
    public static partial class VideoFrames
    {
        /// <summary>The longer side of a picture, in pixels: the models look at 224, faces need more.</summary>
        public const int LongSide = 640;

        /// <summary>A picture is taken at least this many seconds after the one before it.</summary>
        public const double IntervalSeconds = 2;

        private static readonly TimeSpan TimeWait = TimeSpan.FromSeconds(3);

        // Codecs in which every picture is a whole one: there are no key frames to skip to
        private static readonly HashSet<string> IntraOnly = new(StringComparer.OrdinalIgnoreCase)
        {
            "mjpeg", "prores", "dnxhd", "huffyuv", "ffvhuff", "ffv1", "rawvideo", "png", "jpeg2000", "cfhd", "utvideo", "v210",
        };

        public static FrameSampling SamplingFor(VideoInfo info) => IntraOnly.Contains(info.VideoCodec) ? FrameSampling.EveryFrame : FrameSampling.KeyFrames;

        /// <summary>The size of the pictures taken out of a video: as it is seen (a phone's video held upright is turned by ffmpeg), at most <see cref="LongSide"/> on its longer side.</summary>
        public static (int Width, int Height) SizeOf(VideoInfo info)
        {
            if (info.DisplayWidth < 2 || info.DisplayHeight < 2)
                throw new InvalidDataException(string.Format(VideoLibStrings.UnreadableFormat, "no picture size"));

            var scale = Math.Min(1.0, LongSide / (double)Math.Max(info.DisplayWidth, info.DisplayHeight));
            return (Math.Max(2, (int)Math.Round(info.DisplayWidth * scale)), Math.Max(2, (int)Math.Round(info.DisplayHeight * scale)));
        }

        /// <summary>
        /// The ffmpeg arguments: skip to the key frames if asked, take a picture when two seconds have passed since the last one taken (the first picture
        /// at once), scale it, say where it is, and write it as B, G, R bytes with nothing between the pictures.
        /// </summary>
        internal static string[] Arguments(string path, int width, int height, FrameSampling sampling)
        {
            // The commas are inside the quotes of the expression: they would end the filter otherwise
            var select = $"select='isnan(prev_selected_t)+gte(t-prev_selected_t,{IntervalSeconds.ToString(CultureInfo.InvariantCulture)})',scale={width}:{height},showinfo";
            List<string> arguments = ["-hide_banner", "-nostdin", "-loglevel", "info"];
            if (sampling == FrameSampling.KeyFrames)
                arguments.AddRange(["-skip_frame", "nokey"]);
            arguments.AddRange(["-i", path, "-an", "-sn", "-dn", "-vf", select, "-fps_mode", "vfr", "-pix_fmt", "bgr24", "-f", "rawvideo", "pipe:1"]);
            return [.. arguments];
        }

        /// <summary>
        /// The pictures of the video, in order, as they are made (nothing is read ahead beyond the pipe). Stopping reading (or <paramref name="cancellationToken"/>) stops ffmpeg.
        /// Throws <see cref="InvalidDataException"/> if ffmpeg gave no picture at all and said why.
        /// </summary>
        public static IEnumerable<VideoFrame> Read(string path, VideoInfo info, FrameSampling sampling, CancellationToken cancellationToken = default)
        {
            var (width, height) = SizeOf(info);
            var length = width * height * 3;

            using var process = Ffmpeg.Start(Arguments(path, width, height, sampling));
            using var stop = cancellationToken.Register(() => Ffmpeg.Kill(process));

            // ffmpeg says where each picture is on its error stream, as the picture goes through the filter: before its pixels are out
            var gate = new object();
            var times = new List<double>();
            var lastLines = new Queue<string>();
            var finished = false;
            process.ErrorDataReceived += (_, e) =>
            {
                lock (gate)
                {
                    if (e.Data is null)
                    {
                        finished = true;
                    }
                    else if (e.Data.Contains("Parsed_showinfo", StringComparison.Ordinal) && PtsTime().Match(e.Data) is { Success: true } match)
                    {
                        times.Add(double.Parse(match.Groups["t"].Value, CultureInfo.InvariantCulture));
                    }
                    else if (e.Data.Length > 0)
                    {
                        lastLines.Enqueue(e.Data);
                        if (lastLines.Count > 6)
                            lastLines.Dequeue();
                    }
                    Monitor.PulseAll(gate);
                }
            };
            process.BeginErrorReadLine();

            var stream = process.StandardOutput.BaseStream;
            var index = 0;
            try
            {
                while (true)
                {
                    var buffer = new byte[length];
                    if (!ReadWhole(stream, buffer))
                        break;
                    cancellationToken.ThrowIfCancellationRequested();

                    double seconds;
                    lock (gate)
                    {
                        var until = Stopwatch.GetTimestamp() + (long)(TimeWait.TotalSeconds * Stopwatch.Frequency);
                        while (times.Count <= index && !finished && Stopwatch.GetTimestamp() < until)
                            Monitor.Wait(gate, 100);
                        seconds = index < times.Count ? times[index] : index * IntervalSeconds;
                    }
                    index++;
                    yield return new VideoFrame(Math.Max(0, seconds), new DecodedPhoto(width, height, buffer));
                }

                cancellationToken.ThrowIfCancellationRequested();
                process.WaitForExit();
                if (index == 0 && process.ExitCode != 0)
                {
                    string message;
                    lock (gate)
                        message = lastLines.Count > 0 ? lastLines.Last() : $"ffmpeg ended with {process.ExitCode}";
                    throw new InvalidDataException(string.Format(VideoLibStrings.UnreadableFormat, message));
                }
            }
            finally
            {
                if (!process.HasExited)
                    Ffmpeg.Kill(process);
            }
        }

        // false at the end of the stream (a picture that was cut off is not one)
        private static bool ReadWhole(Stream stream, byte[] buffer)
        {
            var got = 0;
            while (got < buffer.Length)
            {
                var n = stream.Read(buffer, got, buffer.Length - got);
                if (n == 0)
                    return false;
                got += n;
            }
            return true;
        }

        [GeneratedRegex(@"pts_time:\s*(?<t>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
        private static partial Regex PtsTime();
    }
}
