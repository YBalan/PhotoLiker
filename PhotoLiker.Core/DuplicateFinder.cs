using System.Security.Cryptography;

namespace PhotoLiker.Core
{
    /// <summary>
    /// Photos that show the same picture, in different places. The paths are sorted and have at least two entries; <paramref name="Lengths"/> are the sizes
    /// of the files, in the same order. <paramref name="MetadataDiffers"/> says that what is written around the picture (EXIF, XMP, ...) is not the
    /// same in all of them: a program edited it in one copy (keywords, a rating, a date), which only the copy that stays keeps.
    /// </summary>
    public sealed record DuplicateGroup(IReadOnlyList<string> Paths, IReadOnlyList<long> Lengths, bool MetadataDiffers = false)
    {
        /// <summary>A group of files that all have this size.</summary>
        public DuplicateGroup(long length, IReadOnlyList<string> paths, bool metadataDiffers = false)
            : this(paths, [.. paths.Select(_ => length)], metadataDiffers)
        {
        }
    }

    /// <param name="Done">Files looked at so far.</param>
    /// <param name="Total">Files that have to be looked at: the ones that share their size (or, if asked, their name) with another.</param>
    /// <param name="BytesRead">What was read for it, to show the speed.</param>
    public readonly record struct DuplicateProgress(int Done, int Total, long BytesRead);

    /// <param name="Candidates">How many files were looked at.</param>
    /// <param name="Unreadable">How many of those could not be read (or changed while they were).</param>
    public sealed record DuplicateFindResult(IReadOnlyList<DuplicateGroup> Groups, int Candidates, int Unreadable);

    /// <param name="Picture">What the picture is: the same for two files that show the same picture, whatever is written around it.</param>
    /// <param name="Header">What is written before the picture (EXIF, XMP, thumbnail, ...); empty for a file that is not a JPEG.</param>
    public sealed record FileFingerprint(string Picture, string Header);

    /// <summary>
    /// Finds photos that show the same picture without reading every photo of the folder (over a network that is the cost that matters). Two
    /// files can only be the same if they have the same size, and the listing of the folder says that for free. Only those files are
    /// then compared, by a fingerprint of the picture data of a JPEG, the part after its header, which a program that writes a rating, a keyword or a date
    /// (and a camera's card reader that adds its own XMP) leaves alone while it changes the header, even without changing the size of the file. The
    /// fingerprint is made of three blocks of 64 KB of it (the start, the middle and the end), or of all of it if asked. A file that is not a JPEG is
    /// taken as it is. The name of a file plays no part in what is a copy: a renamed copy is a copy, and two different photos of two cameras that count to
    /// the same IMG_1234 are not. If asked, photos that share only their name with another are looked at too, since a header that grew (an XMP block that was
    /// added) makes the file bigger and so hides the copy from a comparison of sizes.
    /// </summary>
    public static class DuplicateFinder
    {
        public const int SampleSize = 64 * 1024;

        private const int Readers = 8;                      // reading is waiting for the network, not for the processor
        private const int FirstRead = 2 * SampleSize;       // one read gets the whole header of most photos and the start of their picture: over a network a read costs more than the bytes
        private const long MaxHeader = 4L * 1024 * 1024;    // a header longer than this is not looked at as a whole

