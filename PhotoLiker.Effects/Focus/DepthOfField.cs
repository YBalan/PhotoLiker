using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Motion;

namespace PhotoLiker.Effects.Focus
{
    /// <summary>
    /// A photo refocused after the fact, as a phone does in its portrait mode: what is at the depth of the focus stays sharp, what is nearer or farther is blurred (the more the farther it is from the focus), and
    /// what is far can be lost in haze. It needs the depth of the photo (<see cref="DepthMap"/>, from the depth model) and nothing else.
    ///
    /// The blurred parts are taken from two blurred copies of the photo (a weak one and a strong one, see <see cref="BoxBlur"/>) by how far each point is from the focus, so the cost is the same for any photo
    /// of that size however much is blurred: about 0.15 s for a 3 megapixel photo, with all the cores. The depth is a map of 500 points across; the amount of blur and of haze is worked out on that grid, once, and
    /// smoothed between its points per pixel, so the same look can be given to a preview and to the photo at its full size.
    /// </summary>
    public static class DepthOfField
    {
        /// <summary>The strongest blur (<see cref="FocusLook.Blur"/> 1) has a disc of this share of the long side of the photo as its radius: 60 pixels on a photo of 2000 across.</summary>
        public const double MaxBlurShare = 0.03;

        /// <summary>How deep the zone that stays sharp is at least (Range 0), and how much deeper it gets with Range 1, in depth (0 to 1).</summary>
        private const double ZoneAtLeast = 0.03;
        private const double ZoneMore = 0.25;

        /// <summary>How much farther from the zone the blur takes to be at its strongest, in depth.</summary>
        private const double Falloff = 0.25;

        /// <summary>The haze is thicker the farther a thing is: (1 - depth) to this power.</summary>
        private const double HazeCurve = 1.2;

        private const uint Opaque = 0xFF000000;
        private const int RowsPerChunk = 8;

        /// <summary>The radius of the strongest blur of a photo of this size, in pixels, for a <see cref="FocusLook.Blur"/> of <paramref name="blur"/> (0 to 1).</summary>
        public static int BlurRadius(int width, int height, double blur) =>
            (int)Math.Round(Math.Clamp(double.IsNaN(blur) ? 0 : blur, 0, 1) * MaxBlurShare * Math.Max(width, height));

        /// <summary>
        /// The photo with the focus, the blur and the haze of <paramref name="look"/> (pixels as they are given: 0xAARRGGBB, row after row; the result is opaque). A look that changes nothing (no blur and no haze)
        /// gives a copy of the photo. The pictures are not touched.
        /// </summary>
        /// <param name="depth">How near each part of the photo is.</param>
        public static uint[] Render(uint[] pixels, int width, int height, DepthMap depth, FocusLook look)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentNullException.ThrowIfNull(depth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height, nameof(pixels));

            look = look.Clamped();
            var radius = BlurRadius(width, height, look.Blur);
            if (radius < 1 && look.Haze <= 0)
            {
                var copy = new uint[width * height];
                Array.Copy(pixels, copy, copy.Length);
                return copy;
            }

            // Two blurred copies: the strong one is what is farthest from the focus, the weak one what is half way. (A blur of a disc of radius R is a moving average about R / 2 wide on each side, three times.)
            uint[]? strong = null, weak = null;
            if (radius >= 1)
            {
                var strongWidth = Math.Max(1, radius / 2);
                strong = BoxBlur.Apply(pixels, width, height, strongWidth);
                weak = strongWidth >= 2 ? BoxBlur.Apply(pixels, width, height, Math.Max(1, strongWidth / 2)) : strong;
            }

            var grid = Grid.Of(depth, look);
            var fog = look.Haze > 0 ? FogColour(pixels, width, height, depth) : 0u;
            var result = new uint[width * height];
            var columns = Columns.Of(width, depth.Width);

