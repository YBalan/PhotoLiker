using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text;

namespace PhotoLiker.VectorFiles
{
    /// <summary>
    /// Reads an STL file, binary or text. Which of the two it is is told by the size, not by the first word: a binary file may start its 80-byte header with "solid" (many programs
    /// do), and it is exactly 84 + 50 x count bytes long, which no text file is. A file that is neither is not an STL file and says so with <see cref="InvalidDataException"/>.
    /// A text file may hold several solids; they become one mesh.
    /// </summary>
    public static class StlReader
    {
        private const int HeaderBytes = 80;
        private const int BinaryStart = 84;
        private const int BinaryTriangleBytes = 50;

        /// <summary>The extension of the files this reads (with its dot).</summary>
        public const string Extension = ".stl";

        /// <summary>Reads a file. It is read in one go and let go of at once, also while another program has it open for writing (a slicer that is saving it).</summary>
        public static StlMesh Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            if (stream.Length > int.MaxValue)
                throw new InvalidDataException("The file is too big to be read (more than 2 GB).");

            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return Read(bytes);
        }

        public static StlMesh Read(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
                throw new InvalidDataException("The file is empty.");

            if (IsBinary(data))
                return ReadBinary(data);
            if (LooksLikeText(data))
                return ReadAscii(data);

            throw new InvalidDataException("This is not an STL file: it is neither a binary one nor a text one.");
        }

        private static bool IsBinary(ReadOnlySpan<byte> data)
        {
            if (data.Length < BinaryStart)
                return false;

            var count = BinaryPrimitives.ReadUInt32LittleEndian(data[HeaderBytes..]);
            var expected = BinaryStart + (long)BinaryTriangleBytes * count;
            if (expected == data.Length)
                return true; // the size is exact: binary, whatever the header says

            // A text file that happens to have bytes there is not binary; otherwise a binary file with something after its last triangle (some programs pad) is still one
            return !(StartsWithSolid(data) && LooksLikeText(data)) && count > 0 && expected <= data.Length;
        }

        private static bool StartsWithSolid(ReadOnlySpan<byte> data)
        {
            var at = 0;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                at = 3; // a byte order mark
            while (at < data.Length && data[at] <= 0x20)
                at++;
            return data.Length - at >= 5 && EqualsIgnoreCase(data.Slice(at, 5), "solid"u8);
        }

        private static bool LooksLikeText(ReadOnlySpan<byte> data)
        {
            if (!StartsWithSolid(data))
                return false;

            // "solid" opens a binary header too: a text file has a facet (or an empty solid that ends) in its first lines, and nothing but text before it
            var head = data[..Math.Min(data.Length, 4096)];
            foreach (var b in head)
                if (b == 0 || (b < 0x09) || (b > 0x0D && b < 0x20))
                    return false;

            return ContainsIgnoreCase(head, "facet"u8) || ContainsIgnoreCase(head, "endsolid"u8);
        }