        /// <summary>A stop throws <see cref="OperationCanceledException"/>.</summary>
        /// <param name="wholePictures">Compare all of the picture data: slower, but not a doubt left.</param>
        /// <param name="includeSameName">Look also at the photos that share only their name with another (copies that differ in the size of their metadata).</param>
        public static DuplicateFindResult Find(IReadOnlyList<PhotoFile> files, bool wholePictures, IProgress<DuplicateProgress>? progress, CancellationToken token, bool includeSameName = false)
        {
            var sizes = files.Where(f => f.Length > 0).GroupBy(f => f.Length).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            var names = includeSameName
                ? files.Where(f => f.Length > 0).GroupBy(f => Path.GetFileName(f.OriginalFilePath), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            var candidates = files
                .Where(f => f.Length > 0 && (sizes.Contains(f.Length) || names.Contains(Path.GetFileName(f.OriginalFilePath))))
                .ToList();

            var prints = new FileFingerprint?[candidates.Count];
            var done = 0;
            long bytes = 0;
            progress?.Report(new DuplicateProgress(0, candidates.Count, 0));

            Parallel.For(0, candidates.Count, new ParallelOptions { MaxDegreeOfParallelism = Readers, CancellationToken = token }, i =>
            {
                prints[i] = Fingerprint(candidates[i].OriginalFilePath, candidates[i].Length, wholePictures, read => Interlocked.Add(ref bytes, read));
                var count = Interlocked.Increment(ref done);
                if (count % 25 == 0 || count == candidates.Count)
                    progress?.Report(new DuplicateProgress(count, candidates.Count, Interlocked.Read(ref bytes)));
            });

            var groups = Enumerable.Range(0, candidates.Count)
                .Where(i => prints[i] is not null)
                .GroupBy(i => prints[i]!.Picture)
                .Where(g => g.Count() > 1)
                .Select(g =>
                {
                    var members = g.OrderBy(i => candidates[i].OriginalFilePath, StringComparer.OrdinalIgnoreCase).ToList();
                    var headers = members.Select(i => prints[i]!.Header).Where(h => h.Length > 0).Distinct().Count();
                    return new DuplicateGroup(
                        [.. members.Select(i => candidates[i].OriginalFilePath)],
                        [.. members.Select(i => candidates[i].Length)],
                        MetadataDiffers: headers > 1);
                })
                .OrderBy(g => g.Paths[0], StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new DuplicateFindResult(groups, candidates.Count, prints.Count(p => p is null));
        }

        /// <summary>The fingerprint of a file, or null if it can not be read, or is not the size the folder listing said (it was changed since).</summary>
        public static FileFingerprint? Fingerprint(string path, long length, bool wholePicture, Action<int>? bytesRead = null)
        {
            try
            {
                // No buffer of the stream: what is asked for is read in one go, there is nothing to read ahead
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.RandomAccess);
                if (stream.Length != length)
                    return null;

                var source = new Source(stream, length, bytesRead);
                using var picture = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var scanStart = JpegScanStart(source.Start);
                if (scanStart == NeedsMore)
                    scanStart = JpegScanStart(stream, bytesRead); // a header longer than the first read: asked for piece by piece
                if (scanStart > 0 && scanStart < length)
                {
                    using var header = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var hasHeader = scanStart <= MaxHeader;
                    if (hasHeader)
                        source.Append(header, 0, scanStart);

                    picture.AppendData("P"u8);
                    picture.AppendData(BitConverter.GetBytes(length - scanStart));
                    AppendPicture(picture, source, scanStart, length - scanStart, wholePicture);
                    return new FileFingerprint(Convert.ToHexString(picture.GetHashAndReset()), hasHeader ? Convert.ToHexString(header.GetHashAndReset()) : "big");
                }

                // Not a JPEG (or one that is not understood): the file as it is is the picture
                picture.AppendData("F"u8);
                picture.AppendData(BitConverter.GetBytes(length));
                AppendPicture(picture, source, 0, length, wholePicture);
                return new FileFingerprint(Convert.ToHexString(picture.GetHashAndReset()), string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The start of a file, read at once, and the rest of it as it is asked for.</summary>
        private sealed class Source
        {
            private readonly Stream _stream;
            private readonly Action<int>? _bytesRead;

            public Source(Stream stream, long length, Action<int>? bytesRead)
            {
                _stream = stream;
                _bytesRead = bytesRead;
                Start = new byte[(int)Math.Min(length, FirstRead)];
                stream.ReadExactly(Start);
                bytesRead?.Invoke(Start.Length);
            }

            public byte[] Start { get; }

            /// <summary>Adds the bytes from <paramref name="offset"/> on to the hash, from the first read if they are in it, else from the file.</summary>
            public void Append(IncrementalHash hash, long offset, long count)
            {
                if (offset + count <= Start.Length)
                {
                    hash.AppendData(Start, (int)offset, (int)count);
                    return;
                }

                var buffer = new byte[(int)Math.Min(count, 1 << 20)];
                _stream.Seek(offset, SeekOrigin.Begin);
                while (count > 0)
                {
                    var read = _stream.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
                    if (read <= 0)
                        throw new EndOfStreamException();
                    hash.AppendData(buffer, 0, read);
                    _bytesRead?.Invoke(read);
                    count -= read;
                }
            }
        }

        private static void AppendPicture(IncrementalHash hash, Source source, long start, long count, bool whole)
        {
            if (whole || count <= 3L * SampleSize)
            {
                source.Append(hash, start, count);
                return;
            }

            foreach (var offset in new[] { start, start + count / 2 - SampleSize / 2, start + count - SampleSize })
                source.Append(hash, offset, SampleSize);
        }

        /// <summary>What <see cref="JpegScanStart(ReadOnlySpan{byte})"/> says when the segments go on past the bytes it was given.</summary>
        private const long NeedsMore = -2;

        /// <summary>
        /// Where the picture data of a JPEG starts, looking at the start of the file only: after the header segments (EXIF, XMP, ICC, tables) and the
        /// start-of-scan header. -1 if the file is not a JPEG, or the segments are not in order; <see cref="NeedsMore"/> if the header goes on past the bytes given.
        /// </summary>
        private static long JpegScanStart(ReadOnlySpan<byte> start)
        {
            if (start.Length < 2 || start[0] != 0xFF || start[1] != 0xD8)
                return -1;

            var position = 2;
            while (true)
            {
                if (position + 4 > start.Length)
                    return NeedsMore;
                if (start[position] != 0xFF)
                    return -1;

                var marker = start[position + 1];
                if (marker == 0xFF) // a fill byte before the marker
                {
                    position++;
                    continue;
                }
                if (marker == 0xD9)
                    return -1; // the end of the image before any picture data
                if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
                {
                    position += 2; // markers without a body
                    continue;
                }

                var length = start[position + 2] << 8 | start[position + 3];
                if (length < 2)
                    return -1;
                if (marker == 0xDA)
                    return position + 2 + length;
                position += 2 + length;
            }
        }

        /// <summary>
        /// Where the picture data of a JPEG starts, reading the segments from the stream one at a time; -1 if the file
        /// is not a JPEG, or the segments are not in order.
        /// </summary>
        public static long JpegScanStart(Stream stream, Action<int>? bytesRead = null)
        {
            Span<byte> head = stackalloc byte[4];
            stream.Seek(0, SeekOrigin.Begin);
            if (stream.Read(head[..2]) != 2 || head[0] != 0xFF || head[1] != 0xD8)
                return -1;

            var position = 2L;
            while (position + 4 <= stream.Length)
            {
                stream.Seek(position, SeekOrigin.Begin);
                if (stream.Read(head) != 4)
                    return -1;
                bytesRead?.Invoke(4);

                if (head[0] != 0xFF)
                    return -1;

                var marker = head[1];
                if (marker == 0xFF) // a fill byte before the marker
                {
                    position++;
                    continue;
                }
                if (marker == 0xD9)
                    return -1; // the end of the image before any picture data
                if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
                {
                    position += 2; // markers without a body
                    continue;
                }

                var length = head[2] << 8 | head[3];
                if (length < 2)
                    return -1;
                if (marker == 0xDA)
                    return position + 2 + length;
                position += 2 + length;
            }
            return -1;
        }
    }
}
