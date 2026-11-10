using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    public sealed record DateAlignmentFailure(string Path, string Message);

    /// <summary>
    /// A photo as it is on the disk after a run: the times read back from the file system, and the EXIF dates that were written.
    /// <paramref name="PreviousModifiedUtc"/> is the date of change it had before: an index that knew the photo by that date knows it by the new one.
    /// <paramref name="PreviousLength"/> and <paramref name="Length"/> differ when a date taken had to be added to the photo (or taken away again by an undo).
    /// </summary>
    public sealed record AlignedFile(
        string Path, ExifDateFields Written, DateTime CreatedUtc, DateTime ModifiedUtc, DateTime PreviousModifiedUtc, long PreviousLength = 0, long Length = 0);

    /// <param name="Photos">The photos that were changed (or, after an undo, put back).</param>
    /// <param name="Copies">How many copies in the liked and group folders got the same times, so that they still match their photo.</param>
    /// <param name="NotKept">Photos where the drive did not keep a date that was written (some network drives keep no creation date).</param>
    /// <param name="Skipped">Only for an undo: photos left as they are because someone changed them after the run.</param>
    /// <param name="Renamed">Copies that were renamed to the id of their photo, because the photo got another size (a date taken was added to it) or got its old size back.</param>
    public sealed record DateAlignmentReport(
        IReadOnlyList<AlignedFile> Photos, int Copies, IReadOnlyList<DateAlignmentFailure> Failures, int NotKept, int Skipped, bool Cancelled, string LogFile, int Renamed = 0);

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
            int copies = 0, notKept = 0, renamed = 0, done = 0;
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
                    {
                        copies++;
                        if (result.Renamed)
                            renamed++;
                    }
                    else
                    {
                        photos.Add(new AlignedFile(
                            result.Prepared.Path, result.Prepared.WrittenFields, result.CreatedUtc, result.ModifiedUtc, result.Prepared.OldModified,
                            result.Prepared.OldLength, result.Length));
                        // What could not be done for the file is told, though what could be done was
                        if (result.Prepared.Note is { } note)
                            failures.Add(new DateAlignmentFailure(result.Prepared.Path, note));
                    }
                }

                done += chunk.Length;
                progress?.Report(done);
            }

            return new DateAlignmentReport(photos, copies, failures, notKept, 0, cancelled, logFile, renamed);
        }

        /// <summary>What a photo (or a copy of one) is like now, and what it has to become.</summary>
        private sealed record Prepared(
            string Path, bool IsCopy, DateTime OldCreated, DateTime OldModified, DateTime NewCreated, DateTime NewModified,
            List<ExifEdit> Edits, ExifDateFields WrittenFields, long OldLength = 0, long NewLength = 0, SpliceWork? Splice = null, string? RenameTo = null, string? Note = null)
        {
            public LogLine ToLog() => new(
                Path, IsCopy, OldCreated.Ticks, OldModified.Ticks, NewCreated.Ticks, NewModified.Ticks,
                Edits.Any(e => !e.Binary) ? [.. Edits.Where(e => !e.Binary).Select(e => new LogExif(e.Offset, Encoding.ASCII.GetString(e.Old), Encoding.ASCII.GetString(e.New)))] : null,
                Splice?.ToLog(), RenameTo,
                Edits.Any(e => e.Binary) ? [.. Edits.Where(e => e.Binary).Select(e => new LogPatch(e.Offset, Convert.ToBase64String(e.Old), Convert.ToBase64String(e.New)))] : null);
        }

        /// <summary>
        /// Bytes of the file that are overwritten by as many other bytes: the text of an EXIF date, or (<paramref name="Binary"/>) the bytes of a date in the header of a video,
        /// which are not text and are kept in the log in base64.
        /// </summary>
        private sealed record ExifEdit(long Offset, byte[] Old, byte[] New, bool Binary = false);

        /// <summary>
        /// The Exif block of the photo is replaced by a larger one that has the date taken (see <see cref="JpegExif"/>). <paramref name="OldBytes"/> are the
        /// bytes that go away (the old block), kept in the log so that an undo can put them back; <paramref name="Expected"/> is the size the file had.
        /// </summary>
        private sealed record SpliceWork(ExifSplice Splice, byte[] OldBytes, long Expected)
        {
            public LogSplice ToLog() => new(
                Splice.At, Splice.Insert.Length, Convert.ToBase64String(SHA256.HashData(Splice.Insert)), Convert.ToBase64String(OldBytes));
        }

        private sealed record Executed(Prepared Prepared, string? Error, DateTime CreatedUtc, DateTime ModifiedUtc, bool NotKept, long Length = 0, bool Renamed = false);

        private static Prepared Prepare(DateAlignmentEntry entry)
        {
            var info = new FileInfo(entry.Path);
            if (!info.Exists)
                throw new FileNotFoundException(SearchLibStrings.PhotoGone);

            var taken = entry.Facts.Taken!.Value;
            var takenUtc = DateAlignment.ToUtc(taken);
            var edits = new List<ExifEdit>();
            SpliceWork? splice = null;
            string? note = null;
            var written = entry.SetExif;

            if (MediaKinds.IsVideo(entry.Path))
            {
                // A video has no EXIF: "Original" stands for the date it says it was recorded, which is overwritten in place where the file keeps it (the size stays)
                written = ExifDateFields.None;
                if (entry.SetExif.HasFlag(ExifDateFields.Original))
                {
                    using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (VideoContainerDates.Plan(stream, taken) is { } patches)
                    {
                        edits.AddRange(patches.Select(p => new ExifEdit(p.Offset, p.Old, p.New, Binary: true)));
                        if (edits.Count > 0)
                            written = ExifDateFields.Original;
                    }
                    else
                    {
                        note = SearchLibStrings.VideoNoPlaceForDate; // its file dates are still set
                    }
                }
            }
            else if (entry.SetExif != ExifDateFields.None)
            {
                var text = ExifReader.FormatDate(taken);
                using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (entry.SetExif.HasFlag(ExifDateFields.Original) && entry.Facts.Exif.OriginalOffset is null)
                {
                    // The photo has no place for its date taken: the Exif block is made larger, with the date taken in it (and whatever else was asked for).
                    // What the photo says must still be what it said when it was looked at; if it is another one by now, nothing is written into it
                    if (ExifReader.Locate(stream) != entry.Facts.Exif)
                        throw new InvalidDataException(SearchLibStrings.PhotoChangedLookAgain);
                    var plan = JpegExif.Plan(stream, ChangeOf(entry.SetExif, taken));
                    if (plan.Splice is { } sp)
                    {
                        stream.Position = sp.At;
                        var old = new byte[sp.RemoveLength];
                        stream.ReadExactly(old);
                        splice = new SpliceWork(sp, old, info.Length);
                    }
                    else
                    {
                        // The block does have a place for it after all: the dates are written where they are
                        edits.AddRange(plan.Patches.Where(p => !p.Expected.AsSpan().SequenceEqual(p.New)).Select(p => new ExifEdit(p.At, p.Expected, p.New)));
                    }
                }
                else
                {
                    foreach (var (field, offset, expected) in SlotsOf(entry.Facts.Exif))
                    {
                        if (!entry.SetExif.HasFlag(field) || offset is not { } at)
                            continue;

                        var current = ReadText(stream, at);
                        // The place was found when the photo was looked at; if the photo is another one by now, nothing is written into it
                        if (ExifReader.ParseDate(current) != expected)
                            throw new InvalidDataException(SearchLibStrings.PhotoChangedLookAgain);
                        if (!current.AsSpan().SequenceEqual(text))
                            edits.Add(new ExifEdit(at, current, text));
                    }
                }
            }

            return new Prepared(
                entry.Path, IsCopy: false, info.CreationTimeUtc, info.LastWriteTimeUtc,
                entry.SetCreated ? takenUtc : info.CreationTimeUtc,
                entry.SetModified ? takenUtc : info.LastWriteTimeUtc,
                edits, written, info.Length, splice is null ? info.Length : info.Length + splice.Splice.LengthChange, splice, Note: note);
        }

        private static ExifChange ChangeOf(ExifDateFields fields, DateTime taken) => new()
        {
            Original = fields.HasFlag(ExifDateFields.Original) ? taken : null,
            Digitized = fields.HasFlag(ExifDateFields.Digitized) ? taken : null,
            LastSaved = fields.HasFlag(ExifDateFields.Modified) ? taken : null,
        };

        /// <summary>
        /// A copy only gets the times its photo gets, and the name of its photo's new id when the photo got another size (the name of a
        /// copy carries the id, which is made from the size and the date of change); null if it needs neither.
        /// </summary>
        private static Prepared? PrepareCopy(string copy, Prepared photo)
        {
            var info = new FileInfo(copy);
            if (!info.Exists)
                return null;

            var created = photo.NewCreated != photo.OldCreated ? photo.NewCreated : info.CreationTimeUtc;
            var modified = photo.NewModified != photo.OldModified ? photo.NewModified : info.LastWriteTimeUtc;

            string? renameTo = null;
            if (photo.Splice is not null)
            {
                var now = new PhotoFile(photo.Path, string.Empty) { Length = photo.NewLength, LastWriteTimeUtc = photo.NewModified };
                var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(copy) ?? string.Empty, CopyName.For(now));
                if (!string.Equals(target, copy, StringComparison.OrdinalIgnoreCase) && !File.Exists(target))
                    renameTo = target;
            }

            if (created == info.CreationTimeUtc && modified == info.LastWriteTimeUtc && renameTo is null)
                return null;

            return new Prepared(copy, IsCopy: true, info.CreationTimeUtc, info.LastWriteTimeUtc, created, modified, [], ExifDateFields.None, RenameTo: renameTo);
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
                // A file that was written anew was created now, and writing a file moves its date of change to now: the dates are set
                // also when they are meant to stay as they were. They are set through the handle that wrote the file (see FileDates), and
                // by name only if they are not what they should be.
                var written = work.Splice is not null || work.Edits.Count > 0;
                var setCreated = work.NewCreated != work.OldCreated || work.Splice is not null;
                var setModified = work.NewModified != work.OldModified || written;
                WithWritableFile(work.Path, () =>
                {
                    if (work.Splice is { } splice)
                    {
                        WriteSplice(work.Path, splice, work.NewCreated, work.NewModified);
                    }
                    else if (work.Edits.Count > 0)
                    {
                        using var stream = new FileStream(work.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                        foreach (var edit in work.Edits)
                        {
                            stream.Seek(edit.Offset, SeekOrigin.Begin);
                            stream.Write(edit.New);
                        }
                        stream.Flush(flushToDisk: true);
                        FileDates.SetOnHandle(stream, work.NewCreated, work.NewModified);
                    }

                    if (setCreated && !DateAlignment.SameMoment(new FileInfo(work.Path).CreationTimeUtc, work.NewCreated))
                        File.SetCreationTimeUtc(work.Path, work.NewCreated);
                    if (setModified && !DateAlignment.SameMoment(File.GetLastWriteTimeUtc(work.Path), work.NewModified))
                        File.SetLastWriteTimeUtc(work.Path, work.NewModified);
                });

                // A copy takes the name of the new id of its photo (a copy that is called so already, or a name that is taken, stays)
                var renamed = false;
                var finalPath = work.Path;
                if (work.RenameTo is { } target && !File.Exists(target))
                {
                    File.Move(work.Path, target);
                    finalPath = target;
                    renamed = true;
                }

                // Read back: a network drive may say yes and keep nothing
                var after = new FileInfo(finalPath);
                var notKept =
                    ((work.NewCreated != work.OldCreated || work.Splice is not null) && !DateAlignment.SameMoment(after.CreationTimeUtc, work.NewCreated)) ||
                    ((work.NewModified != work.OldModified || work.Splice is not null) && !DateAlignment.SameMoment(after.LastWriteTimeUtc, work.NewModified));
                return new Executed(work, null, after.CreationTimeUtc, after.LastWriteTimeUtc, notKept, after.Length, renamed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return new Executed(work, ex.Message, default, default, false);
            }
        }

        /// <summary>Replaces the Exif block of the photo by the larger one: a file with it is written next to the photo and takes its place.</summary>
        private static void WriteSplice(string path, SpliceWork work, DateTime created, DateTime written)
        {
            var original = FileReading.ReadAllBytesShared(path);
            var at = checked((int)work.Splice.At);
            if (original.Length != work.Expected || at + work.Splice.RemoveLength > original.Length ||
                !original.AsSpan(at, work.Splice.RemoveLength).SequenceEqual(work.OldBytes))
                throw new InvalidDataException(SearchLibStrings.PhotoChangedLookAgain);

            ReplaceFile(path, work.Splice.Apply(original), created, written);
        }

        /// <summary>Writes the bytes next to the file, with the dates that the file is to have, and lets them take its place.</summary>
        private static void ReplaceFile(string path, byte[] bytes, DateTime created, DateTime written)
        {
            var temp = path + ".saving";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
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
                    throw new InvalidDataException(SearchLibStrings.NotDateLog);

                while (lines.MoveNext())
                {
                    if (lines.Current.Length > 0 && JsonSerializer.Deserialize<LogLine>(lines.Current) is { } item)
                        items.Add(item);
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(SearchLibStrings.NotDateLog, ex);
            }

            var photos = new ConcurrentBag<AlignedFile>();
            var failures = new ConcurrentBag<DateAlignmentFailure>();
            int copies = 0, skipped = 0, renamed = 0, done = 0;
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
                        if (result.Renamed)
                            Interlocked.Increment(ref renamed);
                        if (result.After is { } after)
                        {
                            if (item.Copy)
                                Interlocked.Increment(ref copies);
                            else
                                photos.Add(new AlignedFile(
                                    item.Path, result.Patched ? ExifDateFields.Original : ExifDateFields.None, after.CreationTimeUtc, after.LastWriteTimeUtc, result.Before, result.BeforeLength, after.Length));
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

            return new DateAlignmentReport([.. photos], copies, [.. failures], 0, skipped, cancelled, logFile, renamed);
        }

        /// <param name="Before">The date of change the file had before it was put back.</param>
        private sealed record Restored(bool Skipped, FileInfo? After, DateTime Before, long BeforeLength = 0, bool Renamed = false, bool Patched = false);

        private static Restored Restore(LogLine item)
        {
            // A copy that was renamed to the id of its photo is where the run put it, unless it was moved since
            var path = item.RenamedTo is not null && !File.Exists(item.Path) && File.Exists(item.RenamedTo) ? item.RenamedTo : item.Path;

            var info = new FileInfo(path);
            if (!info.Exists)
                return new Restored(true, null, default);

            var createdNow = info.CreationTimeUtc;
            var modifiedNow = info.LastWriteTimeUtc;
            var lengthNow = info.Length;
            var createdChanged = item.Created != item.NewCreated;
            var modifiedChanged = item.Modified != item.NewModified;
            var createdOurs = createdChanged && DateAlignment.SameMoment(createdNow, new DateTime(item.NewCreated, DateTimeKind.Utc));
            var modifiedOurs = modifiedChanged && DateAlignment.SameMoment(modifiedNow, new DateTime(item.NewModified, DateTimeKind.Utc));
            var skipped = (createdChanged && !createdOurs) || (modifiedChanged && !modifiedOurs);
            var wrote = false;
            var spliceUndone = false;
            var patched = false;

            // A date is only put back if it is the run's; a file that is written meanwhile keeps the one it has otherwise (writing moves it to now)
            var created = createdOurs ? new DateTime(item.Created, DateTimeKind.Utc) : createdNow;
            var modified = modifiedOurs ? new DateTime(item.Modified, DateTimeKind.Utc) : modifiedNow;

            WithWritableFile(path, () =>
            {
                if (item.Splice is { } splice)
                {
                    // The block the run wrote is taken out again and the old one put back, if the file still has the block the run wrote
                    if (UndoSplice(path, splice, created, modified))
                        wrote = spliceUndone = true;
                    else
                        skipped = true;
                }

                if (item.Exif is { Count: > 0 } edits)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
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
                    if (wrote)
                        FileDates.SetOnHandle(stream, created, modified);
                }

                if (item.Patches is { Count: > 0 } patches)
                {
                    // The dates the run wrote into a video go back, where the video still has what the run wrote
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    foreach (var patch in patches)
                    {
                        var written = Convert.FromBase64String(patch.New);
                        var current = new byte[written.Length];
                        stream.Seek(patch.Offset, SeekOrigin.Begin);
                        if (stream.Read(current, 0, current.Length) != current.Length || !current.AsSpan().SequenceEqual(written))
                        {
                            skipped = true; // not the date the run wrote
                            continue;
                        }
                        stream.Seek(patch.Offset, SeekOrigin.Begin);
                        stream.Write(Convert.FromBase64String(patch.Old));
                        wrote = patched = true;
                    }
                    stream.Flush(flushToDisk: true);
                    if (patched)
                        FileDates.SetOnHandle(stream, created, modified);
                }

                // By name, for what was not written (a date that is the run's and nothing else) and for a drive that did not take the dates through the handle
                if ((createdOurs || spliceUndone) && !DateAlignment.SameMoment(new FileInfo(path).CreationTimeUtc, created))
                    File.SetCreationTimeUtc(path, created); // after a file was written anew it was created now, and that was not the run's to put back
                if ((modifiedOurs || wrote) && !DateAlignment.SameMoment(File.GetLastWriteTimeUtc(path), modified))
                    File.SetLastWriteTimeUtc(path, modified); // writing moved it to now; it was not the run's to put back
            });

            var renamed = false;
            if (!string.Equals(path, item.Path, StringComparison.Ordinal) && !File.Exists(item.Path))
            {
                File.Move(path, item.Path);
                path = item.Path;
                renamed = true;
            }

            return new Restored(skipped, createdOurs || modifiedOurs || wrote ? new FileInfo(path) : null, modifiedNow, lengthNow, renamed, patched);
        }

        private static bool UndoSplice(string path, LogSplice splice, DateTime created, DateTime modified)
        {
            var current = FileReading.ReadAllBytesShared(path);
            if (splice.At < 0 || splice.At + splice.NewLength > current.Length)
                return false;

            var written = current.AsSpan((int)splice.At, splice.NewLength);
            if (Convert.ToBase64String(SHA256.HashData(written)) != splice.NewSha)
                return false; // the block is not the one the run wrote: the photo was changed since

            ReplaceFile(path, new ExifSplice(splice.At, splice.NewLength, Convert.FromBase64String(splice.OldBytes)).Apply(current), created, modified);
            return true;
        }

        // ── the log ────────────────────────────────────────────────────────

        private sealed record LogHeader(string Kind, int Version, string Root, string At);

        private sealed record LogExif(long Offset, string Old, string New);

        /// <summary>The Exif block the run wrote: where, how long, a hash of it, and the bytes it replaced (base64).</summary>
        private sealed record LogSplice(long At, int NewLength, string NewSha, string OldBytes);

        /// <summary>One file: its times (as ticks of UTC) before and after the run, the EXIF dates that were overwritten, the Exif block that was replaced, and the name a copy was given.</summary>
        private sealed record LogLine(
            string Path, bool Copy, long Created, long Modified, long NewCreated, long NewModified, List<LogExif>? Exif, LogSplice? Splice = null, string? RenamedTo = null,
            List<LogPatch>? Patches = null);

        /// <summary>Bytes of a video that the run overwrote: where, what was there (base64) and what the run wrote (base64).</summary>
        private sealed record LogPatch(long Offset, string Old, string New);
    }
}
