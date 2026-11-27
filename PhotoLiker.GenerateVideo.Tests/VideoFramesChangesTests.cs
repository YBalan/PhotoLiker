namespace PhotoLiker.GenerateVideo.Tests;

/// <summary>A video that is drawn frame by frame with changes between its pictures (a dissolve, a wipe): where the frames of a change come from, and that the video is as long as it is without them.</summary>
public partial class VideoFramesTests
{
    /// <summary>
    /// A change that dissolves the first pixel of the picture that goes into the first pixel of the one that comes (the pictures of these tests are all of one colour), and notes what it was asked
    /// and given. Every change takes <paramref name="seconds"/> (or what <paramref name="secondsOf"/> says for a picture).
    /// </summary>
    private sealed class TestChanges(double seconds, Func<int, double>? secondsOf = null) : IVideoChanges
    {
        private readonly object _gate = new();
        public List<(int Index, double PictureSeconds)> Asked { get; } = [];
        public List<(int Index, double Progress, uint Outgoing, uint Incoming)> Mixed { get; } = [];

        public double Seconds(int index, double pictureSeconds)
        {
            lock (_gate)
                Asked.Add((index, pictureSeconds));
            return secondsOf?.Invoke(index) ?? seconds;
        }

        public void Mix(int index, double progress, uint[] outgoing, uint[] incoming, uint[] destination)
        {
            lock (_gate)
                Mixed.Add((index, progress, outgoing[0], incoming[0]));

            static uint Channel(uint a, uint b, double share, int shift) => (uint)Math.Round(((a >> shift & 0xFF) * (1 - share)) + ((b >> shift & 0xFF) * share));
            var pixel = 0xFF000000u | Channel(outgoing[0], incoming[0], progress, 16) << 16 | Channel(outgoing[0], incoming[0], progress, 8) << 8 | Channel(outgoing[0], incoming[0], progress, 0);
            Array.Fill(destination, pixel);
        }
    }

