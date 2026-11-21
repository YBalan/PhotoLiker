namespace PhotoLiker.VideoToGif.Tests;

/// <summary>What the GIF will be (frames, size, the part of the video) and what ffmpeg is told, worked out before anything runs.</summary>
public sealed class GifPlanTests
{
    // A 10 second video of 25 frames a second: 250 frames
    private static readonly GifClip Video = new(10, 25, 1920, 1080);

    private static GifSettings Settings(double frame = 0.1, double? delay = null, int width = 640, bool original = false, int colors = 256, GifDither dither = GifDither.Sierra, GifLoop loop = GifLoop.Forever) =>
        new() { FrameSeconds = frame, DelaySeconds = delay, Width = width, OriginalSize = original, Colors = colors, Dither = dither, Loop = loop };

    // ── The frames of the video ─────────────────────────────────────────────

    [Fact]
    public void TheFramesOfAVideo_AreItsLengthTimesItsRate()
    {
        Assert.Equal(250, Video.SourceFrames);
        Assert.Equal(25, Video.SourceFramesPerSecond);
        Assert.Equal(0, new GifClip(0, 25, 100, 100).SourceFrames); // a video that does not say how long it is
        Assert.Equal(1, new GifClip(0.01, 25, 100, 100).SourceFrames); // never none
    }

    [Fact]
    public void AVideoThatDoesNotSayItsRate_IsTakenForTwentyFiveFramesASecond()
    {
        var clip = new GifClip(4, 0, 100, 100);
        Assert.Equal(GifClip.AssumedFramesPerSecond, clip.SourceFramesPerSecond);
        Assert.Equal(100, clip.SourceFrames);
    }

    [Fact]
    public void TheRangeOfFrames_IsInSeconds_AndTheLastFrameOfTheVideoLeavesTheEndOpen()
    {
        var range = GifRange.FromFrames(Video, 25, 74);

        Assert.Equal(1.0, range.StartSeconds, 6);
        Assert.Equal(3.0, range.EndSeconds!.Value, 6); // frame 74 ends at 75 / 25
        Assert.Null(GifRange.FromFrames(Video, 25, 249).EndSeconds); // the last frame: to the end of the video, whatever the length says to a thousandth
        Assert.Equal(GifRange.All, GifRange.FromFrames(Video, 0, 249));
        Assert.True(GifRange.FromFrames(Video, 0, 249).Equals(GifRange.All));
    }

    [Fact]
    public void TheRangeOfFrames_IsPutRightWhenItIsNot()
    {
        Assert.Equal(GifRange.FromFrames(Video, 249, 249), GifRange.FromFrames(Video, 400, 900)); // beyond the end
        Assert.Equal(GifRange.FromFrames(Video, 0, 0), GifRange.FromFrames(Video, -5, -2)); // before the start
        Assert.Equal(GifRange.FromFrames(Video, 100, 100), GifRange.FromFrames(Video, 100, 40)); // the last before the first
        Assert.Equal(GifRange.All, GifRange.FromFrames(new GifClip(0, 25, 100, 100), 3, 9)); // no length, no frames to count
    }

    // ── The plan ────────────────────────────────────────────────────────────

