namespace PhotoLiker.PhotoBook
{
    /// <summary>Reads the size of a PNG or a JPEG from the first bytes of the file, so that a picture in the text of a book needs no picture library to be laid out.</summary>
    internal static class ImageHeader
    {
        /// <summary>True when the bytes start a PNG or a JPEG that has a size; <paramref name="png"/> says which.</summary>
        public static bool TryRead(byte[] data, out bool png, out int width, out int height)
        {
            png = false;
            width = height = 0;

            if (data.Length > 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                // The header chunk of a PNG: its width and height are the first numbers after the name of the chunk
                var w = ((uint)data[16] << 24) | ((uint)data[17] << 16) | ((uint)data[18] << 8) | data[19];
                var h = ((uint)data[20] << 24) | ((uint)data[21] << 16) | ((uint)data[22] << 8) | data[23];
                if (w is 0 or > 100_000 || h is 0 or > 100_000)
                    return false;

                png = true;
                width = (int)w;
                height = (int)h;
                return true;
            }

            if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
            {
                // A JPEG is a row of segments; the one that starts a frame has the size
                var i = 2;
                while (i + 9 < data.Length)
                {
                    if (data[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }

                    var marker = data[i + 1];
                    if (marker == 0xFF)
                    {
                        i++;
                        continue;
                    }

                    if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
                    {
                        i += 2; // markers that have nothing after them
                        continue;
                    }

                    var length = (data[i + 2] << 8) | data[i + 3];
                    if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                    {
                        height = (data[i + 5] << 8) | data[i + 6];
                        width = (data[i + 7] << 8) | data[i + 8];
                        return width > 0 && height > 0;
                    }

                    if (length < 2)
                        return false;

                    i += 2 + length;
                }
            }

            return false;
        }
    }
}
