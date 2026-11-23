using System.Drawing;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>
    /// The place of one line of text in a picture: a rectangle that may be turned (a photographed sign is seldom straight), as its four corners in the pixels of the picture,
    /// in the order top left, top right, bottom right, bottom left of the line as it is read.
    /// </summary>
    public readonly record struct TextRegion(PointF TopLeft, PointF TopRight, PointF BottomRight, PointF BottomLeft)
    {
        public PointF[] Corners => [TopLeft, TopRight, BottomRight, BottomLeft];

        /// <summary>The length of the line along its text.</summary>
        public float Length => Distance(TopLeft, TopRight);

        /// <summary>The height of the line.</summary>
        public float Height => Distance(TopLeft, BottomLeft);

        public PointF Centre => new((TopLeft.X + TopRight.X + BottomRight.X + BottomLeft.X) / 4f, (TopLeft.Y + TopRight.Y + BottomRight.Y + BottomLeft.Y) / 4f);

        /// <summary>The smallest upright rectangle that holds the whole line.</summary>
        public Rectangle Bounds
        {
            get
            {
                var xs = new[] { TopLeft.X, TopRight.X, BottomRight.X, BottomLeft.X };
                var ys = new[] { TopLeft.Y, TopRight.Y, BottomRight.Y, BottomLeft.Y };
                return Rectangle.FromLTRB((int)Math.Floor(xs.Min()), (int)Math.Floor(ys.Min()), (int)Math.Ceiling(xs.Max()), (int)Math.Ceiling(ys.Max()));
            }
        }

        /// <summary>This box moved by <paramref name="by"/> and scaled by the factors (the corners of a box found in a part of a picture, or in a picture that was made smaller, are put where they are in the whole one).</summary>
        public TextRegion Transformed(float scaleX, float scaleY, float moveX, float moveY)
        {
            PointF Map(PointF p) => new(p.X * scaleX + moveX, p.Y * scaleY + moveY);
            return new TextRegion(Map(TopLeft), Map(TopRight), Map(BottomRight), Map(BottomLeft));
        }

        /// <summary>The rectangle with its centre at <paramref name="centre"/>, <paramref name="length"/> long along the direction <paramref name="angle"/> (radians, 0 is to the right, positive turns clockwise on the screen) and <paramref name="height"/> across.</summary>
        public static TextRegion FromCentre(PointF centre, float length, float height, float angle)
        {
            var ux = MathF.Cos(angle) * length / 2f;
            var uy = MathF.Sin(angle) * length / 2f;
            var vx = -MathF.Sin(angle) * height / 2f;
            var vy = MathF.Cos(angle) * height / 2f;
            return new TextRegion(
                new PointF(centre.X - ux - vx, centre.Y - uy - vy),
                new PointF(centre.X + ux - vx, centre.Y + uy - vy),
                new PointF(centre.X + ux + vx, centre.Y + uy + vy),
                new PointF(centre.X - ux + vx, centre.Y - uy + vy));
        }

        private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    /// <summary>One line of text that was read: what it says, how sure the reader is (0 to 1), and where it is.</summary>
    public sealed record TextLine(string Text, float Confidence, TextRegion Box);

    /// <summary>What a scan found: the lines in the order they are read (from the top, each row from the left).</summary>
    public sealed record ScanResult(IReadOnlyList<TextLine> Lines)
    {
        /// <summary>All the text, a row of the picture to a line of text.</summary>
        public string Text => TextOf(Lines);

        public bool IsEmpty => Lines.Count == 0;

        /// <summary>The text of lines that are in reading order: lines that are on one row of the picture (their middles are not further apart than half a line) are joined with a space.</summary>
        internal static string TextOf(IReadOnlyList<TextLine> lines)
        {
            var text = new System.Text.StringBuilder();
            TextLine? previous = null;
            foreach (var line in lines)
            {
                if (previous is not null)
                    text.Append(SameRow(previous, line) ? ' ' : '\n');
                text.Append(line.Text);
                previous = line;
            }
            return text.ToString();
        }

        internal static bool SameRow(TextLine a, TextLine b) =>
            Math.Abs(a.Box.Centre.Y - b.Box.Centre.Y) <= Math.Min(a.Box.Height, b.Box.Height) / 2f && b.Box.Centre.X > a.Box.Centre.X;
    }
}
