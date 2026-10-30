using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>The photos a video can be made of besides the loaded ones: the copies in the liked folder or in one of its group folders.</summary>
public class VideoSourcesTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg", ".png"];

    private readonly string _likedDir = Path.Combine(Path.GetTempPath(), "PLVideoSourcesTests_" + Guid.NewGuid(), "Liked");

    public VideoSourcesTests() => Directory.CreateDirectory(_likedDir);

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_likedDir)!, recursive: true); } catch { }
    }

    private string Touch(string relativePath, string content = "x", DateTime? written = null)
    {
        var path = Path.Combine(_likedDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (written is { } time)
            File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    private static string[] Names(IEnumerable<PhotoFile> photos) => [.. photos.Select(p => Path.GetFileName(p.OriginalFilePath))];

    [Fact]
    public void LikedFolder_ListsItsOwnPhotos_NotThoseOfTheGroupFolders()
    {
        Touch("a.jpg");
        Touch("b.png");
        Touch("notes.txt");
        Touch(@"A - Group\c.jpg");

        Assert.Equal(["a.jpg", "b.png"], Names(VideoSources.Copies(_likedDir, Extensions)).Order());
    }

    [Fact]
    public void GroupFolder_ListsOnlyItsOwnPhotos()
    {
        Touch("liked.jpg");
        Touch(@"A - Group Family\a.jpg");
        Touch(@"A - Group Family\notes.txt");
        Touch(@"B - Group\b.jpg");

        Assert.Equal(["a.jpg"], Names(VideoSources.Copies(Path.Combine(_likedDir, "A - Group Family"), Extensions)));
    }

    [Fact]
    public void ExtensionsAreMatchedWithoutRegardToCase()
    {
        Touch("upper.JPG");
        Touch("other.gif");

        Assert.Equal(["upper.JPG"], Names(VideoSources.Copies(_likedDir, Extensions)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\this\folder\does\not\exist")]
    public void NoFolder_NoPhotos(string folder)
    {
        Assert.Empty(VideoSources.Copies(folder, Extensions));
    }

    [Fact]
    public void AVideoFileInTheFolder_IsNotAPhoto()
    {
        // The video is made in the folder of its photos
        Touch("a.jpg");
        Touch("timelapse_20261030_120000.mp4");

        Assert.Equal(["a.jpg"], Names(VideoSources.Copies(_likedDir, Extensions)));
    }

    [Fact]
    public void ACopyIsDatedByTheLastWriteTimeItKeepsFromItsPhoto()
    {
        // The copy was made today, its photo is from 2015
        var taken = new DateTime(2015, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        Touch("a.jpg", written: taken);

        var photo = Assert.Single(VideoSources.Copies(_likedDir, Extensions));

        Assert.Equal(taken, photo.CreationTimeUtc);
        Assert.Equal(taken, photo.LastWriteTimeUtc);
        Assert.Equal(1, photo.Length);
    }

    [Fact]
    public void OldestFirst_FollowsTheDatesOfThePhotos_NotTheOrderTheyWereLiked()
    {
        // Liked in the order new, old: made at about the same moment, so only the kept last-write times tell them apart
        Touch("new.jpg", written: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch("old.jpg", written: new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var sorted = VideoGenerationService.SortSourceFiles(VideoSources.Copies(_likedDir, Extensions), PhotoSortOrder.OldestFirst);

        Assert.Equal(["old.jpg", "new.jpg"], Names(sorted));
    }
}
