namespace PhotoLiker.Search.Tests;

/// <summary>A JPEG that is only a header with an EXIF block: enough to read dates from, not to look at.</summary>
internal static class ExifJpeg
{
    public static byte[] Build(string? original = null, string? digitized = null, string? modified = null, bool bigEndian = false, bool withJfif = false)
    {
        var tiff = Tiff(original, digitized, modified, bigEndian);
        using var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);
        if (withJfif)
        {
            jpeg.Write([0xFF, 0xE0, 0x00, 0x10]);
            jpeg.Write("JFIF\0\x01\x01\0\0\x01\0\x01\0\0"u8);
        }

        var length = 2 + 6 + tiff.Length;
        jpeg.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
        jpeg.Write("Exif\0\0"u8);
        jpeg.Write(tiff);
        jpeg.Write([0xFF, 0xD9]);
        return jpeg.ToArray();
    }

    public static byte[] Tiff(string? original, string? digitized, string? modified, bool big)
    {
        var data = new List<byte>();
        var ifd0Entries = 1 + (modified is null ? 0 : 1);
        var exifEntries = (original is null ? 0 : 1) + (digitized is null ? 0 : 1);
        var exifIfdOffset = 8 + 2 + ifd0Entries * 12 + 4;
        var dataStart = exifIfdOffset + 2 + exifEntries * 12 + 4;

        uint Place(string text)
        {
            var offset = dataStart + data.Count;
            data.AddRange(System.Text.Encoding.ASCII.GetBytes(text + "\0"));
            return (uint)offset;
        }

        using var tiff = new MemoryStream();
        void U16(int value) => tiff.Write(big ? [(byte)(value >> 8), (byte)value] : new[] { (byte)value, (byte)(value >> 8) });
        void U32(uint value) => tiff.Write(big
            ? [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]
            : new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });
        void Text(int tag, string text) { U16(tag); U16(2); U32((uint)text.Length + 1); U32(Place(text)); }

        tiff.Write(big ? "MM"u8 : "II"u8);
        U16(42);
        U32(8);

        U16(ifd0Entries);
        if (modified is not null)
            Text(0x0132, modified);
        U16(0x8769); U16(4); U32(1); U32((uint)exifIfdOffset);
        U32(0);

        U16(exifEntries);
        if (original is not null)
            Text(0x9003, original);
        if (digitized is not null)
            Text(0x9004, digitized);
        U32(0);

        tiff.Write(data.ToArray());
        return tiff.ToArray();
    }
}
