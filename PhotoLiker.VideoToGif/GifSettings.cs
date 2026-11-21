using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.VideoToGif
{
    /// <summary>How the colours of a frame are brought down to the 256 of a GIF.</summary>
    public enum GifDither
    {
        /// <summary>Flat colours, the smallest file; gradients show bands.</summary>
        None,

        /// <summary>A fixed pattern of dots: small files, the look of an old screen. Steady from frame to frame, so it does not shimmer.</summary>
        Bayer,

        /// <summary>Error diffusion (ffmpeg's sierra2_4a): the smoothest look, the biggest file.</summary>
        Sierra,
    }

    /// <summary>How often a GIF plays.</summary>
    public enum GifLoop
    {
        /// <summary>Over and over, which is what a GIF in a README is for.</summary>
        Forever,

        /// <summary>Once, then it stays on its last frame.</summary>
        Once,
    }

    /// <summary>
    /// What the GIF is like. Kept between two uses (a small JSON file); every value is brought into its range when it is used, so a file that was edited by hand or came from another
    /// version never breaks anything.
    /// </summary>
    public sealed class GifSettings
    {
        /// <summary>A GIF shows a frame for a whole number of hundredths of a second, and browsers show anything under 2 hundredths as 10: 2 hundredths (50 frames a second) is the shortest that is true.</summary>
        public const double MinFrameSeconds = 0.02;

        /// <summary>A minute: more is a slide show, which a GIF is not a good file for.</summary>
        public const double MaxFrameSeconds = 60;

        public const double DefaultFrameSeconds = 0.1;

        public const int MinWidth = 16;
        public const int MaxWidth = 4096;
        public const int DefaultWidth = 640;

        public const int MinColors = 2;
        public const int MaxColors = 256;

        /// <summary>
        /// How far apart in the video the frames are taken, in seconds (a fraction: 0.1 is ten of them for every second of the video, 2.5 is one every two and a half seconds, for a GIF of slides).
        /// Kept to two decimals, as a GIF counts hundredths of a second.
        /// </summary>
        public double FrameSeconds { get; set; } = DefaultFrameSeconds;

        /// <summary>
        /// How long each frame is shown when the GIF plays, in seconds (a fraction, two decimals); null, the usual, is as long as the stretch of the video it stands for (<see cref="FrameSeconds"/>), so
        /// the GIF plays at the speed of the video. Less plays it faster: frames taken every 0.5 seconds and shown for 0.1 play five times as fast; more plays it slower.
        /// </summary>
        public double? DelaySeconds { get; set; }

        /// <summary>The width in pixels the GIF is made at (a video that is narrower is not made wider). Ignored when <see cref="OriginalSize"/> is on.</summary>
        public int Width { get; set; } = DefaultWidth;

        /// <summary>The size of the video itself, nothing scaled.</summary>
        public bool OriginalSize { get; set; }

        public int Colors { get; set; } = MaxColors;

        public GifDither Dither { get; set; } = GifDither.Sierra;

        public GifLoop Loop { get; set; } = GifLoop.Forever;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>A copy with every value inside its range.</summary>
        public GifSettings Normalized() => new()
        {
            FrameSeconds = double.IsFinite(FrameSeconds) ? Math.Clamp(Math.Round(FrameSeconds, 2, MidpointRounding.AwayFromZero), MinFrameSeconds, MaxFrameSeconds) : DefaultFrameSeconds,
            DelaySeconds = DelaySeconds is { } delay && double.IsFinite(delay) ? Math.Clamp(Math.Round(delay, 2, MidpointRounding.AwayFromZero), MinFrameSeconds, MaxFrameSeconds) : null,
            Width = Math.Clamp(Width, MinWidth, MaxWidth),
            OriginalSize = OriginalSize,
            Colors = Math.Clamp(Colors, MinColors, MaxColors),
            Dither = Enum.IsDefined(Dither) ? Dither : GifDither.Sierra,
            Loop = Enum.IsDefined(Loop) ? Loop : GifLoop.Forever,
        };

        /// <summary>Reads the file; a file that is missing, empty or not readable gives the defaults (having none is not an error).</summary>
        public static GifSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new GifSettings();

                return (JsonSerializer.Deserialize<GifSettings>(File.ReadAllText(path), Options) ?? new GifSettings()).Normalized();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new GifSettings();
            }
        }

        /// <summary>Writes the file; false when it could not be written (the settings are a convenience, never a reason to stop).</summary>
        public bool Save(string path)
        {
            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(Normalized(), Options));
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
        }
    }
}
