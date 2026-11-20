using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.Core
{
    /// <summary>
    /// How the setting <see cref="Settings.SlideshowEffectRun"/> is written in the settings file: the number (1 to <see cref="Slideshow.MostEffectRun"/>), or the words <c>Random</c>,
    /// <c>Random 1-10</c> and <c>Random 3-20</c> for the random values (<see cref="Slideshow.RandomEffectRun"/> and the two that follow it). Reading is tolerant, as for the other slideshow settings: a number
    /// out of the range is kept in it (0 or less is <c>Random</c>), a word that is none of these (a typo, or one written by a newer version) or text that is no number gives the default, and a file
    /// written by an older version, which has none of this, has the default too.
    /// </summary>
    public sealed class EffectRunConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var whole)
                        ? Slideshow.EffectRunOfNumber(whole)
                        : reader.TryGetDouble(out var real) && double.IsFinite(real)
                            ? Slideshow.EffectRunOfNumber((long)Math.Clamp(Math.Round(real), int.MinValue, int.MaxValue))
                            : Slideshow.DefaultEffectRun;

                case JsonTokenType.String:
                    var text = reader.GetString()?.Trim();
                    if (Slideshow.TryReadRandomEffectRun(text, out var random))
                        return random;
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed)
                        ? Slideshow.EffectRunOfNumber(typed)
                        : Slideshow.DefaultEffectRun;

                default:
                    reader.Skip();
                    return Slideshow.DefaultEffectRun;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            var kept = Slideshow.EffectRunOf(value);
            if (Slideshow.IsRandomEffectRun(kept))
                writer.WriteStringValue(Slideshow.EffectRunText(kept));
            else
                writer.WriteNumberValue(kept);
        }
    }
}