        private static StlMesh ReadBinary(ReadOnlySpan<byte> data)
        {
            // IsBinary has seen that the file holds as many triangles as it says (a file that says more is not guessed at: nothing tells it from random bytes)
            var triangles = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[HeaderBytes..]));
            var vertices = new float[checked(triangles * 9)];

            var kept = 0;
            var offset = BinaryStart;
            for (var t = 0; t < triangles; t++, offset += BinaryTriangleBytes)
            {
                var corners = data.Slice(offset + 12, 36); // the 12 bytes before are the normal, the 2 after are the attribute: neither is used
                var at = kept * 9;
                var finite = true;
                for (var k = 0; k < 9; k++)
                {
                    var value = BinaryPrimitives.ReadSingleLittleEndian(corners[(k * 4)..]);
                    if (!float.IsFinite(value))
                    {
                        finite = false;
                        break;
                    }
                    vertices[at + k] = value;
                }
                if (finite)
                    kept++;
            }

            return new StlMesh(vertices, kept, HeaderName(data[..HeaderBytes]), StlFormat.Binary, triangles - kept);
        }

        /// <summary>The header of a binary file is 80 bytes of anything; when it is text, it is what the program called the model.</summary>
        private static string HeaderName(ReadOnlySpan<byte> header)
        {
            var end = header.IndexOf((byte)0);
            var text = header[..(end < 0 ? header.Length : end)];
            foreach (var b in text)
                if (b < 0x20 || b > 0x7E)
                    return string.Empty; // not text (a colour, a unit, binary leftovers)

            var name = Encoding.ASCII.GetString(text).Trim();
            if (name.StartsWith("solid ", StringComparison.OrdinalIgnoreCase))
                name = name[6..].Trim();
            return name;
        }

        private static StlMesh ReadAscii(ReadOnlySpan<byte> data)
        {
            var numbers = new FloatBuffer(Math.Max(64, data.Length / 12)); // a facet takes about 200 bytes and has nine numbers: 22 bytes each; this is a guess that the buffer grows past
            var name = string.Empty;
            var nameRead = false;
            var position = 0;
            var cutOff = false;
            Span<float> corner = stackalloc float[3];

            while (!cutOff && NextToken(data, ref position, out var token))
            {
                if (EqualsIgnoreCase(token, "vertex"u8))
                {
                    for (var k = 0; k < 3; k++)
                    {
                        if (!NextToken(data, ref position, out var number))
                        {
                            cutOff = true; // the file stops in the middle of a vertex (a download that did not finish): what came before it is kept
                            break;
                        }

                        if (!Utf8Parser.TryParse(number, out float value, out var used) || used != number.Length || !float.IsFinite(value))
                            throw new InvalidDataException($"A vertex has something that is not a number: \"{Encoding.ASCII.GetString(number[..Math.Min(number.Length, 24)])}\".");
                        corner[k] = value;
                    }

                    if (!cutOff)
                    {
                        numbers.Add(corner[0]);
                        numbers.Add(corner[1]);
                        numbers.Add(corner[2]);
                    }
                }
                else if (!nameRead && EqualsIgnoreCase(token, "solid"u8))
                {
                    nameRead = true;
                    name = RestOfLine(data, position);
                }
            }

            var whole = numbers.Count / 9;
            if (whole == 0)
                throw new InvalidDataException("The file has no triangles.");

            var skipped = cutOff || numbers.Count % 9 != 0 ? 1 : 0; // the last triangle was cut off
            return new StlMesh(numbers.Array, whole, name, StlFormat.Ascii, skipped);
        }

        private static string RestOfLine(ReadOnlySpan<byte> data, int from)
        {
            var end = from;
            while (end < data.Length && data[end] != (byte)'\n' && data[end] != (byte)'\r')
                end++;
            return Encoding.UTF8.GetString(data[from..end]).Trim();
        }

        /// <summary>The next run of bytes that are not white space (and not control characters), after <paramref name="position"/>.</summary>
        private static bool NextToken(ReadOnlySpan<byte> data, ref int position, out ReadOnlySpan<byte> token)
        {
            var i = position;
            while (i < data.Length && data[i] <= 0x20)
                i++;
            if (i >= data.Length)
            {
                position = i;
                token = default;
                return false;
            }

            var start = i;
            while (i < data.Length && data[i] > 0x20)
                i++;
            token = data[start..i];
            position = i;
            return true;
        }

        private static bool EqualsIgnoreCase(ReadOnlySpan<byte> token, ReadOnlySpan<byte> lowerCaseWord)
        {
            if (token.Length != lowerCaseWord.Length)
                return false;
            for (var i = 0; i < token.Length; i++)
                if ((token[i] | 0x20) != lowerCaseWord[i])
                    return false;
            return true;
        }

        private static bool ContainsIgnoreCase(ReadOnlySpan<byte> data, ReadOnlySpan<byte> lowerCaseWord)
        {
            for (var i = 0; i + lowerCaseWord.Length <= data.Length; i++)
                if (EqualsIgnoreCase(data.Slice(i, lowerCaseWord.Length), lowerCaseWord))
                    return true;
            return false;
        }

        /// <summary>The numbers of a text file, as they are found: an array that grows.</summary>
        private sealed class FloatBuffer(int capacity)
        {
            private float[] _items = new float[capacity];

            public float[] Array => _items;
            public int Count { get; private set; }

            public void Add(float value)
            {
                if (Count == _items.Length)
                {
                    var bigger = new float[Math.Max(64, _items.Length * 2)];
                    System.Array.Copy(_items, bigger, Count);
                    _items = bigger;
                }
                _items[Count++] = value;
            }
        }
    }
}
