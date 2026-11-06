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
    public sealed record SpeechIndexSummary(int Transcribed, int Segments, int Silent, int Failed, TimeSpan Elapsed, bool Cancelled, bool DownloadDeclined = false, int Known = 0);

    /// <summary>
    /// Turns what is said in the videos of a folder into text, with a Whisper model, and keeps it in the catalog (see <see cref="VideoCatalog.SaveTranscript"/>). The sound of a
    /// video is made into a 16 kHz WAV file by ffmpeg, checked for being more than silence, and given to the model. It takes about a third of the length of the
    /// speech with the model that works (small), on the processor, so it is a job of its own that is asked for (the setting VideoSpeech), and it continues where it stopped.
    /// </summary>
    public static class SpeechIndexingJob
    {
        /// <summary>A sound quieter than this (dB below the loudest possible, as a mean) is silence for the model's purposes.</summary>
        public const double SilenceDb = -50;

        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(500);

        /// <param name="modelsFolder">Where the models are (the speech model is in its whisper folder).</param>
        /// <param name="language">"auto", or the code of the language that is spoken ("ru", "uk", "en").</param>
        /// <param name="confirmDownload">Asked before the model is downloaded; null means yes.</param>
        /// <param name="recognizerFactory">Makes the recognizer from the path of the model file (a test gives its own); null is Whisper.</param>
        public static Task<SpeechIndexSummary> RunAsync(
            VideoCatalog catalog, string modelsFolder, SpeechModelInfo model, string language, string root, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken, Func<SpeechDownload, bool>? confirmDownload = null,
            Func<string, ISpeechRecognizer>? recognizerFactory = null)
        {
            var done = new TaskCompletionSource<SpeechIndexSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    done.SetResult(Run(catalog, modelsFolder, model, language, root, excludedFolder, extensions, progress, cancellationToken, confirmDownload, recognizerFactory));
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
            IProgress<IndexProgress>? progress, CancellationToken token, Func<SpeechDownload, bool>? confirmDownload, Func<string, ISpeechRecognizer>? recognizerFactory)
        {
            var clock = Stopwatch.StartNew();

            progress?.Report(new IndexProgress(IndexPhase.Scanning, string.Empty, 0, 0, 0, 0, 0, null));
            var onDisk = PhotoScanner.Scan(root, recursive: true, extensions, excludedFolder).Select(f => f.OriginalFilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The videos are known to the catalog already (the picture analysis reads them first); the ones it has not seen yet are read here
            foreach (var path in onDisk)
            {
                token.ThrowIfCancellationRequested();
                try { catalog.GetAsync(path, token).GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is IOException or TimeoutException) { /* tried again next time */ }
            }

            var withSound = catalog.Records().Where(r => onDisk.Contains(catalog.FullPathOf(r)) && r.Info is { Audio: not null }).ToList();
            var states = catalog.TranscriptStates(model.Id);
            var work = withSound
                .Where(r => !(states.TryGetValue(r.Id, out var state) && state.Version >= VideoCatalog.TranscriptVersion))
                .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

            var totalSeconds = work.Sum(r => r.Info!.DurationSeconds);
            var finishedSeconds = 0.0;
            var transcribed = 0;
            var segmentCount = 0;
            var silent = 0;
            var failed = 0;
            var lastReport = Stopwatch.StartNew();

            void Report(string name, int videosDone)
            {
                var seconds = clock.Elapsed.TotalSeconds;
                var speed = seconds > 0 ? finishedSeconds / seconds : 0;
                TimeSpan? remaining = speed > 0 && finishedSeconds > 30 ? TimeSpan.FromSeconds(Math.Max(0, totalSeconds - finishedSeconds) / speed) : null;
                progress?.Report(new IndexProgress(IndexPhase.Speech, name, videosDone, work.Count, failed, segmentCount, speed, remaining));
                lastReport.Restart();
            }

            try
            {
                for (var i = 0; i < work.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var video = work[i];
                    var path = catalog.FullPathOf(video);
                    Report(Path.GetFileName(path), i);

                    var wav = Path.Combine(Path.GetTempPath(), $"photoliker_speech_{Guid.NewGuid():N}.wav");
                    try
                    {
                        if (!SpeechAudio.ExtractAsync(path, wav, token).GetAwaiter().GetResult())
                        {
                            catalog.SaveTranscript(video.Id, model.Id, string.Empty, [], "no sound could be taken from this video");
                            failed++;
                        }
                        else if (SpeechAudio.LoudnessDb(wav) < SilenceDb)
                        {
                            catalog.SaveTranscript(video.Id, model.Id, string.Empty, []);
                            silent++;
                        }
                        else
                        {
                            var heard = recognizer.TranscribeAsync(wav, language, token).GetAwaiter().GetResult();
                            catalog.SaveTranscript(video.Id, model.Id, heard.Language, heard.Segments);
                            transcribed++;
                            segmentCount += heard.Segments.Count;
                        }
                    }
                    catch (InvalidDataException ex)
                    {
                        catalog.SaveTranscript(video.Id, model.Id, string.Empty, [], ex.Message); // not tried again until the file changes
                        failed++;
                    }
                    catch (IOException)
                    {
                        failed++; // could not be read just now: tried again next time
                    }
                    finally
                    {
                        try { File.Delete(wav); } catch (IOException) { /* in the temp folder: it goes with the next clean-up */ }
                    }

                    finishedSeconds += video.Info!.DurationSeconds;
                    if (lastReport.Elapsed >= ReportInterval)
                        Report(Path.GetFileName(path), i + 1);
                }
            }
            catch (OperationCanceledException)
            {
                return new SpeechIndexSummary(transcribed, segmentCount, silent, failed, clock.Elapsed, Cancelled: true, Known: withSound.Count);
            }

            Report(string.Empty, work.Count);
            return new SpeechIndexSummary(transcribed, segmentCount, silent, failed, clock.Elapsed, Cancelled: false, Known: withSound.Count);
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
