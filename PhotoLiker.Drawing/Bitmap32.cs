using System.Drawing;

namespace PhotoLiker.Drawing
{
    /// <summary>
    /// A picture as plain pixels: four bytes for each (blue, green, red, alpha; the colours are not multiplied by the alpha), row after row with no gap between the rows. The same order as
    /// the 32 bit format of Windows, so a front end can copy it over as it is.
    /// </summary>
    public sealed class Bitmap32
    {
        public int Width { get; }

        public int Height { get; }

        /// <summary>The pixels: <c>(y * Width + x) * 4</c> is the blue of the pixel at x, y.</summary>
        public byte[] Bgra { get; }

        public Bitmap32(int width, int height, byte[] bgra)
        {
            ArgumentNullException.ThrowIfNull(bgra);
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "A picture has at least one pixel.");
            if (bgra.Length != checked(width * height * 4))
                throw new ArgumentException($"A picture of {width} by {height} has {width * height * 4} bytes, not {bgra.Length}.", nameof(bgra));

            Width = width;
            Height = height;
            Bgra = bgra;
        }

        /// <summary>A picture of one colour.</summary>
        public static Bitmap32 Filled(int width, int height, Color colour)
        {
            var pixels = new byte[checked(width * height * 4)];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = colour.B;
                pixels[i + 1] = colour.G;
                pixels[i + 2] = colour.R;
                pixels[i + 3] = colour.A;
            }
            return new Bitmap32(width, height, pixels);
        }

        public Bitmap32 Clone() => new(Width, Height, (byte[])Bgra.Clone());

        public Rectangle Bounds => new(0, 0, Width, Height);

        /// <summary>The colour of the pixel at x, y.</summary>
        public Color At(int x, int y)
        {
            var i = (y * Width + x) * 4;
            return Color.FromArgb(Bgra[i + 3], Bgra[i + 2], Bgra[i + 1], Bgra[i]);
        }

        /// <summary>The part of the picture inside <paramref name="area"/> (cut to the picture) as a picture of its own.</summary>
        /// <exception cref="ArgumentException">Nothing of the area is inside the picture.</exception>
        public Bitmap32 Crop(Rectangle area)
        {
            var inside = Rectangle.Intersect(area, Bounds);
            if (inside.Width < 1 || inside.Height < 1)
                throw new ArgumentException("The area is not inside the picture.", nameof(area));

            var pixels = new byte[inside.Width * inside.Height * 4];
            for (var y = 0; y < inside.Height; y++)
                Buffer.BlockCopy(Bgra, ((inside.Y + y) * Width + inside.X) * 4, pixels, y * inside.Width * 4, inside.Width * 4);
            return new Bitmap32(inside.Width, inside.Height, pixels);
        }

        /// <summary>The picture turned clockwise by <paramref name="quarterTurns"/> quarters (negative: counterclockwise); a quarter makes a picture as wide as this one is high.</summary>
        public Bitmap32 Rotate(int quarterTurns)
        {
            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0)
                return Clone();

            var width = turns == 2 ? Width : Height;
            var height = turns == 2 ? Height : Width;
            var pixels = new byte[Bgra.Length];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    // Where the pixel at x, y goes: a quarter clockwise puts the left edge on the top, a half puts it on the right, three quarters put the left edge at the bottom
                    var (nx, ny) = turns switch
                    {
                        1 => (Height - 1 - y, x),
                        2 => (Width - 1 - x, Height - 1 - y),
                        _ => (y, Width - 1 - x),
                    };
                    Buffer.BlockCopy(Bgra, (y * Width + x) * 4, pixels, (ny * width + nx) * 4, 4);
                }
            }
            return new Bitmap32(width, height, pixels);
        }

        /// <summary>Puts <paramref name="colour"/> over the pixel at x, y by <paramref name="coverage"/> (0 to 1; the colour's own alpha counts as well), the way a brush that is not quite opaque does.</summary>
        internal void Blend(int x, int y, Color colour, float coverage)
        {
            var source = coverage * colour.A / 255f;
            if (source <= 0)
                return;

            var i = (y * Width + x) * 4;
            var below = Bgra[i + 3] / 255f;
            var alpha = source + below * (1 - source);
            if (alpha <= 0)
                return;

            // The colours of a picture that is not opaque are not multiplied by their alpha, so what is below counts as much as it shows
            var keep = below * (1 - source);
            Bgra[i] = ToByte((colour.B * source + Bgra[i] * keep) / alpha);
            Bgra[i + 1] = ToByte((colour.G * source + Bgra[i + 1] * keep) / alpha);
            Bgra[i + 2] = ToByte((colour.R * source + Bgra[i + 2] * keep) / alpha);
            Bgra[i + 3] = ToByte(alpha * 255f);
        }

        private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
    }
}
