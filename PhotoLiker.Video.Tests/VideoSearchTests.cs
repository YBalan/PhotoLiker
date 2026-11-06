using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

/// <summary>Searching the moments of videos, and putting what is found with the photos. The vectors are made by hand: the words are the direction (1, 0, 0, 0).</summary>
[Collection(FfmpegCollection.Name)]
public sealed class VideoSearchTests : IDisposable
{
    private static readonly ClipModelInfo Model = ClipModels.Default; // its text scores are the cosines as they are
    private static readonly float[] Words = [1f, 0f, 0f, 0f];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLVideoSearch_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public VideoSearchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static FolderIndex.SearchPlan Plan(string words, DateFilter? dates = null, params string[] names) => new(names, words, dates);

    private static MomentData Moment(long ms, float score) => new(ms, [score, MathF.Sqrt(Math.Max(0, 1 - score * score)), 0f, 0f], null);

    // A catalog with videos in it: made with the real ffmpeg, then given moments
    private async Task<(VideoCatalog Catalog, List<VideoRecord> Videos)> CatalogWith(params (string Name, string? Recorded, MomentData[] Moments)[] videos)
    {
        var catalog = VideoCatalog.Open(_root, DbFile);
        var records = new List<VideoRecord>();
        foreach (var (name, recorded, moments) in videos)
        {
            var file = RealFfmpeg.MakeVideo(_root, name, seconds: 2, recorded: recorded)!;
            var record = await catalog.GetAsync(file);
            catalog.SaveAnalysis(record.Id, Model.Id, moments);
            records.Add(record);
        }
        return (catalog, records);
    }

