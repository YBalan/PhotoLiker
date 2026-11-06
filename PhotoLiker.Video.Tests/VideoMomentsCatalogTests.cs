using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

/// <summary>Where the moments of videos are kept: per model, in memory for the searches, following the file when another connection changes it.</summary>
[Collection(FfmpegCollection.Name)]
public sealed class VideoMomentsCatalogTests : IDisposable
{
    private const string Clip = "clip-vit-b32";
    private const string Other = "siglip2-b16";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLVideoMoments_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public VideoMomentsCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static MomentData Moment(long ms, params float[] vector) => new(ms, vector, [0xFF, 0xD8, (byte)ms, 0xFF, 0xD9]);

    private async Task<VideoRecord> Video(VideoCatalog catalog, string name = "clip.mp4", string? recorded = null)
    {
        var file = RealFfmpeg.MakeVideo(_root, name, seconds: 2, recorded: recorded)!;
        return await catalog.GetAsync(file);
    }

    [Fact]
    public async Task Moments_AreKept_PerVideoAndModel_InOrderOfTime()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);

        catalog.SaveAnalysis(video.Id, Clip, [Moment(4000, 0, 1, 0), Moment(0, 1, 0, 0), Moment(2000, 0, 0, 1)]);
        catalog.SaveAnalysis(video.Id, Other, [Moment(500, 1, 1, 1)]);

        var sets = catalog.MomentSets(Clip);
        var set = Assert.Single(sets);
        Assert.Equal(video.Id, set.Video.Id);
        Assert.Equal(catalog.FullPathOf(video), set.FullPath);
        Assert.Equal([0L, 2000L, 4000L], set.TimesMs);
        Assert.Equal([1f, 0f, 0f], set.Vectors[0]);
        Assert.Equal([0f, 1f, 0f], set.Vectors[2]);
        Assert.Equal([500L], Assert.Single(catalog.MomentSets(Other)).TimesMs); // the other model has its own
        Assert.Equal((1, 3), catalog.CountAnalyzed(Clip));
        Assert.Equal((1, 1), catalog.CountAnalyzed(Other));
    }

    [Fact]
    public async Task TheThumbnails_ComeBackInOrder_ForTheVideoAndModelAsked()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);
        catalog.SaveAnalysis(video.Id, Clip, [Moment(3000, 1, 0), Moment(1000, 0, 1)]);

        var thumbnails = catalog.Thumbnails(catalog.FullPathOf(video), Clip);

        Assert.Equal([1000L, 3000L], thumbnails.Select(t => t.TimeMs));
        Assert.Equal(new byte[] { 0xFF, 0xD8, (byte)(1000 % 256), 0xFF, 0xD9 }, thumbnails[0].Jpeg);
        Assert.Empty(catalog.Thumbnails(catalog.FullPathOf(video), Other));
    }

    [Fact]
    public async Task SavingAgain_ReplacesTheMomentsOfThatModel()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);

        catalog.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0), Moment(2000, 0, 1)]);
        catalog.SaveAnalysis(video.Id, Clip, [Moment(1000, 1, 1)]);

        Assert.Equal([1000L], Assert.Single(catalog.MomentSets(Clip)).TimesMs);
        Assert.Equal((1, 1), catalog.CountAnalyzed(Clip));
    }

    [Fact]
    public async Task WhatWasDone_IsToldByVideo_AndAVideoThatFailedIsRememberedAndKeptOutOfSearches()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var good = await Video(catalog, "good.mp4");
        var bad = await Video(catalog, "bad.mp4");
        var untouched = await Video(catalog, "untouched.mp4");

        catalog.SaveAnalysis(good.Id, Clip, [Moment(0, 1, 0)]);
        catalog.SaveAnalysis(bad.Id, Clip, [], "no picture");

        var states = catalog.AnalysisStates(Clip);
        Assert.Equal(new AnalysisState(VideoCatalog.AnalysisVersion, 1, null), states[good.Id]);
        Assert.Equal(new AnalysisState(VideoCatalog.AnalysisVersion, 0, "no picture"), states[bad.Id]);
        Assert.False(states.ContainsKey(untouched.Id));
        Assert.Single(catalog.MomentSets(Clip));
        Assert.Equal((1, 1), catalog.CountAnalyzed(Clip));
        Assert.Equal(1, catalog.CountUnanalyzable());
        Assert.Equal([good.Id], catalog.AnalyzedRecords().Select(r => r.Id));
    }

    [Fact]
    public async Task AnotherConnection_SeesWhatIsStored_BecauseTheCacheFollowsTheFile()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var shown = VideoCatalog.Open(_root, DbFile);      // the window's
        using var job = VideoCatalog.Open(_root, DbFile);        // the indexing job's, a connection of its own
        var video = await Video(job);

        Assert.Empty(shown.MomentSets(Clip)); // cached as empty
        job.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0)]);

        Assert.Single(shown.MomentSets(Clip));
        job.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0), Moment(2000, 0, 1)]);
        Assert.Equal(2, shown.MomentSets(Clip)[0].TimesMs.Length);
        Assert.Equal((1, 2), shown.CountAnalyzed(Clip));
    }

    [Fact]
    public async Task TheFacesOfAMoment_AreKept_WithTheirBoxAndVector_AndGoWithTheMoment()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);
        MomentFace[] twoFaces = [new(0.1f, 0.2f, 0.3f, 0.4f, 0.9f, [1f, 0f]), new(0.5f, 0.5f, 0.1f, 0.1f, 0.8f, [0f, 1f])];

        catalog.SaveAnalysis(video.Id, Clip, [new MomentData(0, [1f, 0f], null, twoFaces), new MomentData(2000, [0f, 1f], null, null), new MomentData(4000, [1f, 1f], null, [twoFaces[1]])]);

        var set = Assert.Single(catalog.MomentSets(Clip));
        Assert.Equal([2, 0, 1], set.Faces.Select(f => f.Count));
        Assert.Equal((0.1f, 0.2f, 0.3f, 0.4f, 0.9f), (set.Faces[0][0].X, set.Faces[0][0].Y, set.Faces[0][0].Width, set.Faces[0][0].Height, set.Faces[0][0].Score));
        Assert.Equal(new[] { 1f, 0f }, set.Faces[0][0].Embedding);
        Assert.Equal(new[] { 0f, 1f }, set.Faces[2][0].Embedding);
        Assert.Equal(3, catalog.CountFaces(Clip));
        Assert.Equal(0, catalog.CountFaces(Other));

        // another analysis replaces the moments and takes the faces with them
        catalog.SaveAnalysis(video.Id, Clip, [new MomentData(1000, [1f, 0f], null, null)]);
        Assert.Equal(0, catalog.CountFaces(Clip));
        Assert.Empty(Assert.Single(catalog.MomentSets(Clip)).Faces[0]);
    }

    [Fact]
    public async Task AForgottenVideo_TakesItsFacesWithIt()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);
        catalog.SaveAnalysis(video.Id, Clip, [new MomentData(0, [1f, 0f], null, [new MomentFace(0, 0, 0.1f, 0.1f, 0.9f, [1f, 0f])])]);
        Assert.Equal(1, catalog.CountFaces(Clip));

        catalog.Forget(catalog.FullPathOf(video));

        Assert.Equal(0, catalog.CountFaces(Clip));
    }
    [Fact]
    public async Task TheSameAnswer_IsNotReadAgain_WhenNothingChanged()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);
        catalog.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0)]);

        Assert.Same(catalog.MomentSets(Clip), catalog.MomentSets(Clip));
    }

    [Fact]
    public async Task AVideoThatChanged_LosesItsMoments_WhenItIsReadAgain()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);
        catalog.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0)]);
        Assert.Single(catalog.MomentSets(Clip));

        File.Delete(catalog.FullPathOf(video));
        RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 4); // another file under the same name
        var again = await catalog.GetAsync(Path.Combine(_root, "clip.mp4"));

        Assert.Equal(video.Id, again.Id); // the same row, a new file
        Assert.Empty(catalog.MomentSets(Clip));
        Assert.Empty(catalog.AnalysisStates(Clip));
        Assert.Empty(catalog.Thumbnails(catalog.FullPathOf(again), Clip));
    }

    [Fact]
    public async Task AForgottenVideo_TakesItsMomentsWithIt()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var video = await Video(catalog);
        catalog.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0)]);
        Assert.Single(catalog.MomentSets(Clip));

        catalog.Forget(catalog.FullPathOf(video));

        Assert.Empty(catalog.MomentSets(Clip));
        Assert.Equal((0, 0), catalog.CountAnalyzed(Clip));
        Assert.Empty(catalog.Records());
    }

    [Fact]
    public async Task TheCatalogOfAVideoFolder_KeepsItsMoments_BetweenRuns()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        string path;
        using (var catalog = VideoCatalog.Open(_root, DbFile))
        {
            var video = await Video(catalog);
            path = catalog.FullPathOf(video);
            catalog.SaveAnalysis(video.Id, Clip, [Moment(0, 1, 0), Moment(2000, 0, 1)]);
        }

        using var later = VideoCatalog.Open(_root, DbFile);

        Assert.Equal(2, Assert.Single(later.MomentSets(Clip)).TimesMs.Length);
        Assert.Equal(2, later.Thumbnails(path, Clip).Count);
    }

    // ── the whole job, with a model that is not there ──────────────────

    [Fact]
    public async Task TheJob_ListsWhatIsNotAnalyzedYet_AndLeavesTheModelFilesAlone_WhenAskedNotToDownload()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        RealFfmpeg.MakeVideo(Path.Combine(_root, "sub"), "a.mp4", seconds: 2);
        RealFfmpeg.MakeVideo(_root, "b.mp4", seconds: 2);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not a video");
        using var catalog = VideoCatalog.Open(_root, DbFile);
        using var hub = new ModelHub(Path.Combine(_root, "no-models"));
        ModelDownload? asked = null;

        var summary = await VideoIndexingJob.RunAsync(
            catalog, hub, ClipModels.Default, _root, excludedFolder: null, [".mp4"], progress: null, CancellationToken.None,
            confirmDownload: download => { asked = download; return false; });

        Assert.True(summary.DownloadDeclined);
        Assert.NotNull(asked);
        Assert.Equal(ClipModels.Default, asked!.Clip);
        Assert.True(asked.Faces); // the face models too: who is in a video is part of what it shows
        Assert.Equal(2, catalog.Records().Count); // the videos were read, which is needed to know what there is to do
        Assert.Equal(0, catalog.CountAnalyzed(ClipModels.Default.Id).Videos);
    }

    [Fact]
    public async Task TheJob_ForgetsVideosThatAreGone_ButNotWhenTheFolderGivesNothing()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var a = RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 2)!;
        var b = RealFfmpeg.MakeVideo(_root, "b.mp4", seconds: 2)!;
        using var catalog = VideoCatalog.Open(_root, DbFile);
        await catalog.GetAsync(a);
        await catalog.GetAsync(b);
        using var hub = new ModelHub(Path.Combine(_root, "no-models"));

        File.Delete(b);
        var summary = await VideoIndexingJob.RunAsync(catalog, hub, ClipModels.Default, _root, null, [".mp4"], null, CancellationToken.None, _ => false);

        Assert.Equal(1, summary.Removed);
        Assert.Equal(["a.mp4"], catalog.Records().Select(r => r.RelativePath));

        // a folder with no videos in it lists nothing: nothing is forgotten then (it may be a share that answered with an empty folder)
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        var nothing = await VideoIndexingJob.RunAsync(catalog, hub, ClipModels.Default, empty, null, [".mp4"], null, CancellationToken.None, _ => false);
        Assert.Equal(0, nothing.Removed);
        Assert.Single(catalog.Records());

        // a folder that is not there is an error, as it is for the photos
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            VideoIndexingJob.RunAsync(catalog, hub, ClipModels.Default, Path.Combine(_root, "not-there"), null, [".mp4"], null, CancellationToken.None, _ => false));
    }

    [Fact]
    public async Task TheJob_WithTheRealModel_MakesMoments_AndHasNothingToDoTheSecondTime()
    {
        var models = new ModelHub();
        if (RealFfmpeg.Path is null || !models.ClipDownloaded(ClipModels.Default)) return; // needs the model the app downloaded
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        RealFfmpeg.MakeVideo(_root, "a.mp4", seconds: 7);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var reports = new List<IndexProgress>();

        var first = await VideoIndexingJob.RunAsync(catalog, models, ClipModels.Default, _root, null, [".mp4"], new Progress<IndexProgress>(reports.Add), CancellationToken.None);
        await Task.Delay(100);
        var second = await VideoIndexingJob.RunAsync(catalog, models, ClipModels.Default, _root, null, [".mp4"], null, CancellationToken.None);

        Assert.Equal(1, first.Analyzed);
        Assert.InRange(first.Moments, 1, 4);
        Assert.Equal((1, first.Moments), catalog.CountAnalyzed(ClipModels.Default.Id));
        Assert.Contains(reports, r => r.Phase == IndexPhase.Videos && r.Message == "a.mp4");
        Assert.Equal(0, second.Analyzed);
        Assert.Equal(1, second.Known);
    }
}
