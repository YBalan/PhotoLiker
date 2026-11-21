using System.Globalization;
using System.Text;

namespace PhotoLiker.TraceBitmap
{
    /// <summary>
    /// Writes a trace as a DXF file in millimetres, in the plain old R12 form that every CAD program reads: a closed POLYLINE for each outline (a hole is just another outline inside), one layer
    /// for each colour of the trace. A DXF has no Bezier curves, so each curve is cut into short straight pieces that stay within <c>accuracy</c> millimetres of it. The picture's y goes down and
    /// a drawing's y goes up, so the drawing is turned over to look the way the picture does.
    /// </summary>
    public static class DxfWriter
    {
        public static string Write(TraceResult trace, double widthMillimetres, double heightMillimetres, double accuracyMillimetres = TraceDialogSettings.DefaultCurveAccuracy)
        {
            ArgumentNullException.ThrowIfNull(trace);
            Sizes.Check(widthMillimetres, heightMillimetres);
            if (!double.IsFinite(accuracyMillimetres) || accuracyMillimetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(accuracyMillimetres), accuracyMillimetres, "The accuracy has to be more than nothing.");

            var scaleX = widthMillimetres / trace.Width;
            var scaleY = heightMillimetres / trace.Height;
            var layers = trace.Layers.Where(layer => layer.Paths.Count > 0).ToList();

            var text = new StringBuilder();
            Pair(text, 999, "Written by PhotoLiker (Trace Bitmap). Units: millimetres.");
            Pair(text, 0, "SECTION");
            Pair(text, 2, "HEADER");
            Pair(text, 9, "$ACADVER");
            Pair(text, 1, "AC1009");
            Pair(text, 9, "$INSUNITS");
            Pair(text, 70, "4");
            Pair(text, 9, "$MEASUREMENT");
            Pair(text, 70, "1");
            Pair(text, 9, "$EXTMIN");
            Pair(text, 10, "0");
            Pair(text, 20, "0");
            Pair(text, 30, "0");
            Pair(text, 9, "$EXTMAX");
            Pair(text, 10, SvgWriter.Number(widthMillimetres));
            Pair(text, 20, SvgWriter.Number(heightMillimetres));
            Pair(text, 30, "0");
            Pair(text, 0, "ENDSEC");

            Pair(text, 0, "SECTION");
            Pair(text, 2, "TABLES");
            Pair(text, 0, "TABLE");
            Pair(text, 2, "LTYPE");
            Pair(text, 70, "1");
            Pair(text, 0, "LTYPE");
            Pair(text, 2, "CONTINUOUS");
            Pair(text, 70, "0");
            Pair(text, 3, "Solid line");
            Pair(text, 72, "65");
            Pair(text, 73, "0");
            Pair(text, 40, "0");
            Pair(text, 0, "ENDTAB");
            Pair(text, 0, "TABLE");
            Pair(text, 2, "LAYER");
            Pair(text, 70, layers.Count.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < layers.Count; i++)
            {
                Pair(text, 0, "LAYER");
                Pair(text, 2, LayerName(i, layers[i].Color));
                Pair(text, 70, "0");
                Pair(text, 62, ColorIndex(layers[i].Color).ToString(CultureInfo.InvariantCulture));
                Pair(text, 6, "CONTINUOUS");
            }

            Pair(text, 0, "ENDTAB");
            Pair(text, 0, "ENDSEC");

            Pair(text, 0, "SECTION");
            Pair(text, 2, "ENTITIES");
            for (var i = 0; i < layers.Count; i++)
            {
                var name = LayerName(i, layers[i].Color);
                foreach (var path in layers[i].Paths)
                    AppendPolyline(text, name, Flatten(path, scaleX, scaleY, accuracyMillimetres), heightMillimetres);
            }

            Pair(text, 0, "ENDSEC");
            Pair(text, 0, "EOF");
            return text.ToString();
        }

        /// <summary>The name of the layer of a colour: the number it comes in and the colour, so that two layers of one colour (a trace can have them) do not share a name.</summary>
        internal static string LayerName(int index, TraceColor color) =>
            $"TRACE_{(index + 1).ToString(CultureInfo.InvariantCulture)}_{color.R:X2}{color.G:X2}{color.B:X2}";

