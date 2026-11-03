namespace PhotoLiker.Core
{
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>Which photo the slideshow shows after the one on the screen.</summary>
    public enum SlideshowMode
    {
        /// <summary>A photo picked at random, but every photo of the list once before any comes round a second time (see <see cref="SlideshowShuffle"/>).</summary>
        Random,

        /// <summary>The next photo of the list; after the last one the first again.</summary>
        OneByOne,
    }

    /// <summary>What the slideshow writes in the bottom right corner of the screen.</summary>
    public enum SlideshowWatermark
    {
        None,

        /// <summary>The year the photo was taken: 2019.</summary>
        Year,

        /// <summary>The date and the time the photo was taken, as the rest of the program writes them: 14 Aug 2025 09:12:07.</summary>
        DateTime,
    }

    /// <summary>
    /// The rules of the slideshow that do not depend on a window: how long a photo stays, which photo is next to a photo, what the watermark says.
    /// </summary>
    public static class Slideshow
    {
        public const double DefaultTimeoutSeconds = 2;

        /// <summary>Less than this would change the photo faster than it can be seen (and than it can be read from a network share).</summary>
        public const double MinTimeoutSeconds = 0.2;

        public const double MaxTimeoutSeconds = 3600;

        /// <summary>The seconds a photo stays on the screen: the setting, kept between <see cref="MinTimeoutSeconds"/> and <see cref="MaxTimeoutSeconds"/>; a value that is no number gives the default.</summary>
        public static double TimeoutOf(double seconds) =>
            double.IsNaN(seconds) ? DefaultTimeoutSeconds : Math.Clamp(seconds, MinTimeoutSeconds, MaxTimeoutSeconds);

        /// <summary>
        /// The place of the photo that is <paramref name="steps"/> photos from <paramref name="index"/> (negative: towards the first), going round the
        /// ends of the list, so the slideshow never runs out. A photo for which <paramref name="skip"/> says true (one that can not be read) is not counted;
        /// if no other photo is left the index stays where it was.
        /// </summary>
        public static int Move(int index, int count, int steps, Func<int, bool>? skip = null)
        {
            if (count <= 0)
                return -1;

            var direction = Math.Sign(steps);
            var current = index;
            for (var left = Math.Abs(steps); left > 0; left--)
            {
                var next = Neighbour(current, direction, count, skip);
                if (next < 0)
                    return current; // nothing else to go to
                current = next;
            }
            return current;
        }

        /// <summary>The photo next to <paramref name="from"/> in the direction (1 or -1) that is not skipped; -1 if all the others are.</summary>
        private static int Neighbour(int from, int direction, int count, Func<int, bool>? skip)
        {
            for (var distance = 1; distance < count; distance++) // once round the list, without coming back to the start
            {
                var candidate = Wrap(from + direction * distance, count);
                if (skip?.Invoke(candidate) != true)
                    return candidate;
            }
            return -1;
        }

        public static int Wrap(int index, int count) => (index % count + count) % count;

        /// <summary>The text for the corner of the screen; null when there is none to write (no watermark asked for, or the photo has no date).</summary>
        public static string? WatermarkText(SlideshowWatermark watermark, DateTime? taken) => watermark switch
        {
            SlideshowWatermark.Year when taken is { } date => date.Year.ToString(CultureInfo.InvariantCulture),
            SlideshowWatermark.DateTime when taken is { } date => DateText.Display(date),
            _ => null,
        };
    }

    /// <summary>
    /// Reads an enum of the slideshow by its name. A name this version doesn't know (the folder was last used by a newer version) or a typo gives
    /// <paramref name="fallback"/>, rather than failing the whole settings file.
    /// </summary>
    public abstract class TolerantEnumConverter<T>(T fallback) : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = fallback;
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                value = parsed;

            reader.Skip();
            return value;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    public sealed class SlideshowModeConverter() : TolerantEnumConverter<SlideshowMode>(SlideshowMode.Random);

    public sealed class SlideshowWatermarkConverter() : TolerantEnumConverter<SlideshowWatermark>(SlideshowWatermark.DateTime);
}
