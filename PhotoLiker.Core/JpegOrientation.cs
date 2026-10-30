using System.Buffers.Binary;

namespace PhotoLiker.Core
{
    /// <summary>What <see cref="JpegOrientation.TurnClockwise"/> did to a file.</summary>
    /// <param name="Orientation">The EXIF orientation (1-8) the photo has now.</param>
    /// <param name="Length">The size of the file now.</param>
    /// <param name="Rewritten">The photo had no orientation yet, so its Exif block grew and the file was written anew (it has another size now). Otherwise two bytes were changed in place.</param>
    /// <param name="DateKept">The file has the date of change it had before. False when the drive does not keep a date that is set.</param>
    public readonly record struct TurnResult(int Orientation, long Length, bool Rewritten, bool DateKept);

    /// <summary>
    /// Turns a JPEG by changing its EXIF orientation, the number that tells every viewer how the stored pixels are turned to show the
    /// photo upright (this app, Windows, phones and browsers all follow it). The picture data is never decoded or written again, so
    /// nothing is lost however often a photo is turned, and a large photo on a network share costs two bytes instead of a new file.
    ///
    /// A photo that has the orientation already (every phone and most cameras write it, also when it is 1) gets the new number in
    /// the same place: its size does not change. A photo without it gets it added: a new Exif block if it has none, else the
    /// directory of the block is copied to the end of the block with the entry in it, which moves nothing that offsets point to
    /// (maker notes keep their places). That file is written anew, next to the old one, and replaces it. In both cases the date of
    /// change the file had stays, because the app knows a photo by its size and date (see <see cref="CopyName"/>).
    /// </summary>
    public static class JpegOrientation
    {
        private const ushort OrientationTag = 0x0112;
        private const ushort ShortType = 3;
        private const int EntrySize = 12;
        private const int MaxEntries = 1000; // per directory; a damaged file must not send us round for long
        private const int MaxSegmentLength = 0xFFFF;
        private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

        // A drive may keep a date less exactly than the 100 ns of NTFS
        private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

        // The orientation a photo gets when it is turned a quarter clockwise, by the orientation it has (index 0 is not an orientation):
        // 1 normal -> 6, 6 -> 3 (upside down) -> 8 -> 1; the mirrored ones 2 -> 7 -> 4 -> 5 -> 2
        private static readonly int[] QuarterClockwise = [0, 6, 7, 8, 5, 2, 3, 4, 1];

        public static bool IsJpeg(string path) =>
            Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

        /// <summary>The orientation of a photo that is turned clockwise by quarter turns. A number that is not an orientation is taken for 1 (nothing).</summary>
        public static int Turn(int orientation, int clockwiseQuarterTurns)
        {
            if (orientation is < 1 or > 8)
                orientation = 1;

            for (var turns = ((clockwiseQuarterTurns % 4) + 4) % 4; turns > 0; turns--)
                orientation = QuarterClockwise[orientation];
            return orientation;
        }

        /// <summary>The orientation a JPEG has, or 1 when it says none.</summary>
        /// <exception cref="InvalidDataException">Not a JPEG, or its Exif block is damaged.</exception>
        public static int Read(Stream jpeg) => Locate(jpeg).Orientation ?? 1;

        /// <summary>
        /// Turns the photo a quarter clockwise (or by <paramref name="quarterTurns"/>) for everyone who shows it upright. The date of
        /// change and of creation of the file stay as they were.
        /// </summary>
        /// <exception cref="InvalidDataException">Not a JPEG, its Exif block is damaged or has no room for the orientation. The file is not touched.</exception>
        /// <exception cref="IOException">The file could not be read or written, or another program changed it meanwhile. The file is left as it was.</exception>
        public static TurnResult TurnClockwise(string path, int quarterTurns = 1)
        {
            var info = new FileInfo(path);
            var written = info.LastWriteTimeUtc;
            var created = info.CreationTimeUtc;

            Layout layout;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                layout = Locate(stream);

            var orientation = Turn(layout.Orientation ?? 1, quarterTurns);
            if (layout.Orientation is { } now && orientation == now)
                return new TurnResult(orientation, info.Length, Rewritten: false, DateKept: true);

            var rewritten = layout.ValueAt is null;
            if (rewritten)
                Rewrite(path, orientation, created);
            else
                Patch(path, layout, orientation);

            File.SetLastWriteTimeUtc(path, written);
            var kept = (File.GetLastWriteTimeUtc(path) - written).Duration() <= DateTolerance;
            return new TurnResult(orientation, new FileInfo(path).Length, rewritten, kept);
        }

        // ── the two ways to change a file ──────────────────────────────────

        /// <summary>The photo has an orientation: its two bytes get the new number, where they are.</summary>
        private static void Patch(string path, Layout layout, int orientation)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            Span<byte> bytes = stackalloc byte[2];
            stream.Position = layout.ValueAt!.Value;
            stream.ReadExactly(bytes);
            if (ReadU16(bytes, layout.BigEndian) != layout.Orientation)
                throw new IOException("The photo was changed by another program meanwhile.");