        /// <summary>
        /// The AutoCAD colour number nearest to a colour (R12 has no true colours; the layer name carries the exact one). Black is 7, which AutoCAD draws black on a white sheet and white on a
        /// black one.
        /// </summary>
        internal static int ColorIndex(TraceColor color)
        {
            (int Index, int R, int G, int B)[] palette =
            [
                (1, 255, 0, 0), (2, 255, 255, 0), (3, 0, 255, 0), (4, 0, 255, 255), (5, 0, 0, 255), (6, 255, 0, 255),
                (7, 0, 0, 0), (7, 255, 255, 255),
                (250, 51, 51, 51), (251, 91, 91, 91), (252, 132, 132, 132), (253, 173, 173, 173), (254, 214, 214, 214),
            ];
            var best = 7;
            var bestDistance = int.MaxValue;
            foreach (var (index, r, g, b) in palette)
            {
                var distance = (color.R - r) * (color.R - r) + (color.G - g) * (color.G - g) + (color.B - b) * (color.B - b);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = index;
                }
            }

            return best;
        }

        /// <summary>The corners of an outline as straight pieces, in millimetres, the curves cut so that no piece is more than <paramref name="accuracy"/> from the curve. The start is not repeated at the end.</summary>
        internal static List<TracePoint> Flatten(TracePath path, double scaleX, double scaleY, double accuracy)
        {
            TracePoint Scale(TracePoint p) => new(p.X * scaleX, p.Y * scaleY);

            var points = new List<TracePoint> { Scale(path.Start) };
            var current = points[0];
            foreach (var segment in path.Segments)
            {
                var end = Scale(segment.End);
                if (segment.IsCurve)
                    FlattenCurve(current, Scale(segment.Control1), Scale(segment.Control2), end, accuracy, points);
                else
                    points.Add(end);
                current = end;
            }

            if (points.Count > 1 && points[^1].DistanceTo(points[0]) < 1e-9)
                points.RemoveAt(points.Count - 1);
            return points;
        }

        private static void FlattenCurve(TracePoint p0, TracePoint c1, TracePoint c2, TracePoint p3, double accuracy, List<TracePoint> output)
        {
            // Cut in two until the curve is as flat as the accuracy asks: the curve is never farther from its chord than three quarters of how far its control points are
            var pending = new Stack<(TracePoint P0, TracePoint C1, TracePoint C2, TracePoint P3, int Depth)>();
            pending.Push((p0, c1, c2, p3, 0));
            while (pending.Count > 0)
            {
                var (a, b, c, d, depth) = pending.Pop();
                var flatness = 0.75 * Math.Max(DistanceToLine(b, a, d), DistanceToLine(c, a, d));
                if (flatness <= accuracy || depth >= 18)
                {
                    output.Add(d);
                    continue;
                }

                var ab = Middle(a, b);
                var bc = Middle(b, c);
                var cd = Middle(c, d);
                var abc = Middle(ab, bc);
                var bcd = Middle(bc, cd);
                var middle = Middle(abc, bcd);
                pending.Push((middle, bcd, cd, d, depth + 1)); // the second half is looked at after the first, so it goes in first
                pending.Push((a, ab, abc, middle, depth + 1));
            }
        }

        private static TracePoint Middle(TracePoint a, TracePoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private static double DistanceToLine(TracePoint p, TracePoint a, TracePoint b)
        {
            var ab = b - a;
            var length = ab.Length;
            if (length < 1e-12)
                return p.DistanceTo(a);

            return Math.Abs(ab.X * (p.Y - a.Y) - ab.Y * (p.X - a.X)) / length;
        }

        private static void AppendPolyline(StringBuilder text, string layer, List<TracePoint> points, double heightMillimetres)
        {
            if (points.Count < 3)
                return;

            Pair(text, 0, "POLYLINE");
            Pair(text, 8, layer);
            Pair(text, 66, "1");
            Pair(text, 70, "1");
            foreach (var point in points)
            {
                Pair(text, 0, "VERTEX");
                Pair(text, 8, layer);
                Pair(text, 10, SvgWriter.Number(point.X));
                Pair(text, 20, SvgWriter.Number(heightMillimetres - point.Y));
                Pair(text, 30, "0");
            }

            Pair(text, 0, "SEQEND");
            Pair(text, 8, layer);
        }

        private static void Pair(StringBuilder text, int code, string value) =>
            text.Append(code.ToString(CultureInfo.InvariantCulture).PadLeft(3)).Append("\r\n").Append(value).Append("\r\n");
    }
}
