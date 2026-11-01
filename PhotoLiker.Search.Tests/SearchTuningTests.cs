using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

/// <summary>The precision slider: what position means which cutoffs, and that the searches follow them.</summary>
public class SearchTuningTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly FolderIndex _search;
    private int _added;

    public SearchTuningTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    /// <summary>A vector whose cosine with <c>Unit(1, 0)</c> is <paramref name="score"/>.</summary>
    private static float[] AtScore(float score) => Vectors.Unit(score, MathF.Sqrt(1 - score * score));

    /// <summary>A photo of a size of its own, so that no two are taken for copies of one photo.</summary>
    private IndexedPhoto Add(string path, float[] clip, float[]? face = null)
    {
        var photo = new IndexedPhoto(path, 1000 + _added++, 1000 * TimeSpan.TicksPerSecond)
        {
            Clip = clip,
            ClipVersion = 1,
            FaceVersion = 1,
            Date = new PhotoDate(null, DateSource.None, PhotoDates.Version),
        };
        if (face is not null)
            photo.Faces = [new IndexedFace(photo, 0.1f, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, face)];
        _search.Index.Save([photo]);
        return photo;
    }

    // ── the slider ─────────────────────────────────────────────────────────

    [Fact]
    public void TheMiddle_IsTheCutoffsTheSearchHadBeforeTheSlider()
    {
        var middle = SearchTuning.For(Settings.DefaultSearchPrecision);

        Assert.Equal((FolderIndex.MinTextScore, FolderIndex.TextScoreBand, FolderIndex.MinSimilarScore, PeopleLibrary.DefaultMatchThreshold),
            (middle.MinTextScore, middle.TextScoreBand, middle.MinSimilarScore, middle.MinFaceScore));
        Assert.Equal(middle, SearchTuning.Default);
        Assert.Equal(50, new Settings().SearchPrecision);
    }

    [Fact]
    public void TheWideEnd_LetsMoreThrough_AndThePreciseEndLess()
    {
        var wide = SearchTuning.For(0);
        var middle = SearchTuning.Default;
        var precise = SearchTuning.For(Settings.MaxSearchPrecision);

        Assert.True(wide.MinTextScore < middle.MinTextScore && middle.MinTextScore < precise.MinTextScore);
        Assert.True(wide.TextScoreBand > middle.TextScoreBand && middle.TextScoreBand > precise.TextScoreBand);
        Assert.True(wide.MinSimilarScore < middle.MinSimilarScore && middle.MinSimilarScore < precise.MinSimilarScore);
        Assert.True(wide.MinFaceScore < middle.MinFaceScore && middle.MinFaceScore < precise.MinFaceScore);
    }

    [Fact]
    public void EveryStepOfTheSlider_IsNeverLooserThanTheOneBefore()
    {
        var before = SearchTuning.For(0);
        for (var position = 1; position <= Settings.MaxSearchPrecision; position++)
        {
            var now = SearchTuning.For(position);
            Assert.True(now.MinTextScore >= before.MinTextScore, $"text minimum at {position}");
            Assert.True(now.TextScoreBand <= before.TextScoreBand, $"text band at {position}");
            Assert.True(now.MinSimilarScore >= before.MinSimilarScore, $"similar at {position}");
            Assert.True(now.MinFaceScore >= before.MinFaceScore, $"face at {position}");
            before = now;
        }
    }

    [Theory]
    [InlineData(-30, 0)]
    [InlineData(101, 100)]
    [InlineData(9999, 100)]
    public void APositionOutsideTheSlider_IsTheNearestEnd(int position, int end) =>
        Assert.Equal(SearchTuning.For(end), SearchTuning.For(position)); // a hand-edited settings file

    [Fact]
    public void TheTextCutoff_IsTheMinimum_OrTheBandBelowTheBest_WhicheverIsHigher()
    {
        var tuning = new SearchTuning(MinTextScore: 0.23f, TextScoreBand: 0.06f, MinSimilarScore: 0.65f, MinFaceScore: 0.4f);

        Assert.Equal(0.30f, tuning.TextCutoff(0.36f), 4); // a strong best photo: the band decides
        Assert.Equal(0.23f, tuning.TextCutoff(0.26f), 4); // a weak one: the minimum decides
    }

    // ── the searches follow it ─────────────────────────────────────────────

    [Theory]
    [InlineData(0, 3)]   // wide: 0.35, 0.31, 0.26 (the cutoff is 0.23)
    [InlineData(50, 2)]  // standard: 0.35, 0.31 (the cutoff is 0.29)
    [InlineData(100, 1)] // precise: 0.35 (the cutoff is 0.325)
    public void ADescription_FindsMorePhotosTheWiderTheSlider(int precision, int expected)
    {
        foreach (var score in new[] { 0.35f, 0.31f, 0.26f, 0.21f })
            Add($"s{score}.jpg", AtScore(score));

        var result = _search.RankText("beach", Vectors.Unit(1, 0), dates: null, SearchTuning.For(precision));

        Assert.Equal(expected, result.Hits.Count);
        Assert.False(result.Weak);
        Assert.Equal(result.Hits.Select(h => h.Score).OrderDescending(), result.Hits.Select(h => h.Score)); // best first
    }

    [Fact]
    public void WhenNothingScoresEnough_TheClosestPhotosAreShownAsWeak_UntilTheSliderIsWideEnough()
    {
        foreach (var score in new[] { 0.21f, 0.20f })
            Add($"s{score}.jpg", AtScore(score));

        var standard = _search.RankText("beach", Vectors.Unit(1, 0), dates: null, SearchTuning.Default);
        var wide = _search.RankText("beach", Vectors.Unit(1, 0), dates: null, SearchTuning.For(0));

        Assert.True(standard.Weak);
        Assert.False(wide.Weak);
        Assert.Equal(2, wide.Hits.Count);
    }

    [Theory]
    [InlineData(0, 4)]   // wide: the photo itself, 0.9, 0.7, 0.55 (the cutoff is 0.50)
    [InlineData(50, 3)]  // standard: the photo itself, 0.9, 0.7 (0.65)
    [InlineData(100, 2)] // precise: the photo itself, 0.9 (0.85)
    public void MoreLikeThis_FindsMorePhotosTheWiderTheSlider(int precision, int expected)
    {
        Add("shown.jpg", Vectors.Unit(1, 0));
        foreach (var score in new[] { 0.9f, 0.7f, 0.55f, 0.3f })
            Add($"s{score}.jpg", AtScore(score));

        Assert.Equal(expected, _search.SearchSimilar(FullPath("shown.jpg"), SearchTuning.For(precision)).Hits.Count);
    }

    [Theory]
    [InlineData(0, 4)]   // wide: the face itself, 0.6, 0.45, 0.35 (the cutoff is 0.30)
    [InlineData(50, 3)]  // standard: the face itself, 0.6, 0.45 (0.40)
    [InlineData(100, 2)] // precise: the face itself, 0.6 (0.55)
    public void ThePhotosOfAPerson_AreMoreTheWiderTheSlider(int precision, int expected)
    {
        var known = Add("known.jpg", Vectors.Unit(0, 1), face: Vectors.Unit(1, 0));
        foreach (var score in new[] { 0.6f, 0.45f, 0.35f, 0.2f })
            Add($"s{score}.jpg", Vectors.Unit(0, 1), face: AtScore(score));

        Assert.Equal(expected, _search.SearchPerson(known.Faces[0], "Mom", SearchTuning.For(precision)).Hits.Count);
    }

    [Fact]
    public void WithoutATuning_TheSearchesAreTheStandardOnes()
    {
        Add("shown.jpg", Vectors.Unit(1, 0));
        foreach (var score in new[] { 0.9f, 0.7f, 0.55f })
            Add($"s{score}.jpg", AtScore(score));

        Assert.Equal(
            _search.SearchSimilar(FullPath("shown.jpg"), SearchTuning.Default).Hits.Count,
            _search.SearchSimilar(FullPath("shown.jpg")).Hits.Count);
    }
}
