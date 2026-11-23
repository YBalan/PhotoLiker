using System.Drawing;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>What the finder of lines is strict about.</summary>
    public sealed record FinderSettings
    {
        /// <summary>A point of the map belongs to a line of text when the model is at least this sure of it (0 to 1).</summary>
        public float PointThreshold { get; init; } = 0.3f;

        /// <summary>A shape is a line of text when the model is on average at least this sure of its points.</summary>
        public float ShapeThreshold { get; init; } = 0.6f;

        /// <summary>
        /// How much a shape is made larger: the model draws each line smaller than it is (so that two lines that are close do not run into one), and this gives the
        /// size back. The distance each side moves is the area of the shape times this, over its perimeter.
        /// </summary>
        public float Unclip { get; init; } = 1.5f;

        /// <summary>A line whose shorter side (in points of the map) is less than this is dust.</summary>
        public float MinimumSide { get; init; } = 3f;
    }

    /// <summary>
    /// Turns the map the detection model gives (for each point of the picture, how likely it is part of a line of text) into the lines: the points that are sure enough are joined into shapes,
    /// each shape that is sure enough on the whole becomes the smallest rectangle round it, made larger to make up for what the model leaves off.
    /// </summary>
    internal static class LineFinder
    {
        /// <summary>The lines in a map of <paramref name="width"/> by <paramref name="height"/> points, each as a box in the points of the map, with how sure the model is of it.</summary>
        public static List<(TextRegion Box, float Score)> Find(float[] map, int width, int height, FinderSettings settings)
        {
            if (map.Length != width * height)
                throw new ArgumentException($"A map of {width} by {height} has {width * height} points, not {map.Length}.", nameof(map));

            var found = new List<(TextRegion Box, float Score)>();
            var seen = new bool[map.Length];
            var queue = new int[map.Length];

            for (var start = 0; start < map.Length; start++)
            {
                if (seen[start] || map[start] < settings.PointThreshold)
                    continue;

                // Everything that touches this point (up, down, left, right) and is sure enough, with how sure it is on the whole; only the points on the edge of the shape matter for its rectangle
                var edge = new List<PointF>();
                var head = 0;
                var tail = 0;
                queue[tail++] = start;
                seen[start] = true;
                double sum = 0;
                var count = 0;
                while (head < tail)
                {
                    var i = queue[head++];
                    var x = i % width;
                    var y = i / width;
                    sum += map[i];
                    count++;

                    var onEdge = false;
                    onEdge |= Visit(x - 1, y, x > 0);
                    onEdge |= Visit(x + 1, y, x < width - 1);
                    onEdge |= Visit(x, y - 1, y > 0);
                    onEdge |= Visit(x, y + 1, y < height - 1);
                    if (onEdge)
                    {
                        // The centre of the point, and the four corners of it: the rectangle holds the whole of the shape, not only the middles of its points
                        edge.Add(new PointF(x, y));
                        edge.Add(new PointF(x + 1, y));
                        edge.Add(new PointF(x, y + 1));
                        edge.Add(new PointF(x + 1, y + 1));
                    }
                }

                if (count < 4 || sum / count < settings.ShapeThreshold)
                    continue;

                var (centre, length, side, angle) = Geometry.SmallestRectangle(edge);
                if (Math.Min(length, side) < settings.MinimumSide)
                    continue;

                var distance = length * side * settings.Unclip / (2f * (length + side));
                found.Add((TextRegion.FromCentre(centre, length + 2 * distance, side + 2 * distance, angle), (float)(sum / count)));

                bool Visit(int nx, int ny, bool inside)
                {
                    if (!inside)
                        return true; // the border of the map is the edge of the shape
                    var n = ny * width + nx;
                    if (map[n] < settings.PointThreshold)
                        return true;
                    if (!seen[n])
                    {
                        seen[n] = true;
                        queue[tail++] = n;
                    }
                    return false;
                }
            }

            return found;
        }
    }
}
