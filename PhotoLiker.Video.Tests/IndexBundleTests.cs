using System.IO.Compression;
using PhotoLiker.Search;

namespace PhotoLiker.Video.Tests;

/// <summary>Moving the index of a folder to another computer: the photo index and the catalog of the videos in one file, and a plain photo index when there are no videos.</summary>
[Collection(FfmpegCollection.Name)]
public sealed class IndexBundleTests : IDisposable
{
    private const string Clip = "clip-vit-b32";

    private readonly string _work = Path.Combine(Path.GetTempPath(), "PLIndexBundle_" + Guid.NewGuid().ToString("N"));
    private readonly string _source;   // the folder where the index was made
    private readonly string _target;   // the same folder as seen from the other computer
    private readonly string _carried;  // where the exported file travels

    public IndexBundleTests()
    {
        _source = Path.Combine(_work, "source");
        _target = Path.Combine(_work, "target");
        _carried = Path.Combine(_work, "carried");
        foreach (var folder in new[] { _source, _target, _carried })
            Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        foreach (var folder in new[] { _source, _target })
        {
            foreach (var file in new[] { SearchPaths.IndexFile(folder), VideoPaths.CatalogFile(folder) })
                try { File.Delete(file); } catch (IOException) { }
        }
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    private string Exported => Path.Combine(_carried, "folder.plindex");

    private PhotoIndex MakePhotoIndex()
    {
        var vector = new float[ClipModels.Default.Dimensions];
        vector[0] = 1;
        var index = PhotoIndex.Open(_source);
        index.Save([new IndexedPhoto("2009/one.jpg", length: 1000, modifiedTicks: 5000) { Clips = new Dictionary<string, ClipVector> { [Clip] = new(vector, IndexingJob.ClipVersion) } }]);
        return index;
    }

    // A catalog with one analyzed video of two moments, and what is said in it
    private async Task<VideoRecord> MakeVideoCatalog(bool analyzed = true, bool speech = true)
    {
        var file = RealFfmpeg.MakeVideo(_source, "clip.mp4", seconds: 2, recorded: null)!;
        using var catalog = VideoCatalog.Open(_source);
        var video = await catalog.GetAsync(file);
        if (analyzed)
            catalog.SaveAnalysis(video.Id, Clip, [new MomentData(0, [1, 0, 0], [0xFF, 0xD8, 0xFF, 0xD9]), new MomentData(2000, [0, 1, 0], [0xFF, 0xD8, 0xFF, 0xD9])]);
        if (speech)
            catalog.SaveTranscript(video.Id, "small", "ru", [new SpeechSegment(0, 1500, "лошадка")]);
        return video;
    }

    [Fact]
    public async Task WithVideosAnalyzed_TheExportIsOneFile_ThatCarriesBothIndexes()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        await MakeVideoCatalog();

        VideoIndexFileInfo? exported;
        using (var photos = MakePhotoIndex())
            exported = IndexBundle.Export(photos, Exported);

        Assert.Equal(new VideoIndexFileInfo(1, 2, 1), exported);
        Assert.Equal([Exported], Directory.GetFiles(_carried)); // no part and no temp file left next to it
        Assert.True(IndexBundle.IsBundle(Exported));

        var info = IndexBundle.Inspect(Exported);
        Assert.Equal(1, info.Photos.Photos);
        Assert.Equal(new VideoIndexFileInfo(1, 2, 1), info.Videos);
        Assert.Equal([Exported], Directory.GetFiles(_carried)); // looking inside leaves nothing either
    }

    [Fact]
    public async Task ImportingIt_GivesTheOtherFolderThePhotosAndTheVideos_WithTheirMomentsAndSpeech()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        var video = await MakeVideoCatalog();
        using (var photos = MakePhotoIndex())
            IndexBundle.Export(photos, Exported);

        var replacedVideos = IndexBundle.Import(_target, Exported);