    [Fact]
    public async Task TheVideosThatFit_AreFoundBestFirst_EachOnce_WithWhereTheyFit()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _) = await CatalogWith(
            ("good.mp4", null, [Moment(0, 0.10f), Moment(4000, 0.34f), Moment(8000, 0.12f)]),
            ("better.mp4", null, [Moment(2000, 0.38f), Moment(30000, 0.37f)]),
            ("none.mp4", null, [Moment(0, 0.05f), Moment(2000, 0.08f)]));
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Model, Plan("anything"), _ => Words, SearchTuning.Default);

        Assert.False(result.Weak);
        Assert.Equal(["better.mp4", "good.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.Equal(3, result.SearchedVideos);
        var better = result.Hits[0];
        Assert.Equal(2000, better.TimeMs);
        Assert.Equal([2000L, 30000L], better.Moments); // two stretches: far apart
        Assert.Equal(0.38f, better.Score, 0.001f);
        Assert.Equal([4000L], result.Hits[1].Moments);
    }

    [Fact]
    public async Task AFrameNeedsALittleMoreThanAPhoto_ToCount()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        // 0.24 passes for a photo (0.23) but not for a frame of a video (0.25): on clips with no cat in them "a cat" scored that much
        var (catalog, _) = await CatalogWith(("a.mp4", null, [Moment(0, 0.24f)]));
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Model, Plan("cat"), _ => Words, SearchTuning.Default);

        Assert.True(result.Weak);
        Assert.Single(result.Hits); // the closest are shown, said to be only that
        Assert.True(SearchTuning.Default.TextCutoff(0.24f) <= 0.24f);
        Assert.True(SearchTuning.Default.VideoTextCutoff(0.24f) > 0.24f);
    }

    [Fact]
    public async Task WhenNothingFits_TheClosestAreShown_AsWeak_AtMostTwelve()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var videos = Enumerable.Range(0, 15).Select(i => ($"v{i:00}.mp4", (string?)null, new[] { Moment(0, 0.05f + i * 0.01f) })).ToArray();
        var (catalog, _) = await CatalogWith(videos);
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Model, Plan("zzz"), _ => Words, SearchTuning.Default);

        Assert.True(result.Weak);
        Assert.Equal(FolderIndex.WeakResultCount, result.Hits.Count);
        Assert.Equal("v14.mp4", Path.GetFileName(result.Hits[0].FullPath));
        Assert.Equal(15, result.SearchedVideos);
    }

    [Fact]
    public async Task TheSliderOfPrecision_MovesTheCutoffsOfVideosToo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _) = await CatalogWith(("a.mp4", null, [Moment(0, 0.28f)]), ("b.mp4", null, [Moment(0, 0.21f)]));
        using var __ = catalog;

        var wide = VideoSearch.Search(catalog, Model, Plan("x"), _ => Words, SearchTuning.For(0));
        var precise = VideoSearch.Search(catalog, Model, Plan("x"), _ => Words, SearchTuning.For(100));

        Assert.False(wide.Weak);
        Assert.Equal(["a.mp4", "b.mp4"], wide.Hits.Select(h => Path.GetFileName(h.FullPath))); // wide: 0.18 + 0.02 = 0.20, and the band is wide
        Assert.True(precise.Weak); // precise: 0.30 + 0.02
    }

    [Fact]
    public async Task ADateLimitsTheVideos_ByTheDateTheyWereRecorded()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _) = await CatalogWith(
            ("old.mp4", "2009-05-01T12:00:00Z", [Moment(0, 0.5f)]),
            ("new.mp4", "2012-05-01T12:00:00Z", [Moment(0, 0.5f)]));
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Model, Plan("x", new DateFilter(2012, 2012, null, null)), _ => Words, SearchTuning.Default);

        Assert.Equal(["new.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.Equal(1, result.SearchedVideos); // the other was not looked at
    }

    [Fact]
    public async Task ADateAlone_ListsTheVideosOfThatTime_OldestFirst_WithNoModel()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _) = await CatalogWith(
            ("c.mp4", "2010-09-01T12:00:00Z", [Moment(0, 0.1f)]),
            ("a.mp4", "2010-03-01T12:00:00Z", [Moment(0, 0.1f)]),
            ("b.mp4", "2011-03-01T12:00:00Z", [Moment(0, 0.1f)]));
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Model, Plan(string.Empty, new DateFilter(2010, 2010, null, null)), _ => throw new InvalidOperationException("no words"), SearchTuning.Default);

        Assert.Equal(["a.mp4", "c.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.All(result.Hits, h => Assert.Equal(0, h.TimeMs)); // playing starts at the beginning
        Assert.Equal(2, result.SearchedVideos);
    }

    [Fact]
    public async Task APlanThatNamesPeopleNobodyKnows_FindsNoVideo_AndDoesNotAskForTheModel()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _) = await CatalogWith(("a.mp4", null, [Moment(0, 0.5f)]));
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Mom"), _ => throw new InvalidOperationException(), SearchTuning.Default);

        Assert.Empty(result.Hits);
    }

    [Fact]
    public async Task VideosAnalyzedWithAnotherModel_AreNotSearchedWithThisOne()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var (catalog, _) = await CatalogWith(("a.mp4", null, [Moment(0, 0.5f)]));
        using var __ = catalog;

        var result = VideoSearch.Search(catalog, ClipModels.SigLip2, Plan("x"), _ => Words, SearchTuning.Default);

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.SearchedVideos);
    }

    // ── people ─────────────────────────────────────────────────────────

    private static readonly float[] Mom = [1f, 0f, 0f];
    private static readonly float[] Dad = [0f, 1f, 0f];
    private static readonly float[] Stranger = [0f, 0f, 1f];

    private static MomentFace Face(float[] who, float closeness = 1f) => new(0.1f, 0.1f, 0.2f, 0.2f, 0.9f, [who[0] * closeness, who[1] * closeness + (who[1] == 0 && closeness < 1 ? 0.0f : 0f), who[2]]);

    private static MomentData MomentWith(long ms, float text, params MomentFace[] faces) =>
        new(ms, [text, MathF.Sqrt(Math.Max(0, 1 - text * text)), 0f, 0f], null, faces);

    private static IReadOnlyList<float[]> Examples(string name) => name.ToLowerInvariant() switch
    {
        "mom" => [Mom],
        "dad" => [Dad],
        "both" => [Mom, Dad], // a group: anybody of them
        _ => [],
    };

    private async Task<VideoCatalog> PeopleCatalog()
    {
        var (catalog, _) = await CatalogWith(
            ("park.mp4", null, [MomentWith(0, 0.1f, Face(Stranger)), MomentWith(2000, 0.1f, Face(Mom)), MomentWith(4000, 0.1f, Face(Mom), Face(Dad)), MomentWith(30000, 0.1f, Face(Dad))]),
            ("kitchen.mp4", null, [MomentWith(0, 0.4f, Face(Mom)), MomentWith(2000, 0.1f)]),
            ("empty.mp4", null, [MomentWith(0, 0.1f, Face(Stranger))]));
        return catalog;
    }

    [Fact]
    public async Task ANamedPerson_IsFoundInTheMomentsWhereTheirFaceIs()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = await PeopleCatalog();

        var result = VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Mom"), _ => throw new InvalidOperationException("no words"), SearchTuning.Default, Examples);

        Assert.False(result.Weak);
        Assert.Equal(["kitchen.mp4", "park.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)).Order());
        var park = result.Hits.Single(h => h.FullPath.EndsWith("park.mp4"));
        Assert.Equal([2000L], park.Moments); // 2 s and 4 s are one stretch, the best of them stands for it
        Assert.Equal(3, result.SearchedVideos);
        Assert.Equal(1.0f, park.Score, 0.001f);
    }

    [Fact]
    public async Task SeveralPeople_AreFoundTogether_OnlyWhereAllOfThemAre()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = await PeopleCatalog();

        var result = VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Mom", "Dad"), _ => throw new InvalidOperationException(), SearchTuning.Default, Examples);

        var hit = Assert.Single(result.Hits);
        Assert.EndsWith("park.mp4", hit.FullPath);
        Assert.Equal([4000L], hit.Moments); // the only moment with both of them
    }

    [Fact]
    public async Task APersonWithWords_IsFoundWhereTheyAre_AndTheWordsRankThePlaces()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = await PeopleCatalog();

        // "Mom" and the words of (1, 0, 0, 0): the kitchen moment of Mom scores 0.40 for the words, the park ones 0.10
        var result = VideoSearch.Search(catalog, Model, Plan("cooking", null, "Mom"), _ => Words, SearchTuning.Default, Examples);

        Assert.False(result.Weak);
        var hit = Assert.Single(result.Hits);
        Assert.EndsWith("kitchen.mp4", hit.FullPath);
        Assert.Equal(0, hit.TimeMs);
    }

    [Fact]
    public async Task APersonWhoIsNotInAnyMoment_IsNothing_AndAnUnknownNameIsToo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = await PeopleCatalog();

        Assert.Empty(VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Dad", "Mom", "Nobody"), _ => throw new InvalidOperationException(), SearchTuning.Default, Examples).Hits);
        Assert.Empty(VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Nobody"), _ => throw new InvalidOperationException(), SearchTuning.Default, Examples).Hits);
    }

    [Fact]
    public async Task AGroup_StandsForAnyOfItsPeople()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = await PeopleCatalog();

        var result = VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "both"), _ => throw new InvalidOperationException(), SearchTuning.Default, Examples);

        Assert.Equal(["kitchen.mp4", "park.mp4"], result.Hits.Select(h => Path.GetFileName(h.FullPath)).Order());
        Assert.Equal([2000L, 30000L], result.Hits.Single(h => h.FullPath.EndsWith("park.mp4")).Moments);
    }

    [Fact]
    public async Task AFaceThatIsOnlyLikeAPerson_NeedsToBeCloseEnough_AndThePrecisionSliderSaysHowClose()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        // a face at cosine 0.45 to Mom: close enough at the middle of the slider (0.40), not at the precise end (0.55)
        var (catalog, _) = await CatalogWith(("a.mp4", null, [MomentWith(0, 0.1f, new MomentFace(0, 0, 0.2f, 0.2f, 0.9f, [0.45f, MathF.Sqrt(1 - 0.45f * 0.45f), 0f]))]));
        using var __ = catalog;

        Assert.Single(VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Mom"), _ => Words, SearchTuning.Default, Examples).Hits);
        Assert.Empty(VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Mom"), _ => Words, SearchTuning.For(100), Examples).Hits);
        Assert.Single(VideoSearch.Search(catalog, Model, Plan(string.Empty, null, "Mom"), _ => Words, SearchTuning.For(0), Examples).Hits);
    }
    // ── the stretches of a video ───────────────────────────────────────

    [Fact]
    public void MomentsThatFit_AndFollowEachOtherClosely_AreOneStretch_ByTheirBest()
    {
        long[] times = [0, 2000, 4000, 30000, 32000, 60000];
        float[] scores = [0.30f, 0.35f, 0.31f, 0.28f, 0.10f, 0.40f];

        var stretches = VideoSearch.Stretches(times, scores, 0.25f);

        Assert.Equal([2000L, 30000L, 60000L], stretches);
    }

    [Fact]
    public void AStretchThatGoesOn_StaysOne_EvenWhenItIsLongerThanTheGap()
    {
        long[] times = [0, 5000, 10000, 15000, 20000];
        float[] scores = [0.3f, 0.3f, 0.33f, 0.3f, 0.3f];

        Assert.Equal([10000L], VideoSearch.Stretches(times, scores, 0.25f)); // 5 s apart each time: the gap is from the one before
    }

    [Fact]
    public void ANoMomentFits_NoStretch() => Assert.Empty(VideoSearch.Stretches([0, 1000], [0.1f, 0.2f], 0.25f));

    // ── together with the photos ───────────────────────────────────────

    private static SearchHit Hit(string name, float score, long? timeMs = null, DateTime? taken = null) =>
        new(@"C:\" + name, 1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), score, DateSource.Exif, taken, timeMs, timeMs is null ? null : [timeMs.Value]);

    private static SearchResult Photos(bool weak, params SearchHit[] hits) => new("beach", hits, 100, Weak: weak, OtherModelPhotos: 3);

    private static SearchResult Videos(bool weak, int searched, params SearchHit[] hits) => new(string.Empty, hits, 0, Weak: weak, SearchedVideos: searched);

    private static readonly FolderIndex.SearchPlan WordsPlan = Plan("beach");

    [Fact]
    public void WhatFitsWell_IsOneList_ByHowWellItFits()
    {
        var merged = VideoSearch.Merge(
            Photos(false, Hit("a.jpg", 0.40f), Hit("b.jpg", 0.30f)),
            Videos(false, 7, Hit("v1.mp4", 0.35f, 4000), Hit("v2.mp4", 0.29f, 0)),
            WordsPlan);

        Assert.Equal(["a.jpg", "v1.mp4", "b.jpg", "v2.mp4"], merged.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.False(merged.Weak);
        Assert.Equal(("beach", 100, 7, 3), (merged.Description, merged.SearchedPhotos, merged.SearchedVideos, merged.OtherModelPhotos));
    }

    [Fact]
    public void ClosePhotos_AreLeftOut_WhenAVideoFitsWell()
    {
        var merged = VideoSearch.Merge(Photos(true, Hit("far1.jpg", 0.19f), Hit("far2.jpg", 0.18f)), Videos(false, 2, Hit("v.mp4", 0.33f, 5000)), WordsPlan);

        Assert.Equal(["v.mp4"], merged.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.False(merged.Weak);
    }

    [Fact]
    public void CloseVideos_AreLeftOut_WhenAPhotoFitsWell()
    {
        var merged = VideoSearch.Merge(Photos(false, Hit("a.jpg", 0.30f)), Videos(true, 2, Hit("v.mp4", 0.22f, 0)), WordsPlan);

        Assert.Equal(["a.jpg"], merged.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.False(merged.Weak);
    }

    [Fact]
    public void WhenNothingFitsWell_TheClosestOfBoth_AreShown_AsWeak()
    {
        var photos = Enumerable.Range(0, 12).Select(i => Hit($"p{i}.jpg", 0.10f + i * 0.001f)).ToArray();
        var merged = VideoSearch.Merge(Photos(true, photos), Videos(true, 4, Hit("v.mp4", 0.2f, 0)), WordsPlan);

        Assert.True(merged.Weak);
        Assert.Equal(FolderIndex.WeakResultCount, merged.Hits.Count);
        Assert.Equal("v.mp4", Path.GetFileName(merged.Hits[0].FullPath)); // the best of all of them
    }

    [Fact]
    public void NothingAtAll_IsNothing_NotWeak()
    {
        var merged = VideoSearch.Merge(Photos(false), Videos(false, 0), WordsPlan);

        Assert.Empty(merged.Hits);
        Assert.False(merged.Weak);
    }

    [Fact]
    public void ADateAlone_IsOneList_OldestFirst()
    {
        var plan = Plan(string.Empty, new DateFilter(2010, 2010, null, null));
        var merged = VideoSearch.Merge(
            new SearchResult("2010", [Hit("p1.jpg", 0, taken: new DateTime(2010, 6, 1)), Hit("p2.jpg", 0, taken: new DateTime(2010, 1, 1))], 50),
            Videos(false, 2, Hit("v1.mp4", 0, 0, new DateTime(2010, 3, 1)), Hit("v2.mp4", 0, 0, new DateTime(2010, 12, 1))),
            plan);

        Assert.Equal(["p2.jpg", "v1.mp4", "p1.jpg", "v2.mp4"], merged.Hits.Select(h => Path.GetFileName(h.FullPath)));
    }

    [Fact]
    public void ThePhotosResult_IsKept_WhenThereAreNoVideosToAdd()
    {
        var photos = Photos(false, Hit("a.jpg", 0.4f));

        var merged = VideoSearch.Merge(photos, Videos(false, 0), WordsPlan);

        Assert.Equal(photos.Hits, merged.Hits);
        Assert.Equal(0, merged.SearchedVideos);
    }

    // ── the videos only ────────────────────────────────────────────────

    [Fact]
    public void WithoutItsPhotos_ThePhotoResult_KeepsWhatWasSearchedFor_AndNothingElse()
    {
        var photos = Photos(true, Hit("a.jpg", 0.30f)) with { Dates = new DateFilter(2010, 2010, null, null) };

        var without = VideoSearch.WithoutPhotos(photos);

        Assert.Empty(without.Hits);
        Assert.False(without.Weak);
        Assert.Equal(("beach", 0, 0), (without.Description, without.SearchedPhotos, without.OtherModelPhotos));
        Assert.Equal(photos.Dates, without.Dates);
    }

    [Fact]
    public void ASearchOfTheVideosOnly_IsTheVideosThatFit_NoPhotoAmongThem()
    {
        var merged = VideoSearch.Merge(
            VideoSearch.WithoutPhotos(Photos(false, Hit("a.jpg", 0.40f), Hit("b.jpg", 0.30f))),
            Videos(false, 7, Hit("v1.mp4", 0.35f, 4000)),
            WordsPlan);

        Assert.Equal(["v1.mp4"], merged.Hits.Select(h => Path.GetFileName(h.FullPath)));
        Assert.Equal(("beach", 0, 7), (merged.Description, merged.SearchedPhotos, merged.SearchedVideos));
    }

    [Fact]
    public void ASearchOfTheVideosOnly_WhereNoneFitsWell_ShowsTheClosestVideos_AsWeak()
    {
        var merged = VideoSearch.Merge(
            VideoSearch.WithoutPhotos(Photos(false, Hit("strong.jpg", 0.50f))), // a photo that fits well must not push the videos out
            Videos(true, 3, Hit("v1.mp4", 0.20f, 0), Hit("v2.mp4", 0.19f, 0)),
            WordsPlan);

        Assert.True(merged.Weak);
        Assert.Equal(["v1.mp4", "v2.mp4"], merged.Hits.Select(h => Path.GetFileName(h.FullPath)));
    }
}
