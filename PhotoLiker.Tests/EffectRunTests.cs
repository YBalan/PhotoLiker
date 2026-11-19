using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>
/// The setting EffectRun: how many photos a random effect stays for (1 to 100, 5 by default, or Random, which leaves how many to chance), how it is kept in the settings file, and the rule that works out the
/// length of a run.
/// </summary>
public class EffectRunTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PhotoLikerTests_" + Guid.NewGuid());

    public EffectRunTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string Written(Settings settings)
    {
        settings.CurrentFolder = _tempDir;
        settings.Save(_tempDir);
        return File.ReadAllText(Settings.GetFilePath(_tempDir));
    }

    private int LoadedFrom(string json)
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), json);
        return Settings.Load(_tempDir).SlideshowEffectRun;
    }

    // ── the setting ─────────────────────────────────────────────────────────

    [Fact]
    public void ByDefault_ARandomEffectStaysForFivePhotos()
    {
        Assert.Equal(5, new Settings().SlideshowEffectRun);
        Assert.Equal(5, Slideshow.DefaultEffectRun);
        Assert.Equal(100, Slideshow.MostEffectRun);
        Assert.Equal(0, Slideshow.RandomEffectRun);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(37)]
    [InlineData(100)]
    public void ANumber_IsWrittenAsANumber_AndReadBack(int photos)
    {
        var json = Written(new Settings { SlideshowEffectRun = photos });

        Assert.Contains($"\"SlideshowEffectRun\": {photos}", json);
        Assert.Equal(photos, Settings.Load(_tempDir).SlideshowEffectRun);
    }

    [Fact]
    public void Random_IsWrittenAsTheWordRandom_AndReadBack()
    {
        var json = Written(new Settings { SlideshowEffectRun = Slideshow.RandomEffectRun });

        Assert.Contains("\"SlideshowEffectRun\": \"Random\"", json);
        Assert.Equal(Slideshow.RandomEffectRun, Settings.Load(_tempDir).SlideshowEffectRun);
    }

    [Theory]
    [InlineData("\"Random\"", 0)]
    [InlineData("\"random\"", 0)]        // any case
    [InlineData("\" Random \"", 0)]
    [InlineData("\"12\"", 12)]           // a number written as text
    [InlineData("12.4", 12)]             // a number with a fraction
    [InlineData("0", 0)]
    [InlineData("150", 100)]             // out of the range: kept in it
    [InlineData("1000000000000", 100)]
    [InlineData("-3", 0)]                // below the range is random
    [InlineData("\"Randon\"", 5)]        // a word this version does not know: the default
    [InlineData("\"many\"", 5)]
    [InlineData("true", 5)]
    [InlineData("null", 5)]
    [InlineData("[1, 2]", 5)]
    public void WhatAFileHolds_IsReadTolerantly(string value, int expected) =>
        Assert.Equal(expected, LoadedFrom("{ \"SlideshowEffectRun\": " + value + ", \"CacheSize\": 9 }"));

    [Fact]
    public void ABadValue_DoesNotSpoilTheRestOfTheFile()
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), """{ "SlideshowEffectRun": { "a": [1, 2] }, "CacheSize": 9 }""");

        var loaded = Settings.Load(_tempDir);

        Assert.Equal(5, loaded.SlideshowEffectRun);
        Assert.Equal(9, loaded.CacheSize);
    }

    [Fact]
    public void AFileOfAnOlderVersion_HasTheDefault() =>
        Assert.Equal(5, LoadedFrom("""{ "SlideshowTimeout": 4 }"""));

    // ── the rules ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    public void TheSetting_IsKeptInItsRange(int setting, int kept) =>
        Assert.Equal(kept, Slideshow.EffectRunOf(setting));

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(1, 0.99)]
    [InlineData(5, 0.5)]
    [InlineData(100, 0.0)]
    [InlineData(37, 0.77)]
    public void ANumber_IsTheLengthOfTheRun_WhateverTheDraw(int setting, double unit) =>
        Assert.Equal(setting, Slideshow.RunLength(setting, unit));

    [Fact]
    public void Random_DrawsFromOneToOneHundred_ShortRunsMoreOftenThanLongOnes()
    {
        Assert.Equal(1, Slideshow.RunLength(Slideshow.RandomEffectRun, 0.0));
        Assert.Equal(100, Slideshow.RunLength(Slideshow.RandomEffectRun, 1.0));
        Assert.Equal(100, Slideshow.RunLength(Slideshow.RandomEffectRun, 0.9999));
        Assert.Equal(10, Slideshow.RunLength(Slideshow.RandomEffectRun, 0.5)); // the middle of the draw is 10

        // A draw that is even (1000 numbers from 0 up) gives every length from 1 to 100, and half of the runs are of 10 photos or fewer
        var lengths = Enumerable.Range(0, 1000).Select(i => Slideshow.RunLength(Slideshow.RandomEffectRun, i / 1000.0)).ToList();

        Assert.All(lengths, length => Assert.InRange(length, 1, 100));
        Assert.Equal(100, lengths.Distinct().Count()); // all the lengths come
        Assert.InRange(lengths.Count(length => length <= 10), 480, 520);
        Assert.InRange(lengths.Count(length => length <= 3), 265, 280); // more than a quarter are of three or fewer
        Assert.InRange(lengths.Count(length => length > 50), 140, 160);

        var last = 0;
        foreach (var length in lengths)
        {
            Assert.True(length >= last, "the longer the draw, the longer the run");
            last = length;
        }
    }

    [Fact]
    public void ARandomRun_WithADrawThatIsNoNumber_IsStillAGoodLength()
    {
        Assert.Equal(1, Slideshow.RunLength(Slideshow.RandomEffectRun, double.NaN));
        Assert.Equal(1, Slideshow.RunLength(Slideshow.RandomEffectRun, -5));
        Assert.Equal(100, Slideshow.RunLength(Slideshow.RandomEffectRun, 7));
        Assert.Equal(1, Slideshow.RunLength(-4, 0.0)); // a value below the range is random as well
        Assert.Equal(100, Slideshow.RunLength(-4, 1.0));
    }
}
