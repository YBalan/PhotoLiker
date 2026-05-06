using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>
/// Tests for PhotoBrowserService using a real (temp) file system.
/// A minimal JPEG stub is created so LoadImage can open the file.
/// </summary>
public class PhotoBrowserServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _likedDir;
    private readonly string[] _photos;

    public PhotoBrowserServiceTests()
    {
        _tempDir  = Path.Combine(Path.GetTempPath(), "PLSvcTests_" + Guid.NewGuid());
        _likedDir = Path.Combine(_tempDir, "Liked");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_likedDir);

        // Create minimal valid JPEG stubs (enough to pass File.Exists; metadata will fail gracefully)
        _photos = new[] { "photo1.jpg", "photo2.jpg", "photo3.jpg" }
            .Select(name =>
            {
                var path = Path.Combine(_tempDir, name);
                // Minimal JPEG: SOI + EOI markers
                File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
                return path;
            })
            .ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private PhotoBrowserService CreateService()
    {
        var svc = new PhotoBrowserService();
        svc.Settings.LikedFolder = _likedDir;
        return svc;
    }

    // ── OpenFolder ──────────────────────────────────────────────────────────

    [Fact]
    public void OpenFolder_LoadsAllJpegFiles()
    {
        var svc = CreateService();

        svc.OpenFolder(_tempDir);

        Assert.Equal(3, svc.Settings.Files.Count);
    }

    [Fact]
    public void OpenFolder_SetsCurrentFolder()
    {
        var svc = CreateService();

        svc.OpenFolder(_tempDir);

        Assert.Equal(_tempDir, svc.Settings.CurrentFolder);
    }

    [Fact]
    public void OpenFolder_SetsDefaultLikedFolder_WhenNotConfigured()
    {
        var svc = new PhotoBrowserService();

        svc.OpenFolder(_tempDir);

        Assert.Equal(
            Path.Combine(_tempDir, AppStrings.DefaultLikedFolderName),
            svc.Settings.LikedFolder);
    }

    [Fact]
    public void OpenFolder_FiresFolderLoadedEvent()
    {
        var svc = CreateService();
        bool fired = false;
        svc.FolderLoaded += () => fired = true;

        svc.OpenFolder(_tempDir);

        Assert.True(fired);
    }

    [Fact]
    public void OpenFolder_StatusMessage_ContainsPhotoCount()
    {
        var svc = CreateService();
        string? status = null;
        svc.StatusChanged += s => status = s;

        svc.OpenFolder(_tempDir);

        Assert.NotNull(status);
        Assert.Contains("3", status);
    }

    [Fact]
    public void OpenFolder_ExcludesNonImageFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "hello");
        var svc = CreateService();

        svc.OpenFolder(_tempDir);

        Assert.Equal(3, svc.Settings.Files.Count);
    }

    [Fact]
    public void OpenFolder_WithSubFolders_LoadsRecursively()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "deep.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        // Persist GoThroughSubFolders=true so Settings.Load picks it up
        new Settings { CurrentFolder = _tempDir, GoThroughSubFolders = true }.Save(_tempDir);

        var svc = CreateService();
        svc.OpenFolder(_tempDir);

        Assert.Equal(4, svc.Settings.Files.Count);
    }

    [Fact]
    public void OpenFolder_WithoutSubFolders_IgnoresSubDirectoryFiles()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "deep.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);

        var svc = CreateService();
        svc.Settings.GoThroughSubFolders = false;

        svc.OpenFolder(_tempDir);

        Assert.Equal(3, svc.Settings.Files.Count);
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    [Fact]
    public void GoNext_AdvancesIndex()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        int firstIdx = svc.Settings.CurrentIndex;

        svc.GoNext();

        Assert.Equal(firstIdx + 1, svc.Settings.CurrentIndex);
    }

    [Fact]
    public void GoPrevious_DecrementsIndex()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.GoNext();
        int idxAfterNext = svc.Settings.CurrentIndex;

        svc.GoPrevious();

        Assert.Equal(idxAfterNext - 1, svc.Settings.CurrentIndex);
    }

    [Fact]
    public void GoNext_AtLastPhoto_DoesNotExceedBounds()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        for (int i = 0; i < 10; i++) svc.GoNext(); // try to go past the end

        Assert.Equal(2, svc.Settings.CurrentIndex);    // 3 files, max index = 2
    }

    [Fact]
    public void GoPrevious_AtFirstPhoto_DoesNotGoBelowZero()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        for (int i = 0; i < 10; i++) svc.GoPrevious();

        Assert.Equal(0, svc.Settings.CurrentIndex);
    }

    [Fact]
    public void GoNext_FiresImageLoadedEvent()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        bool fired = false;
        svc.ImageLoaded += (_, _) => fired = true;

        svc.GoNext();

        Assert.True(fired);
    }

    // ── Like / Unlike ────────────────────────────────────────────────────────

    [Fact]
    public void LikePhoto_CopiesFileToLikedFolder()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);

        svc.LikePhoto(_photos[0]);

        Assert.True(File.Exists(Path.Combine(_likedDir, Path.GetFileName(_photos[0]))));
    }

    [Fact]
    public void LikePhoto_SetsLikedFileOnPhotoFile()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);

        svc.LikePhoto(_photos[0]);

        var pf = svc.Settings.Files.First(f => f.OriginalFilePath == _photos[0]);
        Assert.NotEmpty(pf.LikedFilePath);
    }

    [Fact]
    public void LikePhoto_SetsLikedFileInSettings()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LoadImage(_photos[0]);

        svc.LikePhoto(_photos[0]);

        Assert.NotEmpty(svc.Settings.LikedFile);
    }

    [Fact]
    public void LikePhoto_StatusMessage_ContainsFileName()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        string? status = null;
        svc.StatusChanged += s => status = s;

        svc.LikePhoto(_photos[0]);

        Assert.NotNull(status);
        Assert.Contains(Path.GetFileName(_photos[0]), status);
    }

    [Fact]
    public void UnlikePhoto_DeletesFileFromLikedFolder()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LikePhoto(_photos[0]);
        svc.LoadImage(_photos[0]);  // sets Settings.LikedFile

        svc.UnlikePhoto(_photos[0]);

        Assert.False(File.Exists(Path.Combine(_likedDir, Path.GetFileName(_photos[0]))));
    }

    [Fact]
    public void UnlikePhoto_ClearsLikedFileInSettings()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LikePhoto(_photos[0]);
        svc.LoadImage(_photos[0]);

        svc.UnlikePhoto(_photos[0]);

        Assert.Empty(svc.Settings.LikedFile);
    }

    [Fact]
    public void ToggleLike_LikesThenUnlikes()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LoadImage(_photos[0]);

        svc.ToggleLike();
        bool likedAfterFirst = svc.IsCurrentLiked;

        svc.ToggleLike();
        bool likedAfterSecond = svc.IsCurrentLiked;

        Assert.True(likedAfterFirst);
        Assert.False(likedAfterSecond);
    }

    [Fact]
    public void ToggleLike_WithNoCurrentFile_DoesNotThrow()
    {
        var svc = CreateService();  // no folder opened

        var ex = Record.Exception(() => svc.ToggleLike());

        Assert.Null(ex);
    }

    [Fact]
    public void IsCurrentLiked_WhenNotLiked_ReturnsFalse()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LoadImage(_photos[0]);

        Assert.False(svc.IsCurrentLiked);
    }

    [Fact]
    public void IsCurrentLiked_AfterLike_ReturnsTrue()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LikePhoto(_photos[0]);
        svc.LoadImage(_photos[0]);

        Assert.True(svc.IsCurrentLiked);
    }

    // ── LikePhoto edge cases ─────────────────────────────────────────────────

    [Fact]
    public void LikePhoto_AlreadyLiked_UnlikesInstead()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        svc.LikePhoto(_photos[0]);
        svc.LoadImage(_photos[0]);

        // second call when file already exists in liked folder → acts as unlike
        svc.LikePhoto(_photos[0]);

        Assert.False(File.Exists(Path.Combine(_likedDir, Path.GetFileName(_photos[0]))));
    }

    [Fact]
    public void LikePhoto_WithEmptyLikedFolder_DoesNothing()
    {
        var svc = new PhotoBrowserService();
        svc.OpenFolder(_tempDir);
        svc.Settings.LikedFolder = string.Empty; // explicitly clear

        var ex = Record.Exception(() => svc.LikePhoto(_photos[0]));

        Assert.Null(ex);
    }

    // ── LoadImage ────────────────────────────────────────────────────────────

    [Fact]
    public void LoadImage_SetsCurrentFilePathAndName()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);

        svc.LoadImage(_photos[1]);

        Assert.Equal(_photos[1], svc.Settings.CurrentFilePath);
        Assert.Equal(Path.GetFileName(_photos[1]), svc.Settings.CurrentFileName);
    }

    [Fact]
    public void LoadImage_SetsCurrentIndex()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);

        svc.LoadImage(_photos[2]);

        Assert.Equal(2, svc.Settings.CurrentIndex);
    }

    [Fact]
    public void LoadImage_FiresImageLoadedEvent()
    {
        var svc = CreateService();
        svc.OpenFolder(_tempDir);
        bool fired = false;
        svc.ImageLoaded += (_, _) => fired = true;

        svc.LoadImage(_photos[0]);

        Assert.True(fired);
    }
}