            WriteU16(bytes, layout.BigEndian, (ushort)orientation);
            stream.Position = layout.ValueAt.Value;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        /// <summary>The photo has no orientation: a copy with one is written next to it and takes its place.</summary>
        private static void Rewrite(string path, int orientation, DateTime created)
        {
            var original = FileReading.ReadAllBytesShared(path);
            var layout = Locate(new MemoryStream(original, writable: false));
            if (layout.ValueAt is not null)
                throw new IOException("The photo was changed by another program meanwhile.");

            var turned = WithOrientation(original, layout, orientation);

            var temp = path + ".turning";
            try
            {
                File.WriteAllBytes(temp, turned);
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* the temporary file is all that is left; nothing else to do */ }
                throw;
            }

            File.SetCreationTimeUtc(path, created);
        }

        // ── finding the orientation ────────────────────────────────────────

        /// <summary>Where things are in a JPEG: the orientation, and the Exif block that has to grow if there is none.</summary>
        /// <param name="Orientation">The number stored, or null if the photo has none.</param>
        /// <param name="ValueAt">Where in the file the two bytes of the number are; null when there is none.</param>
        /// <param name="BigEndian">The byte order of the Exif block.</param>
        /// <param name="SegmentAt">Where the Exif block starts in the file (its marker); -1 when the photo has none.</param>
        /// <param name="Payload">The Exif block without marker and length: "Exif", two zeros, and the TIFF data. Null when the photo has none.</param>
        /// <param name="InsertAt">Where a new Exif block goes when there is none: after the start of the file, and after a JFIF block if that comes first.</param>
        private sealed record Layout(int? Orientation, long? ValueAt, bool BigEndian, long SegmentAt, byte[]? Payload, long InsertAt);

        private static Layout Locate(Stream stream)
        {
            Span<byte> head = stackalloc byte[4];
            stream.Position = 0;
            if (stream.Read(head[..2]) != 2 || head[0] != 0xFF || head[1] != 0xD8)
                throw new InvalidDataException("This is not a JPEG.");

            long insertAt = 2;
            for (var first = true; ; first = false)
            {
                var markerAt = stream.Position;
                if (stream.Read(head) < 4)
                    break; // the file ends: no Exif came before
                if (head[0] != 0xFF)
                    throw new InvalidDataException("The JPEG is damaged.");

                var marker = head[1];
                if (marker == 0xFF)
                {
                    stream.Position = markerAt + 1; // 0xFF bytes may be repeated before a marker
                    continue;
                }
                if (marker is 0xDA or 0xD9)
                    break; // the picture data starts, or ends: no Exif came before
                if (marker is 0x00 or 0x01 or (>= 0xD0 and <= 0xD7))
                {
                    stream.Position = markerAt + 2; // markers without a body
                    continue;
                }

                var length = head[2] << 8 | head[3];
                if (length < 2)
                    throw new InvalidDataException("The JPEG is damaged.");

                if (marker == 0xE0 && first)
                    insertAt = markerAt + 2 + length; // JFIF has to stay the first block
                if (marker == 0xE1 && length - 2 >= ExifHeader.Length)
                {
                    var payload = new byte[length - 2];
                    stream.ReadExactly(payload);
                    if (payload.AsSpan(0, ExifHeader.Length).SequenceEqual(ExifHeader))
                        return LocateInExif(payload, markerAt);
                }

                stream.Position = markerAt + 2 + length;
            }

            return new Layout(null, null, BigEndian: false, SegmentAt: -1, Payload: null, insertAt);
        }

        private static Layout LocateInExif(byte[] payload, long segmentAt)
        {
            const int tiff = 6; // "Exif" and two zeros come first
            if (payload.Length < tiff + 8)
                throw new InvalidDataException("The Exif block is too short.");

            try
            {
                var big = (payload[tiff], payload[tiff + 1]) switch
                {
                    ((byte)'M', (byte)'M') => true,
                    ((byte)'I', (byte)'I') => false,
                    _ => throw new InvalidDataException("The Exif block has an unknown byte order."),
                };
                if (ReadU16(payload.AsSpan(tiff + 2), big) != 42)
                    throw new InvalidDataException("The Exif block is not a TIFF block.");

                var ifd0 = tiff + (int)ReadU32(payload.AsSpan(tiff + 4), big);
                var count = ReadU16(payload.AsSpan(ifd0), big);
                if (count > MaxEntries || ifd0 + 2 + count * EntrySize > payload.Length)
                    throw new InvalidDataException("The Exif block is damaged.");

                for (var i = 0; i < count; i++)
                {
                    var entry = ifd0 + 2 + i * EntrySize;
                    if (ReadU16(payload.AsSpan(entry), big) != OrientationTag)
                        continue;

                    if (ReadU16(payload.AsSpan(entry + 2), big) != ShortType || ReadU32(payload.AsSpan(entry + 4), big) != 1)
                        throw new InvalidDataException("The orientation is not stored as one number.");

                    var value = entry + 8;
                    return new Layout(ReadU16(payload.AsSpan(value), big), segmentAt + 4 + value, big, segmentAt, payload, segmentAt + 4 + payload.Length);
                }

                return new Layout(null, null, big, segmentAt, payload, segmentAt + 4 + payload.Length);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException("The Exif block is damaged.");
            }
        }

