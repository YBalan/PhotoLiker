using System.Drawing;

namespace PhotoLiker.Drawing
{
    /// <summary>Puts the steps on a picture: the strokes of a pen (anti-aliased, with round ends) and the letters of a note.</summary>
    public static class Painter
    {
        /// <summary>The narrowest line that is drawn (a line of nothing is not seen).</summary>
        public const float MinimumWidth = 1f;

        /// <summary>Draws the stroke on <paramref name="target"/>: every pixel the pen touches is given its share of the colour once, so a stroke that crosses itself is not darker there.</summary>
        public static void Draw(Bitmap32 target, StrokeStep stroke)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(stroke);
            if (stroke.Points.Count == 0 || stroke.Colour.A == 0)
                return;

            var radius = Math.Max(stroke.Width, MinimumWidth) / 2f;
            var reach = radius + 1f; // a pixel further than this from the line is not touched at all

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in stroke.Points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            var box = Rectangle.Intersect(
                Rectangle.FromLTRB((int)Math.Floor(minX - reach), (int)Math.Floor(minY - reach), (int)Math.Ceiling(maxX + reach), (int)Math.Ceiling(maxY + reach)),
                target.Bounds);
            if (box.Width < 1 || box.Height < 1)
                return;

            var coverage = new byte[box.Width * box.Height];
            var points = stroke.Points;
            for (var i = 0; i < Math.Max(1, points.Count - 1); i++)
                Cover(coverage, box, points[i], points[Math.Min(i + 1, points.Count - 1)], radius, reach);

            for (var y = 0; y < box.Height; y++)
            {
                for (var x = 0; x < box.Width; x++)
                {
                    var c = coverage[y * box.Width + x];
                    if (c != 0)
                        target.Blend(box.X + x, box.Y + y, stroke.Colour, c / 255f);
                }
            }
        }

        /// <summary>Puts the letters of the note on <paramref name="target"/>; what is outside the picture is left out.</summary>
        public static void Draw(Bitmap32 target, TextStep note)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(note);

            var mask = note.Mask;
            for (var y = 0; y < mask.Height; y++)
            {
                var ty = note.Position.Y + y;
                if (ty < 0 || ty >= target.Height)
                    continue;

                for (var x = 0; x < mask.Width; x++)
                {
                    var tx = note.Position.X + x;
                    if (tx < 0 || tx >= target.Width)
                        continue;

                    var c = mask.Coverage[y * mask.Width + x];
                    if (c != 0)
                        target.Blend(tx, ty, note.Colour, c / 255f);
                }
            }
        }

        /// <summary>Applies a step to the picture: a stroke or a note is drawn on it, and a crop or a turn gives a picture of its own. Returns the picture that results (the one given, drawn on, unless the step was a crop or a turn).</summary>
        public static Bitmap32 Apply(Bitmap32 picture, DrawStep step)
        {
            switch (step)
            {
                case StrokeStep stroke:
                    Draw(picture, stroke);
                    return picture;
                case TextStep note:
                    Draw(picture, note);
                    return picture;
                case CropStep crop:
                    return picture.Crop(crop.Area);
                case RotateStep turn:
                    return picture.Rotate(turn.QuarterTurns);
                default:
                    throw new ArgumentException($"{step.GetType().Name} is no step that can be drawn.", nameof(step));
            }
        }

        /// <summary>The pen's coverage of the pixels near the segment from <paramref name="a"/> to <paramref name="b"/>, kept where it is more than what is there.</summary>
        private static void Cover(byte[] coverage, Rectangle box, PointF a, PointF b, float radius, float reach)
        {
            var top = Math.Max(box.Top, (int)Math.Floor(Math.Min(a.Y, b.Y) - reach));
            var bottom = Math.Min(box.Bottom - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + reach));
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;

            for (var y = top; y <= bottom; y++)
            {
                // The part of the segment that is within reach of this row (a long slanting line touches a narrow stretch of each row, not the width of its box)
                if (!ExtentInBand(a, b, y + 0.5f - reach, y + 0.5f + reach, out var from, out var to))
                    continue;

                var left = Math.Max(box.Left, (int)Math.Floor(from - reach));
                var right = Math.Min(box.Right - 1, (int)Math.Ceiling(to + reach));
                var py = y + 0.5f;
                for (var x = left; x <= right; x++)
                {
                    var px = x + 0.5f;
                    var t = lengthSquared <= 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / lengthSquared, 0f, 1f);
                    var ex = px - (a.X + t * dx);
                    var ey = py - (a.Y + t * dy);
                    var distance = MathF.Sqrt(ex * ex + ey * ey);

                    // One pixel wide edge: the pixel whose centre is exactly the radius away is half covered
                    var share = Math.Clamp(radius + 0.5f - distance, 0f, 1f);
                    if (share <= 0)
                        continue;

                    var value = (byte)(share * 255f + 0.5f);
                    var i = (y - box.Y) * box.Width + (x - box.X);
                    if (value > coverage[i])
                        coverage[i] = value;
                }
            }
        }

        /// <summary>The leftmost and rightmost x of the part of the segment whose y is between <paramref name="low"/> and <paramref name="high"/>; false when none of it is.</summary>
        private static bool ExtentInBand(PointF a, PointF b, float low, float high, out float from, out float to)
        {
            from = to = 0;
            var dy = b.Y - a.Y;
            float t0, t1;
            if (Math.Abs(dy) < 1e-6f)
            {
                if (a.Y < low || a.Y > high)
                    return false;
                t0 = 0;
                t1 = 1;
            }
            else
            {
                t0 = (low - a.Y) / dy;
                t1 = (high - a.Y) / dy;
                if (t0 > t1)
                    (t0, t1) = (t1, t0);
                t0 = Math.Max(t0, 0f);
                t1 = Math.Min(t1, 1f);
                if (t0 > t1)
                    return false;
            }

            var x0 = a.X + t0 * (b.X - a.X);
            var x1 = a.X + t1 * (b.X - a.X);
            from = Math.Min(x0, x1);
            to = Math.Max(x0, x1);
            return true;
        }
    }
}
