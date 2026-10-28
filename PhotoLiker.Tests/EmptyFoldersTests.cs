using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class EmptyFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLEmptyFolders_" + Guid.NewGuid());

    public EmptyFoldersTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch { /* best effort */ }
    }

    private string Folder(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Write(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Thumbs.db the way Windows makes it: hidden and a system file.</summary>
    private string Thumbs(string relativeFolder, string name = "Thumbs.db")
    {
        var path = Write(Path.Combine(relativeFolder, name));
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
        return path;
    }

    private IReadOnlyList<string> Find(params string[] leave) =>
        [.. EmptyFolders.Find(_root, leave, null, CancellationToken.None).Select(f => Path.GetRelativePath(_root, f.Path))];

    [Fact]
    public void AFolderWithNothingInIt_IsFound()
    {
        Folder("empty");

        Assert.Equal(["empty"], Find());
    }

    [Fact]
    public void AFolderWithOnlyThumbnailFiles_IsFound_AlsoWhenTheyAreHiddenSystemFiles()
    {
        Thumbs("a");
        Thumbs("b", "desktop.ini");
        Write("c/.DS_Store");
        Write("c/._IMG_0001.jpg");

        Assert.Equal(["a", "b", "c"], Find().Order());
        var a = EmptyFolders.Find(_root, [], null, CancellationToken.None).Single(f => f.Path.EndsWith('a'));
        Assert.Equal(["Thumbs.db"], a.Junk);
    }

    [Fact]
    public void AFolderWithAnythingElse_Stays_AndSoDoTheFoldersAboveIt()
    {
        Write("holiday/2009/IMG_0001.jpg");
        Thumbs("holiday/2009");
        Write("notes/readme.txt");

        Assert.Empty(Find());
    }

    [Fact]
    public void FoldersInsideFolders_AreListedDeepestFirst_UntilSomethingStopsThem()
    {
        Thumbs("a/b/c");
        Write("keep/photo.jpg");
        Folder("keep/empty inside");

        var found = EmptyFolders.Find(_root, [], null, CancellationToken.None);

        Assert.Equal(["a\\b\\c", "a\\b", "a", "keep\\empty inside"], found.Select(f => Path.GetRelativePath(_root, f.Path)).ToList());
        Assert.Equal([0, 1, 1, 0], found.Select(f => f.Subfolders));
    }

    [Fact]
    public void TheRootItself_IsNeverInTheList()
    {
        Assert.Empty(Find());
        Thumbs(".");
        Assert.Empty(Find());
    }

    [Fact]
    public void FoldersTheAppKeeps_AreLeftAlone_AndSoAreTheFoldersThatHoldThem()
    {
        var deleted = Folder("Deleted");
        var liked = Folder("sub/Liked");
        Folder("elsewhere");

        Assert.Equal(["elsewhere"], Find(deleted, liked));
        Assert.Equal(["Deleted", "elsewhere", "sub", "sub\\Liked"], Find().Order(StringComparer.Ordinal)); // with nothing to leave, they are folders like any
    }

    [Fact]
    public void FoldersOfTheSystemAndTheNas_AreLeftAlone()
    {
        Folder("@eaDir");
        Folder("photos/#recycle");

        Assert.Empty(Find());
    }

    [Fact]
    public void ALinkToAnotherFolder_IsNotFollowedNorRemoved()
    {
        var target = Path.Combine(Path.GetTempPath(), "PLEmptyFoldersTarget_" + Guid.NewGuid());
        Directory.CreateDirectory(target);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "link"), target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // making a link needs a privilege this machine does not give
            }

            Assert.Empty(Find());
            Assert.True(Directory.Exists(target));
        }
        finally
        {
            Directory.Delete(target);
        }
    }

    [Fact]
    public void RemovingFolders_DeletesTheThumbnailFilesWithThem_AndNothingElse()
    {
        Thumbs("a/b");
        Folder("c");
        var photo = Write("keep/IMG_0001.jpg");
        var found = EmptyFolders.Find(_root, [], null, CancellationToken.None);

        var result = EmptyFolders.Remove([.. found.Select(f => f.Path)], null, CancellationToken.None);

        Assert.Equal(3, result.Removed.Count);
        Assert.Equal(1, result.JunkFiles);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(Path.Combine(_root, "a")));
        Assert.False(Directory.Exists(Path.Combine(_root, "c")));
        Assert.True(File.Exists(photo));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void AFolderThatGotAPhotoSinceItWasFound_StaysWithEverythingInIt()
    {
        var thumbs = Thumbs("a");
        var found = EmptyFolders.Find(_root, [], null, CancellationToken.None);
        var photo = Write("a/new.jpg");

        var result = EmptyFolders.Remove([.. found.Select(f => f.Path)], null, CancellationToken.None);

        Assert.Empty(result.Removed);
        Assert.Contains("new.jpg", Assert.Single(result.Failures).Message);
        Assert.True(File.Exists(photo));
        Assert.True(File.Exists(thumbs)); // nothing was deleted from a folder that stays
    }

    [Fact]
    public void AFolderWhoseSubfolderStays_StaysToo_AndIsReported()
    {
        Folder("a/b");
        var found = EmptyFolders.Find(_root, [], null, CancellationToken.None);

        // The user took "a/b" out of the run, "a" is still asked for
        var result = EmptyFolders.Remove([found.Single(f => f.Path.EndsWith("a")).Path], null, CancellationToken.None);

        Assert.Empty(result.Removed);
        Assert.Contains("still holds the folder", Assert.Single(result.Failures).Message);
        Assert.True(Directory.Exists(Path.Combine(_root, "a", "b")));
    }

    [Fact]
    public void AReadOnlyFolderWithAReadOnlyThumbsFile_IsRemoved()
    {
        var thumbs = Thumbs("a");
        File.SetAttributes(thumbs, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(_root, "a"), FileAttributes.Directory | FileAttributes.ReadOnly);

        var result = EmptyFolders.Remove([Path.Combine(_root, "a")], null, CancellationToken.None);

        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(Path.Combine(_root, "a")));
    }

    [Fact]
    public void AStopComesBetweenTwoFolders_AndFindingCanBeStoppedToo()
    {
        Folder("a");
        Folder("b");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var result = EmptyFolders.Remove([Path.Combine(_root, "a"), Path.Combine(_root, "b")], null, stop.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(result.Removed);
        Assert.True(Directory.Exists(Path.Combine(_root, "a")));
        Assert.Throws<OperationCanceledException>(() => EmptyFolders.Find(_root, [], null, stop.Token));
    }

    [Theory]
    [InlineData("Thumbs.db", true)]
    [InlineData("THUMBS.DB", true)]
    [InlineData("ehthumbs.db", true)]
    [InlineData("desktop.ini", true)]
    [InlineData(".DS_Store", true)]
    [InlineData("._IMG_0001.jpg", true)]
    [InlineData("IMG_0001.jpg", false)]
    [InlineData("Thumbs.db.bak", false)]
    [InlineData("photo-liker-config.json", false)]
    [InlineData("notes.txt", false)]
    public void OnlyThumbnailCachesAndTheLike_AreJunk(string name, bool junk)
    {
        Assert.Equal(junk, EmptyFolders.IsJunk(name));
    }
}

