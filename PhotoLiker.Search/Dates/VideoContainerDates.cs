using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoLiker.Search
{
    /// <summary>One place in a video file where the date it was recorded stands: the bytes that are there (<see cref="Old"/>) and the bytes that are to be there (<see cref="New"/>), the same number.</summary>
    public sealed record VideoDatePatch(long Offset, byte[] Old, byte[] New);

    /// <summary>
    /// Finds where a video file says when it was recorded, and works out what has to be written there for another date, without moving a single other byte: the file keeps
    /// its size, and no picture or sound is touched. That is what ffmpeg reads as the <c>creation_time</c> of the video (see <c>VideoProbe</c>), which is the date the program goes by.
    /// <list type="bullet">
    /// <item>MP4, MOV, M4V, 3GP: the creation time (and the time of change, when it is the same) in the movie header, and in the track headers that have one. Seconds since 1904, UTC.</item>
    /// <item>AVI: the <c>IDIT</c> chunk that cameras write ("Sat Jun 05 13:10:45 2010", the clock of the camera, no time zone), in the form it already has.</item>
    /// </list>
    /// A video of another kind, or one that has no such place (an AVI with no <c>IDIT</c>), has no place for a date: it can only get the dates of its file.
    /// </summary>
    public static partial class VideoContainerDates
    {
        private static readonly string[] Extensions = [".mp4", ".m4v", ".mov", ".3gp", ".3g2", ".avi"];

        private static readonly DateTime Epoch1904 = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string[] IsoTypes = ["ftyp", "moov", "mdat", "free", "wide", "skip", "pnot"];

        /// <summary>Whether a file of this name may have a place for a date (its kind is judged by the extension; <see cref="Plan"/> looks inside).</summary>
        public static bool CanWrite(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// What to write into the video to make it say it was recorded at <paramref name="taken"/> (the time of the camera's clock, whole seconds; the computer's time zone is taken to be the camera's,
        /// as for photos): the patches, none if it says so already; null if the file has no place for a date. Reads the headers only. Throws <see cref="InvalidDataException"/>
        /// for a date the video cannot hold.
        /// </summary>
        public static IReadOnlyList<VideoDatePatch>? Plan(Stream stream, DateTime taken)
        {
            taken = new DateTime(taken.Ticks - taken.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
            if (stream.Length < 12)
                return null;

            var head = ReadAt(stream, 0, 12);
            if (head is null)
                return null;
            if (Latin1(head, 0, 4) == "RIFF" && Latin1(head, 8, 4) == "AVI ")
                return PlanAvi(stream, taken);
            if (IsoTypes.Contains(Latin1(head, 4, 4)))
                return PlanIso(stream, DateAlignment.ToUtc(taken));
            return null;
        }

        // ── MP4, MOV, 3GP ──────────────────────────────────────────────────

        private readonly record struct Box(string Type, long Start, long HeaderSize, long End)
        {
            public long Body => Start + HeaderSize;
        }

        private static IEnumerable<Box> Boxes(Stream stream, long from, long to)
        {
            var position = from;
            while (position + 8 <= to)
            {
                var head = ReadAt(stream, position, 8);
                if (head is null)
                    yield break;

                long size = BinaryPrimitives.ReadUInt32BigEndian(head);
                var type = Latin1(head, 4, 4);
                long header = 8;
                if (size == 1)
                {
                    var large = ReadAt(stream, position + 8, 8);
                    if (large is null)
                        yield break;
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(large);
                    header = 16;
                }
                else if (size == 0)
                {
                    size = to - position; // the last box runs to the end
                }

                if (size < header || size > to - position)
                    yield break; // damaged: nothing after it can be trusted
                yield return new Box(type, position, header, position + size);
                position += size;
            }
        }

        private static List<VideoDatePatch>? PlanIso(Stream stream, DateTime takenUtc)
        {
            var seconds = (long)Math.Floor((takenUtc - Epoch1904).TotalSeconds);
            if (seconds < 0)
                throw new InvalidDataException(SearchLibStrings.VideoDateOutOfRange);

            foreach (var moov in Boxes(stream, 0, stream.Length))
            {
                if (moov.Type != "moov")
                    continue;

                var patches = new List<VideoDatePatch>();
                var movieHeader = false;
                foreach (var child in Boxes(stream, moov.Body, moov.End))
                {
                    if (child.Type == "mvhd")
                    {
                        movieHeader = true; // the movie header is where ffmpeg looks first: it gets the date even if it had none
                        AddIfChanged(patches, TimesPatch(stream, child, seconds, onlyIfSet: false));
                    }
                    else if (child.Type == "trak")
                    {
                        foreach (var part in Boxes(stream, child.Body, child.End))
                        {
                            if (part.Type == "tkhd")
                            {
                                AddIfChanged(patches, TimesPatch(stream, part, seconds, onlyIfSet: true));
                            }
                            else if (part.Type == "mdia")
                            {
                                foreach (var media in Boxes(stream, part.Body, part.End).Where(b => b.Type == "mdhd"))
                                    AddIfChanged(patches, TimesPatch(stream, media, seconds, onlyIfSet: true));
                            }
                        }
                    }
                }
                return movieHeader ? patches : null;
            }
            return null;
        }

        private static void AddIfChanged(List<VideoDatePatch> patches, VideoDatePatch? patch)
        {
            if (patch is not null && !patch.Old.AsSpan().SequenceEqual(patch.New))
                patches.Add(patch);
        }

        /// <summary>
        /// The creation time and the time of change of a header box (they stand side by side after the version and flags): the creation time becomes the date, and the
        /// time of change too if it was the same as the creation time, as cameras write them. A header that has no creation time (0 means "not set") is left so when <paramref name="onlyIfSet"/>.
        /// </summary>
        private static VideoDatePatch? TimesPatch(Stream stream, Box box, long seconds, bool onlyIfSet)
        {
            var start = box.Body;
            var version = ReadAt(stream, start, 1)?[0];
            var width = version switch { 0 => 4, 1 => 8, _ => 0 };
            if (width == 0 || box.End - start < 4 + 2 * width)
                return null;

            var old = ReadAt(stream, start + 4, 2 * width);
            if (old is null)
                return null;

            var creation = Read(old, 0, width);
            var modification = Read(old, width, width);
            if (onlyIfSet && creation == 0)
                return null;
            if (width == 4 && seconds > uint.MaxValue)
                throw new InvalidDataException(SearchLibStrings.VideoDateOutOfRange);

            var update = new byte[2 * width];
            Write(update, 0, width, (ulong)seconds);
            Write(update, width, width, modification == creation ? (ulong)seconds : modification);
            return new VideoDatePatch(start + 4, old, update);
        }

        private static ulong Read(byte[] bytes, int at, int width) =>
            width == 8 ? BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));

        private static void Write(byte[] bytes, int at, int width, ulong value)
        {
            if (width == 8)
                BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(at), value);
            else
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), (uint)value);
        }

        // ── AVI ────────────────────────────────────────────────────────────

        private const int MostDepth = 4;

        private static List<VideoDatePatch>? PlanAvi(Stream stream, DateTime taken)
        {
            var found = FindIdit(stream, 12, stream.Length, MostDepth);
            if (found is not { } place)
                return null;

            var (offset, text) = place;
            var original = Encoding.Latin1.GetString(text).TrimEnd('\0');
            string? replacement = null;
            if (CameraStyle().IsMatch(original))
                replacement = taken.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);
            else if (SlashStyle().IsMatch(original))
                replacement = taken.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (replacement is null)
                return null; // a form of the date that is not known: it is not guessed at

            var old = text[..replacement.Length];
            var update = Encoding.Latin1.GetBytes(replacement);
            var patches = new List<VideoDatePatch>();
            AddIfChanged(patches, new VideoDatePatch(offset, old, update));
            return patches;
        }

        /// <summary>The <c>IDIT</c> chunk of the header (before the pictures begin): where its text starts, and the text (up to 64 bytes).</summary>
        private static (long Offset, byte[] Text)? FindIdit(Stream stream, long from, long to, int depth)
        {
            var position = from;
            while (position + 8 <= to)
            {
                var head = ReadAt(stream, position, 8);
                if (head is null)
                    return null;

                var id = Latin1(head, 0, 4);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
                var body = position + 8;
                if (size > to - body)
                    return null;

                if (id == "IDIT")
                {
                    var text = ReadAt(stream, body, (int)Math.Min(size, 64));
                    return text is null ? null : (body, text);
                }

                if (id == "LIST")
                {
                    var type = ReadAt(stream, body, 4);
                    if (type is null || Latin1(type, 0, 4) == "movi")
                        return null; // the pictures start here: the header is over
                    if (depth > 0 && FindIdit(stream, body + 4, body + size, depth - 1) is { } inner)
                        return inner;
                }

                position = body + size + (size & 1);
            }
            return null;
        }

        // "Sat Jun 05 13:10:45 2010" (what ffmpeg reads as the standard AVI date), and "2005/04/03 15:04:43"
        [GeneratedRegex(@"^[A-Za-z]{3} [A-Za-z]{3} [ \d]\d \d{2}:\d{2}:\d{2} \d{4}", RegexOptions.CultureInvariant)]
        private static partial Regex CameraStyle();

        [GeneratedRegex(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}", RegexOptions.CultureInvariant)]
        private static partial Regex SlashStyle();

        // ── reading ────────────────────────────────────────────────────────

        private static byte[]? ReadAt(Stream stream, long offset, int count)
        {
            if (offset < 0 || offset + count > stream.Length)
                return null;

            stream.Seek(offset, SeekOrigin.Begin);
            var bytes = new byte[count];
            try
            {
                stream.ReadExactly(bytes);
                return bytes;
            }
            catch (EndOfStreamException)
            {
                return null;
            }
        }

        private static string Latin1(byte[] bytes, int at, int count) => Encoding.Latin1.GetString(bytes, at, count);
    }
}
