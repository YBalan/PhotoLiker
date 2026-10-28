using System.Text;
using System.Text.Json;

namespace PhotoLiker.Core
{
    public sealed record DuplicateMove(string From, string To);

    public sealed record DuplicateFailure(string Path, string Message);

    /// <param name="Moved">The copies that were moved (or, after an undo, put back).</param>
    /// <param name="Bytes">Their size together.</param>
    /// <param name="Skipped">Only for an undo: copies left where they are because something else is in their old place, or they are gone.</param>
    public sealed record DuplicateMoveReport(IReadOnlyList<DuplicateMove> Moved, long Bytes, IReadOnlyList<DuplicateFailure> Failures, int Skipped, bool Cancelled, string LogFile);

    /// <summary>
    /// Moves the copies of photos that stay into <c>Deleted\Duplicated</c> of the photo folder, with the path they had, so nothing is lost
    /// and a copy goes back by moving it up again. The moves are logged before they are made, so a run that is cut short can be taken back too.
    /// A copy is only moved if the photo that stays is still there with the same size, and the copy itself is still what it was.
    /// </summary>
    public static class DuplicateMover
    {
        public const string LogPrefix = "photo-liker-duplicates-";
        private const string LogKind = "photo-liker-duplicates";
        private const string UndoneSuffix = ".undone.jsonl";
        private const int ChunkSize = 100;

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

        /// <summary>
        /// Moves what <paramref name="plans"/> says (only plans with a copy that stays). A stop comes between two chunks of
        /// <see cref="ChunkSize"/> moves; what is moved stays moved and stays in the log.
        /// </summary>
        /// <param name="progress">The number of copies handled so far.</param>
        public static DuplicateMoveReport Move(IReadOnlyList<DuplicatePlan> plans, string root, string logFile, IProgress<int>? progress, CancellationToken token)
        {
            var moved = new List<DuplicateMove>();
            var failures = new List<DuplicateFailure>();
            long bytes = 0;
            var done = 0;
            var cancelled = false;

            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            using var logStream = new FileStream(logFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var log = new StreamWriter(logStream, new UTF8Encoding(false));
            log.WriteLine(JsonSerializer.Serialize(new LogHeader(LogKind, 1, root, DateTime.UtcNow.ToString("O"))));

            foreach (var chunk in plans.Where(p => p.Keeper is not null).SelectMany(p => p.ToMove.Select(c => (Plan: p, Copy: c))).Chunk(ChunkSize))
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                // Where each copy will go, written down before the first of them is moved
                var work = new List<(DuplicateMove Move, long Length)>();
                foreach (var (plan, copy) in chunk)
                {
                    var keeper = plan.Copies[plan.Keeper!.Value].Path;
                    try
                    {
                        Check(keeper, plan.Copies[plan.Keeper!.Value].Length, copy.Path, copy.Length);
                        work.Add((new DuplicateMove(copy.Path, DeletedPhotos.DestinationOf(root, copy.Path, DeletedPhotos.DuplicatesFolderName)), copy.Length));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(new DuplicateFailure(copy.Path, ex.Message));
                    }
                }

                foreach (var (move, _) in work)
                    log.WriteLine(JsonSerializer.Serialize(new LogLine(move.From, move.To)));
                log.Flush();
                logStream.Flush(flushToDisk: true);

                foreach (var (move, length) in work)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(move.To)!);
                        File.Move(move.From, move.To);
                        moved.Add(move);
                        bytes += length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(new DuplicateFailure(move.From, ex.Message));
                    }
                }

                done += chunk.Length;
                progress?.Report(done);
            }

            return new DuplicateMoveReport(moved, bytes, failures, 0, cancelled, logFile);
        }

        // The photo that stays is there and as big as the group says, and so is the copy: nothing else is moved on the strength of an old listing
        private static void Check(string keeper, long keeperLength, string copy, long copyLength)
        {
            if (!File.Exists(keeper) || new FileInfo(keeper).Length != keeperLength)
                throw new IOException("The photo that stays is gone or has changed.");
            if (!File.Exists(copy) || new FileInfo(copy).Length != copyLength)
                throw new IOException("The copy is gone or has changed.");
        }

        /// <summary>
        /// Puts the copies of a log back where they were. A copy is left where it is if its old place is taken (by a photo, or by the same
        /// name) or it is not there any more; that is counted. A log that was undone completely is renamed, so that <see cref="LatestLog"/>
        /// finds the one before it. A stop comes between two copies.
        /// </summary>
        public static DuplicateMoveReport Undo(string logFile, string root, IProgress<int>? progress, CancellationToken token)
        {
            var lines = new List<LogLine>();
            try
            {
                using var reader = File.ReadLines(logFile).GetEnumerator();
                if (!reader.MoveNext() || JsonSerializer.Deserialize<LogHeader>(reader.Current) is not { Kind: LogKind })
                    throw new InvalidDataException("This is not a PhotoLiker log of moved duplicates.");

                while (reader.MoveNext())
                {
                    if (reader.Current.Length > 0 && JsonSerializer.Deserialize<LogLine>(reader.Current) is { } line)
                        lines.Add(line);
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("This is not a PhotoLiker log of moved duplicates.", ex);
            }

            var restored = new List<DuplicateMove>();
            var failures = new List<DuplicateFailure>();
            long bytes = 0;
            var skipped = 0;
            var done = 0;
            var cancelled = false;

            foreach (var line in lines)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                try
                {
                    if (!File.Exists(line.To) || File.Exists(line.From))
                    {
                        skipped++; // not there to put back, or something is in its place
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(line.From)!);
                        bytes += new FileInfo(line.To).Length;
                        File.Move(line.To, line.From);
                        restored.Add(new DuplicateMove(line.To, line.From));
                        RemoveEmptyParents(Path.GetDirectoryName(line.To)!, DeletedPhotos.DuplicatesFolderOf(root));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new DuplicateFailure(line.To, ex.Message));
                }

                if (++done % 50 == 0)
                    progress?.Report(done);
            }
            progress?.Report(done);

            if (!cancelled && failures.Count == 0)
                File.Move(logFile, logFile[..^".jsonl".Length] + UndoneSuffix, overwrite: true);

            return new DuplicateMoveReport(restored, bytes, failures, skipped, cancelled, logFile);
        }

        // The folders a copy leaves empty behind it: not the Duplicated folder itself, and never one that still holds something (no recursive delete)
        private static void RemoveEmptyParents(string folder, string stopAt)
        {
            try
            {
                while (folder.Length > stopAt.Length && folder.StartsWith(stopAt, StringComparison.OrdinalIgnoreCase)
                       && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                    folder = Path.GetDirectoryName(folder)!;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a leftover empty folder is not worth a failure
            }
        }

        private sealed record LogHeader(string Kind, int Version, string Root, string At);

        private sealed record LogLine(string From, string To);
    }
}
