namespace PhotoLiker.Core
{
    public static class VideoGenerationService
    {
        /// <summary>
        /// The photos in the order they appear in the video: by the date the file was created like the folder list
        /// (<see cref="PhotoSortOrder"/>), by their names (<see cref="PhotoNameOrder"/>), or as they are given. Search results come
        /// without a created date, so for them it is the last-write date. Photos of one date go by their path.
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

        public static async Task GenerateAsync(
            IReadOnlyList<string> sourceFiles,
            string outputPath,
            int width,
            int height,
            double startFrameTimeSeconds,
            double endFrameTimeSeconds,
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

                var frameDurations = BuildFrameDurations(sourceFiles.Count, startFrameTimeSeconds, endFrameTimeSeconds);
                var concatContent = BuildFfmpegConcatFile(sourceFiles, frameDurations);
                File.WriteAllText(listFilePath, concatContent);

                var arguments = BuildFfmpegArguments(listFilePath, outputPath, width, height, codec, crf, audioPath);
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

                double totalDurationMs = frameDurations.Sum() * 1000d;

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
        /// </summary>
        /// <param name="audioPath">
        /// The sound of the video, or null for none. It is played over and over (<c>-stream_loop -1</c>) when it is shorter than the
        /// video, and <c>-shortest</c> ends the video with the photos, so a longer one is cut. Only its sound is taken (not the cover
        /// picture of a music file, which is a video stream to ffmpeg), and the photos only from the list.
        /// </param>
        internal static string BuildFfmpegArguments(string listFilePath, string outputPath, int width, int height, string codec, int crf, string? audioPath = null)
        {
            var scaleFilter = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
            var hasAudio = !string.IsNullOrWhiteSpace(audioPath);
            var audioInput = hasAudio ? $" -stream_loop -1 -i \"{audioPath}\"" : string.Empty;
            var audioOutput = hasAudio ? " -map 0:v:0 -map 1:a:0 -c:a aac -b:a 192k -shortest" : string.Empty;

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

        internal static IReadOnlyList<double> BuildFrameDurations(int sourceFileCount, double startFrameTimeSeconds, double endFrameTimeSeconds)
        {
            if (sourceFileCount <= 0)
                return [];

            if (sourceFileCount == 1)
                return [startFrameTimeSeconds];

            var durations = new List<double>(sourceFileCount);
            var step = (endFrameTimeSeconds - startFrameTimeSeconds) / (sourceFileCount - 1);

            for (var i = 0; i < sourceFileCount; i++)
                durations.Add(startFrameTimeSeconds + (step * i));

            return durations;
        }
    }
}
