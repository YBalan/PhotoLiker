using PhotoLiker.Core;

namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// Grades the colours of a picture as a film, a filter or a colour grading program would (<see cref="SlideshowLook"/>), on its pixels (0xAARRGGBB, row after row; the alpha is left out and the result is opaque),
    /// all the cores at once: about 20 ms for a full HD picture. A look is a few numbers (<see cref="Recipe"/>): the colour is made paler or stronger, given contrast, tinted in the shadows and in the lights,
    /// faded (the black lifted, the white lowered), then the corners are darkened (the vignette) and grain is added; the grain is the same on all three colours, so it never colours a grey. A look from a
    /// <c>.cube</c> file (<see cref="CubeLut"/>) is the table's and nothing else.
    /// </summary>
    public static class LookGrade
    {
        /// <summary>The numbers that make a look. Tints are what is added to red, green and blue (0 to 1 each) at full shadow or full light.</summary>
        internal readonly record struct Recipe(
            float Saturation, float Contrast, (float R, float G, float B) ShadowTint, (float R, float G, float B) LightTint,
            float BlackLift, float WhiteDrop, float Vignette, float Grain, bool Sepia = false)
        {
            public static Recipe? Of(SlideshowLook look) => look switch
            {
                SlideshowLook.Vintage => new Recipe(0.78f, 0.95f, (0.025f, 0.010f, -0.020f), (0.040f, 0.020f, -0.045f), 0.07f, 0.06f, 0.30f, 0.035f),
                SlideshowLook.Film => new Recipe(0.92f, 1.10f, (-0.025f, 0.010f, 0.035f), (0.030f, 0.015f, -0.020f), 0.02f, 0.02f, 0.16f, 0.050f),
                SlideshowLook.BlackAndWhite => new Recipe(0f, 1.12f, (0, 0, 0), (0, 0, 0), 0.02f, 0.02f, 0.14f, 0.035f),
                SlideshowLook.Cinema => new Recipe(1.08f, 1.15f, (-0.070f, 0.010f, 0.060f), (0.070f, 0.030f, -0.070f), 0.01f, 0.01f, 0.18f, 0.015f),
                SlideshowLook.Sepia => new Recipe(0f, 1.05f, (0, 0, 0), (0, 0, 0), 0f, 0f, 0.12f, 0.02f, Sepia: true),
                _ => null,
            };
        }

        /// <summary>The darkest and the lightest brown of <see cref="SlideshowLook.Sepia"/>.</summary>
        private static readonly (float R, float G, float B) SepiaDark = (0.13f, 0.075f, 0.035f);
        private static readonly (float R, float G, float B) SepiaLight = (1.0f, 0.925f, 0.76f);

        private const int RowsPerChunk = 8;

        /// <summary>
        /// The look after <paramref name="look"/> in the order of <see cref="SlideshowLook"/>, going round at the end (what the L key of the slideshow does); <see cref="SlideshowLook.Lut"/> is left out
        /// when there is no table (<paramref name="hasTable"/> false), as a look that would do nothing.
        /// </summary>
        public static SlideshowLook Next(SlideshowLook look, bool hasTable)
        {
            var all = Enum.GetValues<SlideshowLook>();
            var next = look;
            for (var step = 0; step < all.Length; step++)
            {
                next = all[(Array.IndexOf(all, next) + 1) % all.Length];
                if (next != SlideshowLook.Lut || hasTable)
                    return next;
            }
            return look;
        }

        /// <summary>
        /// Grades the picture in place. <see cref="SlideshowLook.None"/> (and <see cref="SlideshowLook.Lut"/> with no table) leaves it as it is.
        /// </summary>
        /// <param name="lut">The table of <see cref="SlideshowLook.Lut"/>.</param>
        /// <param name="seed">What the grain is made from: the same seed gives the same grain (the same grain on a photo each time it is shown), another gives another.</param>
        public static void Apply(uint[] pixels, int width, int height, SlideshowLook look, CubeLut? lut = null, int seed = 0)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height, nameof(pixels));

            if (look == SlideshowLook.None || (look == SlideshowLook.Lut && lut is null))
                return;

            var recipe = look == SlideshowLook.Lut ? null : Recipe.Of(look);
            var chunks = (height + RowsPerChunk - 1) / RowsPerChunk;
            Parallel.For(0, chunks, chunk =>
            {
                var last = Math.Min(height, (chunk + 1) * RowsPerChunk);
                for (var y = chunk * RowsPerChunk; y < last; y++)
                {
                    if (recipe is { } r)
                        GradeRow(pixels, y, width, height, r, seed);
                    else
                        LutRow(pixels, y, width, lut!);
                }
            });
        }

        private static void LutRow(uint[] pixels, int y, int width, CubeLut lut)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[row + x];
                var (r, g, b) = lut.Map(((pixel >> 16) & 0xFF) / 255f, ((pixel >> 8) & 0xFF) / 255f, (pixel & 0xFF) / 255f);
                pixels[row + x] = Pack(r, g, b);
            }
        }

        private static void GradeRow(uint[] pixels, int y, int width, int height, Recipe recipe, int seed)
        {
            var row = y * width;
            var halfWidth = Math.Max(1f, width / 2f);
            var halfHeight = Math.Max(1f, height / 2f);
            var dy = (y + 0.5f - halfHeight) / halfHeight;

            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[row + x];
                float r = ((pixel >> 16) & 0xFF) / 255f, g = ((pixel >> 8) & 0xFF) / 255f, b = (pixel & 0xFF) / 255f;
                var luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;

                if (recipe.Sepia)
                {
                    var tone = Math.Clamp(0.5f + (luma - 0.5f) * recipe.Contrast, 0f, 1f);
                    r = SepiaDark.R + (SepiaLight.R - SepiaDark.R) * tone;
                    g = SepiaDark.G + (SepiaLight.G - SepiaDark.G) * tone;
                    b = SepiaDark.B + (SepiaLight.B - SepiaDark.B) * tone;
                }
                else
                {
                    // Paler or stronger colours, then the contrast about the middle grey
                    r = luma + (r - luma) * recipe.Saturation;
                    g = luma + (g - luma) * recipe.Saturation;
                    b = luma + (b - luma) * recipe.Saturation;
                    r = 0.5f + (r - 0.5f) * recipe.Contrast;
                    g = 0.5f + (g - 0.5f) * recipe.Contrast;
                    b = 0.5f + (b - 0.5f) * recipe.Contrast;

                    // The tints: the shadows get theirs in proportion to how dark the pixel is, the lights in proportion to how light
                    var tone = Math.Clamp(0.2126f * r + 0.7152f * g + 0.0722f * b, 0f, 1f);
                    var shadow = (1 - tone) * (1 - tone);
                    var light = tone * tone;
                    r += recipe.ShadowTint.R * shadow + recipe.LightTint.R * light;
                    g += recipe.ShadowTint.G * shadow + recipe.LightTint.G * light;
                    b += recipe.ShadowTint.B * shadow + recipe.LightTint.B * light;
                }

                // The fade: the black lifted and the white lowered
                var range = 1 - recipe.BlackLift - recipe.WhiteDrop;
                r = recipe.BlackLift + Math.Clamp(r, 0f, 1f) * range;
                g = recipe.BlackLift + Math.Clamp(g, 0f, 1f) * range;
                b = recipe.BlackLift + Math.Clamp(b, 0f, 1f) * range;

                // The corners: dark towards the edge, from a third of the way out (smooth, so that there is no ring)
                if (recipe.Vignette > 0)
                {
                    var dx = (x + 0.5f - halfWidth) / halfWidth;
                    var distance = MathF.Sqrt((dx * dx + dy * dy) * 0.5f);
                    var t = Math.Clamp((distance - 0.35f) / 0.65f, 0f, 1f);
                    var dark = 1 - recipe.Vignette * t * t * (3 - 2 * t);
                    r *= dark;
                    g *= dark;
                    b *= dark;
                }

                // The grain: the same on the three colours, most in the middle tones
                if (recipe.Grain > 0)
                {
                    var middle = 1 - Math.Abs(2 * (0.2126f * r + 0.7152f * g + 0.0722f * b) - 1) * 0.6f;
                    var grain = Noise(x, y, seed) * recipe.Grain * middle;
                    r += grain;
                    g += grain;
                    b += grain;
                }

                pixels[row + x] = Pack(r, g, b);
            }
        }

        /// <summary>A number from -1 to 1 that is the same for the same place and seed, and unrelated to those of the places round it.</summary>
        internal static float Noise(int x, int y, int seed)
        {
            var h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * -2048144777));
            h = unchecked((h ^ (h >> 13)) * 1274126177u);
            h ^= h >> 16;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }

        private static uint Pack(float r, float g, float b) =>
            0xFF000000u | ((uint)Math.Clamp((int)(r * 255f + 0.5f), 0, 255) << 16) | ((uint)Math.Clamp((int)(g * 255f + 0.5f), 0, 255) << 8) | (uint)Math.Clamp((int)(b * 255f + 0.5f), 0, 255);
    }
}
