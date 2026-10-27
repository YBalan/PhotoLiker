using System.Globalization;

namespace PhotoLiker.Search
{
    /// <summary>The dates a camera wrote into a photo (local time of the camera, there is no time zone in EXIF).</summary>
    /// <param name="Original">When the picture was taken (DateTimeOriginal).</param>
    /// <param name="Digitized">When it was stored as a digital file (DateTimeDigitized), the same for a camera.</param>
    /// <param name="Modified">The last change by the camera or a program (DateTime).</param>
    public sealed record ExifDates(DateTime? Original, DateTime? Digitized, DateTime? Modified)
    {
        public static readonly ExifDates None = new(null, null, null);
    }

    /// <summary>
    /// Reads the dates out of the EXIF block of a JPEG without decoding the picture: only the header segments are read,
    /// which is a few kilobytes, so it is cheap also over a network.
    /// </summary>
    public static class ExifReader
    {
        private const ushort DateTimeTag = 0x0132;
        private const ushort ExifIfdTag = 0x8769;
        private const ushort DateTimeOriginalTag = 0x9003;
        private const ushort DateTimeDigitizedTag = 0x9004;

        private const int MaxEntries = 1000; // per directory; a damaged file must not send us round for long
        private static readonly string[] DateFormats = ["yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss"];

        /// <summary>
        /// The dates of a JPEG, or <see cref="ExifDates.None"/> if it is not one or has no (readable) EXIF block.
        /// I/O errors of the stream are not swallowed: the caller decides whether to try the file again.
        /// </summary>
        public static ExifDates Read(Stream jpeg)
        {
            try
            {
                return ReadSegments(jpeg);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or FormatException or OverflowException or EndOfStreamException)
            {
                return ExifDates.None; // damaged or cut off block
            }
        }

        private static ExifDates ReadSegments(Stream stream)
        {
            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
                return ExifDates.None;

            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0)
                    return ExifDates.None;
                if (b != 0xFF)
                    continue;

                int marker;
                do
                {
                    marker = stream.ReadByte(); // 0xFF bytes may be repeated before a marker
                }
                while (marker == 0xFF);

                if (marker < 0 || marker == 0xD9 || marker == 0xDA)
                    return ExifDates.None; // end of the image, or its data starts: no EXIF came before
                if (marker == 0x00 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
                    continue; // markers without a body

                var high = stream.ReadByte();
                var low = stream.ReadByte();
                if (low < 0)
                    return ExifDates.None;

                var length = (high << 8 | low) - 2;
                if (length < 0)
                    return ExifDates.None;

                if (marker != 0xE1)
                {
                    Skip(stream, length);
                    continue;
                }

                var body = new byte[length];
                stream.ReadExactly(body);
                if (length >= 6 && body[0] == 'E' && body[1] == 'x' && body[2] == 'i' && body[3] == 'f' && body[4] == 0 && body[5] == 0)
                    return ReadTiff(body.AsSpan(6));
                // another APP1 block (XMP): carry on
            }
        }

        private static void Skip(Stream stream, int count)
        {
            if (stream.CanSeek)
            {
                stream.Seek(count, SeekOrigin.Current);
                return;
            }

            var scratch = new byte[Math.Min(count, 4096)];
            while (count > 0)
            {
                var read = stream.Read(scratch, 0, Math.Min(count, scratch.Length));
                if (read <= 0)
                    throw new InvalidDataException("The file ends inside a segment.");
                count -= read;
            }
        }

        // ── the TIFF structure inside the EXIF block ───────────────────────

        private static ExifDates ReadTiff(ReadOnlySpan<byte> tiff)
        {
            var reader = new TiffReader(tiff);
            var ifd0 = reader.U32(4);

            DateTime? modified = null, original = null, digitized = null;
            long exifIfd = 0;
            foreach (var entry in reader.Entries(ifd0))
            {
                if (entry.Tag == DateTimeTag)
                    modified = ParseDate(reader.Ascii(entry));
                else if (entry.Tag == ExifIfdTag)
                    exifIfd = reader.U32(entry.ValueOffset);
            }

            if (exifIfd > 0)
            {
                foreach (var entry in reader.Entries(exifIfd))
                {
                    if (entry.Tag == DateTimeOriginalTag)
                        original = ParseDate(reader.Ascii(entry));
                    else if (entry.Tag == DateTimeDigitizedTag)
                        digitized = ParseDate(reader.Ascii(entry));
                }
            }

            return new ExifDates(original, digitized, modified);
        }

        private static DateTime? ParseDate(string text) =>
            DateTime.TryParseExact(text.Trim('\0', ' '), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

        private readonly record struct Entry(ushort Tag, ushort Type, uint Count, int ValueOffset);

        /// <summary>The numbers of a TIFF block in the byte order it declares; every access is checked, the block may be damaged.</summary>
        private readonly ref struct TiffReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private readonly bool _little;

            public TiffReader(ReadOnlySpan<byte> data)
            {
                if (data.Length < 8)
                    throw new InvalidDataException("The EXIF block is too short.");

                _data = data;
                _little = (data[0], data[1]) switch
                {
                    ((byte)'I', (byte)'I') => true,
                    ((byte)'M', (byte)'M') => false,
                    _ => throw new InvalidDataException("Unknown byte order."),
                };
                if (U16(2) != 42)
                    throw new InvalidDataException("Not a TIFF block.");
            }

            public ushort U16(long offset)
            {
                Check(offset, 2);
                var o = (int)offset;
                return _little ? (ushort)(_data[o] | _data[o + 1] << 8) : (ushort)(_data[o] << 8 | _data[o + 1]);
            }

            public uint U32(long offset)
            {
                Check(offset, 4);
                var o = (int)offset;
                return _little
                    ? (uint)(_data[o] | _data[o + 1] << 8 | _data[o + 2] << 16 | _data[o + 3] << 24)
                    : (uint)(_data[o] << 24 | _data[o + 1] << 16 | _data[o + 2] << 8 | _data[o + 3]);
            }

            public List<Entry> Entries(long ifdOffset)
            {
                var count = U16(ifdOffset);
                if (count > MaxEntries)
                    throw new InvalidDataException("Too many entries.");

                var entries = new List<Entry>(count);
                for (var i = 0; i < count; i++)
                {
                    var at = ifdOffset + 2 + i * 12L;
                    entries.Add(new Entry(U16(at), U16(at + 2), U32(at + 4), (int)(at + 8)));
                }
                return entries;
            }

            /// <summary>The text of an ASCII entry: up to four characters sit in the entry itself, longer ones where it points to.</summary>
            public string Ascii(Entry entry)
            {
                if (entry.Type != 2 || entry.Count > 64)
                    return string.Empty;

                long offset = entry.Count <= 4 ? entry.ValueOffset : U32(entry.ValueOffset);
                Check(offset, entry.Count);
                return System.Text.Encoding.ASCII.GetString(_data.Slice((int)offset, (int)entry.Count));
            }

            private void Check(long offset, long length)
            {
                if (offset < 0 || offset + length > _data.Length)
                    throw new InvalidDataException("A number lies outside of the EXIF block.");
            }
        }
    }
}
