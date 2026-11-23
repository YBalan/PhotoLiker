namespace PhotoLiker.Stickers
{
    /// <summary>
    /// The exact Euclidean distance from every point of a grid to the nearest point of a set (Felzenszwalb and Huttenlocher's linear-time method). It is what makes an outline round: a border
    /// that is the points within 8 pixels of the subject is 8 pixels wide in every direction, not 8 along the axes and 11 along the diagonals as a square-ended grow would make it.
    /// </summary>
    internal static class DistanceTransform
    {
        /// <summary>A distance that stands for "there is no point of the set at all": far more than any picture is wide.</summary>
        public const float None = 1e6f;

        private const double Infinity = 1e20;

        /// <summary>For every point: how far it is from the nearest point where <paramref name="inSet"/> is true (0 for those). <see cref="None"/> everywhere when the set is empty.</summary>
        public static float[] To(bool[] inSet, int width, int height)
        {
            if (inSet.Length != width * height)
                throw new ArgumentException("The grid does not have the size that was said.", nameof(inSet));

            var squared = new double[width * height];
            for (var i = 0; i < squared.Length; i++)
                squared[i] = inSet[i] ? 0 : Infinity;

            // Down the columns, then along the rows: the squared distance is separable
            var longest = Math.Max(width, height);
            Parallel.For(0, width, () => new Scratch(longest), (x, _, scratch) =>
            {
                for (var y = 0; y < height; y++)
                    scratch.Input[y] = squared[y * width + x];
                Line(scratch, height);
                for (var y = 0; y < height; y++)
                    squared[y * width + x] = scratch.Output[y];
                return scratch;
            }, _ => { });

            Parallel.For(0, height, () => new Scratch(longest), (y, _, scratch) =>
            {
                Array.Copy(squared, y * width, scratch.Input, 0, width);
                Line(scratch, width);
                Array.Copy(scratch.Output, 0, squared, y * width, width);
                return scratch;
            }, _ => { });

            var result = new float[squared.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = squared[i] >= Infinity / 2 ? None : (float)Math.Sqrt(squared[i]);
            return result;
        }

        private sealed class Scratch(int length)
        {
            public readonly double[] Input = new double[length];
            public readonly double[] Output = new double[length];
            public readonly int[] Parabolas = new int[length];
            public readonly double[] Borders = new double[length + 1];
        }

        /// <summary>The lower envelope of the parabolas that rise from the values of one row or column.</summary>
        private static void Line(Scratch s, int n)
        {
            var f = s.Input;
            var v = s.Parabolas;
            var z = s.Borders;

            var k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (var q = 1; q < n; q++)
            {
                double at;
                while (true)
                {
                    var p = v[k];
                    at = (f[q] + (double)q * q - (f[p] + (double)p * p)) / (2.0 * q - 2.0 * p);
                    if (at > z[k])
                        break;
                    k--; // the new parabola is under the last one from the last one's own start on: that one is hidden
                }

                k++;
                v[k] = q;
                z[k] = at;
                z[k + 1] = double.PositiveInfinity;
            }

            k = 0;
            for (var q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                    k++;
                var d = q - v[k];
                s.Output[q] = d * (double)d + f[v[k]];
            }
        }
    }
}
