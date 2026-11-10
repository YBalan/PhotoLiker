using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Videos in the slideshow: what is played of a video, how far the other programs' sound is turned down, and the settings that say so.</summary>
public class SlideshowVideoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLSlideshowVideo_" + Guid.NewGuid());

    public SlideshowVideoTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ── what is played of a video ──────────────────────────────────────

    [Fact]
    public void TheFullVideo_IsPlayedWhole_WhateverWasFound()
    {
        Assert.Equal([new VideoSegment(0, null)], SlideshowVideo.Segments(SlideshowVideoPart.FullVideo, [10_000, 40_000]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new long[0])]
    public void AVideoWithNothingFoundInIt_IsPlayedWhole_EvenWhenOnlyFoundTimeFramesAreAsked(long[]? found)
    {
        Assert.Equal([new VideoSegment(0, null)], SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, found));
    }

    [Fact]
    public void AFoundPlace_IsPlayedWithTwoSecondsBeforeItAndFourAfter()
    {
        var segments = SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [10_000]);

        Assert.Equal([new VideoSegment(8_000, 14_000)], segments);
        Assert.Equal(SlideshowVideo.FoundLengthMs, segments[0].EndMs - segments[0].StartMs);
    }

    [Fact]
    public void APlaceNearTheStart_BeginsAtTheStart_AndStillEndsFourSecondsAfterIt()
    {
        Assert.Equal([new VideoSegment(0, 5_000)], SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [1_000]));
    }

    [Fact]
    public void PlacesFarApart_AreStretchesOfTheirOwn_InOrderOfTime()
    {
        var segments = SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [40_000, 10_000]);

        Assert.Equal([new VideoSegment(8_000, 14_000), new VideoSegment(38_000, 44_000)], segments);
    }

    [Theory]
    [InlineData(10_000L, 15_000L, 8_000L, 19_000L)]  // overlap
    [InlineData(10_000L, 16_000L, 8_000L, 20_000L)]  // they touch
    public void Stretches_ThatOverlapOrTouch_AreOne(long a, long b, long start, long end)
    {
        Assert.Equal([new VideoSegment(start, end)], SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [a, b]));
    }

    [Fact]
    public void WhatLiesBeyondTheEndOfTheVideo_IsLeftOut()
    {
        var segments = SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [58_000, 70_000], lengthMs: 60_000);

        Assert.Equal([new VideoSegment(56_000, 60_000)], segments); // the second place is past the end: no stretch for it
    }

    [Fact]
    public void WhenEveryPlaceIsBeyondTheEnd_TheVideoIsPlayedWhole()
    {
        Assert.Equal([new VideoSegment(0, null)], SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [90_000], lengthMs: 60_000));
    }

    [Fact]
    public void TheSamePlaceTwice_AndANegativeOne_AreNoMorePlaces()
    {
        Assert.Equal([new VideoSegment(8_000, 14_000)], SlideshowVideo.Segments(SlideshowVideoPart.OnlyFoundTimeFrames, [10_000, 10_000, -5]));
    }

    // ── the other programs' sound ──────────────────────────────────────

    [Theory]
    [InlineData(50, 0.5f)]
    [InlineData(0, 1f)]
    [InlineData(100, 0f)]
    [InlineData(25, 0.75f)]
    [InlineData(150, 0f)]   // more than all is all
    [InlineData(-10, 1f)]   // less than none is none
    public void TheLevelOfTheOthers_IsWhatIsLeftAfterTheyAreTurnedDown(int percent, float level)
    {
        Assert.Equal(level, SlideshowVideo.LevelOfOthers(percent), precision: 3);
    }

    // ── the settings ───────────────────────────────────────────────────

    [Fact]
    public void ByDefault_TheSlideshowShowsPhotosOnly_WithItsSoundOn_AndOnlyTheFoundPlaces_AndTheOthersAtHalf()
    {
        var settings = new Settings();

        Assert.False(settings.SlideshowUseVideo);
        Assert.False(settings.SlideshowMuteVideo);
        Assert.Equal(SlideshowVideoPart.OnlyFoundTimeFrames, settings.SlideshowVideoPart);
        Assert.Equal(50, settings.SlideshowLowerOtherAudio);
        Assert.False(settings.SlideshowShowCaptions);
    }

    [Fact]
    public void TheVideoSettings_AreKeptWithTheFolder()
    {
        new Settings
        {
            CurrentFolder = _root, SlideshowUseVideo = true, SlideshowMuteVideo = true,
            SlideshowVideoPart = SlideshowVideoPart.FullVideo, SlideshowLowerOtherAudio = 70, SlideshowShowCaptions = true,
        }.Save(_root);

        var loaded = Settings.Load(_root);

        Assert.True(loaded.SlideshowUseVideo);
        Assert.True(loaded.SlideshowMuteVideo);
        Assert.Equal(SlideshowVideoPart.FullVideo, loaded.SlideshowVideoPart);
        Assert.Equal(70, loaded.SlideshowLowerOtherAudio);
        Assert.True(loaded.SlideshowShowCaptions);
    }

    [Fact]
    public void AFileOfAnOlderVersion_HasTheDefaults()
    {
        File.WriteAllText(Settings.GetFilePath(_root), """{ "SlideshowTimeout": 4 }""");

        var loaded = Settings.Load(_root);

        Assert.Equal(4, loaded.SlideshowTimeout);
        Assert.False(loaded.SlideshowUseVideo);
        Assert.Equal(SlideshowVideoPart.OnlyFoundTimeFrames, loaded.SlideshowVideoPart);
        Assert.Equal(50, loaded.SlideshowLowerOtherAudio);
        Assert.False(loaded.SlideshowShowCaptions);
    }

    [Fact]
    public void APartThisVersionDoesNotKnow_IsTheDefault_NotAFailure()
    {
        File.WriteAllText(Settings.GetFilePath(_root), """{ "SlideshowVideoPart": "Teaser", "SlideshowUseVideo": true }""");

        var loaded = Settings.Load(_root);

        Assert.Equal(SlideshowVideoPart.OnlyFoundTimeFrames, loaded.SlideshowVideoPart);
        Assert.True(loaded.SlideshowUseVideo);
    }
}
