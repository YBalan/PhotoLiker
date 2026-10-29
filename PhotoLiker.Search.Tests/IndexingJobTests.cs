using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Search.Tests;

/// <summary>
/// Runs the real pipeline with the real models, so these tests only run on a machine that has them (the models are
/// downloaded by the app the first time a folder is indexed). Without models they pass without doing anything.
/// </summary>
public class IndexingJobTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg"];

    private readonly TempFolder _photos = new();
    private readonly ModelHub _models = new(RealModelsFolder());
    private readonly FolderIndex _search;

    public IndexingJobTests()
    {
        _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);
    }

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private static string RealModelsFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");

    private bool ModelsAvailable => _models.IsDownloaded;

    private static byte[] SolidJpeg(byte blue, byte green, byte red, int size = 160)
    {
        var pixels = new byte[size * size * 3];
        for (var i = 0; i < size * size; i++)
        {
            pixels[i * 3] = blue;
            pixels[i * 3 + 1] = green;
            pixels[i * 3 + 2] = red;
        }
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgr24, null, pixels, size * 3)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private void Write(string relativePath, byte[] content) => File.WriteAllBytes(_photos.File(relativePath), content);

    private Task<IndexSummary> IndexAsync(CancellationToken cancellationToken = default) =>
        _search.IndexAsync(_models, excludedFolder: null, Extensions, progress: null, cancellationToken);

    [Fact]
    public async Task IndexesPhotosInSubfolders_AndFindsThemByDescription()
    {
        if (!ModelsAvailable) return;
        Write("one/red.jpg", SolidJpeg(0, 0, 230));
        Write("two/blue.jpg", SolidJpeg(230, 20, 0));
        Write("two/green.jpg", SolidJpeg(0, 200, 20));

        var summary = await IndexAsync();

        Assert.Equal(3, summary.Indexed);
        Assert.Equal(0, summary.Failed);
        Assert.False(summary.Cancelled);
        Assert.Equal(3, _search.Index.PhotoCount);
        Assert.NotNull(summary.Times);

        var result = _search.SearchText(_models, "a plain red picture");
        Assert.Equal(Path.Combine(_photos.Path, "one", "red.jpg"), result.Hits[0].FullPath, ignoreCase: true);
    }

    [Fact]
    public async Task AnUnchangedFolder_IsNotDoneAgain_AChangedPhotoIs_AGonePhotoIsForgotten()
    {
        if (!ModelsAvailable) return;
        Write("a.jpg", SolidJpeg(0, 0, 230));
        Write("b.jpg", SolidJpeg(230, 0, 0));
        Write("c.jpg", SolidJpeg(0, 230, 0));
        await IndexAsync();

        var again = await IndexAsync();
        Assert.Equal(0, again.Indexed);
        Assert.Equal(3, _search.Index.PhotoCount);

        // b is replaced by another picture (a different size), c is deleted, d is new
        Write("b.jpg", SolidJpeg(200, 0, 200, size: 200));
        File.Delete(Path.Combine(_photos.Path, "c.jpg"));
        Write("d.jpg", SolidJpeg(100, 100, 100));
        var changed = await IndexAsync();

        Assert.Equal(2, changed.Indexed); // b and d
        Assert.Equal(1, changed.Removed); // c
        Assert.Equal(3, _search.Index.PhotoCount);
        Assert.Null(_search.Index.Find("c.jpg"));
    }

    [Fact]
    public async Task APhotoThatCanNotBeDecoded_IsReported_AndNotTriedAgainUntilItChanges()
    {
        if (!ModelsAvailable) return;
        Write("good.jpg", SolidJpeg(0, 0, 230));
        Write("broken.jpg", [1, 2, 3, 4, 5, 6, 7, 8]);

        var first = await IndexAsync();
        Assert.Equal(1, first.Indexed);
        Assert.Equal(1, first.Failed);
        Assert.Equal(1, _search.Index.FailedCount);
        Assert.NotNull(_search.Index.Find("broken.jpg")!.Failure);

        var second = await IndexAsync();
        Assert.Equal(0, second.Indexed + second.Failed);

        Write("broken.jpg", SolidJpeg(10, 10, 10)); // fixed
        var third = await IndexAsync();
        Assert.Equal(1, third.Indexed);
        Assert.Equal(0, _search.Index.FailedCount);
    }

    [Fact]
    public async Task StoppingKeepsWhatIsDone_AndTheNextRunContinues()
    {
        if (!ModelsAvailable) return;
        for (var i = 0; i < 40; i++)
            Write($"p{i:00}.jpg", SolidJpeg((byte)(i * 6), (byte)(255 - i * 6), (byte)(i * 3)));

        using var stop = new CancellationTokenSource();
        var running = IndexAsync(stop.Token);
        while (_search.Index.PhotoCount < 5 && !running.IsCompleted)
            await Task.Delay(20);
        stop.Cancel();

        var stopped = await running;
        Assert.True(stopped.Cancelled || _search.Index.PhotoCount == 40); // it may just have been quick
        var doneSoFar = _search.Index.PhotoCount;
        Assert.InRange(doneSoFar, 5, 40);

        var rest = await IndexAsync();
        Assert.Equal(40 - doneSoFar, rest.Indexed);
        Assert.Equal(40, _search.Index.PhotoCount);
    }

    [Fact]
    public async Task TheExcludedFolder_IsNotIndexed()
    {
        if (!ModelsAvailable) return;
        Write("a.jpg", SolidJpeg(0, 0, 230));
        Write("Liked/a.jpg", SolidJpeg(0, 0, 230)); // the copy of a liked photo

        await _search.IndexAsync(_models, Path.Combine(_photos.Path, "Liked"), Extensions, null, CancellationToken.None);

        Assert.Equal(1, _search.Index.PhotoCount);
        Assert.NotNull(_search.Index.Find("a.jpg"));
    }

    [Fact]
    public async Task AnUnreachableFolder_DoesNotEmptyTheIndex()
    {
        if (!ModelsAvailable) return;
        Write("a.jpg", SolidJpeg(0, 0, 230));
        await IndexAsync();

        // The share is gone (or listing it failed): nothing is found, which must not be taken as "all photos deleted"
        File.Delete(Path.Combine(_photos.Path, "a.jpg"));
        var summary = await IndexAsync();

        Assert.Equal(0, summary.Removed);
        Assert.Equal(1, _search.Index.PhotoCount);
    }
}
