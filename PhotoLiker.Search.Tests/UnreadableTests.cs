using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

public class UnreadablePhotosInTheIndexTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();

    public void Dispose()
    {
        _photos.Dispose();
        _data.Dispose();
    }

    [Fact]
    public void ThePhotosTheIndexerCouldNotDecode_AreListedWithTheirFullPathAndWhy()
    {
        using var index = PhotoIndex.Open(_photos.Path, Path.Combine(_data.Path, "index.db"));
        var fine = new IndexedPhoto("314CANON/IMG_0001.jpg", 1000, 1) { Clip = Vectors.Unit(1), ClipVersion = 1, FaceVersion = 1 };
        var broken = new IndexedPhoto("145_1204/IMG_2117.JPG", 5099211, 2) { Failure = "Could not decode the photo: The image header might be corrupted." };
        var alsoBroken = new IndexedPhoto("a/b.jpg", 42, 3) { Failure = "CLIP failed: out of memory" };
        index.Save([fine, broken, alsoBroken]);

        var failed = index.FailedPhotos();

        Assert.Equal(
        [
            new UnreadablePhoto(Path.Combine(_photos.Path, "145_1204", "IMG_2117.JPG"), 5099211, "Could not decode the photo: The image header might be corrupted."),
            new UnreadablePhoto(Path.Combine(_photos.Path, "a", "b.jpg"), 42, "CLIP failed: out of memory"),
        ], failed);
        Assert.Equal(2, index.FailedCount);
    }

    [Fact]
    public void AnIndexWithoutFailures_HasNoUnreadablePhotos()
    {
        using var index = PhotoIndex.Open(_photos.Path, Path.Combine(_data.Path, "index.db"));
        index.Save([new IndexedPhoto("a.jpg", 1000, 1) { Clip = Vectors.Unit(1), ClipVersion = 1, FaceVersion = 1 }]);

        Assert.Empty(index.FailedPhotos());
    }
}