    private static uint Pixel(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;

    // ── how many frames each change has ─────────────────────────────────────

    [Fact]
    public void WithoutChanges_NoPictureHasFramesOfAChange()
    {
        Assert.Equal([0, 0, 0], VideoFrames.PlanChanges(VideoFrames.PlanFrames([1.0, 1.0, 1.0]), [1.0, 1.0, 1.0], null));
        Assert.Empty(VideoFrames.PlanChanges([], [], new TestChanges(0.5)));
    }

    [Fact]
    public void TheFirstPictureIsNotBroughtIn_AndTheOthersHaveTheTimeOfTheirChange_AtTwentyFiveFramesASecond()
    {
        var durations = new[] { 2.0, 2.0, 2.0 };
        var changes = new TestChanges(0.4);

        var lengths = VideoFrames.PlanChanges(VideoFrames.PlanFrames(durations), durations, changes);

        Assert.Equal([0, 10, 10], lengths);
        Assert.Equal([1, 2], changes.Asked.Select(a => a.Index)); // asked for the pictures that are brought in, not for the first
    }

    [Fact]
    public void TheChange_IsAskedForTheShorterOfThePicturesItIsBetween()
    {
        var durations = new[] { 4.0, 0.5, 3.0 };
        var changes = new TestChanges(0.2);

        VideoFrames.PlanChanges(VideoFrames.PlanFrames(durations), durations, changes);

        Assert.Equal([(1, 0.5), (2, 0.5)], changes.Asked);
    }

    [Fact]
    public void AChange_IsNeverLongerThanThePictureThatGoes()
    {
        var durations = new[] { 0.2, 5.0 }; // 5 frames, then a change that would take 50
        var frames = VideoFrames.PlanFrames(durations);

        var lengths = VideoFrames.PlanChanges(frames, durations, new TestChanges(2.0));

        Assert.Equal(frames[0], lengths[1]);
        Assert.Equal(0, lengths[0]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(0.01)] // less than half a frame
    public void ACutOrNoTimeOrNoNumber_IsNoChange(double seconds)
    {
        var durations = new[] { 1.0, 1.0 };

        Assert.Equal([0, 0], VideoFrames.PlanChanges(VideoFrames.PlanFrames(durations), durations, new TestChanges(seconds)));
    }

    [Fact]
    public void ThePicturesThatWantNoChange_AreCutTo_WhileTheOthersAreBroughtIn()
    {
        var durations = new[] { 1.0, 1.0, 1.0 };

        var lengths = VideoFrames.PlanChanges(VideoFrames.PlanFrames(durations), durations, new TestChanges(0, index => index == 2 ? 0.4 : 0));

        Assert.Equal([0, 0, 10], lengths);
    }

    // ── a video made for real ───────────────────────────────────────────────

    [Fact]
    public async Task AVideoWithChanges_IsAsLongAsItIsWithout_AndTheChangeIsMadeOfTheTwoPicturesInTheLastFramesOfTheOneThatGoes()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();
        source.Colours["red"] = (0, 0, 230);
        source.Colours["green"] = (0, 230, 0);
        var changes = new TestChanges(0.8); // 20 frames at the end of the red picture

        var (output, _, progress) = await Make(Sequence(("red", 2.0), ("green", 2.0)), source, changes: changes);

        Assert.Equal(4.04, DurationOf(await InfoOf(output)), 0.1); // the same length as without the change

        // The change is made of 20 steps, between the pictures themselves, and the pictures it is made of are the ones that went and the one that came
        Assert.Equal(20, changes.Mixed.Count);
        Assert.All(changes.Mixed, m => Assert.Equal(1, m.Index));
        Assert.All(changes.Mixed, m => Assert.Equal((Pixel(230, 0, 0), Pixel(0, 230, 0)), (m.Outgoing, m.Incoming)));
        var steps = changes.Mixed.Select(m => m.Progress).ToList();
        Assert.All(steps, step => Assert.InRange(step, 0.0001, 0.9999)); // never the pictures themselves
        Assert.Equal(steps.OrderBy(step => step), steps);
        Assert.Equal(1.0 / 21, steps[0], 1e-9);
        Assert.Equal(20.0 / 21, steps[^1], 1e-9);

        // What the video shows: the red picture, the dissolve (red going, green coming), then the green picture from the moment of its own time
        var red = await ColourAt(output, 1.0);
        var halfway = await ColourAt(output, 1.6);           // the middle of the change (1.2 s to 2.0 s)
        var green = await ColourAt(output, 3.0);
        Assert.True(red.R > 150 && red.G < 90, $"red: {red}");
        Assert.True(halfway.R > 60 && halfway.G > 60, $"in the middle of the change both show: {halfway}");
        Assert.True(green.G > 150 && green.R < 90, $"green: {green}");
        Assert.Equal(100, progress.Max());
    }

    [Fact]
    public async Task ThePictureThatComes_MovesFromTheFirstFrameOfTheChange_ToTheLastOfItsOwnTime_InOneGo()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();

        await Make(Sequence(("a", 1.0), ("b", 1.0), ("c", 1.0)), source, changes: new TestChanges(0.2)); // 5 frames of change; the frames are 25, 25 and 26

        // The order of the frames: a, then a and b together, b, b and c together, c
        var files = source.Renders.Select(r => r.File).ToList();
        Assert.Equal(
            [.. Enumerable.Repeat("a", 20), .. Enumerable.Repeat(new[] { "a", "b" }, 5).SelectMany(pair => pair), .. Enumerable.Repeat("b", 20),
             .. Enumerable.Repeat(new[] { "b", "c" }, 5).SelectMany(pair => pair), .. Enumerable.Repeat("c", 26)],
            files);

        // A picture moves evenly from 0 to 1 over the frames of its change and of its own time together (5 + 25 for b), not starting again where its own time does
        var b = source.Renders.Where(r => r.File == "b").Select(r => r.Progress).ToList();
        Assert.Equal(30, b.Count);
        Assert.Equal(Enumerable.Range(0, 30).Select(k => k / 29.0), b, new ToleranceComparer(1e-9));

        // The first picture has no change before it, the last has its own time and one frame more
        var a = source.Renders.Where(r => r.File == "a").Select(r => r.Progress).ToList();
        Assert.Equal(Enumerable.Range(0, 25).Select(k => k / 24.0), a, new ToleranceComparer(1e-9));
        var c = source.Renders.Where(r => r.File == "c").Select(r => r.Progress).ToList();
        Assert.Equal(Enumerable.Range(0, 31).Select(k => k / 30.0), c, new ToleranceComparer(1e-9));

        Assert.Equal(1, source.Active[1]); // one frame at a time, whatever the thread
        Assert.All(source.Pictures, picture => Assert.True(picture.Disposed)); // and none is held on to
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;

        public int GetHashCode(double value) => 0;
    }

