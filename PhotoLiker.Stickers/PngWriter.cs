using System.IO.Compression;

namespace PhotoLiker.Stickers
{
    /// <summary>
    /// Writes a picture with an alpha as a PNG (8 bits for each of red, green, blue and alpha). Written here so that the PNG of a sticker is made without a program to run and without Windows; each
    /// row is stored with whichever of the five filters of PNG makes it smallest, and compressed as hard as zlib goes, because a sticker that is over Telegram's 512 KB is one that is refused.
    /// </summary>
    internal static class PngWriter
    {
        private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private static readonly uint[] CrcTable = MakeCrcTable();

        public static byte[] Write(StickerPicture picture)
        {
            using var file = new MemoryStream();
            file.Write(Signature);

            var header = new byte[13];
            WriteInt(header, 0, picture.Width);
            WriteInt(header, 4, picture.Height);
            header[8] = 8; // bits for each channel
            header[9] = 6; // colour type: red, green, blue and alpha
            WriteChunk(file, "IHDR", header);
            WriteChunk(file, "IDAT", Compress(Filtered(picture)));
            WriteChunk(file, "IEND", []);
            return file.ToArray();
        }

        /// <summary>The rows as PNG stores them: a byte that says which filter, then the row filtered.</summary>
        internal static byte[] Filtered(StickerPicture picture)
        {
            const int bytesPerPixel = 4;
            var rowLength = picture.Width * bytesPerPixel;
            var result = new byte[picture.Height * (rowLength + 1)];

            var rgba = new byte[picture.Height][];
            for (var y = 0; y < picture.Height; y++)
            {
                var row = new byte[rowLength];
                for (var x = 0; x < picture.Width; x++)
                {
                    var from = (y * picture.Width + x) * 4;
                    row[x * 4] = picture.Bgra[from + 2];
                    row[x * 4 + 1] = picture.Bgra[from + 1];
                    row[x * 4 + 2] = picture.Bgra[from];
                    row[x * 4 + 3] = picture.Bgra[from + 3];
                }
                rgba[y] = row;
            }

            var candidates = new byte[5][];
            for (var f = 0; f < 5; f++)
                candidates[f] = new byte[rowLength];

            var zero = new byte[rowLength];
            for (var y = 0; y < picture.Height; y++)
            {
                var row = rgba[y];
                var above = y == 0 ? zero : rgba[y - 1];

                var best = 0;
                var bestCost = long.MaxValue;
                for (var filter = 0; filter < 5; filter++)
                {
                    var output = candidates[filter];
                    long cost = 0;
                    for (var i = 0; i < rowLength; i++)
                    {
                        var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                        var upLeft = i >= bytesPerPixel ? above[i - bytesPerPixel] : 0;
                        var value = (byte)(row[i] - Predict(filter, left, above[i], upLeft));
                        output[i] = value;
                        cost += Math.Abs((int)(sbyte)value); // the usual measure: rows of small numbers compress well (as an int: -128 has no sbyte opposite)
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = filter;
                    }
                }

                result[y * (rowLength + 1)] = (byte)best;
                Buffer.BlockCopy(candidates[best], 0, result, y * (rowLength + 1) + 1, rowLength);
            }
            return result;
        }

        private static int Predict(int filter, int left, int above, int upLeft) => filter switch
        {
            0 => 0,
            1 => left,
            2 => above,
            3 => (left + above) / 2,
            _ => Paeth(left, above, upLeft),
        };

        private static int Paeth(int a, int b, int c)
        {
            var estimate = a + b - c;
            var distanceA = Math.Abs(estimate - a);
            var distanceB = Math.Abs(estimate - b);
            var distanceC = Math.Abs(estimate - c);
            if (distanceA <= distanceB && distanceA <= distanceC)
                return a;
            return distanceB <= distanceC ? b : c;
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(data);
            return output.ToArray();
        }

        private static void WriteChunk(Stream file, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            file.Write(length);

            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            file.Write(typeBytes);
            file.Write(data);

            var crc = Crc(typeBytes, data);
            var check = new byte[4];
            WriteInt(check, 0, unchecked((int)crc));
            file.Write(check);
        }

        private static void WriteInt(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        internal static uint Crc(byte[] type, byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in type)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (var b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
