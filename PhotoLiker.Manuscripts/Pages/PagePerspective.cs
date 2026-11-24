using System.Drawing;
using PhotoLiker.Drawing;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>
    /// Straightens a sheet of paper that was photographed from an angle: the picture inside the four corners is stretched onto an upright rectangle (a perspective transform, so the lines of
    /// a ruled page become parallel again and the desk round the sheet is left out). The transform is exact for a sheet seen at any angle, not only a sheet that is a parallelogram in the photo.
    /// </summary>
    public static class PagePerspective
    {
        /// <summary>
        /// The sheet as an upright picture. Its width and height are the lengths of the sides of the quadrilateral on average, made smaller (the same proportions) when the longer is more than
        /// <paramref name="maximumSide"/>; <paramref name="inset"/> takes that share of each side off all round (the edge of the paper is seldom as sharp as the model's idea of it, and a
        /// strip of desk is worse than a strip of paper missing).
        /// </summary>
        public static Bitmap32 Straighten(Bitmap32 photo, PageQuad quad, int maximumSide = 3600, float inset = 0f)
        {
            ArgumentNullException.ThrowIfNull(photo);

            var (a, b, c, d) = Shrunk(quad, inset);
            var width = Math.Max(16f, (Distance(a, b) + Distance(d, c)) / 2f);
            var height = Math.Max(16f, (Distance(a, d) + Distance(b, c)) / 2f);
            var scale = Math.Min(1f, maximumSide / Math.Max(width, height));
            var outWidth = Math.Max(16, (int)Math.Round(width * scale));
            var outHeight = Math.Max(16, (int)Math.Round(height * scale));

            // The perspective transform of the unit square onto the quadrilateral (Heckbert's formulas): a point (u, v) of the straight sheet is at (X, Y) in the photo
            var (x0, y0, x1, y1, x2, y2, x3, y3) = (a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y);
            double sx = x0 - x1 + x2 - x3, sy = y0 - y1 + y2 - y3;
            double dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
            double g = 0, h = 0;
            var denominator = dx1 * dy2 - dx2 * dy1;
            if ((Math.Abs(sx) > 1e-9 || Math.Abs(sy) > 1e-9) && Math.Abs(denominator) > 1e-12)
            {
                g = (sx * dy2 - dx2 * sy) / denominator;
                h = (dx1 * sy - sx * dy1) / denominator;
            }

            double ha = x1 - x0 + g * x1, hb = x3 - x0 + h * x3, hc = x0;
            double hd = y1 - y0 + g * y1, he = y3 - y0 + h * y3, hf = y0;

            var pixels = new byte[checked(outWidth * outHeight * 4)];
            var source = photo.Bgra;
            for (var y = 0; y < outHeight; y++)
            {
                var v = (y + 0.5) / outHeight;
                for (var x = 0; x < outWidth; x++)
                {
                    var u = (x + 0.5) / outWidth;
                    var w = g * u + h * v + 1;
                    var px = (float)((ha * u + hb * v + hc) / w - 0.5);
                    var py = (float)((hd * u + he * v + hf) / w - 0.5);
                    Sample(source, photo.Width, photo.Height, px, py, pixels, (y * outWidth + x) * 4);
                }
            }

            return new Bitmap32(outWidth, outHeight, pixels);
        }

        private static (PointF A, PointF B, PointF C, PointF D) Shrunk(PageQuad quad, float inset)
        {
            var centre = new PointF((quad.TopLeft.X + quad.TopRight.X + quad.BottomRight.X + quad.BottomLeft.X) / 4f, (quad.TopLeft.Y + quad.TopRight.Y + quad.BottomRight.Y + quad.BottomLeft.Y) / 4f);
            var keep = 1f - 2f * inset;
            PointF In(PointF p) => new(centre.X + (p.X - centre.X) * keep, centre.Y + (p.Y - centre.Y) * keep);
            return (In(quad.TopLeft), In(quad.TopRight), In(quad.BottomRight), In(quad.BottomLeft));
        }

        private static float Distance(PointF p, PointF q) => MathF.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));

        /// <summary>The colour of the photo between its pixels (bilinear; a point outside takes the pixel at the edge) written as four bytes.</summary>
        private static void Sample(byte[] source, int width, int height, float x, float y, byte[] target, int offset)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            var x1 = Math.Clamp(x0 + 1, 0, width - 1);
            var y1 = Math.Clamp(y0 + 1, 0, height - 1);
            x0 = Math.Clamp(x0, 0, width - 1);
            y0 = Math.Clamp(y0, 0, height - 1);

            var i00 = (y0 * width + x0) * 4;
            var i10 = (y0 * width + x1) * 4;
            var i01 = (y1 * width + x0) * 4;
            var i11 = (y1 * width + x1) * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var top = source[i00 + channel] + (source[i10 + channel] - source[i00 + channel]) * fx;
                var bottom = source[i01 + channel] + (source[i11 + channel] - source[i01 + channel]) * fx;
                target[offset + channel] = (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * fy), 0, 255);
            }
        }
    }
}
