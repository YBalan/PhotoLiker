namespace PhotoLiker.VideoToGif.Tests;

/// <summary>The progress report of ffmpeg, which is text.</summary>
public sealed class GifProgressTests
{
    [Theory]
    [InlineData("out_time_us=2500000", 10, 0.25)]
    [InlineData("out_time_ms=2500000", 10, 0.25)] // ffmpeg's older name for the same microseconds
    [InlineData("out_time_us=10000000", 10, 1.0)]
    [InlineData("out_time_us=99000000", 10, 1.0)] // a hair past the end is the end
    [InlineData("out_time_us=0", 10, 0.0)]
    [InlineData("  out_time_us=5000000  ", 10, 0.5)]
    [InlineData("out_time_us=-500000", 10, 0.0)] // the first frames of a stream that starts before 0
    public void ThePositionInTheReport_IsAFractionOfTheLength(string line, double length, double expected)
    {
        Assert.True(GifMaker.TryParseProgress(line, length, out var fraction));
        Assert.Equal(expected, fraction, 6);
    }

    [Fact]
    public void TheEndOfTheReport_IsAllOfIt()
    {
        Assert.True(GifMaker.TryParseProgress("progress=end", 0, out var fraction));
        Assert.Equal(1.0, fraction);
    }

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("out_time=00:00:02.500000")]
    [InlineData("frame=25")]
    [InlineData("progress=continue")]
    [InlineData("")]
    [InlineData("out_time_us=")]
    [InlineData("out_time_us=abc")]
    public void AnythingElse_IsNotAPosition(string line) =>
        Assert.False(GifMaker.TryParseProgress(line, 10, out _));

    [Fact]
    public void WithoutALength_ThereIsNoFraction() =>
        Assert.False(GifMaker.TryParseProgress("out_time_us=2500000", 0, out _));

    [Theory]
    [InlineData("first\nsecond\nLast reason  \n\n", 1, "Last reason")]
    [InlineData("", 3, "exit code 3")]
    [InlineData("\r\n\r\n", 4, "exit code 4")]
    public void TheReasonFfmpegStopped_IsItsLastMessage(string messages, int exitCode, string expected) =>
        Assert.Equal(expected, GifMaker.LastMessage(messages, exitCode));
}

