namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The border <see cref="PhotoLiker.Core.SlideshowBorder.Frame"/>, made on the pixels of a picture (0xAARRGGBB, row after row) that is already the size of the frame: the photo is made smaller (<see cref="Share"/> of the
    /// picture each way, so it keeps its shape), and round it are a cream mat with a bevelled opening and a gilt moulding with mitred corners (lit from the top left, so its top and left are bright and its bottom and
    /// right dark), and the photo has a little shadow along its top and left edge, as if it lay behind the mat. The photo that is put in the frame is what it was: the grading of its colours is done before.
    /// </summary>
    public static class PictureFrame
    {
        /// <summary>The share of the width and of the height of the picture the photo takes.</summary>
        public const float Share = 0.78f;

        /// <summary>The thickness of the moulding, as a share of the shorter side of the picture.</summary>
        public const float MouldingShare = 0.045f;

        /// <summary>The mat: a warm cream.</summary>
        public const uint MatColour = 0xFFEFE9DA;

        /// <summary>The cut edge of the mat round the photo, which the light catches.</summary>
        private const uint BevelColour = 0xFFFBF9F2;

        /// <summary>The line where the bevel meets the mat.</summary>
        private const uint BevelEdgeColour = 0xFFCFC6B1;

        /// <summary>Where the photo is in the picture, as shares of its width and height (left, top, width, height): what the faces and the drift of the photo are worked out in.</summary>
        public static (float X, float Y, float Width, float Height) Inner => ((1 - Share) / 2, (1 - Share) / 2, Share, Share);

        /// <summary>Puts the picture in the frame, in place (the frame is the whole of the picture's size).</summary>
        public static void Apply(uint[] pixels, int width, int height)
        {
            BorderPaint.Check(pixels, width, height);
            var place = BorderPaint.Place(width, height, Inner);
            var small = BorderPaint.Shrink(pixels, width, height, place.Width, place.Height);

            var shortSide = Math.Min(width, height);
            var right = place.Left + place.Width - 1;
            var bottom = place.Top + place.Height - 1;
            var margin = Math.Min(Math.Min(place.Left, place.Top), Math.Min(width - 1 - right, height - 1 - bottom));
            var moulding = Math.Clamp((int)Math.Round(shortSide * MouldingShare), 0, margin);
            var matRoom = margin - moulding;
            var bevel = matRoom >= 4 ? Math.Min(Math.Max(1, (int)Math.Round(shortSide * 0.006)), matRoom / 2) : 0;
            var lip = Math.Max(1, (int)Math.Round(shortSide * 0.012)); // the shadow the moulding throws on the mat

            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var d = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                    if (d < moulding)
                    {
                        pixels[y * width + x] = Moulding(x, y, width, height, moulding);
                        continue;
                    }

                    var mat = MatColour;
                    var fromMoulding = d - moulding;
                    if (fromMoulding < lip)
                        mat = BorderPaint.Scale(mat, 0.78 + 0.22 * fromMoulding / lip);

                    var outside = Math.Max(Math.Max(place.Left - x, x - right), Math.Max(place.Top - y, y - bottom)); // how far outside the photo (0 or less: inside)
                    if (outside >= 1 && outside <= bevel)
                        mat = outside == bevel ? BevelEdgeColour : BevelColour;

                    pixels[y * width + x] = mat;
                }
            });

            BorderPaint.Paste(pixels, width, small, place);

            // The mat lies in front of the photo: its top and left edge throw a shadow on it
            var depth = Math.Max(1, (int)Math.Round(shortSide * 0.018));
            for (var y = 0; y < Math.Min(depth, place.Height); y++)
            for (var x = 0; x < place.Width; x++)
                Darken(pixels, (place.Top + y) * width + place.Left + x, Math.Min(x, y), depth);
            for (var y = depth; y < place.Height; y++)
            for (var x = 0; x < Math.Min(depth, place.Width); x++)
                Darken(pixels, (place.Top + y) * width + place.Left + x, x, depth);
        }

        private static void Darken(uint[] pixels, int index, int distance, int depth)
        {
            var near = 1 - distance / (double)depth;
            pixels[index] = BorderPaint.Scale(pixels[index], 1 - 0.42 * near * near);
        }

        /// <summary>
        /// One point of the moulding: the distance to the nearest outer edge says where it is across (<c>u</c>, 0 at the outer edge, 1 at the inner), which edge it is nearest to says how it is lit (the corners are mitred at 45 degrees);
        /// across, it is a round ridge, bright in the middle and dark at both edges; with a little grain.
        /// </summary>
        private static uint Moulding(int x, int y, int width, int height, int thickness)
        {
            var left = x;
            var right = width - 1 - x;
            var top = y;
            var bottom = height - 1 - y;
            var d = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            var light = d == top ? 1.14 : d == left ? 1.0 : d == right ? 0.78 : 0.64;

            var u = (d + 0.5) / thickness;
            var ridge = 0.50 + 0.62 * Math.Pow(Math.Sin(Math.PI * u), 0.7);
            var grain = 1 + ((BorderPaint.Noise(x, y) & 15) - 7) / 7.0 * 0.025;
            var k = ridge * light * grain;
            return BorderPaint.Rgb(214 * k, 172 * k, 66 * k);
        }
    }
}
