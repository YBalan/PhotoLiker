using System.Diagnostics;
using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>What a run is about to download, to ask the user first: the speech model (hundreds of megabytes).</summary>
    public sealed record SpeechDownload(SpeechModelInfo Model);

    /// <param name="Transcribed">Videos whose speech was turned into text in this run (also one that had nothing said in it that the model could hear).</param>
    /// <param name="Segments">How many stretches of speech those videos have.</param>
    /// <param name="Silent">Videos with no sound worth listening to (not given to the model: it writes words for silence).</param>
    /// <param name="Failed">Videos whose sound could not be read.</param>
    /// <param name="Known">Videos of the folder that have sound.</param>
    /// <param name="Reread">How many of <paramref name="Transcribed"/> had been heard before, as another language than the one that is set, and were heard again.</param>
    public sealed record SpeechIndexSummary(int Transcribed, int Segments, int Silent, int Failed, TimeSpan Elapsed, bool Cancelled, bool DownloadDeclined = false, int Known = 0, int Reread = 0);

    /// <summary>
    /// What a run is about to do over again, to ask the user first: videos whose text was written for another language than the one that is set now. Hearing them again
    /// replaces that text, and takes about a third of their length.
    /// </summary>
    /// <param name="Videos">How many videos.</param>
    /// <param name="Length">How long they are together.</param>
    /// <param name="Language">The language they would be heard in.</param>
    public sealed record SpeechReread(int Videos, TimeSpan Length, string Language);

    /// <summary>
    /// Turns what is said in the videos of a folder into text, with a Whisper model, and keeps it in the catalog (see <see cref="VideoCatalog.SaveTranscript"/>). The sound of a
    /// video is made into a 16 kHz WAV file by ffmpeg, checked for being more than silence, and given to the model. It takes about a third of the length of the
    /// speech with the model that works (small), on the processor, so it is a job of its own that is asked for (the setting VideoSpeech), and it continues where it stopped.
    /// Left to find the language itself ("auto") the model takes a short or noisy video for any language there is and writes nonsense; when a language is set, the videos
    /// that were heard as another one are heard again in it (see <see cref="HeardInAnotherLanguage"/>), after asking.
    /// </summary>
    public static class SpeechIndexingJob
    {
        /// <summary>A sound quieter than this (dB below the loudest possible, as a mean) is silence for the model's purposes.</summary>
        public const double SilenceDb = -50;

        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>Whether the model is left to hear the language itself (nothing set, or "auto").</summary>
        public static bool IsAuto(string? language) => string.IsNullOrWhiteSpace(language) || string.Equals(language.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a video was heard as another language than <paramref name="language"/>, the one that is set: it is then heard again in it. Nothing is heard again while the
        /// language is "auto", and what was never given to the model (a silent video, one that failed: no language kept) is not either.
        /// </summary>
        public static bool HeardInAnotherLanguage(TranscriptState state, string? language) =>
            !IsAuto(language) && state.Failure is null && state.Language.Length > 0 && !string.Equals(state.Language, language!.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The language a transcript is kept as: the one that was asked for, else the one the model heard. Kept as asked, so that a video the model wrote nothing for
        /// (it then reports no language) is not heard again and again.
        /// </summary>
        public static string LanguageToKeep(string asked, string heard) => IsAuto(asked) ? heard : asked.Trim().ToLowerInvariant();

        /// <summary>How often (milliseconds) the cores setting is looked at while the videos are being heard: more cores start more videos at once at the next look.</summary>
        private const int CoresPollInterval = 500;

        /// <param name="modelsFolder">Where the models are (the speech model is in its whisper folder).</param>
        /// <param name="language">"auto", or the code of the language that is spoken ("ru", "uk", "en"). With a code, the videos that were heard as another language are heard again.</param>
        /// <param name="confirmDownload">Asked before the model is downloaded; null means yes.</param>
        /// <param name="recognizerFactory">Makes the recognizer from the path of the model file (a test gives its own); null is Whisper.</param>
        /// <param name="cores">
        /// How many cores of the computer the job may use, asked again before each video is started (so it can be changed while the job runs); how they are shared between
        /// videos heard at the same time and the threads of each is <see cref="SpeechCores.LayoutFor"/>. Null means all the cores the computer has.
        /// </param>
        /// <param name="confirmReread">Asked, with the videos that were heard as another language than <paramref name="language"/>, before they are heard again (that replaces their text); no means they are left as they are. Null means yes.</param>
        public static Task<SpeechIndexSummary> RunAsync(
            VideoCatalog catalog, string modelsFolder, SpeechModelInfo model, string language, string root, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken, Func<SpeechDownload, bool>? confirmDownload = null,
            Func<string, ISpeechRecognizer>? recognizerFactory = null, Func<int>? cores = null, Func<SpeechReread, bool>? confirmReread = null) =>
            Launch(cancellationToken, () => Run(
                catalog, modelsFolder, model, language, root, excludedFolder, extensions, redo: null,
                progress, cancellationToken, confirmDownload, recognizerFactory, cores ?? (() => Environment.ProcessorCount), confirmReread));

        /// <summary>
        /// Hears the given videos of the catalog (full paths) again, in this language, whatever was heard in them before and whatever language it was: the person asked for exactly
        /// these, so nothing is asked and the text of the others is not touched. Videos without sound, and ones the catalog does not know, are left out. The text of a video is replaced when
        /// its new text is ready, so a run that is stopped leaves the rest as they were. <see cref="SpeechIndexSummary.Reread"/> counts the videos that had been heard before.
        /// </summary>
        /// <param name="language">"auto", or the code of the language that is spoken.</param>
        public static Task<SpeechIndexSummary> RedoAsync(
            VideoCatalog catalog, string modelsFolder, SpeechModelInfo model, string language, IReadOnlyCollection<string> videos,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken, Func<SpeechDownload, bool>? confirmDownload = null,
            Func<string, ISpeechRecognizer>? recognizerFactory = null, Func<int>? cores = null) =>
            Launch(cancellationToken, () => Run(
                catalog, modelsFolder, model, language, root: string.Empty, excludedFolder: null, extensions: [], redo: videos,
                progress, cancellationToken, confirmDownload, recognizerFactory, cores ?? (() => Environment.ProcessorCount), confirmReread: null));

        private static Task<SpeechIndexSummary> Launch(CancellationToken cancellationToken, Func<SpeechIndexSummary> run)
        {
            var done = new TaskCompletionSource<SpeechIndexSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    done.SetResult(run());
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
            { Name = "PhotoLiker speech index", IsBackground = true, Priority = ThreadPriority.BelowNormal };
            thread.Start();
            return done.Task;
        }

        private static SpeechIndexSummary Run(
            VideoCatalog catalog, string modelsFolder, SpeechModelInfo model, string language, string root, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IReadOnlyCollection<string>? redo,
            IProgress<IndexProgress>? progress, CancellationToken token, Func<SpeechDownload, bool>? confirmDownload, Func<string, ISpeechRecognizer>? recognizerFactory, Func<int> cores,
            Func<SpeechReread, bool>? confirmReread)
        {
            var clock = Stopwatch.StartNew();

            List<VideoRecord> withSound;
            if (redo is null)
            {
                progress?.Report(new IndexProgress(IndexPhase.Scanning, string.Empty, 0, 0, 0, 0, 0, null));
                var onDisk = PhotoScanner.Scan(root, recursive: true, extensions, excludedFolder).Select(f => f.OriginalFilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // The videos are known to the catalog already (the picture analysis reads them first); the ones it has not seen yet are read here
                foreach (var path in onDisk)
                {
                    token.ThrowIfCancellationRequested();
                    try { catalog.GetAsync(path, token).GetAwaiter().GetResult(); }
                    catch (Exception ex) when (ex is IOException or TimeoutException) { /* tried again next time */ }
                }

                withSound = catalog.Records().Where(r => onDisk.Contains(catalog.FullPathOf(r)) && r.Info is { Audio: not null }).ToList();
            }
            else
            {
                var asked = redo.ToHashSet(StringComparer.OrdinalIgnoreCase);
                withSound = catalog.Records().Where(r => asked.Contains(catalog.FullPathOf(r)) && r.Info is { Audio: not null }).ToList();
            }

            var states = catalog.TranscriptStates(model.Id);
            bool Heard(VideoRecord r) => states.TryGetValue(r.Id, out var state) && state.Version >= VideoCatalog.TranscriptVersion;

            HashSet<long> rereadIds;
            List<VideoRecord> work;
            if (redo is null)
            {
                // The videos heard as another language than the one that is set are heard again, but only after asking: that replaces their text, and takes a while
                var again = withSound.Where(r => Heard(r) && HeardInAnotherLanguage(states[r.Id], language)).ToList();
                if (again.Count > 0 && confirmReread is not null && !confirmReread(new SpeechReread(again.Count, TimeSpan.FromSeconds(again.Sum(r => r.Info!.DurationSeconds)), language.Trim())))
                    again = [];
                rereadIds = again.Select(r => r.Id).ToHashSet();

                work = withSound
                    .Where(r => !Heard(r) || rereadIds.Contains(r.Id))
                    .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                // Exactly the videos that were asked for, heard or not
                rereadIds = withSound.Where(Heard).Select(r => r.Id).ToHashSet();
                work = [.. withSound.OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)];
            }
            if (work.Count == 0)
                return new SpeechIndexSummary(0, 0, 0, 0, clock.Elapsed, Cancelled: false, Known: withSound.Count);

            var modelPath = SpeechModels.PathOf(modelsFolder, model);
            if (!SpeechModels.IsPresent(modelsFolder, model) && recognizerFactory is null)
            {
                if (confirmDownload is not null && !confirmDownload(new SpeechDownload(model)))
                    return new SpeechIndexSummary(0, 0, 0, 0, clock.Elapsed, Cancelled: false, DownloadDeclined: true, Known: withSound.Count);

                SpeechModels.DownloadAsync(modelsFolder, model, new DownloadReporter(progress), token).GetAwaiter().GetResult();
            }

            progress?.Report(new IndexProgress(IndexPhase.LoadingModels, string.Empty, 0, work.Count, 0, 0, 0, null));
            using var recognizer = recognizerFactory?.Invoke(modelPath) ?? new WhisperRecognizer(modelPath);

            // The workers (videos heard at the same time) report and count under this lock
            var gate = new object();
            var totalSeconds = work.Sum(r => r.Info!.DurationSeconds);
            var finishedSeconds = 0.0;
            var finishedVideos = 0;
            var transcribed = 0;
            var segmentCount = 0;
            var silent = 0;
            var failed = 0;
            var reread = 0;
            var latestName = string.Empty;
            var lastReport = Stopwatch.StartNew();

            // The speed is the one since the cores were last changed, or the start: after a change the old average says nothing about the new speed
            var sinceAt = TimeSpan.Zero;
            var sinceSeconds = 0.0;

            void Report(bool force) // under the lock
            {
                if (!force && lastReport.Elapsed < ReportInterval)
                    return;

                var seconds = (clock.Elapsed - sinceAt).TotalSeconds;
                var heardSeconds = finishedSeconds - sinceSeconds;
                var speed = seconds > 0 ? heardSeconds / seconds : 0;
                TimeSpan? remaining = speed > 0 && heardSeconds > 30 ? TimeSpan.FromSeconds(Math.Max(0, totalSeconds - finishedSeconds) / speed) : null;
                progress?.Report(new IndexProgress(IndexPhase.Speech, latestName, finishedVideos, work.Count, failed, segmentCount, speed, remaining));
                lastReport.Restart();
            }

            async Task HearAsync(VideoRecord video, int threads, CancellationToken stop)
            {
                var wav = Path.Combine(Path.GetTempPath(), $"photoliker_speech_{Guid.NewGuid():N}.wav");
                try
                {
                    if (!await SpeechAudio.ExtractAsync(catalog.FullPathOf(video), wav, stop).ConfigureAwait(false))
                    {
                        catalog.SaveTranscript(video.Id, model.Id, string.Empty, [], "no sound could be taken from this video");
                        lock (gate) failed++;
                    }
                    else if (SpeechAudio.LoudnessDb(wav) < SilenceDb)
                    {
                        catalog.SaveTranscript(video.Id, model.Id, string.Empty, []);
                        lock (gate) silent++;
                    }
                    else
                    {
                        var heard = await recognizer.TranscribeAsync(wav, language, threads, stop).ConfigureAwait(false);
                        catalog.SaveTranscript(video.Id, model.Id, LanguageToKeep(language, heard.Language), heard.Segments);
                        lock (gate)
                        {
                            transcribed++;
                            segmentCount += heard.Segments.Count;
                            if (rereadIds.Contains(video.Id))
                                reread++;
                        }
                    }
                }
                catch (InvalidDataException ex)
                {
                    catalog.SaveTranscript(video.Id, model.Id, string.Empty, [], ex.Message); // not tried again until the file changes
                    lock (gate) failed++;
                }
                catch (IOException)
                {
                    lock (gate) failed++; // could not be read just now: tried again next time
                }
                finally
                {
                    try { File.Delete(wav); } catch (IOException) { /* in the temp folder: it goes with the next clean-up */ }
                }

                lock (gate)
                {
                    finishedSeconds += video.Info!.DurationSeconds;
                    finishedVideos++;
                    Report(force: false);
                }
            }

            // The videos are heard by as many workers at a time as the cores allow, and the cores are asked again before each video is started:
            // more cores starts more workers at once, fewer cores lets the ones that are busy finish and starts the next ones smaller.
            // A video that is under way keeps the threads it was started with, so the next one waits until its own share fits beside them.
            var pending = new Queue<VideoRecord>(work);
            var running = new List<(Task Task, int Threads)>();
            using var stopAll = CancellationTokenSource.CreateLinkedTokenSource(token);
            Exception? fault = null;
            var layout = default(SpeechLayout);
            var cancelled = false;

            void Collect(Task finished)
            {
                if (finished.Exception is { } error && error.InnerExceptions.FirstOrDefault(e => e is not OperationCanceledException) is { } real && fault is null)
                {
                    fault = real;
                    stopAll.Cancel(); // the others stop, and the job fails with this
                }
            }

            try
            {
                while (pending.Count > 0 || running.Count > 0)
                {
                    token.ThrowIfCancellationRequested();

                    var wanted = SpeechCores.LayoutFor(cores());
                    if (wanted != layout)
                    {
                        layout = wanted;
                        lock (gate)
                        {
                            sinceAt = clock.Elapsed;
                            sinceSeconds = finishedSeconds;
                        }
                    }

                    while (fault is null && pending.Count > 0 && running.Sum(r => r.Threads) + layout.ThreadsEach <= layout.Cores)
                    {
                        var video = pending.Dequeue();
                        var threads = layout.ThreadsEach;
                        lock (gate)
                        {
                            latestName = Path.GetFileName(catalog.FullPathOf(video));
                            Report(force: true);
                        }
                        running.Add((Task.Run(() => HearAsync(video, threads, stopAll.Token)), threads));
                    }

                    if (running.Count == 0)
                        break;

                    var index = Task.WaitAny([.. running.Select(r => r.Task)], CoresPollInterval);
                    if (index >= 0)
                    {
                        Collect(running[index].Task);
                        running.RemoveAt(index);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                // The model is let go of (below) only when no video is being heard any more
                try { Task.WaitAll([.. running.Select(r => r.Task)]); } catch (AggregateException) { /* looked at one by one next */ }
                foreach (var (task, _) in running)
                    Collect(task);
            }

            if (fault is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();

            if (cancelled || token.IsCancellationRequested)
                return new SpeechIndexSummary(transcribed, segmentCount, silent, failed, clock.Elapsed, Cancelled: true, Known: withSound.Count, Reread: reread);

            lock (gate)
            {
                latestName = string.Empty;
                Report(force: true);
            }
            return new SpeechIndexSummary(transcribed, segmentCount, silent, failed, clock.Elapsed, Cancelled: false, Known: withSound.Count, Reread: reread);
        }

        private sealed class DownloadReporter(IProgress<IndexProgress>? progress) : IProgress<DownloadProgress>
        {
            private readonly Stopwatch _since = Stopwatch.StartNew();

            public void Report(DownloadProgress value)
            {
                if (progress is null || (_since.ElapsedMilliseconds < 250 && value.Done < value.Total))
                    return;

                _since.Restart();
                progress.Report(new IndexProgress(IndexPhase.DownloadingModels, string.Format(SearchLibStrings.DownloadingFormat, value.Name, value.Done >> 20, value.Total >> 20), (int)(value.Done >> 20), (int)(value.Total >> 20), 0, 0, 0, null));
            }
        }
    }

    /// <summary>The sound of a video as a file the speech model can read.</summary>
    internal static class SpeechAudio
    {
        /// <summary>Writes the first sound track as 16 kHz, mono, 16 bit WAV. False if the video has no sound that could be taken.</summary>
        public static async Task<bool> ExtractAsync(string videoPath, string wavPath, CancellationToken cancellationToken)
        {
            var result = await Ffmpeg.RunAsync(
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", videoPath, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", "-f", "wav", wavPath],
                Timeout.InfiniteTimeSpan, cancellationToken);
            return result.ExitCode == 0 && File.Exists(wavPath) && new FileInfo(wavPath).Length > 1000;
        }

        /// <summary>How loud a 16-bit WAV is on the whole, in dB below the loudest sound it could hold (0 is as loud as can be, -100 is nothing).</summary>
        public static double LoudnessDb(string wavPath)
        {
            using var stream = File.OpenRead(wavPath);
            stream.Seek(Math.Min(100, stream.Length), SeekOrigin.Begin); // past the header
            var buffer = new byte[1 << 20];
            double sum = 0;
            long count = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 1)
            {
                for (var i = 0; i + 1 < read; i += 2)
                {
                    var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                    sum += (double)sample * sample;
                }
                count += read / 2;
            }
            return count == 0 || sum == 0 ? -100 : 20 * Math.Log10(Math.Sqrt(sum / count) / 32768.0);
        }
    }
}
