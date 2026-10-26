using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>PhotoScanner lists the photos of a folder against a real temp file system.</summary>
public class PhotoScannerTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg", ".png"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLScannerTests_" + Guid.NewGuid());

    public PhotoScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Touch(string relativePath, string content = "x")
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static IEnumerable<string> Names(IEnumerable<PhotoFile> photos) =>
        photos.Select(p => Path.GetFileName(p.OriginalFilePath)).Order();

    [Fact]
    public void ListsPhotosOfTheFolder_AndNothingElse()
    {
        Touch("a.jpg");
        Touch("b.png");
        Touch("notes.txt");

        var photos = PhotoScanner.Scan(_root, recursive: false, Extensions, excludedFolder: null);

        Assert.Equal(["a.jpg", "b.png"], Names(photos));
    }

    [Fact]
    public void ExtensionsAreMatchedWithoutRegardToCase()
    {
        Touch("upper.JPG");
        Touch("mixed.Png");

        var photos = PhotoScanner.Scan(_root, recursive: false, Extensions, excludedFolder: null);

        Assert.Equal(["mixed.Png", "upper.JPG"], Names(photos));
    }

    [Fact]
    public void PhotoPathsAreFullPaths_WithTheSizeAndTimestampOfTheFile()
    {
        var path = Touch("a.jpg", "12345");

        var photo = Assert.Single(PhotoScanner.Scan(_root, recursive: false, Extensions, excludedFolder: null));

        Assert.Equal(path, photo.OriginalFilePath);
        Assert.Equal(string.Empty, photo.LikedFilePath);
        Assert.Equal(5, photo.Length);
        Assert.Equal(File.GetLastWriteTimeUtc(path), photo.LastWriteTimeUtc, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SubfoldersAreOnlyListedWhenAsked()
    {
        Touch("top.jpg");
        Touch(Path.Combine("sub", "deep.jpg"));

        Assert.Equal(["top.jpg"], Names(PhotoScanner.Scan(_root, recursive: false, Extensions, null)));
        Assert.Equal(["deep.jpg", "top.jpg"], Names(PhotoScanner.Scan(_root, recursive: true, Extensions, null)));
    }

    [Fact]
    public void ExcludedFolder_IsLeftOut_WhenGoingThroughSubfolders()
    {
        Touch("a.jpg");
        Touch(Path.Combine("Liked", "a.jpg"));
        Touch(Path.Combine("Other", "b.jpg"));

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, excludedFolder: Path.Combine(_root, "Liked"));

        Assert.Equal(["a.jpg", "b.jpg"], Names(photos));
        Assert.DoesNotContain(photos, p => p.OriginalFilePath.Contains(Path.DirectorySeparatorChar + "Liked" + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ExcludedFolder_IsMatchedWithoutRegardToCaseOrATrailingSeparator()
    {
        Touch(Path.Combine("Liked", "a.jpg"));

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, excludedFolder: Path.Combine(_root, "liked") + Path.DirectorySeparatorChar);

        Assert.Empty(photos);
    }

    [Theory]
    [InlineData("@eaDir")]            // Synology thumbnails
    [InlineData("#recycle")]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("System Volume Information")]
    [InlineData(".AppleDouble")]
    public void FoldersOfTheSystem_AreSkipped(string folder)
    {
        Touch("a.jpg");
        Touch(Path.Combine(folder, "thumb.jpg"));

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, excludedFolder: null);

        Assert.Equal(["a.jpg"], Names(photos));
    }

    [Fact]
    public void MacResourceForkStubs_AreNotPhotos()
    {
        Touch("a.jpg");
        Touch("._a.jpg");

        Assert.Equal(["a.jpg"], Names(PhotoScanner.Scan(_root, recursive: false, Extensions, null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFolder_NoPhotos(string folder)
    {
        Assert.Empty(PhotoScanner.Scan(folder, recursive: true, Extensions, null));
    }
}
