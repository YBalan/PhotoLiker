using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PhotoLiker.Search
{
    public sealed record DateAlignmentFailure(string Path, string Message);

    /// <summary>
    /// A photo as it is on the disk after a run: the times read back from the file system, and the EXIF dates that were written.
    /// <paramref name="PreviousModifiedUtc"/> is the date of change it had before: an index that knew the photo by that date knows it by the new one.
    /// </summary>
    public sealed record AlignedFile(string Path, ExifDateFields Written, DateTime CreatedUtc, DateTime ModifiedUtc, DateTime PreviousModifiedUtc);

    /// <param name="Photos">The photos that were changed (or, after an undo, put back).</param>
    /// <param name="Copies">How many copies in the liked and group folders got the same times, so that they still match their photo.</param>
    /// <param name="NotKept">Photos where the drive did not keep a date that was written (some network drives keep no creation date).</param>
    /// <param name="Skipped">Only for an undo: photos left as they are because someone changed them after the run.</param>
    public sealed record DateAlignmentReport(
        IReadOnlyList<AlignedFile> Photos, int Copies, IReadOnlyList<DateAlignmentFailure> Failures, int NotKept, int Skipped, bool Cancelled, string LogFile);

    /// <summary>
    /// Writes the dates that <see cref="DateAlignment.Plan"/> decided on, and takes them back. Every run writes a log with what each file
    /// was like before; the log is written ahead of the changes, so that a run that is cut short (a crash, a pulled cable) can be undone too.
    /// </summary>
    public static class DateAligner
    {
        public const string LogPrefix = "photo-liker-datefix-";
        private const string LogKind = "photo-liker-datefix";
        private const string UndoneSuffix = ".undone.jsonl";

        private const int ChunkSize = 100;  // files whose old state is logged before the first of them is changed
        private const int Writers = 8;

        /// <summary>The log of a run at this moment, kept in the photo folder like the other files of PhotoLiker.</summary>
        public static string NewLogFile(string folder, DateTime now) => Path.Combine(folder, $"{LogPrefix}{now:yyyyMMdd-HHmmss}.jsonl");

        /// <summary>The newest log in the folder that has not been undone, or null.</summary>
        public static string? LatestLog(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            try
            {
                return Directory.EnumerateFiles(folder, LogPrefix + "*.jsonl", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.EndsWith(UndoneSuffix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        // ── doing it ───────────────────────────────────────────────────────

        /// <summary>
        /// Changes the photos in <paramref name="entries"/> (those with something to change). Stops between two chunks of
        /// <see cref="ChunkSize"/> files when cancelled; what is done stays done and stays in the log.
        /// </summary>
        /// <param name="copiesOf">The copies of a photo (in the liked and group folders): they get the same times, since a copy is matched with its photo by size and time.</param>
        public static DateAlignmentReport Apply(
            IReadOnlyList<DateAlignmentEntry> entries, string root, string logFile,
            Func<string, IReadOnlyList<string>>? copiesOf, IProgress<int>? progress, CancellationToken token)
        {
            var photos = new List<AlignedFile>();
            var failures = new List<DateAlignmentFailure>();
            int copies = 0, notKept = 0, done = 0;
            var cancelled = false;

            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            using var logStream = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var log = new StreamWriter(logStream, new UTF8Encoding(false));
            log.WriteLine(JsonSerializer.Serialize(new LogHeader(LogKind, 1, root, DateTime.UtcNow.ToString("O"))));

            foreach (var chunk in entries.Chunk(ChunkSize))
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var work = new ConcurrentBag<Prepared>();
                var refused = new ConcurrentBag<DateAlignmentFailure>();
                Parallel.ForEach(chunk, new ParallelOptions { MaxDegreeOfParallelism = Writers }, entry =>
                {
                    try
                    {
                        var photo = Prepare(entry);
                        work.Add(photo);
                        foreach (var copy in copiesOf?.Invoke(entry.Path) ?? [])
                        {
                            if (PrepareCopy(copy, photo) is { } prepared)
                                work.Add(prepared);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        refused.Add(new DateAlignmentFailure(entry.Path, ex.Message));
                    }
                });

                // The old state goes to the disk before the first file of the chunk is touched
                foreach (var prepared in work)
                    log.WriteLine(JsonSerializer.Serialize(prepared.ToLog()));
                log.Flush();
                logStream.Flush(flushToDisk: true);

                var results = new ConcurrentBag<Executed>();
                Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Writers }, prepared => results.Add(Execute(prepared)));

                failures.AddRange(refused);
                foreach (var result in results)
                {
                    if (result.Error is not null)
                    {
                        failures.Add(new DateAlignmentFailure(result.Prepared.Path, result.Error));
                        continue;
                    }
                    if (result.NotKept)
                        notKept++;
                    if (result.Prepared.IsCopy)
                        copies++;
                    else
                        photos.Add(new AlignedFile(
                            result.Prepared.Path, result.Prepared.WrittenFields, result.CreatedUtc, result.ModifiedUtc, result.Prepared.OldModified));
                }

                done += chunk.Length;
                progress?.Report(done);
            }

            return new DateAlignmentReport(photos, copies, failures, notKept, 0, cancelled, logFile);
        }

        /// <summary>What a photo (or a copy of one) is like now, and what it has to become.</summary>
        private sealed record Prepared(
            string Path, bool IsCopy, DateTime OldCreated, DateTime OldModified, DateTime NewCreated, DateTime NewModified,
            List<ExifEdit> Edits, ExifDateFields WrittenFields)
        {
            public LogLine ToLog() => new(
                Path, IsCopy, OldCreated.Ticks, OldModified.Ticks, NewCreated.Ticks, NewModified.Ticks,
                Edits.Count > 0 ? [.. Edits.Select(e => new LogExif(e.Offset, Encoding.ASCII.GetString(e.Old), Encoding.ASCII.GetString(e.New)))] : null);
        }

        private sealed record ExifEdit(long Offset, byte[] Old, byte[] New);

        private sealed record Executed(Prepared Prepared, string? Error, DateTime CreatedUtc, DateTime ModifiedUtc, bool NotKept);

        private static Prepared Prepare(DateAlignmentEntry entry)
        {
            var info = new FileInfo(entry.Path);
            if (!info.Exists)
                throw new FileNotFoundException("The photo is gone.");

            var taken = entry.Facts.Taken!.Value;
            var takenUtc = DateAlignment.ToUtc(taken);
            var edits = new List<ExifEdit>();

            if (entry.SetExif != ExifDateFields.None)
            {
                var text = ExifReader.FormatDate(taken);
                using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                foreach (var (field, offset, expected) in SlotsOf(entry.Facts.Exif))
                {
                    if (!entry.SetExif.HasFlag(field) || offset is not { } at)
                        continue;

                    var current = ReadText(stream, at);
                    // The place was found when the photo was looked at; if the photo is another one by now, nothing is written into it
                    if (ExifReader.ParseDate(current) != expected)
                        throw new InvalidDataException("The photo changed after it was looked at; look again.");
                    if (!current.AsSpan().SequenceEqual(text))
                        edits.Add(new ExifEdit(at, current, text));
                }
            }

            return new Prepared(
                entry.Path, IsCopy: false, info.CreationTimeUtc, info.LastWriteTimeUtc,
                entry.SetCreated ? takenUtc : info.CreationTimeUtc,
                entry.SetModified ? takenUtc : info.LastWriteTimeUtc,
                edits, entry.SetExif);
        }

        /// <summary>A copy only gets the times its photo gets; null if it is not there or has them already.</summary>
        private static Prepared? PrepareCopy(string copy, Prepared photo)
        {
            var info = new FileInfo(copy);
            if (!info.Exists)
                return null;

            var created = photo.NewCreated != photo.OldCreated ? photo.NewCreated : info.CreationTimeUtc;
            var modified = photo.NewModified != photo.OldModified ? photo.NewModified : info.LastWriteTimeUtc;
            if (created == info.CreationTimeUtc && modified == info.LastWriteTimeUtc)
                return null;

            return new Prepared(copy, IsCopy: true, info.CreationTimeUtc, info.LastWriteTimeUtc, created, modified, [], ExifDateFields.None);
        }

        private static IEnumerable<(ExifDateFields Field, long? Offset, DateTime? Value)> SlotsOf(ExifDateSlots slots)
        {
            yield return (ExifDateFields.Original, slots.OriginalOffset, slots.Dates.Original);
            yield return (ExifDateFields.Digitized, slots.DigitizedOffset, slots.Dates.Digitized);
            yield return (ExifDateFields.Modified, slots.ModifiedOffset, slots.Dates.Modified);
        }

        private static byte[] ReadText(Stream stream, long offset)
        {
            var text = new byte[ExifReader.DateTextLength];
            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadExactly(text);
            return text;
        }

        private static Executed Execute(Prepared work)
        {
            try
            {
                WithWritableFile(work.Path, () =>
                {
                    if (work.Edits.Count > 0)
                    {
                        using var stream = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                        foreach (var edit in work.Edits)
                        {
                            stream.Seek(edit.Offset, SeekOrigin.Begin);
                            stream.Write(edit.New);
                        }
                        stream.Flush(flushToDisk: true);
                    }

                    if (work.NewCreated != work.OldCreated)
                        File.SetCreationTimeUtc(work.Path, work.NewCreated);
                    // Writing the file moved its date of change to now, so it is set also when it is meant to stay as it was
                    if (work.NewModified != work.OldModified || work.Edits.Count > 0)
                        File.SetLastWriteTimeUtc(work.Path, work.NewModified);
                });

                // Read back: a network drive may say yes and keep nothing
                var after = new FileInfo(work.Path);
                var notKept =
                    (work.NewCreated != work.OldCreated && !DateAlignment.SameMoment(after.CreationTimeUtc, work.NewCreated)) ||
                    (work.NewModified != work.OldModified && !DateAlignment.SameMoment(after.LastWriteTimeUtc, work.NewModified));
                return new Executed(work, null, after.CreationTimeUtc, after.LastWriteTimeUtc, notKept);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new Executed(work, ex.Message, default, default, false);
            }
        }

        /// <summary>Runs the action with the read-only mark of the file taken off, and puts it back.</summary>
        private static void WithWritableFile(string path, Action action)
        {
            var attributes = File.GetAttributes(path);
            var readOnly = attributes.HasFlag(FileAttributes.ReadOnly);
            if (readOnly)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            try
            {
                action();
            }
            finally
            {
                if (readOnly)
                    File.SetAttributes(path, attributes);
            }
        }

        // ── taking it back ─────────────────────────────────────────────────

        /// <summary>
        /// Puts the files of a log back as they were. A date is only put back if it still is what the run made it: a photo that was
        /// edited or touched since is left alone and counted as skipped. A log that was undone completely is renamed, so that
        /// <see cref="LatestLog"/> finds the one before it.
        /// </summary>
        public static DateAlignmentReport Undo(string logFile, IProgress<int>? progress, CancellationToken token)
        {
            var items = new List<LogLine>();
            try
            {
                using var lines = File.ReadLines(logFile).GetEnumerator();
                if (!lines.MoveNext() || JsonSerializer.Deserialize<LogHeader>(lines.Current) is not { Kind: LogKind })
                    throw new InvalidDataException("This is not a PhotoLiker date log.");

                while (lines.MoveNext())
                {
                    if (lines.Current.Length > 0 && JsonSerializer.Deserialize<LogLine>(lines.Current) is { } item)
                        items.Add(item);
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("This is not a PhotoLiker date log.", ex);
            }

            var photos = new ConcurrentBag<AlignedFile>();
            var failures = new ConcurrentBag<DateAlignmentFailure>();
            int copies = 0, skipped = 0, done = 0;
            var cancelled = false;

            try
            {
                Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = Writers, CancellationToken = token }, item =>
                {
                    try
                    {
                        var result = Restore(item);
                        if (result.Skipped)
                            Interlocked.Increment(ref skipped);
                        if (result.After is { } after)
                        {
                            if (item.Copy)
                                Interlocked.Increment(ref copies);
                            else
                                photos.Add(new AlignedFile(item.Path, ExifDateFields.None, after.CreationTimeUtc, after.LastWriteTimeUtc, result.Before));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(new DateAlignmentFailure(item.Path, ex.Message));
                    }

                    var count = Interlocked.Increment(ref done);
                    if (count % 50 == 0 || count == items.Count)
                        progress?.Report(count);
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            if (!cancelled && failures.IsEmpty)
                File.Move(logFile, logFile[..^".jsonl".Length] + UndoneSuffix, overwrite: true);

            return new DateAlignmentReport([.. photos], copies, [.. failures], 0, skipped, cancelled, logFile);
        }

        /// <param name="Before">The date of change the file had before it was put back.</param>
        private sealed record Restored(bool Skipped, FileInfo? After, DateTime Before);

        private static Restored Restore(LogLine item)
        {
            var info = new FileInfo(item.Path);
            if (!info.Exists)
                return new Restored(true, null, default);

            var createdNow = info.CreationTimeUtc;
            var modifiedNow = info.LastWriteTimeUtc;
            var createdChanged = item.Created != item.NewCreated;
            var modifiedChanged = item.Modified != item.NewModified;
            var createdOurs = createdChanged && DateAlignment.SameMoment(createdNow, new DateTime(item.NewCreated, DateTimeKind.Utc));
            var modifiedOurs = modifiedChanged && DateAlignment.SameMoment(modifiedNow, new DateTime(item.NewModified, DateTimeKind.Utc));
            var skipped = (createdChanged && !createdOurs) || (modifiedChanged && !modifiedOurs);
            var wrote = false;

            WithWritableFile(item.Path, () =>
            {
                if (item.Exif is { Count: > 0 } edits)
                {
                    using var stream = new FileStream(item.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    foreach (var edit in edits)
                    {
                        if (Encoding.ASCII.GetString(ReadText(stream, edit.Offset)) != edit.New)
                        {
                            skipped = true; // not the date the run wrote
                            continue;
                        }
                        stream.Seek(edit.Offset, SeekOrigin.Begin);
                        stream.Write(Encoding.ASCII.GetBytes(edit.Old));
                        wrote = true;
                    }
                    stream.Flush(flushToDisk: true);
                }

                if (createdOurs)
                    File.SetCreationTimeUtc(item.Path, new DateTime(item.Created, DateTimeKind.Utc));
                if (modifiedOurs)
                    File.SetLastWriteTimeUtc(item.Path, new DateTime(item.Modified, DateTimeKind.Utc));
                else if (wrote)
                    File.SetLastWriteTimeUtc(item.Path, modifiedNow); // writing moved it to now; it was not the run's to put back
            });

            return new Restored(skipped, createdOurs || modifiedOurs || wrote ? new FileInfo(item.Path) : null, modifiedNow);
        }

        // ── the log ────────────────────────────────────────────────────────

        private sealed record LogHeader(string Kind, int Version, string Root, string At);

        private sealed record LogExif(long Offset, string Old, string New);

        /// <summary>One file: its times (as ticks of UTC) before and after the run, and the EXIF dates that were overwritten.</summary>
        private sealed record LogLine(string Path, bool Copy, long Created, long Modified, long NewCreated, long NewModified, List<LogExif>? Exif);
    }
}
