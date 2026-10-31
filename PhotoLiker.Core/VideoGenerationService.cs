using System.Runtime.InteropServices;

namespace PhotoLiker.Core
{
    public static class VideoGenerationService
    {
        /// <summary>
        /// The photos in the order they appear in the video: by the date the file was created like the folder list
        /// (<see cref="PhotoSortOrder"/>), by their names (<see cref="PhotoNameOrder"/>), in a random order (a new one each time), or
        /// as they are given. Search results come without a created date, so for them it is the last-write date. Photos of one date
        /// go by their path.
        /// </summary>
        public static List<PhotoFile> SortSourceFiles(IReadOnlyCollection<PhotoFile> photos, PhotoSortOrder sortOrder)
        {
            var ordered = photos.ToList();
            switch (sortOrder)
            {
                case PhotoSortOrder.OldestFirst:
                    ordered.Sort(OldestFirst);
                    break;
                case PhotoSortOrder.NewestFirst:
                    ordered.Sort((a, b) => OldestFirst(b, a));
                    break;
                case PhotoSortOrder.ByName:
                    ordered.Sort((a, b) => PhotoNameOrder.Compare(a.OriginalFilePath, b.OriginalFilePath));
                    break;
                case PhotoSortOrder.Random:
                    Random.Shared.Shuffle(CollectionsMarshal.AsSpan(ordered));
                    break;
            }

            return ordered;
        }