            var chunks = (height + RowsPerChunk - 1) / RowsPerChunk;
            Parallel.For(0, chunks, chunk =>
            {
                var last = Math.Min(height, (chunk + 1) * RowsPerChunk);
                for (var y = chunk * RowsPerChunk; y < last; y++)
                    RenderRow(pixels, strong, weak, fog, result, y, width, height, grid, columns);
            });
            return result;
        }

        /// <summary>
        /// The depth to focus on when the person has not said: the depth of the largest face if there is one (the median of the depth in it), else of the middle of the photo, where what the photograph is of mostly is.
        /// 0 to 1, as the map says it.
        /// </summary>
        public static double AutoFocus(DepthMap depth, IReadOnlyList<PhotoRegion> faces)
        {
            var largest = faces.Where(face => face.IsUsable).OrderByDescending(face => face.Area).Cast<PhotoRegion?>().FirstOrDefault();
            if (largest is { } face)
            {
                // The middle of the box: the hair and the background at its edges do not say how far the person is
                var inner = new PhotoRegion(face.X + face.Width * 0.15, face.Y + face.Height * 0.15, face.Width * 0.7, face.Height * 0.7);
                return MedianDepth(depth, inner, 5);
            }

            return MedianDepth(depth, new PhotoRegion(0.25, 0.25, 0.5, 0.5), 7);
        }

        /// <summary>The depth to focus on when the person points at a place of the photo (<paramref name="x"/> and <paramref name="y"/> are fractions of it): the median of the depth round the place, so that one thin thing does not decide.</summary>
        public static double FocusAt(DepthMap depth, double x, double y) =>
            MedianDepth(depth, new PhotoRegion(x - 0.02, y - 0.02, 0.04, 0.04), 5);

        private static double MedianDepth(DepthMap depth, PhotoRegion region, int across)
        {
            var samples = new List<float>(across * across);
            for (var j = 0; j < across; j++)
            for (var i = 0; i < across; i++)
                samples.Add(depth.At(region.X + region.Width * (i + 0.5) / across, region.Y + region.Height * (j + 0.5) / across));

            samples.Sort();
            return samples[samples.Count / 2];
        }

        /// <summary>The colour the haze is: the colour of the far parts of the photo, lightened, so that a haze over a sunset is warm and over a lake is cold. A photo that has no far parts gets a pale blue grey.</summary>
        private static uint FogColour(uint[] pixels, int width, int height, DepthMap depth)
        {
            const int Samples = 48;
            double red = 0, green = 0, blue = 0, total = 0;
            for (var j = 0; j < Samples; j++)
            for (var i = 0; i < Samples; i++)
            {
                var x = (i + 0.5) / Samples;
                var y = (j + 0.5) / Samples;
                var farness = 1 - depth.At(x, y);
                var weight = farness * farness * farness;
                var pixel = pixels[Math.Min(height - 1, (int)(y * height)) * width + Math.Min(width - 1, (int)(x * width))];
                red += ((pixel >> 16) & 0xFF) * weight;
                green += ((pixel >> 8) & 0xFF) * weight;
                blue += (pixel & 0xFF) * weight;
                total += weight;
            }

            if (total < 1e-6)
                return Opaque | 0xD8DEE8;

            const double Lighter = 0.55; // how far from the colour of the far parts towards white
            static uint Channel(double sum, double all) => (uint)Math.Clamp(Math.Round(sum / all + (255 - sum / all) * Lighter), 0, 255);
            return Opaque | (Channel(red, total) << 16) | (Channel(green, total) << 8) | Channel(blue, total);
        }

        /// <summary>The blur and the haze at every point of the grid of the depth.</summary>
        private sealed class Grid
        {
            public required int Width { get; init; }
            public required int Height { get; init; }

            /// <summary>How out of focus each point is, 0 (sharp) to 1 (the strongest blur).</summary>
            public required float[] Blur { get; init; }

            /// <summary>How much haze lies over each point, 0 to 1.</summary>
            public required float[] Haze { get; init; }

