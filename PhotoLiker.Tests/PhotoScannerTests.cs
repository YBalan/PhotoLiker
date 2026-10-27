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
    public void ThePhotoKnowsWhenItsFileWasCreated_WhichIsNotWhenItWasLastChanged()
    {
        var path = Touch("a.jpg", "12345");
        File.SetCreationTimeUtc(path, new DateTime(2010, 5, 5, 12, 0, 0, DateTimeKind.Utc));

        var photo = Assert.Single(PhotoScanner.Scan(_root, recursive: false, Extensions, excludedFolder: null));

        Assert.Equal(new DateTime(2010, 5, 5, 12, 0, 0, DateTimeKind.Utc), photo.CreationTimeUtc, TimeSpan.FromSeconds(2));
        Assert.True(photo.LastWriteTimeUtc > photo.CreationTimeUtc);
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
    [InlineData("Deleted")]
    [InlineData("deleted")]
    [InlineData("DELETED")]
    public void TheDeletedFolder_IsSkipped_WhateverItsCase(string folder)
    {
        Touch("a.jpg");
        Touch(Path.Combine(folder, "gone.jpg"));
        Touch(Path.Combine(folder, "2024", "gone-too.jpg"));

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, excludedFolder: null);

        Assert.Equal(["a.jpg"], Names(photos));
    }

    [Fact]
    public void TheDeletedFolder_IsSkipped_WithAnExcludedFolderToo()
    {
        Touch("a.jpg");
        Touch(Path.Combine("Deleted", "gone.jpg"));
        Touch(Path.Combine("Liked", "liked.jpg"));
        Touch(Path.Combine("Other", "b.jpg"));

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, excludedFolder: Path.Combine(_root, "Liked"));

        Assert.Equal(["a.jpg", "b.jpg"], Names(photos));
    }

    [Fact]
    public void OnlyTheDeletedFolderOfTheScannedFolderIsSkipped_NotOneFurtherDown()
    {
        Touch(Path.Combine("trips", "Deleted", "mine.jpg")); // somebody's own folder that happens to be called that
        Touch(Path.Combine("Deleted", "gone.jpg"));

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, excludedFolder: null);

        Assert.Equal(["mine.jpg"], Names(photos));
    }

    [Fact]
    public void TheDeletedFolder_CanBeScannedItself_ToLookAtWhatWasDeleted()
    {
        Touch(Path.Combine("Deleted", "gone.jpg"));
        Touch(Path.Combine("Deleted", "2024", "gone-too.jpg"));

        var photos = PhotoScanner.Scan(Path.Combine(_root, "Deleted"), recursive: true, Extensions, excludedFolder: null);

        Assert.Equal(["gone-too.jpg", "gone.jpg"], Names(photos));
    }

    [Fact]
    public void TheDeletedFolder_IsSkipped_WhenTheScannedFolderIsGivenWithATrailingSeparator()
    {
        Touch("a.jpg");
        Touch(Path.Combine("Deleted", "gone.jpg"));

        Assert.Equal(["a.jpg"], Names(PhotoScanner.Scan(_root + Path.DirectorySeparatorChar, recursive: true, Extensions, null)));
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

    // ── order ───────────────────────────────────────────────────────────────

    private string TouchCreated(string relativePath, int year)
    {
        var path = Touch(relativePath);
        File.SetCreationTimeUtc(path, new DateTime(year, 6, 1, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    // Names run against the dates, so the order of the names can't pass for the order of the dates
    private void MakeThreePhotos()
    {
        TouchCreated("a-newest.jpg", 2020);
        TouchCreated("b-oldest.jpg", 2005);
        TouchCreated(Path.Combine("sub", "c-middle.jpg"), 2012);
    }

    private static string[] InOrder(IEnumerable<PhotoFile> photos) => [.. photos.Select(p => Path.GetFileName(p.OriginalFilePath))];

    [Fact]
    public void OldestFirst_ListsByTheDateTheFileWasCreated_NotByName()
    {
        MakeThreePhotos();

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, null, PhotoSortOrder.OldestFirst);

        Assert.Equal(["b-oldest.jpg", "c-middle.jpg", "a-newest.jpg"], InOrder(photos));
    }

    [Fact]
    public void NewestFirst_IsTheOtherWayRound()
    {
        MakeThreePhotos();

        var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, null, PhotoSortOrder.NewestFirst);

        Assert.Equal(["a-newest.jpg", "c-middle.jpg", "b-oldest.jpg"], InOrder(photos));
    }

    [Fact]
    public void None_KeepsTheOrderOfTheFileSystem_AndSoDoesNoOrderAtAll()
    {
        MakeThreePhotos();

        var unsorted = InOrder(PhotoScanner.Scan(_root, recursive: true, Extensions, null));

        Assert.Equal(unsorted, InOrder(PhotoScanner.Scan(_root, recursive: true, Extensions, null, PhotoSortOrder.None)));
        Assert.Equal(["a-newest.jpg", "b-oldest.jpg", "c-middle.jpg"], unsorted.Order()); // it is the same photos
    }

    [Fact]
    public void PhotosCreatedAtTheSameMoment_AreOrderedByPath_AndNewestFirstIsExactlyTheReverse()
    {
        foreach (var name in new[] { "c.jpg", "a.jpg", "b.jpg" })
            File.SetCreationTimeUtc(Touch(name), new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var oldest = InOrder(PhotoScanner.Scan(_root, recursive: false, Extensions, null, PhotoSortOrder.OldestFirst));
        var newest = InOrder(PhotoScanner.Scan(_root, recursive: false, Extensions, null, PhotoSortOrder.NewestFirst));

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], oldest);
        Assert.Equal(oldest.Reverse(), newest);
    }

    [Fact]
    public void SortingDoesNotChangeWhichPhotosAreListed()
    {
        MakeThreePhotos();
        Touch(Path.Combine("Liked", "a-newest.jpg"));
        Touch("notes.txt");

        foreach (var order in Enum.GetValues<PhotoSortOrder>())
        {
            var photos = PhotoScanner.Scan(_root, recursive: true, Extensions, Path.Combine(_root, "Liked"), order);

            Assert.Equal(["a-newest.jpg", "b-oldest.jpg", "c-middle.jpg"], Names(photos));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFolder_NoPhotos(string folder)
    {
        Assert.Empty(PhotoScanner.Scan(folder, recursive: true, Extensions, null));
    }
}
