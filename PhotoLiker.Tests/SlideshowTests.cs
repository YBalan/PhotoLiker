using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class SlideshowTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PhotoLikerTests_" + Guid.NewGuid());

    public SlideshowTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── settings ────────────────────────────────────────────────────────────

    [Fact]
    public void TheSettings_StartRandom_TwoSeconds_WithTheDateAndTime()
    {
        var settings = new Settings();

        Assert.Equal(SlideshowMode.Random, settings.SlideshowMode);
        Assert.Equal(SlideshowMonitors.Current, settings.SlideshowMonitors);
        Assert.Equal(2, settings.SlideshowTimeout);
        Assert.Equal(SlideshowWatermark.DateTime, settings.SlideshowWatermark);
    }

    [Fact]
    public void TheSettings_AreKeptInTheFileOfTheFolder_ByName()
    {
        new Settings { CurrentFolder = _tempDir, SlideshowMode = SlideshowMode.OneByOne, SlideshowMonitors = SlideshowMonitors.All, SlideshowTimeout = 0.5, SlideshowWatermark = SlideshowWatermark.Year }.Save(_tempDir);

        var json = File.ReadAllText(Settings.GetFilePath(_tempDir));
        Assert.Contains("\"SlideshowMode\": \"OneByOne\"", json);
        Assert.Contains("\"SlideshowWatermark\": \"Year\"", json);
        Assert.Contains("\"SlideshowMonitors\": \"All\"", json);

        var loaded = Settings.Load(_tempDir);
        Assert.Equal(SlideshowMode.OneByOne, loaded.SlideshowMode);
        Assert.Equal(SlideshowMonitors.All, loaded.SlideshowMonitors);
        Assert.Equal(0.5, loaded.SlideshowTimeout);
        Assert.Equal(SlideshowWatermark.Year, loaded.SlideshowWatermark);
    }

    [Fact]
    public void AFileWithoutThem_GivesTheDefaults()
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), """{ "CacheSize": 7 }""");

        var loaded = Settings.Load(_tempDir);

        Assert.Equal(7, loaded.CacheSize);
        Assert.Equal(SlideshowMode.Random, loaded.SlideshowMode);
        Assert.Equal(SlideshowWatermark.DateTime, loaded.SlideshowWatermark);
    }

    [Fact]
    public void AName_ThisVersionDoesNotKnow_DoesNotSpoilTheRestOfTheFile()
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir),
            """{ "CacheSize": 7, "SlideshowMode": "Spiral", "SlideshowWatermark": 12, "SlideshowMonitors": "Both", "SlideshowTimeout": 3 }""");

        var loaded = Settings.Load(_tempDir);

        Assert.Equal(7, loaded.CacheSize);
        Assert.Equal(3, loaded.SlideshowTimeout);
        Assert.Equal(SlideshowMode.Random, loaded.SlideshowMode);
        Assert.Equal(SlideshowMonitors.Current, loaded.SlideshowMonitors);
        Assert.Equal(SlideshowWatermark.DateTime, loaded.SlideshowWatermark);
    }

    [Theory]
    [InlineData("oneByOne", SlideshowMode.OneByOne)]
    [InlineData("RANDOM", SlideshowMode.Random)]
    public void TheCaseOfAName_DoesNotMatter(string name, SlideshowMode expected)
    {
        File.WriteAllText(Settings.GetFilePath(_tempDir), $$"""{ "SlideshowMode": "{{name}}" }""");

        Assert.Equal(expected, Settings.Load(_tempDir).SlideshowMode);
    }

    // ── the time a photo stays ──────────────────────────────────────────────

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0.5, 0.5)]
    [InlineData(0, Slideshow.MinTimeoutSeconds)]
    [InlineData(-4, Slideshow.MinTimeoutSeconds)]
    [InlineData(0.01, Slideshow.MinTimeoutSeconds)]
    [InlineData(1e9, Slideshow.MaxTimeoutSeconds)]
    [InlineData(double.PositiveInfinity, Slideshow.MaxTimeoutSeconds)]
    [InlineData(double.NaN, Slideshow.DefaultTimeoutSeconds)]
    public void TheTimeout_IsKeptInRange(double seconds, double expected) => Assert.Equal(expected, Slideshow.TimeoutOf(seconds));

    // ── Left / Right ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 1, 3)]
    [InlineData(2, -1, 1)]
    [InlineData(4, 1, 0)]    // after the last photo the first
    [InlineData(0, -1, 4)]   // before the first the last
    [InlineData(1, 3, 4)]
    [InlineData(1, 4, 0)]
    [InlineData(3, -9, 4)]
    [InlineData(2, 0, 2)]
    public void Move_GoesRoundTheEndsOfTheList(int index, int steps, int expected) => Assert.Equal(expected, Slideshow.Move(index, 5, steps));

    [Fact]
    public void Move_DoesNotCountAPhotoThatCanNotBeRead()
    {
        bool Broken(int i) => i is 2 or 3;

        Assert.Equal(4, Slideshow.Move(1, 6, 1, Broken));   // 2 and 3 are passed over
        Assert.Equal(5, Slideshow.Move(1, 6, 2, Broken));
        Assert.Equal(1, Slideshow.Move(4, 6, -1, Broken));
        Assert.Equal(0, Slideshow.Move(5, 6, 1, Broken));   // round the end
    }

    [Fact]
    public void Move_StaysWhereItIs_WhenThereIsNowhereToGo()
    {
        Assert.Equal(0, Slideshow.Move(0, 1, 1));
        Assert.Equal(1, Slideshow.Move(1, 3, 1, i => i != 1));
        Assert.Equal(-1, Slideshow.Move(0, 0, 1));
    }

    // ── random order ────────────────────────────────────────────────────────

    [Fact]
    public void Shuffle_ShowsEveryPhotoOnce_BeforeAnyComesAgain()
    {
        var shuffle = new SlideshowShuffle(50, new Random(7));

        for (var round = 0; round < 4; round++)
        {
            var picks = Enumerable.Range(0, 50).Select(_ => shuffle.Take(avoid: -1)).ToList();
            Assert.Equal(Enumerable.Range(0, 50), picks.Order());
        }
    }

    [Fact]
    public void Shuffle_NeverGivesTheSamePhotoTwiceInARow_AlsoAtTheSeamOfTwoRounds()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var shuffle = new SlideshowShuffle(3, new Random(seed));
            var previous = -1;
            for (var i = 0; i < 30; i++)
            {
                var pick = shuffle.Take(avoid: -1);
                Assert.NotEqual(previous, pick);
                previous = pick;
            }
        }
    }

    [Fact]
    public void Shuffle_NeverGivesThePhotoThatIsOnTheScreen()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var shuffle = new SlideshowShuffle(4, new Random(seed));
            var shown = shuffle.Upcoming(1)[0]; // the screen moved to the photo that was planned (a key did)
            Assert.NotEqual(shown, shuffle.Take(avoid: shown));
        }
    }

    [Fact]
    public void Shuffle_ShowsWhatIsNext_AndGivesExactlyThat()
    {
        var shuffle = new SlideshowShuffle(20, new Random(3));

        var upcoming = shuffle.Upcoming(5).ToList();
        Assert.Equal(upcoming, shuffle.Upcoming(5));          // looking does not use anything up
        Assert.Equal(upcoming.Take(3), shuffle.Upcoming(3));

        var taken = Enumerable.Range(0, 5).Select(_ => shuffle.Take(avoid: -1)).ToList();
        Assert.Equal(upcoming, taken);
    }

    [Fact]
    public void Shuffle_LeavesOutThePhotoThatWasOnTheScreenAtTheStart_InTheFirstRoundOnly()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var shuffle = new SlideshowShuffle(30, new Random(seed), shown: 12);

            var first = Enumerable.Range(0, 29).Select(_ => shuffle.Take(avoid: -1)).ToList();
            Assert.DoesNotContain(12, first);
            Assert.Equal(Enumerable.Range(0, 30).Where(i => i != 12), first.Order());

            var second = Enumerable.Range(0, 30).Select(_ => shuffle.Take(avoid: -1)).ToList();
            Assert.Equal(Enumerable.Range(0, 30), second.Order());
            Assert.NotEqual(first[^1], second[0]);
        }
    }

    [Fact]
    public void Shuffle_NeverStartsWithThePhotoOnTheScreen_EvenForTwoPhotos()
    {
        var shuffle = new SlideshowShuffle(2, new Random(1), shown: 1);

        Assert.Equal([0, 1, 0, 1], Enumerable.Range(0, 4).Select(_ => shuffle.Take(avoid: -1)));
    }

    [Fact]
    public void Shuffle_OfOnePhoto_GivesThatPhoto()
    {
        var shuffle = new SlideshowShuffle(1);

        Assert.Equal(0, shuffle.Take(avoid: 0));
        Assert.Equal([0, 0], shuffle.Upcoming(2));
    }

    [Fact]
    public void Shuffle_OfNoPhotos_IsNotMade() => Assert.Throws<ArgumentOutOfRangeException>(() => new SlideshowShuffle(0));

    [Fact]
    public void Shuffle_IsNotTheOrderOfTheList()
    {
        // Not seeded, as in the program; 20 photos can be in 20! orders, so six equal ones would be a broken shuffle, not luck
        var orders = Enumerable.Range(0, 6).Select(_ => string.Join(",", DrawAll(new SlideshowShuffle(20)))).Distinct().Count();

        Assert.True(orders > 1);

        static IEnumerable<int> DrawAll(SlideshowShuffle shuffle) => Enumerable.Range(0, 20).Select(_ => shuffle.Take(-1)).ToList();
    }

    // ── several screens ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 3, 8, new[] { 5, 6, 7 })]
    [InlineData(7, 3, 8, new[] { 7, 0, 1 })]   // round the end of the list
    [InlineData(0, 1, 8, new[] { 0 })]
    public void Strip_IsThePhotosOneAfterTheOther(int head, int screens, int count, int[] expected) =>
        Assert.Equal(expected, Slideshow.Strip(head, screens, count));

    [Fact]
    public void Strip_PassesOverPhotosThatCanNotBeRead_WithoutGapsOrRepeats()
    {
        bool Broken(int i) => i is 6 or 7;

        Assert.Equal([5, 8, 9], Slideshow.Strip(5, 3, 12, Broken));
        Assert.Equal([8, 9, 10], Slideshow.Strip(6, 3, 12, Broken));    // the head itself can not be read: the next one that can
        Assert.Equal([4, 5, 8], Slideshow.Strip(4, 3, 12, Broken));
    }

    [Theory]
    [InlineData(3, 1, 100, new[] { 0, 1, 2 })]
    [InlineData(1, 0, 100, new[] { 0 })]       // one screen, one photo each
    [InlineData(3, 1, 2, new[] { 0, 1 })]      // two photos: two screens, the one the window is on and its neighbour
    [InlineData(4, 3, 2, new[] { 2, 3 })]      // the window on the last of four screens: the one next to it
    [InlineData(4, 1, 3, new[] { 0, 1, 2 })]
    [InlineData(4, 0, 3, new[] { 0, 1, 2 })]
    [InlineData(3, 1, 1, new[] { 1 })]         // one photo, one screen: the window's
    public void ChooseScreens_KeepsTheWindowsScreenAndTheNearestOnes(int screens, int primary, int photos, int[] expected) =>
        Assert.Equal(expected, Slideshow.ChooseScreens(screens, primary, photos));

    [Fact]
    public void Shuffle_Take_CanTurnDownPhotos_AndDoesNotGiveThemInTheSameRound()
    {
        var shuffle = new SlideshowShuffle(10, new Random(5));
        var picks = new List<int>();
        for (var i = 0; i < 4; i++)
            picks.Add(shuffle.Take(place => picks.Contains(place) || place % 2 == 0));

        Assert.All(picks, place => Assert.Equal(1, place % 2));
        Assert.Equal(picks.Count, picks.Distinct().Count());
    }

    [Fact]
    public void Shuffle_Take_GivesUpWhenEveryPhotoIsTurnedDown()
    {
        var shuffle = new SlideshowShuffle(3, new Random(1));

        Assert.InRange(shuffle.Take(_ => true), 0, 2);
    }

    [Fact]
    public void ScreensThatTakeInTurns_GetDifferentPhotos_EveryRound()
    {
        // 3 screens and 12 photos: every tick gives the screens 3 different photos, and each photo comes once in 4 ticks
        for (var seed = 0; seed < 50; seed++)
        {
            var shuffle = new SlideshowShuffle(12, new Random(seed), shown: 4);
            var shown = new[] { 4, -1, -1 };
            var all = new List<int>();
            for (var tick = 0; tick < 8; tick++)
            {
                var next = new int[3];
                for (var screen = 0; screen < 3; screen++)
                    next[screen] = shuffle.Take(place => place == shown[screen] || Array.IndexOf(next, place, 0, screen) >= 0);

                Assert.Equal(3, next.Distinct().Count());
                all.AddRange(next);
                shown = next;
            }

            Assert.Equal(24, all.Count);
            Assert.Equal(12, all.Distinct().Count());
        }
    }

    // ── the watermark ───────────────────────────────────────────────────────

    private static readonly DateTime Taken = new(2019, 7, 14, 15, 32, 5);

    [Fact]
    public void TheYear_IsJustTheYear() => Assert.Equal("2019", Slideshow.WatermarkText(SlideshowWatermark.Year, Taken));

    [Fact]
    public void TheDateAndTime_AreWrittenAsTheRestOfTheProgramWritesThem() =>
        Assert.Equal("14 Jul 2019 15:32:05", Slideshow.WatermarkText(SlideshowWatermark.DateTime, Taken));

    [Theory]
    [InlineData(SlideshowWatermark.None)]
    [InlineData(SlideshowWatermark.Year)]
    [InlineData(SlideshowWatermark.DateTime)]
    public void APhotoWithoutADate_HasNothingToWrite(SlideshowWatermark watermark) => Assert.Null(Slideshow.WatermarkText(watermark, null));

    [Fact]
    public void NoWatermark_WritesNothing() => Assert.Null(Slideshow.WatermarkText(SlideshowWatermark.None, Taken));
}
