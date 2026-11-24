using System.Drawing;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.Stickers;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>
    /// Finds the pictures on a page of a manuscript (a sticker, a drawing, a pasted photo, a stamp) the way the Telegram stickers are cut out: the model that finds the subject of a photo (IS-Net)
    /// draws a contour round what stands out. On a page of writing that is the writing, so the lines of text are found first (the finder of PhotoLiker.Drawing) and painted over with the colour
    /// of the paper round each of them, and the model then sees a sheet with nothing on it but the pictures. What it leaves is judged by its shape: a picture is a solid blob, a piece of
    /// handwriting or of the squares of the paper that was left is thin and ragged, and is dropped. A page of only writing has no picture and gives none.
    /// </summary>
    public sealed class PictureFinder : IDisposable
    {
        /// <summary>How sure the model has to be of a point for it to be part of a picture (0 to 1).</summary>
        public const float Threshold = 0.5f;

        /// <summary>The smallest and the largest share of the page a picture can be: a smaller one is a speck, a larger one is the page itself.</summary>
        public const double MinimumShare = 0.015;
        public const double MaximumShare = 0.6;

        /// <summary>The least a piece fills of its convex hull, and of its bounding rectangle, to be a picture and not what is left of the writing (measured: pictures 0.94 and 0.80, what is left under 0.35).</summary>
        public const double MinimumSolidity = 0.6;
        public const double MinimumFill = 0.45;

        /// <summary>The thinnest a picture can be, as a share of the shorter side of the page: a strip along the edge of the sheet, or a shadow, is not a picture (measured: a sliver of 2 % along the edge of a sheet on a desk).</summary>
        public const double MinimumThickness = 0.05;

        /// <summary>How far the colour of a picture has to be from the colour of the paper (the distance of the red, green and blue, each 0 to 255): the sheet itself, which the model sometimes takes for the subject, is the colour of the paper.</summary>
        public const double MinimumColourDistance = 30;

        public const int MaximumPictures = 8;

        /// <summary>The longer side of the page the finder of lines looks at.</summary>
        private const int FinderSide = 1536;

        /// <summary>How much larger than its strip a line is painted over: the strip is made larger by the finder (handwriting by twice), then by this much once more.</summary>
        private const float PaintAcross = 1.12f;
        private const float PaintAlong = 1.06f;

        private readonly Func<PhotoPixels, SubjectMap> _detect;
        private readonly Func<Bitmap32, CancellationToken, IReadOnlyList<TextRegion>> _lines;
        private readonly List<IDisposable> _owned = [];

        /// <param name="cutoutModelPath">The model of the Telegram stickers (IS-Net).</param>
        /// <param name="finderModelPath">The model that finds the lines of text (PP-OCR detection).</param>
        /// <exception cref="FileNotFoundException">A model's file is not there.</exception>
        public PictureFinder(string cutoutModelPath, string finderModelPath, int threads = 0)
        {
            var detector = new SubjectDetector(cutoutModelPath, threads);
            _owned.Add(detector);
            try
            {
                var finder = new TextFinder(finderModelPath, threads);
                _owned.Add(finder);
                var settings = new FinderSettings { Unclip = 2.0f };
                _detect = detector.Detect;
                _lines = (page, cancel) => [.. finder.Find(page, FinderSide, settings, 300, cancel).Select(found => found.Box)];
            }
            catch
            {
                detector.Dispose();
                throw;
            }
        }

        /// <summary>A finder that gets the lines and the subject another way (a test, which has no models).</summary>
        internal PictureFinder(Func<PhotoPixels, SubjectMap> detect, Func<Bitmap32, IReadOnlyList<TextRegion>> lines)
        {
            _detect = detect;
            _lines = (page, _) => lines(page);
        }

        /// <summary>The pictures on the page, the largest first. A page with no picture gives none.</summary>
        /// <exception cref="OperationCanceledException">The search was cancelled.</exception>
        public IReadOnlyList<PagePicture> Find(Bitmap32 page, CancellationToken cancel = default)
        {
            ArgumentNullException.ThrowIfNull(page);

            var lines = _lines(page, cancel);
            cancel.ThrowIfCancellationRequested();
            var (painted, paper) = PaintedOver(page, lines);
            var map = _detect(painted);
            cancel.ThrowIfCancellationRequested();

            return PicturesIn(map, page.Width, page.Height, painted, paper);
        }

        /// <summary>
        /// The pictures that a map of the subject holds, as outlines in the pixels of a page of the given size. With the pixels the model was shown (and the colour of the paper) a piece that is the colour
        /// of the paper is no picture; without them only the shape of a piece is judged.
        /// </summary>
        internal static List<PagePicture> PicturesIn(SubjectMap map, int pageWidth, int pageHeight, PhotoPixels? seen = null, (byte B, byte G, byte R)? paper = null)
        {
            var pieces = PictureOutline.Pieces(map.Values, map.Width, map.Height, Threshold, out var labels);
            var total = (double)map.Width * map.Height;
            var found = new List<(PagePicture Picture, int Area)>();
            foreach (var piece in pieces)
            {
                var share = piece.Area / total;
                if (share < MinimumShare || share > MaximumShare || piece.Fill < MinimumFill || Math.Min(piece.Width, piece.Height) < MinimumThickness * Math.Min(map.Width, map.Height)
                    || PictureOutline.Solidity(piece, labels, map.Width) < MinimumSolidity)
                    continue;

                if (seen is not null && paper is { } paperColour && ColourDistance(piece, labels, map, seen, paperColour) < MinimumColourDistance)
                    continue;

                var edge = PictureOutline.Simplify(PictureOutline.Trace(piece, labels, map.Width, map.Height), Math.Max(1.5f, Math.Max(piece.Width, piece.Height) / 150f));
                if (edge.Count < 3)
                    continue;

                // From the points of the map to the pixels of the page, and a little larger: the model draws the line where the picture is surest, which is just inside its rim
                var scaleX = pageWidth / (float)map.Width;
                var scaleY = pageHeight / (float)map.Height;
                float cx = edge.Average(p => p.X), cy = edge.Average(p => p.Y);
                var outline = edge
                    .Select(p => new PointF(
                        Math.Clamp((cx + (p.X - cx) * 1.02f) * scaleX, 0, pageWidth),
                        Math.Clamp((cy + (p.Y - cy) * 1.02f) * scaleY, 0, pageHeight)))
                    .ToArray();
                found.Add((new PagePicture(outline), piece.Area));
            }

            return [.. found.OrderByDescending(f => f.Area).Take(MaximumPictures).Select(f => f.Picture)];
        }

        /// <summary>How far the mean colour of a piece is from the colour of the paper (the points of the map stand for the pixels of the photo they are at).</summary>
        private static double ColourDistance(PictureOutline.Piece piece, int[] labels, SubjectMap map, PhotoPixels photo, (byte B, byte G, byte R) paper)
        {
            double blue = 0, green = 0, red = 0;
            long count = 0;
            for (var y = piece.Top; y <= piece.Bottom; y++)
            {
                var photoY = Math.Min(photo.Height - 1, (int)((y + 0.5) * photo.Height / map.Height));
                for (var x = piece.Left; x <= piece.Right; x++)
                {
                    if (labels[y * map.Width + x] != piece.Label)
                        continue;

                    var i = (photoY * photo.Width + Math.Min(photo.Width - 1, (int)((x + 0.5) * photo.Width / map.Width))) * 3;
                    blue += photo.Bgr[i];
                    green += photo.Bgr[i + 1];
                    red += photo.Bgr[i + 2];
                    count++;
                }
            }

            if (count == 0)
                return 0;

            double db = blue / count - paper.B, dg = green / count - paper.G, dr = red / count - paper.R;
            return Math.Sqrt(db * db + dg * dg + dr * dr);
        }

        /// <summary>
        /// The page as the model gets it: each line of text painted over with the colour of the paper round it, so that what stands out on the page is what is not text; and the colour of the paper
        /// (the middle of the colours the lines were painted with; of the whole page when there is no line).
        /// </summary>
        internal static (PhotoPixels Pixels, (byte B, byte G, byte R) Paper) PaintedOver(Bitmap32 page, IReadOnlyList<TextRegion> lines)
        {
            var width = page.Width;
            var height = page.Height;
            var bgra = (byte[])page.Bgra.Clone();
            var used = new List<(byte B, byte G, byte R)>();
            foreach (var line in lines)
            {
                if (Paint(bgra, width, height, line) is { } colour)
                    used.Add(colour);
            }

            var bgr = new byte[width * height * 3];
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                // A see-through point is the colour it is mixed with white, as the rest of the program takes it
                var alpha = bgra[i + 3];
                for (var c = 0; c < 3; c++)
                    bgr[j + c] = alpha == 255 ? bgra[i + c] : (byte)((bgra[i + c] * alpha + 255 * (255 - alpha)) / 255);
            }

            (byte B, byte G, byte R) paper;
            if (used.Count > 0)
            {
                paper = (Middle(used.Select(c => c.B)), Middle(used.Select(c => c.G)), Middle(used.Select(c => c.R)));
            }
            else
            {
                // No line to take the paper from: the middle colour of the page (a page that is mostly paper, with a picture on it)
                var step = Math.Max(1, (int)Math.Sqrt((double)width * height / 40000));
                var sample = new List<(byte B, byte G, byte R)>();
                for (var y = 0; y < height; y += step)
                {
                    for (var x = 0; x < width; x += step)
                    {
                        var i = (y * width + x) * 3;
                        sample.Add((bgr[i], bgr[i + 1], bgr[i + 2]));
                    }
                }

                paper = sample.Count == 0 ? ((byte)255, (byte)255, (byte)255) : (Middle(sample.Select(c => c.B)), Middle(sample.Select(c => c.G)), Middle(sample.Select(c => c.R)));
            }

            return (new PhotoPixels(width, height, bgr), paper);
        }

        private static byte Middle(IEnumerable<byte> values)
        {
            var sorted = values.Order().ToList();
            return sorted.Count == 0 ? (byte)0 : sorted[sorted.Count / 2];
        }

        /// <summary>Paints a line over with the colour of the paper round it, which is the colour it gives back; null when there is no paper in sight round the line (it is left as it is).</summary>
        private static (byte B, byte G, byte R)? Paint(byte[] bgra, int width, int height, TextRegion line)
        {
            var centre = line.Centre;
            var inner = line.Corners.Select(p => new PointF(centre.X + (p.X - centre.X) * PaintAlong, centre.Y + (p.Y - centre.Y) * PaintAcross)).ToArray();
            var outer = line.Corners.Select(p => new PointF(centre.X + (p.X - centre.X) * 1.1f, centre.Y + (p.Y - centre.Y) * 2.2f)).ToArray();

            // The paper is the middle colour of the ring just outside the line (the line itself is mostly ink and shadow of the hand)
            var blue = new List<byte>();
            var green = new List<byte>();
            var red = new List<byte>();
            ForEachPoint(outer, width, height, 2, (x, y) =>
            {
                if (Inside(inner, x + 0.5f, y + 0.5f))
                    return;

                var i = (y * width + x) * 4;
                blue.Add(bgra[i]);
                green.Add(bgra[i + 1]);
                red.Add(bgra[i + 2]);
            });
            if (blue.Count < 20)
                return null; // nothing of the paper in sight: the line is left as it is

            blue.Sort();
            green.Sort();
            red.Sort();
            byte b = blue[blue.Count / 2], g = green[green.Count / 2], r = red[red.Count / 2];
            ForEachPoint(inner, width, height, 1, (x, y) =>
            {
                if (!Inside(inner, x + 0.5f, y + 0.5f))
                    return;

                var i = (y * width + x) * 4;
                bgra[i] = b;
                bgra[i + 1] = g;
                bgra[i + 2] = r;
                bgra[i + 3] = 255;
            });
            return (b, g, r);
        }

        private static void ForEachPoint(PointF[] polygon, int width, int height, int step, Action<int, int> action)
        {
            var left = Math.Max(0, (int)Math.Floor(polygon.Min(p => p.X)));
            var right = Math.Min(width - 1, (int)Math.Ceiling(polygon.Max(p => p.X)));
            var top = Math.Max(0, (int)Math.Floor(polygon.Min(p => p.Y)));
            var bottom = Math.Min(height - 1, (int)Math.Ceiling(polygon.Max(p => p.Y)));
            for (var y = top; y <= bottom; y += step)
            {
                for (var x = left; x <= right; x += step)
                    action(x, y);
            }
        }

        /// <summary>Whether a point is inside a convex quadrilateral.</summary>
        private static bool Inside(PointF[] quad, float x, float y)
        {
            var sign = 0;
            for (var i = 0; i < quad.Length; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) % quad.Length];
                var side = Math.Sign((b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X));
                if (side == 0)
                    continue;
                if (sign == 0)
                    sign = side;
                else if (sign != side)
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            foreach (var owned in _owned)
                owned.Dispose();
        }
    }
}