            public static Grid Of(DepthMap depth, FocusLook look)
            {
                var blur = new float[depth.Values.Length];
                var haze = new float[depth.Values.Length];
                var zone = ZoneAtLeast + ZoneMore * look.Range;
                for (var i = 0; i < blur.Length; i++)
                {
                    var near = depth.Values[i] / 255.0;
                    var outOfFocus = Math.Clamp((Math.Abs(near - look.Focus) - zone) / Falloff, 0, 1);
                    blur[i] = (float)(outOfFocus * outOfFocus * (3 - 2 * outOfFocus));
                    haze[i] = (float)(look.Haze * Math.Pow(1 - near, HazeCurve));
                }
                return new Grid { Width = depth.Width, Height = depth.Height, Blur = blur, Haze = haze };
            }
        }

        /// <summary>For every column of the picture, the two columns of the grid it falls between and how far between (the grid is smoothed between its points).</summary>
        private sealed class Columns
        {
            public required int[] Left { get; init; }
            public required int[] Right { get; init; }
            public required float[] Fraction { get; init; }

            public static Columns Of(int width, int gridWidth)
            {
                var left = new int[width];
                var right = new int[width];
                var fraction = new float[width];
                for (var x = 0; x < width; x++)
                {
                    var at = Math.Clamp((x + 0.5f) * gridWidth / width - 0.5f, 0f, gridWidth - 1f);
                    left[x] = (int)at;
                    right[x] = Math.Min(left[x] + 1, gridWidth - 1);
                    fraction[x] = at - left[x];
                }
                return new Columns { Left = left, Right = right, Fraction = fraction };
            }
        }

        private static void RenderRow(uint[] pixels, uint[]? strong, uint[]? weak, uint fog, uint[] result, int y, int width, int height, Grid grid, Columns columns)
        {
            var at = Math.Clamp((y + 0.5f) * grid.Height / height - 0.5f, 0f, grid.Height - 1f);
            var upper = (int)at * grid.Width;
            var lower = Math.Min((int)at + 1, grid.Height - 1) * grid.Width;
            var down = at - (int)at;
            var row = y * width;

            for (var x = 0; x < width; x++)
            {
                var l = columns.Left[x];
                var r = columns.Right[x];
                var across = columns.Fraction[x];

                var pixel = pixels[row + x] | Opaque;
                if (strong is not null && weak is not null)
                {
                    var blur = Smooth(grid.Blur, upper, lower, l, r, across, down);
                    if (blur > 0)
                    {
                        pixel = blur < 0.5f
                            ? Lerp(pixel, weak[row + x], (uint)(blur * 2 * 256))
                            : Lerp(weak[row + x], strong[row + x], (uint)((blur - 0.5f) * 2 * 256));
                    }
                }

                if (fog != 0)
                {
                    var haze = Smooth(grid.Haze, upper, lower, l, r, across, down);
                    if (haze > 0)
                        pixel = Lerp(pixel, fog, (uint)(Math.Min(haze, 1f) * 256));
                }

                result[row + x] = pixel;
            }
        }

        /// <summary>The value of a grid at a place between four of its points (the points of the row above and below, the columns left and right, and how far across and down).</summary>
        private static float Smooth(float[] values, int upper, int lower, int left, int right, float across, float down)
        {
            var top = values[upper + left] + (values[upper + right] - values[upper + left]) * across;
            var bottom = values[lower + left] + (values[lower + right] - values[lower + left]) * across;
            return top + (bottom - top) * down;
        }

        /// <summary>The pixel <paramref name="fraction"/> / 256 of the way from <paramref name="a"/> to <paramref name="b"/>, each channel (two at a time, with room between them for the carry).</summary>
        private static uint Lerp(uint a, uint b, uint fraction)
        {
            fraction = Math.Min(fraction, 256);
            var inverse = 256 - fraction;
            var redBlue = ((a & 0x00FF00FF) * inverse + (b & 0x00FF00FF) * fraction >> 8) & 0x00FF00FF;
            var alphaGreen = (((a >> 8) & 0x00FF00FF) * inverse + ((b >> 8) & 0x00FF00FF) * fraction) & 0xFF00FF00;
            return redBlue | alphaGreen;
        }
    }
}
