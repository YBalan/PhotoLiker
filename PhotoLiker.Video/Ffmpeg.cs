using System.Diagnostics;
using System.Text;

namespace PhotoLiker.Video
{
    /// <summary>Runs ffmpeg, the one that comes with the program (ffmpeg\ffmpeg.exe next to it), else the one on the PATH.</summary>
    internal static class Ffmpeg
    {
        /// <summary>Another ffmpeg to use (the tests point it at the one in the repository); null for the usual one.</summary>
        internal static string? ExecutableOverride { get; set; }

        internal static string Executable
        {
            get
            {
                if (ExecutableOverride is { Length: > 0 } custom)
                    return custom;

                var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
                return File.Exists(local) ? local : "ffmpeg";
            }
        }

        /// <summary>The result of a run: the exit code, what ffmpeg wrote to its standard output (pictures, when asked for), and its messages.</summary>
        internal sealed record Result(int ExitCode, byte[] Output, string Messages);

        /// <summary>
        /// Runs ffmpeg and waits for it. A run that takes longer than <paramref name="timeout"/> (a share that stopped answering) or is cancelled is
        /// stopped, with whatever it started. Throws <see cref="FileNotFoundException"/> when there is no ffmpeg to run.
        /// </summary>
        internal static async Task<Result> RunAsync(IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo(Executable)
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

            Process process;
            try
            {
                process = Process.Start(start) ?? throw new FileNotFoundException(VideoLibStrings.FfmpegNotFound, start.FileName);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new FileNotFoundException(VideoLibStrings.FfmpegNotFound, start.FileName, ex);
            }

            using (process)
            {
                process.StandardInput.Close();
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(timeout);

                var output = new MemoryStream();
                var copying = process.StandardOutput.BaseStream.CopyToAsync(output, limit.Token);
                var messages = process.StandardError.ReadToEndAsync(limit.Token);
                try
                {
                    await process.WaitForExitAsync(limit.Token);
                    await Task.WhenAll(copying, messages);
                }
                catch (OperationCanceledException)
                {
                    Stop(process);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"ffmpeg did not finish in {timeout.TotalSeconds:0} s.");
                }
                return new Result(process.ExitCode, output.ToArray(), messages.Result);
            }
        }

        /// <summary>
        /// Starts ffmpeg and hands the process over, for a run whose output is read as it comes (the pictures of a video, one after the other):
        /// the caller reads <c>StandardOutput</c>, handles the messages and disposes the process. It runs at a low priority, like all the analysis, and
        /// nothing it reads is typed in. Throws <see cref="FileNotFoundException"/> when there is no ffmpeg to run.
        /// </summary>
        internal static Process Start(IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            try
            {
                var process = Process.Start(start) ?? throw new FileNotFoundException(VideoLibStrings.FfmpegNotFound, start.FileName);
                process.StandardInput.Close();
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* it ended already */ }
                return process;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new FileNotFoundException(VideoLibStrings.FfmpegNotFound, start.FileName, ex);
            }
        }

        internal static void Kill(Process process) => Stop(process);

        private static void Stop(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // it ended by itself in the meantime
            }
        }
    }
}
