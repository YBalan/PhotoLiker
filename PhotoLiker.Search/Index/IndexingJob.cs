using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>
    /// Brings the index of a folder up to date: finds the photos that are new or changed, reads, decodes and analyzes them
    /// and stores the result. Stopping it loses nothing that is finished, running it again continues where it was.
    /// </summary>
    /// <remarks>
    /// The work is a pipeline so that the disk, the decoder and the models are all busy at the same time:
    /// one reader (in path order, a disk likes that), a few decoders, a CLIP stage and a face stage that run side by side
    /// on the decoded photos, and a writer that saves finished photos in batches.
    /// </remarks>
    public static class IndexingJob
    {
        /// <summary>Raised when the models change in a way that makes old vectors useless: everything is then done again.</summary>
        public const int ClipVersion = 1;
        public const int FaceVersion = 1;

        public const int MaxFacesPerPhoto = 30;

        /// <summary>Shorter faces (pixels of the decoded photo) are too small to be recognized.</summary>
        public const float MinFaceSize = 28f;

        private const int ClipBatchSize = 16;
        private const int SaveBatchSize = 50;
        private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(250);

        /// <param name="excludedFolder">A folder to leave out, e.g. the liked folder with its copies.</param>
        public static async Task<IndexSummary> RunAsync(
            PhotoIndex index,
            ModelHub models,
            string? excludedFolder,
            IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();

            progress?.Report(new IndexProgress(IndexPhase.Scanning, "Looking for photos...", 0, 0, 0, 0, 0, null));
            var found = await Task.Run(() => PhotoScanner.Scan(index.Root, recursive: true, extensions, excludedFolder), cancellationToken);

            var removed = RemoveVanished(index, found);
            var (work, dateWork) = ListWork(index, found);

            // Photos that are indexed but have no date yet (an index made before dates were kept): quick, needs no models
            var datesRead = 0;
            if (dateWork.Count > 0)
                datesRead = await Task.Run(() => ReadDates(index, dateWork, progress, cancellationToken), CancellationToken.None);

            if (work.Count == 0 || cancellationToken.IsCancellationRequested)
                return new IndexSummary(0, 0, 0, removed, clock.Elapsed, cancellationToken.IsCancellationRequested, DatesRead: datesRead);

            if (!models.IsDownloaded)
                await models.EnsureDownloadedAsync(new DownloadReporter(progress), cancellationToken);

            progress?.Report(new IndexProgress(IndexPhase.LoadingModels, "Loading the models...", 0, work.Count, 0, 0, 0, null));
            await Task.Run(() =>
            {
                _ = models.ImageEncoder;
                _ = models.Detector;
                _ = models.Embedder;
            }, cancellationToken);

            var pipeline = new Pipeline(index, models, work, progress);
            await Task.Run(() => pipeline.Run(cancellationToken), CancellationToken.None);

            return new IndexSummary(pipeline.Done - pipeline.Failed, pipeline.Failed, pipeline.Faces, removed, clock.Elapsed,
                cancellationToken.IsCancellationRequested, pipeline.Times, datesRead);
        }

        /// <summary>Forgets the photos that are gone from the disk. Not when nothing was found: the folder may just be unreachable.</summary>
        private static int RemoveVanished(PhotoIndex index, IReadOnlyCollection<PhotoFile> found)
        {
            if (found.Count == 0)
                return 0;

            var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in found)
            {
                if (index.TryGetRelativePath(file.OriginalFilePath, out var relative))
                    onDisk.Add(relative);
            }

            var vanished = index.Snapshot().Where(p => !onDisk.Contains(p.RelativePath)).Select(p => p.RelativePath).ToList();
            index.Remove(vanished);
            return vanished.Count;
        }

        /// <summary>A photo that is indexed and unchanged, and only lacks its date.</summary>
        private sealed record DateWork(PhotoFile File, IndexedPhoto Photo);

        /// <summary>
        /// The photos that are not indexed yet, changed since, or were indexed by older models, in path order; and apart from
        /// them the photos that only need their date read (which does not take the models, so it is a separate, quick job).
        /// </summary>
        private static (List<WorkItem> Work, List<DateWork> DateWork) ListWork(PhotoIndex index, IEnumerable<PhotoFile> found)
        {
            var work = new List<WorkItem>();
            var dateWork = new List<DateWork>();
            foreach (var file in found.OrderBy(f => f.OriginalFilePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!index.TryGetRelativePath(file.OriginalFilePath, out var relative))
                    continue;

                var existing = index.Find(relative);
                var unchanged = existing is not null && existing.Length == file.Length && existing.ModifiedTicks == file.LastWriteTimeUtc.Ticks;

                var needClip = !unchanged || existing!.ClipVersion < ClipVersion;
                var needFaces = !unchanged || existing!.FaceVersion < FaceVersion;
                if (needClip || needFaces)
                    work.Add(new WorkItem(file, relative, unchanged ? existing : null, needClip, needFaces)); // its date is read on the way
                else if (existing!.Date.Version < PhotoDates.Version)
                    dateWork.Add(new DateWork(file, existing));
            }
            return (work, dateWork);
        }

        private const int DateReaders = 8;   // reading a header is waiting for the network, not for the processor (a router tops out at about 30 photos a second, more readers do not help)
        private const int DateChunkSize = 200;

        /// <summary>
        /// Reads the date of photos that are in the index without one: only the start of each file, no decoding, so it takes
        /// minutes for a library where indexing takes hours. Returns how many were done; a stop keeps what is finished.
        /// </summary>
        private static int ReadDates(PhotoIndex index, IReadOnlyList<DateWork> work, IProgress<IndexProgress>? progress, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            var now = DateTime.Now;
            var done = 0;
            progress?.Report(new IndexProgress(IndexPhase.ReadingDates, string.Empty, 0, work.Count, 0, 0, 0, null));

            foreach (var chunk in work.Chunk(DateChunkSize))
            {
                if (token.IsCancellationRequested)
                    break;

                var dates = new (IndexedPhoto Photo, PhotoDate Date)?[chunk.Length];
                Parallel.For(0, chunk.Length, new ParallelOptions { MaxDegreeOfParallelism = DateReaders }, i =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (ReadDate(chunk[i].File, now) is { } date)
                        dates[i] = (chunk[i].Photo, date);
                });

                var ready = dates.Where(d => d is not null).Select(d => d!.Value).ToList();
                index.SaveDates(ready);
                done += ready.Count;

                var rate = clock.Elapsed.TotalSeconds > 0 ? done / clock.Elapsed.TotalSeconds : 0;
                TimeSpan? remaining = done >= 20 && rate > 0 ? TimeSpan.FromSeconds((work.Count - done) / rate) : null;
                progress?.Report(new IndexProgress(IndexPhase.ReadingDates, string.Empty, done, work.Count, 0, 0, rate, remaining));
            }
            return done;
        }

        /// <summary>The date of a photo file, read from the start of the file; null if the file can not be read now (it is tried again next time).</summary>
        private static PhotoDate? ReadDate(PhotoFile file, DateTime now)
        {
            try
            {
                using var stream = new FileStream(
                    file.OriginalFilePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 << 16, FileOptions.RandomAccess);
                return ResolveDate(ExifReader.Read(stream), file, now);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static PhotoDate ResolveDate(ExifDates exif, PhotoFile file, DateTime now) =>
            PhotoDates.Resolve(exif, PhotoDates.FileTime(file.CreationTimeUtc), PhotoDates.FileTime(file.LastWriteTimeUtc), now);

        // ── the pipeline ───────────────────────────────────────────────────

        private sealed record FaceData(float X, float Y, float Width, float Height, float Score, float[] Embedding);

        private sealed class WorkItem(PhotoFile file, string relativePath, IndexedPhoto? existing, bool needClip, bool needFaces)
        {
            public PhotoFile File { get; } = file;
            public string RelativePath { get; } = relativePath;
            public IndexedPhoto? Existing { get; } = existing; // the same photo from an earlier run, if it did not change
            public bool NeedClip { get; } = needClip;
            public bool NeedFaces { get; } = needFaces;

            public byte[]? FileBytes;
            public PhotoDate Date = PhotoDate.NotRead; // worked out by the reader, from the bytes it has just read
            public DecodedPhoto? Decoded;
            public float[]? ClipInput;
            public float[]? DetectInput;
            public float DetectScale;

            public float[]? Clip;
            public List<FaceData> Faces = [];

            public string? Failure;
            public bool Transient; // the failure was reading the file: not worth remembering, try again next time
            public int Pending;    // stages that still have to finish

            public void Fail(string message, bool transient = false)
            {
                lock (this)
                {
                    Failure ??= message;
                    Transient |= transient;
                }
            }
        }

        private sealed class Pipeline
        {
            private readonly PhotoIndex _index;
            private readonly ModelHub _models;
            private readonly IReadOnlyList<WorkItem> _work;
            private readonly IProgress<IndexProgress>? _progress;

            private readonly BlockingCollection<WorkItem> _raw = new(24);
            private readonly BlockingCollection<WorkItem> _forClip = new(16);
            private readonly BlockingCollection<WorkItem> _forFaces = new(16);
            private readonly BlockingCollection<WorkItem> _finished = new();

            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private Exception? _fault;
            private CancellationTokenSource? _stop;
            private int _decoderCount;
            private long _readTicks, _decodeTicks, _clipTicks, _faceTicks;

            // Written by the writer thread only; read after it has finished
            public int Done { get; private set; }
            public int Failed { get; private set; }
            public int Faces { get; private set; }

            public StageTimes Times => new(Elapsed(_readTicks), Elapsed(_decodeTicks), Elapsed(_clipTicks), Elapsed(_faceTicks), _decoderCount, _models.Threads.FaceWorkers);

            private static TimeSpan Elapsed(long ticks) => TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);

            public Pipeline(PhotoIndex index, ModelHub models, IReadOnlyList<WorkItem> work, IProgress<IndexProgress>? progress)
            {
                _index = index;
                _models = models;
                _work = work;
                _progress = progress;
            }

            public void Run(CancellationToken cancellationToken)
            {
                _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var token = _stop.Token;

                var threads = _models.Threads;
                _decoderCount = threads.Decoders;
                var decoders = Enumerable.Range(0, _decoderCount).Select(i => Start($"decoder {i}", () => Decode(token))).ToList();
                var reader = Start("reader", () => Read(token));
                var clip = Start("clip", () => EncodeClip(token));
                var faces = Enumerable.Range(0, threads.FaceWorkers).Select(i => Start($"faces {i}", () => FindFaces(token))).ToList();
                var writer = Start("writer", Write);

                reader.Join();
                decoders.ForEach(t => t.Join());
                _forClip.CompleteAdding();
                _forFaces.CompleteAdding();
                clip.Join();
                faces.ForEach(t => t.Join());
                _finished.CompleteAdding();
                writer.Join();

                _stop.Dispose();
                _stop = null;
                if (_fault is not null)
                    ExceptionDispatchInfo.Capture(_fault).Throw();
            }

            /// <summary>A background thread that lets the rest of the computer have the CPU first.</summary>
            private Thread Start(string name, Action body)
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        body();
                    }
                    catch (OperationCanceledException)
                    {
                        // stopped
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref _fault, ex, null);
                        _stop?.Cancel();
                    }
                })
                {
                    Name = $"PhotoLiker index {name}",
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                };
                thread.Start();
                return thread;
            }

            private void Read(CancellationToken token)
            {
                try
                {
                    foreach (var item in _work)
                    {
                        token.ThrowIfCancellationRequested();
                        var started = Stopwatch.GetTimestamp();
                        try
                        {
                            item.FileBytes = FileReading.ReadAllBytesShared(item.File.OriginalFilePath);
                            Interlocked.Add(ref _readTicks, Stopwatch.GetTimestamp() - started);
                            item.Date = ResolveDate(ExifReader.Read(new MemoryStream(item.FileBytes, writable: false)), item.File, DateTime.Now);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            item.Fail(ex.Message, transient: true);
                            _finished.Add(item);
                            continue;
                        }
                        _raw.Add(item, token);
                    }
                }
                finally
                {
                    _raw.CompleteAdding();
                }
            }

            private void Decode(CancellationToken token)
            {
                foreach (var item in _raw.GetConsumingEnumerable(token))
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        var photo = WicPhotoDecoder.Decode(item.FileBytes!);
                        item.FileBytes = null;

                        if (item.NeedClip)
                            item.ClipInput = ClipImageEncoder.Prepare(photo);
                        if (item.NeedFaces)
                        {
                            (item.DetectInput, item.DetectScale) = FaceDetector.Prepare(photo);
                            item.Decoded = photo; // the faces are cut out of it later
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        item.FileBytes = null;
                        item.Fail($"Could not decode the photo: {ex.Message}");
                        _finished.Add(item);
                        continue;
                    }

                    Interlocked.Add(ref _decodeTicks, Stopwatch.GetTimestamp() - started);

                    item.Pending = (item.NeedClip ? 1 : 0) + (item.NeedFaces ? 1 : 0);
                    if (item.NeedClip)
                        _forClip.Add(item, token);
                    if (item.NeedFaces)
                        _forFaces.Add(item, token);
                }
            }

            private void EncodeClip(CancellationToken token)
            {
                var encoder = _models.ImageEncoder;
                var buffer = new float[ClipBatchSize * ClipImageEncoder.InputLength];
                var batch = new List<WorkItem>(ClipBatchSize);

                foreach (var first in _forClip.GetConsumingEnumerable(token))
                {
                    // Whatever is waiting goes into one batch, a short queue is not worth waiting for
                    batch.Add(first);
                    while (batch.Count < ClipBatchSize && _forClip.TryTake(out var more))
                        batch.Add(more);

                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        for (var i = 0; i < batch.Count; i++)
                            Array.Copy(batch[i].ClipInput!, 0, buffer, i * ClipImageEncoder.InputLength, ClipImageEncoder.InputLength);

                        var vectors = encoder.Encode(buffer, batch.Count);
                        for (var i = 0; i < batch.Count; i++)
                            batch[i].Clip = vectors[i];
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        foreach (var item in batch)
                            item.Fail($"CLIP failed: {ex.Message}");
                    }

                    Interlocked.Add(ref _clipTicks, Stopwatch.GetTimestamp() - started);

                    foreach (var item in batch)
                    {
                        item.ClipInput = null;
                        StageDone(item);
                    }
                    batch.Clear();
                }
            }

            private void FindFaces(CancellationToken token)
            {
                var detector = _models.Detector;
                var embedder = _models.Embedder;

                foreach (var item in _forFaces.GetConsumingEnumerable(token))
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        item.Faces = DetectAndEmbed(item, detector, embedder);
                        Interlocked.Add(ref _faceTicks, Stopwatch.GetTimestamp() - started);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        item.Fail($"Face detection failed: {ex.Message}");
                    }

                    item.DetectInput = null;
                    item.Decoded = null;
                    StageDone(item);
                }
            }

            private void StageDone(WorkItem item)
            {
                if (Interlocked.Decrement(ref item.Pending) == 0)
                    _finished.Add(item);
            }

            private static List<FaceData> DetectAndEmbed(WorkItem item, FaceDetector detector, FaceEmbedder embedder)
            {
                var photo = item.Decoded!;
                var found = detector.Detect(item.DetectInput!, item.DetectScale)
                    .Where(f => Math.Min(f.Width, f.Height) >= MinFaceSize)
                    .OrderByDescending(f => f.Width * f.Height)
                    .Take(MaxFacesPerPhoto)
                    .ToList();
                if (found.Count == 0)
                    return [];

                var crops = new float[found.Count * FaceAligner.Length];
                for (var i = 0; i < found.Count; i++)
                    Array.Copy(FaceAligner.Align(photo, found[i].Landmarks), 0, crops, i * FaceAligner.Length, FaceAligner.Length);
                var embeddings = embedder.Embed(crops, found.Count);

                var faces = new List<FaceData>(found.Count);
                for (var i = 0; i < found.Count; i++)
                    faces.Add(ToFaceData(found[i], photo, embeddings[i]));
                return faces;
            }

            private static FaceData ToFaceData(DetectedFace face, DecodedPhoto photo, float[] embedding)
            {
                var x = Math.Clamp(face.X / photo.Width, 0f, 1f);
                var y = Math.Clamp(face.Y / photo.Height, 0f, 1f);
                var width = Math.Min(face.Width / photo.Width, 1f - x);
                var height = Math.Min(face.Height / photo.Height, 1f - y);
                return new FaceData(x, y, width, height, face.Score, embedding);
            }

            /// <summary>Saves finished photos in batches and reports the progress. Runs until everything is finished, also after a stop.</summary>
            private void Write()
            {
                var batch = new List<IndexedPhoto>(SaveBatchSize);
                var lastSave = Stopwatch.StartNew();
                var lastReport = Stopwatch.StartNew();

                foreach (var item in _finished.GetConsumingEnumerable())
                {
                    Done++;
                    if (item.Failure is not null)
                        Failed++;

                    if (Build(item) is { } photo)
                    {
                        Faces += photo.Faces.Count;
                        batch.Add(photo);
                    }
                    item.Decoded = null;

                    if (batch.Count >= SaveBatchSize || (batch.Count > 0 && lastSave.Elapsed >= SaveInterval))
                    {
                        _index.Save(batch);
                        batch.Clear();
                        lastSave.Restart();
                    }

                    if (lastReport.Elapsed >= ReportInterval)
                    {
                        lastReport.Restart();
                        Report();
                    }
                }

                _index.Save(batch);
                Report();
            }

            private void Report()
            {
                var seconds = _clock.Elapsed.TotalSeconds;
                var rate = seconds > 0 ? Done / seconds : 0;
                TimeSpan? remaining = Done >= 20 && rate > 0 ? TimeSpan.FromSeconds((_work.Count - Done) / rate) : null;
                _progress?.Report(new IndexProgress(IndexPhase.Indexing, string.Empty, Done, _work.Count, Failed, Faces, rate, remaining));
            }

            /// <summary>The photo as it goes into the index; null if there is nothing to store (a failed read, or a failure that must not replace good data).</summary>
            private static IndexedPhoto? Build(WorkItem item)
            {
                var existing = item.Existing;
                if (item.Failure is not null && (item.Transient || existing is not null))
                    return null;

                var photo = new IndexedPhoto(item.RelativePath, item.File.Length, item.File.LastWriteTimeUtc.Ticks)
                {
                    Clip = item.Failure is not null ? null : item.NeedClip ? item.Clip : existing?.Clip,
                    ClipVersion = ClipVersion,
                    FaceVersion = FaceVersion,
                    Failure = item.Failure,
                    Date = item.Date,
                };
                if (item.Failure is not null)
                    return photo;

                var faces = new List<IndexedFace>();
                if (item.NeedFaces)
                {
                    faces.AddRange(item.Faces.Select(f => new IndexedFace(photo, f.X, f.Y, f.Width, f.Height, f.Score, manual: false, f.Embedding)));

                    // The faces marked by hand are the user's work: they stay when the detector runs again
                    if (existing is not null)
                        faces.AddRange(existing.Faces.Where(f => f.Manual).Select(f => Copy(f, photo)));
                }
                else if (existing is not null)
                {
                    faces.AddRange(existing.Faces.Select(f => Copy(f, photo)));
                }
                photo.Faces = faces;
                return photo;
            }

            private static IndexedFace Copy(IndexedFace face, IndexedPhoto photo) =>
                new(photo, face.X, face.Y, face.Width, face.Height, face.Score, face.Manual, face.Embedding);
        }

        /// <summary>Passes the progress of a model download on, a few times per second.</summary>
        private sealed class DownloadReporter(IProgress<IndexProgress>? progress) : IProgress<DownloadProgress>
        {
            private readonly Stopwatch _since = Stopwatch.StartNew();

            public void Report(DownloadProgress value)
            {
                if (progress is null || (_since.ElapsedMilliseconds < 250 && value.Done < value.Total))
                    return;

                _since.Restart();
                var message = value.Total > 0
                    ? $"Downloading {value.Name}: {value.Done >> 20} of {value.Total >> 20} MB"
                    : $"Downloading {value.Name}: {value.Done >> 20} MB";
                progress.Report(new IndexProgress(IndexPhase.DownloadingModels, message, (int)(value.Done >> 20), (int)(value.Total >> 20), 0, 0, 0, null));
            }
        }
    }
}
