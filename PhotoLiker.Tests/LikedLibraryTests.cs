using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Tests for LikedLibrary against a real temp file system.</summary>
public class LikedLibraryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PLLikedLibTests_" + Guid.NewGuid());
    private readonly string _likedDir;

    public LikedLibraryTests()
    {
        _likedDir = Path.Combine(_tempDir, "Liked");
        Directory.CreateDirectory(_likedDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>A photo on disk, described the way the folder scan describes it.</summary>
    private PhotoFile MakePhoto(string relativePath, string content = "photo")
    {
        var path = Path.Combine(_tempDir, "Photos", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        var info = new FileInfo(path);
        return new PhotoFile(path, string.Empty) { Length = info.Length, LastWriteTimeUtc = info.LastWriteTimeUtc };
    }

    private string LikedPath(string fileName) => Path.Combine(_likedDir, fileName);

    /// <summary>What the app does to like a photo.</summary>
    private string Like(PhotoFile photo)
    {
        var library = LikedLibrary.Scan(_likedDir);
        var destination = library.GetNewCopyPath(photo);
        LikedLibrary.CopyPhoto(photo.OriginalFilePath, destination);
        return destination;
    }

    // ── a photo that was turned has another size ────────────────────────────

    [Fact]
    public void RenameCopy_TheCopyGetsTheIdOfThePhotoAsItIsNow_AndIsStillItsCopy()
    {
        var photo = MakePhoto("IMG_0001.JPG", "photo");
        var copy = Like(photo);
        Assert.Equal(copy, LikedLibrary.Scan(_likedDir).Find(photo));

        photo.Length += 36; // the app added the orientation to it
        Assert.Null(LikedLibrary.Scan(_likedDir).Find(photo));

        var renamed = LikedLibrary.RenameCopy(copy, photo);

        Assert.Equal(LikedPath(CopyName.For(photo)), renamed);
        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(renamed));
        Assert.Equal(renamed, LikedLibrary.Scan(_likedDir).Find(photo));
    }

    [Fact]
    public void RenameCopy_ACopyMadeBeforeTheIds_GetsTheIdForm()
    {
        var photo = MakePhoto("IMG_0001.JPG", "photo");
        var oldStyle = LikedPath("IMG_0001.JPG");
        File.Copy(photo.OriginalFilePath, oldStyle);
        File.SetLastWriteTimeUtc(oldStyle, photo.LastWriteTimeUtc);

        photo.Length += 36;
        var renamed = LikedLibrary.RenameCopy(oldStyle, photo);

        Assert.Equal(LikedPath(CopyName.For(photo)), renamed);
        Assert.Equal(renamed, LikedLibrary.Scan(_likedDir).Find(photo));
    }

    [Fact]
    public void RenameCopy_ACopyThatHasTheNameAlready_IsLeftAlone()
    {
        var photo = MakePhoto("IMG_0001.JPG", "photo");
        var copy = Like(photo);

        Assert.Null(LikedLibrary.RenameCopy(copy, photo));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void RenameCopy_AnotherFileWithTheNewName_IsNotReplaced()
    {
        var photo = MakePhoto("IMG_0001.JPG", "photo");
        var copy = Like(photo);
        photo.Length += 36;
        var taken = LikedPath(CopyName.For(photo));
        File.WriteAllText(taken, "another");

        Assert.Null(LikedLibrary.RenameCopy(copy, photo));

        Assert.True(File.Exists(copy));
        Assert.Equal("another", File.ReadAllText(taken));
    }

    [Fact]
    public void TheIndexFollowsARenamedCopy()
    {
        var photo = MakePhoto("IMG_0001.JPG", "photo");
        var copy = Like(photo);
        var library = LikedLibrary.Scan(_likedDir);
        Assert.Equal(copy, library.Find(photo));

        photo.Length += 36;
        var renamed = LikedLibrary.RenameCopy(copy, photo)!;
        library.Remove(copy);
        library.Add(renamed, photo);

        Assert.Equal(renamed, library.Find(photo));
    }

    // ── new copies ──────────────────────────────────────────────────────────

    [Fact]
    public void GetNewCopyPath_IsInTheLikedFolderAndNamedWithTheId()
    {
        var photo = MakePhoto(Path.Combine("100CANON", "IMG_0001.JPG"));

        var path = LikedLibrary.Scan(_likedDir).GetNewCopyPath(photo);

        Assert.Equal(LikedPath(CopyName.For(photo)), path);
    }

    [Fact]
    public void GetNewCopyPath_DoesNotDependOnWhatTheFolderHolds()
    {
        var first = MakePhoto(Path.Combine("a", "IMG.jpg"), "one");
        var second = MakePhoto(Path.Combine("b", "IMG.jpg"), "another one");
        var alone = LikedLibrary.Scan(_likedDir).GetNewCopyPath(second);

        Like(first);

        Assert.Equal(alone, LikedLibrary.Scan(_likedDir).GetNewCopyPath(second));
    }

    [Fact]
    public void GetNewCopyPath_PhotosWithTheSameName_GetDifferentPaths()
    {
        var first = MakePhoto(Path.Combine("a", "IMG.jpg"), "one");
        var second = MakePhoto(Path.Combine("b", "IMG.jpg"), "another one");
        var library = LikedLibrary.Scan(_likedDir);

        Assert.NotEqual(library.GetNewCopyPath(first), library.GetNewCopyPath(second));
    }

    // ── finding the copy of a photo ─────────────────────────────────────────

    [Fact]
    public void Find_AfterLiking_ReturnsTheCopy()
    {
        var photo = MakePhoto("IMG.jpg");
        var copy = Like(photo);

        Assert.Equal(copy, LikedLibrary.Scan(_likedDir).Find(photo));
    }

    [Fact]
    public void Find_NothingLiked_ReturnsNull()
    {
        var photo = MakePhoto("IMG.jpg");

        Assert.Null(LikedLibrary.Scan(_likedDir).Find(photo));
    }

    [Fact]
    public void Find_LikedFolderMissing_ReturnsNull()
    {
        var photo = MakePhoto("IMG.jpg");

        Assert.Null(LikedLibrary.Scan(Path.Combine(_tempDir, "nothing here")).Find(photo));
        Assert.Null(LikedLibrary.Scan(string.Empty).Find(photo));
    }

    [Fact]
    public void Find_PhotoOfTheSameNameInAnotherFolder_IsNotTheLikedOne()
    {
        // Only one IMG.jpg is open, so nothing about the name tells it from the liked one
        var liked = MakePhoto(Path.Combine("a", "IMG.jpg"), "taken at the lake");
        var other = MakePhoto(Path.Combine("b", "IMG.jpg"), "taken at home, another size");
        var copy = Like(liked);

        var library = LikedLibrary.Scan(_likedDir);

        Assert.Equal(copy, library.Find(liked));
        Assert.Null(library.Find(other));
    }

    [Fact]
    public void Find_PhotosWithTheSameName_EachFindsItsOwnCopy()
    {
        var first = MakePhoto(Path.Combine("a", "IMG.jpg"), "one");
        var second = MakePhoto(Path.Combine("b", "IMG.jpg"), "another one");
        var firstCopy = Like(first);
        var secondCopy = Like(second);

        var library = LikedLibrary.Scan(_likedDir);

        Assert.Equal(firstCopy, library.Find(first));
        Assert.Equal(secondCopy, library.Find(second));
    }

    [Fact]
    public void Find_TheCopyWasLikedFromAnotherPlace_StillBelongsToThePhoto()
    {
        // The same photo on another drive or computer: other path, same size and time
        var photo = MakePhoto(Path.Combine("a", "IMG.jpg"));
        var copy = Like(photo);
        var elsewhere = new PhotoFile(@"Z:\backup\2015\IMG.jpg", string.Empty) { Length = photo.Length, LastWriteTimeUtc = photo.LastWriteTimeUtc };

        Assert.Equal(copy, LikedLibrary.Scan(_likedDir).Find(elsewhere));
    }

    [Fact]
    public void Find_TheCopyLostItsTime_IsFoundByTheId()
    {
        // A sync tool or a copy through another program gave the copy the time of the copying
        var photo = MakePhoto("IMG.jpg");
        var copy = Like(photo);
        File.SetLastWriteTimeUtc(copy, photo.LastWriteTimeUtc.AddDays(400));

        Assert.Equal(copy, LikedLibrary.Scan(_likedDir).Find(photo));
    }

    [Fact]
    public void Find_TheDatesOfPhotoAndCopyWereChangedTogether_IsFoundByTheTimes()
    {
        // Aligning the file dates changes the time of the photo and of its copy, and the id in the name is the old one
        var photo = MakePhoto("IMG.jpg");
        var copy = Like(photo);
        var aligned = photo.LastWriteTimeUtc.AddYears(-6);
        File.SetLastWriteTimeUtc(copy, aligned);
        var alignedPhoto = new PhotoFile(photo.OriginalFilePath, string.Empty) { Length = photo.Length, LastWriteTimeUtc = aligned };

        Assert.Equal(copy, LikedLibrary.Scan(_likedDir).Find(alignedPhoto));
    }

    // ── copies made before the ids ──────────────────────────────────────────

    [Fact]
    public void Find_ACopyNamedLikeThePhoto_IsFoundBySizeAndTime()
    {
        var photo = MakePhoto("IMG.jpg");
        File.Copy(photo.OriginalFilePath, LikedPath("IMG.jpg"));

        Assert.Equal(LikedPath("IMG.jpg"), LikedLibrary.Scan(_likedDir).Find(photo));
    }

    [Fact]
    public void Find_ACopyNamedLikeAnotherPhoto_IsNotFoundForThisOne()
    {
        // An old plain "IMG.jpg" in the liked folder is the copy of the photo it is identical to and of no other
        var liked = MakePhoto(Path.Combine("a", "IMG.jpg"), "taken at the lake");
        var other = MakePhoto(Path.Combine("b", "IMG.jpg"), "taken at home, another size");
        File.Copy(liked.OriginalFilePath, LikedPath("IMG.jpg"));

        var library = LikedLibrary.Scan(_likedDir);

        Assert.Equal(LikedPath("IMG.jpg"), library.Find(liked));
        Assert.Null(library.Find(other));
    }

    [Fact]
    public void Find_ASecondCopyMadeBeforeTheIds_IsFoundForItsPhoto()
    {
        var first = MakePhoto(Path.Combine("a", "IMG.jpg"), "one");
        var second = MakePhoto(Path.Combine("b", "IMG.jpg"), "another one");
        File.Copy(first.OriginalFilePath, LikedPath("IMG.jpg"));
        File.Copy(second.OriginalFilePath, LikedPath("IMG (2).jpg"));

        var library = LikedLibrary.Scan(_likedDir);

        Assert.Equal(LikedPath("IMG.jpg"), library.Find(first));
        Assert.Equal(LikedPath("IMG (2).jpg"), library.Find(second));
    }

    [Fact]
    public void Find_APhotoNamedLikeACopyWithAnIdOrANumber_IsFoundByItsOwnName()
    {
        // The photo is called "IMG (2).jpg" (Windows made that name) or "IMG [1bbfa943].jpg"
        var numbered = MakePhoto(Path.Combine("a", "IMG (2).jpg"), "one");
        var bracketed = MakePhoto(Path.Combine("b", "IMG [1bbfa943].jpg"), "another one");
        File.Copy(numbered.OriginalFilePath, LikedPath("IMG (2).jpg"));
        File.Copy(bracketed.OriginalFilePath, LikedPath("IMG [1bbfa943].jpg"));

        var library = LikedLibrary.Scan(_likedDir);

        Assert.Equal(LikedPath("IMG (2).jpg"), library.Find(numbered));
        Assert.Equal(LikedPath("IMG [1bbfa943].jpg"), library.Find(bracketed));
    }

    [Fact]
    public void Find_NameDifferingOnlyInCase_IsTheSameName()
    {
        var photo = MakePhoto("IMG.JPG");
        File.Copy(photo.OriginalFilePath, LikedPath("img.jpg"));

        Assert.NotNull(LikedLibrary.Scan(_likedDir).Find(photo));
    }

    // ── apply, add, remove ──────────────────────────────────────────────────

    [Fact]
    public void Apply_SetsTheLikedPathOfEveryPhoto()
    {
        var liked = MakePhoto(Path.Combine("a", "IMG.jpg"), "one");
        var other = MakePhoto(Path.Combine("b", "IMG.jpg"), "another one");
        var copy = Like(liked);

        LikedLibrary.Scan(_likedDir).Apply([liked, other]);

        Assert.Equal(copy, liked.LikedFilePath);
        Assert.Equal(string.Empty, other.LikedFilePath);
    }

    [Fact]
    public void Add_MakesThePhotoLiked_Remove_TakesItBack()
    {
        var photo = MakePhoto("IMG.jpg");
        var library = LikedLibrary.Scan(_likedDir);
        var path = library.GetNewCopyPath(photo);

        library.Add(path, photo);
        Assert.Equal(path, library.Find(photo));

        library.Remove(path);
        Assert.Null(library.Find(photo));
    }

    [Fact]
    public void Remove_ACopyOfAnotherPhotoWithTheSameName_StaysLiked()
    {
        var first = MakePhoto(Path.Combine("a", "IMG.jpg"), "one");
        var second = MakePhoto(Path.Combine("b", "IMG.jpg"), "another one");
        Like(first);
        var secondCopy = Like(second);
        var library = LikedLibrary.Scan(_likedDir);

        library.Remove(secondCopy);

        Assert.NotNull(library.Find(first));
        Assert.Null(library.Find(second));
    }

    [Fact]
    public void Remove_ACopyMadeBeforeTheIds_TakesItBack()
    {
        var photo = MakePhoto("IMG.jpg");
        File.Copy(photo.OriginalFilePath, LikedPath("IMG (2).jpg"));
        var library = LikedLibrary.Scan(_likedDir);

        library.Remove(LikedPath("IMG (2).jpg"));

        Assert.Null(library.Find(photo));
    }

    [Fact]
    public void Remove_SomethingThatIsNotThere_ChangesNothing()
    {
        var photo = MakePhoto("IMG.jpg");
        var copy = Like(photo);
        var library = LikedLibrary.Scan(_likedDir);

        library.Remove(LikedPath("nothing.jpg"));

        Assert.Equal(copy, library.Find(photo));
    }

    // ── CopyPhoto ───────────────────────────────────────────────────────────

    [Fact]
    public void CopyPhoto_KeepsTheTimeOfThePhoto()
    {
        var photo = MakePhoto("IMG.jpg");
        var taken = new DateTime(2012, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(photo.OriginalFilePath, taken);

        LikedLibrary.CopyPhoto(photo.OriginalFilePath, LikedPath("copy.jpg"));

        Assert.Equal(taken, File.GetLastWriteTimeUtc(LikedPath("copy.jpg")));
    }
}
