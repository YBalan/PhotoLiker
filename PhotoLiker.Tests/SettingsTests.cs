using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PhotoLikerTests_" + Guid.NewGuid());

    public SettingsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Load ────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NonExistentFile_ReturnsDefaultSettings()
    {
        var settings = Settings.Load(_tempDir);

        Assert.Equal(_tempDir, settings.CurrentFolder);
        Assert.Equal(string.Empty, settings.LikedFolder);
        Assert.Equal(20, settings.CacheSize);
        Assert.Equal(110, settings.ThumbnailSize);
        Assert.False(settings.IsDarkTheme);
        Assert.False(settings.GoThroughSubFolders);
        Assert.NotEmpty(settings.Extensions);
    }

    [Fact]
    public void Load_ValidJson_RoundTrips()
    {
        var original = new Settings
        {
            CurrentFolder = _tempDir,
            LikedFolder   = Path.Combine(_tempDir, "Liked"),
            CacheSize     = 50,
            ThumbnailSize = 200,
            IsDarkTheme   = true,
            GoThroughSubFolders = true,
            Extensions    = [".jpg", ".png"],
        };

        original.Save(_tempDir);
        var loaded = Settings.Load(_tempDir);

        Assert.Equal(original.LikedFolder, loaded.LikedFolder);
        Assert.Equal(50, loaded.CacheSize);
        Assert.Equal(200, loaded.ThumbnailSize);
        Assert.True(loaded.IsDarkTheme);
        Assert.True(loaded.GoThroughSubFolders);
        Assert.Equal([".jpg", ".png"], loaded.Extensions);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultSettings()
    {
        var path = Path.Combine(_tempDir, "photo-liker-config.json");
        File.WriteAllText(path, "NOT_VALID_JSON{{{{");

        var settings = Settings.Load(_tempDir);

        Assert.Equal(_tempDir, settings.CurrentFolder);
    }

    // ── JsonIgnore fields ───────────────────────────────────────────────────

    [Fact]
    public void Save_JsonIgnoreFields_AreNotPersisted()
    {
        var settings = new Settings
        {
            CurrentFolder   = _tempDir,
            CurrentFilePath = @"C:\fake\photo.jpg",
            CurrentFileName = "photo.jpg",
            CurrentIndex    = 7,
            LikedFile       = @"C:\fake\Liked\photo.jpg",
        };
        settings.Files.Add(new PhotoFile(@"C:\fake\photo.jpg", string.Empty));

        settings.Save(_tempDir);
        var loaded = Settings.Load(_tempDir);

        Assert.Equal(string.Empty, loaded.CurrentFilePath);
        Assert.Equal(string.Empty, loaded.CurrentFileName);
        Assert.Equal(0, loaded.CurrentIndex);
        Assert.Equal(string.Empty, loaded.LikedFile);
        Assert.Empty(loaded.Files);
    }

    // ── Save ────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_CreatesFile()
    {
        var settings = new Settings { CurrentFolder = _tempDir };

        settings.Save();

        Assert.True(File.Exists(Path.Combine(_tempDir, "photo-liker-config.json")));
    }

    [Fact]
    public void Save_WithExplicitFolder_UsesGivenFolder()
    {
        var sub = Path.Combine(_tempDir, "sub");
        var settings = new Settings { CurrentFolder = _tempDir };

        settings.Save(sub);

        Assert.True(File.Exists(Path.Combine(sub, "photo-liker-config.json")));
    }

    // ── Default extensions ──────────────────────────────────────────────────

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".tiff")]
    [InlineData(".webp")]
    public void DefaultExtensions_ContainsCommonImageFormats(string ext)
    {
        var settings = new Settings();

        Assert.Contains(ext, settings.Extensions);
    }
}
