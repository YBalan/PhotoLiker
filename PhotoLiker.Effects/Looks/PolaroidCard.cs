namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// The card of <see cref="PhotoLiker.Core.SlideshowBorder.Polaroid"/>, made on the pixels of a picture (0xAARRGGBB, row after row) that is already the size of the card: the picture is made smaller (<see cref="PhotoShare"/>
    /// of the card each way, so it keeps its shape) and put on a warm white card with the margin of an instant print: narrow above, a little wider at the sides, widest below, where the date is written
    /// (the writing is the window's: this is only the card and where the date goes, <see cref="DateArea"/>). The photo that is put on the card is what it was: the grading of its colours is done before.
    /// </summary>
    public static class PolaroidCard
    {
        /// <summary>The share of the width and of the height of the card the photo takes.</summary>
        public const float PhotoShare = 0.84f;

        /// <summary>The margin above the photo, as a share of the height of the card.</summary>
        public const float TopMargin = 0.04f;

        /// <summary>The card: a warm white.</summary>
        public const uint CardColour = 0xFFF3EFE6;

        /// <summary>The thin edge of the photo, a little darker than the card, so that a light sky does not run into it.</summary>
        private const uint EdgeColour = 0xFFD9D4C8;

        /// <summary>Where the photo is on the card, as shares of the width and the height of the card (left, top, width, height): what the faces and the drift of the photo are worked out in.</summary>
        public static (float X, float Y, float Width, float Height) Inner => ((1 - PhotoShare) / 2, TopMargin, PhotoShare, PhotoShare);

        /// <summary>Where the date goes: the margin below the photo, as shares of the card (left, top, width, height).</summary>
        public static (float X, float Y, float Width, float Height) DateArea => ((1 - PhotoShare) / 2, TopMargin + PhotoShare, PhotoShare, 1 - TopMargin - PhotoShare);

        /// <summary>Puts the picture on the card, in place (the card is the whole of the picture's size).</summary>
        public static void Apply(uint[] pixels, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height, nameof(pixels));

            var photoWidth = Math.Max(1, (int)Math.Round(width * PhotoShare));
            var photoHeight = Math.Max(1, (int)Math.Round(height * PhotoShare));
            var left = Math.Clamp((int)Math.Round(width * (1 - PhotoShare) / 2), 0, width - photoWidth);
            var top = Math.Clamp((int)Math.Round(height * TopMargin), 0, height - photoHeight);

            // The picture made smaller first, into a picture of its own: it is read from where the card is written
            var small = new uint[photoWidth * photoHeight];
            Resample(pixels, width, height, small, photoWidth, photoHeight);

            Array.Fill(pixels, CardColour, 0, width * height);
            for (var y = 0; y < photoHeight; y++)
                Array.Copy(small, y * photoWidth, pixels, (top + y) * width + left, photoWidth);

            // The edge: a line round the photo
            for (var x = left; x < left + photoWidth; x++)
            {
                if (top > 0)
                    pixels[(top - 1) * width + x] = EdgeColour;
                if (top + photoHeight < height)
                    pixels[(top + photoHeight) * width + x] = EdgeColour;
            }
            for (var y = top; y < top + photoHeight; y++)
            {
                if (left > 0)
                    pixels[y * width + left - 1] = EdgeColour;
                if (left + photoWidth < width)
                    pixels[y * width + left + photoWidth] = EdgeColour;
            }
        }

        /// <summary>The picture made smaller, each point the average of the part of the picture it covers (a box), so that fine detail does not alias.</summary>
        private static void Resample(uint[] source, int sourceWidth, int sourceHeight, uint[] target, int width, int height)
        {
            var scaleX = sourceWidth / (double)width;
            var scaleY = sourceHeight / (double)height;
            Parallel.For(0, height, y =>
            {
                var y0 = Math.Clamp((int)Math.Floor(y * scaleY), 0, sourceHeight - 1);
                var y1 = Math.Clamp((int)Math.Ceiling((y + 1) * scaleY), y0 + 1, sourceHeight);
                for (var x = 0; x < width; x++)
                {
                    var x0 = Math.Clamp((int)Math.Floor(x * scaleX), 0, sourceWidth - 1);
                    var x1 = Math.Clamp((int)Math.Ceiling((x + 1) * scaleX), x0 + 1, sourceWidth);
                    long r = 0, g = 0, b = 0;
                    for (var yy = y0; yy < y1; yy++)
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var pixel = source[yy * sourceWidth + xx];
                        r += (pixel >> 16) & 0xFF;
                        g += (pixel >> 8) & 0xFF;
                        b += pixel & 0xFF;
                    }
                    var count = (y1 - y0) * (x1 - x0);
                    target[y * width + x] = 0xFF000000u | (uint)(r / count) << 16 | (uint)(g / count) << 8 | (uint)(b / count);
                }
            });
        }
    }
}