/// <summary>The GIF made for real, by the ffmpeg that comes with the program, from videos that are made for the test (and then read back as GIF files).</summary>
public sealed class GifMakerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // A 4 second video of 25 frames a second, 320 x 240
    private static readonly GifClip Video = new(4, 25, 320, 240);

    private string? MakeVideo(double seconds = 4, int rate = 25, int width = 320, int height = 240) =>
        RealFfmpeg.MakeVideo(_folder.Path, $"clip-{seconds}-{rate}-{width}x{height}.mp4", seconds, rate, width, height);

    private async Task<GifInfo?> Make(string video, GifRange range, GifSettings settings, GifClip? clip = null, string name = "out.gif", IProgress<GifProgress>? progress = null)
    {
        var result = await GifMaker.MakeAsync(video, _folder.File(name), clip ?? Video, range, settings, progress, ffmpegPath: RealFfmpeg.Path);
        Assert.Equal(_folder.File(name), result.Path);
        Assert.Equal(new FileInfo(result.Path).Length, result.Bytes);
        return GifInfo.Read(result.Path);
    }

    private static GifSettings Settings(double frame = 0.1, double? delay = null, int width = 640, bool original = false, int colors = 256, GifDither dither = GifDither.Sierra, GifLoop loop = GifLoop.Forever) =>
        new() { FrameSeconds = frame, DelaySeconds = delay, Width = width, OriginalSize = original, Colors = colors, Dither = dither, Loop = loop };

    // ── What comes out ──────────────────────────────────────────────────────

    [Fact]
    public async Task AllOfTheVideo_IsAGifOfAllItsFrames_AtTheRateAsked()
    {
        if (MakeVideo() is not { } video)
            return;

        var info = (await Make(video, GifRange.All, Settings(frame: 0.1)))!;

        Assert.Equal(40, info.FrameCount); // 4 seconds at 10 a second
        Assert.Equal(4.0, info.DurationSeconds, 1);
        Assert.Equal((320, 240), (info.Width, info.Height)); // the video is narrower than 640: it is not stretched
        Assert.True(info.LoopsForever);
    }

    [Fact]
    public async Task ThePlanThatWasShown_IsWhatWasMade()
    {
        if (MakeVideo() is not { } video)
            return;

        var range = GifRange.FromFrames(Video, 25, 74);
        var settings = Settings(frame: 0.08, width: 160);
        var plan = GifPlan.Make(Video, range, settings);

        var info = (await Make(video, range, settings))!;

        Assert.Equal(plan.FrameCount, info.FrameCount);
        Assert.Equal((plan.Width, plan.Height), (info.Width, info.Height));
        Assert.Equal(plan.LengthSeconds, info.DurationSeconds, 1);
    }

    [Fact]
    public async Task ARange_IsOnlyThatPartOfTheVideo()
    {
        if (MakeVideo() is not { } video)
            return;

        // Frames 25 to 74 of the video are its second and third second: two seconds, 20 frames of the GIF
        var info = (await Make(video, GifRange.FromFrames(Video, 25, 74), Settings(frame: 0.1)))!;

        Assert.Equal(20, info.FrameCount);
        Assert.Equal(2.0, info.DurationSeconds, 1);
    }

    [Fact]
    public async Task ARange_StartsWhereItWasAsked_NotAtTheStartOfTheVideo()
    {
        if (MakeVideo() is not { } video)
            return;

        // The test pattern has a running counter, so the two GIFs of different parts must differ; the part from 3 s is 1 second, whichever way it is asked for
        var whole = (await Make(video, GifRange.All, Settings(frame: 0.1), name: "all.gif"))!;
        var last = (await Make(video, new GifRange(3, null), Settings(frame: 0.1), name: "last.gif"))!;
        var first = (await Make(video, new GifRange(0, 1), Settings(frame: 0.1), name: "first.gif"))!;

        Assert.Equal(40, whole.FrameCount);
        Assert.Equal(10, last.FrameCount);
        Assert.Equal(10, first.FrameCount);
        Assert.NotEqual(File.ReadAllBytes(_folder.File("first.gif")), File.ReadAllBytes(_folder.File("last.gif")));
    }

    [Fact]
    public async Task AWidth_ScalesTheGif_InTheProportionsOfTheVideo()
    {
        if (MakeVideo() is not { } video)
            return;

        var info = (await Make(video, GifRange.All, Settings(width: 160)))!;

        Assert.Equal((160, 120), (info.Width, info.Height));
    }

    [Fact]
    public async Task TheOriginalSize_IsNotScaled()
    {
        if (MakeVideo(width: 640, height: 360) is not { } video)
            return;

        var scaled = (await Make(video, GifRange.All, Settings(width: 320), new GifClip(4, 25, 640, 360), name: "small.gif"))!;
        var original = (await Make(video, GifRange.All, Settings(width: 320, original: true), new GifClip(4, 25, 640, 360), name: "big.gif"))!;

        Assert.Equal((320, 180), (scaled.Width, scaled.Height));
        Assert.Equal((640, 360), (original.Width, original.Height));
    }

    [Fact]
    public async Task ARoundedHeight_IsWhatFfmpegMakes_SoTheSizeShownIsTrue()
    {
        if (MakeVideo(width: 640, height: 360) is not { } video)
            return;

        var clip = new GifClip(4, 25, 640, 360);
        var settings = Settings(width: 333);
        var plan = GifPlan.Make(clip, GifRange.All, settings);

        var info = (await Make(video, GifRange.All, settings, clip))!;

        Assert.Equal((333, 187), (plan.Width, plan.Height)); // 187.3
        Assert.Equal((plan.Width, plan.Height), (info.Width, info.Height));
    }

    [Fact]
    public async Task AVideoThatIsShownTurned_IsMadeAsItIsSeen()
    {
        if (MakeVideo() is not { } stored || RealFfmpeg.Turned(stored) is not { } turned)
            return;

        // Stored 320 x 240, shown 240 x 320: the front end tells the size as it is shown, and the GIF is that
        var clip = new GifClip(4, 25, 240, 320);
        var settings = Settings(width: 120);
        var info = (await Make(turned, GifRange.All, settings, clip))!;

        Assert.Equal((120, 160), (info.Width, info.Height));
        Assert.Equal((120, 160), (GifPlan.Make(clip, GifRange.All, settings).Width, GifPlan.Make(clip, GifRange.All, settings).Height));
    }

    // The video is 4 seconds long. A frame is taken at the start and then every frame time; the file holds that many frames, each shown for the frame time
    [Theory]
    [InlineData(0.05, 80)]
    [InlineData(0.1, 40)]
    [InlineData(0.2, 20)]
    [InlineData(0.5, 8)]
    [InlineData(1.0, 4)]
    [InlineData(1.5, 3)]
    [InlineData(2.5, 2)]
    [InlineData(3.0, 2)]
    [InlineData(5.0, 1)]
    public async Task TheTimeOfAFrame_DecidesTheFrames_AndHowLongEachIsShown(double frame, int frames)
    {
        if (MakeVideo() is not { } video)
            return;

        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: frame));
        var info = (await Make(video, GifRange.All, Settings(frame: frame)))!;

        Assert.Equal(frames, plan.FrameCount); // what the window said it would be
        Assert.Equal(frames, info.FrameCount); // and what the file holds
        Assert.Equal(frames * frame, info.DurationSeconds, 1);
    }

    // The video is 4 seconds long (100 frames). The frames are taken a time apart, and shown for a time of their own: the file holds what the plan said, and plays that long
    [Theory]
    [InlineData(0.5, 0.1, 8, 0.8)] // 4 seconds of video in 0.8: five times as fast
    [InlineData(1.0, 0.2, 4, 0.8)]
    [InlineData(0.04, 0.1, 100, 10.0)] // every frame of the video, each for a tenth: 4 seconds in 10, slower than the video
    [InlineData(0.1, 0.5, 40, 20.0)]
    [InlineData(0.2, 0.2, 20, 4.0)] // the same time: as fast as the video
    public async Task TheDelay_DecidesHowLongEachFrameIsShown_WhateverTheDistanceTheyWereTakenAt(double frame, double delay, int frames, double playSeconds)
    {
        if (MakeVideo() is not { } video)
            return;

        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: frame, delay: delay));
        var info = (await Make(video, GifRange.All, Settings(frame: frame, delay: delay)))!;

        Assert.Equal(frames, plan.FrameCount);
        Assert.Equal(playSeconds, plan.PlaySeconds, 6);
        Assert.Equal(frames, info.FrameCount);
        Assert.Equal(playSeconds, info.DurationSeconds, 1); // the last frame too: it is shown for the delay
    }

    [Fact]
    public async Task ASpedUpGif_HasTheSameFramesAsOneThatPlaysAtTheSpeedOfTheVideo_OnlyShownForLess()
    {
        if (MakeVideo() is not { } video)
            return;

        await Make(video, GifRange.All, Settings(frame: 0.5), name: "real.gif");
        await Make(video, GifRange.All, Settings(frame: 0.5, delay: 0.1), name: "fast.gif");

        var real = GifInfo.Read(_folder.File("real.gif"));
        var fast = GifInfo.Read(_folder.File("fast.gif"));
        Assert.Equal(real.FrameCount, fast.FrameCount);
        Assert.Equal((real.Width, real.Height), (fast.Width, fast.Height));
        Assert.Equal(4.0, real.DurationSeconds, 1);
        Assert.Equal(0.8, fast.DurationSeconds, 1);
    }

    [Fact]
    public async Task TheProgress_OfASpedUpGif_IsStillAFractionThatReachesOne()
    {
        if (MakeVideo() is not { } video)
            return;

        var reports = new List<GifProgress>();
        await Make(video, GifRange.All, Settings(frame: 0.04, delay: 0.02), progress: new SynchronousProgress(reports.Add));

        var fractions = reports.Where(r => r.Phase == GifPhase.Frames).Select(r => r.Fraction).ToList();
        Assert.Equal(1.0, fractions[^1]);
        Assert.Equal(fractions.OrderBy(f => f), fractions);
        Assert.All(fractions, f => Assert.InRange(f, 0, 1));
    }

    [Theory]
    [InlineData(2.0, 5.0, 1)] // a part that is shorter than a frame time still has the frame at its start
    [InlineData(1.0, 3.0, 1)]
    [InlineData(3.0, 2.0, 1)]
    [InlineData(0.0, 2.0, 2)]
    public async Task APartThatIsShorterThanAFrameTime_IsNotEmpty(double from, double frame, int frames)
    {
        if (MakeVideo() is not { } video)
            return;

        // The part from <from> to the end of a 4 second video
        var range = new GifRange(from, null);
        var info = (await Make(video, range, Settings(frame: frame)))!;

        Assert.Equal(frames, GifPlan.Make(Video, range, Settings(frame: frame)).FrameCount);
        Assert.Equal(frames, info.FrameCount);
    }

    [Fact]
    public async Task AFrameTimeThatIsNotAWholeNumberOfHundredths_IsKeptToTheHundredth()
    {
        if (MakeVideo() is not { } video)
            return;

        // 0.126 is 0.13: 31 frames of 13 hundredths in 4 seconds, which is what the plan said
        var settings = Settings(frame: 0.126);
        var info = (await Make(video, GifRange.All, settings))!;

        Assert.Equal(GifPlan.Make(Video, GifRange.All, settings).FrameCount, info.FrameCount);
        Assert.Equal(31, info.FrameCount);
        Assert.Equal(31 * 0.13, info.DurationSeconds, 1);
    }

    [Fact]
    public async Task OnceMeansNoRepeatBlock_AndForeverMeansRepeatForEver()
    {
        if (MakeVideo(seconds: 2) is not { } video)
            return;

        var forever = (await Make(video, GifRange.All, Settings(loop: GifLoop.Forever), new GifClip(2, 25, 320, 240), "forever.gif"))!;
        var once = (await Make(video, GifRange.All, Settings(loop: GifLoop.Once), new GifClip(2, 25, 320, 240), "once.gif"))!;

        Assert.Equal(0, forever.LoopCount);
        Assert.Null(once.LoopCount);
    }

    [Fact]
    public async Task FewerColours_MakeASmallerFile()
    {
        if (MakeVideo() is not { } video)
            return;

        await Make(video, GifRange.All, Settings(colors: 256), name: "many.gif");
        await Make(video, GifRange.All, Settings(colors: 8), name: "few.gif");

        var many = new FileInfo(_folder.File("many.gif")).Length;
        var few = new FileInfo(_folder.File("few.gif")).Length;
        Assert.True(few < many, $"8 colours ({few} bytes) are smaller than 256 ({many} bytes)");
    }

    [Fact]
    public async Task NoDithering_MakesASmallerFileThanSierra()
    {
        if (MakeVideo() is not { } video)
            return;

        await Make(video, GifRange.All, Settings(dither: GifDither.Sierra), name: "sierra.gif");
        await Make(video, GifRange.All, Settings(dither: GifDither.None), name: "none.gif");

        Assert.True(new FileInfo(_folder.File("none.gif")).Length < new FileInfo(_folder.File("sierra.gif")).Length);
    }

    [Fact]
    public async Task TheVideoSoundIsNotAProblem_AGifHasNone()
    {
        // The test video has a sound track; the whole of the tests above made GIFs from it. This one only says so out loud.
        if (MakeVideo(seconds: 1) is not { } video)
            return;

        var info = (await Make(video, GifRange.All, Settings(), new GifClip(1, 25, 320, 240)))!;

        Assert.Equal(10, info.FrameCount);
    }

    [Fact]
    public async Task TheTemporaryFiles_AreGone_AndOnlyTheGifIsLeft()
    {
        if (MakeVideo(seconds: 1) is not { } video)
            return;

        var before = Directory.GetFiles(Path.GetTempPath(), "photoliker_gif_*").Length;
        await Make(video, GifRange.All, Settings(), new GifClip(1, 25, 320, 240));

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "photoliker_gif_*").Length);
        Assert.Equal(["clip-1-25-320x240.mp4", "out.gif"], Directory.GetFiles(_folder.Path).Select(file => Path.GetFileName(file)!).OrderBy(name => name).ToArray());
    }

    [Fact]
    public async Task AGifThatWasThere_IsReplaced()
    {
        if (MakeVideo(seconds: 1) is not { } video)
            return;

        File.WriteAllText(_folder.File("out.gif"), "an old file");

        var info = (await Make(video, GifRange.All, Settings(), new GifClip(1, 25, 320, 240)))!;

        Assert.Equal(10, info.FrameCount);
    }

    [Fact]
    public async Task TheFolderOfTheGif_IsMadeWhenItIsNotThere()
    {
        if (MakeVideo(seconds: 1) is not { } video)
            return;

        var target = Path.Combine(_folder.Path, "new", "folder", "out.gif");
        var result = await GifMaker.MakeAsync(video, target, new GifClip(1, 25, 320, 240), GifRange.All, Settings(), ffmpegPath: RealFfmpeg.Path);

        Assert.True(File.Exists(target));
        Assert.Equal(10, result.Plan.FrameCount);
    }

    [Fact]
    public async Task APathWithSpacesAndQuotes_Works()
    {
        if (RealFfmpeg.MakeVideo(_folder.Path, "my holiday (2) 'a'.mp4", 1) is not { } video)
            return;

        var target = _folder.File("my gif (2) 'a'.gif");
        await GifMaker.MakeAsync(video, target, new GifClip(1, 25, 320, 240), GifRange.All, Settings(), ffmpegPath: RealFfmpeg.Path);

        Assert.Equal(10, GifInfo.Read(target).FrameCount);
    }

    // ── Progress ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheProgress_StartsWithThePalette_ThenTheFrames_AndEndsWithAll()
    {
        if (MakeVideo() is not { } video)
            return;

        var reports = new List<GifProgress>();
        await Make(video, GifRange.All, Settings(), progress: new SynchronousProgress(reports.Add));

        Assert.Equal(new GifProgress(GifPhase.Palette, 0), reports[0]);
        Assert.Equal(new GifProgress(GifPhase.Frames, 0), reports[1]);
        Assert.Equal(new GifProgress(GifPhase.Frames, 1), reports[^1]);
        var fractions = reports.Where(r => r.Phase == GifPhase.Frames).Select(r => r.Fraction).ToList();
        Assert.Equal(fractions.OrderBy(f => f), fractions); // it only goes forward
        Assert.All(fractions, f => Assert.InRange(f, 0, 1));
    }

    private sealed class SynchronousProgress(Action<GifProgress> report) : IProgress<GifProgress>
    {
        public void Report(GifProgress value)
        {
            lock (report)
                report(value);
        }
    }

    // ── Stopping and failing ────────────────────────────────────────────────

    [Fact]
    public async Task Stopping_StopsFfmpeg_AndLeavesNothingBehind()
    {
        if (MakeVideo(seconds: 60) is not { } video)
            return;

        // A long video at the original size takes a while; the token is cancelled when ffmpeg is in the middle of the frames (a report with a fraction that is neither 0 nor 1), so that it is
        // a running ffmpeg that is stopped, not one that was never started
        using var stop = new CancellationTokenSource();
        var progress = new SynchronousProgress(report =>
        {
            if (report.Phase == GifPhase.Frames && report.Fraction is > 0 and < 1)
                stop.Cancel();
        });
        var before = Directory.GetFiles(Path.GetTempPath(), "photoliker_gif_*").Length;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GifMaker.MakeAsync(video, _folder.File("stopped.gif"), new GifClip(60, 25, 320, 240), GifRange.All, Settings(frame: 0.04, original: true), progress, stop.Token, RealFfmpeg.Path));

        Assert.False(File.Exists(_folder.File("stopped.gif")));
        Assert.Empty(Directory.GetFiles(_folder.Path, "*.part"));
        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "photoliker_gif_*").Length);
    }

    [Fact]
    public async Task ATokenThatIsAlreadyCancelled_RunsNothing()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GifMaker.MakeAsync("video.mp4", _folder.File("never.gif"), Video, GifRange.All, Settings(), null, stop.Token, "no such ffmpeg.exe"));
    }

    [Fact]
    public async Task AGifThatWasThere_StaysWhenTheNewOneFails()
    {
        File.WriteAllText(_folder.File("out.gif"), "a good file");
        if (RealFfmpeg.Path is null)
            return;

        var broken = _folder.File("broken.mp4");
        File.WriteAllText(broken, "this is not a video");

        await Assert.ThrowsAsync<GifException>(() => GifMaker.MakeAsync(broken, _folder.File("out.gif"), Video, GifRange.All, Settings(), ffmpegPath: RealFfmpeg.Path));

        Assert.Equal("a good file", File.ReadAllText(_folder.File("out.gif")));
        Assert.Empty(Directory.GetFiles(_folder.Path, "*.part"));
    }

    [Fact]
    public async Task AVideoThatCannotBeRead_IsAnErrorThatSaysWhy()
    {
        if (RealFfmpeg.Path is null)
            return;

        var broken = _folder.File("broken.mp4");
        File.WriteAllText(broken, "this is not a video");

        var error = await Assert.ThrowsAsync<GifException>(() => GifMaker.MakeAsync(broken, _folder.File("out.gif"), Video, GifRange.All, Settings(), ffmpegPath: RealFfmpeg.Path));

        Assert.Equal(GifFailure.FfmpegFailed, error.Reason);
        Assert.False(string.IsNullOrWhiteSpace(error.Detail));
        Assert.Contains(error.Detail, error.Message);
        Assert.False(File.Exists(_folder.File("out.gif")));
    }

    [Fact]
    public async Task AMissingVideo_IsAnError()
    {
        if (RealFfmpeg.Path is null)
            return;

        var error = await Assert.ThrowsAsync<GifException>(() => GifMaker.MakeAsync(_folder.File("gone.mp4"), _folder.File("out.gif"), Video, GifRange.All, Settings(), ffmpegPath: RealFfmpeg.Path));

        Assert.Equal(GifFailure.FfmpegFailed, error.Reason);
    }

    [Fact]
    public async Task AMissingFfmpeg_IsAnErrorOfItsOwn()
    {
        var path = Path.Combine(_folder.Path, "no such folder", "ffmpeg.exe");

        var error = await Assert.ThrowsAsync<GifException>(() => GifMaker.MakeAsync("video.mp4", _folder.File("out.gif"), Video, GifRange.All, Settings(), ffmpegPath: path));

        Assert.Equal(GifFailure.FfmpegNotFound, error.Reason);
        Assert.Equal(path, error.Detail);
    }

    [Fact]
    public async Task APlaceBeyondTheEndOfTheVideo_IsAnErrorOrAGifOfTheLastFrames_NeverABrokenFile()
    {
        if (MakeVideo(seconds: 1) is not { } video)
            return;

        // The length the front end gave (4 s) is more than the video really has (1 s): the plan trusts the length, ffmpeg has nothing there
        var wrong = new GifClip(4, 25, 320, 240);
        try
        {
            await GifMaker.MakeAsync(video, _folder.File("beyond.gif"), wrong, new GifRange(3, null), Settings(), ffmpegPath: RealFfmpeg.Path);
        }
        catch (GifException error)
        {
            Assert.Equal(GifFailure.NoOutput, error.Reason);
            Assert.False(File.Exists(_folder.File("beyond.gif")));
            Assert.Empty(Directory.GetFiles(_folder.Path, "*.part"));
            return;
        }

        Assert.True(GifInfo.Read(_folder.File("beyond.gif")).FrameCount >= 1);
    }

    [Fact]
    public void ThereIsAnFfmpeg_ToRunWhenNothingIsSaid()
    {
        var path = GifMaker.ResolveFfmpeg();

        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(path == "ffmpeg" || path.EndsWith(Path.Combine("ffmpeg", "ffmpeg.exe"), StringComparison.OrdinalIgnoreCase));
    }
}