    [Fact]
    public async Task APictureIsAskedForTheTimeItIsOnTheScreenFor_TheChangeThatBringsItInIncluded_AndForItsPlaceInTheSequence()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();

        await Make(Sequence(("cover", 2.0), ("a", 1.0), ("cover", 1.0)), source, still: ["cover"], changes: new TestChanges(0.4));

        // 0.4 s of change brings in the second and the third picture (ten frames each); the first is not brought in. The cover is two pictures.
        var opened = source.Indexes.OrderBy(i => i.Index).Select(i => (i.Index, i.File, Math.Round(i.Seconds, 3))).ToList();
        Assert.Equal([(0, "cover", 2.0), (1, "a", 1.4), (2, "cover", 1.4)], opened);
        Assert.All(source.Opened, o => Assert.Equal(o.File == "cover", o.Still)); // the cover is the one that stays still, both times
    }

    [Fact]
    public async Task APictureThatCanNotBeMade_IsBlackInTheChange_AndInItsTime_AndTheVideoGoesOn()
    {
        if (Ffmpeg is null) return;
        var source = new TestSource();
        source.Broken.Add("bad");
        source.Colours["good"] = (0, 230, 0);
        var changes = new TestChanges(0.4);

        var (output, log, _) = await Make(Sequence(("good", 1.0), ("bad", 1.0), ("good", 1.0)), source, changes: changes);

        Assert.Contains(log, line => line.Contains("bad") && line.Contains("not a picture"));
        Assert.Equal(20, changes.Mixed.Count); // two changes of ten frames, the picture that is not there is part of both
        Assert.Contains(changes.Mixed, m => m.Index == 1 && m.Incoming == Pixel(0, 0, 0));  // brought in as black
        Assert.Contains(changes.Mixed, m => m.Index == 2 && m.Outgoing == Pixel(0, 0, 0));  // and sent off as black
        var black = await ColourAt(output, 1.5);
        Assert.True(black.R < 30 && black.G < 30 && black.B < 30, $"black: {black}");
        Assert.Equal(3.04, DurationOf(await InfoOf(output)), 0.1);
    }

    [Fact]
    public async Task AVideoWhoseChangesAreAllCuts_IsDrawnAsOneWithoutChanges()
    {
        if (Ffmpeg is null) return;
        var cuts = new TestSource();
        var none = new TestSource();

        await Make(Sequence(("a", 1.0), ("b", 0.5)), cuts, changes: new TestChanges(0));
        await Make(Sequence(("a", 1.0), ("b", 0.5)), none);

        Assert.Equal(none.Renders.Select(r => (r.File, r.Progress)), cuts.Renders.Select(r => (r.File, r.Progress)));
    }

    [Fact]
    public async Task ACancelledRun_WithChanges_Stops_AndThrowsCancelled_AndLetsGoOfThePictures()
    {
        if (Ffmpeg is null) return;
        using var cancel = new CancellationTokenSource();
        var source = new TestSource();

        var run = Make(Sequence(("a", 3600.0), ("b", 3600.0)), source, cancellation: cancel.Token, changes: new TestChanges(0.8));
        await Task.Delay(1500);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.NotEmpty(source.Pictures);
        Assert.All(source.Pictures, picture => Assert.True(picture.Disposed)); // the ones that were being drawn and the ones that were made ahead
    }
}
