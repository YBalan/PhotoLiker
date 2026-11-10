using System.Security.Cryptography;
using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

/// <summary>Align dates for videos: the date a video was recorded as the date to align the file to, only the file changing, and the catalog going on knowing the video after.</summary>
[Collection(FfmpegCollection.Name)]
public sealed class VideoDateFactsTests : IDisposable
{
    private const string Clip = "clip-vit-b32";
    private const string Recorded = "2012-03-04T05:06:07Z"; // UTC, as a phone writes it

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLVideoDates_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public VideoDateFactsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static PhotoFile Listed(string path) => new(path, string.Empty);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static DateTime RecordedLocal => DateTime.SpecifyKind(DateTime.Parse(Recorded, null, System.Globalization.DateTimeStyles.AdjustToUniversal), DateTimeKind.Utc).ToLocalTime();

    private static void Same(DateTime expected, DateTime? actual) => Assert.True(actual is { } a && DateAlignment.SameMoment(expected, a), $"{expected:O} expected, {actual:O} found");

    // ── what is known of a video ───────────────────────────────────────

    [Fact]
    public async Task AVideoThatSaysWhenItWasRecorded_HasThatDate_AsItsDateTaken_WithTheDatesOfItsFile()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var file = RealFfmpeg.MakeVideo(_root, "phone.mp4", seconds: 2, recorded: Recorded)!;
        using var catalog = VideoCatalog.Open(_root, DbFile);

        var facts = await VideoDateFacts.InspectAsync(catalog, Listed(file), default);

