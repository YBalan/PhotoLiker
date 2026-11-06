using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

[Collection(FfmpegCollection.Name)]
public sealed class VideoCatalogTests : IDisposable
{
    private const string Missing = @"Z:\no\such\ffmpeg.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PhotoLikerVideoTests_" + Guid.NewGuid().ToString("N"));
    private string DbFile => Path.Combine(_root, "catalog", "videos.db");

    public VideoCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* the next run's temp folder cleaner gets it */ }
    }

    [Fact]
    public void TheCatalog_IsAFileOfItsOwn_NextToThePhotoIndex()
    {
        var folder = Path.Combine(_root, "photos");

        var video = VideoPaths.CatalogFile(folder);
        var photos = SearchPaths.IndexFile(folder);

        Assert.NotEqual(photos, video);
        Assert.Equal(Path.GetDirectoryName(photos), Path.GetDirectoryName(video));
        Assert.StartsWith("video-", Path.GetFileName(video));
        Assert.Equal(video, VideoPaths.CatalogFile(folder.ToUpperInvariant())); // the same folder whatever the case
        Assert.NotEqual(video, VideoPaths.CatalogFile(Path.Combine(_root, "other")));
    }

    [Fact]
    public async Task AVideo_IsReadOnce_AndKnownFromTheCatalogAfterThat()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(Path.Combine(_root, "v"), "clip.mp4", seconds: 3, recorded: "2012-03-04T05:06:07Z")!;

        using (RealFfmpeg.Use(RealFfmpeg.Path))
        using (var catalog = VideoCatalog.Open(_root, DbFile))
        {
            var record = await catalog.GetAsync(file);

            Assert.Null(record.Failure);
            Assert.Equal("v/clip.mp4", record.RelativePath);
            Assert.Equal(3.0, record.Info!.DurationSeconds, 0.3);
            Assert.Equal((320, 240), (record.Info.Width, record.Info.Height));
            Assert.Equal("mpeg4", record.Info.VideoCodec);
            Assert.StartsWith("aac", record.Info.Audio);
            Assert.Equal(DateSource.Exif, record.Date.Source);
            Assert.Equal(DateTimeOffset.Parse("2012-03-04T05:06:07Z").LocalDateTime, record.Date.Taken);
            Assert.Equal(1, catalog.Count);
        }

        // Without any ffmpeg the same answer comes from the file of the catalog
        using (RealFfmpeg.Use(Missing))
        using (var again = VideoCatalog.Open(_root, DbFile))
        {
            var record = await again.GetAsync(file);

            Assert.Equal(3.0, record.Info!.DurationSeconds, 0.3);
            Assert.Equal(DateTimeOffset.Parse("2012-03-04T05:06:07Z").LocalDateTime, record.Date.Taken);
        }
    }

    [Fact]
    public async Task APoster_IsAJpegOfTheVideo_AndIsKept()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4")!;

        byte[] first;
        using (RealFfmpeg.Use(RealFfmpeg.Path))
        using (var catalog = VideoCatalog.Open(_root, DbFile))
        {
            first = await catalog.GetPosterAsync(file);

            Assert.True(first.Length > 1000);
            Assert.Equal(0xFF, first[0]);
            Assert.Equal(0xD8, first[1]); // starts like a JPEG
        }

        using (RealFfmpeg.Use(Missing))
        using (var again = VideoCatalog.Open(_root, DbFile))
            Assert.Equal(first, await again.GetPosterAsync(file));
    }

    [Fact]
    public async Task AVideoThatChanged_IsReadAgain_WithANewPoster()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 2)!;

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var before = await catalog.GetAsync(file);
        var posterBefore = await catalog.GetPosterAsync(file);

        File.Delete(file);
        RealFfmpeg.MakeVideo(_root, "clip.mp4", seconds: 4);
        var after = await catalog.GetAsync(file);
        var posterAfter = await catalog.GetPosterAsync(file);

        Assert.Equal(2.0, before.Info!.DurationSeconds, 0.3);
        Assert.Equal(4.0, after.Info!.DurationSeconds, 0.3);
        Assert.Equal(1, catalog.Count);
        Assert.NotEqual(posterBefore, posterAfter);
    }

    [Fact]
    public async Task AFileThatIsNotAVideo_IsRememberedAsUnreadable_NotReadAgain()
    {
        if (RealFfmpeg.Path is null) return;
        var file = Path.Combine(_root, "broken.mp4");
        File.WriteAllText(file, "this is not a video at all");

        using (RealFfmpeg.Use(RealFfmpeg.Path))
        using (var catalog = VideoCatalog.Open(_root, DbFile))
        {
            var record = await catalog.GetAsync(file);

            Assert.NotNull(record.Failure);
            Assert.Null(record.Info);
            await Assert.ThrowsAsync<InvalidDataException>(() => catalog.GetPosterAsync(file));
        }

        using (RealFfmpeg.Use(Missing))
        using (var again = VideoCatalog.Open(_root, DbFile))
            Assert.NotNull((await again.GetAsync(file)).Failure); // from the catalog: there is no ffmpeg to ask
    }

    [Fact]
    public async Task WithNoFfmpeg_ReadingFails_AndNothingIsRemembered()
    {
        var file = Path.Combine(_root, "clip.mp4");
        File.WriteAllText(file, "x");

        using var _ = RealFfmpeg.Use(Missing);
        using var catalog = VideoCatalog.Open(_root, DbFile);

        await Assert.ThrowsAsync<FileNotFoundException>(() => catalog.GetAsync(file));
        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public async Task AVideoThatIsGone_IsNotFound()
    {
        using var catalog = VideoCatalog.Open(_root, DbFile);

        await Assert.ThrowsAsync<FileNotFoundException>(() => catalog.GetAsync(Path.Combine(_root, "gone.mp4")));
    }

    [Fact]
    public async Task AForgottenVideo_IsReadAgainNextTime()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4")!;

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        await catalog.GetAsync(file);
        Assert.Equal(1, catalog.Count);

        catalog.Forget(file);
        Assert.Equal(0, catalog.Count);
        catalog.Forget(file); // nothing to forget: no harm

        Assert.NotNull((await catalog.GetAsync(file)).Info);
    }

    [Fact]
    public async Task ADateThatIsNotInTheVideo_IsTheDateOfTheFile()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4")!; // no creation_time written
        var written = new DateTime(2015, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        File.SetCreationTimeUtc(file, written);

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);
        var record = await catalog.GetAsync(file);

        Assert.Equal(DateSource.File, record.Date.Source);
        Assert.Equal(written.ToLocalTime(), record.Date.Taken);
    }

    [Fact]
    public async Task ACatalogOfAnotherLayout_IsMadeAgain()
    {
        if (RealFfmpeg.Path is null) return;
        var file = RealFfmpeg.MakeVideo(_root, "clip.mp4")!;
        Directory.CreateDirectory(Path.GetDirectoryName(DbFile)!);
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + DbFile + ";Pooling=False"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL); INSERT INTO meta VALUES ('schema', '0'); CREATE TABLE videos (path TEXT)";
            command.ExecuteNonQuery();
        }

        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using var catalog = VideoCatalog.Open(_root, DbFile);

        Assert.NotNull((await catalog.GetAsync(file)).Info);
    }
}
