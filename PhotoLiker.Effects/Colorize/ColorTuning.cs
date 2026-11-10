namespace PhotoLiker.Effects.Colorize
{
    /// <summary>
    /// What a person can change in the colours that the model gave a photo, after the model has run (so changing it costs a few milliseconds, not the two seconds of the model).
    /// </summary>
    /// <param name="Saturation">How strong the colours are: 1 as the model gives them, less is paler, more is stronger (the Lab a and b are multiplied by it).</param>
    /// <param name="Warmth">From -1 (cooler: towards blue) to 1 (warmer: towards yellow and red); 0 leaves the colours as they are. Added to the colours of the middle tones, so that black stays black and white stays white.</param>
    public readonly record struct ColorTuning(float Saturation = 1f, float Warmth = 0f)
    {
        /// <summary>The colours as the model gives them.</summary>
        public static ColorTuning AsGiven => new(1f, 0f);

        /// <summary>How far the greatest warmth moves the b of Lab (towards yellow, or blue when cooler): about the strength of a sepia print.</summary>
        public const float WarmthB = 18f;

        /// <summary>How far the greatest warmth moves the a of Lab (towards red): a warm light is yellow with a little red in it.</summary>
        public const float WarmthA = 5f;

        /// <summary>The warmth that is added to a point of this lightness (L, 0 to 100): all of it in the middle tones, none in the black and the white.</summary>
        internal static float WarmthWeight(float lightness)
        {
            var t = Math.Clamp(lightness / 100f, 0f, 1f);
            return MathF.Sqrt(4f * t * (1f - t));
        }
    }
}
