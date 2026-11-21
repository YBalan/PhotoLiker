namespace PhotoLiker.TraceBitmap
{
    /// <summary>
    /// One outline of the black pixels, along the edges of the pixels: the corners of its steps (a point where it turns), in the order they are met. Black is on the right-hand side when walking
    /// (on the screen, y down), so an outer outline goes round clockwise and the outline of a hole goes round the other way.
    /// </summary>
    internal sealed class PixelLoop(int[] x, int[] y, long area2, int length)
    {
        public int[] X { get; } = x;

        public int[] Y { get; } = y;

        /// <summary>Twice the area it encloses, signed: positive for an outer outline, negative for a hole.</summary>
        public long Area2 { get; } = area2;

        /// <summary>The number of pixel edges it is made of.</summary>
        public int Length { get; } = length;

        public int Count => X.Length;

        public bool IsHole => Area2 < 0;
    }

    /// <summary>
    /// Follows the outlines of the black pixels of a <see cref="Bilevel"/> picture. Every edge between a black and a white pixel is an arrow with the black on its right; arrows join into closed
    /// outlines. Where two black pixels touch only at a corner (a chessboard), the outline goes on to the other pixel, so black that touches at a corner is one shape.
    /// </summary>
    internal static class ContourTracer
    {
        private static readonly int[] Dx = [1, 0, -1, 0];
        private static readonly int[] Dy = [0, 1, 0, -1];

        public static List<PixelLoop> Trace(Bilevel bitmap, CancellationToken cancel = default)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var stride = width + 1;
            var arrows = new byte[stride * (height + 1)]; // at each corner of a pixel, which of the four directions (right, down, left, up) an arrow leaves it in

            for (var y = 0; y < height; y++)
            {
                if ((y & 63) == 0)
                    cancel.ThrowIfCancellationRequested();

                for (var x = 0; x < width; x++)
                {
                    if (!bitmap[x, y])
                        continue;

                    if (!bitmap[x, y - 1])
                        arrows[y * stride + x] |= 1; // along the top, to the right
                    if (!bitmap[x + 1, y])
                        arrows[y * stride + x + 1] |= 2; // down the right side
                    if (!bitmap[x, y + 1])
                        arrows[(y + 1) * stride + x + 1] |= 4; // along the bottom, to the left
                    if (!bitmap[x - 1, y])
                        arrows[(y + 1) * stride + x] |= 8; // up the left side
                }
            }

            var loops = new List<PixelLoop>();
            var steps = new List<byte>();
            for (var start = 0; start < arrows.Length; start++)
            {
                if ((start & 4095) == 0)
                    cancel.ThrowIfCancellationRequested();

                while (arrows[start] != 0)
                    loops.Add(Follow(arrows, stride, start, steps));
            }

            return loops;
        }

        private static PixelLoop Follow(byte[] arrows, int stride, int start, List<byte> steps)
        {
            steps.Clear();
            var startDirection = LowestDirection(arrows[start]);
            var startBit = (byte)(1 << startDirection);

            var at = start;
            var direction = startDirection;
            while (true)
            {
                arrows[at] &= (byte)~(1 << direction);
                steps.Add((byte)direction);
                at += Dx[direction] + Dy[direction] * stride;

                // Where to go on: the only arrow, or at a chessboard corner the one that turns left (towards the black that is on the other side of the corner). The arrow the outline
                // started with counts as still there until the outline is closed, so that coming back to the start the choice is the same as anywhere else.
                var available = arrows[at];
                if (at == start)
                    available |= startBit;

                direction = Choose(available, direction);
                if (at == start && direction == startDirection)
                    break;
            }

            return Corners(start % stride, start / stride, steps);
        }

        private static int LowestDirection(byte mask)
        {
            for (var direction = 0; direction < 4; direction++)
            {
                if ((mask & (1 << direction)) != 0)
                    return direction;
            }

            return 0;
        }

        private static int Choose(byte available, int coming)
        {
            if (available == 0)
                return coming; // cannot happen: every corner has as many arrows going in as out

            var left = (coming + 3) & 3;
            var right = (coming + 1) & 3;
            if ((available & (1 << left)) != 0)
                return left;
            if ((available & (1 << right)) != 0)
                return right;
            return (available & (1 << coming)) != 0 ? coming : LowestDirection(available);
        }

        /// <summary>The corners of the steps of an outline that starts at (<paramref name="startX"/>, <paramref name="startY"/>), and the area it encloses.</summary>
        private static PixelLoop Corners(int startX, int startY, List<byte> steps)
        {
            var n = steps.Count;
            var xs = new List<int>();
            var ys = new List<int>();
            int x = startX, y = startY;
            for (var i = 0; i < n; i++)
            {
                if (steps[i] != steps[(i + n - 1) % n])
                {
                    xs.Add(x);
                    ys.Add(y);
                }

                x += Dx[steps[i]];
                y += Dy[steps[i]];
            }

            var cx = xs.ToArray();
            var cy = ys.ToArray();
            long area2 = 0;
            for (var i = 0; i < cx.Length; i++)
            {
                var next = (i + 1) % cx.Length;
                area2 += (long)cx[i] * cy[next] - (long)cx[next] * cy[i];
            }

            return new PixelLoop(cx, cy, area2, n);
        }
    }
}
