using System.Drawing;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>The geometry of finding a line of text: the smallest rectangle (turned as it must be) that holds some pixels.</summary>
    internal static class Geometry
    {
        /// <summary>The corners of the smallest convex shape that holds all the points, going round (in the order they are met in a picture, y down: clockwise on the screen).</summary>
        public static List<PointF> ConvexHull(IReadOnlyList<PointF> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count <= 2)
                return sorted;

            // Andrew's monotone chain
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

        /// <summary>
        /// The smallest rectangle (of any turn) that holds all the points: one of its sides lies along an edge of the hull, so each edge is tried. The rectangle is given with its
        /// length along the direction nearest to horizontal (a turn between -45 and 45 degrees), so a line of text is as long as it is wide.
        /// </summary>
        public static (PointF Centre, float Length, float Height, float Angle) SmallestRectangle(IReadOnlyList<PointF> points)
        {
            var hull = ConvexHull(points);
            if (hull.Count == 0)
                return (PointF.Empty, 0, 0, 0);

            if (hull.Count == 1)
                return (hull[0], 0, 0, 0);

            var best = (Area: float.MaxValue, Centre: PointF.Empty, Length: 0f, Height: 0f, Angle: 0f);
            for (var i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                var angle = MathF.Atan2(b.Y - a.Y, b.X - a.X);
                var ux = MathF.Cos(angle);
                var uy = MathF.Sin(angle);

                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (var p in hull)
                {
                    var u = p.X * ux + p.Y * uy;
                    var v = -p.X * uy + p.Y * ux;
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                }

                var length = maxU - minU;
                var height = maxV - minV;
                var area = length * height;
                if (area < best.Area - 1e-4f)
                {
                    var cu = (minU + maxU) / 2f;
                    var cv = (minV + maxV) / 2f;
                    best = (area, new PointF(cu * ux - cv * uy, cu * uy + cv * ux), length, height, angle);
                }
            }

            // Along the nearer to horizontal of the two directions of the rectangle
            var (centre, rectLength, rectHeight, rectAngle) = (best.Centre, best.Length, best.Height, best.Angle);
            while (rectAngle >= MathF.PI / 4f)
            {
                rectAngle -= MathF.PI / 2f;
                (rectLength, rectHeight) = (rectHeight, rectLength);
            }
            while (rectAngle < -MathF.PI / 4f)
            {
                rectAngle += MathF.PI / 2f;
                (rectLength, rectHeight) = (rectHeight, rectLength);
            }
            return (centre, rectLength, rectHeight, rectAngle);
        }
    }
}
