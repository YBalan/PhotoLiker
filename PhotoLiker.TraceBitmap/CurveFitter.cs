namespace PhotoLiker.TraceBitmap
{
    /// <summary>
    /// Turns an outline along the pixel edges (<see cref="PixelLoop"/>) into straight lines and cubic Bezier curves.
    /// <list type="number">
    /// <item>The outline is simplified to a polygon whose sides stay within a pixel of the steps (Douglas-Peucker).</item>
    /// <item>A corner of the polygon stays a sharp corner when it sticks out far enough from the line between its two neighbours; the others are smoothed. How far is the "corner threshold" of
    /// Inkscape (the alphamax of Potrace): 0 keeps every corner, about 1.3 smooths every one.</item>
    /// <item>Between two sharp corners the middles of the pixel edges (they lie on the line a staircase of pixels stands for, which the corners of the steps do not) are fitted with Bezier
    /// curves, as few as keep within the tolerance (Schneider's algorithm).</item>
    /// </list>
    /// </summary>
    internal static class CurveFitter
    {
        /// <summary>How far, in pixels, the sides of the first polygon may be from the steps of the outline.</summary>
        private const double PolygonTolerance = 1.0;

        /// <summary>A curve whose control points are this close (in pixels) to the line between its ends is written as a line.</summary>
        private const double FlatEnough = 0.01;

        private const int MaxDepth = 48;

        public static TracePath Fit(PixelLoop loop, TraceSettings settings)
        {
            var corners = Enumerable.Range(0, loop.Count).Select(i => new TracePoint(loop.X[i], loop.Y[i])).ToArray();

            // A shape of a few pixels is not worth a curve: its outline is the outline
            var polygonOnly = !settings.SmoothCorners || settings.CornerThreshold <= 0;
            if (loop.Length <= 8)
                return Polygon(corners);

            var polygon = Simplify(corners, PolygonTolerance);
            if (polygon.Length < 3)
                return Polygon(corners);

            var sharp = new bool[polygon.Length];
            var sharpCount = 0;
            for (var j = 0; j < polygon.Length; j++)
            {
                var previous = corners[polygon[(j + polygon.Length - 1) % polygon.Length]];
                var current = corners[polygon[j]];
                var next = corners[polygon[(j + 1) % polygon.Length]];
                sharp[j] = polygonOnly || IsSharp(CornerAlpha(previous, current, next), settings.CornerThreshold);
                if (sharp[j])
                    sharpCount++;
            }

            // Corners everywhere (or the person asked for no smoothing at all): the polygon is the answer
            if (polygonOnly)
                return Polygon(polygon.Select(index => corners[index]).ToArray());

            // How far, in pixels, a curve may be from the middles of the pixel edges it replaces. The edges of a drawn circle are themselves up to half a pixel from it, so under that the fit
            // would follow the steps and make a curve for each few of them.
            var tolerance = settings.OptimizePaths ? 0.5 + 1.5 * settings.OptimizeTolerance : 0.5;
            var segments = new List<TraceSegment>();
            TracePoint start;

            if (sharpCount == 0)
            {
                // A smooth outline with no corner: two halves that meet where the middle of the outline is, with the same direction on both sides
                var middles = Midpoints(corners, 0, corners.Length, closed: true);
                var half = middles.Count / 2;
                var around = Tangent(middles, 0, closed: true);
                var opposite = Tangent(middles, half, closed: true);
                start = middles[0];
                FitPoints(middles.GetRange(0, half + 1), around, opposite * -1, tolerance, segments);
                var second = middles.GetRange(half, middles.Count - half);
                second.Add(middles[0]);
                FitPoints(second, opposite, around * -1, tolerance, segments);
            }
            else
            {
                var sharpIndexes = Enumerable.Range(0, polygon.Length).Where(j => sharp[j]).ToArray();
                start = corners[polygon[sharpIndexes[0]]];
                for (var s = 0; s < sharpIndexes.Length; s++)
                {
                    var from = sharpIndexes[s];
                    var to = sharpIndexes[(s + 1) % sharpIndexes.Length];
                    var fromCorner = polygon[from];
                    var toCorner = polygon[to];
                    var adjacent = (from + 1) % polygon.Length == to && sharpIndexes.Length > 1;
                    if (adjacent)
                    {
                        // The sides between two sharp corners with no smooth corner in between are straight
                        segments.Add(TraceSegment.Line(corners[toCorner]));
                        continue;
                    }

                    var data = new List<TracePoint> { corners[fromCorner] };
                    data.AddRange(Midpoints(corners, fromCorner, toCorner, closed: false));
                    data.Add(corners[toCorner]);
                    FitPoints(data, EndTangent(data, atStart: true), EndTangent(data, atStart: false), tolerance, segments);
                }
            }

            return new TracePath(start, segments);
        }

        private static TracePath Polygon(TracePoint[] points)
        {
            var segments = new List<TraceSegment>(points.Length);
            for (var i = 1; i < points.Length; i++)
                segments.Add(TraceSegment.Line(points[i]));
            segments.Add(TraceSegment.Line(points[0]));
            return new TracePath(points[0], segments);
        }

        /// <summary>
        /// How sharp a corner is, by the measure of Potrace's smoothing: the distance of the corner from the line through its two neighbours (taken in pixels, a pixel being what it is about)
        /// gives 0 for a corner that sticks out by a pixel or less and goes up towards 4/3 as it sticks out more. A corner is kept sharp when this is at least the threshold.
        /// </summary>
        internal static double CornerAlpha(TracePoint previous, TracePoint current, TracePoint next)
        {
            var across = next - previous;
            var denominator = Math.Abs(across.X) + Math.Abs(across.Y);
            if (denominator < 1e-9)
                return 4.0 / 3.0;

            var cross = (current.X - previous.X) * (next.Y - previous.Y) - (next.X - previous.X) * (current.Y - previous.Y);
            var distance = Math.Abs(cross) / denominator;
            var alpha = distance > 1 ? 1 - 1.0 / distance : 0;
            return alpha / 0.75;
        }

        /// <summary>A corner whose alpha is at least the corner threshold stays sharp; the threshold itself is sharp (a threshold of 0 keeps every corner).</summary>
        internal static bool IsSharp(double alpha, double threshold) => alpha >= threshold;
        // ── The polygon ──────────────────────────────────────────────────

        /// <summary>The indexes of the points a closed polyline can be reduced to with no side more than <paramref name="tolerance"/> from the points it replaces (Douglas-Peucker, in two halves).</summary>
        internal static int[] Simplify(TracePoint[] points, double tolerance)
        {
            var n = points.Length;
            if (n <= 3)
                return Enumerable.Range(0, n).ToArray();

            // The two halves of the closed outline: from the first point to the one farthest from it, and back
            var far = 0;
            var farDistance = -1.0;
            for (var i = 1; i < n; i++)
            {
                var distance = points[0].DistanceTo(points[i]);
                if (distance > farDistance)
                {
                    farDistance = distance;
                    far = i;
                }
            }

            var keep = new bool[n];
            keep[0] = true;
            keep[far] = true;
            Reduce(points, 0, far, tolerance, keep);
            Reduce(points, far, n, tolerance, keep); // the last stretch ends at point 0 again

            return Enumerable.Range(0, n).Where(i => keep[i]).ToArray();
        }

        private static void Reduce(TracePoint[] points, int first, int last, double tolerance, bool[] keep)
        {
            var n = points.Length;
            var pending = new Stack<(int First, int Last)>();
            pending.Push((first, last));
            while (pending.Count > 0)
            {
                var (from, to) = pending.Pop();
                if (to - from < 2)
                    continue;

                var a = points[from % n];
                var b = points[to % n];
                var worst = -1.0;
                var at = -1;
                for (var i = from + 1; i < to; i++)
                {
                    var distance = DistanceToSegment(points[i % n], a, b);
                    if (distance > worst)
                    {
                        worst = distance;
                        at = i;
                    }
                }

                if (worst > tolerance && at > 0)
                {
                    keep[at % n] = true;
                    pending.Push((from, at));
                    pending.Push((at, to));
                }
            }
        }

        private static double DistanceToSegment(TracePoint p, TracePoint a, TracePoint b)
        {
            var ab = b - a;
            var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
            if (lengthSquared < 1e-12)
                return p.DistanceTo(a);

            var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared, 0, 1);
            return p.DistanceTo(a + ab * t);
        }

        // ── The data the curves are fitted to ────────────────────────────

        /// <summary>
        /// The middle of each pixel edge between corner number <paramref name="from"/> and corner number <paramref name="to"/> (exclusive of the corners themselves). With <paramref name="closed"/>
        /// the whole outline, from the first corner round to the first again.
        /// </summary>
        private static List<TracePoint> Midpoints(TracePoint[] corners, int from, int to, bool closed)
        {
            var n = corners.Length;
            var result = new List<TracePoint>();
            var count = closed ? n : ((to - from) % n + n) % n;
            if (!closed && count == 0)
                count = n; // one corner only: all the way round and back to it

            for (var step = 0; step < count; step++)
            {
                var a = corners[(from + step) % n];
                var b = corners[(from + step + 1) % n];
                var delta = b - a;
                var length = (int)Math.Round(delta.Length);
                if (length <= 0)
                    continue;

                var unit = delta * (1.0 / length);
                for (var k = 0; k < length; k++)
                    result.Add(a + unit * (k + 0.5));
            }

            return result;
        }

        private static TracePoint Normalize(TracePoint v)
        {
            var length = v.Length;
            return length < 1e-12 ? new TracePoint(0, 0) : v * (1.0 / length);
        }

        /// <summary>The direction of the outline at point <paramref name="index"/>, from the points two before and two after it.</summary>
        private static TracePoint Tangent(List<TracePoint> points, int index, bool closed)
        {
            var n = points.Count;
            TracePoint At(int i) => closed ? points[((i % n) + n) % n] : points[Math.Clamp(i, 0, n - 1)];
            return Normalize(At(index + 2) - At(index - 2));
        }

        /// <summary>The direction a run of points leaves its first point in (or, for the end, leaves its last point backwards), taken over about three pixels so that one step of a staircase does not decide it.</summary>
        private static TracePoint EndTangent(List<TracePoint> points, bool atStart)
        {
            var n = points.Count;
            var origin = atStart ? points[0] : points[n - 1];
            for (var k = 1; k < n; k++)
            {
                var other = atStart ? points[k] : points[n - 1 - k];
                if (origin.DistanceTo(other) >= 3.0 || k == n - 1)
                    return Normalize(other - origin);
            }

            return new TracePoint(0, 0);
        }

        // ── Schneider's curve fitting ────────────────────────────────────

        /// <summary>Fits the points with cubic Bezier curves (appended to <paramref name="output"/>), the first and the last points being the ends and the two directions being the way the curve leaves them.</summary>
        private static void FitPoints(List<TracePoint> points, TracePoint tangentStart, TracePoint tangentEnd, double tolerance, List<TraceSegment> output)
        {
            FitRange([.. points], 0, points.Count - 1, tangentStart, tangentEnd, tolerance * tolerance, output, 0);
        }

        private static void FitRange(TracePoint[] d, int first, int last, TracePoint tangent1, TracePoint tangent2, double errorSquared, List<TraceSegment> output, int depth)
        {
            var count = last - first + 1;
            if (count <= 2)
            {
                // Two points: a line, or a curve with a third of the way as the handles when the directions say it is not straight
                var distance = d[first].DistanceTo(d[last]) / 3.0;
                AddCurve(output, d[first], d[first] + tangent1 * distance, d[last] + tangent2 * distance, d[last]);
                return;
            }

            var u = ChordLengths(d, first, last);
            var bezier = GenerateBezier(d, first, last, u, tangent1, tangent2);
            var (maxError, split) = MaxError(d, first, last, bezier, u);
            if (maxError < errorSquared)
            {
                AddCurve(output, bezier[0], bezier[1], bezier[2], bezier[3]);
                return;
            }

            // Close: try again with better places on the curve for the points
            if (maxError < errorSquared * 16)
            {
                for (var iteration = 0; iteration < 4; iteration++)
                {
                    u = Reparameterize(d, first, last, u, bezier);
                    bezier = GenerateBezier(d, first, last, u, tangent1, tangent2);
                    (maxError, split) = MaxError(d, first, last, bezier, u);
                    if (maxError < errorSquared)
                    {
                        AddCurve(output, bezier[0], bezier[1], bezier[2], bezier[3]);
                        return;
                    }
                }
            }

            if (depth >= MaxDepth || split <= first || split >= last)
            {
                // Cannot get closer: straight lines through the points that are left
                for (var i = first + 1; i <= last; i++)
                    output.Add(TraceSegment.Line(d[i]));
                return;
            }

            // Cut at the worst point, the curve having the same direction on both sides of the cut
            var center = Normalize(d[Math.Max(first, split - 2)] - d[Math.Min(last, split + 2)]);
            FitRange(d, first, split, tangent1, center, errorSquared, output, depth + 1);
            FitRange(d, split, last, center * -1, tangent2, errorSquared, output, depth + 1);
        }

        private static void AddCurve(List<TraceSegment> output, TracePoint p0, TracePoint c1, TracePoint c2, TracePoint p3)
        {
            var flat = DistanceToSegment(c1, p0, p3) <= FlatEnough && DistanceToSegment(c2, p0, p3) <= FlatEnough;
            output.Add(flat ? TraceSegment.Line(p3) : TraceSegment.Curve(c1, c2, p3));
        }

        private static double[] ChordLengths(TracePoint[] d, int first, int last)
        {
            var u = new double[last - first + 1];
            for (var i = first + 1; i <= last; i++)
                u[i - first] = u[i - first - 1] + d[i].DistanceTo(d[i - 1]);

            var total = u[last - first];
            for (var i = 1; i < u.Length; i++)
                u[i] = total > 0 ? u[i] / total : (double)i / (u.Length - 1);
            return u;
        }

        private static TracePoint[] GenerateBezier(TracePoint[] d, int first, int last, double[] u, TracePoint tangent1, TracePoint tangent2)
        {
            var count = last - first + 1;
            double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
            var p0 = d[first];
            var p3 = d[last];
            for (var i = 0; i < count; i++)
            {
                var t = u[i];
                var s = 1 - t;
                double b0 = s * s * s, b1 = 3 * t * s * s, b2 = 3 * t * t * s, b3 = t * t * t;
                var a1 = tangent1 * b1;
                var a2 = tangent2 * b2;
                c00 += a1.X * a1.X + a1.Y * a1.Y;
                c01 += a1.X * a2.X + a1.Y * a2.Y;
                c11 += a2.X * a2.X + a2.Y * a2.Y;
                var rest = d[first + i] - (p0 * (b0 + b1) + p3 * (b2 + b3));
                x0 += a1.X * rest.X + a1.Y * rest.Y;
                x1 += a2.X * rest.X + a2.Y * rest.Y;
            }

            var determinant = c00 * c11 - c01 * c01;
            var alphaStart = Math.Abs(determinant) < 1e-12 ? 0 : (x0 * c11 - x1 * c01) / determinant;
            var alphaEnd = Math.Abs(determinant) < 1e-12 ? 0 : (c00 * x1 - c01 * x0) / determinant;

            var segmentLength = p0.DistanceTo(p3);
            var epsilon = 1e-6 * segmentLength;

            // A handle longer than the whole run of points it is fitted to makes a loop that flies far out of the shape between the points (the fit of an outline that ends near where it began
            // can ask for one thousands of pixels long)
            var runLength = 0.0;
            for (var i = first + 1; i <= last; i++)
                runLength += d[i].DistanceTo(d[i - 1]);

            if (alphaStart < epsilon || alphaEnd < epsilon || alphaStart > runLength || alphaEnd > runLength || double.IsNaN(alphaStart) || double.IsNaN(alphaEnd))
            {
                // The fit has the handles pointing the wrong way, or out of all reason: a third of the way, the usual guess
                alphaStart = alphaEnd = segmentLength / 3.0;
            }

            return [p0, p0 + tangent1 * alphaStart, p3 + tangent2 * alphaEnd, p3];
        }

        private static TracePoint BezierAt(TracePoint[] v, double t)
        {
            var s = 1 - t;
            return v[0] * (s * s * s) + v[1] * (3 * t * s * s) + v[2] * (3 * t * t * s) + v[3] * (t * t * t);
        }

        private static (double Error, int Split) MaxError(TracePoint[] d, int first, int last, TracePoint[] bezier, double[] u)
        {
            var split = (last - first + 1) / 2 + first;
            var worst = 0.0;
            for (var i = first + 1; i < last; i++)
            {
                var difference = BezierAt(bezier, u[i - first]) - d[i];
                var distance = difference.X * difference.X + difference.Y * difference.Y;
                if (distance >= worst)
                {
                    worst = distance;
                    split = i;
                }
            }

            return (worst, split);
        }

        /// <summary>One step of Newton-Raphson for each point: the place on the curve that is nearest to it.</summary>
        private static double[] Reparameterize(TracePoint[] d, int first, int last, double[] u, TracePoint[] bezier)
        {
            var result = new double[u.Length];
            for (var i = 0; i < u.Length; i++)
            {
                var t = u[i];
                var q = BezierAt(bezier, t);
                var s = 1 - t;

                // The first and the second derivative of the curve
                var q1 = (bezier[1] - bezier[0]) * (3 * s * s) + (bezier[2] - bezier[1]) * (6 * s * t) + (bezier[3] - bezier[2]) * (3 * t * t);
                var q2 = (bezier[2] - bezier[1] * 2 + bezier[0]) * (6 * s) + (bezier[3] - bezier[2] * 2 + bezier[1]) * (6 * t);

                var difference = q - d[first + i];
                var numerator = difference.X * q1.X + difference.Y * q1.Y;
                var denominator = q1.X * q1.X + q1.Y * q1.Y + difference.X * q2.X + difference.Y * q2.Y;
                result[i] = Math.Abs(denominator) < 1e-12 ? t : Math.Clamp(t - numerator / denominator, 0, 1);
            }

            // The ends stay where they are, and the places must keep their order
            result[0] = 0;
            result[^1] = 1;
            for (var i = 1; i < result.Length; i++)
                result[i] = Math.Max(result[i], result[i - 1]);
            return result;
        }
    }
}
