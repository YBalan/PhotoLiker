namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The border <see cref="PhotoLiker.Core.SlideshowBorder.Glow"/>, made on the pixels of a picture (0xAARRGGBB, row after row) that is already the size of the frame: the photo is made smaller (<see cref="Share"/> of the
    /// picture each way, so it keeps its shape) and put on a dark ground, with a neon line along its edge and a glow that fades out from it, in the colour that the photo has most of (its hue, made vivid: a photo of the
    /// sea glows blue, one of a field green; a photo that has no colour glows a cool white-blue).
    /// </summary>
    public static class GlowBorder
    {
        /// <summary>The share of the width and of the height of the picture the photo takes.</summary>
        public const float Share = 0.86f;

        /// <summary>The ground: almost black, with a hint of blue.</summary>
        public const uint GroundColour = 0xFF07080C;

        /// <summary>What a photo with no colour in it glows with.</summary>
        public const uint NeutralGlow = 0xFF8FD3FF;

        /// <summary>Where the photo is in the picture, as shares of its width and height (left, top, width, height): what the faces and the drift of the photo are worked out in.</summary>
        public static (float X, float Y, float Width, float Height) Inner => ((1 - Share) / 2, (1 - Share) / 2, Share, Share);

        /// <summary>Puts the picture on the dark ground with its glow, in place (the ground is the whole of the picture's size).</summary>
        public static void Apply(uint[] pixels, int width, int height)
        {
            BorderPaint.Check(pixels, width, height);
            var glow = GlowOf(pixels, width, height); // from the whole photo, before it is made smaller and drawn over
            var place = BorderPaint.Place(width, height, Inner);
            var small = BorderPaint.Shrink(pixels, width, height, place.Width, place.Height);

            // The colour at each distance (in half pixels) from the edge of the photo, worked out once: a core that is nearly white, and a glow that dies away
            var sigma = Math.Max(1.5, Math.Min(width, height) * 0.020);
            var ramp = new uint[(int)Math.Ceiling(sigma * 4.5 * 2) + 2];
            for (var i = 0; i < ramp.Length; i++)
            {
                var d = i / 2.0;
                var strength = 0.80 * Math.Exp(-d / sigma) + 0.55 * Math.Exp(-d / (0.22 * sigma));
                var colour = BorderPaint.Rgb(
                    ((GroundColour >> 16) & 0xFF) + ((glow >> 16) & 0xFF) * strength,
                    ((GroundColour >> 8) & 0xFF) + ((glow >> 8) & 0xFF) * strength,
                    (GroundColour & 0xFF) + (glow & 0xFF) * strength);
                ramp[i] = BorderPaint.Mix(colour, 0xFFFFFFFF, 0.55 * Math.Exp(-d / (0.15 * sigma)));
            }

            var right = place.Left + place.Width - 1;
            var bottom = place.Top + place.Height - 1;
            Parallel.For(0, height, y =>
            {
                var dy = Math.Max(Math.Max(place.Top - y, y - bottom), 0);
                for (var x = 0; x < width; x++)
                {
                    var dx = Math.Max(Math.Max(place.Left - x, x - right), 0);
                    var distance = Math.Sqrt(dx * dx + dy * dy) - 1; // the pixel next to the photo is at 0
                    var index = distance <= 0 ? 0 : (int)(distance * 2);
                    pixels[y * width + x] = index < ramp.Length ? ramp[index] : GroundColour;
                }
            });

            BorderPaint.Paste(pixels, width, small, place);
        }

        /// <summary>
        /// The colour a photo glows with: the hue most of it has (the hues of its points, each weighted by how vivid and how bright the point is, averaged round the colour wheel), at full brightness and strong colour. A photo that is
        /// nearly all grey, white or black has <see cref="NeutralGlow"/>.
        /// </summary>
        public static uint GlowOf(uint[] pixels, int width, int height)
        {
            BorderPaint.Check(pixels, width, height);
            var step = Math.Max(1, (int)Math.Sqrt(width * (double)height / 4096));
            double sumX = 0, sumY = 0, weight = 0;
            var count = 0;
            for (var y = 0; y < height; y += step)
            for (var x = 0; x < width; x += step)
            {
                var pixel = pixels[y * width + x];
                double r = (pixel >> 16) & 0xFF, g = (pixel >> 8) & 0xFF, b = pixel & 0xFF;
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                count++;
                var delta = max - min;
                if (delta < 1)
                    continue;

                var hue = Hue(r, g, b, max, delta);
                var strength = delta / 255.0 * (max / 255.0); // vivid and bright counts, a dim or washed-out point hardly
                sumX += Math.Cos(hue) * strength;
                sumY += Math.Sin(hue) * strength;
                weight += strength;
            }

            if (count == 0 || weight / count < 0.04)
                return NeutralGlow;

            return FromHue(Math.Atan2(sumY, sumX));
        }

        /// <summary>The hue of a colour in radians (0 is red, going through yellow and green), from its channels (0 to 255), the largest of them and the difference between the largest and the smallest.</summary>
        private static double Hue(double r, double g, double b, double max, double delta)
        {
            double sector;
            if (max == r)
                sector = ((g - b) / delta + 6) % 6;
            else if (max == g)
                sector = (b - r) / delta + 2;
            else
                sector = (r - g) / delta + 4;
            return sector * Math.PI / 3;
        }

        /// <summary>A vivid colour (strong colour, full brightness) of the hue, in radians.</summary>
        private static uint FromHue(double hue)
        {
            const double saturation = 0.80;
            var sector = ((hue / (Math.PI / 3)) % 6 + 6) % 6;
            var whole = (int)Math.Floor(sector);
            var fraction = sector - whole;
            var low = 255 * (1 - saturation);
            var falling = 255 * (1 - saturation * fraction);
            var rising = 255 * (1 - saturation * (1 - fraction));
            return whole switch
            {
                0 => BorderPaint.Rgb(255, rising, low),
                1 => BorderPaint.Rgb(falling, 255, low),
                2 => BorderPaint.Rgb(low, 255, rising),
                3 => BorderPaint.Rgb(low, falling, 255),
                4 => BorderPaint.Rgb(rising, low, 255),
                _ => BorderPaint.Rgb(255, low, falling),
            };
        }
    }
}
