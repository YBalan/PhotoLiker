namespace PhotoLikerUI
{
    internal static class VideoGenerationService
    {
        public static List<string> SortSourceFiles(IReadOnlyCollection<string> sourceFiles, bool reverseSourceFiles)
        {
            var ordered = sourceFiles.ToList();
            if (reverseSourceFiles)
                ordered.Reverse();

            return ordered;
        }

        public static async Task GenerateAsync(
            IReadOnlyList<string> sourceFiles,
            string outputPath,
            int width,
            int height,
            double startFrameTimeSeconds,
            double endFrameTimeSeconds,
            string codec,
            int crf,
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

                var scaleFilter = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2";
                var arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" -vf \"{scaleFilter}\" -c:v {codec} -crf {crf} -pix_fmt yuv420p -progress pipe:1 -nostats \"{outputPath}\"";
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
                throw new InvalidOperationException("ffmpeg executable not found. Place ffmpeg.exe in the 'ffmpeg' subfolder next to the app, or ensure ffmpeg is available in PATH.", ex);
            }
            finally
            {
                if (File.Exists(listFilePath))
                    File.Delete(listFilePath);
            }
        }

        private static string ResolveFfmpegExecutablePath()
        {
            var localFfmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(localFfmpegPath))
                return localFfmpegPath;

            return "ffmpeg";
        }

        private static bool TryParseFfmpegProgressPercent(string line, double totalDurationMs, out int percent)
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

        private static string BuildFfmpegConcatFile(IReadOnlyList<string> sourceFiles, IReadOnlyList<double> frameDurations)
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

        private static IReadOnlyList<double> BuildFrameDurations(int sourceFileCount, double startFrameTimeSeconds, double endFrameTimeSeconds)
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