        // ── adding the orientation ─────────────────────────────────────────

        private static byte[] WithOrientation(byte[] file, Layout layout, int orientation)
        {
            if (layout.Payload is not { } payload)
            {
                return Splice(file, (int)layout.InsertAt, (int)layout.InsertAt, NewExifBlock(orientation));
            }

            var big = layout.BigEndian;
            const int tiff = 6;
            var ifd0 = tiff + (int)ReadU32(payload.AsSpan(tiff + 4), big);
            var count = ReadU16(payload.AsSpan(ifd0), big);
            var entries = payload.AsSpan(ifd0 + 2, count * EntrySize);

            // The old directory stays where it is, unused. The new one goes behind everything, on an even offset, so no offset
            // inside the block changes; the tags have to be in order, so the new entry goes before the first one with a greater tag.
            var newIfdAt = payload.Length - tiff + (payload.Length - tiff) % 2;
            ReadOnlySpan<byte> next = ifd0 + 2 + count * EntrySize + 4 <= payload.Length ? payload.AsSpan(ifd0 + 2 + count * EntrySize, 4) : new byte[4];
            var entry = new byte[EntrySize];
            WriteU16(entry.AsSpan(0), big, OrientationTag);
            WriteU16(entry.AsSpan(2), big, ShortType);
            WriteU32(entry.AsSpan(4), big, 1);
            WriteU16(entry.AsSpan(8), big, (ushort)orientation);

            var before = 0;
            while (before < count && ReadU16(entries[(before * EntrySize)..], big) < OrientationTag)
                before++;

            var directory = new byte[2 + (count + 1) * EntrySize + 4];
            WriteU16(directory, big, (ushort)(count + 1));
            entries[..(before * EntrySize)].CopyTo(directory.AsSpan(2));
            entry.CopyTo(directory, 2 + before * EntrySize);
            entries[(before * EntrySize)..].CopyTo(directory.AsSpan(2 + (before + 1) * EntrySize));
            next.CopyTo(directory.AsSpan(2 + (count + 1) * EntrySize));

            var grown = new byte[tiff + newIfdAt + directory.Length];
            payload.CopyTo(grown, 0);
            directory.CopyTo(grown, tiff + newIfdAt);
            WriteU32(grown.AsSpan(tiff + 4), big, (uint)newIfdAt);

            if (grown.Length + 2 > MaxSegmentLength)
                throw new InvalidDataException("The Exif block of this photo has no room for the orientation.");

            var segment = new byte[4 + grown.Length];
            segment[0] = 0xFF;
            segment[1] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(grown.Length + 2));
            grown.CopyTo(segment, 4);
            return Splice(file, (int)layout.SegmentAt, (int)layout.SegmentAt + 4 + payload.Length, segment);
        }

        /// <summary>The file with the bytes from <paramref name="from"/> up to <paramref name="to"/> replaced by <paramref name="insert"/>.</summary>
        private static byte[] Splice(byte[] file, int from, int to, byte[] insert)
        {
            var result = new byte[file.Length - (to - from) + insert.Length];
            file.AsSpan(0, from).CopyTo(result);
            insert.CopyTo(result, from);
            file.AsSpan(to).CopyTo(result.AsSpan(from + insert.Length));
            return result;
        }

        /// <summary>A whole Exif block that has nothing but the orientation, in the byte order of Intel.</summary>
        private static byte[] NewExifBlock(int orientation)
        {
            var block = new byte[4 + ExifHeader.Length + 8 + 2 + EntrySize + 4];
            block[0] = 0xFF;
            block[1] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(2), (ushort)(block.Length - 2));
            ExifHeader.CopyTo(block, 4);

            var tiff = block.AsSpan(4 + ExifHeader.Length);
            "II"u8.CopyTo(tiff);
            WriteU16(tiff[2..], big: false, 42);
            WriteU32(tiff[4..], big: false, 8);          // the directory follows the header
            WriteU16(tiff[8..], big: false, 1);          // one entry
            WriteU16(tiff[10..], big: false, OrientationTag);
            WriteU16(tiff[12..], big: false, ShortType);
            WriteU32(tiff[14..], big: false, 1);
            WriteU16(tiff[18..], big: false, (ushort)orientation);
            return block;                                  // the last four bytes, "no next directory", stay zero
        }

        // ── numbers in the byte order of the block ─────────────────────────

        private static ushort ReadU16(ReadOnlySpan<byte> data, bool big) =>
            big ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);

        private static uint ReadU32(ReadOnlySpan<byte> data, bool big) =>
            big ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);

        private static void WriteU16(Span<byte> data, bool big, ushort value)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(data, value); else BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        }

        private static void WriteU32(Span<byte> data, bool big, uint value)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(data, value); else BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        }
    }
}
