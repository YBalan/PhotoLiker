using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace PhotoLiker.GenerateVideo
{
    /// <summary>
    /// One picture of a video that is drawn frame by frame (a photo that moves while it is shown, see <see cref="VideoFrames"/>), as opposed to one that ffmpeg is given as a file and shows
    /// as it is. It is made for the size of the video, so it is drawn as it is.
    /// </summary>
    public interface IVideoPicture : IDisposable
    {
        /// <summary>
        /// Draws the picture as it is <paramref name="progress"/> of the way through the time it is shown (0 at its first frame, 1 at its last; a picture with one frame gets 0.5) on the whole
        /// frame: the pixels of the video, row after row, 0xAARRGGBB, all of them (what is not the picture is black), opaque. Called one frame after the other (never two at once, though not always
        /// on the same thread).
        /// </summary>
        void Render(double progress, uint[] frame);
    }

    /// <summary>Makes the pictures that <see cref="VideoFrames.GenerateAsync"/> draws.</summary>
    public interface IVideoPictureSource
    {
        /// <summary>
        /// Makes the picture of a file for a video of this size, that is to be shown for <paramref name="seconds"/>: reads it, and works out whatever it needs to move. <paramref name="still"/>
        /// is a picture that does not move (the cover). Called on worker threads, several at a time, for pictures a little ahead of the one that is drawn; one that can not be made
        /// (the file is gone or damaged) throws, and its time is black.
        /// </summary>
        IVideoPicture Open(string file, int width, int height, double seconds, bool still);
    }

    /// <summary>
    /// Making a video of pictures that move: ffmpeg is not given the photos as files (it would show each as it is) but the frames, one after the other, on its standard input, which are drawn
    /// here by <see cref="IVideoPicture"/>s: the depth parallax of the photos, for one. Everything else is as it is for the video of <see cref="VideoGenerationService.GenerateAsync"/>:
    /// the pictures, their times, the audio and what it does come from the same <see cref="VideoSequence"/>.
    /// </summary>
    public static class VideoFrames
    {
        /// <summary>Frames for every second of the video: what ffmpeg makes of photos given as files, so a video of moving photos plays as smoothly as the screen of a slideshow at its slowest.</summary>
        public const int FramesPerSecond = 25;

        /// <summary>
        /// How many frames each picture of a sequence has: its time at <see cref="FramesPerSecond"/>, counted from the start of the video, so that the rounding of a picture does not add up
        /// (a hundred pictures of 0.5 s are 12.5 frames each, and the video ends where it should) and never fewer than one. The last picture has one frame more, as a video of files ends with one
        /// (<see cref="VideoGenerationService.LastFrameSeconds"/>).
        /// </summary>
        public static int[] PlanFrames(IReadOnlyList<double> durations)
        {
            var frames = new int[durations.Count];
            double elapsed = 0;
            long made = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                elapsed += Math.Max(0, durations[i]);
                var end = (long)Math.Round(elapsed * FramesPerSecond, MidpointRounding.AwayFromZero);
                if (i == frames.Length - 1)
                    end++;

                frames[i] = (int)Math.Min(int.MaxValue, Math.Max(1, end - made));
                made += frames[i];
            }
            return frames;
        }

        /// <param name="sequence">The pictures and their times, as <see cref="VideoGenerationService.BuildSequence"/> makes them.</param>
        /// <param name="stillFiles">The files of <paramref name="sequence"/> that do not move (the cover).</param>
        /// <param name="prepareAhead">How many pictures are made ahead of the one that is drawn (each takes half a second of work to move, and the drawing goes on meanwhile).</param>
        /// <param name="ffmpegPath">The ffmpeg to run; the one that comes with the program when not given.</param>
        public static async Task GenerateAsync(
            VideoSequence sequence,
            string outputPath,
            int width,
            int height,
            string codec,
            int crf,
            string? audioPath,
            IVideoPictureSource pictures,
            IReadOnlySet<string> stillFiles,
            Action<string> appendOutput,
            Action<int> reportProgress,
            CancellationToken cancellationToken = default,
            string? ffmpegPath = null,
            int prepareAhead = 3)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            var arguments = VideoGenerationService.BuildPipedFfmpegArguments(outputPath, width, height, FramesPerSecond, codec, crf, audioPath, sequence.Audio, sequence.AudioEndSeconds);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath ?? VideoGenerationService.ResolveFfmpegExecutablePath(),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
            };

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(VideoGenerationService.FfmpegNotFoundMessage, ex);
            }

            var totalMs = sequence.ExpectedSeconds * 1000d;
            var stdOut = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(CancellationToken.None)) is not null)
                {
                    appendOutput(line);
                    if (VideoGenerationService.TryParseFfmpegProgressPercent(line, totalMs, out var percent))
                        reportProgress(percent);
                }
            }, CancellationToken.None);
            var stdErr = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync(CancellationToken.None)) is not null)
                    appendOutput(line);
            }, CancellationToken.None);

            try
            {
                await WriteFramesAsync(process.StandardInput.BaseStream, sequence, width, height, pictures, stillFiles, appendOutput, Math.Max(1, prepareAhead), cancellationToken);
                process.StandardInput.Close(); // that is the end of the video for ffmpeg
            }
            catch (IOException) when (process.WaitForExit(5000))
            {
                // ffmpeg gave up before all the frames were written (a codec it does not have, an output it can not write): its own message and exit code say why, below
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* it has ended */ }
                throw;
            }

            await Task.WhenAll(stdOut, stdErr, process.WaitForExitAsync(CancellationToken.None));

            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.Format(GenerateVideoStrings.FfmpegFailedFormat, process.ExitCode));
            if (!File.Exists(outputPath))
                throw new FileNotFoundException(string.Format(GenerateVideoStrings.FfmpegNoOutputFormat, outputPath), outputPath);
        }

        /// <summary>
        /// Draws the frames of every picture one after the other and writes them: the pictures are made a few ahead (on the pool), and the drawing and the writing go on side by side, with a
        /// few frames between them (a frame of full HD is 8 MB, and ffmpeg takes what it can encode), so neither waits for the other more than it must.
        /// </summary>
        private static async Task WriteFramesAsync(
            Stream output, VideoSequence sequence, int width, int height, IVideoPictureSource pictures, IReadOnlySet<string> stillFiles,
            Action<string> appendOutput, int prepareAhead, CancellationToken cancellationToken)
        {
            const int buffers = 3;
            var counts = PlanFrames(sequence.Durations);
            var free = Channel.CreateBounded<uint[]>(buffers);
            var filled = Channel.CreateBounded<uint[]>(buffers);
            for (var i = 0; i < buffers; i++)
                free.Writer.TryWrite(new uint[width * height]);

            var writer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var buffer in filled.Reader.ReadAllAsync(CancellationToken.None))
                    {
                        output.Write(MemoryMarshal.AsBytes(buffer.AsSpan()));
                        await free.Writer.WriteAsync(buffer, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    // The drawing waits for a free frame, or for room to put one down: it must learn that none will come
                    free.Writer.TryComplete(ex);
                    filled.Writer.TryComplete(ex);
                    throw;
                }
            }, CancellationToken.None);

            var opening = new Queue<Task<IVideoPicture?>>();
            var next = 0;

            Task<IVideoPicture?> Open(int index) => Task.Run(() =>
            {
                var file = sequence.Files[index];
                try
                {
                    return (IVideoPicture?)pictures.Open(file, width, height, sequence.Durations[index], stillFiles.Contains(file));
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    appendOutput(string.Format(GenerateVideoStrings.PictureFailedFormat, file, ex.Message));
                    return null; // its time is black
                }
            }, CancellationToken.None);

            void Fill()
            {
                while (opening.Count < prepareAhead && next < sequence.Files.Count)
                    opening.Enqueue(Open(next++));
            }

            try
            {
                Fill();
                for (var index = 0; index < sequence.Files.Count; index++)
                {
                    var picture = await opening.Dequeue();
                    Fill();

                    using (picture)
                    {
                        for (var frame = 0; frame < counts[index]; frame++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var buffer = await free.Reader.ReadAsync(cancellationToken);
                            if (picture is null)
                                Array.Fill(buffer, 0xFF000000u);
                            else
                                picture.Render(counts[index] == 1 ? 0.5 : frame / (double)(counts[index] - 1), buffer);

                            await filled.Writer.WriteAsync(buffer, cancellationToken);
                        }
                    }
                }

                filled.Writer.Complete();
                await writer;
            }
            catch
            {
                filled.Writer.TryComplete();
                Exception? writeError = null;
                try { await writer; } catch (Exception ex) { writeError = ex; }
                foreach (var task in opening)
                {
                    try { (await task)?.Dispose(); } catch { /* nobody wants it any more */ }
                }

                // A writer that failed is why the drawing stopped (the drawing only sees that its channels are closed): that is the failure to report
                if (writeError is IOException)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(writeError).Throw();
                throw;
            }
        }
    }
}
