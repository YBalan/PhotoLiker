using System.Diagnostics;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <param name="Analyzed">Videos whose moments were made in this run.</param>
    /// <param name="Moments">How many moments those videos have.</param>
    /// <param name="Failed">Videos whose pictures could not be taken (remembered until the file changes) or that could not be read just now (tried again next time).</param>
    /// <param name="Removed">Videos forgotten because the files are gone.</param>
    /// <param name="Unreadable">Videos that are in the folder but are no video ffmpeg can read.</param>
    public sealed record VideoIndexSummary(
        int Analyzed, int Moments, int Failed, int Removed, TimeSpan Elapsed, bool Cancelled, bool DownloadDeclined = false, int Unreadable = 0, int Known = 0);

    /// <summary>
    /// Brings the catalog of a folder up to date with what the videos show: finds the videos, reads them (what is in them, see <see cref="VideoCatalog"/>), and for
    /// those that have no moments for the model yet takes pictures out of them (<see cref="VideoFrames"/>), has the model look at each, and keeps the moments
    /// (<see cref="VideoAnalyzer"/>). A video is finished or not: stopping loses the one that is being done, and running again continues with the next.
    /// It runs on one background thread at a low priority, after the photos are indexed; ffmpeg reads the video as it goes, which on a network share is what takes the time.
    /// </summary>
    public static class VideoIndexingJob
    {
        private const int ProbeThreads = 3;
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(250);

        /// <param name="root">The folder that is indexed, with its subfolders.</param>
        /// <param name="excludedFolder">A folder to leave out, e.g. the liked folder with its copies.</param>
        /// <param name="extensions">The extensions that count as videos.</param>
        /// <param name="confirmDownload">Asked before the files of the model are downloaded (they are not needed if they are here); null means yes.</param>
        public static Task<VideoIndexSummary> RunAsync(
            VideoCatalog catalog, ModelHub models, ClipModelInfo model, string root, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken, Func<ModelDownload, bool>? confirmDownload = null)
        {
            var done = new TaskCompletionSource<VideoIndexSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    done.SetResult(Run(catalog, models, model, root, excludedFolder, extensions, progress, cancellationToken, confirmDownload));
                }
                catch (OperationCanceledException)
                {
                    done.SetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            })
            { Name = "PhotoLiker video index", IsBackground = true, Priority = ThreadPriority.BelowNormal };
            thread.Start();
            return done.Task;
        }

        private static VideoIndexSummary Run(
            VideoCatalog catalog, ModelHub models, ClipModelInfo model, string root, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken token, Func<ModelDownload, bool>? confirmDownload)
        {
            var clock = Stopwatch.StartNew();

            progress?.Report(new IndexProgress(IndexPhase.Scanning, string.Empty, 0, 0, 0, 0, 0, null));
            var found = PhotoScanner.Scan(root, recursive: true, extensions, excludedFolder);
            if (found.Count == 0)
                return new VideoIndexSummary(0, 0, 0, 0, clock.Elapsed, Cancelled: false);

            // A folder that cannot be listed (a share that is off) gives nothing, and nothing is forgotten then
            var onDisk = new HashSet<string>(found.Select(f => f.OriginalFilePath), StringComparer.OrdinalIgnoreCase);
            var removed = 0;
            foreach (var gone in catalog.Records().Where(r => !onDisk.Contains(catalog.FullPathOf(r))))
            {
                catalog.Forget(catalog.FullPathOf(gone));
                removed++;
            }

            // Every video is read first (quick: the header), which also tells how much there is to do
            var read = 0;
            var transient = 0;
            Parallel.ForEach(found, new ParallelOptions { MaxDegreeOfParallelism = ProbeThreads, CancellationToken = token }, file =>
            {
                try
                {
                    catalog.GetAsync(file.OriginalFilePath, token).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    Interlocked.Increment(ref transient); // tried again next time
                }

                var n = Interlocked.Increment(ref read);
                if (n % 4 == 0 || n == found.Count)
                    progress?.Report(new IndexProgress(IndexPhase.Videos, string.Empty, n, found.Count, 0, 0, 0, null));
            });

            var records = catalog.Records().Where(r => onDisk.Contains(catalog.FullPathOf(r))).ToList();
            var states = catalog.AnalysisStates(model.Id);
            var work = records
                .Where(r => r.Info is not null && !(states.TryGetValue(r.Id, out var state) && state.Version >= VideoCatalog.AnalysisVersion))
                .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var unreadable = records.Count(r => r.Info is null);

            if (work.Count == 0)
                return new VideoIndexSummary(0, 0, transient, removed, clock.Elapsed, Cancelled: false, Unreadable: unreadable, Known: records.Count);

            // The model that looks at pictures, and the ones that look at faces: both are needed (who is in a video is part of what it shows)
            var needClip = !models.ClipDownloaded(model);
            var needFaces = !models.FacesDownloaded;
            if (needClip || needFaces)
            {
                var download = new ModelDownload(needClip ? model : null, needFaces, ModelFiles.MissingMegabytes(models.ModelsFolder, model, faces: true), WithIndexing: true);
                if (confirmDownload is not null && !confirmDownload(download))
                    return new VideoIndexSummary(0, 0, 0, removed, clock.Elapsed, Cancelled: false, DownloadDeclined: true, Unreadable: unreadable, Known: records.Count);

                models.EnsureDownloadedAsync(needClip ? model : null, needFaces, new DownloadReporter(progress), token).GetAwaiter().GetResult();
            }

            progress?.Report(new IndexProgress(IndexPhase.LoadingModels, string.Empty, 0, work.Count, 0, 0, 0, null));
            using var encoder = models.CreateImageEncoder(model, Math.Max(2, models.Threads.ClipAlone));
            var detector = models.Detector;
            var embedder = models.Embedder;
            Func<DecodedPhoto, IReadOnlyList<MomentFace>> findFaces = picture => MomentFaces.Find(picture, detector, embedder);
            var faceWorkers = models.Threads.FaceWorkers;

            var totalSeconds = work.Sum(r => r.Info!.DurationSeconds);
            var finishedSeconds = 0.0;
            var analyzed = 0;
            var moments = 0;
            var failed = transient;
            var lastReport = Stopwatch.StartNew();

            void Report(string name, int videosDone, double secondsDone)
            {
                var seconds = clock.Elapsed.TotalSeconds;
                var speed = seconds > 0 ? secondsDone / seconds : 0;
                TimeSpan? remaining = speed > 0 && secondsDone > 20 ? TimeSpan.FromSeconds(Math.Max(0, totalSeconds - secondsDone) / speed) : null;
                progress?.Report(new IndexProgress(IndexPhase.Videos, name, videosDone, work.Count, failed, moments, speed, remaining));
                lastReport.Restart();
            }

            try
            {
                for (var i = 0; i < work.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var video = work[i];
                    var path = catalog.FullPathOf(video);
                    var name = Path.GetFileName(path);
                    Report(name, i, finishedSeconds);

                    try
                    {
                        var made = AnalyzeVideo(path, video.Info!, model, encoder, token,
                            reached => { if (lastReport.Elapsed >= ReportInterval) Report(name, i, finishedSeconds + Math.Min(reached, video.Info!.DurationSeconds)); },
                            findFaces, faceWorkers);
                        if (made.Count == 0)
                        {
                            catalog.SaveAnalysis(video.Id, model.Id, [], "ffmpeg gave no picture of this video");
                            failed++;
                        }
                        else
                        {
                            catalog.SaveAnalysis(video.Id, model.Id, made);
                            analyzed++;
                            moments += made.Count;
                        }
                    }
                    catch (InvalidDataException ex)
                    {
                        catalog.SaveAnalysis(video.Id, model.Id, [], ex.Message); // not tried again until the file changes
                        failed++;
                    }
                    catch (IOException)
                    {
                        failed++; // could not be read just now: tried again next time
                    }

                    finishedSeconds += video.Info!.DurationSeconds;
                }
            }
            catch (OperationCanceledException)
            {
                return new VideoIndexSummary(analyzed, moments, failed, removed, clock.Elapsed, Cancelled: true, Unreadable: unreadable, Known: records.Count);
            }

            Report(string.Empty, work.Count, finishedSeconds);
            return new VideoIndexSummary(analyzed, moments, failed, removed, clock.Elapsed, Cancelled: false, Unreadable: unreadable, Known: records.Count);
        }

        /// <summary>The moments of one video: its key frames if they are close enough together, else every picture.</summary>
        internal static List<MomentData> AnalyzeVideo(
            string path, VideoInfo info, ClipModelInfo model, ClipImageEncoder encoder, CancellationToken token, Action<double>? reached,
            Func<DecodedPhoto, IReadOnlyList<MomentFace>>? findFaces = null, int faceWorkers = 1)
        {
            var sampling = VideoFrames.SamplingFor(info);
            if (sampling == FrameSampling.KeyFrames)
            {
                try
                {
                    return VideoAnalyzer.Analyze(VideoFrames.Read(path, info, sampling, token), model, encoder.Encode, new MomentSelector(), token, reached, keyFramesOnly: true, findFaces, faceWorkers);
                }
                catch (SparseKeyFramesException)
                {
                    // The key frames are too far apart (a long GOP): decode every picture and take them by the clock instead
                }
            }

            return VideoAnalyzer.Analyze(VideoFrames.Read(path, info, FrameSampling.EveryFrame, token), model, encoder.Encode, new MomentSelector(), token, reached, keyFramesOnly: false, findFaces, faceWorkers);
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
                    ? string.Format(SearchLibStrings.DownloadingFormat, value.Name, value.Done >> 20, value.Total >> 20)
                    : string.Format(SearchLibStrings.DownloadingNoTotalFormat, value.Name, value.Done >> 20);
                progress.Report(new IndexProgress(IndexPhase.DownloadingModels, message, (int)(value.Done >> 20), (int)(value.Total >> 20), 0, 0, 0, null));
            }
        }
    }
}
