using System.Diagnostics;

namespace PhotoLiker.GenerateVideo.Tests;

/// <summary>Making a video from frames that are drawn one by one and given to ffmpeg on its standard input (the depth parallax of the photos is one such picture).</summary>
public class VideoFramesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLVideoFrames_" + Guid.NewGuid().ToString("N"));

    public VideoFramesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ── how many frames each picture has ────────────────────────────────────

    [Fact]
    public void APictureOfOneSecond_HasTwentyFiveFrames_AndTheLastPictureOneMore()
    {
        Assert.Equal([26], VideoFrames.PlanFrames([1.0]));        // the last picture is listed once more: one frame more
        Assert.Equal([25, 26], VideoFrames.PlanFrames([1.0, 1.0]));
    }

    [Fact]
    public void NoPictures_HaveNoFrames() =>
        Assert.Empty(VideoFrames.PlanFrames([]));

    [Fact]
    public void ThePicturesOfHalfASecond_AddUpToTheLengthOfTheVideo_WithoutDriftingFromTheirRounding()
    {
        var frames = VideoFrames.PlanFrames(Enumerable.Repeat(0.5, 100).ToArray()); // 12.5 frames each

        Assert.Equal(100, frames.Length);
        Assert.Equal(1250 + 1, frames.Sum());                       // 50 s, and the one frame more
        Assert.All(frames.Take(99), count => Assert.InRange(count, 12, 13));
    }

    [Theory]
    [InlineData(0.04)]
    [InlineData(0.001)]
    [InlineData(0)]
    public void APictureIsNeverWithoutAFrame(double seconds)
    {
        var frames = VideoFrames.PlanFrames([seconds, seconds, seconds]);

        Assert.All(frames, count => Assert.True(count >= 1));
    }

    [Fact]
    public void TheFramesOfASequence_MakeTheLengthItExpects_WithinAFrame()
    {
        var sequence = VideoGenerationService.BuildSequence(
            ["a", "b", "c", "d"], "cover", 3, 0.5, 2.7, randomSamples: null, repeatToAudio: false, repeatAudio: false, audioSeconds: null, coverEndSeconds: 2);

        var seconds = VideoFrames.PlanFrames(sequence.Durations).Sum() / (double)VideoFrames.FramesPerSecond;

        Assert.Equal(sequence.ExpectedSeconds, seconds, 1.0 / VideoFrames.FramesPerSecond);
    }

    [Fact]
    public void TheFramesOfAVideoThatRepeatsForTheAudio_MakeTheLengthOfTheAudio()
    {
        var sequence = VideoGenerationService.BuildSequence(
            ["a", "b", "c"], null, null, 1.3, 1.3, randomSamples: null, repeatToAudio: true, repeatAudio: false, audioSeconds: 20);

        var seconds = VideoFrames.PlanFrames(sequence.Durations).Sum() / (double)VideoFrames.FramesPerSecond;

        Assert.True(seconds >= 20, $"{seconds}"); // the photos are listed until they are longer than the audio: it ends the video
        Assert.True(seconds < 20 + VideoGenerationService.RepeatMarginSeconds + 4, $"{seconds}");
    }

    // ── the arguments for ffmpeg ────────────────────────────────────────────

    [Fact]
    public void ThePipedVideo_IsGivenRawFramesOfTheSizeOfTheVideo_AtTheFrameRate()
    {
        var arguments = VideoGenerationService.BuildPipedFfmpegArguments(@"C:\v\out.mp4", 1920, 1080, 25, "libx264", 23);

        Assert.StartsWith("-y -f rawvideo -pixel_format bgra -video_size 1920x1080 -framerate 25 -i pipe:0 ", arguments);
        Assert.DoesNotContain("concat", arguments);
        Assert.DoesNotContain("-vf", arguments);          // nothing is scaled or padded: the frames are the video
        Assert.DoesNotContain("-noautorotate", arguments);
        Assert.Contains("-c:v libx264 -crf 23 -bf 0 -pix_fmt yuv420p", arguments); // no B-frames, as for the video of files
        Assert.EndsWith(" -progress pipe:1 -nostats \"C:\\v\\out.mp4\"", arguments);
        Assert.DoesNotContain("-map", arguments);          // no audio, no choosing of streams
    }

    [Theory]
    [InlineData(AudioPlayback.Loop, " -stream_loop -1 -i \"C:\\a\\song.mp3\"", " -map 0:v:0 -map 1:a:0 -c:a aac -b:a 192k -shortest")]
    [InlineData(AudioPlayback.OnceThenSilence, " -i \"C:\\a\\song.mp3\"", " -map 0:v:0 -map 1:a:0 -af apad -c:a aac -b:a 192k -shortest")]
    [InlineData(AudioPlayback.OnceEndsVideo, " -i \"C:\\a\\song.mp3\"", " -map 0:v:0 -map 1:a:0 -c:a aac -b:a 192k -shortest -t 12.5")]
    public void TheAudio_IsGivenToFfmpegAsItIsForTheVideoOfFiles(AudioPlayback audio, string input, string output)
    {
        var piped = VideoGenerationService.BuildPipedFfmpegArguments(@"C:\v\out.mp4", 1280, 720, 25, "libx264", 20, @"C:\a\song.mp3", audio, audio == AudioPlayback.OnceEndsVideo ? 12.5 : null);
        var files = VideoGenerationService.BuildFfmpegArguments(@"C:\t\list.txt", @"C:\v\out.mp4", 1280, 720, "libx264", 20, @"C:\a\song.mp3", audio, audio == AudioPlayback.OnceEndsVideo ? 12.5 : null);

        Assert.Contains("-i pipe:0" + input + output + " -c:v", piped); // right after the frames, before the encoder options
        Assert.Contains(input, files);   // and the same ones as the video of files has
        Assert.Contains(output, files);
    }

    [Theory]
    [InlineData("libx265", " -x265-params bframes=0")]
    [InlineData("libx264", " -bf 0")]
    public void TheEncoderIsKeptFromReorderingPictures_AsForTheVideoOfFiles(string codec, string expected)
    {
        var arguments = VideoGenerationService.BuildPipedFfmpegArguments(@"C:\v\out.mp4", 640, 360, 25, codec, 23);

        Assert.Contains(expected, arguments);
    }

    // ── a video made for real (where ffmpeg is) ─────────────────────────────

    private static string? Ffmpeg { get; } = FindFfmpeg();

    private static string? FindFfmpeg()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PhotoLiker.sln")))
            directory = directory.Parent;

        var bundled = directory is null ? null : Path.Combine(directory.FullName, "PhotoLikerUI", "ffmpeg", "ffmpeg.exe");
        return bundled is not null && File.Exists(bundled) ? bundled : null;
    }

    /// <summary>A picture of one colour whose brightness grows with the progress, that notes how it was drawn.</summary>
    private sealed class TestPicture(byte blue, byte green, byte red, Action<string, double, int> noted, string file, int[] active) : IVideoPicture
    {
        public bool Disposed { get; private set; }

        public void Render(double progress, uint[] frame)
        {
            // active[0] is how many are being drawn now, active[1] the most that were at once
            var now = Interlocked.Increment(ref active[0]);
            lock (active)
                active[1] = Math.Max(active[1], now);

            noted(file, progress, Environment.CurrentManagedThreadId);
            var pixel = 0xFF000000u | (uint)(red << 16) | (uint)(green << 8) | blue;
            Array.Fill(frame, pixel);
            Thread.Sleep(1);
            Interlocked.Decrement(ref active[0]);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class TestSource : IVideoPictureSource
    {
        private readonly object _gate = new();
        public List<(string File, double Progress, int Thread)> Renders { get; } = [];
        public List<(string File, int Width, int Height, double Seconds, bool Still)> Opened { get; } = [];
        public HashSet<string> Broken { get; } = [];
        public Dictionary<string, (byte B, byte G, byte R)> Colours { get; } = [];
        public List<TestPicture> Pictures { get; } = [];
        public int[] Active { get; } = new int[2];

        public IVideoPicture Open(string file, int width, int height, double seconds, bool still)
        {
            lock (_gate)
                Opened.Add((file, width, height, seconds, still));
            if (Broken.Contains(file))
                throw new InvalidDataException("not a picture");

            var (b, g, r) = Colours.GetValueOrDefault(file, ((byte)128, (byte)128, (byte)128));
            var picture = new TestPicture(b, g, r, (f, p, t) => { lock (_gate) Renders.Add((f, p, t)); }, file, Active);
            lock (_gate)
                Pictures.Add(picture);
            return picture;
        }
    }

    private static VideoSequence Sequence(params (string File, double Seconds)[] pictures) =>
        new([.. pictures.Select(p => p.File)], [.. pictures.Select(p => p.Seconds)], pictures.Sum(p => p.Seconds) + VideoGenerationService.LastFrameSeconds, AudioPlayback.Loop);

    private async Task<(string Output, List<string> Log, List<int> Progress)> Make(
        VideoSequence sequence, TestSource source, string? audio = null, string codec = "libx264", string[]? still = null, CancellationToken cancellation = default)
    {
        var output = Path.Combine(_folder, "out", "video.mp4");
        var log = new List<string>();
        var progress = new List<int>();
        await VideoFrames.GenerateAsync(
            sequence, output, 64, 48, codec, 23, audio, source, new HashSet<string>(still ?? []),
            line => { lock (log) log.Add(line); }, percent => { lock (progress) progress.Add(percent); }, cancellation, Ffmpeg);
        return (output, log, progress);
    }

    private async Task<string> InfoOf(string file)
    {
        using var process = Process.Start(new ProcessStartInfo(Ffmpeg!, $"-hide_banner -i \"{file}\"") { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        var text = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return text;
    }

    private static double DurationOf(string info)
    {
        var match = System.Text.RegularExpressions.Regex.Match(info, @"Duration: (\d+):(\d+):(\d+\.\d+)");
        return int.Parse(match.Groups[1].Value) * 3600 + int.Parse(match.Groups[2].Value) * 60 + double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The colour of the middle of the picture shown at a time of the video, as ffmpeg decodes it.</summary>
    private async Task<(int R, int G, int B)> ColourAt(string video, double seconds)
    {
        var png = Path.Combine(_folder, $"at{seconds:0.00}.png");
        using var process = Process.Start(new ProcessStartInfo(Ffmpeg!, $"-y -hide_banner -loglevel error -ss {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{video}\" -frames:v 1 \"{png}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        await process.WaitForExitAsync();
        Assert.True(File.Exists(png), $"no picture at {seconds}");

        // A PNG made of one colour: find it in the decoded file through ffmpeg again, as raw rgb24 (no image library here)
        var raw = Path.Combine(_folder, $"at{seconds:0.00}.rgb");
        using var convert = Process.Start(new ProcessStartInfo(Ffmpeg!, $"-y -hide_banner -loglevel error -i \"{png}\" -frames:v 1 -f rawvideo -pix_fmt rgb24 \"{raw}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        await convert.WaitForExitAsync();
        var bytes = await File.ReadAllBytesAsync(raw);
        var middle = (24 * 64 + 32) * 3;
        return (bytes[middle], bytes[middle + 1], bytes[middle + 2]);
    }

    [Fact]
    public async Task TheVideo_HasTheLengthOfItsSequence_AndEachPictureIsWhereItsTimeIs()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();
        source.Colours["red"] = (0, 0, 230);
        source.Colours["green"] = (0, 230, 0);
        source.Colours["blue"] = (230, 0, 0);

        var (output, _, progress) = await Make(Sequence(("red", 1.0), ("green", 0.5), ("blue", 1.5)), source);

        var info = await InfoOf(output);
        Assert.Contains("Video: h264", info);
        Assert.Contains("64x48", info);
        Assert.Equal(3.04, DurationOf(info), 0.1);

        var red = await ColourAt(output, 0.5);
        var green = await ColourAt(output, 1.25);
        var blue = await ColourAt(output, 2.2);
        Assert.True(red.R > 150 && red.G < 90 && red.B < 90, $"red: {red}");
        Assert.True(green.G > 150 && green.R < 90 && green.B < 90, $"green: {green}");
        Assert.True(blue.B > 150 && blue.R < 90 && blue.G < 90, $"blue: {blue}");

        Assert.Equal(100, progress.Max()); // the bar gets to the end
        Assert.Equal(progress.OrderBy(p => p), progress);
    }

    [Fact]
    public async Task EveryPictureIsDrawnFromItsFirstFrameToItsLast_OneFrameAtATime_InOrder()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();

        await Make(Sequence(("a", 1.0), ("b", 0.2), ("c", 0.04)), source);

        // The order of the pictures; each from 0 to 1, evenly
        var files = source.Renders.Select(r => r.File).ToList();
        Assert.Equal([.. Enumerable.Repeat("a", 25), .. Enumerable.Repeat("b", 5), .. Enumerable.Repeat("c", 2)], files); // 1 s, 0.2 s, and one frame and the one more at the end
        Assert.Equal(1, source.Active[1]); // one frame at a time, whatever the thread
        var a = source.Renders.Where(r => r.File == "a").Select(r => r.Progress).ToList();
        Assert.Equal(0, a[0]);
        Assert.Equal(1, a[^1]);
        Assert.Equal(a.OrderBy(p => p), a);
        Assert.Equal(1.0 / 24, a[1] - a[0], 1e-9);
        Assert.All(source.Pictures, picture => Assert.True(picture.Disposed)); // none is held on to
    }

    [Fact]
    public async Task ThePicturesAreAskedForTheSizeOfTheVideo_TheTimeTheyAreShown_AndAreTheCoversTold()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();

        await Make(Sequence(("cover", 2.0), ("a", 0.5), ("cover", 1.0)), source, still: ["cover"]);

        var opened = source.Opened.OrderBy(o => o.Seconds).ToList();
        Assert.All(opened, o => Assert.Equal((64, 48), (o.Width, o.Height)));
        Assert.Equal([("a", 0.5, false), ("cover", 1.0, true), ("cover", 2.0, true)], opened.Select(o => (o.File, o.Seconds, o.Still)));
    }

    [Fact]
    public async Task APictureThatCanNotBeMade_IsBlackForItsTime_AndTheVideoGoesOn()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();
        source.Broken.Add("bad");
        source.Colours["good"] = (0, 230, 0);

        var (output, log, _) = await Make(Sequence(("bad", 1.0), ("good", 1.0)), source);

        Assert.Contains(log, line => line.Contains("bad") && line.Contains("not a picture")); // the person is told which, and why
        var black = await ColourAt(output, 0.5);
        var green = await ColourAt(output, 1.5);
        Assert.True(black.R < 30 && black.G < 30 && black.B < 30, $"black: {black}");
        Assert.True(green.G > 150, $"green: {green}");
    }

    [Fact]
    public async Task TheAudio_GoesWithTheFrames_AndTheVideoEndsWhereItsPicturesDo()
    {
        if (Ffmpeg is null) return;
        var audio = Path.Combine(_folder, "tone.wav");
        using (var tone = Process.Start(new ProcessStartInfo(Ffmpeg, $"-y -hide_banner -loglevel error -f lavfi -i sine=frequency=440:duration=5 \"{audio}\"") { UseShellExecute = false, CreateNoWindow = true })!)
            await tone.WaitForExitAsync();

        var source = new TestSource();
        var (output, _, _) = await Make(Sequence(("a", 1.0), ("b", 1.0)), source, audio); // 2 s of pictures, 5 s of sound: the pictures end it

        var info = await InfoOf(output);
        Assert.Contains("Audio: aac", info);
        Assert.Contains("Video: h264", info);
        Assert.Equal(2.04, DurationOf(info), 0.2);
    }

    [Fact]
    public async Task AnEncoderThatDoesNotExist_IsAnError_NotAHang()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();

        var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
            Make(Sequence(("a", 4.0), ("b", 4.0)), source, codec: "no_such_encoder").WaitAsync(TimeSpan.FromSeconds(60)));

        Assert.IsNotType<TimeoutException>(failure);
        Assert.Contains("ffmpeg", failure.Message, StringComparison.OrdinalIgnoreCase); // "ffmpeg failed with exit code ..."
    }

    [Fact]
    public async Task ACancelledRun_Stops_AndThrowsCancelled()
    {
        if (Ffmpeg is null) return;
        using var cancel = new CancellationTokenSource();
        var source = new TestSource();
        source.Colours["a"] = (0, 0, 200);

        var run = Make(Sequence(("a", 3600.0)), source, cancellation: cancel.Token); // an hour: 90,000 frames, far from done
        await Task.Delay(1500);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task WithoutFfmpeg_TheErrorSaysSo()
    {
        var source = new TestSource();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => VideoFrames.GenerateAsync(
            Sequence(("a", 1.0)), Path.Combine(_folder, "x.mp4"), 64, 48, "libx264", 23, null, source, new HashSet<string>(), _ => { }, _ => { },
            ffmpegPath: Path.Combine(_folder, "there-is-no-ffmpeg.exe")));

        Assert.Contains("ffmpeg", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(source.Opened); // nothing was made for a video that can not be
    }
}