        Assert.True(replacedVideos);
        using var importedPhotos = PhotoIndex.Open(_target);
        Assert.Equal(1, importedPhotos.PhotoCount);
        using var catalog = VideoCatalog.Open(_target);
        Assert.Equal((1, 2), catalog.CountAnalyzed(Clip));
        Assert.Equal((1, 1), catalog.CountTranscribed("small"));
        var set = Assert.Single(catalog.MomentSets(Clip));
        Assert.Equal([0L, 2000L], set.TimesMs);
        Assert.Equal(Path.Combine(_target, "clip.mp4"), set.FullPath); // the paths follow the folder
        Assert.Equal("лошадка", Assert.Single(catalog.SpeechOf(Path.Combine(_target, "clip.mp4"))).Text);
        Assert.Equal(video.Length, set.Video.Length);
    }

    [Fact]
    public async Task WithNoVideoAnalyzed_TheExportIsThePhotoIndexAlone_AsItAlwaysWas()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        await MakeVideoCatalog(analyzed: false, speech: false); // only what the header says

        VideoIndexFileInfo? exported;
        using (var photos = MakePhotoIndex())
            exported = IndexBundle.Export(photos, Exported);

        Assert.Null(exported);
        Assert.False(IndexBundle.IsBundle(Exported));
        Assert.Equal(1, PhotoIndex.Inspect(Exported).Photos); // an older version reads it
        Assert.Null(IndexBundle.Inspect(Exported).Videos);
        Assert.Equal([Exported], Directory.GetFiles(_carried));
    }

    [Fact]
    public void AFolderWithNoCatalogAtAll_ExportsThePhotosAlone()
    {
        VideoIndexFileInfo? exported;
        using (var photos = MakePhotoIndex())
            exported = IndexBundle.Export(photos, Exported);

        Assert.Null(exported);
        Assert.False(IndexBundle.IsBundle(Exported));
        Assert.False(File.Exists(VideoPaths.CatalogFile(_source)));
    }

    [Fact]
    public async Task APhotoIndexFile_LeavesTheVideosOfTheFolderAsTheyAre()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        using (var photos = MakePhotoIndex())
            photos.ExportTo(Exported); // a file of photos alone, as an older version writes it

        var target = Path.Combine(_work, "kept");
        Directory.CreateDirectory(target);
        var file = RealFfmpeg.MakeVideo(target, "keep.mp4", seconds: 2, recorded: null)!;
        using (var catalog = VideoCatalog.Open(target))
            catalog.SaveAnalysis((await catalog.GetAsync(file)).Id, Clip, [new MomentData(0, [1, 0, 0], [0xFF, 0xD8, 0xFF, 0xD9])]);

        Assert.False(IndexBundle.Import(target, Exported));

        using var kept = VideoCatalog.Open(target);
        Assert.Equal((1, 1), kept.CountAnalyzed(Clip)); // still there
        foreach (var f in new[] { SearchPaths.IndexFile(target), VideoPaths.CatalogFile(target) })
            try { File.Delete(f); } catch (IOException) { }
    }

    [Fact]
    public async Task ABundleThatIsNotWhole_ChangesNothing_AndSaysSo()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        await MakeVideoCatalog();
        using (var photos = MakePhotoIndex())
            IndexBundle.Export(photos, Exported);

        // A bundle whose videos part is not a catalog
        var broken = Path.Combine(_carried, "broken.plindex");
        using (var from = System.IO.Compression.ZipFile.OpenRead(Exported))
        using (var to = System.IO.Compression.ZipFile.Open(broken, System.IO.Compression.ZipArchiveMode.Create))
        {
            from.GetEntry("photos.plindex")!.ExtractToFile(Path.Combine(_work, "p.tmp"), overwrite: true);
            to.CreateEntryFromFile(Path.Combine(_work, "p.tmp"), "photos.plindex");
            var entry = to.CreateEntry("videos.plvideo");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("this is not a database");
        }

        Assert.Throws<InvalidDataException>(() => IndexBundle.Inspect(broken));
        Assert.Throws<InvalidDataException>(() => IndexBundle.Import(_target, broken));
        Assert.False(File.Exists(SearchPaths.IndexFile(_target))); // the photos were not replaced either
        Assert.False(File.Exists(VideoPaths.CatalogFile(_target)));
        Assert.Equal(["broken.plindex", "folder.plindex"], Directory.GetFiles(_carried).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AZipThatIsNoBundle_IsNotAnIndex()
    {
        var zip = Path.Combine(_carried, "other.plindex");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            archive.CreateEntry("readme.txt");

        Assert.Throws<InvalidDataException>(() => IndexBundle.Inspect(zip));
        Assert.Empty(Directory.GetFiles(_carried, "*.part"));
    }

    /// <summary>Takes the reports where they are made (Progress&lt;T&gt; would post them to another thread).</summary>
    private sealed class Reports : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);

        public void AssertWholeRun()
        {
            Assert.NotEmpty(Values);
            Assert.Equal(Values.Order().ToList(), Values); // the bar only goes forward
            Assert.Equal(1.0, Values[^1]);
            Assert.All(Values, value => Assert.InRange(value, 0.0, 1.0));
        }
    }

    [Fact]
    public async Task ExportingBothIndexes_AndImportingThem_SayHowFarTheyAre_FromNothingToDone()
    {
        if (RealFfmpeg.Path is null) return;
        using var _ = RealFfmpeg.Use(RealFfmpeg.Path);
        await MakeVideoCatalog();

        var exporting = new Reports();
        using (var photos = MakePhotoIndex())
            IndexBundle.Export(photos, Exported, exporting);
        exporting.AssertWholeRun();
        Assert.True(IndexBundle.IsBundle(Exported));

        var looking = new Reports();
        Assert.NotNull(IndexBundle.Inspect(Exported, looking).Videos);
        looking.AssertWholeRun();

        var importing = new Reports();
        Assert.True(IndexBundle.Import(_target, Exported, importing));
        importing.AssertWholeRun();
        using var imported = PhotoIndex.Open(_target);
        Assert.Equal(1, imported.PhotoCount); // the progress did not cost the work
    }

    [Fact]
    public void ThePhotoIndexAlone_SaysHowFarItIs_Too()
    {
        var exporting = new Reports();
        using (var photos = MakePhotoIndex())
            Assert.Null(IndexBundle.Export(photos, Exported, exporting));
        exporting.AssertWholeRun();

        var looking = new Reports();
        Assert.Null(IndexBundle.Inspect(Exported, looking).Videos);
        looking.AssertWholeRun();

        var importing = new Reports();
        Assert.False(IndexBundle.Import(_target, Exported, importing));
        importing.AssertWholeRun();
    }

    [Fact]
    public void ABigIndex_ReportsOnTheWay_NotOnlyAtTheEnd()
    {
        // Enough photos for the copies to take several steps: a bar that jumps from 0 to 100 is no bar
        var vector = new float[ClipModels.Default.Dimensions];
        using (var index = PhotoIndex.Open(_source))
        {
            var photos = new List<IndexedPhoto>();
            for (var i = 0; i < 4000; i++)
            {
                vector[i % vector.Length] = 1;
                photos.Add(new IndexedPhoto($"p/{i}.jpg", length: 1000, modifiedTicks: 5000 + i) { Clips = new Dictionary<string, ClipVector> { [Clip] = new((float[])vector.Clone(), IndexingJob.ClipVersion) } });
            }
            index.Save(photos);

            var exporting = new Reports();
            IndexBundle.Export(index, Exported, exporting);
            exporting.AssertWholeRun();
            Assert.Contains(exporting.Values, value => value is > 0 and < 1);
        }

        var importing = new Reports();
        IndexBundle.Import(_target, Exported, importing);
        importing.AssertWholeRun();
        Assert.True(importing.Values.Count > 1, "more than the last report: " + importing.Values.Count);
    }

    [Fact]
    public void AMissingFile_IsNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => IndexBundle.Inspect(Path.Combine(_carried, "nothing.plindex")));
        Assert.Throws<FileNotFoundException>(() => IndexBundle.Import(_target, Path.Combine(_carried, "nothing.plindex")));
    }
}
