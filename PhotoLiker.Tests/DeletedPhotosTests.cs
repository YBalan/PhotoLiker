using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Deleting moves a photo into the Deleted folder of the photo folder, against a real temp file system.</summary>
public class DeletedPhotosTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLDeletedTests_" + Guid.NewGuid());

    public DeletedPhotosTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public void TheDeletedFolder_IsInsideThePhotoFolder()
    {
        Assert.Equal(Path.Combine(_root, "Deleted"), DeletedPhotos.FolderOf(_root));
    }

    [Fact]
    public void ThePhotoIsMovedNotCopied_AndKeepsItsContent()
    {
        var photo = Touch("a.jpg", "the picture");

        var moved = DeletedPhotos.Move(_root, photo);

        Assert.Equal(Path.Combine(_root, "Deleted", "a.jpg"), moved);
        Assert.False(File.Exists(photo));
        Assert.Equal("the picture", File.ReadAllText(moved));
    }

    [Fact]
    public void ThePathBelowThePhotoFolder_IsKept()
    {
        var photo = Touch(Path.Combine("314CANON", "IMG_0001.jpg"));

        var moved = DeletedPhotos.Move(_root, photo);

        Assert.Equal(Path.Combine(_root, "Deleted", "314CANON", "IMG_0001.jpg"), moved);
        Assert.True(File.Exists(moved));
    }

    [Fact]
    public void PhotosOfDifferentSubfoldersWithTheSameName_DoNotMeet()
    {
        var first = Touch(Path.Combine("314CANON", "IMG_0001.jpg"), "first");
        var second = Touch(Path.Combine("315CANON", "IMG_0001.jpg"), "second");

        var movedFirst = DeletedPhotos.Move(_root, first);
        var movedSecond = DeletedPhotos.Move(_root, second);

        Assert.Equal("first", File.ReadAllText(movedFirst));
        Assert.Equal("second", File.ReadAllText(movedSecond));
    }

    [Fact]
    public void APhotoDeletedTwice_DoesNotReplaceTheEarlierOne()
    {
        var movedFirst = DeletedPhotos.Move(_root, Touch("a.jpg", "first"));
        var movedSecond = DeletedPhotos.Move(_root, Touch("a.jpg", "second"));
        var movedThird = DeletedPhotos.Move(_root, Touch("a.jpg", "third"));

        Assert.Equal(Path.Combine(_root, "Deleted", "a.jpg"), movedFirst);
        Assert.Equal(Path.Combine(_root, "Deleted", "a (2).jpg"), movedSecond);
        Assert.Equal(Path.Combine(_root, "Deleted", "a (3).jpg"), movedThird);
        Assert.Equal(["first", "second", "third"], new[] { movedFirst, movedSecond, movedThird }.Select(File.ReadAllText));
    }

    [Fact]
    public void APhotoOutsideThePhotoFolder_GoesInByItsName()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "PLDeletedTests_other_" + Guid.NewGuid());
        Directory.CreateDirectory(elsewhere);
        try
        {
            var photo = Path.Combine(elsewhere, "far.jpg");
            File.WriteAllText(photo, "x");

            var moved = DeletedPhotos.Move(_root, photo);

            Assert.Equal(Path.Combine(_root, "Deleted", "far.jpg"), moved);
            Assert.False(File.Exists(photo));
        }
        finally
        {
            try { Directory.Delete(elsewhere, recursive: true); } catch { }
        }
    }

    [Fact]
    public void APhotoThatIsGone_FailsLoudly_AndLeavesNoDeletedFileBehind()
    {
        Assert.ThrowsAny<IOException>(() => DeletedPhotos.Move(_root, Path.Combine(_root, "missing.jpg")));

        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheScanner_DoesNotListWhatWasDeleted()
    {
        var photo = Touch(Path.Combine("sub", "a.jpg"));
        Touch("b.jpg");

        DeletedPhotos.Move(_root, photo);

        var listed = PhotoScanner.Scan(_root, recursive: true, [".jpg"], excludedFolder: null);
        Assert.Equal(["b.jpg"], listed.Select(p => Path.GetFileName(p.OriginalFilePath)));
    }
}
