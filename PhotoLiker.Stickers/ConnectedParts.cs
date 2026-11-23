namespace PhotoLiker.Stickers
{
    /// <summary>The separate pieces of a black and white picture: every point that is on belongs to the piece of the points on that touch it.</summary>
    internal sealed class ConnectedParts
    {
        private ConnectedParts(int[] labels, int[] areas)
        {
            Labels = labels;
            Areas = areas;
        }

        /// <summary>For every point: 0 when it is off, else the number (from 1) of its piece.</summary>
        public int[] Labels { get; }

        /// <summary>How many points each piece has; <c>Areas[0]</c> is the number of points that are off, and <c>Areas[n]</c> is the area of piece n.</summary>
        public int[] Areas { get; }

        /// <summary>The number of pieces.</summary>
        public int Count => Areas.Length - 1;

        /// <param name="on">The points that are on.</param>
        /// <param name="eightNeighbours">Whether points that touch only at a corner are one piece (the pieces of a subject are; the holes in it, being 4-connected, are not).</param>
        public static ConnectedParts Find(bool[] on, int width, int height, bool eightNeighbours)
        {
            var labels = new int[on.Length];
            var areas = new List<int> { 0 };
            var stack = new Stack<int>();

            for (var start = 0; start < on.Length; start++)
            {
                if (!on[start])
                {
                    areas[0]++;
                    continue;
                }
                if (labels[start] != 0)
                    continue;

                var label = areas.Count;
                var area = 0;
                labels[start] = label;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var at = stack.Pop();
                    area++;
                    var x = at % width;
                    var y = at / width;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var ny = y + dy;
                        if (ny < 0 || ny >= height)
                            continue;
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if ((dx == 0 && dy == 0) || (!eightNeighbours && dx != 0 && dy != 0))
                                continue;
                            var nx = x + dx;
                            if (nx < 0 || nx >= width)
                                continue;
                            var next = ny * width + nx;
                            if (on[next] && labels[next] == 0)
                            {
                                labels[next] = label;
                                stack.Push(next);
                            }
                        }
                    }
                }
                areas.Add(area);
            }

            return new ConnectedParts(labels, [.. areas]);
        }

        /// <summary>The numbers of the pieces that touch the edge of the picture (a 'hole' that does is not a hole but the outside).</summary>
        public HashSet<int> TouchingEdge(int width, int height)
        {
            var result = new HashSet<int>();
            for (var x = 0; x < width; x++)
            {
                if (Labels[x] != 0)
                    result.Add(Labels[x]);
                if (Labels[(height - 1) * width + x] != 0)
                    result.Add(Labels[(height - 1) * width + x]);
            }
            for (var y = 0; y < height; y++)
            {
                if (Labels[y * width] != 0)
                    result.Add(Labels[y * width]);
                if (Labels[y * width + width - 1] != 0)
                    result.Add(Labels[y * width + width - 1]);
            }
            return result;
        }
    }
}