    [Fact]
    public void AllOfTheVideo_IsItsLengthTimesTheRateOfTheGif()
    {
        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: 0.1));

        Assert.Equal(0, plan.StartSeconds);
        Assert.Null(plan.EndSeconds);
        Assert.Equal(10, plan.LengthSeconds);
        Assert.Equal(100, plan.FrameCount);
    }

    // The video is 10 seconds long. A frame is taken at the start and then every frame time, so a frame time that does not divide the length leaves a last frame that is shown for less than
    // its time: 10 / 3 is 3.33, which is 4 frames (at 0, 3, 6 and 9 seconds)
    [Theory]
    [InlineData(1.0, 10)]
    [InlineData(0.1, 100)]
    [InlineData(0.08, 125)]
    [InlineData(0.02, 500)]
    [InlineData(0.5, 20)]
    [InlineData(2.5, 4)]
    [InlineData(3.0, 4)]
    [InlineData(4.0, 3)]
    [InlineData(10.0, 1)]
    [InlineData(20.0, 1)]
    [InlineData(60.0, 1)]
    public void TheFramesOfTheGif_FollowTheTimeOfAFrame(double frame, int frames) =>
        Assert.Equal(frames, GifPlan.Make(Video, GifRange.All, Settings(frame: frame)).FrameCount);

    [Theory]
    [InlineData(5.0, 2.0, 3)] // at 0, 2 and 4
    [InlineData(3.5, 2.0, 2)]
    [InlineData(4.0, 3.0, 2)] // at 0 and 3: the second is shown for a second only
    [InlineData(2.0, 5.0, 1)] // shorter than a frame: the frame at the start
    [InlineData(6.0, 2.0, 3)]
    [InlineData(7.0, 2.0, 4)]
    [InlineData(12.0, 5.0, 3)]
    [InlineData(5.0, 0.3, 17)]
    [InlineData(4.0, 0.1, 40)]
    [InlineData(0.56, 0.08, 7)] // 0.56 / 0.08 is 7.000000000000001 in a double, and is still 7 (a frame that would begin after the end is not taken)
    [InlineData(1.12, 0.02, 56)]
    [InlineData(1.32, 0.12, 11)]
    [InlineData(3.04, 0.1, 31)]
    public void APartOfTheVideo_HasAFrameAtItsStart_AndOneEveryFrameTime(double length, double frame, int frames)
    {
        var clip = new GifClip(length, 25, 320, 240);

        Assert.Equal(frames, GifPlan.Make(clip, GifRange.All, Settings(frame: frame)).FrameCount);
    }

    [Theory]
    [InlineData(0.1, 4.0)]
    [InlineData(1.5, 4.5)] // 3 frames, each shown for its 1.5 seconds: more than the 4 seconds of the video
    [InlineData(2.5, 5.0)]
    [InlineData(10.0, 10.0)] // a part that is shorter than a frame time still shows its frame for the whole time
    public void TheGifPlays_ItsFramesTimesTheTimeOfAFrame(double frame, double seconds)
    {
        var plan = GifPlan.Make(new GifClip(4, 25, 320, 240), GifRange.All, Settings(frame: frame));

        Assert.Equal(seconds, plan.PlaySeconds, 6);
    }

    // ── How fast it plays ───────────────────────────────────────────────────

    [Fact]
    public void WithoutADelay_EachFrameIsShownAsLongAsTheStretchOfVideoItStandsFor_SoTheGifPlaysAtTheSpeedOfTheVideo()
    {
        Assert.Null(new GifSettings().DelaySeconds);

        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: 0.5));

        Assert.Equal(0.5, plan.DelaySeconds);
        Assert.Equal(1.0, plan.Speed);
        Assert.Equal(20, plan.FrameCount);
        Assert.Equal(10.0, plan.PlaySeconds, 6);
    }

    [Fact]
    public void ADelayShorterThanTheDistanceOfTheFrames_PlaysTheGifFaster()
    {
        // A frame every half second of the video, each shown for a tenth: ten seconds of video play in two
        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: 0.5, delay: 0.1));

        Assert.Equal(20, plan.FrameCount);
        Assert.Equal(0.1, plan.DelaySeconds);
        Assert.Equal(2.0, plan.PlaySeconds, 6);
        Assert.Equal(5.0, plan.Speed, 6);
    }

    [Fact]
    public void ADelayLongerThanTheDistanceOfTheFrames_PlaysTheGifSlower()
    {
        // Every frame of a video of 25 a second (0.04), each shown for a tenth: 10 seconds of video play in 25
        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: 0.04, delay: 0.1));

        Assert.Equal(250, plan.FrameCount);
        Assert.Equal(25.0, plan.PlaySeconds, 6);
        Assert.Equal(0.4, plan.Speed, 6);
    }

    [Fact]
    public void TheFramesAreRetimed_OnlyWhenTheyAreShownForAnotherTimeThanTheyWereTakenApart()
    {
        string Filter(GifSettings settings) => After(GifPlan.Make(Video, GifRange.All, settings).GifArguments("v.mp4", "p.png", "o.gif"), "-lavfi");

        // fps first (the frames are taken), then a fine time base and setpts (they get their new times), then scale
        Assert.StartsWith("fps=1/0.5:round=up,settb=1/1000,setpts=N*0.1/TB,scale='min(640,iw)':-1:flags=lanczos[x];", Filter(Settings(frame: 0.5, delay: 0.1)));
        Assert.StartsWith("fps=1/0.04:round=up,settb=1/1000,setpts=N*0.1/TB,", Filter(Settings(frame: 0.04, delay: 0.1)));
        Assert.DoesNotContain("setpts", Filter(Settings(frame: 0.1)));
        Assert.DoesNotContain("setpts", Filter(Settings(frame: 0.1, delay: 0.1))); // the same time: nothing to change
        Assert.DoesNotContain("setpts", Filter(Settings(frame: 0.5, delay: 0.5)));
        Assert.StartsWith("fps=1/0.5:round=up,settb=1/1000,setpts=N*0.1/TB[x];", Filter(Settings(frame: 0.5, delay: 0.1, original: true)));
    }

    [Fact]
    public void TheGifRun_KeepsTheTimesOfTheFrames_SoNoneIsDroppedForBeingTooClose()
    {
        var arguments = GifPlan.Make(Video, GifRange.All, Settings(frame: 0.5, delay: 0.1)).GifArguments("v.mp4", "p.png", "o.gif");

        Assert.Equal("passthrough", After(arguments, "-fps_mode"));
    }

    [Fact]
    public void ThePaletteIsFoundFromTheFramesThatAreTaken_WhateverTheyAreShownFor()
    {
        var arguments = GifPlan.Make(Video, GifRange.All, Settings(frame: 0.5, delay: 0.1)).PaletteArguments("v.mp4", "p.png");

        Assert.DoesNotContain("setpts", After(arguments, "-vf"));
        Assert.StartsWith("fps=1/0.5:round=up,scale=", After(arguments, "-vf"));
    }

    [Theory]
    [InlineData(0.1, null, "10")]
    [InlineData(2.5, null, "250")]
    [InlineData(0.5, 0.1, "10")] // the last frame is shown for the delay, not for the distance it was taken at
    [InlineData(0.04, 0.12, "12")]
    [InlineData(0.1, 60.0, "6000")]
    public void TheLastFrame_IsShownForTheDelay_InHundredthsOfASecond(double frame, double? delay, string hundredths)
    {
        var arguments = GifPlan.Make(Video, GifRange.All, Settings(frame: frame, delay: delay)).GifArguments("v.mp4", "p.png", "o.gif");

        Assert.Equal(hundredths, After(arguments, "-final_delay"));
    }

    [Fact]
    public void TheDelay_IsKeptToHundredthsOfASecond_AndInItsRange()
    {
        Assert.Equal(0.13, GifPlan.Make(Video, GifRange.All, Settings(delay: 0.126)).DelaySeconds);
        Assert.Equal(0.02, GifPlan.Make(Video, GifRange.All, Settings(delay: 0.001)).DelaySeconds); // 50 frames a second at the most
        Assert.Equal(60, GifPlan.Make(Video, GifRange.All, Settings(delay: 600)).DelaySeconds);
        Assert.Null(GifPlan.Make(Video, GifRange.All, Settings(delay: double.NaN)).Settings.DelaySeconds); // not a number: as fast as the video
        Assert.Equal(0.1, GifPlan.Make(Video, GifRange.All, Settings(delay: double.NaN)).DelaySeconds);
    }

    [Fact]
    public void TheTimeOfAFrame_IsKeptToHundredthsOfASecond_AsAGifIs()
    {
        Assert.Equal(0.13, GifPlan.Make(Video, GifRange.All, Settings(frame: 0.126)).Settings.FrameSeconds);
        Assert.Equal(0.12, GifPlan.Make(Video, GifRange.All, Settings(frame: 0.124)).Settings.FrameSeconds);
        Assert.Equal(0.02, GifPlan.Make(Video, GifRange.All, Settings(frame: 0.001)).Settings.FrameSeconds); // 50 frames a second at the most
        Assert.Equal(60, GifPlan.Make(Video, GifRange.All, Settings(frame: 600)).Settings.FrameSeconds);
        Assert.Equal(GifSettings.DefaultFrameSeconds, GifPlan.Make(Video, GifRange.All, Settings(frame: double.NaN)).Settings.FrameSeconds);
    }

    [Fact]
    public void ARange_IsTheLengthOfTheGif()
    {
        var plan = GifPlan.Make(Video, new GifRange(2, 5.5), Settings(frame: 0.1));

        Assert.Equal(2, plan.StartSeconds);
        Assert.Equal(5.5, plan.EndSeconds);
        Assert.Equal(3.5, plan.LengthSeconds);
        Assert.Equal(35, plan.FrameCount);
    }

    [Fact]
    public void ARangeFromAPlaceToTheEnd_RunsToTheEnd()
    {
        var plan = GifPlan.Make(Video, new GifRange(4, null), Settings(frame: 0.1));

        Assert.Null(plan.EndSeconds);
        Assert.Equal(6, plan.LengthSeconds);
        Assert.Equal(60, plan.FrameCount);
    }

    [Fact]
    public void AnEndThatIsPastTheVideo_IsTheEnd_AndOneThatIsNearlyTheEndToo()
    {
        Assert.Null(GifPlan.Make(Video, new GifRange(1, 99), Settings()).EndSeconds);
        Assert.Null(GifPlan.Make(Video, new GifRange(1, 9.9996), Settings()).EndSeconds);
        Assert.Equal(9.99, GifPlan.Make(Video, new GifRange(1, 9.99), Settings()).EndSeconds);
    }

    [Fact]
    public void ARangeIsAtLeastOneFrameOfTheVideo_WhateverTheTimeOfAFrameOfTheGif()
    {
        foreach (var frame in new[] { 0.1, 2.5, 60 })
        {
            var plan = GifPlan.Make(Video, new GifRange(3, 3), Settings(frame: frame));

            Assert.Equal(3.04, plan.EndSeconds!.Value, 6); // one frame of the video, which has 25 a second
            Assert.Equal(1, plan.FrameCount);

            // And a start at the very end moves back so that there is a frame to take: not by a whole frame time of the GIF, which may be a minute
            var late = GifPlan.Make(Video, new GifRange(10, 12), Settings(frame: frame));
            Assert.Equal(9.96, late.StartSeconds, 6);
            Assert.Equal(1, late.FrameCount);
        }
    }

    [Fact]
    public void ARangeThatIsNonsense_IsPutRight()
    {
        var plan = GifPlan.Make(Video, new GifRange(-4, double.NaN), Settings());

        Assert.Equal(0, plan.StartSeconds);
        Assert.Null(plan.EndSeconds);
        Assert.Equal(10, plan.LengthSeconds);
    }

    [Fact]
    public void AVideoOfAnUnknownLength_HasNoFrameCount_UnlessTheRangeSaysWhereItEnds()
    {
        var unknown = new GifClip(0, 25, 640, 360);

        var all = GifPlan.Make(unknown, GifRange.All, Settings());
        Assert.Equal(0, all.FrameCount);
        Assert.Equal(0, all.LengthSeconds);

        var part = GifPlan.Make(unknown, new GifRange(2, 5), Settings(frame: 0.1));
        Assert.Equal(30, part.FrameCount);
        Assert.Equal(3, part.LengthSeconds);
    }

    // ── The size ────────────────────────────────────────────────────────────

    [Fact]
    public void TheSize_IsTheWidthAsked_InTheProportionsOfTheVideo()
    {
        var plan = GifPlan.Make(Video, GifRange.All, Settings(width: 640));

        Assert.Equal(640, plan.Width);
        Assert.Equal(360, plan.Height);
    }

    [Fact]
    public void AVideoThatIsNarrowerThanTheWidth_IsNotMadeWider()
    {
        var plan = GifPlan.Make(new GifClip(5, 25, 320, 240), GifRange.All, Settings(width: 640));

        Assert.Equal(320, plan.Width);
        Assert.Equal(240, plan.Height);
    }

    [Fact]
    public void TheOriginalSize_IsTheSizeOfTheVideo()
    {
        var plan = GifPlan.Make(Video, GifRange.All, Settings(width: 320, original: true));

        Assert.Equal(1920, plan.Width);
        Assert.Equal(1080, plan.Height);
    }

    [Theory]
    [InlineData(1920, 1080, 641, 361)] // 360.56
    [InlineData(1280, 720, 500, 281)] // 281.25
    [InlineData(1000, 3, 640, 2)] // 1.92
    [InlineData(100000, 3, 640, 1)] // never none
    public void TheHeight_IsRounded_AndIsNeverNothing(int sourceWidth, int sourceHeight, int width, int height)
    {
        var plan = GifPlan.Make(new GifClip(5, 25, sourceWidth, sourceHeight), GifRange.All, Settings(width: width));

        Assert.Equal(Math.Min(width, sourceWidth), plan.Width);
        Assert.Equal(height, plan.Height);
    }

    [Fact]
    public void AVideoThatDoesNotSayItsSize_HasNoSizeInThePlan()
    {
        var plan = GifPlan.Make(new GifClip(5, 25, 0, 0), GifRange.All, Settings());

        Assert.Equal(0, plan.Width);
        Assert.Equal(0, plan.Height);
    }

    // ── What ffmpeg is told ─────────────────────────────────────────────────

    private static string After(string[] arguments, string name) => arguments[Array.IndexOf(arguments, name) + 1];

    [Fact]
    public void TheFirstRun_FindsThePalette_OfTheFramesThatAreUsed()
    {
        var plan = GifPlan.Make(Video, new GifRange(2, 5.5), Settings(frame: 0.08, width: 480, colors: 64));
        var arguments = plan.PaletteArguments("C:\\v\\clip.mp4", "C:\\t\\palette.png");

        Assert.Equal("2", After(arguments, "-ss"));
        Assert.Equal("3.5", After(arguments, "-t"));
        Assert.True(Array.IndexOf(arguments, "-ss") < Array.IndexOf(arguments, "-i"), "the place is asked before the video, so it is not read up to it");
        Assert.True(Array.IndexOf(arguments, "-t") < Array.IndexOf(arguments, "-i"));
        Assert.Equal("C:\\v\\clip.mp4", After(arguments, "-i"));
        Assert.Equal("fps=1/0.08:round=up,scale='min(480,iw)':-1:flags=lanczos,palettegen=max_colors=64:stats_mode=full", After(arguments, "-vf"));
        Assert.Equal("C:\\t\\palette.png", arguments[^1]);
        Assert.Contains("-an", arguments);
        Assert.Contains("-nostdin", arguments);
    }

    [Fact]
    public void TheSecondRun_DrawsTheFrames_WithThatPalette()
    {
        var plan = GifPlan.Make(Video, new GifRange(2, 5.5), Settings(frame: 0.08, width: 480, dither: GifDither.Bayer));
        var arguments = plan.GifArguments("C:\\v\\clip.mp4", "C:\\t\\palette.png", "C:\\out\\clip.gif");

        var inputs = arguments.Select((argument, index) => (argument, index)).Where(x => x.argument == "-i").Select(x => arguments[x.index + 1]).ToArray();
        Assert.Equal(["C:\\v\\clip.mp4", "C:\\t\\palette.png"], inputs);
        Assert.Equal("fps=1/0.08:round=up,scale='min(480,iw)':-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle", After(arguments, "-lavfi"));
        Assert.Equal("gif", After(arguments, "-f"));
        Assert.Equal("C:\\out\\clip.gif", arguments[^1]);

        // The part is asked for the video only: the palette is a picture of its own, whole
        var video = Array.IndexOf(arguments, "-i");
        var palette = Array.LastIndexOf(arguments, "-i");
        Assert.True(Array.IndexOf(arguments, "-ss") < video);
        Assert.True(Array.IndexOf(arguments, "-t") < video);
        Assert.DoesNotContain("-ss", arguments[(video + 2)..palette]);
    }

    [Fact]
    public void AllOfTheVideo_HasNoPlaceAndNoLength_InTheArguments()
    {
        var arguments = GifPlan.Make(Video, GifRange.All, Settings()).GifArguments("v.mp4", "p.png", "o.gif");

        Assert.DoesNotContain("-ss", arguments);
        Assert.DoesNotContain("-t", arguments);
    }

    [Fact]
    public void ARangeToTheEnd_HasAPlace_ButNoLength()
    {
        var arguments = GifPlan.Make(Video, new GifRange(4, null), Settings()).GifArguments("v.mp4", "p.png", "o.gif");

        Assert.Equal("4", After(arguments, "-ss"));
        Assert.DoesNotContain("-t", arguments);
    }

    [Fact]
    public void ThePlaceAndTheLength_AreWrittenWithADotWhateverTheCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("uk-UA");
        try
        {
            var arguments = GifPlan.Make(Video, new GifRange(1.25, 3.75), Settings(frame: 0.1)).GifArguments("v.mp4", "p.png", "o.gif");

            Assert.Equal("1.25", After(arguments, "-ss"));
            Assert.Equal("2.5", After(arguments, "-t"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheOriginalSize_HasNoScaling()
    {
        var plan = GifPlan.Make(Video, GifRange.All, Settings(frame: 2.5, original: true));

        Assert.Equal("fps=1/2.5:round=up", plan.Chain); // a frame every two and a half seconds, and the one at the start
        Assert.DoesNotContain("scale", plan.GifArguments("v.mp4", "p.png", "o.gif").Single(a => a.Contains("paletteuse")));
    }

    [Theory]
    [InlineData(GifDither.None, "dither=none:diff_mode=rectangle")]
    [InlineData(GifDither.Bayer, "dither=bayer:bayer_scale=3:diff_mode=rectangle")]
    [InlineData(GifDither.Sierra, "dither=sierra2_4a:diff_mode=rectangle")]
    public void TheDithering_IsPassedToThePalette(GifDither dither, string expected) =>
        Assert.Contains(expected, After(GifPlan.Make(Video, GifRange.All, Settings(dither: dither)).GifArguments("v.mp4", "p.png", "o.gif"), "-lavfi"));

    [Theory]
    [InlineData(GifLoop.Forever, "0")]
    [InlineData(GifLoop.Once, "-1")]
    public void TheRepeat_IsPassedToTheFile(GifLoop loop, string expected) =>
        Assert.Equal(expected, After(GifPlan.Make(Video, GifRange.All, Settings(loop: loop)).GifArguments("v.mp4", "p.png", "o.gif"), "-loop"));

    [Fact]
    public void ThePaths_AreOneArgumentEach_WhateverIsInThem()
    {
        var arguments = GifPlan.Make(Video, GifRange.All, Settings()).GifArguments("C:\\My Videos\\a 'b' \"c\".mp4", "C:\\Temp Dir\\p.png", "C:\\My GIFs\\o o.gif");

        Assert.Contains("C:\\My Videos\\a 'b' \"c\".mp4", arguments);
        Assert.Contains("C:\\Temp Dir\\p.png", arguments);
        Assert.Equal("C:\\My GIFs\\o o.gif", arguments[^1]);
    }

    [Fact]
    public void TheSettings_AreBroughtIntoTheirRanges_BeforeTheyAreUsed()
    {
        var plan = GifPlan.Make(Video, GifRange.All, new GifSettings { FrameSeconds = 0.001, Width = 1, Colors = 9999 });

        Assert.Equal(GifSettings.MinFrameSeconds, plan.Settings.FrameSeconds);
        Assert.Equal(GifSettings.MinWidth, plan.Settings.Width);
        Assert.Equal(GifSettings.MaxColors, plan.Settings.Colors);
        Assert.Equal(GifSettings.MinWidth, plan.Width);
        Assert.Contains("max_colors=256", After(plan.PaletteArguments("v", "p"), "-vf"));
    }
}
