using System.Drawing;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>Takes pixels out of a picture for the models: the whole picture made smaller, and one line of text straightened. The colours come out as the models were trained on them: blue, green, red (the order of the picture), over white where the picture is see-through.</summary>
    internal static class Sampler
    {
        /// <summary>The picture made <paramref name="width"/> by <paramref name="height"/>, as three planes of numbers (blue, green, red) with <c>(value / 255 - mean) / deviation</c> for each channel.</summary>
        public static float[] Resized(Bitmap32 picture, int width, int height, float[] mean, float[] deviation)
        {
            var source = picture;
            var scaleX = (float)picture.Width / width;
            var scaleY = (float)picture.Height / height;

            // A picture made much smaller by picking points loses thin lines of text altogether: it is averaged down to about twice the size wanted first
            var factor = (int)Math.Floor(Math.Min(scaleX, scaleY) / 2f);
            if (factor >= 2)
                source = Shrink(picture, factor);

            var plane = width * height;
            var result = new float[3 * plane];
            var sx = (float)source.Width / width;
            var sy = (float)source.Height / height;
            for (var y = 0; y < height; y++)
            {
                var fy = (y + 0.5f) * sy - 0.5f;
                for (var x = 0; x < width; x++)
                {
                    var fx = (x + 0.5f) * sx - 0.5f;
                    Bilinear(source, fx, fy, out var b, out var g, out var r);
                    var i = y * width + x;
                    result[i] = (b / 255f - mean[0]) / deviation[0];
                    result[plane + i] = (g / 255f - mean[1]) / deviation[1];
                    result[2 * plane + i] = (r / 255f - mean[2]) / deviation[2];
                }
            }
            return result;
        }

        /// <summary>
        /// One line of text, cut out of the picture along its box and straightened, <paramref name="height"/> high and as wide as the line is long at that height (no wider than <paramref name="maximumWidth"/>).
        /// A line that is much higher than long is a line of text that lies on its side: it is turned a quarter, to be read. The numbers are as in <see cref="Resized"/>.
        /// </summary>
        public static (float[] Planes, int Width) Line(Bitmap32 picture, TextRegion box, int height, int maximumWidth, float[] mean, float[] deviation)
        {
            var origin = box.TopLeft;
            var along = new PointF(box.TopRight.X - box.TopLeft.X, box.TopRight.Y - box.TopLeft.Y);
            var across = new PointF(box.BottomLeft.X - box.TopLeft.X, box.BottomLeft.Y - box.TopLeft.Y);
            var length = Math.Max(1f, MathF.Sqrt(along.X * along.X + along.Y * along.Y));
            var side = Math.Max(1f, MathF.Sqrt(across.X * across.X + across.Y * across.Y));

            var turned = side / length >= 1.5f;
            if (turned)
            {
                // The line reads from the bottom to the top: its start is the bottom left, its way along goes up, and across goes to the right
                origin = box.BottomLeft;
                along = new PointF(box.TopLeft.X - box.BottomLeft.X, box.TopLeft.Y - box.BottomLeft.Y);
                across = new PointF(box.BottomRight.X - box.BottomLeft.X, box.BottomRight.Y - box.BottomLeft.Y);
                (length, side) = (side, length);
            }

            var width = Math.Clamp((int)Math.Ceiling(height * length / side), 8, maximumWidth);
            var plane = width * height;
            var result = new float[3 * plane];
            for (var y = 0; y < height; y++)
            {
                var v = (y + 0.5f) / height;
                for (var x = 0; x < width; x++)
                {
                    var u = (x + 0.5f) / width;
                    var fx = origin.X + along.X * u + across.X * v - 0.5f;
                    var fy = origin.Y + along.Y * u + across.Y * v - 0.5f;
                    Bilinear(picture, fx, fy, out var b, out var g, out var r);
                    var i = y * width + x;
                    result[i] = (b / 255f - mean[0]) / deviation[0];
                    result[plane + i] = (g / 255f - mean[1]) / deviation[1];
                    result[2 * plane + i] = (r / 255f - mean[2]) / deviation[2];
                }
            }
            return (result, width);
        }

        /// <summary>The colour between the pixels, from the four around the point (a point outside the picture takes the pixel at the edge); a see-through pixel counts as a mix with white.</summary>
        private static void Bilinear(Bitmap32 picture, float x, float y, out float blue, out float green, out float red)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            var x1 = Math.Clamp(x0 + 1, 0, picture.Width - 1);
            var y1 = Math.Clamp(y0 + 1, 0, picture.Height - 1);
            x0 = Math.Clamp(x0, 0, picture.Width - 1);
            y0 = Math.Clamp(y0, 0, picture.Height - 1);

            var data = picture.Bgra;
            var i00 = (y0 * picture.Width + x0) * 4;
            var i10 = (y0 * picture.Width + x1) * 4;
            var i01 = (y1 * picture.Width + x0) * 4;
            var i11 = (y1 * picture.Width + x1) * 4;
            var w00 = (1 - fx) * (1 - fy);
            var w10 = fx * (1 - fy);
            var w01 = (1 - fx) * fy;
            var w11 = fx * fy;

            blue = Channel(data, 0, i00, i10, i01, i11, w00, w10, w01, w11);
            green = Channel(data, 1, i00, i10, i01, i11, w00, w10, w01, w11);
            red = Channel(data, 2, i00, i10, i01, i11, w00, w10, w01, w11);
        }

        private static float Channel(byte[] data, int offset, int i00, int i10, int i01, int i11, float w00, float w10, float w01, float w11) =>
            OverWhite(data, i00, offset) * w00 + OverWhite(data, i10, offset) * w10 + OverWhite(data, i01, offset) * w01 + OverWhite(data, i11, offset) * w11;

        private static float OverWhite(byte[] data, int pixel, int offset)
        {
            var alpha = data[pixel + 3];
            return alpha == 255 ? data[pixel + offset] : (data[pixel + offset] * alpha + 255 * (255 - alpha)) / 255f;
        }

        /// <summary>The picture made <paramref name="factor"/> times smaller in each direction, every new pixel the average of the block it takes the place of.</summary>
        internal static Bitmap32 Shrink(Bitmap32 picture, int factor)
        {
            var width = Math.Max(1, picture.Width / factor);
            var height = Math.Max(1, picture.Height / factor);
            var result = new byte[width * height * 4];
            var data = picture.Bgra;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int b = 0, g = 0, r = 0, a = 0, n = 0;
                    for (var by = 0; by < factor; by++)
                    {
                        var sy = y * factor + by;
                        if (sy >= picture.Height)
                            break;
                        for (var bx = 0; bx < factor; bx++)
                        {
                            var sx = x * factor + bx;
                            if (sx >= picture.Width)
                                break;
                            var i = (sy * picture.Width + sx) * 4;
                            b += data[i];
                            g += data[i + 1];
                            r += data[i + 2];
                            a += data[i + 3];
                            n++;
                        }
                    }

                    var o = (y * width + x) * 4;
                    result[o] = (byte)(b / n);
                    result[o + 1] = (byte)(g / n);
                    result[o + 2] = (byte)(r / n);
                    result[o + 3] = (byte)(a / n);
                }
            }
            return new Bitmap32(width, height, result);
        }
    }
}
