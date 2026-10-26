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

    [Fact]
    public void Load_RelativePathsFromTheWinFormsApp_AreResolvedAgainstTheFolder()
    {
        File.WriteAllText(Path.Combine(_tempDir, "photo-liker-config.json"),
            """{ "CurrentFolder": ".", "LikedFolder": "Favourites" }""");

        var settings = Settings.Load(_tempDir);

        Assert.Equal(Path.GetFullPath(_tempDir), settings.CurrentFolder);
        Assert.Equal(Path.Combine(Path.GetFullPath(_tempDir), "Favourites"), settings.LikedFolder);
    }

    [Fact]
    public void Load_AbsolutePaths_AreLeftAlone()
    {
        var liked = Path.Combine(Path.GetTempPath(), "SomewhereElse", "Liked");
        var json = System.Text.Json.JsonSerializer.Serialize(new { CurrentFolder = _tempDir, LikedFolder = liked });
        File.WriteAllText(Path.Combine(_tempDir, "photo-liker-config.json"), json);

        var settings = Settings.Load(_tempDir);

        Assert.Equal(_tempDir, settings.CurrentFolder);
        Assert.Equal(liked, settings.LikedFolder);
    }

    // ── What is persisted ───────────────────────────────────────────────────

    [Fact]
    public void Save_RuntimeOnlyFields_AreNotPersisted()
    {
        var settings = new Settings
        {
            CurrentFolder   = _tempDir,
            CurrentFileName = "photo.jpg",
            CurrentIndex    = 7,
            LikedFile       = @"C:\fake\Liked\photo.jpg",
            SavedInFolders  = ["Liked", "A - Group"],
        };
        settings.Files.Add(new PhotoFile(@"C:\fake\photo.jpg", string.Empty));

        settings.Save(_tempDir);
        var loaded = Settings.Load(_tempDir);

        Assert.Equal(string.Empty, loaded.CurrentFileName);
        Assert.Equal(0, loaded.CurrentIndex);
        Assert.Equal(string.Empty, loaded.LikedFile);
        Assert.Empty(loaded.SavedInFolders);
        Assert.Empty(loaded.Files);
    }

    [Fact]
    public void Save_CurrentFilePath_IsRemembered()
    {
        var photo = Path.Combine(_tempDir, "sub", "photo.jpg");
        new Settings { CurrentFolder = _tempDir, CurrentFilePath = photo }.Save(_tempDir);

        var loaded = Settings.Load(_tempDir);

        Assert.Equal(photo, loaded.CurrentFilePath);
    }

    [Fact]
    public void Save_StoresPathsRelativeToTheFolder()
    {
        new Settings
        {
            CurrentFolder   = _tempDir,
            LikedFolder     = Path.Combine(_tempDir, "Liked"),
            CurrentFilePath = Path.Combine(_tempDir, "sub", "photo.jpg"),
        }.Save(_tempDir);

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Settings.GetFilePath(_tempDir)));
        var root = json.RootElement;

        Assert.Equal(".", root.GetProperty("CurrentFolder").GetString());
        Assert.Equal("Liked", root.GetProperty("LikedFolder").GetString());
        Assert.Equal(Path.Combine("sub", "photo.jpg"), root.GetProperty("CurrentFilePath").GetString());
    }

    [Fact]
    public void Save_DoesNotChangeTheSettingsItSaves()
    {
        var settings = new Settings { CurrentFolder = _tempDir, LikedFolder = Path.Combine(_tempDir, "Liked") };

        settings.Save(_tempDir);

        Assert.Equal(_tempDir, settings.CurrentFolder);
        Assert.Equal(Path.Combine(_tempDir, "Liked"), settings.LikedFolder);
    }

    [Fact]
    public void Save_KeepsTheWindowState()
    {
        new Settings
        {
            CurrentFolder = _tempDir,
            WindowLeft    = 10,
            WindowTop     = 20,
            WindowWidth   = 1000,
            WindowHeight  = 800,
            ScreenIndex   = 1,
            WindowState   = 2,
        }.Save(_tempDir);

        var loaded = Settings.Load(_tempDir);

        Assert.Equal((10, 20, 1000, 800, 1, 2),
            (loaded.WindowLeft, loaded.WindowTop, loaded.WindowWidth, loaded.WindowHeight, loaded.ScreenIndex, loaded.WindowState));
    }

    // ── GoThroughSubFolders in the file ─────────────────────────────────────

    [Fact]
    public void Save_WritesGoThroughSubFoldersUnderTheNameTheWinFormsAppHasAlwaysUsed()
    {
        new Settings { CurrentFolder = _tempDir, GoThroughSubFolders = true }.Save(_tempDir);

        var json = File.ReadAllText(Settings.GetFilePath(_tempDir));

        Assert.Contains("\"GoThroughtSubFolders\": true", json);
        Assert.DoesNotContain("\"GoThroughSubFolders\"", json);
    }

    [Theory]
    [InlineData("GoThroughtSubFolders")] // the WinForms app
    [InlineData("GoThroughSubFolders")]  // the MAUI app
    public void Load_ReadsGoThroughSubFoldersUnderBothSpellings(string key)
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), "{ \"" + key + "\": true }");

        var settings = Settings.Load(_tempDir);

        Assert.True(settings.GoThroughSubFolders);
    }

    [Fact]
    public void Load_FileWrittenByTheWinFormsApp_IsRead()
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), """
            {
              "CurrentFolder": ".",
              "LikedFolder": "Liked",
              "GoThroughtSubFolders": true,
              "Extensions": [".jpg", ".png"],
              "CacheSize": 30,
              "WindowLeft": 5,
              "WindowTop": 6,
              "WindowWidth": 900,
              "WindowHeight": 600,
              "ScreenIndex": 0,
              "WindowState": 2,
              "CurrentFileName": "a.jpg",
              "CurrentFilePath": "sub\\a.jpg",
              "CurrentIndex": 4,
              "LikedFile": "Liked\\a.jpg",
              "IsDarkTheme": true,
              "ThumbnailSize": 150
            }
            """);

        var settings = Settings.Load(_tempDir);

        Assert.Equal(_tempDir, settings.CurrentFolder);
        Assert.Equal(Path.Combine(_tempDir, "Liked"), settings.LikedFolder);
        Assert.True(settings.GoThroughSubFolders);
        Assert.Equal([".jpg", ".png"], settings.Extensions);
        Assert.Equal(30, settings.CacheSize);
        Assert.Equal((5, 6, 900, 600, 2), (settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight, settings.WindowState));
        Assert.True(settings.IsDarkTheme);
        Assert.Equal(150, settings.ThumbnailSize);
    }

    // ── Folder the settings were found in ───────────────────────────────────

    [Fact]
    public void Load_FullPathsOfAnotherMachine_AreMovedToTheOpenedFolder()
    {
        // Written by an older version on a machine where the photos were on drive Y:
        File.WriteAllText(Settings.GetFilePath(_tempDir), """
            {
              "CurrentFolder": "Y:\\Photos",
              "LikedFolder": "Y:\\Photos\\Liked",
              "CurrentFilePath": "Y:\\Photos\\sub\\a.jpg"
            }
            """);

        var settings = Settings.Load(_tempDir);

        Assert.Equal(_tempDir, settings.CurrentFolder);
        Assert.Equal(Path.Combine(_tempDir, "Liked"), settings.LikedFolder);
        Assert.Equal(Path.Combine(_tempDir, "sub", "a.jpg"), settings.CurrentFilePath);
    }

    // ── Read / Write ────────────────────────────────────────────────────────

    [Fact]
    public void Read_FolderWithoutSettingsFile_ReturnsNull()
    {
        Assert.Null(Settings.Read(_tempDir, pinCurrentFolder: true));
    }

    [Fact]
    public void Read_CorruptFile_Throws()
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), "NOT_VALID_JSON{{{{");

        Assert.ThrowsAny<Exception>(() => Settings.Read(_tempDir, pinCurrentFolder: true));
    }

    [Fact]
    public void Read_WithoutPinning_KeepsTheStoredCurrentFolder()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "PLSomewhereElse");
        File.WriteAllText(Settings.GetFilePath(_tempDir),
            System.Text.Json.JsonSerializer.Serialize(new { CurrentFolder = elsewhere }));

        var settings = Settings.Read(_tempDir, pinCurrentFolder: false);

        Assert.Equal(elsewhere, settings!.CurrentFolder);
    }

    [Fact]
    public void Write_WithoutAFolder_WritesIntoTheCurrentFolder()
    {
        new Settings { CurrentFolder = _tempDir }.Write();

        Assert.True(File.Exists(Settings.GetFilePath(_tempDir)));
    }

    [Fact]
    public void Write_MissingFolder_Throws()
    {
        var settings = new Settings { CurrentFolder = Path.Combine(_tempDir, "missing") };

        Assert.ThrowsAny<IOException>(() => settings.Write());
    }

    [Fact]
    public void Save_MissingFolder_CreatesIt()
    {
        var folder = Path.Combine(_tempDir, "missing");

        new Settings { CurrentFolder = folder }.Save();

        Assert.True(File.Exists(Settings.GetFilePath(folder)));
    }

    // ── Liked folder ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDefaultLikedFolder_NothingSet_IsLikedInsideThePhotoFolder()
    {
        var settings = new Settings { CurrentFolder = _tempDir };

        settings.ApplyDefaultLikedFolder();

        Assert.Equal(Path.Combine(_tempDir, "Liked"), settings.LikedFolder);
    }

    [Fact]
    public void ApplyDefaultLikedFolder_FolderCalledLikedElsewhere_MovesInsideThePhotoFolder()
    {
        var settings = new Settings { CurrentFolder = _tempDir, LikedFolder = Path.Combine(Path.GetTempPath(), "OldPlace", "liked") };

        settings.ApplyDefaultLikedFolder();

        Assert.Equal(Path.Combine(_tempDir, "Liked"), settings.LikedFolder);
    }

    [Fact]
    public void ApplyDefaultLikedFolder_OtherName_IsKept()
    {
        var favourites = Path.Combine(Path.GetTempPath(), "Favourites");
        var settings = new Settings { CurrentFolder = _tempDir, LikedFolder = favourites };

        settings.ApplyDefaultLikedFolder();

        Assert.Equal(favourites, settings.LikedFolder);
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
