namespace PhotoLiker.Core
{
    /// <summary>
    /// Turns the audio file chosen for a video into a plain WAV file that can be listened to, with the same ffmpeg that makes the
    /// video: whatever it can put into a video can be played, and what is played is what it will read.
    /// </summary>
    public static class AudioDecoder
    {
        /// <summary>A longer file is cut here: a WAV is about 10 MB per minute, and it is only to have a listen.</summary>
        public const int MaxPreviewSeconds = 600;

        /// <summary>The WAV has this format: 44.1 kHz, 2 channels, 16 bits.</summary>
        public const int WavBytesPerSecond = 44100 * 2 * 2;

        /// <summary>No output is given: ffmpeg only reads the file and tells about it (and ends with an error that it has nowhere to write).</summary>
        internal static string BuildDurationArguments(string audioPath) => $"-hide_banner -i \"{audioPath}\"";

        /// <summary>The length ffmpeg tells for its input, <c>Duration: 00:03:25.12, start: ...</c>; null when it tells none (<c>N/A</c>) or the text has no such line.</summary>
        internal static TimeSpan? ParseDuration(string ffmpegOutput)
        {
            // The first one is the file's; tags of a file can have a duration of their own, but are written "DURATION   : ..."
            var match = System.Text.RegularExpressions.Regex.Match(ffmpegOutput, @"\bDuration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
            if (!match.Success)
                return null;

            return TimeSpan.FromHours(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                + TimeSpan.FromMinutes(int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
                + TimeSpan.FromSeconds(double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>How long the audio file is, as ffmpeg sees it; null when that cannot be told (not a media file, or no length in it).</summary>
        /// <exception cref="InvalidOperationException">ffmpeg is not there.</exception>
        public static async Task<TimeSpan?> ReadDurationAsync(string audioPath, CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = VideoGenerationService.ResolveFfmpegExecutablePath(),
                    Arguments = BuildDurationArguments(audioPath),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(VideoGenerationService.FfmpegNotFoundMessage, ex);
            }

            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* it ended by itself */ }
                throw;
            }

            // Its exit code says nothing here (it is an error without an output file): the text does
            return ParseDuration(await error);
        }

        internal static string BuildArguments(string audioPath, string wavPath) =>
            $"-y -hide_banner -loglevel error -i \"{audioPath}\" -vn -t {MaxPreviewSeconds} -ac 2 -ar 44100 -c:a pcm_s16le \"{wavPath}\"";

        /// <exception cref="InvalidOperationException">ffmpeg is not there, or cannot read the file as audio.</exception>
        public static async Task ToWavAsync(string audioPath, string wavPath, CancellationToken cancellationToken)
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = VideoGenerationService.ResolveFfmpegExecutablePath(),
                    Arguments = BuildArguments(audioPath, wavPath),
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(VideoGenerationService.FfmpegNotFoundMessage, ex);
            }

            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* it ended by itself */ }
                throw;
            }

            if (process.ExitCode != 0)
            {
                var lastLine = (await error).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                throw new InvalidOperationException($"ffmpeg cannot read this file as audio{(lastLine is null ? "." : ": " + lastLine)}");
            }
        }
    }
}
