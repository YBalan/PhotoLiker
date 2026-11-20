namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The border <see cref="PhotoLiker.Core.SlideshowBorder.Film"/>, made on the pixels of a picture (0xAARRGGBB, row after row) that is already the size of the frame: the photo is made smaller (<see cref="Share"/> of the
    /// picture each way, so it keeps its shape) and put on a length of 35 mm film: black above and below it, with a row of rounded sprocket holes (light, as the light behind the film shows through them) in each band. The date is
    /// stamped in orange in the lower right corner of the photo (the writing is the window's: this is only the film and where the date goes, <see cref="DateArea"/>).
    /// </summary>
    public static class FilmStrip
    {
        /// <summary>The share of the width and of the height of the picture the photo takes.</summary>
        public const float Share = 0.80f;

        /// <summary>The film base: black with a hint of brown.</summary>
        public const uint FilmColour = 0xFF14110F;

        /// <summary>The light that shows through a sprocket hole.</summary>
        public const uint HoleColour = 0xFFE6E1D3;

        /// <summary>Where the photo is in the picture, as shares of its width and height (left, top, width, height): what the faces and the drift of the photo are worked out in.</summary>
        public static (float X, float Y, float Width, float Height) Inner => ((1 - Share) / 2, (1 - Share) / 2, Share, Share);

        /// <summary>Where the date goes: the lower right corner of the photo, as shares of the picture (left, top, width, height); the writing is flush right.</summary>
        public static (float X, float Y, float Width, float Height) DateArea =>
            ((1 - Share) / 2 + Share * 0.50f, (1 - Share) / 2 + Share * 0.865f, Share * 0.47f, Share * 0.10f);

        /// <summary>Puts the picture on the film, in place (the film is the whole of the picture's size).</summary>
        public static void Apply(uint[] pixels, int width, int height)
        {
            BorderPaint.Check(pixels, width, height);
            var place = BorderPaint.Place(width, height, Inner);
            var small = BorderPaint.Shrink(pixels, width, height, place.Width, place.Height);

            Array.Fill(pixels, FilmColour, 0, width * height);
            BorderPaint.Paste(pixels, width, small, place);

            Sprockets(pixels, width, 0, place.Top);
            Sprockets(pixels, width, place.Top + place.Height, height);
        }

        /// <summary>One band of the film (the rows from <paramref name="from"/> up to <paramref name="to"/>): holes along it, as many as fit, evenly spaced, in the middle of the band.</summary>
        private static void Sprockets(uint[] pixels, int width, int from, int to)
        {
            var band = to - from;
            if (band < 6)
                return;

            var holeHeight = Math.Max(2, (int)Math.Round(band * 0.46));
            var holeWidth = Math.Max(2, (int)Math.Round(holeHeight * 1.45));
            var count = Math.Max(1, (int)Math.Round(width / (holeWidth * 2.0)));
            var pitch = width / (double)count;
            holeWidth = Math.Max(2, Math.Min(holeWidth, (int)(pitch * 0.7)));

            var centreY = from + band / 2.0;
            var radius = holeHeight * 0.3;
            for (var i = 0; i < count; i++)
            {
                var centreX = (i + 0.5) * pitch;
                var y0 = Math.Max(from, (int)Math.Floor(centreY - holeHeight / 2.0) - 1);
                var y1 = Math.Min(to - 1, (int)Math.Ceiling(centreY + holeHeight / 2.0) + 1);
                var x0 = Math.Max(0, (int)Math.Floor(centreX - holeWidth / 2.0) - 1);
                var x1 = Math.Min(width - 1, (int)Math.Ceiling(centreX + holeWidth / 2.0) + 1);
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var coverage = BorderPaint.Coverage(BorderPaint.BoxDistance(x + 0.5, y + 0.5, centreX, centreY, holeWidth / 2.0, holeHeight / 2.0, radius));
                    if (coverage > 0)
                        pixels[y * width + x] = BorderPaint.Mix(pixels[y * width + x], HoleColour, coverage);
                }
            }
        }
    }
}
