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
        /// Makes the picture of a file for a video of this size, that is to be shown for <paramref name="seconds"/> (with the change that brings it in, if the video has changes: see
        /// <see cref="IVideoChanges"/>): reads it, and works out whatever it needs to move. <paramref name="index"/> is its place in the <see cref="VideoSequence"/> (a file that is in it twice is two
        /// pictures); <paramref name="still"/> is a picture that does not move (the cover). Called on worker threads, several at a time, for pictures a little ahead of the one that is drawn;
        /// one that can not be made (the file is gone or damaged) throws, and its time is black.
        /// </summary>
        IVideoPicture Open(int index, string file, int width, int height, double seconds, bool still);
    }

    /// <summary>
    /// How a video that is drawn frame by frame goes from one picture to the next: a dissolve, a slide, a wipe. A change takes the last frames of the picture that goes (so the video is as long as it
    /// is without changes, and the picture that comes is whole from the moment of its own time), and in them both pictures are drawn, the one that goes as it goes on, the one that comes as it begins,
    /// and put together by <see cref="Mix"/>. A video without changes cuts from one picture to the next.
    /// </summary>
    public interface IVideoChanges
    {
        /// <summary>
        /// How long the change that brings in the picture <paramref name="index"/> (1 or more: the first picture is not brought in) takes, in seconds: 0 for a cut. <paramref name="pictureSeconds"/> is the
        /// shorter of the times of the picture that goes and the one that comes. A change is never longer than the picture that goes is shown.
        /// </summary>
        double Seconds(int index, double pictureSeconds);

        /// <summary>
        /// Puts the change that brings in the picture <paramref name="index"/>, <paramref name="progress"/> of the way through (more than 0 and less than 1), into <paramref name="destination"/>: the whole
        /// frames (row after row, 0xAARRGGBB, opaque) of the picture that goes and the one that comes as they are at that moment. <paramref name="destination"/> is none of the other two.
        /// </summary>
        void Mix(int index, double progress, uint[] outgoing, uint[] incoming, uint[] destination);
    }

    /// <summary>
    /// Making a video of pictures that move: ffmpeg is not given the photos as files (it would show each as it is) but the frames, one after the other, on its standard input, which are drawn
    /// here by <see cref="IVideoPicture"/>s (the photos that drift, turn or move by their depth) and, between them, by the <see cref="IVideoChanges"/> (a dissolve, a wipe). Everything else is as
    /// it is for the video of <see cref="VideoGenerationService.GenerateAsync"/>: the pictures, their times, the audio and what it does come from the same <see cref="VideoSequence"/>.
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

        /// <summary>
        /// How many frames the change that brings in each picture of a sequence takes (see <see cref="IVideoChanges"/>): none for the first picture and for every picture of a video without changes, and
        /// never more than the picture that goes has frames, which it takes the last of. <paramref name="frames"/> are those of <see cref="PlanFrames"/>.
        /// </summary>
        public static int[] PlanChanges(IReadOnlyList<int> frames, IReadOnlyList<double> durations, IVideoChanges? changes)
        {
            var lengths = new int[frames.Count];
            if (changes is null)
                return lengths;

            for (var i = 1; i < lengths.Length; i++)
            {
                var seconds = changes.Seconds(i, Math.Min(Math.Max(0, durations[i - 1]), Math.Max(0, durations[i])));
                if (!(seconds > 0))
                    continue; // none, or not a number

                var count = (int)Math.Min(int.MaxValue, Math.Round(seconds * FramesPerSecond, MidpointRounding.AwayFromZero));
                lengths[i] = Math.Clamp(count, 0, frames[i - 1]);
            }
            return lengths;
        }

        /// <param name="sequence">The pictures and their times, as <see cref="VideoGenerationService.BuildSequence"/> makes them.</param>
        /// <param name="stillFiles">The files of <paramref name="sequence"/> that do not move (the cover).</param>
        /// <param name="prepareAhead">How many pictures are made ahead of the one that is drawn (each takes half a second of work to move, and the drawing goes on meanwhile).</param>
        /// <param name="ffmpegPath">The ffmpeg to run; the one that comes with the program when not given.</param>
        /// <param name="changes">How one picture goes over into the next; null cuts (as a video of files does).</param>
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
            int prepareAhead = 3,
            IVideoChanges? changes = null)
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
                await WriteFramesAsync(process.StandardInput.BaseStream, sequence, width, height, pictures, stillFiles, changes, appendOutput, Math.Max(1, prepareAhead), cancellationToken);
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
            Stream output, VideoSequence sequence, int width, int height, IVideoPictureSource pictures, IReadOnlySet<string> stillFiles, IVideoChanges? changes,
            Action<string> appendOutput, int prepareAhead, CancellationToken cancellationToken)
        {
            const int buffers = 3;
            var counts = PlanFrames(sequence.Durations);
            var leads = PlanChanges(counts, sequence.Durations, changes); // the frames of the change that brings each picture in, taken from the end of the picture before it

            // The two pictures of a change are drawn on frames of their own, which the change is made of (only a video that has changes needs them)
            var hasChanges = leads.Any(lead => lead > 0);
            var outgoingFrame = hasChanges ? new uint[width * height] : null;
            var incomingFrame = hasChanges ? new uint[width * height] : null;

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
                    // A picture is on the screen for the change that brings it in as well as for its own time, and moves all that time
                    var seconds = sequence.Durations[index] + leads[index] / (double)FramesPerSecond;
                    return (IVideoPicture?)pictures.Open(index, file, width, height, seconds, stillFiles.Contains(file));
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

            // The picture that is drawn now (null: one that could not be made, which is black), and the one that comes after it once its change has begun
            IVideoPicture? current = null;
            IVideoPicture? incoming = null;
            var haveCurrent = false; // the picture of this index was taken from the queue already, when the change that brings it in began

            try
            {
                Fill();
                for (var index = 0; index < sequence.Files.Count; index++)
                {
                    if (!haveCurrent)
                    {
                        current = await opening.Dequeue();
                        Fill();
                    }
                    haveCurrent = false;

                    // The last frames of this picture are the change that brings in the next one: both are drawn in them, and the picture that comes moves from the first of them
                    var change = index + 1 < sequence.Files.Count ? leads[index + 1] : 0;
                    var firstOfChange = counts[index] - change;
                    var length = leads[index] + counts[index]; // the frames the picture is on the screen for, those of the change that brought it in too
                    var incomingLength = 0;
                    var incomingTaken = false;

                    for (var frame = 0; frame < counts[index]; frame++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var buffer = await free.Reader.ReadAsync(cancellationToken);
                        if (frame < firstOfChange)
                        {
                            Draw(current, leads[index] + frame, length, buffer);
                        }
                        else
                        {
                            if (!incomingTaken)
                            {
                                incoming = await opening.Dequeue();
                                Fill();
                                incomingTaken = true;
                                incomingLength = leads[index + 1] + counts[index + 1];
                            }

                            var step = frame - firstOfChange;
                            Draw(current, leads[index] + frame, length, outgoingFrame!);
                            Draw(incoming, step, incomingLength, incomingFrame!);
                            changes!.Mix(index + 1, (step + 1.0) / (change + 1), outgoingFrame!, incomingFrame!, buffer); // never 0 or 1: those are the two pictures themselves
                        }

                        await filled.Writer.WriteAsync(buffer, cancellationToken);
                    }

                    current?.Dispose();
                    current = null;
                    if (incomingTaken)
                    {
                        current = incoming;
                        incoming = null;
                        haveCurrent = true;
                    }
                }

                filled.Writer.Complete();
                await writer;
            }
            catch
            {
                current?.Dispose();
                incoming?.Dispose();
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

        /// <summary>
        /// Draws one frame of a picture, the <paramref name="step"/>th of the <paramref name="length"/> it is on the screen for (a picture with one frame gets the middle of its movement, 0.5);
        /// a picture that could not be made is black.
        /// </summary>
        private static void Draw(IVideoPicture? picture, int step, int length, uint[] frame)
        {
            if (picture is null)
                Array.Fill(frame, 0xFF000000u);
            else
                picture.Render(length == 1 ? 0.5 : step / (double)(length - 1), frame);
        }
    }
}
