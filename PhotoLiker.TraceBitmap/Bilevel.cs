namespace PhotoLiker.TraceBitmap
{
    /// <summary>A picture of black and white pixels; what the tracer follows the outlines of. A pixel outside the picture is white.</summary>
    internal sealed class Bilevel(int width, int height)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        /// <summary>1 for a black pixel, 0 for a white one; row after row.</summary>
        public byte[] Pixels { get; } = new byte[width * height];

        public bool this[int x, int y]
        {
            get => (uint)x < (uint)Width && (uint)y < (uint)Height && Pixels[y * Width + x] != 0;
            set => Pixels[y * Width + x] = value ? (byte)1 : (byte)0;
        }

        public int BlackCount
        {
            get
            {
                var count = 0;
                foreach (var pixel in Pixels)
                    count += pixel;
                return count;
            }
        }

        /// <summary>The same picture with black and white swapped.</summary>
        public Bilevel Inverted()
        {
            var copy = new Bilevel(Width, Height);
            for (var i = 0; i < Pixels.Length; i++)
                copy.Pixels[i] = (byte)(Pixels[i] ^ 1);
            return copy;
        }

        /// <summary>
        /// Takes away the specks: a black shape of <paramref name="size"/> pixels or fewer is made white, and a white hole of that size inside black is made black. Black pixels that touch at a corner
        /// are one shape (as the tracer follows them), white ones are one hole only when they touch side by side. White that reaches the edge of the picture is not a hole.
        /// </summary>
        public void SuppressSpeckles(int size)
        {
            if (size <= 0)
                return;

            ClearSmallRegions(black: true, size);
            ClearSmallRegions(black: false, size);
        }

        private void ClearSmallRegions(bool black, int size)
        {
            var wanted = black ? (byte)1 : (byte)0;
            var seen = new bool[Pixels.Length];
            var queue = new int[Math.Min(Pixels.Length, size + 1)];
            var stack = new Stack<int>();

            for (var start = 0; start < Pixels.Length; start++)
            {
                if (seen[start] || Pixels[start] != wanted)
                    continue;

                // Flood the region from here, remembering the first `size + 1` pixels only: a region that is bigger than that is not a speck, whatever its exact size
                var count = 0;
                var touchesEdge = false;
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    if (count < queue.Length)
                        queue[count] = at;
                    count++;

                    var x = at % Width;
                    var y = at / Width;
                    if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1)
                        touchesEdge = true;

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if ((dx == 0 && dy == 0) || (!black && dx != 0 && dy != 0))
                                continue; // black grows through corners, white only side by side

                            var nx = x + dx;
                            var ny = y + dy;
                            if ((uint)nx >= (uint)Width || (uint)ny >= (uint)Height)
                                continue;

                            var next = ny * Width + nx;
                            if (seen[next] || Pixels[next] != wanted)
                                continue;

                            seen[next] = true;
                            stack.Push(next);
                        }
                    }
                }

                if (count > size || (!black && touchesEdge))
                    continue;

                for (var i = 0; i < count; i++)
                    Pixels[queue[i]] = (byte)(wanted ^ 1);
            }
        }
    }
}
