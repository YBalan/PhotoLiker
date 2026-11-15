using System.Globalization;

namespace PhotoLiker.Effects.Looks
{
    /// <summary>
    /// A colour lookup table read from a <c>.cube</c> file (the format of Adobe and of the colour grading programs): a cube of <c>N</c> by <c>N</c> by <c>N</c> colours that says what colour each colour becomes
    /// (<c>LUT_3D_SIZE</c>, the red changes fastest in the list), or a curve of <c>N</c> colours for each of red, green and blue (<c>LUT_1D_SIZE</c>). Colours between the points are worked out
    /// by interpolation (trilinear for a cube). <c>DOMAIN_MIN</c> and <c>DOMAIN_MAX</c> are honoured; <c>TITLE</c> and the comments are skipped.
    /// </summary>
    public sealed class CubeLut
    {
        /// <summary>The largest cube read: 256 points a side is 16.7 million colours, 200 MB as floats; 65 is what programs usually write.</summary>
        public const int MaxSize = 129;

        private readonly float[] _values;   // r, g, b of every point; red fastest for a cube
        private readonly float[] _min;
        private readonly float[] _max;

        private CubeLut(int size, bool isCube, float[] values, float[] min, float[] max, string title)
        {
            Size = size;
            IsCube = isCube;
            _values = values;
            _min = min;
            _max = max;
            Title = title;
        }

        /// <summary>The number of points on a side of the cube (or in each curve).</summary>
        public int Size { get; }

        /// <summary>A 3D table; else three curves.</summary>
        public bool IsCube { get; }

        public string Title { get; }

        /// <summary>Reads the file.</summary>
        /// <exception cref="FormatException">It is not a .cube file this program can use; the message says what is wrong.</exception>
        /// <exception cref="IOException">It can not be read.</exception>
        public static CubeLut Load(string path)
        {
            using var reader = new StreamReader(path);
            return Parse(reader);
        }

        public static CubeLut Parse(TextReader reader)
        {
            var size = 0;
            var isCube = false;
            var title = string.Empty;
            float[] min = [0, 0, 0], max = [1, 1, 1];
            List<float>? values = null;
            var line = 0;
            string? text;
            while ((text = reader.ReadLine()) is not null)
            {
                line++;
                text = text.Trim();
                if (text.Length == 0 || text[0] == '#')
                    continue;

                var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0].ToUpperInvariant())
                {
                    case "TITLE":
                        title = text[5..].Trim().Trim('"');
                        continue;
                    case "LUT_3D_SIZE":
                    case "LUT_1D_SIZE":
                        if (size != 0)
                            throw new FormatException($"Line {line}: the size is given twice.");
                        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 2 || size > MaxSize)
                            throw new FormatException($"Line {line}: the size must be a number from 2 to {MaxSize}.");
                        isCube = parts[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase);
                        values = new List<float>(isCube ? size * size * size * 3 : size * 3);
                        continue;
                    case "DOMAIN_MIN":
                        min = Three(parts, line);
                        continue;
                    case "DOMAIN_MAX":
                        max = Three(parts, line);
                        continue;
                }

                if (!char.IsAsciiDigit(parts[0][0]) && parts[0][0] is not ('-' or '+' or '.'))
                    continue; // a keyword this program has no use for (LUT_3D_INPUT_RANGE, ...)

                if (values is null)
                    throw new FormatException($"Line {line}: colours come before the size of the table.");
                if (parts.Length < 3)
                    throw new FormatException($"Line {line}: a colour needs three numbers.");

                foreach (var number in parts.Take(3))
                {
                    if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                        throw new FormatException($"Line {line}: \"{number}\" is not a number.");
                    values.Add(value);
                }
            }

            if (size == 0 || values is null)
                throw new FormatException("There is no LUT_3D_SIZE or LUT_1D_SIZE: this is not a .cube file.");

            var expected = (isCube ? size * size * size : size) * 3;
            if (values.Count != expected)
                throw new FormatException($"The table should have {expected / 3} colours, and has {values.Count / 3}.");
            for (var channel = 0; channel < 3; channel++)
            {
                if (!(max[channel] > min[channel]))
                    throw new FormatException("DOMAIN_MAX must be larger than DOMAIN_MIN.");
            }

            return new CubeLut(size, isCube, [.. values], min, max, title);
        }

        private static float[] Three(string[] parts, int line)
        {
            if (parts.Length != 4)
                throw new FormatException($"Line {line}: three numbers are needed.");

            var numbers = new float[3];
            for (var i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !float.IsFinite(numbers[i]))
                    throw new FormatException($"Line {line}: \"{parts[i + 1]}\" is not a number.");
            }
            return numbers;
        }

        /// <summary>The colour that red, green and blue (0 to 1 each) become; the result is not brought into 0 to 1.</summary>
        public (float R, float G, float B) Map(float r, float g, float b)
        {
            var rr = Unit(r, 0);
            var gg = Unit(g, 1);
            var bb = Unit(b, 2);
            return IsCube ? Cube(rr, gg, bb) : Curves(rr, gg, bb);
        }

        /// <summary>A colour as a share (0 to 1) of the domain of its channel, kept inside it.</summary>
        private float Unit(float value, int channel) => Math.Clamp((value - _min[channel]) / (_max[channel] - _min[channel]), 0f, 1f);

        private (float, float, float) Curves(float r, float g, float b) => (Curve(r, 0), Curve(g, 1), Curve(b, 2));

        private float Curve(float share, int channel)
        {
            var at = share * (Size - 1);
            var low = Math.Min((int)at, Size - 2);
            var fraction = at - low;
            return _values[low * 3 + channel] + (_values[(low + 1) * 3 + channel] - _values[low * 3 + channel]) * fraction;
        }

        private (float, float, float) Cube(float r, float g, float b)
        {
            var scale = Size - 1;
            var rr = r * scale;
            var gg = g * scale;
            var bb = b * scale;
            var r0 = Math.Min((int)rr, Size - 2);
            var g0 = Math.Min((int)gg, Size - 2);
            var b0 = Math.Min((int)bb, Size - 2);
            var fr = rr - r0;
            var fg = gg - g0;
            var fb = bb - b0;

            float outR = 0, outG = 0, outB = 0;
            for (var corner = 0; corner < 8; corner++)
            {
                var dr = corner & 1;
                var dg = (corner >> 1) & 1;
                var db = (corner >> 2) & 1;
                var weight = (dr == 1 ? fr : 1 - fr) * (dg == 1 ? fg : 1 - fg) * (db == 1 ? fb : 1 - fb);
                if (weight == 0)
                    continue;

                var at = (((b0 + db) * Size + g0 + dg) * Size + r0 + dr) * 3;
                outR += _values[at] * weight;
                outG += _values[at + 1] * weight;
                outB += _values[at + 2] * weight;
            }
            return (outR, outG, outB);
        }
    }
}
