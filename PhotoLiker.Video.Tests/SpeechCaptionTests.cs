using PhotoLiker.Video;

namespace PhotoLiker.Video.Tests;

/// <summary>Which words are a caption at a moment of a video (the viewer's line under the picture, the slideshow's text over it).</summary>
public class SpeechCaptionTests
{
    private static readonly SpeechSegment[] Said =
    [
        new(2_000, 4_000, "Вот лошадка"),
        new(9_000, 12_000, "  Ещё\n раз,   смотри  "),
    ];

    [Fact]
    public void WhileTheWordsAreSaid_TheyAreTheCaption()
    {
        Assert.Equal("Вот лошадка", SpeechCaptions.At(Said, 3_000));
        Assert.Equal("Вот лошадка", SpeechCaptions.At(Said, 2_000));
        Assert.Equal("Вот лошадка", SpeechCaptions.At(Said, 4_000));
    }

    [Fact]
    public void TheWordsComeABitBeforeTheyAreSaid_AndStayABitAfter()
    {
        Assert.Null(SpeechCaptions.At(Said, 2_000 - SpeechCaptions.LeadMs - 1));
        Assert.Equal("Вот лошадка", SpeechCaptions.At(Said, 2_000 - SpeechCaptions.LeadMs));
        Assert.Equal("Вот лошадка", SpeechCaptions.At(Said, 4_000 + SpeechCaptions.LingerMs));
        Assert.Null(SpeechCaptions.At(Said, 4_000 + SpeechCaptions.LingerMs + 1));
    }

    [Fact]
    public void WhereNothingIsSaid_ThereIsNoCaption()
    {
        Assert.Null(SpeechCaptions.At(Said, 0));
        Assert.Null(SpeechCaptions.At(Said, 7_000));
        Assert.Null(SpeechCaptions.At(Said, 60_000));
    }

    [Fact]
    public void TheLinesTheModelBroke_AndTheSpacesAroundThem_AreOneTidyLine() =>
        Assert.Equal("Ещё раз, смотри", SpeechCaptions.At(Said, 10_000));

    [Fact]
    public void WhenTheSpeechWasNotRead_ThereIsNoCaption()
    {
        Assert.Null(SpeechCaptions.At(null, 1_000));
        Assert.Null(SpeechCaptions.At([], 1_000));
    }

    [Fact]
    public void ASegmentOfNothingButSpaces_IsNotACaption_AndTheNextOneIsLookedAt()
    {
        SpeechSegment[] segments = [new(1_000, 5_000, "   "), new(4_000, 8_000, "после")];

        Assert.Null(SpeechCaptions.At(segments, 2_000));
        Assert.Equal("после", SpeechCaptions.At(segments, 4_500));
    }

    [Fact]
    public void WhereTwoSegmentsOverlap_TheEarlierOneWins()
    {
        SpeechSegment[] segments = [new(1_000, 5_000, "first"), new(4_000, 8_000, "second")];

        Assert.Equal("first", SpeechCaptions.At(segments, 4_500));
    }
}
