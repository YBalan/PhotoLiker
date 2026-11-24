using System.Drawing;

namespace PhotoLiker.Manuscripts.Pages
{
    /// <summary>The four corners of a sheet of paper in a photo, in the pixels of the photo, in the order top left, top right, bottom right, bottom left.</summary>
    public readonly record struct PageQuad(PointF TopLeft, PointF TopRight, PointF BottomRight, PointF BottomLeft)
    {
        public PointF[] Corners => [TopLeft, TopRight, BottomRight, BottomLeft];

        /// <summary>The length of the top and bottom sides on average (the width of the sheet as it is seen).</summary>
        public float Width => (Distance(TopLeft, TopRight) + Distance(BottomLeft, BottomRight)) / 2f;

        /// <summary>The length of the left and right sides on average.</summary>
        public float Height => (Distance(TopLeft, BottomLeft) + Distance(TopRight, BottomRight)) / 2f;

        /// <summary>The area of the quadrilateral (the shoelace formula).</summary>
        public float Area
        {
            get
            {
                var c = Corners;
                double sum = 0;
                for (var i = 0; i < 4; i++)
                {
                    var a = c[i];
                    var b = c[(i + 1) % 4];
                    sum += (double)a.X * b.Y - (double)b.X * a.Y;
                }

                return (float)Math.Abs(sum) / 2f;
            }
        }

        private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    /// <summary>
    /// Finds the sheet of paper in a photo from the map of its subject that the model of the Telegram stickers gives (how likely each point is part of the thing the photo is about): the points that
    /// are sure enough, the largest piece of them (a sheet on a desk is the largest thing in a photo of it; a pen or a hand next to it is smaller), the smallest convex shape round it, and four
    /// corners of that. A sheet that is photographed from an angle is a quadrilateral, not a rectangle, so the corners are not forced to be square.
    /// </summary>
    public static class PageOutline
    {
        /// <summary>
        /// A point of the map is part of the sheet when the model is at least this sure of it. It is low: the model is sure of the writing and the ruling on a sheet (what it takes for the subject) and hardly
        /// of the paper between and the edge of it, and at 0.5 the pieces it gives are the lines of writing; at 0.1 they run together and the shape round them is the sheet.
        /// </summary>
        public const float Threshold = 0.1f;

        /// <summary>A sheet whose corners are all this near (a share of the width and the height) the corners of the photo is the whole photo: there is no desk round it to cut away.</summary>
        public const float FillsPhoto = 0.04f;

        /// <summary>A sheet is at least this share of the photo, or what was found is something else (a photo that is all text, with no desk round it, is the sheet itself).</summary>
        public const float MinimumShare = 0.2f;

        /// <summary>
        /// The four corners of the sheet in the photo, or null when there is no sheet (nothing found, a piece too small, or a shape that is no four-sided sheet). <paramref name="map"/> is
        /// <paramref name="mapWidth"/> by <paramref name="mapHeight"/> points, which is the photo made that size whatever its shape.
        /// </summary>
        public static PageQuad? Find(float[] map, int mapWidth, int mapHeight, int photoWidth, int photoHeight) =>
            Find(map, mapWidth, mapHeight, photoWidth, photoHeight, Threshold);

        internal static PageQuad? Find(float[] map, int mapWidth, int mapHeight, int photoWidth, int photoHeight, float threshold)
        {
            if (map.Length != mapWidth * mapHeight)
                throw new ArgumentException($"A map of {mapWidth} by {mapHeight} has {mapWidth * mapHeight} points, not {map.Length}.", nameof(map));

            // The model draws the sheet as an outline with what is on it (the ruling, the writing) rather than as a filled shape, so the piece of points that are sure is a net, and it is the shape
            // round the net that is the sheet: its area, not the number of its points, is what must be a share of the photo
            var piece = LargestPiece(map, mapWidth, mapHeight, threshold);
            if (piece.Count < 16)
                return null;

            var hull = ConvexHull(piece.EdgePoints);
            if (hull.Count < 3)
                return null;

            var hullArea = Area(hull);
            if (hullArea < MinimumShare * mapWidth * mapHeight)
                return null;

            // The four-sided shape that holds the whole of it, as small as can be made: a sheet is one, a round thing or a heap of things is not (it would need a quadrilateral far larger than itself)
            if (EnclosingQuad(hull) is not { } quad || quad.Area > 1.35f * hullArea)
                return null;

            if (FillsTheMap(quad, mapWidth, mapHeight))
                return null;

            // On a plain page the model may outline only the writing, which makes a shape that is no sheet
            if (!IsPlausible(quad))
                return null;

            var scaleX = (float)photoWidth / mapWidth;
            var scaleY = (float)photoHeight / mapHeight;
            PointF Back(PointF p) => new(p.X * scaleX, p.Y * scaleY);
            return new PageQuad(Back(quad.TopLeft), Back(quad.TopRight), Back(quad.BottomRight), Back(quad.BottomLeft));
        }

        /// <summary>
        /// The quadrilateral whose sides are the lines that touch the convex shape on the outside, each in the direction of a side of <paramref name="quad"/>: it holds the whole shape. Null when two
        /// neighbouring sides are parallel.
        /// </summary>
        internal static PageQuad? Circumscribed(PageQuad quad, IReadOnlyList<PointF> hull)
        {
            var corners = quad.Corners;
            var centre = new PointF(corners.Average(c => c.X), corners.Average(c => c.Y));
            var lines = new (PointF Point, PointF Direction)[4];
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                var length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                if (length < 1e-3f)
                    return null;

                var direction = new PointF((b.X - a.X) / length, (b.Y - a.Y) / length);
                var normal = new PointF(direction.Y, -direction.X);
                if ((centre.X - a.X) * normal.X + (centre.Y - a.Y) * normal.Y > 0)
                    normal = new PointF(-normal.X, -normal.Y); // pointing away from the middle

                var farthest = hull.Max(p => (p.X - a.X) * normal.X + (p.Y - a.Y) * normal.Y);
                lines[i] = (new PointF(a.X + normal.X * farthest, a.Y + normal.Y * farthest), direction);
            }

            var result = new PointF[4];
            for (var i = 0; i < 4; i++)
            {
                // The corner after side i is where side i meets side i + 1
                var (p1, d1) = lines[i];
                var (p2, d2) = lines[(i + 1) % 4];
                var denominator = d1.X * d2.Y - d1.Y * d2.X;
                if (Math.Abs(denominator) < 1e-6f)
                    return null;

                var t = ((p2.X - p1.X) * d2.Y - (p2.Y - p1.Y) * d2.X) / denominator;
                result[(i + 1) % 4] = new PointF(p1.X + d1.X * t, p1.Y + d1.Y * t);
            }

            return new PageQuad(result[0], result[1], result[2], result[3]);
        }

        /// <summary>The longest a side of a sheet seen at an angle is, against the one opposite it (a sheet is a rectangle, so its opposite sides are alike but for perspective).</summary>
        private const float MaximumSideRatio = 1.7f;

        /// <summary>The corners of a sheet are right angles but for perspective: none is sharper or blunter than this many degrees from a right angle.</summary>
        private const float MaximumAngleFromRight = 35f;

        /// <summary>Whether a quadrilateral is a sheet of paper seen at an angle: its opposite sides about as long, its corners about right angles.</summary>
        internal static bool IsPlausible(PageQuad quad)
        {
            var c = quad.Corners;
            float Side(int i) => MathF.Sqrt((c[(i + 1) % 4].X - c[i].X) * (c[(i + 1) % 4].X - c[i].X) + (c[(i + 1) % 4].Y - c[i].Y) * (c[(i + 1) % 4].Y - c[i].Y));
            var (top, right, bottom, left) = (Side(0), Side(1), Side(2), Side(3));
            if (Math.Min(Math.Min(top, bottom), Math.Min(left, right)) < 1e-3f)
                return false;
            if (Math.Max(top, bottom) / Math.Min(top, bottom) > MaximumSideRatio || Math.Max(left, right) / Math.Min(left, right) > MaximumSideRatio)
                return false;

            for (var i = 0; i < 4; i++)
            {
                var before = c[(i + 3) % 4];
                var corner = c[i];
                var after = c[(i + 1) % 4];
                var angle = MathF.Abs(MathF.Atan2(Cross(corner, before, after), Dot(corner, before, after))) * 180f / MathF.PI;
                if (Math.Abs(angle - 90f) > MaximumAngleFromRight)
                    return false;
            }

            return true;
        }

        private static float Dot(PointF o, PointF a, PointF b) => (a.X - o.X) * (b.X - o.X) + (a.Y - o.Y) * (b.Y - o.Y);

        private static bool FillsTheMap(PageQuad quad, int width, int height)
        {
            bool Near(PointF corner, float x, float y) => Math.Abs(corner.X - x) <= FillsPhoto * width && Math.Abs(corner.Y - y) <= FillsPhoto * height;
            return Near(quad.TopLeft, 0, 0) && Near(quad.TopRight, width, 0) && Near(quad.BottomRight, width, height) && Near(quad.BottomLeft, 0, height);
        }

        internal sealed record Piece(int Count, List<PointF> EdgePoints);

        /// <summary>The largest group of points that are sure enough and touch each other (up, down, left, right), with the points of its edge.</summary>
        internal static Piece LargestPiece(float[] map, int width, int height, float threshold = Threshold)
        {
            var seen = new bool[map.Length];
            var queue = new int[map.Length];
            Piece best = new(0, []);

            for (var start = 0; start < map.Length; start++)
            {
                if (seen[start] || map[start] < threshold)
                    continue;

                var head = 0;
                var tail = 0;
                queue[tail++] = start;
                seen[start] = true;
                var edge = new List<PointF>();
                while (head < tail)
                {
                    var i = queue[head++];
                    var x = i % width;
                    var y = i / width;
                    var onEdge = false;
                    onEdge |= Visit(x - 1, y, x > 0);
                    onEdge |= Visit(x + 1, y, x < width - 1);
                    onEdge |= Visit(x, y - 1, y > 0);
                    onEdge |= Visit(x, y + 1, y < height - 1);
                    if (onEdge)
                    {
                        // The four corners of the point, so that the hull holds the whole of it
                        edge.Add(new PointF(x, y));
                        edge.Add(new PointF(x + 1, y));
                        edge.Add(new PointF(x, y + 1));
                        edge.Add(new PointF(x + 1, y + 1));
                    }
                }

                if (tail > best.Count)
                    best = new Piece(tail, edge);

                bool Visit(int nx, int ny, bool inside)
                {
                    if (!inside)
                        return true;
                    var n = ny * width + nx;
                    if (map[n] < threshold)
                        return true;
                    if (!seen[n])
                    {
                        seen[n] = true;
                        queue[tail++] = n;
                    }

                    return false;
                }
            }

            return best;
        }

        /// <summary>Andrew's monotone chain: the corners of the smallest convex shape that holds all the points.</summary>
        internal static List<PointF> ConvexHull(IReadOnlyList<PointF> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count <= 2)
                return sorted;

            var hull = new List<PointF>();
            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            var lower = hull.Count + 1;
            for (var i = sorted.Count - 2; i >= 0; i--)
            {
                var p = sorted[i];
                while (hull.Count >= lower && Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float Cross(PointF o, PointF a, PointF b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        private static float Area(IReadOnlyList<PointF> polygon)
        {
            double sum = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }

            return (float)Math.Abs(sum) / 2f;
        }

        /// <summary>
        /// The quadrilateral that holds a convex polygon and is as small as can be got by this way: the side of the polygon whose removal adds the least area (its two neighbours are
        /// extended until they meet) is taken away, again and again, until four sides are left. The many short sides of a rounded corner go first, so each rounded corner of a sheet
        /// becomes the point where its two long sides meet, and nothing of the sheet is cut off. Null when there is no way down to four sides (a triangle, a polygon whose neighbouring
        /// sides never meet).
        /// </summary>
        internal static PageQuad? EnclosingQuad(IReadOnlyList<PointF> hull)
        {
            if (hull.Count < 4)
                return null;

            var polygon = hull.ToList();
            while (polygon.Count > 4)
            {
                var n = polygon.Count;
                var bestIndex = -1;
                var bestAdded = float.MaxValue;
                var bestPoint = PointF.Empty;
                for (var i = 0; i < n; i++)
                {
                    var a = polygon[(i + n - 1) % n];
                    var b = polygon[i];
                    var c = polygon[(i + 1) % n];
                    var d = polygon[(i + 2) % n];
                    if (Meet(a, b, d, c) is not { } p)
                        continue;

                    // The two sides must meet beyond the ends of the side that is taken away (the polygon is convex, so they do when their directions turn towards each other)
                    if ((p.X - b.X) * (b.X - a.X) + (p.Y - b.Y) * (b.Y - a.Y) < 0 || (p.X - c.X) * (c.X - d.X) + (p.Y - c.Y) * (c.Y - d.Y) < 0)
                        continue;

                    var added = Math.Abs(Cross(b, p, c)) / 2f;
                    if (added < bestAdded)
                    {
                        bestAdded = added;
                        bestIndex = i;
                        bestPoint = p;
                    }
                }

                if (bestIndex < 0)
                    return null;

                // The side from bestIndex to the next point is replaced by the point where its neighbours meet
                var rotated = polygon.Skip(bestIndex).Concat(polygon.Take(bestIndex)).ToList();
                polygon = [bestPoint, .. rotated.Skip(2)];
            }

            var centre = new PointF(polygon.Average(p => p.X), polygon.Average(p => p.Y));
            return Order(polygon, centre.X, centre.Y);
        }

        /// <summary>Where the line through <paramref name="a"/> and <paramref name="b"/> meets the line through <paramref name="c"/> and <paramref name="d"/>; null when they are parallel.</summary>
        private static PointF? Meet(PointF a, PointF b, PointF c, PointF d)
        {
            var d1 = new PointF(b.X - a.X, b.Y - a.Y);
            var d2 = new PointF(d.X - c.X, d.Y - c.Y);
            var denominator = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(denominator) < 1e-6f)
                return null;

            var t = ((c.X - a.X) * d2.Y - (c.Y - a.Y) * d2.X) / denominator;
            return new PointF(a.X + d1.X * t, a.Y + d1.Y * t);
        }

        /// <summary>The corners as top left, top right, bottom right, bottom left: the corner nearest the top left of the map by x + y is the top left, and so on round, which holds for a sheet turned by less than a quarter.</summary>
        internal static PageQuad Order(IReadOnlyList<PointF> corners, float centreX, float centreY)
        {
            // By the angle round the middle, from the one that is up and to the left, clockwise on the screen
            var ordered = corners.OrderBy(p => Math.Atan2(p.Y - centreY, p.X - centreX)).ToList();
            // Atan2 runs from -pi at the left, through -pi/2 at the top, to pi at the left again: the first corner after the left-top one is the top left
            var topLeft = ordered.OrderBy(p => p.X + p.Y).First();
            var start = ordered.IndexOf(topLeft);
            var turned = Enumerable.Range(0, 4).Select(i => ordered[(start + i) % 4]).ToList();
            return new PageQuad(turned[0], turned[1], turned[2], turned[3]);
        }
    }
}
