using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class VideoSequenceTests
{
    private static string[] Photos(int count) => [.. Enumerable.Range(1, count).Select(i => $"p{i}.jpg")];

    // ── DrawRandomSamples ───────────────────────────────────────────────────

    [Fact]
    public void DrawRandomSamples_OneForEachPhoto_FromZeroUpToButNotIncludingOne()
    {
        var samples = VideoGenerationService.DrawRandomSamples(1000, new Random(1));

        Assert.Equal(1000, samples.Length);
        Assert.All(samples, s => Assert.InRange(s, 0.0, 0.9999999999999999));
        Assert.True(samples.Distinct().Count() > 990);
    }

    [Fact]
    public void DrawRandomSamples_SameSeed_SameSamples_AnotherSeed_Others()
    {
        Assert.Equal(VideoGenerationService.DrawRandomSamples(20, new Random(7)), VideoGenerationService.DrawRandomSamples(20, new Random(7)));
        Assert.NotEqual(VideoGenerationService.DrawRandomSamples(20, new Random(7)), VideoGenerationService.DrawRandomSamples(20, new Random(8)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void DrawRandomSamples_NoPhotos_NoSamples(int count)
    {
        Assert.Empty(VideoGenerationService.DrawRandomSamples(count));
    }

    // ── BuildFrameDurations: the photos only ────────────────────────────────

    [Fact]
    public void BuildFrameDurations_NoPhotos_NoDurations()
    {
        Assert.Empty(VideoGenerationService.BuildFrameDurations(0, 1, 3));
        Assert.Empty(VideoGenerationService.BuildFrameDurations(0, 1, 3, [0.5]));
    }

    [Fact]
    public void BuildFrameDurations_OnePhoto_ShowsForTheStartTime()
    {
        Assert.Equal([2.0], VideoGenerationService.BuildFrameDurations(1, 2, 5));
    }

    [Fact]
    public void BuildFrameDurations_SeveralPhotos_GoLinearlyFromStartToEnd()
    {
        Assert.Equal([1.0, 2.0, 3.0], VideoGenerationService.BuildFrameDurations(3, 1, 3));
    }

    [Fact]
    public void BuildFrameDurations_CanGetShorter()
    {
        Assert.Equal([3.0, 2.0, 1.0], VideoGenerationService.BuildFrameDurations(3, 3, 1));
    }

    [Fact]
    public void BuildFrameDurations_WithSamples_EachPhotoIsAtItsPositionInTheRange()
    {
        // Range 2..6: position 0 is 2 s, 0.5 is 4 s, 0.25 is 3 s
        Assert.Equal([2.0, 4.0, 3.0], VideoGenerationService.BuildFrameDurations(3, 2, 6, [0.0, 0.5, 0.25]));
    }

    [Fact]
    public void BuildFrameDurations_WithSamples_StartAndEndTheOtherWayRound_IsTheSameRange()
    {
        Assert.Equal(
            VideoGenerationService.BuildFrameDurations(3, 2, 6, [0.0, 0.5, 0.25]),
            VideoGenerationService.BuildFrameDurations(3, 6, 2, [0.0, 0.5, 0.25]));
    }

    [Fact]
    public void BuildFrameDurations_WithSamples_ChangingTheRangeMovesTheTimesAlongAndKeepsTheirOrder()
    {
        double[] samples = [0.9, 0.1, 0.5];

        var narrow = VideoGenerationService.BuildFrameDurations(3, 1, 2, samples);
        var wide = VideoGenerationService.BuildFrameDurations(3, 1, 3, samples);

        // The shortest stays the shortest, the longest the longest: the same draw, rescaled
        Assert.Equal([1.9, 1.1, 1.5], narrow.Select(d => Math.Round(d, 9)));
        Assert.Equal([2.8, 1.2, 2.0], wide.Select(d => Math.Round(d, 9)));
    }

    [Fact]
    public void BuildFrameDurations_WithSamples_AllWithinTheRange()
    {
        var durations = VideoGenerationService.BuildFrameDurations(500, 0.5, 2.5, VideoGenerationService.DrawRandomSamples(500, new Random(1)));

        Assert.Equal(500, durations.Count);
        Assert.All(durations, d => Assert.InRange(d, 0.5, 2.5));
        Assert.True(durations.Distinct().Count() > 400);
    }

    [Fact]
    public void BuildFrameDurations_WithSamples_StartEqualToEnd_IsThatTimeForAll()
    {
        Assert.Equal([2.0, 2.0, 2.0], VideoGenerationService.BuildFrameDurations(3, 2, 2, [0.1, 0.5, 0.9]));
    }

    [Fact]
    public void BuildFrameDurations_WithSamples_AMissingSampleGetsARandomOne_InTheRange()
    {
        var durations = VideoGenerationService.BuildFrameDurations(5, 1, 3, [0.0, 0.75]);

        Assert.Equal(5, durations.Count);
        Assert.Equal(1.0, durations[0]);
        Assert.Equal(2.5, durations[1]);
        Assert.All(durations, d => Assert.InRange(d, 1.0, 3.0));
    }

    [Fact]
    public void BuildFrameDurations_WithMoreSamplesThanPhotos_UsesTheFirstOnes()
    {
        // A photo can be gone from the disk after the samples were drawn for the listed ones
        Assert.Equal([1.0, 3.0], VideoGenerationService.BuildFrameDurations(2, 1, 5, [0.0, 0.5, 0.99, 0.2]));
    }

    // ── PhotoSeconds / EstimateLength agree with what is really built ───────

    public static TheoryData<int, double, double, bool> Settings() => new()
    {
        { 0, 1, 3, false },
        { 1, 0.5, 0.5, false },
        { 1, 2.0, 5.0, false },
        { 2, 2.0, 5.0, false },
        { 3, 1.0, 3.0, false },
        { 3, 3.0, 1.0, false },
        { 40, 0.04, 60.0, false },
        { 1000, 0.5, 0.5, false },
        { 1, 1.0, 3.0, true },
        { 2, 3.0, 1.0, true },
        { 7, 0.25, 4.0, true },
        { 1000, 0.04, 60.0, true },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void PhotoSeconds_IsWhatTheTimesAddUpTo(int photos, double start, double end, bool random)
    {
        var samples = random ? VideoGenerationService.DrawRandomSamples(photos, new Random(photos)) : null;

        Assert.Equal(
            VideoGenerationService.BuildFrameDurations(photos, start, end, samples).Sum(),
            VideoGenerationService.PhotoSeconds(photos, start, end, samples),
            precision: 6);
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void EstimateLength_PlayedOnce_IsTheLengthOfTheBuiltSequence(int photos, double start, double end, bool random)
    {
        var samples = random ? VideoGenerationService.DrawRandomSamples(photos, new Random(photos + 1)) : null;

        foreach (double? cover in new double?[] { null, 3.0 })
        {
            var sequence = VideoGenerationService.BuildSequence(Photos(photos), cover is null ? null : "cover.jpg", cover ?? 0, start, end, samples, false, true, null);
            var estimate = VideoGenerationService.EstimateLength(photos, start, end, samples, cover, false, null);

            if (photos == 0 && cover is null)
                Assert.Equal(0, estimate);
            else
                Assert.Equal(sequence.ExpectedSeconds, estimate, precision: 6);
        }
    }

    [Fact]
    public void EstimateLength_PlayedOnce_AddsTheLastFrame()
    {
        // 3 photos of 2 s: 6 s, and the last photo is listed once more: one frame
        Assert.Equal(6 + VideoGenerationService.LastFrameSeconds, VideoGenerationService.EstimateLength(3, 2, 2, null, null, false, null), precision: 9);
    }

    [Fact]
    public void EstimateLength_WithACover_AddsItsTime()
    {
        Assert.Equal(6 + 3 + VideoGenerationService.LastFrameSeconds, VideoGenerationService.EstimateLength(3, 2, 2, null, 3, false, null), precision: 9);
    }

    [Fact]
    public void EstimateLength_Random_IsTheSumOfTheTimesDrawn_NotAGuess()
    {
        double[] samples = [0.0, 0.75, 0.5];

        // 1 + 2.5 + 2 (range 1..3)
        Assert.Equal(5.5 + VideoGenerationService.LastFrameSeconds, VideoGenerationService.EstimateLength(3, 1, 3, samples, null, false, null), precision: 6);
    }

    [Fact]
    public void EstimateLength_NoPhotosAndNoCover_IsNothing_ACoverAlone_IsItsTime()
    {
        Assert.Equal(0, VideoGenerationService.EstimateLength(0, 1, 3, null, null, false, null));
        Assert.Equal(4 + VideoGenerationService.LastFrameSeconds, VideoGenerationService.EstimateLength(0, 1, 3, null, 4, false, null), precision: 9);
    }

    // ── what becomes of the audio when the photos play once ─────────────────

    [Theory]
    [InlineData(60, 200, AudioFit.Cut)]
    [InlineData(200, 60, AudioFit.Repeated)]
    [InlineData(60, 60.5, AudioFit.Same)]
    [InlineData(60, 59.2, AudioFit.Same)]
    [InlineData(60, 61.5, AudioFit.Cut)]
    [InlineData(60, 58.0, AudioFit.Repeated)]
    public void FitOfAudio_IsCutWhenLongerAndRepeatedWhenShorter(double video, double audio, AudioFit expected)
    {
        Assert.Equal(expected, VideoGenerationService.FitOfAudio(video, audio));
    }

    // ── BuildSequence ───────────────────────────────────────────────────────

    [Fact]
    public void BuildSequence_WithoutACover_IsThePhotosWithTheirTimes()
    {
        var sequence = VideoGenerationService.BuildSequence(["a", "b", "c"], null, 0, 1, 3, null, false, true, null);

        Assert.Equal(["a", "b", "c"], sequence.Files);
        Assert.Equal([1.0, 2.0, 3.0], sequence.Durations);
        Assert.False(sequence.AudioEndsVideo);
        Assert.Equal(6 + VideoGenerationService.LastFrameSeconds, sequence.ExpectedSeconds, precision: 9);
    }

    [Fact]
    public void BuildSequence_TheCoverIsFirst_WithItsOwnTime_AndIsNotPartOfTheRange()
    {
        var sequence = VideoGenerationService.BuildSequence(["a", "b", "c"], "cover", 5, 1, 3, null, false, true, null);

        Assert.Equal(["cover", "a", "b", "c"], sequence.Files);
        Assert.Equal([5.0, 1.0, 2.0, 3.0], sequence.Durations);
    }

    [Fact]
    public void BuildSequence_AOnePhotoVideoWithACover_ShowsThePhotoForTheStartTime()
    {
        var sequence = VideoGenerationService.BuildSequence(["a"], "cover", 3, 2, 5, null, false, true, null);

        Assert.Equal([3.0, 2.0], sequence.Durations);
    }

    [Fact]
    public void BuildSequence_OnlyACover_IsTheCover()
    {
        var sequence = VideoGenerationService.BuildSequence([], "cover", 3, 2, 5, null, false, true, null);

        Assert.Equal(["cover"], sequence.Files);
        Assert.Equal([3.0], sequence.Durations);
    }

    [Fact]
    public void BuildSequence_WithSamples_GivesThePhotosTheirDrawnTimes_AndTheCoverItsOwn()
    {
        var sequence = VideoGenerationService.BuildSequence(["a", "b", "c"], "cover", 5, 2, 6, [0.0, 0.5, 0.25], false, true, null);

        Assert.Equal([5.0, 2.0, 4.0, 3.0], sequence.Durations);
    }

    [Fact]
    public void BuildSequence_NotAskedToRepeat_ListsEverythingOnce_WhateverTheAudio()
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(3), "cover", 2, 1, 1, null, repeatToAudio: false, repeatAudio: true, audioSeconds: 600);

        Assert.Equal(4, sequence.Files.Count);
        Assert.False(sequence.AudioEndsVideo);
    }

    [Fact]
    public void BuildSequence_Repeat_ListsThePhotosAgainUntilTheyAreLongerThanTheAudio_ButNotTheCover()
    {
        // The cover 2 s, 3 photos of 1 s: 5 s once through; the audio is 20 s, so photos (3 s a pass) go on until more than 21 s in all
        var sequence = VideoGenerationService.BuildSequence(Photos(3), "cover", 2, 1, 1, null, repeatToAudio: true, repeatAudio: true, audioSeconds: 20);

        Assert.True(sequence.AudioEndsVideo);
        Assert.Equal(20, sequence.ExpectedSeconds);
        Assert.Equal("cover", sequence.Files[0]);
        Assert.Equal(1, sequence.Files.Count(f => f == "cover"));
        Assert.Equal(sequence.Files.Count, sequence.Durations.Count);

        var total = sequence.Durations.Sum();
        Assert.True(total >= 20 + VideoGenerationService.RepeatMarginSeconds, $"total {total}");
        // Not a pass more than needed
        Assert.True(total - 3 < 20 + VideoGenerationService.RepeatMarginSeconds, $"total {total}");
        Assert.Equal(0, (sequence.Files.Count - 1) % 3); // whole passes of the 3 photos
    }

    [Fact]
    public void BuildSequence_Repeat_EachPassIsTheSameSequenceWithTheSameTimes()
    {
        var samples = VideoGenerationService.DrawRandomSamples(4, new Random(5));
        var sequence = VideoGenerationService.BuildSequence(Photos(4), null, 0, 1, 3, samples, true, true, 60);

        var pass = sequence.Files.Take(4).ToList();
        var passTimes = sequence.Durations.Take(4).ToList();
        for (var start = 0; start < sequence.Files.Count; start += 4)
        {
            Assert.Equal(pass, sequence.Files.Skip(start).Take(4));
            Assert.Equal(passTimes, sequence.Durations.Skip(start).Take(4));
        }
    }

    [Fact]
    public void BuildSequence_Repeat_TheAudioIsOnlyLongerByLessThanOnePass_OnePassMoreIsListed()
    {
        // 10 s once through, the audio 10.5 s: one more pass (10 s) takes it past 11.5 s
        var sequence = VideoGenerationService.BuildSequence(Photos(5), null, 0, 2, 2, null, true, true, 10.5);

        Assert.True(sequence.AudioEndsVideo);
        Assert.Equal(10, sequence.Files.Count);
    }

    [Theory]
    [InlineData(10, false)]   // the audio is as long as the photos
    [InlineData(5, false)]    // shorter: it is played over and over, the photos end the video
    [InlineData(10.01, true)] // longer
    public void BuildSequence_Repeat_OnlyRepeatsForAnAudioLongerThanThePhotos(double audioSeconds, bool repeats)
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(5), null, 0, 2, 2, null, true, true, audioSeconds);

        Assert.Equal(repeats, sequence.AudioEndsVideo);
        if (!repeats)
            Assert.Equal(5, sequence.Files.Count);
    }

    [Fact]
    public void BuildSequence_Repeat_TheCoverCountsTowardsWhatTheAudioHasToBeLongerThan()
    {
        // Photos 6 s, cover 2 s: 8 s once through. An audio of 7 s is shorter than that: no repeat
        Assert.False(VideoGenerationService.BuildSequence(Photos(3), "cover", 2, 2, 2, null, true, true, 7).AudioEndsVideo);
        Assert.True(VideoGenerationService.BuildSequence(Photos(3), "cover", 2, 2, 2, null, true, true, 9).AudioEndsVideo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void BuildSequence_Repeat_NoAudioLengthKnown_PlaysOnce(double? audioSeconds)
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(3), null, 0, 1, 1, null, true, true, audioSeconds);

        Assert.False(sequence.AudioEndsVideo);
        Assert.Equal(3, sequence.Files.Count);
    }

    [Fact]
    public void BuildSequence_Repeat_OnlyACover_HasNothingToRepeat()
    {
        var sequence = VideoGenerationService.BuildSequence([], "cover", 3, 1, 1, null, true, true, 100);

        Assert.False(sequence.AudioEndsVideo);
        Assert.Equal(["cover"], sequence.Files);
    }

    [Fact]
    public void BuildSequence_Repeat_ListingTooManyFiles_IsRefused()
    {
        // 0.04 s for one photo against 20 hours of audio: millions of entries
        var ex = Assert.Throws<InvalidOperationException>(() => VideoGenerationService.BuildSequence(["a"], null, 0, 0.04, 0.04, null, true, true, 20 * 3600));

        Assert.Contains("too many", ex.Message);
    }

    [Fact]
    public void BuildSequence_Repeat_LongButNotTooManyFiles_IsFine()
    {
        // 0.04 s for one photo against an hour of audio: 90,000 entries
        var sequence = VideoGenerationService.BuildSequence(["a"], null, 0, 0.04, 0.04, null, true, true, 3600);

        Assert.True(sequence.AudioEndsVideo);
        Assert.InRange(sequence.Files.Count, 90_000, 91_000);
    }

    // ── EstimateLength with the audio ───────────────────────────────────────

    [Theory]
    [InlineData(3, 1.0, 1.0, null, 100.0)]
    [InlineData(3, 1.0, 1.0, 2.0, 100.0)]
    [InlineData(8, 0.5, 2.0, 3.0, 61.0)]
    [InlineData(8, 0.5, 2.0, null, 12.0)]
    [InlineData(5, 2.0, 2.0, null, 10.5)]
    public void EstimateLength_Repeat_IsTheLengthOfTheAudio_AsTheSequenceSays(int photos, double start, double end, double? cover, double audio)
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(photos), cover is null ? null : "cover", cover ?? 0, start, end, null, true, true, audio);
        var estimate = VideoGenerationService.EstimateLength(photos, start, end, null, cover, true, audio);

        Assert.True(sequence.AudioEndsVideo);
        Assert.Equal(sequence.ExpectedSeconds, estimate, precision: 6);
        Assert.Equal(audio, estimate, precision: 6);
    }

    [Theory]
    [InlineData(3, 1.0, 1.0, null, 2.0)]   // the audio is shorter: the photos play once
    [InlineData(3, 1.0, 1.0, null, 3.0)]   // exactly as long
    [InlineData(3, 1.0, 1.0, 2.0, 5.0)]    // exactly as long as cover + photos
    public void EstimateLength_Repeat_AnAudioThatIsNotLonger_IsTheLengthOfThePhotos(int photos, double start, double end, double? cover, double audio)
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(photos), cover is null ? null : "cover", cover ?? 0, start, end, null, true, true, audio);
        var estimate = VideoGenerationService.EstimateLength(photos, start, end, null, cover, true, audio);

        Assert.False(sequence.AudioEndsVideo);
        Assert.Equal(sequence.ExpectedSeconds, estimate, precision: 6);
    }

    [Fact]
    public void EstimateLength_PlayedOnce_TheAudioDoesNotChangeIt()
    {
        Assert.Equal(
            VideoGenerationService.EstimateLength(3, 1, 1, null, null, false, null),
            VideoGenerationService.EstimateLength(3, 1, 1, null, null, false, 600));
    }

    [Fact]
    public void EstimateLength_Repeat_WithoutAnAudioLength_IsThePhotosOnce()
    {
        Assert.Equal(
            VideoGenerationService.EstimateLength(3, 1, 1, null, null, false, null),
            VideoGenerationService.EstimateLength(3, 1, 1, null, null, true, null));
    }


    // ── what the audio does: repeat or once ─────────────────────────────────

    [Fact]
    public void BuildSequence_PhotosOnce_AudioRepeat_TheAudioLoops()
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(3), null, 0, 2, 2, null, repeatToAudio: false, repeatAudio: true, audioSeconds: 3);

        Assert.Equal(AudioPlayback.Loop, sequence.Audio);
        Assert.False(sequence.AudioEndsVideo);
    }

    [Fact]
    public void BuildSequence_PhotosOnce_AudioOnce_TheAudioPlaysOnceAndThenItIsSilent()
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(3), null, 0, 2, 2, null, repeatToAudio: false, repeatAudio: false, audioSeconds: 3);

        Assert.Equal(AudioPlayback.OnceThenSilence, sequence.Audio);
        Assert.Equal(3, sequence.Files.Count);
        Assert.Equal(6 + VideoGenerationService.LastFrameSeconds, sequence.ExpectedSeconds, precision: 9); // the short audio does not shorten the video
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void BuildSequence_PhotosRepeat_AnAudioLongerThanThePhotos_PlaysOnceAndEndsTheVideo_WhateverTheAudioIsSetTo(bool repeatToAudio, bool repeatAudio)
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(3), null, 0, 2, 2, null, repeatToAudio, repeatAudio, audioSeconds: 20);

        Assert.Equal(AudioPlayback.OnceEndsVideo, sequence.Audio);
        Assert.True(sequence.AudioEndsVideo);
        Assert.Equal(20, sequence.ExpectedSeconds);
    }

    [Theory]
    [InlineData(true, AudioPlayback.Loop)]
    [InlineData(false, AudioPlayback.OnceThenSilence)]
    public void BuildSequence_PhotosRepeat_AnAudioShorterThanThePhotos_IsLeftToTheAudiosOwnSetting(bool repeatAudio, AudioPlayback expected)
    {
        // Nothing to repeat for a short audio: the photos play once, and the audio does what it is set to
        var sequence = VideoGenerationService.BuildSequence(Photos(3), null, 0, 2, 2, null, repeatToAudio: true, repeatAudio, audioSeconds: 3);

        Assert.Equal(expected, sequence.Audio);
        Assert.Equal(3, sequence.Files.Count);
    }

    [Fact]
    public void BuildSequence_TheAudioSetting_DoesNotChangeThePictures()
    {
        var loop = VideoGenerationService.BuildSequence(Photos(4), "cover", 2, 1, 3, [0.1, 0.2, 0.3, 0.4], false, true, 9);
        var once = VideoGenerationService.BuildSequence(Photos(4), "cover", 2, 1, 3, [0.1, 0.2, 0.3, 0.4], false, false, 9);

        Assert.Equal(loop.Files, once.Files);
        Assert.Equal(loop.Durations, once.Durations);
        Assert.Equal(loop.ExpectedSeconds, once.ExpectedSeconds);
    }

    // ── the ffmpeg arguments ────────────────────────────────────────────────

    private static string Arguments(string? audio, AudioPlayback playback) =>
        VideoGenerationService.BuildFfmpegArguments(@"C:\t\list.txt", @"C:\v\out.mp4", 640, 360, "libx264", 23, audio, playback);

    [Fact]
    public void BuildFfmpegArguments_Loop_PlaysTheAudioOverAndOver_AndTheShorterOfTheTwoEndsTheVideo()
    {
        var arguments = Arguments(@"C:\a\song.mp3", AudioPlayback.Loop);

        Assert.Contains("-stream_loop -1 -i \"C:\\a\\song.mp3\"", arguments);
        Assert.DoesNotContain("apad", arguments);
        Assert.Contains("-shortest", arguments);
    }

    [Fact]
    public void BuildFfmpegArguments_OnceThenSilence_PlaysTheAudioOnce_AndPadsItSoThatItDoesNotEndTheVideo()
    {
        var arguments = Arguments(@"C:\a\song.mp3", AudioPlayback.OnceThenSilence);

        Assert.DoesNotContain("-stream_loop", arguments);
        Assert.Contains("-i \"C:\\a\\song.mp3\"", arguments);
        Assert.Contains("-af apad", arguments);
        Assert.Contains("-shortest", arguments); // the photos then end the video, the padding being endless
        // The filter is for the audio stream and comes before it is encoded
        Assert.True(arguments.IndexOf("-af apad", StringComparison.Ordinal) < arguments.IndexOf("-c:a aac", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFfmpegArguments_OnceEndsVideo_PlaysTheAudioOnce_WithoutPaddingOrLooping_SoThatItEndsTheVideo()
    {
        var arguments = Arguments(@"C:\a\song.mp3", AudioPlayback.OnceEndsVideo);

        Assert.DoesNotContain("-stream_loop", arguments);
        Assert.DoesNotContain("apad", arguments);
        Assert.Contains("-shortest", arguments);
        Assert.EndsWith("\"C:\\v\\out.mp4\"", arguments);
    }

    [Theory]
    [InlineData(AudioPlayback.Loop)]
    [InlineData(AudioPlayback.OnceThenSilence)]
    [InlineData(AudioPlayback.OnceEndsVideo)]
    public void BuildFfmpegArguments_NoAudio_HasNoAudioInput_WhateverThePlaybackSays(AudioPlayback playback)
    {
        var arguments = Arguments(null, playback);

        Assert.DoesNotContain("-stream_loop", arguments);
        Assert.DoesNotContain("apad", arguments);
        Assert.DoesNotContain("-shortest", arguments);
        Assert.DoesNotContain("-map", arguments);
    }
}