        Assert.Null(facts.Problem);
        Same(RecordedLocal, facts.Taken);
        Assert.Equal(File.GetLastWriteTimeUtc(file), facts.ModifiedUtc);
        Assert.Equal(new FileInfo(file).CreationTimeUtc, facts.CreatedUtc);
        Assert.Equal(ExifDateSlots.None, facts.Exif); // nothing inside the video that could be written
    }

    [Fact]
    public async Task AVideoThatSaysNothing_HasNoDateTaken_TheDateOfItsFileIsNoDateToAlignTo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var file = RealFfmpeg.MakeVideo(_root, "plain.mp4", seconds: 2)!;
        using var catalog = VideoCatalog.Open(_root, DbFile);

        var facts = await VideoDateFacts.InspectAsync(catalog, Listed(file), default);

        Assert.Null(facts.Taken);
        var entry = DateAlignment.Plan(facts, DateAlignmentOptions.Default);
        Assert.Equal(DateAlignmentStatus.NoDate, entry.Status);
    }

    [Fact]
    public async Task AFileThatIsNoVideo_AndAFileThatIsGone_AreFacts_NotFailures()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var broken = Path.Combine(_root, "broken.mp4");
        File.WriteAllText(broken, "this is not a video");
        using var catalog = VideoCatalog.Open(_root, DbFile);

        var unreadable = await VideoDateFacts.InspectAsync(catalog, Listed(broken), default);
        var gone = await VideoDateFacts.InspectAsync(catalog, Listed(Path.Combine(_root, "gone.mp4")), default);

        Assert.NotNull(unreadable.Problem);
        Assert.NotNull(gone.Problem);
        Assert.Equal(DateAlignmentStatus.Unreadable, DateAlignment.Plan(unreadable, DateAlignmentOptions.Default).Status);
        Assert.Equal(DateAlignmentStatus.Unreadable, DateAlignment.Plan(gone, DateAlignmentOptions.Default).Status);
    }

    [Fact]
    public async Task AllTheVideos_AreLookedAt_InTheOrderGiven_WithTheProgress()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var files = Enumerable.Range(0, 5).Select(i => RealFfmpeg.MakeVideo(_root, $"v{i}.mp4", seconds: 1, recorded: Recorded)!).ToList();
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var reported = new List<int>();

        var facts = await VideoDateFacts.InspectAllAsync(catalog, [.. files.Select(Listed)], new Progress5(reported), default);

        Assert.Equal(files, facts.Select(f => f.Path));
        Assert.All(facts, f => Same(RecordedLocal, f.Taken));
        Assert.Equal(5, reported.Max());
    }

    private sealed class Progress5(List<int> seen) : IProgress<int>
    {
        public void Report(int value) { lock (seen) seen.Add(value); }
    }

    // ── what the tool does with it ─────────────────────────────────────

    [Fact]
    public async Task TheTool_MakesTheDatesOfTheFileTheDateRecorded_WithoutWritingTheVideo_AndUndoPutsThemBack()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var file = RealFfmpeg.MakeVideo(_root, "phone.mp4", seconds: 2, recorded: Recorded)!;
        var contents = Hash(file);
        var oldCreated = new FileInfo(file).CreationTimeUtc;
        var oldModified = File.GetLastWriteTimeUtc(file);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var entry = DateAlignment.Plan(await VideoDateFacts.InspectAsync(catalog, Listed(file), default), new DateAlignmentOptions(Created: true, Modified: true, ExifFields: true));

        Assert.Equal(DateAlignmentStatus.Change, entry.Status);
        Assert.Equal(ExifDateFields.None, entry.SetExif); // the dates inside are for photos

        var log = Path.Combine(_root, "log.jsonl");
        var report = DateAligner.Apply([entry], _root, log, null, null, default);

        Assert.Single(report.Photos);
        Assert.Empty(report.Failures);
        Same(RecordedLocal, DateTime.SpecifyKind(File.GetLastWriteTimeUtc(file), DateTimeKind.Utc).ToLocalTime());
        Same(RecordedLocal, DateTime.SpecifyKind(new FileInfo(file).CreationTimeUtc, DateTimeKind.Utc).ToLocalTime());
        Assert.Equal(contents, Hash(file)); // the video is what it was, byte for byte

        var undone = DateAligner.Undo(log, null, default);

        Assert.Single(undone.Photos);
        Assert.True(DateAlignment.SameMoment(oldModified, File.GetLastWriteTimeUtc(file)));
        Assert.True(DateAlignment.SameMoment(oldCreated, new FileInfo(file).CreationTimeUtc));
        Assert.Equal(contents, Hash(file));
    }

    [Fact]
    public async Task ADateGivenToAVideoThatHasNone_BecomesTheDatesOfItsFile()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var file = RealFfmpeg.MakeVideo(_root, "plain.mp4", seconds: 2)!;
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var facts = await VideoDateFacts.InspectAsync(catalog, Listed(file), default);
        var given = new DateTime(2010, 5, 5, 12, 0, 0);

        var entry = DateAlignment.Plan(facts, new DateAlignmentOptions(true, true, ExifFields: false), given, writeTaken: false);
        DateAligner.Apply([entry], _root, Path.Combine(_root, "log.jsonl"), null, null, default);

        Assert.Equal(DateAlignmentStatus.Change, entry.Status);
        Same(given, DateTime.SpecifyKind(File.GetLastWriteTimeUtc(file), DateTimeKind.Utc).ToLocalTime());
    }

    // ── the catalog after it ───────────────────────────────────────────

    private async Task<(VideoRecord Video, string File)> AnalyzedVideo(VideoCatalog catalog, string? recorded)
    {
        var file = RealFfmpeg.MakeVideo(_root, "analyzed.mp4", seconds: 2, recorded: recorded)!;
        var video = await catalog.GetAsync(file);
        catalog.SaveAnalysis(video.Id, Clip, [new MomentData(0, [1, 0, 0], [0xFF, 0xD8, 0xFF, 0xD9]), new MomentData(1000, [0, 1, 0], [0xFF, 0xD8, 0xFF, 0xD9])]);
        catalog.SaveTranscript(video.Id, "small", "ru", [new SpeechSegment(0, 900, "раз")]);
        return (video, file);
    }

    [Fact]
    public async Task WhenTheCatalogIsToldTheNewStamp_TheMomentsAndTheSpeechOfTheVideoStay()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var (video, file) = await AnalyzedVideo(catalog, Recorded);
        var entry = DateAlignment.Plan(await VideoDateFacts.InspectAsync(catalog, Listed(file), default), DateAlignmentOptions.Default);

        var report = DateAligner.Apply([entry], _root, Path.Combine(_root, "log.jsonl"), null, null, default);
        catalog.UpdateStamps([.. report.Photos.Select(p => (p.Path, p.PreviousModifiedUtc.Ticks, p.ModifiedUtc.Ticks))]);

        var again = await catalog.GetAsync(file);
        Assert.Equal(video.Id, again.Id); // the same video: not read again
        Assert.Equal((1, 2), catalog.CountAnalyzed(Clip));
        Assert.Equal((1, 1), catalog.CountTranscribed("small"));
        Assert.Equal(File.GetLastWriteTimeUtc(file).Ticks, again.ModifiedTicks);
    }

    [Fact]
    public async Task WithoutIt_TheAlignedVideoWouldBeTakenForAnotherOne_AndLoseItsAnalysis()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var (_, file) = await AnalyzedVideo(catalog, Recorded);
        var entry = DateAlignment.Plan(await VideoDateFacts.InspectAsync(catalog, Listed(file), default), DateAlignmentOptions.Default);

        DateAligner.Apply([entry], _root, Path.Combine(_root, "log.jsonl"), null, null, default);
        await catalog.GetAsync(file); // the file has another date of change: read again

        Assert.Equal((0, 0), catalog.CountAnalyzed(Clip)); // which is the reason the stamps are given to the catalog
    }

    [Fact]
    public async Task TheDateOfAVideoThatSaysNone_FollowsItsFile_AfterTheStamp()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var (_, file) = await AnalyzedVideo(catalog, recorded: null);
        var given = new DateTime(2010, 5, 5, 12, 0, 0);
        var entry = DateAlignment.Plan(await VideoDateFacts.InspectAsync(catalog, Listed(file), default), new DateAlignmentOptions(true, true, false), given);

        var report = DateAligner.Apply([entry], _root, Path.Combine(_root, "log.jsonl"), null, null, default);
        catalog.UpdateStamps([.. report.Photos.Select(p => (p.Path, p.PreviousModifiedUtc.Ticks, p.ModifiedUtc.Ticks))]);

        var set = Assert.Single(catalog.MomentSets(Clip)); // what a search goes through holds the new date
        Same(given, set.Video.Date.Taken);
        Assert.Equal(DateSource.File, set.Video.Date.Source);
    }

    [Fact]
    public async Task ADateWrittenIntoTheVideo_IsTheDateTheCatalogKnowsItBy_ItsAnalysisStays_AndAnUndoPutsEverythingBack()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var (video, file) = await AnalyzedVideo(catalog, Recorded);
        var before = File.ReadAllBytes(file);
        var given = new DateTime(2003, 1, 31, 18, 6, 0);
        var entry = DateAlignment.Plan(await VideoDateFacts.InspectAsync(catalog, Listed(file), default), DateAlignmentOptions.Default, given, writeTaken: true);
        Assert.True(entry.SetExif.HasFlag(ExifDateFields.Original));

        var report = DateAligner.Apply([entry], _root, Path.Combine(_root, "log.jsonl"), null, null, default);
        Assert.Empty(report.Failures);
        catalog.UpdateStamps([.. report.Photos.Select(p => (p.Path, p.PreviousModifiedUtc.Ticks, p.ModifiedUtc.Ticks))]);
        await catalog.RefreshRecordedAsync(file);

        Assert.Equal(before.Length, new FileInfo(file).Length); // written in place
        Same(given, (await VideoProbe.ReadAsync(file)).Recorded); // what ffmpeg reads from the file itself
        var again = await catalog.GetAsync(file);
        Assert.Equal(video.Id, again.Id); // the same video: not read again
        Same(given, again.Info!.Recorded);
        Same(given, again.Date.Taken);
        Assert.Equal((1, 2), catalog.CountAnalyzed(Clip));
        Assert.Equal((1, 1), catalog.CountTranscribed("small"));

        var undone = DateAligner.Undo(report.LogFile, null, default);
        catalog.UpdateStamps([.. undone.Photos.Select(p => (p.Path, p.PreviousModifiedUtc.Ticks, p.ModifiedUtc.Ticks))]);
        foreach (var p in undone.Photos.Where(p => p.Written.HasFlag(ExifDateFields.Original)))
            await catalog.RefreshRecordedAsync(p.Path);

        Assert.Equal(before, File.ReadAllBytes(file)); // every byte as it was
        var back = await catalog.GetAsync(file);
        Assert.Equal(video.Id, back.Id);
        Same(RecordedLocal, back.Date.Taken);
        Assert.Equal((1, 2), catalog.CountAnalyzed(Clip));
    }

    [Fact]
    public async Task AStampThatIsNotTheOneTheCatalogKnows_IsIgnored_TheVideoWasChangedBySomeoneElse()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var (video, file) = await AnalyzedVideo(catalog, Recorded);

        catalog.UpdateStamps([(file, video.ModifiedTicks + 12345, video.ModifiedTicks + 99999)]);

        Assert.Equal(video.ModifiedTicks, (await catalog.GetAsync(file)).ModifiedTicks);
        Assert.Equal((1, 2), catalog.CountAnalyzed(Clip));
    }
}
