using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PhotoLiker.VideoToGif
{
    /// <summary>The two parts of the work: finding the colours, then drawing the frames.</summary>
    public enum GifPhase
    {
        /// <summary>The palette is found. ffmpeg has to read all the frames to know the best colours and says nothing until it has, so there is no fraction to report.</summary>
        Palette,

        /// <summary>The frames are drawn and written; the fraction is how much of the part is done.</summary>
        Frames,
    }

    /// <summary>How far the work is. <paramref name="Fraction"/> is 0 to 1 and only means something in <see cref="GifPhase.Frames"/>.</summary>
    public readonly record struct GifProgress(GifPhase Phase, double Fraction);

    /// <summary>A GIF that was made: where it is, how big the file is, and the plan it was made by (frames, size).</summary>
    public sealed record GifResult(string Path, long Bytes, GifPlan Plan);

    /// <summary>Makes the GIF: runs ffmpeg twice (the palette, then the frames), and puts the file in place only when it is whole.</summary>
    public static class GifMaker
    {
        /// <summary>The ffmpeg that comes with the program (ffmpeg\ffmpeg.exe beside it), else the one on the PATH.</summary>
        public static string ResolveFfmpeg()
        {
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            return File.Exists(local) ? local : "ffmpeg";
        }

        /// <summary>
        /// Makes <paramref name="gifPath"/> (a file that is there is replaced) from the part of the video that <paramref name="range"/> says. Nothing is left behind when it fails or is stopped:
        /// the GIF is written under another name and moved into place when it is whole, so a GIF that was there before stays until a new one is ready.
        /// </summary>
        /// <param name="clip">What is known of the video (its length, frame rate and shown size).</param>
        /// <param name="progress">Told where the work is, from any thread.</param>
        /// <param name="ffmpegPath">The ffmpeg to run; the one that comes with the program when not given.</param>
        /// <exception cref="GifException">ffmpeg is not there, or it stopped with an error, or no GIF came.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled (ffmpeg is stopped).</exception>
        public static async Task<GifResult> MakeAsync(
            string videoPath, string gifPath, GifClip clip, GifRange range, GifSettings settings,
            IProgress<GifProgress>? progress = null, CancellationToken cancellationToken = default, string? ffmpegPath = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(gifPath);

            var plan = GifPlan.Make(clip, range, settings);
            var ffmpeg = ffmpegPath ?? ResolveFfmpeg();
            var target = Path.GetFullPath(gifPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var id = Guid.NewGuid().ToString("N")[..12];
            var palette = Path.Combine(Path.GetTempPath(), $"photoliker_gif_{id}.png");
            var partial = $"{target}.{id}.part";
            try
            {
                progress?.Report(new GifProgress(GifPhase.Palette, 0));
                await RunAsync(ffmpeg, plan.PaletteArguments(videoPath, palette), 0, null, cancellationToken);
                if (!File.Exists(palette) || new FileInfo(palette).Length == 0)
                    throw new GifException(GifFailure.NoOutput, videoPath);

                progress?.Report(new GifProgress(GifPhase.Frames, 0));
                await RunAsync(ffmpeg, plan.GifArguments(videoPath, palette, partial), plan.PlaySeconds, progress, cancellationToken);
                if (!File.Exists(partial) || new FileInfo(partial).Length == 0)
                    throw new GifException(GifFailure.NoOutput, target);

                File.Move(partial, target, overwrite: true);
                progress?.Report(new GifProgress(GifPhase.Frames, 1));
                return new GifResult(target, new FileInfo(target).Length, plan);
            }
            finally
            {
                TryDelete(palette);
                TryDelete(partial);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file in the temporary folder that stays is not worth failing for
            }
        }

        private static async Task RunAsync(string ffmpeg, string[] arguments, double totalSeconds, IProgress<GifProgress>? progress, CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true, // closed at once: ffmpeg must never wait for a key
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            cancellationToken.ThrowIfCancellationRequested();
            Process process;
            try
            {
                process = Process.Start(start) ?? throw new GifException(GifFailure.FfmpegNotFound, ffmpeg);
            }
            catch (Win32Exception ex)
            {
                throw new GifException(GifFailure.FfmpegNotFound, ffmpeg, ex);
            }

            using (process)
            {
                process.StandardInput.Close();
                using var stop = cancellationToken.Register(() => Kill(process));

                var reading = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
                    {
                        if (progress is not null && TryParseProgress(line, totalSeconds, out var fraction))
                            progress.Report(new GifProgress(GifPhase.Frames, fraction));
                    }
                });
                var messages = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();
                await Task.WhenAll(reading, messages);

                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                    throw new GifException(GifFailure.FfmpegFailed, LastMessage(messages.Result, process.ExitCode));
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // It ended by itself in the meantime
            }
        }

        /// <summary>The last thing ffmpeg wrote, which is the reason it stopped; its exit code when it wrote nothing.</summary>
        internal static string LastMessage(string messages, int exitCode) =>
            messages.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? $"exit code {exitCode}";

        /// <summary>
        /// How far a line of the progress report says the work is: <c>out_time_us=2500000</c> (and <c>out_time_ms</c>, which ffmpeg also writes, in microseconds as well) is the time of the GIF written so far,
        /// out of <paramref name="totalSeconds"/>; <c>progress=end</c> is all of it. Any other line (and the <c>N/A</c> before the first frame) is not a position.
        /// </summary>
        internal static bool TryParseProgress(string line, double totalSeconds, out double fraction)
        {
            fraction = 0;
            line = line.Trim();
            if (line.Equals("progress=end", StringComparison.Ordinal))
            {
                fraction = 1;
                return true;
            }

            foreach (var key in new[] { "out_time_us=", "out_time_ms=" })
            {
                if (!line.StartsWith(key, StringComparison.Ordinal))
                    continue;

                if (totalSeconds <= 0 || !long.TryParse(line[key.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                    return false;

                fraction = Math.Clamp(microseconds / 1_000_000.0 / totalSeconds, 0, 1);
                return true;
            }

            return false;
        }
    }
}
