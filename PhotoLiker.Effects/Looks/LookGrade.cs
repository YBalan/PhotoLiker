using PhotoLiker.Core;
using PhotoLiker.Effects.Compositing;

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
            float BlackLift, float WhiteDrop, float Vignette, float Grain, bool Sepia = false, bool Dust = false)
        {
            /// <summary>The recipe of a look; <paramref name="taken"/> is the date of the photo, which <see cref="SlideshowLook.Season"/> goes by (no date, no recipe).</summary>
            public static Recipe? Of(SlideshowLook look, DateTime? taken = null) => look switch
            {
                SlideshowLook.Vintage => new Recipe(0.78f, 0.95f, (0.025f, 0.010f, -0.020f), (0.040f, 0.020f, -0.045f), 0.07f, 0.06f, 0.30f, 0.035f),
                SlideshowLook.Film => new Recipe(0.92f, 1.10f, (-0.025f, 0.010f, 0.035f), (0.030f, 0.015f, -0.020f), 0.02f, 0.02f, 0.16f, 0.050f),
                SlideshowLook.BlackAndWhite => new Recipe(0f, 1.12f, (0, 0, 0), (0, 0, 0), 0.02f, 0.02f, 0.14f, 0.035f),
                SlideshowLook.Cinema => new Recipe(1.08f, 1.15f, (-0.070f, 0.010f, 0.060f), (0.070f, 0.030f, -0.070f), 0.01f, 0.01f, 0.18f, 0.015f),
                SlideshowLook.Sepia => new Recipe(0f, 1.05f, (0, 0, 0), (0, 0, 0), 0f, 0f, 0.12f, 0.02f, Sepia: true),
                SlideshowLook.Vignette => new Recipe(1f, 1f, (0, 0, 0), (0, 0, 0), 0f, 0f, 0.45f, 0f),
                SlideshowLook.Grain => new Recipe(1f, 1f, (0, 0, 0), (0, 0, 0), 0f, 0f, 0f, 0.07f),
                SlideshowLook.OldPhoto => new Recipe(0f, 0.95f, (0, 0, 0), (0, 0, 0), 0.09f, 0.07f, 0.42f, 0.06f, Sepia: true, Dust: true),
                SlideshowLook.Season when taken is { } date => OfSeason(SeasonOf(date)),
                _ => null,
            };

            private static Recipe OfSeason(Season season) => season switch
            {
                Season.Winter => new Recipe(0.82f, 1.05f, (-0.035f, 0.005f, 0.055f), (-0.015f, 0.012f, 0.050f), 0.02f, 0.01f, 0.10f, 0f),
                Season.Spring => new Recipe(1.10f, 1.03f, (-0.005f, 0.012f, 0.005f), (0.000f, 0.025f, -0.005f), 0.01f, 0.00f, 0.08f, 0f),
                Season.Summer => new Recipe(1.15f, 1.06f, (0.010f, 0.000f, -0.010f), (0.060f, 0.025f, -0.050f), 0.00f, 0.01f, 0.08f, 0f),
                _ => new Recipe(1.12f, 1.08f, (0.040f, 0.012f, -0.025f), (0.075f, 0.030f, -0.075f), 0.01f, 0.01f, 0.12f, 0f),
            };
        }

        /// <summary>The seasons of the year, by the months of the northern hemisphere (the meteorological ones: December to February is winter).</summary>
        public enum Season { Winter, Spring, Summer, Autumn }

        /// <summary>The season of a date.</summary>
        public static Season SeasonOf(DateTime date) => date.Month switch
        {
            12 or 1 or 2 => Season.Winter,
            3 or 4 or 5 => Season.Spring,
            6 or 7 or 8 => Season.Summer,
            _ => Season.Autumn,
        };

        /// <summary>
        /// The looks that a random choice picks from (see <see cref="Random"/>): every look but <see cref="SlideshowLook.Lut"/>, which needs a table (it is one of them when there is one), and
        /// <see cref="SlideshowLook.None"/> is one of them as well, so some photos stay as they are.
        /// </summary>
        public static IReadOnlyList<SlideshowLook> RandomChoices(bool hasTable) =>
            [.. Enum.GetValues<SlideshowLook>().Where(look => look != SlideshowLook.Lut || hasTable)];

        /// <summary>
        /// A look chosen by chance for the photo with this <paramref name="key"/>: the same key and <paramref name="seed"/> always give the same look (a photo that is made again, or seen again, has the one it had), another
        /// key gives another by chance. Every look of <see cref="RandomChoices"/> is as likely as the others.
        /// </summary>
        public static SlideshowLook Random(string key, int seed, bool hasTable)
        {
            var choices = RandomChoices(hasTable);
            return choices[(int)(Mix(key, seed) % (uint)choices.Count)];
        }

        /// <summary>A number made of a text and a seed, the same for the same ones on every run (a string's own hash code is not: it changes from one run of the program to the next), well spread.</summary>
        internal static uint Mix(string key, int seed)
        {
            unchecked
            {
                var h = 2166136261u ^ (uint)seed;
                foreach (var c in key)
                    h = (h ^ c) * 16777619u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
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
        /// <param name="seed">What the grain (and the scratches of <see cref="SlideshowLook.OldPhoto"/>) is made from: the same seed gives the same grain (the same grain on a photo each time it is shown), another gives another.</param>
        /// <param name="taken">When the photo was taken, for <see cref="SlideshowLook.Season"/>: a photo with no date is left as it is by that look.</param>
        public static void Apply(uint[] pixels, int width, int height, SlideshowLook look, CubeLut? lut = null, int seed = 0, DateTime? taken = null)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height, nameof(pixels));

            if (look == SlideshowLook.None || (look == SlideshowLook.Lut && lut is null))
                return;

            var recipe = look == SlideshowLook.Lut ? null : Recipe.Of(look, taken);
            if (recipe is null && look != SlideshowLook.Lut)
                return; // a look that has no recipe (the season of a photo that has no date)

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

            if (recipe is { Dust: true })
                AddDust(pixels, width, height, seed);
        }

        /// <summary>
        /// The marks of a photo that has been in a drawer: a few thin scratches that run down (light or dark, not quite straight) and specks of dust (small light or dark spots), at places that come from
        /// <paramref name="seed"/>, so the same photo has the same ones every time. Their size goes with the height of the picture.
        /// </summary>
        private static void AddDust(uint[] pixels, int width, int height, int seed)
        {
            var state = (uint)unchecked(seed * 747796405 + 2891336453);
            if (state == 0)
                state = 1;

            uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }

            float Unit() => (Next() & 0xFFFFFF) / 16777216f;

            var size = Math.Max(1, height / 900);
            var scratches = 2 + (int)(Unit() * 3);
            for (var s = 0; s < scratches; s++)
            {
                var x = Unit() * width;
                var top = (int)(Unit() * height * 0.6f);
                var bottom = Math.Min(height, top + (int)(height * (0.15f + Unit() * 0.4f)));
                var colour = Unit() < 0.7f ? 0xFFF4EBD8u : 0xFF1A130Eu;
                for (var y = top; y < bottom; y++)
                {
                    x = Math.Clamp(x + (Unit() - 0.5f) * 0.6f, 0, width - 1);
                    for (var k = 0; k < size; k++)
                        Dab(pixels, width, (int)x + k, y, colour, 90);
                }
            }

            var specks = 30 + (int)(Unit() * 30);
            for (var s = 0; s < specks; s++)
            {
                int cx = (int)(Unit() * width), cy = (int)(Unit() * height);
                var radius = size * (1 + (int)(Unit() * 2.5f));
                var colour = Unit() < 0.5f ? 0xFFF4EBD8u : 0xFF1A130Eu;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (dx * dx + dy * dy <= radius * radius)
                            Dab(pixels, width, cx + dx, cy + dy, colour, 120);
                    }
                }
            }

            void Dab(uint[] target, int stride, int x, int y, uint colour, uint share)
            {
                if (x < 0 || x >= stride || y < 0 || y >= height)
                    return;

                target[y * stride + x] = FrameCompositor.Lerp(target[y * stride + x], colour, share);
            }
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
