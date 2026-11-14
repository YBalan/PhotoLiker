namespace PhotoLiker.Effects.Focus
{
    /// <summary>
    /// A blur that is quick on a processor: three passes of a moving average along the rows and three down the columns, which is as good as a Gaussian blur to the eye, at a cost that does not depend
    /// on how wide it is (each pass adds a pixel and takes one away). The pictures are 0xAARRGGBB pixels, row after row; the alpha of the result is opaque. The edge of the picture is repeated.
    /// </summary>
    internal static class BoxBlur
    {
        private const int Passes = 3;
        private const int ColumnsPerStripe = 64;

        /// <summary>A blurred copy of the picture; <paramref name="radius"/> is how many pixels each side a pass looks at (0 or less: a copy as it is).</summary>
        public static uint[] Apply(uint[] source, int width, int height, int radius)
        {
            var length = width * height;
            var first = new uint[length];
            Array.Copy(source, first, length);
            if (radius < 1)
                return first;

            radius = Math.Min(radius, 1000);
            var second = new uint[length];
            var from = first;
            var to = second;
            for (var pass = 0; pass < Passes; pass++)
            {
                AlongRows(from, to, width, height, radius);
                (from, to) = (to, from);
                AlongColumns(from, to, width, height, radius);
                (from, to) = (to, from);
            }
            return from;
        }

        /// <summary>The share a sum of <c>2 * radius + 1</c> pixels is multiplied by to give the average (one division for the whole picture, not one for each pixel).</summary>
        private static float Reciprocal(int radius) => 1f / (2 * radius + 1);

        private static uint Pack(int blue, int green, int red, float reciprocal) =>
            0xFF000000u
            | ((uint)(red * reciprocal + 0.5f) << 16)
            | ((uint)(green * reciprocal + 0.5f) << 8)
            | (uint)(blue * reciprocal + 0.5f);

        private static void AlongRows(uint[] source, uint[] target, int width, int height, int radius)
        {
            var reciprocal = Reciprocal(radius);
            Parallel.For(0, height, y =>
            {
                var row = y * width;
                int blue = 0, green = 0, red = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var pixel = source[row + Math.Clamp(k, 0, width - 1)];
                    blue += (int)(pixel & 0xFF);
                    green += (int)((pixel >> 8) & 0xFF);
                    red += (int)((pixel >> 16) & 0xFF);
                }

                for (var x = 0; x < width; x++)
                {
                    target[row + x] = Pack(blue, green, red, reciprocal);
                    var added = source[row + Math.Min(x + radius + 1, width - 1)];
                    var removed = source[row + Math.Max(x - radius, 0)];
                    blue += (int)(added & 0xFF) - (int)(removed & 0xFF);
                    green += (int)((added >> 8) & 0xFF) - (int)((removed >> 8) & 0xFF);
                    red += (int)((added >> 16) & 0xFF) - (int)((removed >> 16) & 0xFF);
                }
            });
        }

        /// <summary>Down the columns, a stripe of them at a time: every step of the moving average then reads whole pieces of rows, which is what the memory of a processor is quick at.</summary>
        private static void AlongColumns(uint[] source, uint[] target, int width, int height, int radius)
        {
            var reciprocal = Reciprocal(radius);
            var stripes = (width + ColumnsPerStripe - 1) / ColumnsPerStripe;
            Parallel.For(0, stripes, stripe =>
            {
                var first = stripe * ColumnsPerStripe;
                var count = Math.Min(ColumnsPerStripe, width - first);
                Span<int> blue = stackalloc int[ColumnsPerStripe];
                Span<int> green = stackalloc int[ColumnsPerStripe];
                Span<int> red = stackalloc int[ColumnsPerStripe];
                blue.Clear();
                green.Clear();
                red.Clear();

                for (var k = -radius; k <= radius; k++)
                {
                    var row = Math.Clamp(k, 0, height - 1) * width + first;
                    for (var i = 0; i < count; i++)
                    {
                        var pixel = source[row + i];
                        blue[i] += (int)(pixel & 0xFF);
                        green[i] += (int)((pixel >> 8) & 0xFF);
                        red[i] += (int)((pixel >> 16) & 0xFF);
                    }
                }

                for (var y = 0; y < height; y++)
                {
                    var here = y * width + first;
                    var addedRow = Math.Min(y + radius + 1, height - 1) * width + first;
                    var removedRow = Math.Max(y - radius, 0) * width + first;
                    for (var i = 0; i < count; i++)
                    {
                        target[here + i] = Pack(blue[i], green[i], red[i], reciprocal);
                        var added = source[addedRow + i];
                        var removed = source[removedRow + i];
                        blue[i] += (int)(added & 0xFF) - (int)(removed & 0xFF);
                        green[i] += (int)((added >> 8) & 0xFF) - (int)((removed >> 8) & 0xFF);
                        red[i] += (int)((added >> 16) & 0xFF) - (int)((removed >> 16) & 0xFF);
                    }
                }
            });
        }
    }
}
