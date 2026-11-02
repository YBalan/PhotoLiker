using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PhotoLiker.Core
{
    /// <summary>
    /// What to change in the Exif block of a JPEG. The dates are the time of the camera's clock (EXIF has no time zone), as
    /// <see cref="DateText.ExifFormat"/> writes them.
    /// </summary>
    public sealed record ExifChange
    {
        /// <summary>Clockwise quarter turns to give the orientation (negative turns counterclockwise); a whole number of circles leaves it as it is.</summary>
        public int QuarterTurns { get; init; }

        /// <summary>DateTimeOriginal, the date taken. Added to a photo that has none.</summary>
        public DateTime? Original { get; init; }

        /// <summary>DateTimeDigitized.</summary>
        public DateTime? Digitized { get; init; }

        /// <summary>DateTime of the main directory, shown as "Date Last Saved".</summary>
        public DateTime? LastSaved { get; init; }

        public bool IsEmpty => QuarterTurns % 4 == 0 && Original is null && Digitized is null && LastSaved is null;
    }

    /// <summary>Bytes of the file that get other values where they are. <paramref name="Expected"/> is what the plan saw there.</summary>
    public sealed record ExifPatch(long At, byte[] Expected, byte[] New);

    /// <summary>The bytes <c>[At, At + RemoveLength)</c> of the file are replaced by <paramref name="Insert"/>: the Exif block, a new or a grown one.</summary>
    public sealed record ExifSplice(long At, int RemoveLength, byte[] Insert)
    {
        public long LengthChange => Insert.Length - RemoveLength;

        /// <summary>The file with the splice made.</summary>
        public byte[] Apply(byte[] file)
        {
            var from = checked((int)At);
            var result = new byte[file.Length - RemoveLength + Insert.Length];
            file.AsSpan(0, from).CopyTo(result);
            Insert.CopyTo(result, from);
            file.AsSpan(from + RemoveLength).CopyTo(result.AsSpan(from + Insert.Length));
            return result;
        }
    }

    /// <summary>
    /// What <see cref="JpegExif.Plan"/> decided: <paramref name="Patches"/> when every value that is asked for has its place in the block
    /// already (the file keeps its size and nothing moves), otherwise a <paramref name="Splice"/> that replaces the block by a larger one.
    /// </summary>
    /// <param name="Orientation">The orientation the photo has afterwards.</param>
    public sealed record ExifPlan(int Orientation, IReadOnlyList<ExifPatch> Patches, ExifSplice? Splice)
    {
        public bool IsEmpty => Patches.Count == 0 && Splice is null;
    }

    /// <summary>What <see cref="JpegExif.Save"/> did to a file.</summary>
    /// <param name="Orientation">The EXIF orientation (1-8) the photo has now.</param>
    /// <param name="Length">The size of the file now.</param>
    /// <param name="Rewritten">The Exif block had to grow (a value was added), so the file was written anew and has another size. Otherwise bytes were changed in place.</param>
    /// <param name="DateKept">The file has the dates it was meant to have (the ones it had, or the ones it was given). False when the drive does not keep a date that is set.</param>
    /// <param name="CreatedUtc">The date the file was created, as the disk says now.</param>
    /// <param name="LastWriteUtc">The date the file was last changed, as the disk says now.</param>
    public readonly record struct ExifSaveResult(int Orientation, long Length, bool Rewritten, bool DateKept, DateTime CreatedUtc, DateTime LastWriteUtc);

    /// <summary>
    /// Changes the Exif block of a JPEG without decoding the picture: the orientation (see <see cref="JpegOrientation"/>) and the dates.
    ///
    /// A value that is in the block already is overwritten where it is: its size is fixed (two bytes for the orientation, nineteen
    /// characters for a date), so nothing moves and the file keeps its size. A value that is not there (a scan has no date taken, a photo
    /// edited by a program may have no orientation) is added: the block gets a new directory that holds it, written behind everything
    /// the block has (the old directory stays where it is, unused), and the new directory is the one the header points to. Nothing in the
    /// block moves, so what other entries point to (maker notes with their own absolute offsets, the thumbnail) stays valid. A photo with
    /// no Exif block gets a small new one after its first block. Such a file is written anew, next to the old one, and replaces it.
    /// </summary>
    public static class JpegExif
    {
        private const ushort OrientationTag = 0x0112;
        private const ushort LastSavedTag = 0x0132;
        private const ushort ExifIfdTag = 0x8769;
        private const ushort OriginalTag = 0x9003;
        private const ushort DigitizedTag = 0x9004;

        private const ushort AsciiType = 2;
        private const ushort ShortType = 3;
        private const ushort LongType = 4;

        private const int EntrySize = 12;
        private const int MaxEntries = 1000; // per directory; a damaged file must not send us round for long
        private const int MaxSegmentLength = 0xFFFF;
        private const int Tiff = 6;          // "Exif" and two zeros come first in the block
        private const int DateChars = 19;    // "2015:04:18 10:11:12"; the standard stores a zero after it as well

        private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

        // A drive may keep a date less exactly than the 100 ns of NTFS
        private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

        // ── the file ───────────────────────────────────────────────────────

        /// <summary>
        /// Makes the change in the file. The date of change and of creation of the file stay as they were, or both become
        /// <paramref name="fileDatesUtc"/> if that is given: writing moves them to now, and the app knows a photo by its size and date of change.
        /// A change that has nothing to do (a photo that already has these values is still written, but a photo that is not turned and has no date
        /// to write is not) leaves the file alone, unless dates are given, which are then set.
        /// </summary>
        /// <exception cref="InvalidDataException">Not a JPEG, its Exif block is damaged or has no room for the values. The file is not touched.</exception>
        /// <exception cref="IOException">The file could not be read or written, or another program changed it meanwhile. The file is left as it was.</exception>
        public static ExifSaveResult Save(string path, ExifChange change, DateTime? fileDatesUtc = null)
        {
            var info = new FileInfo(path);
            var created = fileDatesUtc ?? info.CreationTimeUtc;
            var written = fileDatesUtc ?? info.LastWriteTimeUtc;

            ExifPlan plan;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                plan = Plan(stream, change);

            if (plan.IsEmpty && fileDatesUtc is null)
                return new ExifSaveResult(plan.Orientation, info.Length, Rewritten: false, DateKept: true, info.CreationTimeUtc, info.LastWriteTimeUtc);

            var rewritten = plan.Splice is not null;
            if (plan.Splice is { } splice)
                Rewrite(path, change, splice, created, written);
            else if (plan.Patches.Count > 0)
                Patch(path, plan.Patches, created, written);

            // The dates were set through the handle that wrote the file, which is what keeps a network share from moving the date of change to
            // now when the handle is closed (a Samba share does, a moment after a date was set through another handle). Set again by the name,
            // for a drive that did not take them that way and for a file that was not written at all.
            if (!SameMoment(new FileInfo(path).CreationTimeUtc, created))
                File.SetCreationTimeUtc(path, created);
            if (!SameMoment(File.GetLastWriteTimeUtc(path), written))
                File.SetLastWriteTimeUtc(path, written);

            var after = new FileInfo(path);
            var kept = SameMoment(after.LastWriteTimeUtc, written) && SameMoment(after.CreationTimeUtc, created);
            return new ExifSaveResult(plan.Orientation, after.Length, rewritten, kept, after.CreationTimeUtc, after.LastWriteTimeUtc);
        }

        private static bool SameMoment(DateTime a, DateTime b) => (a - b).Duration() <= DateTolerance;

        private static void Patch(string path, IReadOnlyList<ExifPatch> patches, DateTime created, DateTime written)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            foreach (var patch in patches)
            {
                var current = new byte[patch.Expected.Length];
                stream.Position = patch.At;
                stream.ReadExactly(current);
                if (!current.AsSpan().SequenceEqual(patch.Expected))
                    throw new IOException(CoreStrings.ChangedMeanwhile);
            }

            foreach (var patch in patches)
            {
                stream.Position = patch.At;
                stream.Write(patch.New);
            }
            stream.Flush(flushToDisk: true);
            FileDates.SetOnHandle(stream, created, written);
        }

        /// <summary>The block has to grow: a copy with it is written next to the photo and takes its place.</summary>
        private static void Rewrite(string path, ExifChange change, ExifSplice expected, DateTime created, DateTime written)
        {
            var original = FileReading.ReadAllBytesShared(path);
            var again = Plan(new MemoryStream(original, writable: false), change);
            if (again.Splice is not { } splice || splice.At != expected.At || splice.RemoveLength != expected.RemoveLength)
                throw new IOException(CoreStrings.ChangedMeanwhile);

            var temp = path + ".saving";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(splice.Apply(original));
                    stream.Flush(flushToDisk: true);
                    FileDates.SetOnHandle(stream, created, written);
                }
                FileDates.MoveOver(temp, path);
            }
            catch
            {
                try { File.Delete(temp); } catch { /* the temporary file is all that is left; nothing else to do */ }
                throw;
            }
        }

        // ── planning ───────────────────────────────────────────────────────

        /// <summary>Works out how the change is made: only the start of the file is read (up to the Exif block).</summary>
        /// <exception cref="InvalidDataException">Not a JPEG, its Exif block is damaged or has no room for the values.</exception>
        public static ExifPlan Plan(Stream jpeg, ExifChange change)
        {
            var layout = Locate(jpeg);
            var turned = change.QuarterTurns % 4 != 0;
            var orientation = turned ? JpegOrientation.Turn(layout.Orientation ?? 1, change.QuarterTurns) : layout.Orientation ?? 1;

            var writes = new List<Write>(4);
            if (turned)
                writes.Add(new Write(OrientationTag, InExifIfd: false, orientation, null));
            if (change.LastSaved is { } lastSaved)
                writes.Add(new Write(LastSavedTag, InExifIfd: false, null, DateBytes(lastSaved)));
            if (change.Original is { } original)
                writes.Add(new Write(OriginalTag, InExifIfd: true, null, DateBytes(original)));
            if (change.Digitized is { } digitized)
                writes.Add(new Write(DigitizedTag, InExifIfd: true, null, DateBytes(digitized)));

            return writes.Count == 0 ? new ExifPlan(orientation, [], null) : Plan(layout, orientation, writes);
        }

        /// <summary>One value to put in a directory: the orientation (<paramref name="Short"/>) or a date (<paramref name="Text"/>, the 19 characters and a zero).</summary>
        private readonly record struct Write(ushort Tag, bool InExifIfd, int? Short, byte[]? Text);

        private static byte[] DateBytes(DateTime date) =>
            Encoding.ASCII.GetBytes(date.ToString(DateText.ExifFormat, CultureInfo.InvariantCulture) + "\0");

        /// <summary>A directory as it is read: its entries (with the place of each in the block), and the 4 bytes that follow them.</summary>
        private sealed class Ifd
        {
            public List<Entry> Entries { get; } = [];
            public byte[] Next { get; set; } = new byte[4];
        }

        /// <summary><paramref name="At"/> is where in the block the entry is; -1 for one that is new.</summary>
        private sealed record Entry(byte[] Bytes, int At);

        private static ExifPlan Plan(Layout layout, int orientation, List<Write> writes)
        {
            var fresh = layout.Payload is null;
            var payload = layout.Payload ?? FreshBlock();
            var big = !fresh && layout.BigEndian;

            var ifd0 = fresh ? new Ifd() : ReadIfd(payload, ToIndex(ReadU32(payload.AsSpan(Tiff + 4), big), payload.Length), big);
            Ifd? exif = null;
            if (writes.Any(w => w.InExifIfd) && FindEntry(ifd0, ExifIfdTag, big) is { } pointer)
            {
                if (ReadU16(pointer.Bytes.AsSpan(2), big) != LongType || ReadU32(pointer.Bytes.AsSpan(4), big) != 1)
                    throw Damaged();
                exif = ReadIfd(payload, ToIndex(ReadU32(pointer.Bytes.AsSpan(8), big), payload.Length), big);
            }

            // Does every value have its place already? Then they are written where they are.
            var places = writes.Select(w => PlaceOf(payload, w, w.InExifIfd ? exif : ifd0, big)).ToList();
            if (!fresh && places.All(p => p is not null))
            {
                var patches = new List<ExifPatch>(writes.Count);
                for (var i = 0; i < writes.Count; i++)
                {
                    var at = places[i]!.Value;
                    var bytes = ValueBytes(writes[i], big, forPlace: true);
                    patches.Add(new ExifPatch(layout.SegmentAt + 4 + at, payload.AsSpan(at, bytes.Length).ToArray(), bytes));
                }
                return new ExifPlan(orientation, patches, null);
            }

            // Otherwise the block grows. What has its place is written there in the copy; the rest goes into directories behind everything.
            var grown = new List<byte>(payload);
            bool ifd0Changed = false, exifChanged = false;
            for (var i = 0; i < writes.Count; i++)
            {
                var write = writes[i];

                // A date has its text elsewhere in the block, and is written there. The orientation is inside its entry, and the entry
                // is copied into the new directory: so that one is replaced like an entry that is new.
                if (places[i] is { } at && write.Short is null)
                {
                    var bytes = ValueBytes(write, big, forPlace: true);
                    for (var b = 0; b < bytes.Length; b++)
                        grown[at + b] = bytes[b];
                    continue;
                }

                var entry = write.Short is { } number
                    ? MakeEntry(write.Tag, ShortType, 1, Short(number, big), big)
                    : MakeEntry(write.Tag, AsciiType, (uint)write.Text!.Length, Long((uint)Append(grown, write.Text), big), big);
                if (write.InExifIfd)
                {
                    Put(exif ??= new Ifd(), entry, big);
                    exifChanged = true;
                }
                else
                {
                    Put(ifd0, entry, big);
                    ifd0Changed = true;
                }
            }

            if (exifChanged)
            {
                var exifAt = Append(grown, SerializeIfd(exif!, big));
                Put(ifd0, MakeEntry(ExifIfdTag, LongType, 1, Long((uint)exifAt, big), big), big);
                ifd0Changed = true;
            }

            if (ifd0Changed)
            {
                // The header points to the new main directory; the old one stays where it is, unused
                var ifd0At = Append(grown, SerializeIfd(ifd0, big));
                var headerPointer = new byte[4];
                WriteU32(headerPointer, big, (uint)ifd0At);
                for (var i = 0; i < 4; i++)
                    grown[Tiff + 4 + i] = headerPointer[i];
            }

            if (grown.Count + 2 > MaxSegmentLength)
                throw new InvalidDataException(CoreStrings.ExifNoRoom);

            var segment = new byte[4 + grown.Count];
            segment[0] = 0xFF;
            segment[1] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(grown.Count + 2));
            grown.CopyTo(segment, 4);
            return new ExifPlan(orientation, [], fresh
                ? new ExifSplice(layout.InsertAt, 0, segment)
                : new ExifSplice(layout.SegmentAt, 4 + payload.Length, segment));
        }

        /// <summary>
        /// The payload of a block that has nothing in it yet: "Exif" and two zeros, and the header of the TIFF data in the byte order of
        /// Intel. The header points nowhere until the first directory is written behind it.
        /// </summary>
        private static byte[] FreshBlock()
        {
            var block = new byte[Tiff + 8];
            ExifHeader.CopyTo(block, 0);
            block[Tiff] = (byte)'I';
            block[Tiff + 1] = (byte)'I';
            WriteU16(block.AsSpan(Tiff + 2), false, 42);
            return block;
        }

        private static int SerializedLength(Ifd ifd) => 2 + ifd.Entries.Count * EntrySize + 4;

        /// <summary>Where in the block (index into the payload) the value of the entry sits, if the entry is there and the value can be overwritten as it is. Null: it has to be added.</summary>
        private static int? PlaceOf(byte[] payload, Write write, Ifd? ifd, bool big)
        {
            if (ifd is null || FindEntry(ifd, write.Tag, big) is not { } entry)
                return null;

            var type = ReadU16(entry.Bytes.AsSpan(2), big);
            var count = ReadU32(entry.Bytes.AsSpan(4), big);
            if (write.Short is not null)
            {
                if (type != ShortType || count != 1)
                    throw new InvalidDataException(CoreStrings.OrientationNotNumber);
                return entry.At + 8;
            }

            // A date has its text elsewhere in the block (it is longer than the four bytes of an entry)
            if (type != AsciiType || count < DateChars || count <= 4)
                return null;
            var at = (long)Tiff + ReadU32(entry.Bytes.AsSpan(8), big);
            return at + DateChars <= payload.Length ? (int)at : null;
        }

        private static byte[] ValueBytes(Write write, bool big, bool forPlace)
        {
            if (write.Short is { } number)
                return Short(number, big)[..2];
            return forPlace ? write.Text![..DateChars] : write.Text!;
        }

        private static byte[] Short(int value, bool big)
        {
            var bytes = new byte[4]; // the value is in the first two bytes of the entry's last four
            WriteU16(bytes, big, (ushort)value);
            return bytes;
        }

        private static byte[] Long(uint value, bool big)
        {
            var bytes = new byte[4];
            WriteU32(bytes, big, value);
            return bytes;
        }

        private static byte[] MakeEntry(ushort tag, ushort type, uint count, byte[] last4, bool big)
        {
            var entry = new byte[EntrySize];
            WriteU16(entry.AsSpan(0), big, tag);
            WriteU16(entry.AsSpan(2), big, type);
            WriteU32(entry.AsSpan(4), big, count);
            last4.CopyTo(entry, 8);
            return entry;
        }

        /// <summary>The entry goes in place of the one with its tag, else before the first with a greater tag: a directory has to be in order.</summary>
        private static void Put(Ifd ifd, byte[] entry, bool big)
        {
            var tag = ReadU16(entry, big);
            var found = ifd.Entries.FindIndex(e => ReadU16(e.Bytes, big) == tag);
            if (found >= 0)
            {
                ifd.Entries[found] = new Entry(entry, -1);
                return;
            }

            var before = ifd.Entries.FindIndex(e => ReadU16(e.Bytes, big) > tag);
            ifd.Entries.Insert(before < 0 ? ifd.Entries.Count : before, new Entry(entry, -1));
        }

        private static Entry? FindEntry(Ifd ifd, ushort tag, bool big) =>
            ifd.Entries.FirstOrDefault(e => ReadU16(e.Bytes, big) == tag);

        private static byte[] SerializeIfd(Ifd ifd, bool big)
        {
            var bytes = new byte[SerializedLength(ifd)];
            WriteU16(bytes, big, (ushort)ifd.Entries.Count);
            for (var i = 0; i < ifd.Entries.Count; i++)
                ifd.Entries[i].Bytes.CopyTo(bytes, 2 + i * EntrySize);
            ifd.Next.CopyTo(bytes, 2 + ifd.Entries.Count * EntrySize);
            return bytes;
        }

        /// <summary>Adds data at the end of the block, on an even offset like every value in a TIFF block. Returns the offset from the start of the TIFF data.</summary>
        private static int Append(List<byte> block, byte[] data)
        {
            if ((block.Count - Tiff) % 2 != 0)
                block.Add(0);
            var offset = block.Count - Tiff;
            block.AddRange(data);
            return offset;
        }

        private static Ifd ReadIfd(byte[] payload, int at, bool big)
        {
            try
            {
                var count = ReadU16(payload.AsSpan(at), big);
                if (count > MaxEntries || at + 2 + count * EntrySize > payload.Length)
                    throw Damaged();

                var ifd = new Ifd();
                for (var i = 0; i < count; i++)
                {
                    var entryAt = at + 2 + i * EntrySize;
                    ifd.Entries.Add(new Entry(payload.AsSpan(entryAt, EntrySize).ToArray(), entryAt));
                }

                var nextAt = at + 2 + count * EntrySize;
                if (nextAt + 4 <= payload.Length)
                    ifd.Next = payload.AsSpan(nextAt, 4).ToArray();
                return ifd;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw Damaged();
            }
        }

        private static int ToIndex(uint offset, int length)
        {
            var at = (long)Tiff + offset;
            if (offset < 8 || at + 2 > length)
                throw Damaged();
            return (int)at;
        }

        private static InvalidDataException Damaged() => new(CoreStrings.ExifDamaged);

        // ── finding things in the file ─────────────────────────────────────

        /// <summary>Where things are in a JPEG: the orientation, and the Exif block that has to grow if a value is added to it.</summary>
        /// <param name="Orientation">The number stored, or null if the photo has none.</param>
        /// <param name="BigEndian">The byte order of the Exif block.</param>
        /// <param name="SegmentAt">Where the Exif block starts in the file (its marker); -1 when the photo has none.</param>
        /// <param name="Payload">The Exif block without marker and length: "Exif", two zeros, and the TIFF data. Null when the photo has none.</param>
        /// <param name="InsertAt">Where a new Exif block goes when there is none: after the start of the file, and after a JFIF block if that comes first.</param>
        internal sealed record Layout(int? Orientation, bool BigEndian, long SegmentAt, byte[]? Payload, long InsertAt);

        internal static Layout Locate(Stream stream)
        {
            Span<byte> head = stackalloc byte[4];
            stream.Position = 0;
            if (stream.Read(head[..2]) != 2 || head[0] != 0xFF || head[1] != 0xD8)
                throw new InvalidDataException(CoreStrings.NotJpeg);

            long insertAt = 2;
            for (var first = true; ; first = false)
            {
                var markerAt = stream.Position;
                if (stream.Read(head) < 4)
                    break; // the file ends: no Exif came before
                if (head[0] != 0xFF)
                    throw new InvalidDataException(CoreStrings.JpegDamaged);

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
                    throw new InvalidDataException(CoreStrings.JpegDamaged);

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

            return new Layout(null, BigEndian: false, SegmentAt: -1, Payload: null, insertAt);
        }

        private static Layout LocateInExif(byte[] payload, long segmentAt)
        {
            if (payload.Length < Tiff + 8)
                throw new InvalidDataException(CoreStrings.ExifTooShort);

            try
            {
                var big = (payload[Tiff], payload[Tiff + 1]) switch
                {
                    ((byte)'M', (byte)'M') => true,
                    ((byte)'I', (byte)'I') => false,
                    _ => throw new InvalidDataException(CoreStrings.ExifByteOrder),
                };
                if (ReadU16(payload.AsSpan(Tiff + 2), big) != 42)
                    throw new InvalidDataException(CoreStrings.ExifNotTiff);

                var ifd0 = Tiff + (int)ReadU32(payload.AsSpan(Tiff + 4), big);
                var count = ReadU16(payload.AsSpan(ifd0), big);
                if (count > MaxEntries || ifd0 + 2 + count * EntrySize > payload.Length)
                    throw new InvalidDataException(CoreStrings.ExifDamaged);

                for (var i = 0; i < count; i++)
                {
                    var entry = ifd0 + 2 + i * EntrySize;
                    if (ReadU16(payload.AsSpan(entry), big) != OrientationTag)
                        continue;

                    if (ReadU16(payload.AsSpan(entry + 2), big) != ShortType || ReadU32(payload.AsSpan(entry + 4), big) != 1)
                        throw new InvalidDataException(CoreStrings.OrientationNotNumber);

                    return new Layout(ReadU16(payload.AsSpan(entry + 8), big), big, segmentAt, payload, segmentAt + 4 + payload.Length);
                }

                return new Layout(null, big, segmentAt, payload, segmentAt + 4 + payload.Length);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidDataException(CoreStrings.ExifDamaged);
            }
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