        private static int OldestFirst(PhotoFile a, PhotoFile b)
        {
            var byDate = DateOf(a).CompareTo(DateOf(b));
            return byDate != 0 ? byDate : string.Compare(a.OriginalFilePath, b.OriginalFilePath, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime DateOf(PhotoFile photo) =>
            photo.CreationTimeUtc != default ? photo.CreationTimeUtc : photo.LastWriteTimeUtc;

        /// <param name="sequence">The pictures and their times, as <see cref="BuildSequence"/> makes them.</param>
        public static async Task GenerateAsync(
            VideoSequence sequence,
            string outputPath,
            int width,
            int height,
            string codec,
            int crf,
            string? audioPath,
            Action<string> appendOutput,
            Action<int> reportProgress)
        {
            var listFilePath = Path.Combine(Path.GetTempPath(), $"photoliker_ffmpeg_{Guid.NewGuid():N}.txt");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                var concatContent = BuildFfmpegConcatFile(sequence.Files, sequence.Durations);
                File.WriteAllText(listFilePath, concatContent);

                var arguments = BuildFfmpegArguments(listFilePath, outputPath, width, height, codec, crf, audioPath, sequence.Audio);
                var ffmpegExecutablePath = ResolveFfmpegExecutablePath();

                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegExecutablePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                double totalDurationMs = sequence.ExpectedSeconds * 1000d;

                var stdOutTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
                    {
                        appendOutput(line);
                        if (TryParseFfmpegProgressPercent(line, totalDurationMs, out var percent))
                            reportProgress(percent);
                    }
                });

                var stdErrTask = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) is not null)
                        appendOutput(line);
                });

                await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync());

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}.");

                if (!File.Exists(outputPath))
                    throw new FileNotFoundException($"ffmpeg completed but output file was not created: {outputPath}", outputPath);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(FfmpegNotFoundMessage, ex);
            }
            finally
            {
                if (File.Exists(listFilePath))
                    File.Delete(listFilePath);
            }
        }

        /// <summary>
        /// The photos are shown as they are stored: <c>-noautorotate</c> keeps ffmpeg from turning them by their EXIF orientation, so the
        /// photos given have to be upright already. Left to ffmpeg (7.1), a photo that has to be turned (a portrait shot of a phone)
        /// also turns the photos after it that have no orientation or "as stored", and landscape photos come out as portrait.
        /// They also have to be of one kind of file: the concat demuxer takes the decoder of the first one for all, so a PNG followed by
        /// JPEG files ends in a video of a single frame, without an error.
        /// </summary>
        /// <param name="audioPath">
        /// The sound of the video, or null for none. <c>-shortest</c> ends the video with the shorter of the photos and the audio. Only
        /// the sound is taken (not the cover picture of a music file, which is a video stream to ffmpeg), and the photos only from the list.
        /// </param>
        /// <param name="audio">
        /// <see cref="AudioPlayback.Loop"/>: the audio is played over and over (<c>-stream_loop -1</c>), so a shorter one fills the video
        /// and the photos end it. <see cref="AudioPlayback.OnceThenSilence"/>: it is played once, and padded with silence (<c>apad</c>),
        /// so that a shorter one does not end the video, and <c>-shortest</c> ends the video with the photos. <see cref="AudioPlayback.OnceEndsVideo"/>:
        /// it is played once and the photos are longer than it, so that <c>-shortest</c> ends the video with it.
        /// </param>
        internal static string BuildFfmpegArguments(
            string listFilePath, string outputPath, int width, int height, string codec, int crf, string? audioPath = null, AudioPlayback audio = AudioPlayback.Loop)
        {
            var scaleFilter = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
            var hasAudio = !string.IsNullOrWhiteSpace(audioPath);
            var audioInput = hasAudio ? $"{(audio == AudioPlayback.Loop ? " -stream_loop -1" : string.Empty)} -i \"{audioPath}\"" : string.Empty;
            var audioOutput = hasAudio
                ? $" -map 0:v:0 -map 1:a:0{(audio == AudioPlayback.OnceThenSilence ? " -af apad" : string.Empty)} -c:a aac -b:a 192k -shortest"
                : string.Empty;

            return $"-y -f concat -safe 0 -noautorotate -i \"{listFilePath}\"{audioInput} -vf \"{scaleFilter}\"{audioOutput} -c:v {codec} -crf {crf} -pix_fmt yuv420p -progress pipe:1 -nostats \"{outputPath}\"";
        }

        internal const string FfmpegNotFoundMessage =
            "ffmpeg executable not found. Place ffmpeg.exe in the 'ffmpeg' subfolder next to the app, or ensure ffmpeg is available in PATH.";

        internal static string ResolveFfmpegExecutablePath()
        {
            var localFfmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(localFfmpegPath))
                return localFfmpegPath;

            return "ffmpeg";
        }

        internal static bool TryParseFfmpegProgressPercent(string line, double totalDurationMs, out int percent)
        {
            percent = 0;

            if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
            {
                percent = 100;
                return true;
            }

            if (totalDurationMs <= 0d || !line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                return false;

            var valueText = line["out_time_ms=".Length..];
            if (!double.TryParse(valueText, out var outTimeMicroseconds))
                return false;

            var currentMs = outTimeMicroseconds / 1000d;
            percent = (int)Math.Clamp(Math.Round(currentMs / totalDurationMs * 100d), 0d, 100d);
            return true;
        }

        internal static string BuildFfmpegConcatFile(IReadOnlyList<string> sourceFiles, IReadOnlyList<double> frameDurations)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < sourceFiles.Count; i++)
            {
                var escapedPath = sourceFiles[i].Replace("'", "'\\''");
                sb.Append("file '").Append(escapedPath).AppendLine("'");

                if (i < frameDurations.Count)
                    sb.Append("duration ").AppendLine(frameDurations[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (sourceFiles.Count > 0)
            {
                var escapedLastPath = sourceFiles[^1].Replace("'", "'\\''");
                sb.Append("file '").Append(escapedLastPath).AppendLine("'");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The last photo is listed once more, without a time (how the concat list ends), and that is one more frame, at 25 fps, at the end
        /// of a video: it is a little longer than its times add up to.
        /// </summary>
        public const double LastFrameSeconds = 0.04;

        /// <summary>Less than this (seconds) apart, an audio and a video count as of the same length.</summary>
        public const double AudioSameWithinSeconds = 1;

        /// <summary>
        /// Photos that are repeated until an audio ends are listed until they are this much (seconds) longer than it, so that the audio
        /// ends the video, and not they.
        /// </summary>
        internal const double RepeatMarginSeconds = 1;

        /// <summary>The list of a video can be no longer than this: an hour of audio over a single photo of 0.04 s would list it 90,000 times.</summary>
        internal const int MaxListedFiles = 1_000_000;

        /// <summary>
        /// For random photo times: a position in the range for each of <paramref name="count"/> photos, from 0 (the shortest time) up to
        /// but not including 1 (the longest). Drawn once, and kept, they give the same times again whatever the range is changed to, so
        /// that what is shown about the video is what the video gets.
        /// </summary>
        public static double[] DrawRandomSamples(int count, Random? random = null)
        {
            random ??= Random.Shared;
            var samples = new double[Math.Max(0, count)];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = random.NextDouble();

            return samples;
        }

        /// <summary>
        /// The time of each photo (not the cover). Without <paramref name="randomSamples"/> they go in equal steps from the start to the
        /// end time (one photo gets the start time). With them, photo <c>i</c> gets the time at its position <c>randomSamples[i]</c> between
        /// the smaller and the larger of the two (given either way round); a photo that has no sample gets a fresh random one.
        /// </summary>
        public static IReadOnlyList<double> BuildFrameDurations(
            int photoCount, double startFrameTimeSeconds, double endFrameTimeSeconds, IReadOnlyList<double>? randomSamples = null)
        {
            if (photoCount <= 0)
                return [];

            if (randomSamples is not null)
            {
                var shortest = Math.Min(startFrameTimeSeconds, endFrameTimeSeconds);
                var longest = Math.Max(startFrameTimeSeconds, endFrameTimeSeconds);
                var randomDurations = new double[photoCount];
                for (var i = 0; i < photoCount; i++)
                    randomDurations[i] = shortest + ((i < randomSamples.Count ? randomSamples[i] : Random.Shared.NextDouble()) * (longest - shortest));

                return randomDurations;
            }

            if (photoCount == 1)
                return [startFrameTimeSeconds];

            var durations = new List<double>(photoCount);
            var step = (endFrameTimeSeconds - startFrameTimeSeconds) / (photoCount - 1);

            for (var i = 0; i < photoCount; i++)
                durations.Add(startFrameTimeSeconds + (step * i));

            return durations;
        }

        /// <summary>
        /// How long the photos take (not the cover), once through: what the times of <see cref="BuildFrameDurations"/> add up to, worked
        /// out without making them (there can be tens of thousands of photos, and the dialog asks at every change of a setting). A
        /// photo that has no sample is counted at the middle of the range.
        /// </summary>
        public static double PhotoSeconds(
            int photoCount, double startFrameTimeSeconds, double endFrameTimeSeconds, IReadOnlyList<double>? randomSamples = null)
        {
            if (photoCount <= 0)
                return 0;

            if (randomSamples is not null)
            {
                var shortest = Math.Min(startFrameTimeSeconds, endFrameTimeSeconds);
                var longest = Math.Max(startFrameTimeSeconds, endFrameTimeSeconds);
                var positions = 0d;
                for (var i = 0; i < photoCount; i++)
                    positions += i < randomSamples.Count ? randomSamples[i] : 0.5;

                return (photoCount * shortest) + (positions * (longest - shortest));
            }

            // Equal steps from start to end add up to the middle time times their number; one photo is shown for the start time
            return photoCount == 1 ? startFrameTimeSeconds : photoCount * (startFrameTimeSeconds + endFrameTimeSeconds) / 2;
        }

        /// <summary>
        /// Whether the photos are repeated for an audio: they are asked to be, the audio is longer than the cover and the photos once
        /// through, and there are photos to repeat.
        /// </summary>
        private static bool RepeatsForAudio(bool repeatToAudio, double? audioSeconds, double onceSeconds, double photoPassSeconds) =>
            repeatToAudio && audioSeconds is { } audio && audio > onceSeconds && photoPassSeconds > 0;

        /// <summary>
        /// How long the video will be with these settings, as <see cref="BuildSequence"/> would make it, but worked out without making
        /// it. Not exact where the photos are not the ones counted: files that have gone from the disk by the time it is made.
        /// </summary>
        /// <param name="photoCount">The photos (not counting the cover).</param>
        /// <param name="coverSeconds">Null for no cover.</param>
        /// <param name="repeatToAudio">The photos play over and over until the audio ends (when it is longer than they are).</param>
        /// <param name="audioSeconds">The length of the audio, if there is one and it is known.</param>
        public static double EstimateLength(
            int photoCount,
            double startFrameTimeSeconds,
            double endFrameTimeSeconds,
            IReadOnlyList<double>? randomSamples,
            double? coverSeconds,
            bool repeatToAudio,
            double? audioSeconds)
        {
            var photoPass = PhotoSeconds(photoCount, startFrameTimeSeconds, endFrameTimeSeconds, randomSamples);
            var once = (coverSeconds ?? 0) + photoPass;
            if (RepeatsForAudio(repeatToAudio, audioSeconds, once, photoPass))
                return audioSeconds!.Value;

            return photoCount <= 0 && coverSeconds is null ? 0 : once + LastFrameSeconds;
        }

        /// <summary>
        /// What happens to the audio of a video that plays its photos once, which is as long as <paramref name="videoSeconds"/>: cut,
        /// played over and over, or about as long as the video.
        /// </summary>
        public static AudioFit FitOfAudio(double videoSeconds, double audioSeconds)
        {
            if (Math.Abs(audioSeconds - videoSeconds) < AudioSameWithinSeconds)
                return AudioFit.Same;

            return audioSeconds > videoSeconds ? AudioFit.Cut : AudioFit.Repeated;
        }

        /// <summary>
        /// Puts the pictures of a video in order with the time of each: the cover first (when there is one), then the photos, in the
        /// order given. With <paramref name="repeatToAudio"/> and an audio that is longer than all of that, the photos (not the cover)
        /// are listed again and again, with the same times, until they are longer than the audio, which is played once and ends the video.
        /// Otherwise the pictures are listed once and end the video, and the audio, if any, is played over and over to fill it
        /// (<paramref name="repeatAudio"/>), or once, with silence after it if it is shorter.
        /// </summary>
        /// <param name="photoFiles">The photos, in the order they are shown, upright and of one kind (see <see cref="BuildFfmpegArguments"/>).</param>
        /// <param name="coverFile">The cover picture, or null for none.</param>
        /// <param name="randomSamples">Null: the photos go from the start to the end time in steps. Otherwise see <see cref="BuildFrameDurations"/>.</param>
        /// <param name="repeatToAudio">An audio that is longer than the pictures once through is not cut: the photos play over and over until it ends.</param>
        /// <param name="repeatAudio">An audio that is shorter than the pictures is played over and over to fill them (otherwise once, and then silence).</param>
        /// <param name="audioSeconds">The length of the audio; null when there is none or it is not known (then no photo is repeated for it).</param>
        /// <exception cref="InvalidOperationException">The photos would have to be listed more than <see cref="MaxListedFiles"/> times in all.</exception>
        public static VideoSequence BuildSequence(
            IReadOnlyList<string> photoFiles,
            string? coverFile,
            double coverSeconds,
            double startFrameTimeSeconds,
            double endFrameTimeSeconds,
            IReadOnlyList<double>? randomSamples,
            bool repeatToAudio,
            bool repeatAudio,
            double? audioSeconds)
        {
            var photoDurations = BuildFrameDurations(photoFiles.Count, startFrameTimeSeconds, endFrameTimeSeconds, randomSamples);
            var files = new List<string>(photoFiles.Count + 1);
            var durations = new List<double>(photoFiles.Count + 1);
            if (coverFile is not null)
            {
                files.Add(coverFile);
                durations.Add(coverSeconds);
            }

            files.AddRange(photoFiles);
            durations.AddRange(photoDurations);

            var once = durations.Sum();
            var photoPass = photoDurations.Sum();
            if (!RepeatsForAudio(repeatToAudio, audioSeconds, once, photoPass))
            {
                return new VideoSequence(
                    files, durations, once + LastFrameSeconds, repeatAudio ? AudioPlayback.Loop : AudioPlayback.OnceThenSilence);
            }

            var audio = audioSeconds!.Value;
            var morePasses = Math.Ceiling((audio + RepeatMarginSeconds - once) / photoPass);
            if ((morePasses * photoFiles.Count) + files.Count > MaxListedFiles)
                throw new InvalidOperationException($"The photos would have to be repeated {morePasses:N0} times to last as long as the audio: too many. Let them play once.");

            for (var pass = 0; pass < morePasses; pass++)
            {
                files.AddRange(photoFiles);
                durations.AddRange(photoDurations);
            }

            return new VideoSequence(files, durations, audio, AudioPlayback.OnceEndsVideo);
        }
    }
}
