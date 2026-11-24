using System.Drawing;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>A picture found on a page: the outline of its shape, in the pixels of the (upright) photo of the page. The shape is what the model that cuts out the Telegram stickers drew round it.</summary>
    public sealed record PagePicture(PointF[] Outline)
    {
        /// <summary>The smallest rectangle that holds the outline.</summary>
        public RectangleF Bounds
        {
            get
            {
                if (Outline.Length == 0)
                    return RectangleF.Empty;

                float left = Outline.Min(p => p.X), right = Outline.Max(p => p.X), top = Outline.Min(p => p.Y), bottom = Outline.Max(p => p.Y);
                return new RectangleF(left, top, right - left, bottom - top);
            }
        }

        /// <summary>The middle of <see cref="Bounds"/>.</summary>
        public PointF Centre
        {
            get
            {
                var bounds = Bounds;
                return new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
            }
        }
    }

    /// <summary>
    /// The geometry of finding pictures in what the model of the subject of a photo gives: the pieces of the map, how solid each is, and the outline of a piece (traced along its edge, then made
    /// of a few straight sides). A piece of handwriting that was left in the map is thin and ragged, a picture is a solid blob: that is what tells them apart.
    /// </summary>
    internal static class PictureOutline
    {
        /// <summary>A part of the map that is all above the threshold, in one piece (points that touch at a corner are one piece).</summary>
        internal sealed record Piece(int Label, int Area, int Left, int Top, int Right, int Bottom)
        {
            public int Width => Right - Left + 1;

            public int Height => Bottom - Top + 1;

            /// <summary>The share of its bounding rectangle that the piece fills: 1 for a rectangle, 0.79 for a disc, little for a thin ragged thing.</summary>
            public double Fill => Area / (double)(Width * Height);
        }

        /// <summary>The pieces of the points of <paramref name="map"/> that are at least <paramref name="threshold"/>; <paramref name="labels"/> says, for each point, the piece it is in (0 for none).</summary>
        internal static List<Piece> Pieces(float[] map, int width, int height, float threshold, out int[] labels)
        {
            labels = new int[map.Length];
            var pieces = new List<Piece>();
            var stack = new Stack<int>();
            for (var start = 0; start < map.Length; start++)
            {
                if (labels[start] != 0 || map[start] < threshold)
                    continue;

                var label = pieces.Count + 1;
                int area = 0, left = width, top = height, right = 0, bottom = 0;
                labels[start] = label;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var point = stack.Pop();
                    int x = point % width, y = point / width;
                    area++;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                                continue;

                            var next = ny * width + nx;
                            if (labels[next] == 0 && map[next] >= threshold)
                            {
                                labels[next] = label;
                                stack.Push(next);
                            }
                        }
                    }
                }

                pieces.Add(new Piece(label, area, left, top, right, bottom));
            }

            return pieces;
        }

        /// <summary>
        /// How much of the convex hull of a piece the piece itself fills (1 for a shape with no dents; a ring, a comb or a piece of ragged handwriting is much less). Rows are what the hull is made
        /// of: the first and the last point of each.
        /// </summary>
        internal static double Solidity(Piece piece, int[] labels, int width)
        {
            var points = new List<PointF>();
            for (var y = piece.Top; y <= piece.Bottom; y++)
            {
                int first = -1, last = -1;
                for (var x = piece.Left; x <= piece.Right; x++)
                {
                    if (labels[y * width + x] != piece.Label)
                        continue;

                    if (first < 0)
                        first = x;
                    last = x;
                }

                if (first >= 0)
                {
                    points.Add(new PointF(first, y));
                    points.Add(new PointF(last + 1, y));
                    points.Add(new PointF(first, y + 1));
                    points.Add(new PointF(last + 1, y + 1));
                }
            }

            var hull = PageOutline.ConvexHull(points);
            var hullArea = Math.Abs(SignedArea(hull));
            return hullArea <= 0 ? 0 : Math.Min(1.0, piece.Area / hullArea);
        }

        /// <summary>The outline of a piece as the corners of its edge, in points of the map (the outside edge of the first and last point of the piece along its edge: a hole in it is not followed).</summary>
        internal static List<PointF> Trace(Piece piece, int[] labels, int width, int height)
        {
            // The first point in reading order has nothing of the piece to its left and above it
            var startX = -1;
            var startY = piece.Top;
            for (var x = piece.Left; x <= piece.Right; x++)
            {
                if (labels[startY * width + x] == piece.Label)
                {
                    startX = x;
                    break;
                }
            }

            var edge = new List<Point>();
            if (startX < 0)
                return [];

            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && labels[y * width + x] == piece.Label;

            // Moore's neighbour tracing: round the piece, looking at the eight neighbours clockwise, starting from where the last step came from
            ReadOnlySpan<int> dx = [-1, -1, 0, 1, 1, 1, 0, -1];
            ReadOnlySpan<int> dy = [0, -1, -1, -1, 0, 1, 1, 1];
            int cx = startX, cy = startY, back = 0;
            Point? first = null;
            edge.Add(new Point(cx, cy));
            var guard = 4 * piece.Area + 16;
            while (guard-- > 0)
            {
                var found = -1;
                for (var k = 1; k <= 8; k++)
                {
                    var d = (back + k) % 8;
                    if (Inside(cx + dx[d], cy + dy[d]))
                    {
                        found = d;
                        break;
                    }
                }

                if (found < 0)
                    break; // a piece of one point

                // The point that was looked at just before the one that was found is where the next step looks from
                var lastEmpty = (found + 7) % 8;
                var emptyX = cx + dx[lastEmpty];
                var emptyY = cy + dy[lastEmpty];
                var next = new Point(cx + dx[found], cy + dy[found]);
                if (first is null)
                    first = next;
                else if (cx == startX && cy == startY && next == first)
                    break; // round again, and about to take the first step again

                cx = next.X;
                cy = next.Y;
                back = 0;
                for (var d = 0; d < 8; d++)
                {
                    if (cx + dx[d] == emptyX && cy + dy[d] == emptyY)
                    {
                        back = d;
                        break;
                    }
                }

                if (cx == startX && cy == startY)
                    continue;

                edge.Add(next);
            }

            // The corners of the points: the middle of each, and a point at the border of the map goes to the border, so that a picture the edge of the photo cuts is cut by it
            return [.. edge.Select(p => new PointF(p.X == 0 ? 0 : p.X == width - 1 ? width : p.X + 0.5f, p.Y == 0 ? 0 : p.Y == height - 1 ? height : p.Y + 0.5f))];
        }

        /// <summary>The closed shape with its corners thinned out (Douglas and Peucker): a point stays when it is farther than <paramref name="tolerance"/> from the straight side that would replace it.</summary>
        internal static List<PointF> Simplify(IReadOnlyList<PointF> shape, float tolerance)
        {
            if (shape.Count <= 4)
                return [.. shape];

            // A closed shape is cut in two at its first point and the point farthest from it, and each half is simplified as an open line
            var far = 0;
            var farthest = -1f;
            for (var i = 1; i < shape.Count; i++)
            {
                var d = Distance(shape[0], shape[i]);
                if (d > farthest)
                {
                    farthest = d;
                    far = i;
                }
            }

            var keep = new bool[shape.Count];
            keep[0] = keep[far] = true;
            Reduce(shape, 0, far, tolerance, keep);
            var second = shape.Concat([shape[0]]).ToList();
            var keepSecond = new bool[second.Count];
            keepSecond[0] = keepSecond[^1] = true;
            Reduce(second, far, second.Count - 1, tolerance, keepSecond);

            var result = new List<PointF>();
            for (var i = 0; i < shape.Count; i++)
            {
                if (keep[i] || (i >= far && keepSecond[i]))
                    result.Add(shape[i]);
            }

            return result;
        }

        private static void Reduce(IReadOnlyList<PointF> line, int from, int to, float tolerance, bool[] keep)
        {
            if (to <= from + 1)
                return;

            var worst = -1f;
            var index = -1;
            for (var i = from + 1; i < to; i++)
            {
                var d = DistanceToSegment(line[i], line[from], line[to]);
                if (d > worst)
                {
                    worst = d;
                    index = i;
                }
            }

            if (worst <= tolerance || index < 0)
                return;

            keep[index] = true;
            Reduce(line, from, index, tolerance, keep);
            Reduce(line, index, to, tolerance, keep);
        }

        private static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            var length2 = dx * dx + dy * dy;
            if (length2 <= 0)
                return Distance(p, a);

            var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length2, 0f, 1f);
            return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>The area of a polygon (positive when its corners go clockwise on a screen).</summary>
        internal static double SignedArea(IReadOnlyList<PointF> polygon)
        {
            double sum = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }

            return sum / 2;
        }
    }
}
