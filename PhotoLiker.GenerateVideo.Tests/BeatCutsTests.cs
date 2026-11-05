using PhotoLiker.Core;

namespace PhotoLiker.GenerateVideo.Tests;

public class BeatCutsTests
{
    /// <summary>Beats 0.4, 0.9, 1.4 ... every half second (120 bpm), in an audio of the given length.</summary>
    private static RhythmAnalysis Steady(double duration = 10, double first = 0.4, double period = 0.5, float[]? strength = null)
    {
        var beats = new List<double>();
        for (var time = first; time < duration - 0.05; time += period)
            beats.Add(time);

        return new RhythmAnalysis(0.01, strength ?? new float[(int)(duration / 0.01)], [.. beats], 60 / period, duration);
    }

    private static List<string> Photos(int count) => [.. Enumerable.Range(1, count).Select(i => $"p{i}")];

    // ── RhythmAnalysis ──────────────────────────────────────────────────────

    [Fact]
    public void BeatPeriod_IsTheMiddleGap_NotMovedByAMissedBeat()
    {
        var analysis = new RhythmAnalysis(0.01, [], [1, 1.5, 2, 3, 3.5, 4, 4.5], 120, 6);

        Assert.Equal(0.5, analysis.BeatPeriodSeconds, 6);
    }

    [Fact]
    public void BeatPeriod_WithoutBeats_IsFromTheTempo_OrZero()
    {
        Assert.Equal(0.5, new RhythmAnalysis(0.01, [], [], 120, 6).BeatPeriodSeconds);
        Assert.Equal(0, new RhythmAnalysis(0.01, [], [], 0, 6).BeatPeriodSeconds);
    }

    [Theory]
    [InlineData(0, 0.4)]
    [InlineData(3, 1.9)]
    public void BeatTime_InsideTheAudio_IsTheBeat(int index, double expected)
    {
        Assert.Equal(expected, Steady().BeatTime(index), 6);
    }

    [Fact]
    public void BeatTime_PastTheEnd_TheBeatsComeAgainOneAudioLengthLater()
    {
        var analysis = Steady(duration: 5); // beats 0.4 ... 4.9: ten of them
        Assert.Equal(10, analysis.Beats.Count);

        Assert.Equal(5.4, analysis.BeatTime(10), 6);
        Assert.Equal(5.9, analysis.BeatTime(11), 6);
        Assert.Equal(10.4, analysis.BeatTime(20), 6);
    }

    [Fact]
    public void BeatTime_BeforeTheFirst_IsTheLastOfTheTurnBefore()
    {
        var analysis = Steady(duration: 5);

        Assert.Equal(4.9 - 5, analysis.BeatTime(-1), 6);
    }

