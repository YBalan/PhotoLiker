using PhotoLiker.Core;

namespace PhotoLiker.GenerateVideo.Tests;

public sealed class VideoSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLVideoSettings_" + Guid.NewGuid().ToString("N"));

    public VideoSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static VideoSettings Everything(string folder) => new()
    {
        SortOrder = nameof(PhotoSortOrder.ByName),
        OutputFolder = folder,
        OutputFileName = "family.mp4",
        AudioPath = Path.Combine(folder, "music", "song.mp3"),
        RepeatPhotos = true,
        RepeatAudio = false,
        CoverPath = Path.Combine(folder, "cover.jpg"),
        CoverAtStart = true,
        CoverStartSeconds = 3.5,
        CoverAtEnd = true,
        CoverEndSeconds = 2.25,
        Width = 1280,
        Height = 720,
        StartFrameTimeSeconds = 0.3,
        EndFrameTimeSeconds = 2.0,
        UseRandom = true,
        Codec = "libx265",
        Crf = 28,
        CutOnBeat = true,
        TempoOctaves = -1,
    };

    [Fact]
    public void SaveAndLoad_EveryValue_ComesBackTheSame()
    {
        var saved = Everything(_folder);

        Assert.True(VideoSettingsFile.TrySave(_folder, saved, out var error), error);
        var loaded = VideoSettingsFile.Load(_folder)!;

        Assert.Equal(saved.SortOrder, loaded.SortOrder);
        Assert.Equal(PhotoSortOrder.ByName, loaded.GetSortOrder());
        Assert.Equal(saved.OutputFolder, loaded.OutputFolder);
        Assert.Equal("family.mp4", loaded.OutputFileName);
        Assert.Equal(saved.AudioPath, loaded.AudioPath);
        Assert.Equal(true, loaded.RepeatPhotos);
        Assert.Equal(false, loaded.RepeatAudio);
        Assert.Equal(saved.CoverPath, loaded.CoverPath);
        Assert.Equal(true, loaded.CoverAtStart);
        Assert.Equal(3.5, loaded.CoverStartSeconds);
        Assert.Equal(true, loaded.CoverAtEnd);
        Assert.Equal(2.25, loaded.CoverEndSeconds);
        Assert.Equal(1280, loaded.Width);
        Assert.Equal(720, loaded.Height);
        Assert.Equal(0.3, loaded.StartFrameTimeSeconds);
        Assert.Equal(2.0, loaded.EndFrameTimeSeconds);
        Assert.Equal(true, loaded.UseRandom);
        Assert.Equal("libx265", loaded.Codec);
        Assert.Equal(28, loaded.Crf);
        Assert.Equal(true, loaded.CutOnBeat);
        Assert.Equal(-1, loaded.TempoOctaves);
        Assert.Equal(VideoSettings.CurrentVersion, loaded.Version);
        Assert.NotNull(loaded.SavedUtc);
    }

    [Fact]
    public void Save_WritesTheFileInTheFolder_AndLeavesNoTemporaryFile()
    {
        Assert.True(VideoSettingsFile.TrySave(_folder, Everything(_folder), out _));

        Assert.True(File.Exists(Path.Combine(_folder, "photo-liker-video.json")));
        Assert.Equal(["photo-liker-video.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_PathsInsideTheFolder_AreStoredRelative_AndOnesOutsideStayFull()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "PLVideoSettings_elsewhere", "song.mp3");
        var settings = new VideoSettings { OutputFolder = _folder, CoverPath = Path.Combine(_folder, "pics", "cover.png"), AudioPath = elsewhere };

        Assert.True(VideoSettingsFile.TrySave(_folder, settings, out _));
        var text = File.ReadAllText(Path.Combine(_folder, "photo-liker-video.json"));

        Assert.DoesNotContain(_folder.Replace("\\", "\\\\"), text); // nothing of where the folder is
        Assert.Contains("\"OutputFolder\": \".\"", text);
        Assert.Contains(Path.Combine("pics", "cover.png").Replace("\\", "\\\\"), text);
        Assert.Contains("PLVideoSettings_elsewhere", text);
    }

    [Fact]
    public void Load_AFolderThatWasMoved_StillFindsItsFiles()
    {
        File.WriteAllText(Path.Combine(_folder, "cover.jpg"), "x");
        Assert.True(VideoSettingsFile.TrySave(_folder, new VideoSettings { OutputFolder = _folder, CoverPath = Path.Combine(_folder, "cover.jpg") }, out _));

        var moved = _folder + "_moved";
        try
        {
            Directory.Move(_folder, moved);
            var loaded = VideoSettingsFile.Load(moved)!;

            Assert.Equal(Path.GetFullPath(moved), loaded.OutputFolder);
            Assert.Equal(Path.Combine(moved, "cover.jpg"), loaded.CoverPath);
            Assert.True(File.Exists(loaded.CoverPath));
        }
        finally
        {
            if (Directory.Exists(moved))
                Directory.Move(moved, _folder);
        }
    }

    [Fact]
    public void Save_ABlankPathIsNone_ANullOneIsLeftOut()
    {
        Assert.True(VideoSettingsFile.TrySave(_folder, new VideoSettings { AudioPath = "  ", CoverPath = null, Width = 640 }, out _));
        var text = File.ReadAllText(Path.Combine(_folder, "photo-liker-video.json"));

        Assert.Contains("\"AudioPath\": \"\"", text);
        Assert.DoesNotContain("CoverPath", text);
        Assert.Contains("\"Width\": 640", text);

        var loaded = VideoSettingsFile.Load(_folder)!;
        Assert.Equal(string.Empty, loaded.AudioPath); // none: the audio was cleared
        Assert.Null(loaded.CoverPath);                // not in the file: leave it
    }

    [Fact]
    public void Save_DoesNotChangeTheSettingsGiven()
    {
        var settings = Everything(_folder);

        VideoSettingsFile.TrySave(_folder, settings, out _);

        Assert.Equal(Path.Combine(_folder, "cover.jpg"), settings.CoverPath);
        Assert.Null(settings.SavedUtc);
    }

    [Fact]
    public void Save_AgainOverwritesTheOldFile()
    {
        VideoSettingsFile.TrySave(_folder, new VideoSettings { Crf = 20 }, out _);
        VideoSettingsFile.TrySave(_folder, new VideoSettings { Crf = 30, Width = 100 }, out _);

        var loaded = VideoSettingsFile.Load(_folder)!;
        Assert.Equal(30, loaded.Crf);
        Assert.Equal(100, loaded.Width);
    }

    [Fact]
    public void Save_AFolderThatIsNotThere_FailsWithAReason_AndDoesNotMakeIt()
    {
        var gone = Path.Combine(_folder, "gone");

        Assert.False(VideoSettingsFile.TrySave(gone, new VideoSettings(), out var error));

        Assert.Contains("not there", error);
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void Save_WhenTheFileCannotBeReplaced_FailsWithAReason_AndLeavesTheOldFile()
    {
        VideoSettingsFile.TrySave(_folder, new VideoSettings { Crf = 20 }, out _);
        var path = VideoSettingsFile.GetPath(_folder);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // held open: the move over it is refused
        {
            Assert.False(VideoSettingsFile.TrySave(_folder, new VideoSettings { Crf = 30 }, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        Assert.Equal(20, VideoSettingsFile.Load(_folder)!.Crf);
        Assert.False(File.Exists(path + ".tmp"));
    }

    // ── Load: what a file can be ────────────────────────────────────────────

    [Fact]
    public void Load_NoFile_NoFolder_BlankFolder_AreNull()
    {
        Assert.Null(VideoSettingsFile.Load(_folder));
        Assert.Null(VideoSettingsFile.Load(Path.Combine(_folder, "gone")));
        Assert.Null(VideoSettingsFile.Load(""));
        Assert.Null(VideoSettingsFile.Load("   "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"Width\": \"wide\" }")]
    [InlineData("{ \"Crf\": 1.5e400 }")]
    [InlineData("null")]
    public void Load_AFileThatIsNotUsable_IsNull_NotAnError(string content)
    {
        File.WriteAllText(VideoSettingsFile.GetPath(_folder), content);

        Assert.Null(VideoSettingsFile.Load(_folder));
    }

    [Fact]
    public void Load_AFileWithSomeValues_LeavesTheOthersNull()
    {
        File.WriteAllText(VideoSettingsFile.GetPath(_folder), "{ \"Codec\": \"libx265\", \"Version\": 1 }");

        var loaded = VideoSettingsFile.Load(_folder)!;

        Assert.Equal("libx265", loaded.Codec);
        Assert.Null(loaded.Width);
        Assert.Null(loaded.AudioPath);
        Assert.Null(loaded.UseRandom);
        Assert.Null(loaded.GetSortOrder());
    }

    [Fact]
    public void Load_ValuesThisVersionDoesNotKnow_AreIgnored()
    {
        File.WriteAllText(VideoSettingsFile.GetPath(_folder), "{ \"Version\": 9, \"Crf\": 18, \"Subtitles\": { \"Font\": \"Arial\" } }");

        var loaded = VideoSettingsFile.Load(_folder)!;

        Assert.Equal(18, loaded.Crf);
        Assert.Equal(9, loaded.Version);
    }

    [Theory]
    [InlineData("ByName", PhotoSortOrder.ByName)]
    [InlineData("byname", PhotoSortOrder.ByName)]
    [InlineData("Random", PhotoSortOrder.Random)]
    [InlineData("None", PhotoSortOrder.None)]
    [InlineData("Sideways", null)]
    [InlineData("42", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetSortOrder_IsTheNamedOrder_OrNullForOneThatIsNotKnown(string? name, PhotoSortOrder? expected)
    {
        Assert.Equal(expected, new VideoSettings { SortOrder = name }.GetSortOrder());
    }

    [Fact]
    public void Load_RelativePathsInAHandMadeFile_AreResolvedAgainstTheFolder()
    {
        File.WriteAllText(VideoSettingsFile.GetPath(_folder), "{ \"AudioPath\": \"..\\\\other\\\\a.mp3\", \"CoverPath\": \"c.jpg\", \"OutputFolder\": \".\" }");

        var loaded = VideoSettingsFile.Load(_folder)!;

        Assert.Equal(Path.GetFullPath(Path.Combine(_folder, "..", "other", "a.mp3")), loaded.AudioPath);
        Assert.Equal(Path.Combine(_folder, "c.jpg"), loaded.CoverPath);
        Assert.Equal(Path.GetFullPath(_folder), loaded.OutputFolder);
    }

    [Fact]
    public void TheFile_IsReadableText_WithTheValuesByName()
    {
        VideoSettingsFile.TrySave(_folder, new VideoSettings { SortOrder = "Random", Codec = "libx264", CutOnBeat = true }, out _);

        var text = File.ReadAllText(VideoSettingsFile.GetPath(_folder));

        Assert.Contains("\"SortOrder\": \"Random\"", text);
        Assert.Contains("\"CutOnBeat\": true", text);
        Assert.Contains("\n", text); // indented, for a person to read and edit
    }
}
