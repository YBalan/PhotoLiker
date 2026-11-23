using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PhotoLiker.Stickers
{
    /// <summary>A sticker as the bytes of its file, and how it was made.</summary>
    /// <param name="Quality">For a WebP, the quality it was written at (less than asked for when the first try was too big); 100 for a PNG, which has none.</param>
    public sealed record EncodedSticker(byte[] Bytes, StickerFormat Format, int Quality)
    {
        /// <summary>Whether the file is under the 512 KB Telegram allows.</summary>
        public bool Fits => Bytes.Length <= StickerFile.MaxBytes;
    }

    /// <summary>
    /// Writes the picture of a sticker as the file Telegram takes. A PNG is written here (<see cref="PngWriter"/>), a WebP by the ffmpeg that comes with the program (its libwebp, lossy, with the
    /// alpha kept exactly). A WebP that comes out over the limit is written again at a lower quality until it fits.
    /// </summary>
    public static class StickerEncoder
    {
        /// <summary>How much the quality is lowered each time the file is over the limit.</summary>
        internal const int QualityStep = 10;

        /// <summary>The ffmpeg that comes with the program (ffmpeg\ffmpeg.exe beside it), else the one on the PATH.</summary>
        public static string ResolveFfmpeg()
        {
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            return File.Exists(local) ? local : "ffmpeg";
        }

        /// <summary>The bytes of a PNG of the picture.</summary>
        public static byte[] ToPng(StickerPicture picture) => PngWriter.Write(picture);

        /// <summary>
        /// The picture as a file of <paramref name="format"/>. A WebP of more than 512 KB is written again at a lower quality (down to 1); what comes back says what it ended as, and whether it fits
        /// (a PNG is not made smaller: it is exactly what the picture is, and <see cref="EncodedSticker.Fits"/> says when it is too big).
        /// </summary>
        /// <param name="quality">1 to 100; only a WebP has one.</param>
        /// <param name="ffmpegPath">The ffmpeg to run for a WebP; the one that comes with the program when not given.</param>
        /// <exception cref="StickerException">ffmpeg is not there, or stopped with an error, or no WebP came.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled (ffmpeg is stopped).</exception>
        public static async Task<EncodedSticker> EncodeAsync(
            StickerPicture picture, StickerFormat format, int quality, CancellationToken cancellationToken = default, string? ffmpegPath = null)
        {
            ArgumentNullException.ThrowIfNull(picture);
            if (format == StickerFormat.Png)
                return new EncodedSticker(ToPng(picture), StickerFormat.Png, 100);

            quality = Math.Clamp(quality, StickerSettings.MinQuality, StickerSettings.MaxQuality);
            var ffmpeg = ffmpegPath ?? ResolveFfmpeg();
            var id = Guid.NewGuid().ToString("N")[..12];
            var source = Path.Combine(Path.GetTempPath(), $"photoliker_sticker_{id}.png");
            var target = Path.Combine(Path.GetTempPath(), $"photoliker_sticker_{id}.webp");
            try
            {
                await File.WriteAllBytesAsync(source, ToPng(picture), cancellationToken);

                while (true)
                {
                    await RunAsync(ffmpeg, WebPArguments(source, target, quality), cancellationToken);
                    if (!File.Exists(target) || new FileInfo(target).Length == 0)
                        throw new StickerException(StickerFailure.NoOutput, target);

                    var bytes = await File.ReadAllBytesAsync(target, cancellationToken);
                    if (bytes.Length <= StickerFile.MaxBytes || quality <= StickerSettings.MinQuality)
                        return new EncodedSticker(bytes, StickerFormat.WebP, quality);

                    quality = Math.Max(StickerSettings.MinQuality, quality - QualityStep);
                }
            }
            finally
            {
                TryDelete(source);
                TryDelete(target);
            }
        }

        /// <summary>The arguments that make a WebP of a PNG: lossy at <paramref name="quality"/>, the slowest and smallest search (a sticker is one small picture, so the time does not matter).</summary>
        internal static string[] WebPArguments(string source, string target, int quality) =>
        [
            "-y", "-hide_banner", "-loglevel", "error", "-nostdin",
            "-i", source,
            "-c:v", "libwebp", "-lossless", "0",
            "-quality", quality.ToString(CultureInfo.InvariantCulture),
            "-compression_level", "6",
            "-frames:v", "1",
            target,
        ];

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

        private static async Task RunAsync(string ffmpeg, string[] arguments, CancellationToken cancellationToken)
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
                process = Process.Start(start) ?? throw new StickerException(StickerFailure.FfmpegNotFound, ffmpeg);
            }
            catch (Win32Exception ex)
            {
                throw new StickerException(StickerFailure.FfmpegNotFound, ffmpeg, ex);
            }

            using (process)
            {
                process.StandardInput.Close();
                using var stop = cancellationToken.Register(() => Kill(process));

                var output = process.StandardOutput.ReadToEndAsync();
                var messages = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                await Task.WhenAll(output, messages);

                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                    throw new StickerException(StickerFailure.FfmpegFailed, LastMessage(messages.Result, process.ExitCode));
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
    }
}
