namespace PhotoLiker.TraceBitmap
{
    /// <summary>A point of a traced picture, in pixels of the traced picture: x to the right, y down, the corner of the top left pixel at (0, 0).</summary>
    public readonly record struct TracePoint(double X, double Y)
    {
        public static TracePoint operator +(TracePoint a, TracePoint b) => new(a.X + b.X, a.Y + b.Y);

        public static TracePoint operator -(TracePoint a, TracePoint b) => new(a.X - b.X, a.Y - b.Y);

        public static TracePoint operator *(TracePoint a, double k) => new(a.X * k, a.Y * k);

        public double Length => Math.Sqrt(X * X + Y * Y);

        public double DistanceTo(TracePoint other) => (this - other).Length;
    }

    /// <summary>A colour of a layer of the trace.</summary>
    public readonly record struct TraceColor(byte R, byte G, byte B)
    {
        public static readonly TraceColor Black = new(0, 0, 0);

        /// <summary>The colour the way SVG writes it: <c>#rrggbb</c>.</summary>
        public string Hex => $"#{R:x2}{G:x2}{B:x2}";

        /// <summary>How light the colour is, from 0 (black) to 1 (white).</summary>
        public double Brightness => (0.299 * R + 0.587 * G + 0.114 * B) / 255.0;
    }

    /// <summary>
    /// One step of a path: a straight line to <see cref="End"/>, or a cubic Bezier curve to it with the two control points. A line has no control points (they are left equal to the end).
    /// </summary>
    public readonly record struct TraceSegment(bool IsCurve, TracePoint Control1, TracePoint Control2, TracePoint End)
    {
        public static TraceSegment Line(TracePoint end) => new(false, end, end, end);

        public static TraceSegment Curve(TracePoint control1, TracePoint control2, TracePoint end) => new(true, control1, control2, end);
    }

    /// <summary>A closed outline: where it starts, and the lines and curves that go round it back to the start (the last segment ends at <see cref="Start"/>).</summary>
    public sealed class TracePath(TracePoint start, IReadOnlyList<TraceSegment> segments)
    {
        public TracePoint Start { get; } = start;

        public IReadOnlyList<TraceSegment> Segments { get; } = segments;

        /// <summary>The number of nodes (points the outline passes through).</summary>
        public int NodeCount => Segments.Count;
    }

    /// <summary>
    /// The outlines of one colour. Holes are outlines too: the shape is filled where an odd number of outlines surround a point (the even-odd rule), which is how the file writers say it.
    /// </summary>
    public sealed class TraceLayer(TraceColor color, IReadOnlyList<TracePath> paths)
    {
        public TraceColor Color { get; } = color;

        public IReadOnlyList<TracePath> Paths { get; } = paths;
    }

    /// <summary>What a trace made: the layers from the bottom one up, and the size of the picture that was traced (the coordinates of the paths are in its pixels).</summary>
    public sealed class TraceResult(int width, int height, IReadOnlyList<TraceLayer> layers)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public IReadOnlyList<TraceLayer> Layers { get; } = layers;

        public int PathCount => Layers.Sum(layer => layer.Paths.Count);

        public int NodeCount => Layers.Sum(layer => layer.Paths.Sum(path => path.NodeCount));
    }

    /// <summary>A picture as pixels: interleaved B, G, R bytes, no padding (what <c>PhotoLiker.Imaging</c> decodes a photo to).</summary>
    public sealed class PixelImage
    {
        public PixelImage(int width, int height, byte[] bgr)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(bgr);
            if (bgr.Length != (long)width * height * 3)
                throw new ArgumentException($"A picture of {width} x {height} pixels has {(long)width * height * 3} bytes, not {bgr.Length}.", nameof(bgr));

            Width = width;
            Height = height;
            Bgr = bgr;
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Bgr { get; }
    }
}