    [Fact]
    public void BeatTime_WithoutBeats_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new RhythmAnalysis(0.01, [], [], 0, 6).BeatTime(0));
    }

    [Theory]
    [InlineData(0.0, 0)]    // before the first
    [InlineData(0.5, 0)]    // 0.4 is nearer than 0.9
    [InlineData(0.7, 1)]    // 0.9 is nearer than 0.4
    [InlineData(1.9, 3)]
    [InlineData(4.44, 8)]
    public void NearestBeatIndex_InsideTheAudio(double seconds, int expected)
    {
        Assert.Equal(expected, Steady(duration: 5).NearestBeatIndex(seconds));
    }

    [Theory]
    [InlineData(5.3, 10)]   // the first beat of the second turn, 5.4
    [InlineData(6.0, 11)]   // 5.9
    [InlineData(10.5, 20)]
    public void NearestBeatIndex_PastTheEnd_FollowsTheBeatsThatComeAgain(double seconds, int expected)
    {
        Assert.Equal(expected, Steady(duration: 5).NearestBeatIndex(seconds));
    }

    [Fact]
    public void NearestBeatIndex_JustBeforeTheEnd_CanBeTheLastBeatOrTheFirstOfTheNextTurn()
    {
        var analysis = Steady(duration: 5); // last beat 4.9, then 5.4

        Assert.Equal(9, analysis.NearestBeatIndex(4.7));
        Assert.Equal(10, analysis.NearestBeatIndex(5.2));
    }

    [Fact]
    public void Scaled_Faster_PutsABeatBetweenEachTwo_AndDoublesTheTempo()
    {
        var analysis = new RhythmAnalysis(0.01, new float[100], [1, 2, 3], 60, 4);

        var faster = analysis.Scaled(1);

        Assert.Equal([1, 1.5, 2, 2.5, 3], faster.Beats);
        Assert.Equal(120, faster.Bpm);
    }

    [Fact]
    public void Scaled_Slower_KeepsEverySecondBeat_OfTheSetThatHitsHarder()
    {
        var strength = new float[600];
        foreach (var odd in new[] { 1.0, 3.0, 5.0 }) // the 2nd, 4th and 6th beat hit harder
            strength[(int)(odd * 100)] = 1;

        var analysis = new RhythmAnalysis(0.01, strength, [0, 1, 2, 3, 4, 5], 120, 6);

        Assert.Equal([1, 3, 5], analysis.Scaled(-1).Beats);
        Assert.Equal(60, analysis.Scaled(-1).Bpm);

        strength[(int)(1.0 * 100)] = 0;
        strength[(int)(3.0 * 100)] = 0;
        strength[(int)(5.0 * 100)] = 0;
        strength[(int)(0.0 * 100)] = 1;
        strength[(int)(2.0 * 100)] = 1;
        strength[(int)(4.0 * 100)] = 1;
        Assert.Equal([0, 2, 4], analysis.Scaled(-1).Beats);
    }

    [Fact]
    public void Scaled_ByNothing_OrWithoutBeats_IsTheSame()
    {
        var analysis = Steady();
        Assert.Same(analysis, analysis.Scaled(0));

        var none = new RhythmAnalysis(0.01, [], [], 0, 5);
        Assert.Same(none, none.Scaled(1));
    }

    [Fact]
    public void Scaled_FasterThenSlower_IsTheBeatsAgain()
    {
        var analysis = Steady();

        Assert.Equal(analysis.Beats, analysis.Scaled(1).Scaled(-1).Beats);
    }

    // ── PlanOnBeats ─────────────────────────────────────────────────────────

    [Fact]
    public void PlanOnBeats_EachPhotoIsAWholeNumberOfBeats_AndEndsOnABeat()
    {
        var rhythm = Steady(); // beats every 0.5 s from 0.4

        var plan = VideoGenerationService.PlanOnBeats(5, 1.0, 1.0, null, null, rhythm);

        // The first photo ends on the beat nearest to 1.0 (0.9); then 2 beats each
        Assert.Null(plan.CoverSeconds);
        Assert.Equal(0.9, plan.Durations[0], 6);
        Assert.All(plan.Durations.Skip(1), duration => Assert.Equal(1.0, duration, 6));

        var cuts = plan.Durations.Aggregate(new List<double>(), (list, duration) => { list.Add((list.Count == 0 ? 0 : list[^1]) + duration); return list; });
        Assert.All(cuts, cut => Assert.True(rhythm.Beats.Any(beat => Math.Abs(beat - cut) < 1e-9), $"{cut} is not a beat"));
    }

    [Theory]
    [InlineData(0.2, 1)]   // shorter than a beat: one beat
    [InlineData(0.74, 1)]
    [InlineData(0.76, 2)]  // 1.5 beats and a bit: 2
    [InlineData(1.25, 3)]  // 2.5 beats: 3 (halves go up)
    [InlineData(3.0, 6)]
    public void PlanOnBeats_TheNumberOfBeatsIsTheTimeRoundedToBeats_AtLeastOne(double seconds, int beats)
    {
        var rhythm = Steady();

        var plan = VideoGenerationService.PlanOnBeats(3, seconds, seconds, null, 2.0, rhythm); // the cover puts the count on the beat

        Assert.All(plan.Durations, duration => Assert.Equal(beats * 0.5, duration, 6));
    }

    [Fact]
    public void PlanOnBeats_TheCoverAtTheStartIsMovedToTheNearestBeat_AndPhotosGoFromThere()
    {
        var rhythm = Steady();

        var plan = VideoGenerationService.PlanOnBeats(2, 1.0, 1.0, null, 3.0, rhythm);

        Assert.Equal(2.9, plan.CoverSeconds!.Value, 6); // the beats are at ... 2.4, 2.9, 3.4
        Assert.All(plan.Durations, duration => Assert.Equal(1.0, duration, 6));
        Assert.Equal(2, plan.Durations.Length);
    }

    [Fact]
    public void PlanOnBeats_ACoverShorterThanTheFirstBeat_WaitsForABeatItCanEndOn()
    {
        var rhythm = Steady(first: 0.01); // a beat at 0.01 s is no time for a cover

        var plan = VideoGenerationService.PlanOnBeats(1, 1.0, 1.0, null, 0.04, rhythm);

        Assert.True(plan.CoverSeconds >= VideoGenerationService.LastFrameSeconds);
        Assert.Equal(0.51, plan.CoverSeconds!.Value, 6);
    }

    [Fact]
    public void PlanOnBeats_FromTheStartTimeToTheEndTime_TheBeatsPerPhotoGrow()
    {
        var rhythm = Steady(duration: 200);

        var plan = VideoGenerationService.PlanOnBeats(6, 0.5, 3.0, null, 1.4, rhythm);

        var beats = plan.Durations.Select(duration => (int)Math.Round(duration / 0.5)).ToArray();
        Assert.Equal(beats.OrderBy(b => b), beats);
        Assert.Equal(1, beats[0]);
        Assert.Equal(6, beats[^1]);
    }

    [Fact]
    public void PlanOnBeats_RandomTimes_AreRoundedToBeatsToo()
    {
        var rhythm = Steady(duration: 200);

        var plan = VideoGenerationService.PlanOnBeats(50, 0.5, 4.0, VideoGenerationService.DrawRandomSamples(50, new Random(1)), 1.4, rhythm);

        Assert.All(plan.Durations, duration =>
        {
            Assert.InRange(duration, 0.5 - 1e-9, 4.0 + 0.25);
            Assert.Equal(0, (duration / 0.5) - Math.Round(duration / 0.5), 6);
        });
        Assert.True(plan.Durations.Distinct().Count() > 3);
    }

    [Fact]
    public void PlanOnBeats_PhotosThatOutlastTheAudio_GoOnOnTheBeatsThatComeAgain()
    {
        var rhythm = Steady(duration: 5); // ten beats, 0.4 ... 4.9

        var plan = VideoGenerationService.PlanOnBeats(8, 1.0, 1.0, null, 1.4, rhythm);

        // The cover to 1.4, then a photo every two beats: 2.4, 3.4, 4.4, then (the audio comes again) 5.4, 6.4 ...
        Assert.Equal(1.4, plan.CoverSeconds!.Value, 6);
        Assert.Equal(1.0 * 8, plan.Durations.Sum(), 6);
        Assert.All(plan.Durations, duration => Assert.Equal(1.0, duration, 6));
    }

    [Fact]
    public void PlanOnBeats_NoPhotos_OnlyMovesTheCover()
    {
        var plan = VideoGenerationService.PlanOnBeats(0, 1.0, 1.0, null, 3.0, Steady());

        Assert.Empty(plan.Durations);
        Assert.Equal(2.9, plan.CoverSeconds!.Value, 6);
    }

    [Fact]
    public void PlanOnBeats_NoTimeIsShorterThanAFrame()
    {
        var rhythm = new RhythmAnalysis(0.01, [], [0.0, 0.01, 0.5, 1.0, 1.5], 120, 2); // a beat found twice in a row, 10 ms apart

        var plan = VideoGenerationService.PlanOnBeats(4, 0.01, 0.01, null, null, rhythm);

        Assert.All(plan.Durations, duration => Assert.True(duration >= VideoGenerationService.LastFrameSeconds - 1e-9));
    }

    // ── BuildSequence and EstimateLength with a rhythm ──────────────────────

    [Fact]
    public void BuildSequence_WithARhythm_TheCoverAndPhotosAreCutOnTheBeats()
    {
        var rhythm = Steady();

        var sequence = VideoGenerationService.BuildSequence(Photos(3), "cover", 3.0, 1.0, 1.0, null, false, true, 10, null, rhythm);

        Assert.Equal(["cover", "p1", "p2", "p3"], sequence.Files);
        Assert.Equal([2.9, 1.0, 1.0, 1.0], sequence.Durations.Select(d => Math.Round(d, 6)));
    }

    [Fact]
    public void BuildSequence_WithARhythm_AndNoCoverAtTheStart_StartsAtTheFirstBeatThatIsNearTheTime()
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(3), null, null, 1.0, 1.0, null, false, true, 10, null, Steady());

        Assert.Equal([0.9, 1.0, 1.0], sequence.Durations.Select(d => Math.Round(d, 6)));
    }

    [Fact]
    public void BuildSequence_WithARhythm_TheEndCoverComesAfterTheLastCut_WithItsOwnTime()
    {
        var sequence = VideoGenerationService.BuildSequence(Photos(2), "cover", 3.0, 1.0, 1.0, null, false, true, 10, 4.0, Steady());

        Assert.Equal(["cover", "p1", "p2", "cover"], sequence.Files);
        Assert.Equal(4.0, sequence.Durations[^1]);
    }

    [Fact]
    public void BuildSequence_WithARhythm_PhotosAreNotRepeatedForTheAudio()
    {
        // Ten seconds of audio over 3 s of photos: without a rhythm the photos would play again until it ends
        var plain = VideoGenerationService.BuildSequence(Photos(3), null, null, 1.0, 1.0, null, true, true, 10);
        var onBeats = VideoGenerationService.BuildSequence(Photos(3), null, null, 1.0, 1.0, null, true, true, 10, null, Steady());

        Assert.True(plain.AudioEndsVideo);
        Assert.Equal(3, onBeats.Files.Count);
        Assert.False(onBeats.AudioEndsVideo);
    }

    [Fact]
    public void BuildSequence_ARhythmWithoutBeats_ChangesNothing()
    {
        var none = new RhythmAnalysis(0.01, new float[10], [], 0, 10);

        var plain = VideoGenerationService.BuildSequence(Photos(4), "cover", 3.0, 0.5, 2.0, null, true, true, 30, 2.0);
        var withNone = VideoGenerationService.BuildSequence(Photos(4), "cover", 3.0, 0.5, 2.0, null, true, true, 30, 2.0, none);

        Assert.Equal(plain.Files, withNone.Files);
        Assert.Equal(plain.Durations, withNone.Durations);
        Assert.Equal(plain.ExpectedSeconds, withNone.ExpectedSeconds);
    }

    [Theory]
    [InlineData(5, 3.0, 1.0, 1.0, null, false)]
    [InlineData(5, 3.0, 0.5, 3.0, 2.0, true)]
    [InlineData(7, null, 1.0, 1.0, null, true)]
    [InlineData(1, 0.04, 1.0, 1.0, 5.0, false)]
    [InlineData(40, 2.0, 0.3, 0.3, 2.0, true)]
    public void EstimateLength_WithARhythm_IsTheLengthOfTheSequenceBuilt(int photos, double? cover, double start, double end, double? coverEnd, bool repeatToAudio)
    {
        var rhythm = Steady(duration: 30);

        var estimate = VideoGenerationService.EstimateLength(photos, start, end, null, cover, repeatToAudio, 30, coverEnd, rhythm);
        var sequence = VideoGenerationService.BuildSequence(Photos(photos), cover is null && coverEnd is null ? null : "cover", cover, start, end, null, repeatToAudio, true, 30, coverEnd, rhythm);

        Assert.Equal(sequence.ExpectedSeconds, estimate, 6);
        Assert.Equal(sequence.Durations.Sum() + VideoGenerationService.LastFrameSeconds, estimate, 6);
    }

    [Fact]
    public void EstimateLength_WithARhythm_Samples_AreUsedToo()
    {
        var rhythm = Steady(duration: 30);
        var samples = VideoGenerationService.DrawRandomSamples(20, new Random(5));

        var estimate = VideoGenerationService.EstimateLength(20, 0.5, 3.0, samples, 2.0, false, 30, null, rhythm);
        var sequence = VideoGenerationService.BuildSequence(Photos(20), "cover", 2.0, 0.5, 3.0, samples, false, true, 30, null, rhythm);

        Assert.Equal(sequence.ExpectedSeconds, estimate, 6);
    }
}
