using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

/// <summary>What the search library gives to the video library (PhotoLiker.Video): the plan of a search text, the cutoff for the moments of a video, the fields of a hit.</summary>
public class VideoHooksTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly FolderIndex _search;

    public VideoHooksTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    [Fact]
    public void ADateAlone_IsADatePlan_WordsAreNot()
    {
        var date = _search.Plan("September 2009");
        var words = _search.Plan("playing guitar");
        var both = _search.Plan("beach 2009");

        Assert.True(date!.DateOnly);
        Assert.NotNull(date.Dates);
        Assert.False(words!.DateOnly);
        Assert.Equal("playing guitar", words.Words);
        Assert.False(both!.DateOnly);
        Assert.Equal(2009, both.Dates!.YearFrom);
        Assert.Null(_search.Plan("   "));
    }

    [Fact]
    public void TheMomentsOfAVideo_NeedALittleMoreThanAPhoto_AtEveryPositionOfTheSlider()
    {
        foreach (var position in new[] { 0, 25, 50, 75, 100 })
        {
            var tuning = SearchTuning.For(position);

            Assert.True(tuning.VideoTextCutoff(0.0f) > tuning.TextCutoff(0.0f));
            Assert.Equal(tuning.MinTextScore + SearchTuning.VideoMargin, tuning.VideoTextCutoff(0.0f), 0.0001f);
        }
    }

    [Fact]
    public void TheBandBelowTheBest_IsTheSameForVideos_ButNeverBelowTheirMinimum()
    {
        var tuning = SearchTuning.Default;

        Assert.Equal(0.60f - tuning.TextScoreBand, tuning.VideoTextCutoff(0.60f), 0.0001f); // a very good best: the band decides
        Assert.Equal(tuning.MinTextScore + SearchTuning.VideoMargin, tuning.VideoTextCutoff(0.26f), 0.0001f); // a weak best: the minimum does
    }

    [Fact]
    public void AHitThatIsAVideo_CarriesWhereToStart_AndTheStretches_AndSurvivesOrdering()
    {
        var hit = new SearchHit(@"C:\v\a.mp4", 10, new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0.4f, DateSource.Exif, new DateTime(2010, 1, 1), 4000, [4000, 30000]);
        var photo = new SearchHit(@"C:\p\b.jpg", 10, new DateTime(2011, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0.3f);
        var result = new SearchResult("x", [hit, photo], 5, SearchedVideos: 2);

        var ordered = result.OrderedBy(PhotoSortOrder.NewestFirst);

        Assert.Null(photo.TimeMs);
        Assert.Null(photo.Moments);
        Assert.Equal(4000, ordered.Hits.Single(h => h.FullPath.EndsWith("a.mp4")).TimeMs);
        Assert.Equal([4000L, 30000L], ordered.Hits.Single(h => h.FullPath.EndsWith("a.mp4")).Moments);
        Assert.Equal(2, ordered.SearchedVideos);
    }

    [Fact]
    public void ASearchWithNoVideos_CountsNone()
    {
        var result = _search.Search(_ => [1f, 0f], "anything");

        Assert.Equal(0, result.SearchedVideos);
    }
}