public class UnreadablePhotosTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PLUnreadable_" + Guid.NewGuid());

    public UnreadablePhotosTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Make(string relative, string content = "")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static readonly string[] Photos = [".jpg", ".png"];

    [Fact]
    public void AnEmptyPhoto_IsFoundOnTheWalkOfTheFolders_AndItsFolderStays()
    {
        var empty = Make(@"Broken\IMG_0001.JPG");
        Make(@"Broken\IMG_0002.JPG", "a real photo");
        Make(@"Hollow\IMG_0003.png");

        var scan = EmptyFolders.Scan(_root, [], Photos, null, CancellationToken.None);

        Assert.Equal([empty, Path.Combine(_root, "Hollow", "IMG_0003.png")], scan.EmptyPhotos.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(scan.Folders); // a folder that holds an empty photo is not empty: the photo is not junk
    }

    [Fact]
    public void OnlyEmptyFilesThatAreNamedLikePhotos_AreListed()
    {
        Make(@"a\notes.txt");              // empty, but no photo
        Make(@"a\Thumbs.db");              // junk
        Make(@"a\._IMG_0001.jpg");         // a macOS stub: junk, not a photo
        Make(@"a\movie.mp4");
        var photo = Make(@"a\p.jpg");

        var scan = EmptyFolders.Scan(_root, [], Photos, null, CancellationToken.None);

        Assert.Equal([photo], scan.EmptyPhotos);
    }

    [Fact]
    public void WithoutExtensions_NoPhotoIsLookedFor_AndFindIsTheSameAsBefore()
    {
        Make(@"Broken\IMG_0001.jpg");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        Assert.Empty(EmptyFolders.Scan(_root, [], [], null, CancellationToken.None).EmptyPhotos);
        Assert.Equal([Path.Combine(_root, "empty")], EmptyFolders.Find(_root, [], null, CancellationToken.None).Select(f => f.Path));
    }

    [Fact]
    public void UnreadablePhotos_AreMovedToDeletedUnreadable_WithTheirPath_AndNothingIsErased()
    {
        var broken = Make(@"145_1204\IMG_2117.JPG", "cut off");

        var result = UnreadablePhotos.MoveAway(_root, [broken], null, CancellationToken.None);

        Assert.Equal([broken], result.Moved);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(broken));
        Assert.Equal("cut off", File.ReadAllText(Path.Combine(_root, "Deleted", "Unreadable", "145_1204", "IMG_2117.JPG")));
    }

    [Fact]
    public void APhotoThatIsGone_IsAFailure_AndTheOthersAreStillMoved()
    {
        var gone = Path.Combine(_root, "gone.jpg");
        var there = Make("there.jpg", "x");

        var result = UnreadablePhotos.MoveAway(_root, [gone, there], null, CancellationToken.None);

        Assert.Equal([there], result.Moved);
        Assert.Equal(gone, Assert.Single(result.Failures).Path);
    }

    [Fact]
    public void ASecondPhotoWithTheSameNameAndPlace_GetsANumber_NotOverwritten()
    {
        var first = Make(@"a\IMG_1.jpg", "one");
        UnreadablePhotos.MoveAway(_root, [first], null, CancellationToken.None);
        var second = Make(@"a\IMG_1.jpg", "two");

        UnreadablePhotos.MoveAway(_root, [second], null, CancellationToken.None);

        Assert.Equal("one", File.ReadAllText(Path.Combine(_root, "Deleted", "Unreadable", "a", "IMG_1.jpg")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(_root, "Deleted", "Unreadable", "a", "IMG_1 (2).jpg")));
    }

    [Fact]
    public void AStopComesBetweenTwoPhotos()
    {
        var photo = Make("a.jpg", "x");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var result = UnreadablePhotos.MoveAway(_root, [photo], null, stop.Token);

        Assert.True(result.Cancelled);
        Assert.Empty(result.Moved);
        Assert.True(File.Exists(photo));
    }
}
