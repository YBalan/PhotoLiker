using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoLiker.Core
{
    /// <summary>
    /// How the setting <see cref="Settings.SlideshowEffectRun"/> is written in the settings file: the number (1 to <see cref="Slideshow.MostEffectRun"/>), or the word <c>Random</c> for
    /// <see cref="Slideshow.RandomEffectRun"/>. Reading is tolerant, as for the other slideshow settings: a number out of the range is kept in it, a word that is not <c>Random</c> (a typo, or one written
    /// by a newer version) or text that is no number gives the default, and a file written by an older version, which has none of this, has the default too.
    /// </summary>
    public sealed class EffectRunConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var whole)
                        ? Slideshow.EffectRunOf((int)Math.Clamp(whole, int.MinValue, int.MaxValue))
                        : reader.TryGetDouble(out var real) && double.IsFinite(real)
                            ? Slideshow.EffectRunOf((int)Math.Clamp(Math.Round(real), int.MinValue, int.MaxValue))
                            : Slideshow.DefaultEffectRun;

                case JsonTokenType.String:
                    var text = reader.GetString()?.Trim();
                    if (string.Equals(text, nameof(SlideshowEffect.Random), StringComparison.OrdinalIgnoreCase))
                        return Slideshow.RandomEffectRun;
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed)
                        ? Slideshow.EffectRunOf(typed)
                        : Slideshow.DefaultEffectRun;

                default:
                    reader.Skip();
                    return Slideshow.DefaultEffectRun;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            var kept = Slideshow.EffectRunOf(value);
            if (kept == Slideshow.RandomEffectRun)
                writer.WriteStringValue(nameof(SlideshowEffect.Random));
            else
                writer.WriteNumberValue(kept);
        }
    }
}
