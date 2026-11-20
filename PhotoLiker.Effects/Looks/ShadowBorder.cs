namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The border <see cref="PhotoLiker.Core.SlideshowBorder.Shadow"/>, made on the pixels of a picture (0xAARRGGBB, row after row) that is already the size of the frame: the photo is made smaller (<see cref="Share"/> of the
    /// picture each way, so it keeps its shape), has rounded corners, and lies on a sheet of light grey paper (a little darker towards the corners) with a soft shadow that falls down and to the right.
    /// </summary>
    public static class ShadowBorder
    {
        /// <summary>The share of the width and of the height of the picture the photo takes.</summary>
        public const float Share = 0.86f;

        /// <summary>The margin above the photo, as a share of the height of the picture (the one below is 0.08: the shadow falls there).</summary>
        public const float TopMargin = 0.06f;

        /// <summary>The paper, in the middle of the picture.</summary>
        public const uint PaperColour = 0xFFE9E6E0;

        private const uint ShadowColour = 0xFF1A1820;

        /// <summary>Where the photo is in the picture, as shares of its width and height (left, top, width, height): what the faces and the drift of the photo are worked out in.</summary>
        public static (float X, float Y, float Width, float Height) Inner => ((1 - Share) / 2, TopMargin, Share, Share);

        /// <summary>Puts the picture on the paper, in place (the paper is the whole of the picture's size).</summary>
        public static void Apply(uint[] pixels, int width, int height)
        {
            BorderPaint.Check(pixels, width, height);
            var place = BorderPaint.Place(width, height, Inner);
            var small = BorderPaint.Shrink(pixels, width, height, place.Width, place.Height);

            var shortSide = Math.Min(width, height);
            var radius = Math.Max(1.0, Math.Min(place.Width, place.Height) * 0.018);
            var sigma = Math.Max(1.0, shortSide * 0.014);
            var offsetX = shortSide * 0.006;
            var offsetY = shortSide * 0.012;
            var centreX = place.Left + place.Width / 2.0;
            var centreY = place.Top + place.Height / 2.0;
            var halfWidth = place.Width / 2.0;
            var halfHeight = place.Height / 2.0;

            // The paper gets darker towards the corners: one colour for each step of how far from the middle, worked out once
            var paper = new uint[256];
            for (var i = 0; i < paper.Length; i++)
                paper[i] = BorderPaint.Scale(PaperColour, 1 - 0.10 * i / (paper.Length - 1));

            // The photo is simply copied where it is well inside its rounded corners; only its edge and its corners are worked out
            var cornerReach = (int)Math.Ceiling(radius) + 1;
            var right = place.Left + place.Width - 1;
            var bottom = place.Top + place.Height - 1;

            Parallel.For(0, height, y =>
            {
                var py = y + 0.5;
                var ny = py / height * 2 - 1;
                var rowInside = y > place.Top && y < bottom;
                var rowInCorner = y < place.Top + cornerReach || y > bottom - cornerReach;
                for (var x = 0; x < width; x++)
                {
                    if (rowInside && x > place.Left && x < right && !(rowInCorner && (x < place.Left + cornerReach || x > right - cornerReach)))
                    {
                        pixels[y * width + x] = small[(y - place.Top) * place.Width + (x - place.Left)];
                        continue;
                    }

                    var px = x + 0.5;
                    var nx = px / width * 2 - 1;
                    var colour = paper[Math.Clamp((int)((nx * nx + ny * ny) / 2 * (paper.Length - 1)), 0, paper.Length - 1)];

                    var shadow = BorderPaint.BoxDistance(px, py, centreX + offsetX, centreY + offsetY, halfWidth, halfHeight, radius);
                    if (shadow < 1.8 * sigma)
                        colour = BorderPaint.Mix(colour, ShadowColour, 0.5 * (1 - BorderPaint.Smooth(-sigma, 1.8 * sigma, shadow)));

                    var coverage = BorderPaint.Coverage(BorderPaint.BoxDistance(px, py, centreX, centreY, halfWidth, halfHeight, radius));
                    if (coverage > 0)
                    {
                        var photoX = Math.Clamp(x - place.Left, 0, place.Width - 1);
                        var photoY = Math.Clamp(y - place.Top, 0, place.Height - 1);
                        colour = BorderPaint.Mix(colour, small[photoY * place.Width + photoX], coverage);
                    }

                    pixels[y * width + x] = colour;
                }
            });
        }
    }
}
