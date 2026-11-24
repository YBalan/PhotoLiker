using System.Drawing;
using PhotoLiker.Drawing;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>
    /// Cuts a picture out of the photo of a page along its outline: the smallest rectangle that holds the outline, with everything outside the outline see-through (the edge a pixel soft). It needs no
    /// model: the outline is what the finder (<see cref="PictureFinder"/>) found and the page kept, so a book can be made again, and again, without finding anything.
    /// </summary>
    public static class PictureCutter
    {
        /// <summary>The picture as pixels with an alpha channel (B, G, R, A, not multiplied), no larger than <paramref name="maximumSide"/> on its longer side; null when the outline holds no pixel of the photo.</summary>
        public static Bitmap32? Cut(Bitmap32 photo, IReadOnlyList<PointF> outline, int maximumSide = 2400)
        {
            ArgumentNullException.ThrowIfNull(photo);
            ArgumentNullException.ThrowIfNull(outline);
            if (outline.Count < 3 || maximumSide < 1)
                return null;

            var left = Math.Max(0, (int)Math.Floor(outline.Min(p => p.X)));
            var top = Math.Max(0, (int)Math.Floor(outline.Min(p => p.Y)));
            var right = Math.Min(photo.Width, (int)Math.Ceiling(outline.Max(p => p.X)));
            var bottom = Math.Min(photo.Height, (int)Math.Ceiling(outline.Max(p => p.Y)));
            int sourceWidth = right - left, sourceHeight = bottom - top;
            if (sourceWidth < 1 || sourceHeight < 1)
                return null;

            var scale = Math.Min(1.0, maximumSide / (double)Math.Max(sourceWidth, sourceHeight));
            var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            var bgra = new byte[width * height * 4];

            var coverage = new float[width];
            var crossings = new List<float>();
            const int SubRows = 4;
            for (var y = 0; y < height; y++)
            {
                Array.Clear(coverage);
                for (var s = 0; s < SubRows; s++)
                {
                    // Where the outline crosses this thin row, in pixels of the picture; between the first and the second crossing, the third and the fourth... is inside
                    var photoY = top + (y + (s + 0.5) / SubRows) / scale;
                    crossings.Clear();
                    for (var i = 0; i < outline.Count; i++)
                    {
                        var a = outline[i];
                        var b = outline[(i + 1) % outline.Count];
                        if ((a.Y <= photoY && b.Y > photoY) || (b.Y <= photoY && a.Y > photoY))
                            crossings.Add((float)((a.X + (photoY - a.Y) / (b.Y - a.Y) * (b.X - a.X) - left) * scale));
                    }

                    crossings.Sort();
                    for (var i = 0; i + 1 < crossings.Count; i += 2)
                    {
                        float from = Math.Clamp(crossings[i], 0, width), to = Math.Clamp(crossings[i + 1], 0, width);
                        for (var x = (int)from; x < width && x < to; x++)
                            coverage[x] += (Math.Min(to, x + 1) - Math.Max(from, x)) / SubRows;
                    }
                }

                for (var x = 0; x < width; x++)
                {
                    var alpha = Math.Clamp(coverage[x], 0f, 1f);
                    if (alpha <= 0)
                        continue;

                    // The colour is the mean of the pixels of the photo this one stands for
                    int x0 = left + (int)Math.Floor(x / scale), x1 = Math.Min(photo.Width, Math.Max(x0 + 1, left + (int)Math.Ceiling((x + 1) / scale)));
                    int y0 = top + (int)Math.Floor(y / scale), y1 = Math.Min(photo.Height, Math.Max(y0 + 1, top + (int)Math.Ceiling((y + 1) / scale)));
                    x0 = Math.Min(x0, photo.Width - 1);
                    y0 = Math.Min(y0, photo.Height - 1);
                    long sumB = 0, sumG = 0, sumR = 0, count = 0;
                    for (var sy = y0; sy < y1; sy++)
                    {
                        for (var sx = x0; sx < x1; sx++)
                        {
                            var i = (sy * photo.Width + sx) * 4;
                            // A see-through point of the photo is the colour it is mixed with white
                            var a = photo.Bgra[i + 3];
                            sumB += a == 255 ? photo.Bgra[i] : (photo.Bgra[i] * a + 255 * (255 - a)) / 255;
                            sumG += a == 255 ? photo.Bgra[i + 1] : (photo.Bgra[i + 1] * a + 255 * (255 - a)) / 255;
                            sumR += a == 255 ? photo.Bgra[i + 2] : (photo.Bgra[i + 2] * a + 255 * (255 - a)) / 255;
                            count++;
                        }
                    }

                    var o = (y * width + x) * 4;
                    bgra[o] = (byte)(sumB / count);
                    bgra[o + 1] = (byte)(sumG / count);
                    bgra[o + 2] = (byte)(sumR / count);
                    bgra[o + 3] = (byte)Math.Round(alpha * 255);
                }
            }

            return new Bitmap32(width, height, bgra);
        }
    }
}
