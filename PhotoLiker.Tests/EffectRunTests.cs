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
        Assert.Equal(-1, Slideshow.RandomOneToTenEffectRun);
        Assert.Equal(-2, Slideshow.RandomThreeToTwentyEffectRun);
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

    [Theory]
    [InlineData(Slideshow.RandomEffectRun, "Random")]
    [InlineData(Slideshow.RandomOneToTenEffectRun, "Random 1-10")]
    [InlineData(Slideshow.RandomThreeToTwentyEffectRun, "Random 3-20")]
    public void ARandomValue_IsWrittenAsItsWords_AndReadBack(int setting, string words)
    {
        var json = Written(new Settings { SlideshowEffectRun = setting });

        Assert.Contains($"\"SlideshowEffectRun\": \"{words}\"", json);
        Assert.Equal(setting, Settings.Load(_tempDir).SlideshowEffectRun);
    }

    [Theory]
    [InlineData("\"Random\"", 0)]
    [InlineData("\"random\"", 0)]        // any case
    [InlineData("\" Random \"", 0)]
    [InlineData("\"Random 1-10\"", -1)]
    [InlineData("\"random 1-10\"", -1)]
    [InlineData("\"Random 1–10\"", -1)]  // with an en dash
    [InlineData("\"Random1 - 10\"", -1)] // spaces do not matter
    [InlineData("\"Random 3-20\"", -2)]
    [InlineData("\"RANDOM 3 – 20\"", -2)]
    [InlineData("\"Random 1-100\"", 0)]  // the whole range is Random itself
    [InlineData("\"Random 2-9\"", 5)]    // a range this version does not know: the default
    [InlineData("\"Random 1-\"", 5)]
    [InlineData("\"12\"", 12)]           // a number written as text
    [InlineData("12.4", 12)]             // a number with a fraction
    [InlineData("0", 0)]
    [InlineData("150", 100)]             // out of the range: kept in it
    [InlineData("1000000000000", 100)]
    [InlineData("-3", 0)]                // below the range is random
    [InlineData("-1", 0)]                // the two numbers that stand for the short ranges in the program are no way to write them
    [InlineData("-2", 0)]
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
    [InlineData(-1, -1)]   // the random values are kept as they are
    [InlineData(-2, -2)]
    [InlineData(-3, 0)]    // any other value below 1 is Random
    [InlineData(int.MinValue, 0)]
    public void TheSetting_IsKeptInItsRange(int setting, int kept) =>
        Assert.Equal(kept, Slideshow.EffectRunOf(setting));

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(-2, true)]
    [InlineData(-9, true)]
    [InlineData(1, false)]
    [InlineData(100, false)]
    [InlineData(500, false)]
    public void OnlyTheRandomValues_LeaveTheNumberToChance(int setting, bool random) =>
        Assert.Equal(random, Slideshow.IsRandomEffectRun(setting));

    [Theory]
    [InlineData(5, "5")]
    [InlineData(100, "100")]
    [InlineData(0, "Random")]
    [InlineData(-1, "Random 1-10")]
    [InlineData(-2, "Random 3-20")]
    [InlineData(-8, "Random")]
    public void TheSetting_HasItsWords(int setting, string words) =>
        Assert.Equal(words, Slideshow.EffectRunText(setting));

    [Theory]
    [InlineData("Random", true, 0)]
    [InlineData("random 1-10", true, -1)]
    [InlineData("  Random 3-20  ", true, -2)]
    [InlineData("Random 3—20", true, -2)]   // an em dash
    [InlineData("Random 3−20", true, -2)]   // a minus sign
    [InlineData("Random 1-100", true, 0)]
    [InlineData("Random 10-1", false, 5)]
    [InlineData("Random 3-21", false, 5)]
    [InlineData("Randomly", false, 5)]
    [InlineData("1-10", false, 5)]
    [InlineData("", false, 5)]
    [InlineData(null, false, 5)]
    public void TheWordsOfTheRandomValues_AreReadWithoutFuss(string? text, bool read, int setting)
    {
        Assert.Equal(read, Slideshow.TryReadRandomEffectRun(text, out var value));
        Assert.Equal(setting, value);
    }

    [Theory]
    [InlineData(long.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(long.MaxValue, 100)]
    public void ATypedNumber_IsKeptInTheRange_AndNeverTheShortRandomValues(long number, int kept) =>
        Assert.Equal(kept, Slideshow.EffectRunOfNumber(number));

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

    [Theory]
    [InlineData(Slideshow.RandomOneToTenEffectRun, 1, 10)]
    [InlineData(Slideshow.RandomThreeToTwentyEffectRun, 3, 20)]
    public void ARandomShortRange_DrawsEveryLengthOfItEquallyOften(int setting, int least, int most)
    {
        var count = most - least + 1;
        Assert.Equal(least, Slideshow.RunLength(setting, 0.0));
        Assert.Equal(most, Slideshow.RunLength(setting, 1.0));
        Assert.Equal(most, Slideshow.RunLength(setting, 0.9999));
        Assert.Equal(least + count / 2, Slideshow.RunLength(setting, 0.5)); // the middle of the draw is the middle of the range

        // An even draw (100 numbers for every length, each in the middle of its share of 0 to 1) gives every length of the range the same number of times, and none out of it
        var lengths = Enumerable.Range(0, count * 100).Select(i => Slideshow.RunLength(setting, (i + 0.5) / (count * 100))).ToList();

        Assert.Equal(Enumerable.Range(least, count), lengths.Distinct().OrderBy(length => length));
        Assert.All(Enumerable.Range(least, count), length => Assert.Equal(100, lengths.Count(drawn => drawn == length)));

        var last = 0;
        foreach (var length in lengths)
        {
            Assert.True(length >= last, "the longer the draw, the longer the run");
            last = length;
        }
    }

    [Theory]
    [InlineData(Slideshow.RandomEffectRun, 1, 100)]
    [InlineData(Slideshow.RandomOneToTenEffectRun, 1, 10)]
    [InlineData(Slideshow.RandomThreeToTwentyEffectRun, 3, 20)]
    public void ARandomRun_WithADrawThatIsNoNumber_IsStillAGoodLength(int setting, int least, int most)
    {
        Assert.Equal(least, Slideshow.RunLength(setting, double.NaN));
        Assert.Equal(least, Slideshow.RunLength(setting, -5));
        Assert.Equal(most, Slideshow.RunLength(setting, 7));
        Assert.Equal(least, Slideshow.RunLength(setting, double.NegativeInfinity));
        Assert.Equal(most, Slideshow.RunLength(setting, double.PositiveInfinity));
    }

    [Fact]
    public void AValueBelowTheRange_ThatIsNoRandomValue_IsRandomOfOneToOneHundred()
    {
        Assert.Equal(1, Slideshow.RunLength(-4, 0.0));
        Assert.Equal(100, Slideshow.RunLength(-4, 1.0));
        Assert.Equal(10, Slideshow.RunLength(int.MinValue, 0.5));
    }
}
