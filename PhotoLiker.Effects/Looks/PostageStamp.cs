namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The border <see cref="PhotoLiker.Core.SlideshowBorder.Stamp"/>, made on the pixels of a picture (0xAARRGGBB, row after row) that is already the size of the frame: the photo is made smaller (<see cref="Share"/> of the
    /// picture each way, so it keeps its shape) and put on cream paper with a fine line round it, and the edge of the paper is perforated: round notches (the black of the screen shows through them) all along it, except at
    /// the corners. The margin below the photo is wider; the date goes there (the writing is the window's: this is only the stamp and where the date goes, <see cref="DateArea"/>).
    /// </summary>
    public static class PostageStamp
    {
        /// <summary>The share of the width and of the height of the picture the photo takes.</summary>
        public const float Share = 0.86f;

        /// <summary>The margin above the photo, as a share of the height of the picture (the one below is 0.10).</summary>
        public const float TopMargin = 0.04f;

        /// <summary>The paper: cream.</summary>
        public const uint PaperColour = 0xFFF6F2E7;

        /// <summary>The fine line round the photo.</summary>
        private const uint LineColour = 0xFF8E8678;

        /// <summary>Where the photo is in the picture, as shares of its width and height (left, top, width, height): what the faces and the drift of the photo are worked out in.</summary>
        public static (float X, float Y, float Width, float Height) Inner => ((1 - Share) / 2, TopMargin, Share, Share);

        /// <summary>Where the date goes: the margin below the photo, as shares of the picture (left, top, width, height); the writing is centred.</summary>
        public static (float X, float Y, float Width, float Height) DateArea => ((1 - Share) / 2, TopMargin + Share + 0.016f, Share, 1 - TopMargin - Share - 0.016f - 0.012f);

        /// <summary>Puts the picture on the stamp, in place (the stamp is the whole of the picture's size).</summary>
        public static void Apply(uint[] pixels, int width, int height)
        {
            BorderPaint.Check(pixels, width, height);
            var place = BorderPaint.Place(width, height, Inner);
            var small = BorderPaint.Shrink(pixels, width, height, place.Width, place.Height);

            Array.Fill(pixels, PaperColour, 0, width * height);
            BorderPaint.Paste(pixels, width, small, place);

            var shortSide = Math.Min(width, height);
            var right = place.Left + place.Width - 1;
            var bottom = place.Top + place.Height - 1;

            // The fine line round the photo, a little way out
            var gap = Math.Max(1, (int)Math.Round(shortSide * 0.007));
            Line(pixels, width, height, place.Left - gap, place.Top - gap, right + gap, place.Top - gap);
            Line(pixels, width, height, place.Left - gap, bottom + gap, right + gap, bottom + gap);
            Line(pixels, width, height, place.Left - gap, place.Top - gap, place.Left - gap, bottom + gap);
            Line(pixels, width, height, right + gap, place.Top - gap, right + gap, bottom + gap);

            // The perforation: half-round notches centred on the edge of the paper
            var margin = Math.Min(Math.Min(place.Left, place.Top), Math.Min(width - 1 - right, height - 1 - bottom));
            var radius = Math.Min(shortSide * 0.013, margin * 0.45);
            if (radius < 1.2)
                return;

            var pitch = radius * 2.9;
            var across = Math.Max(2, (int)Math.Round(width / pitch));
            for (var i = 1; i < across; i++)
            {
                Notch(pixels, width, height, i * (width / (double)across), 0, radius);
                Notch(pixels, width, height, i * (width / (double)across), height, radius);
            }
            var down = Math.Max(2, (int)Math.Round(height / pitch));
            for (var i = 1; i < down; i++)
            {
                Notch(pixels, width, height, 0, i * (height / (double)down), radius);
                Notch(pixels, width, height, width, i * (height / (double)down), radius);
            }
        }

        /// <summary>A line one pixel wide along a row or a column (from one end to the other, both included), as far as it is in the picture.</summary>
        private static void Line(uint[] pixels, int width, int height, int x0, int y0, int x1, int y1)
        {
            for (var y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
            for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                pixels[y * width + x] = LineColour;
        }

        /// <summary>A round hole in the paper (black behind it), centred on a point of the edge; its rim is smooth.</summary>
        private static void Notch(uint[] pixels, int width, int height, double centreX, double centreY, double radius)
        {
            var y0 = Math.Max(0, (int)Math.Floor(centreY - radius) - 1);
            var y1 = Math.Min(height - 1, (int)Math.Ceiling(centreY + radius) + 1);
            var x0 = Math.Max(0, (int)Math.Floor(centreX - radius) - 1);
            var x1 = Math.Min(width - 1, (int)Math.Ceiling(centreX + radius) + 1);
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var distance = Math.Sqrt((x + 0.5 - centreX) * (x + 0.5 - centreX) + (y + 0.5 - centreY) * (y + 0.5 - centreY));
                var coverage = BorderPaint.Coverage(distance - radius);
                if (coverage > 0)
                    pixels[y * width + x] = BorderPaint.Mix(pixels[y * width + x], 0xFF000000, coverage);
            }
        }
    }
}
