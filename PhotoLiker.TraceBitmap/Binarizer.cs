namespace PhotoLiker.TraceBitmap
{
    /// <summary>The ways a picture is cut into black and white (or into bands of brightness or colour) before the outlines are followed.</summary>
    internal static class Binarizer
    {
        /// <summary>How light each pixel is, 0 to 255 (the weights of the television: green counts most).</summary>
        public static byte[] Luminance(PixelImage image)
        {
            var pixels = image.Width * image.Height;
            var result = new byte[pixels];
            var bgr = image.Bgr;
            for (var i = 0; i < pixels; i++)
            {
                var b = bgr[i * 3];
                var g = bgr[i * 3 + 1];
                var r = bgr[i * 3 + 2];
                result[i] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
            }

            return result;
        }

        /// <summary>Black where the pixel is darker than the cutoff (0 to 1).</summary>
        public static Bilevel BrightnessCutoff(byte[] luminance, int width, int height, double cutoff)
        {
            var result = new Bilevel(width, height);
            var limit = cutoff * 255.0;
            for (var i = 0; i < luminance.Length; i++)
                result.Pixels[i] = luminance[i] < limit ? (byte)1 : (byte)0;
            return result;
        }

        /// <summary>A copy of the picture blurred with a box of 3 x 3 pixels, twice (close to a Gaussian blur with a sigma of one pixel and a half).</summary>
        public static PixelImage Blurred(PixelImage image)
        {
            var once = new byte[image.Bgr.Length];
            var twice = new byte[image.Bgr.Length];
            BoxBlur(image.Bgr, once, image.Width, image.Height);
            BoxBlur(once, twice, image.Width, image.Height);
            return new PixelImage(image.Width, image.Height, twice);
        }

        private static void BoxBlur(byte[] source, byte[] target, int width, int height)
        {
            // Two passes of three taps (across, then down) are the same as one box of 3 x 3; the edge pixels repeat
            var middle = new byte[source.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var left = Math.Max(x - 1, 0);
                    var right = Math.Min(x + 1, width - 1);
                    for (var c = 0; c < 3; c++)
                        middle[(y * width + x) * 3 + c] = (byte)((source[(y * width + left) * 3 + c] + source[(y * width + x) * 3 + c] + source[(y * width + right) * 3 + c] + 1) / 3);
                }
            }

            for (var y = 0; y < height; y++)
            {
                var up = Math.Max(y - 1, 0);
                var down = Math.Min(y + 1, height - 1);
                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < 3; c++)
                        target[(y * width + x) * 3 + c] = (byte)((middle[(up * width + x) * 3 + c] + middle[(y * width + x) * 3 + c] + middle[(down * width + x) * 3 + c] + 1) / 3);
                }
            }
        }

        // ── Edge detection (Canny) ───────────────────────────────────────

        /// <summary>
        /// The edges of the picture as one-pixel lines: the picture is blurred a little, the strength and the direction of the slope of brightness are measured (Sobel), only the ridge of each slope is
        /// kept, and a ridge counts when its strength (as a share of the strongest in the picture) is over the low threshold and it is joined to one over the high threshold.
        /// </summary>
        public static Bilevel Edges(byte[] luminance, int width, int height, double highThreshold, double lowThreshold = 0.1)
        {
            var result = new Bilevel(width, height);
            if (width < 3 || height < 3)
                return result;

            var smooth = GaussianBlur(luminance, width, height);
            var magnitude = new float[width * height];
            var direction = new byte[width * height]; // the direction of the slope, brightness rising: 0 to the side, 1 down and to the right, 2 downwards, 3 down and to the left (or the reverse)
            var strongest = 0f;

            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var i = y * width + x;
                    var gx = -smooth[i - width - 1] + smooth[i - width + 1] - 2 * smooth[i - 1] + 2 * smooth[i + 1] - smooth[i + width - 1] + smooth[i + width + 1];
                    var gy = -smooth[i - width - 1] - 2 * smooth[i - width] - smooth[i - width + 1] + smooth[i + width - 1] + 2 * smooth[i + width] + smooth[i + width + 1];
                    var strength = MathF.Sqrt(gx * gx + gy * gy);
                    magnitude[i] = strength;
                    if (strength > strongest)
                        strongest = strength;

                    // The direction of the slope, to the nearest of four
                    var angle = MathF.Atan2(gy, gx) * (180f / MathF.PI);
                    if (angle < 0)
                        angle += 180f;
                    direction[i] = angle < 22.5f || angle >= 157.5f ? (byte)0 : angle < 67.5f ? (byte)1 : angle < 112.5f ? (byte)2 : (byte)3;
                }
            }

            if (strongest <= 0)
                return result;

            // Thin the ridges: a pixel stays only when it is not weaker than its two neighbours across the edge
            var ridge = new float[width * height];
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var i = y * width + x;
                    var (a, b) = direction[i] switch
                    {
                        0 => (magnitude[i - 1], magnitude[i + 1]),
                        1 => (magnitude[i - width - 1], magnitude[i + width + 1]),
                        2 => (magnitude[i - width], magnitude[i + width]),
                        _ => (magnitude[i - width + 1], magnitude[i + width - 1]),
                    };
                    ridge[i] = magnitude[i] >= a && magnitude[i] >= b ? magnitude[i] / strongest : 0f;
                }
            }

            // Hysteresis: start at the strong ridges and spread over every touching ridge that is over the low threshold
            var stack = new Stack<int>();
            for (var i = 0; i < ridge.Length; i++)
            {
                if (ridge[i] >= highThreshold && result.Pixels[i] == 0)
                {
                    result.Pixels[i] = 1;
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        var at = stack.Pop();
                        var x = at % width;
                        var y = at / width;
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                var nx = x + dx;
                                var ny = y + dy;
                                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                                    continue;

                                var next = ny * width + nx;
                                if (result.Pixels[next] == 0 && ridge[next] >= lowThreshold)
                                {
                                    result.Pixels[next] = 1;
                                    stack.Push(next);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static float[] GaussianBlur(byte[] luminance, int width, int height)
        {
            // A kernel of five taps (sigma 1), across and then down
            float[] kernel = [0.0545f, 0.2442f, 0.4026f, 0.2442f, 0.0545f];
            var middle = new float[luminance.Length];
            var result = new float[luminance.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = 0f;
                    for (var k = -2; k <= 2; k++)
                        sum += kernel[k + 2] * luminance[y * width + Math.Clamp(x + k, 0, width - 1)];
                    middle[y * width + x] = sum;
                }
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = 0f;
                    for (var k = -2; k <= 2; k++)
                        sum += kernel[k + 2] * middle[Math.Clamp(y + k, 0, height - 1) * width + x];
                    result[y * width + x] = sum;
                }
            }

            return result;
        }

        // ── Bands of brightness, shades of gray, reduced colours ─────────

        /// <summary>
        /// For the scans of brightness steps: for each pixel, the number of the first of the <paramref name="steps"/> thresholds (evenly spaced between 0 and 1, none at either end) that the pixel
        /// is darker than, or <paramref name="steps"/> when it is lighter than all of them.
        /// </summary>
        public static byte[] BrightnessLevels(byte[] luminance, int steps)
        {
            var levels = new byte[luminance.Length];
            for (var i = 0; i < luminance.Length; i++)
            {
                var level = steps;
                for (var step = 0; step < steps; step++)
                {
                    if (luminance[i] < BrightnessThreshold(step, steps) * 255.0)
                    {
                        level = step;
                        break;
                    }
                }

                levels[i] = (byte)level;
            }

            return levels;
        }

        /// <summary>The brightness (0 to 1) scan number <paramref name="step"/> of <paramref name="steps"/> cuts at.</summary>
        public static double BrightnessThreshold(int step, int steps) => (step + 1.0) / (steps + 1.0);

        /// <summary>For each pixel, which of the <paramref name="grays"/> equal bands of brightness it is in, 0 for the darkest.</summary>
        public static byte[] GrayLevels(byte[] luminance, int grays)
        {
            var levels = new byte[luminance.Length];
            for (var i = 0; i < luminance.Length; i++)
                levels[i] = (byte)Math.Min(grays - 1, luminance[i] * grays / 256);
            return levels;
        }

        /// <summary>The gray in the middle of band <paramref name="level"/> of <paramref name="grays"/>.</summary>
        public static TraceColor GrayOfBand(int level, int grays)
        {
            var value = (byte)Math.Clamp((int)Math.Round((level + 0.5) * 255.0 / grays), 0, 255);
            return new TraceColor(value, value, value);
        }

        /// <summary>The gray of a brightness scan: the middle of the band between the scan before it and this one.</summary>
        public static TraceColor GrayOfBrightnessStep(int step, int steps)
        {
            var low = step == 0 ? 0.0 : BrightnessThreshold(step - 1, steps);
            var value = (byte)Math.Clamp((int)Math.Round((low + BrightnessThreshold(step, steps)) / 2.0 * 255.0), 0, 255);
            return new TraceColor(value, value, value);
        }

        /// <summary>
        /// The lines where reduced colours meet: a pixel is black when a pixel next to it (side by side) has another palette number. The picture of palette numbers comes from
        /// <see cref="ColorQuantizer"/>.
        /// </summary>
        public static Bilevel Boundaries(byte[] indices, int width, int height)
        {
            var result = new Bilevel(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var differs = (x + 1 < width && indices[i + 1] != indices[i]) || (y + 1 < height && indices[i + width] != indices[i]);
                    result.Pixels[i] = differs ? (byte)1 : (byte)0;
                }
            }

            return result;
        }
    }
}
