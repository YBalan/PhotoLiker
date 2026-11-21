namespace PhotoLiker.VideoToGif.Tests;

/// <summary>The values of the GIF: their ranges, and the little file that keeps them between two uses.</summary>
public sealed class GifSettingsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void TheDefaults_AreAGifThatSuitsAReadme()
    {
        var settings = new GifSettings();

        Assert.Equal(0.1, settings.FrameSeconds);
        Assert.Null(settings.DelaySeconds); // each frame is shown as long as it stands for: the speed of the video
        Assert.Equal(640, settings.Width);
        Assert.False(settings.OriginalSize);
        Assert.Equal(256, settings.Colors);
        Assert.Equal(GifDither.Sierra, settings.Dither);
        Assert.Equal(GifLoop.Forever, settings.Loop);
    }

    [Fact]
    public void EveryValue_IsBroughtIntoItsRange()
    {
        var low = new GifSettings { FrameSeconds = -3, Width = -50, Colors = 0 }.Normalized();
        var high = new GifSettings { FrameSeconds = 9000, Width = 90000, Colors = 9000 }.Normalized();

        Assert.Equal((0.02, 16, 2), (low.FrameSeconds, low.Width, low.Colors)); // 50 frames a second is the most
        Assert.Equal((60.0, 4096, 256), (high.FrameSeconds, high.Width, high.Colors));
        Assert.Equal(GifDither.Sierra, new GifSettings { Dither = (GifDither)77 }.Normalized().Dither);
        Assert.Equal(GifLoop.Forever, new GifSettings { Loop = (GifLoop)77 }.Normalized().Loop);
    }

    [Fact]
    public void TheDelay_IsBroughtIntoItsRange_OrLeftOpen()
    {
        Assert.Equal(0.02, new GifSettings { DelaySeconds = -3 }.Normalized().DelaySeconds);
        Assert.Equal(60.0, new GifSettings { DelaySeconds = 9000 }.Normalized().DelaySeconds);
        Assert.Equal(0.13, new GifSettings { DelaySeconds = 0.126 }.Normalized().DelaySeconds);
        Assert.Null(new GifSettings { DelaySeconds = null }.Normalized().DelaySeconds);
        Assert.Null(new GifSettings { DelaySeconds = double.NaN }.Normalized().DelaySeconds);
        Assert.Null(new GifSettings { DelaySeconds = double.PositiveInfinity }.Normalized().DelaySeconds);
    }

    [Fact]
    public void TheDelay_SurvivesBeingSavedAndLoaded_AndSoDoesHavingNone()
    {
        var file = _folder.File("delay.json");

        Assert.True(new GifSettings { FrameSeconds = 0.5, DelaySeconds = 0.08 }.Save(file));
        var loaded = GifSettings.Load(file);
        Assert.Equal((0.5, (double?)0.08), (loaded.FrameSeconds, loaded.DelaySeconds));

        Assert.True(new GifSettings { FrameSeconds = 0.5 }.Save(file));
        Assert.Null(GifSettings.Load(file).DelaySeconds);
    }

    [Fact]
    public void Normalising_ChangesNothingThatIsRight_AndDoesNotTouchTheOriginal()
    {
        var settings = new GifSettings { FrameSeconds = 0.25, Width = 480, OriginalSize = true, Colors = 64, Dither = GifDither.None, Loop = GifLoop.Once };

        var copy = settings.Normalized();

        Assert.NotSame(settings, copy);
        Assert.Equal((0.25, 480, true, 64, GifDither.None, GifLoop.Once), (copy.FrameSeconds, copy.Width, copy.OriginalSize, copy.Colors, copy.Dither, copy.Loop));

        var wrong = new GifSettings { FrameSeconds = 500 };
        wrong.Normalized();
        Assert.Equal(500, wrong.FrameSeconds);
    }

    [Fact]
    public void TheValues_SurviveBeingSavedAndLoaded()
    {
        var file = _folder.File("kept.json");
        var settings = new GifSettings { FrameSeconds = 2.5, Width = 480, OriginalSize = true, Colors = 64, Dither = GifDither.Bayer, Loop = GifLoop.Once };

        Assert.True(settings.Save(file));
        var loaded = GifSettings.Load(file);

        Assert.Equal((2.5, 480, true, 64, GifDither.Bayer, GifLoop.Once), (loaded.FrameSeconds, loaded.Width, loaded.OriginalSize, loaded.Colors, loaded.Dither, loaded.Loop));
        Assert.Contains("\"Dither\": \"Bayer\"", File.ReadAllText(file)); // a name a person can read and edit, not a number
    }

    [Fact]
    public void SavingMakesTheFolder_AndLeavesNoTemporaryFile()
    {
        var file = Path.Combine(_folder.Path, "a", "b", "kept.json");

        Assert.True(new GifSettings().Save(file));

        Assert.True(File.Exists(file));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp"));
    }

    [Fact]
    public void AFileThatIsMissing_GivesTheDefaults()
    {
        var loaded = GifSettings.Load(_folder.File("nothing.json"));

        Assert.Equal(0.1, loaded.FrameSeconds);
        Assert.Equal(GifLoop.Forever, loaded.Loop);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"Dither\": \"Nonsense\"}")]
    [InlineData("{\"FrameSeconds\": \"fast\"}")]
    public void AFileThatCannotBeRead_GivesTheDefaults_AndIsNoError(string content)
    {
        var file = _folder.File("broken.json");
        File.WriteAllText(file, content);

        var loaded = GifSettings.Load(file);

        Assert.Equal(0.1, loaded.FrameSeconds);
        Assert.Equal(640, loaded.Width);
    }

    [Fact]
    public void AFileWithValuesOutOfRange_IsLoadedInRange()
    {
        var file = _folder.File("wild.json");
        File.WriteAllText(file, "{\"FrameSeconds\": 900, \"Width\": 3, \"Colors\": 1000, \"Loop\": \"Once\"}");

        var loaded = GifSettings.Load(file);

        Assert.Equal((60.0, 16, 256, GifLoop.Once), (loaded.FrameSeconds, loaded.Width, loaded.Colors, loaded.Loop));
    }

    [Fact]
    public void AFileThatCannotBeWritten_IsNoError()
    {
        // A folder where the file should be: it cannot be written, and nothing may be thrown for that
        var blocked = _folder.File("blocked.json");
        Directory.CreateDirectory(blocked);

        Assert.False(new GifSettings().Save(blocked));
    }
}
