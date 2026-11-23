using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.Stickers
{
    /// <summary>The file a sticker is written as. Telegram takes both for a static sticker.</summary>
    public enum StickerFormat
    {
        /// <summary>The format Telegram's own stickers are in: small (a sticker is 20 to 60 KB), with a see-through background.</summary>
        WebP,

        /// <summary>The format every program can open; larger than a WebP, and for a busy photo it can be over the 512 KB that Telegram allows.</summary>
        Png,
    }

    /// <summary>Where the shape of the sticker comes from.</summary>
    public enum CutoutMode
    {
        /// <summary>The subject of the photo is found by the model and everything else is cut away.</summary>
        Detect,

        /// <summary>No cutout: the whole picture is the sticker, with rounded corners.</summary>
        WholePicture,
    }

    /// <summary>
    /// What the sticker is like. Kept between two uses (a small JSON file); every value is brought into its range when it is used, so a file that was edited by hand or came from another version
    /// never breaks anything. The sizes are in pixels of the sticker itself (512 on its longer side), so an outline of 8 is the same fraction of the sticker whatever the photo was.
    /// </summary>
    public sealed class StickerSettings
    {
        public const int MinSensitivity = 1;
        public const int MaxSensitivity = 99;
        public const int DefaultSensitivity = 50;

        public const int DefaultSmallParts = 10;

        public const int DefaultFollowEdges = 40;

        public const int MaxEdgeShift = 10;

        public const int MaxOutlineWidth = 40;
        public const int DefaultOutlineWidth = 8;

        public const string DefaultOutlineColor = "FFFFFF";

        public const int MinQuality = 1;
        public const int MaxQuality = 100;
        public const int DefaultQuality = 85;

        public CutoutMode Cutout { get; set; } = CutoutMode.Detect;

        /// <summary>
        /// How much of what the model is unsure of is kept, 1 to 99: the higher, the more (the mask is cut where the model's certainty is 100 minus this). 50 keeps what the model says is more
        /// likely subject than not.
        /// </summary>
        public int Sensitivity { get; set; } = DefaultSensitivity;

        /// <summary>Parts of the subject that are smaller than this many percent of the biggest part are dropped (the stray patches the model finds in the background); 0 keeps every part, 100 only the biggest.</summary>
        public int SmallParts { get; set; } = DefaultSmallParts;

        /// <summary>
        /// How firmly the cut follows the edges that are in the photo, 0 to 100: the model sees a picture of 1024 pixels and its mask is soft at the edge, so the mask is pulled to the nearest edge of
        /// the photo (a guided filter); 0 leaves it as the model drew it.
        /// </summary>
        public int FollowEdges { get; set; } = DefaultFollowEdges;

        /// <summary>The cut is moved this many pixels of the sticker, -10 to 10: less takes off the fringe of the old background, more keeps what the model cut too closely. 0 leaves it where it is.</summary>
        public int EdgeShift { get; set; }

        /// <summary>A coloured border around the cut, the white edge stickers have.</summary>
        public bool Outline { get; set; } = true;

        /// <summary>The width of the border in pixels of the sticker, 0 to 40.</summary>
        public int OutlineWidth { get; set; } = DefaultOutlineWidth;

        /// <summary>The colour of the border, as six hexadecimal digits (RRGGBB).</summary>
        public string OutlineColor { get; set; } = DefaultOutlineColor;

        /// <summary>A soft shadow under the sticker, so that it shows on a light chat as well.</summary>
        public bool Shadow { get; set; } = true;

        public StickerFormat Format { get; set; } = StickerFormat.WebP;

        /// <summary>How much of the picture a WebP keeps, 1 to 100 (the file is made smaller with less until it is under Telegram's limit).</summary>
        public int Quality { get; set; } = DefaultQuality;

        /// <summary>The colour of the border as red, green and blue bytes.</summary>
        [JsonIgnore]
        public (byte Red, byte Green, byte Blue) OutlineRgb
        {
            get
            {
                var digits = NormalizeColor(OutlineColor);
                return (byte.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(digits[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Six upper case hexadecimal digits from what was given (a leading # is allowed); white when it is not a colour.</summary>
        public static string NormalizeColor(string? text)
        {
            var digits = (text ?? "").Trim().TrimStart('#');
            return digits.Length == 6 && digits.All(Uri.IsHexDigit) ? digits.ToUpperInvariant() : DefaultOutlineColor;
        }

        /// <summary>The colour of the border written as six hexadecimal digits.</summary>
        public static string ColorOf(byte red, byte green, byte blue) => $"{red:X2}{green:X2}{blue:X2}";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>A copy with every value inside its range.</summary>
        public StickerSettings Normalized() => new()
        {
            Cutout = Enum.IsDefined(Cutout) ? Cutout : CutoutMode.Detect,
            Sensitivity = Math.Clamp(Sensitivity, MinSensitivity, MaxSensitivity),
            SmallParts = Math.Clamp(SmallParts, 0, 100),
            FollowEdges = Math.Clamp(FollowEdges, 0, 100),
            EdgeShift = Math.Clamp(EdgeShift, -MaxEdgeShift, MaxEdgeShift),
            Outline = Outline,
            OutlineWidth = Math.Clamp(OutlineWidth, 0, MaxOutlineWidth),
            OutlineColor = NormalizeColor(OutlineColor),
            Shadow = Shadow,
            Format = Enum.IsDefined(Format) ? Format : StickerFormat.WebP,
            Quality = Math.Clamp(Quality, MinQuality, MaxQuality),
        };

        /// <summary>Reads the file; a file that is missing, empty or not readable gives the defaults (having none is not an error).</summary>
        public static StickerSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new StickerSettings();

                return (JsonSerializer.Deserialize<StickerSettings>(File.ReadAllText(path), Options) ?? new StickerSettings()).Normalized();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new StickerSettings();
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
