namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// What the borders (<see cref="Borders"/>) paint with, on the pixels of a picture (0xAARRGGBB, row after row; the picture is already the size of the frame, and every pixel stays opaque): where the photo goes, the photo
    /// made smaller to go there, colours mixed and shaded, and the distance of a point to a rounded box (for soft edges and shadows).
    /// </summary>
    internal static class BorderPaint
    {
        /// <summary>Refuses a picture that is not one: no pixels, no size, or fewer pixels than the size says.</summary>
        public static void Check(uint[] pixels, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height, nameof(pixels));
        }

        /// <summary>Where the photo goes in a picture of this size, in pixels, for the share of it a border keeps for the photo (the same share each way, so that the photo keeps its shape).</summary>
        public static (int Left, int Top, int Width, int Height) Place(int width, int height, (float X, float Y, float Width, float Height) inner)
        {
            var photoWidth = Math.Max(1, (int)Math.Round(width * inner.Width));
            var photoHeight = Math.Max(1, (int)Math.Round(height * inner.Height));
            var left = Math.Clamp((int)Math.Round(width * inner.X), 0, Math.Max(0, width - photoWidth));
            var top = Math.Clamp((int)Math.Round(height * inner.Y), 0, Math.Max(0, height - photoHeight));
            return (left, top, photoWidth, photoHeight);
        }

        /// <summary>The picture made smaller, each point the average of the part of the picture it covers (a box), so that fine detail does not alias.</summary>
        public static uint[] Shrink(uint[] source, int sourceWidth, int sourceHeight, int width, int height)
        {
            var target = new uint[width * height];
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
            return target;
        }

        /// <summary>Puts the (smaller) photo in its place.</summary>
        public static void Paste(uint[] pixels, int width, uint[] photo, (int Left, int Top, int Width, int Height) place)
        {
            for (var y = 0; y < place.Height; y++)
                Array.Copy(photo, y * place.Width, pixels, (place.Top + y) * width + place.Left, place.Width);
        }

        public static uint Rgb(double r, double g, double b) =>
            0xFF000000u | (uint)Math.Clamp((int)Math.Round(r), 0, 255) << 16 | (uint)Math.Clamp((int)Math.Round(g), 0, 255) << 8 | (uint)Math.Clamp((int)Math.Round(b), 0, 255);

        /// <summary>The colour that is <paramref name="amount"/> (0 to 1) of the way from <paramref name="under"/> to <paramref name="over"/>.</summary>
        public static uint Mix(uint under, uint over, double amount)
        {
            if (amount <= 0)
                return under | 0xFF000000u;
            if (amount >= 1)
                return over | 0xFF000000u;

            return Rgb(
                Channel(under, 16) + (Channel(over, 16) - Channel(under, 16)) * amount,
                Channel(under, 8) + (Channel(over, 8) - Channel(under, 8)) * amount,
                Channel(under, 0) + (Channel(over, 0) - Channel(under, 0)) * amount);
        }

        /// <summary>The colour made brighter (<paramref name="factor"/> above 1) or darker.</summary>
        public static uint Scale(uint colour, double factor) => Rgb(Channel(colour, 16) * factor, Channel(colour, 8) * factor, Channel(colour, 0) * factor);

        private static double Channel(uint colour, int shift) => (colour >> shift) & 0xFF;

        /// <summary>How much of a pixel lies inside an edge, from the distance of its middle to the edge (negative inside): 1 well inside, 0 well outside, a ramp one pixel wide across it.</summary>
        public static double Coverage(double distance) => Math.Clamp(0.5 - distance, 0, 1);

        /// <summary>0 up to <paramref name="from"/>, 1 from <paramref name="to"/>, a smooth step between.</summary>
        public static double Smooth(double from, double to, double x)
        {
            var t = Math.Clamp((x - from) / (to - from), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>The distance of a point to the edge of a box with rounded corners: negative inside, positive outside.</summary>
        public static double BoxDistance(double x, double y, double centreX, double centreY, double halfWidth, double halfHeight, double radius)
        {
            var qx = Math.Abs(x - centreX) - (halfWidth - radius);
            var qy = Math.Abs(y - centreY) - (halfHeight - radius);
            var outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
            return outside + Math.Min(Math.Max(qx, qy), 0) - radius;
        }

        /// <summary>A number from 0 to 255 that is the same for the same point, and uncorrelated with the numbers of the points next to it: grain.</summary>
        public static int Noise(int x, int y)
        {
            unchecked
            {
                var h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)((h ^ (h >> 16)) & 0xFF);
            }
        }
    }
}
