using PhotoLiker.Core;
using PhotoLiker.Effects.Looks;

namespace PhotoLiker.Effects.Tests.Looks;

/// <summary>The looks that came with the new effects (Vignette, Grain, OldPhoto, Season), and what is left to chance when a look, a border or a collage is Random.</summary>
public class NewLooksTests
{
    private const int W = 60, H = 40;

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static (int R, int G, int B) Parts(uint pixel) => ((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF));
    private static uint[] Uniform(uint colour) => Enumerable.Repeat(colour, W * H).ToArray();

    private static uint[] Graded(uint colour, SlideshowLook look, int seed = 0, DateTime? taken = null, int width = W, int height = H)
    {
        var pixels = Enumerable.Repeat(colour, width * height).ToArray();
        LookGrade.Apply(pixels, width, height, look, seed: seed, taken: taken);
        return pixels;
    }

    // ── Vignette and Grain ──────────────────────────────────────────────────

    [Fact]
    public void Vignette_DarkensTheCorners_AndLeavesTheMiddleAlone()
    {
        var grey = Rgb(128, 128, 128);
        var pixels = Graded(grey, SlideshowLook.Vignette);

        Assert.Equal(grey, pixels[(H / 2) * W + W / 2]);        // the middle is as it was
        Assert.Equal(grey, pixels[(H / 2) * W + W / 2 + 5]);
        var corner = Parts(pixels[0]);
        Assert.True(corner.R < 100 && corner.G < 100 && corner.B < 100, $"the corner is darker: {corner}");
        Assert.True(corner.R == corner.G && corner.G == corner.B, "and it stays grey: no tint");
    }

    [Fact]
    public void Vignette_IsDarkerTheFartherFromTheMiddle_AllTheWayOut()
    {
        var pixels = Graded(Rgb(200, 200, 200), SlideshowLook.Vignette);
        var row = H / 2;
        var last = 256;
        for (var x = W / 2; x < W; x++)
        {
            var value = Parts(pixels[row * W + x]).R;
            Assert.True(value <= last, $"x {x}: {value} > {last}");
            last = value;
        }
    }

    [Fact]
    public void Grain_ChangesThePixelsAbit_NeverTintsAGrey_AndIsTheSameForTheSameSeed()
    {
        var grey = Rgb(128, 128, 128);
        var a = Graded(grey, SlideshowLook.Grain, seed: 5);
        var b = Graded(grey, SlideshowLook.Grain, seed: 5);
        var c = Graded(grey, SlideshowLook.Grain, seed: 6);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(a.Distinct().Count() > 5, "there is grain");
        Assert.All(a, pixel =>
        {
            var (r, g, bl) = Parts(pixel);
            Assert.True(r == g && g == bl, "the same on all three colours");
            Assert.InRange(r, 128 - 20, 128 + 20);
        });
        Assert.InRange(a.Average(p => (double)Parts(p).R), 124, 132);    // no brighter or darker overall
        Assert.Equal(16, Graded(grey, SlideshowLook.Grain, seed: 5, width: 4, height: 4).Length); // a picture that is tiny still works
    }

    // ── OldPhoto ────────────────────────────────────────────────────────────

    [Fact]
    public void OldPhoto_IsBrown_Faded_AndDarkInTheCorners()
    {
        var black = Parts(Graded(Rgb(0, 0, 0), SlideshowLook.OldPhoto)[(H / 2) * W + W / 2]);
        var white = Parts(Graded(Rgb(255, 255, 255), SlideshowLook.OldPhoto)[(H / 2) * W + W / 2]);
        var mid = Parts(Graded(Rgb(128, 128, 128), SlideshowLook.OldPhoto)[(H / 2) * W + W / 2]);

        Assert.True(black.R > 20, $"the black is lifted: {black}");
        Assert.True(white.R < 245, $"the white is lowered: {white}");
        Assert.True(mid.R > mid.G && mid.G > mid.B, $"brown: {mid}");

        var corner = Parts(Graded(Rgb(200, 200, 200), SlideshowLook.OldPhoto, seed: 3)[0]);
        var centre = Parts(Graded(Rgb(200, 200, 200), SlideshowLook.OldPhoto, seed: 3)[(H / 2) * W + W / 2]);
        Assert.True(corner.R < centre.R - 20, $"the corner is darker: {corner} against {centre}");
    }

    [Fact]
    public void OldPhoto_HasScratchesAndSpecks_TheSameOnesForTheSameSeed_AndOthersForAnotherSeed()
    {
        const int w = 400, h = 300;
        var a = Graded(Rgb(128, 128, 128), SlideshowLook.OldPhoto, seed: 11, width: w, height: h);
        var again = Graded(Rgb(128, 128, 128), SlideshowLook.OldPhoto, seed: 11, width: w, height: h);
        var other = Graded(Rgb(128, 128, 128), SlideshowLook.OldPhoto, seed: 12, width: w, height: h);
        var sepia = Graded(Rgb(128, 128, 128), SlideshowLook.Sepia, seed: 11, width: w, height: h);

        Assert.Equal(a, again);
        Assert.NotEqual(a, other);
        // A speck or a scratch is much lighter or much darker than the plain sepia round it
        var marks = Enumerable.Range(0, w * h).Count(i => Math.Abs(Parts(a[i]).R - Parts(other[i]).R) > 40);
        Assert.True(marks > 30, $"marks that differ between seeds: {marks}");
    }

    // ── Season ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(12, LookGrade.Season.Winter)]
    [InlineData(1, LookGrade.Season.Winter)]
    [InlineData(2, LookGrade.Season.Winter)]
    [InlineData(3, LookGrade.Season.Spring)]
    [InlineData(5, LookGrade.Season.Spring)]
    [InlineData(6, LookGrade.Season.Summer)]
    [InlineData(8, LookGrade.Season.Summer)]
    [InlineData(9, LookGrade.Season.Autumn)]
    [InlineData(11, LookGrade.Season.Autumn)]
    public void TheSeason_FollowsTheMonth(int month, LookGrade.Season season) =>
        Assert.Equal(season, LookGrade.SeasonOf(new DateTime(2020, month, 15)));

    [Fact]
    public void Season_OfAPhotoWithNoDate_LeavesItAlone()
    {
        var picture = Rgb(90, 140, 200);
        Assert.All(Graded(picture, SlideshowLook.Season, taken: null), pixel => Assert.Equal(picture, pixel));
    }

    [Fact]
    public void Season_GivesWinterCoolAndSummerWarm_AndNoSeasonIsTheOther()
    {
        var grey = Rgb(128, 128, 128);
        var centre = (H / 2) * W + W / 2;
        var winter = Parts(Graded(grey, SlideshowLook.Season, taken: new DateTime(2020, 1, 10))[centre]);
        var summer = Parts(Graded(grey, SlideshowLook.Season, taken: new DateTime(2020, 7, 10))[centre]);
        var autumn = Parts(Graded(grey, SlideshowLook.Season, taken: new DateTime(2020, 10, 10))[centre]);
        var spring = Parts(Graded(grey, SlideshowLook.Season, taken: new DateTime(2020, 4, 10))[centre]);

        Assert.True(winter.B > winter.R + 4, $"winter is cool: {winter}");
        Assert.True(summer.R > summer.B + 4, $"summer is warm: {summer}");
        Assert.True(autumn.R > autumn.B + 4 && autumn.R >= summer.R - 8, $"autumn is golden: {autumn}");
        Assert.True(spring.G >= spring.R && spring.G >= spring.B, $"spring is fresh and green: {spring}");
        Assert.Equal(4, new[] { winter, summer, autumn, spring }.Distinct().Count());
    }

    [Fact]
    public void Season_OnlyDependsOnTheMonth_NotTheYearOrTheDay()
    {
        var grey = Rgb(128, 128, 128);
        Assert.Equal(
            Graded(grey, SlideshowLook.Season, taken: new DateTime(1999, 7, 1)),
            Graded(grey, SlideshowLook.Season, taken: new DateTime(2024, 7, 30)));
    }

    // ── the random choices ──────────────────────────────────────────────────

    [Fact]
    public void ARandomLook_IsTheSameForTheSameKeyAndSeed_AndAnyLookIsPossible()
    {
        Assert.Equal(RandomStyle.Look("a.jpg", 7, false), RandomStyle.Look("a.jpg", 7, false));

        var seen = new HashSet<SlideshowLook>();
        for (var i = 0; i < 600; i++)
            seen.Add(RandomStyle.Look($"photo{i}.jpg", 1, hasTable: false));

        Assert.Equal(Enum.GetValues<SlideshowLook>().Where(look => look != SlideshowLook.Lut && look != SlideshowLook.Random).OrderBy(look => look), seen.OrderBy(look => look));
        Assert.Contains(SlideshowLook.None, seen); // some photos stay as they are
        Assert.DoesNotContain(SlideshowLook.Lut, seen); // there is no table
        Assert.DoesNotContain(SlideshowLook.Random, seen); // a random look is never Random itself
    }

    [Fact]
    public void ARandomLook_MayBeTheTable_OnlyWhenThereIsOne()
    {
        var withTable = Enumerable.Range(0, 600).Select(i => RandomStyle.Look($"photo{i}.jpg", 1, hasTable: true)).ToHashSet();
        var without = Enumerable.Range(0, 600).Select(i => RandomStyle.Look($"photo{i}.jpg", 1, hasTable: false)).ToHashSet();

        Assert.Contains(SlideshowLook.Lut, withTable);
        Assert.DoesNotContain(SlideshowLook.Lut, without);
        Assert.Equal(Enum.GetValues<SlideshowLook>().Length - 1, LookGrade.RandomChoices(true).Count);   // all but Random itself
        Assert.Equal(Enum.GetValues<SlideshowLook>().Length - 2, LookGrade.RandomChoices(false).Count);  // and not the table either
    }

    [Fact]
    public void ARandomLook_IsSpreadEvenly_AndAnotherSeedMakesAnotherShow()
    {
        var counts = Enumerable.Range(0, 4000).GroupBy(i => RandomStyle.Look($"p{i}", 3, false)).ToDictionary(g => g.Key, g => g.Count());
        var expected = 4000.0 / counts.Count;

        Assert.All(counts.Values, count => Assert.InRange(count, expected * 0.7, expected * 1.3));
        Assert.NotEqual(
            Enumerable.Range(0, 50).Select(i => RandomStyle.Look($"p{i}", 3, false)),
            Enumerable.Range(0, 50).Select(i => RandomStyle.Look($"p{i}", 4, false)));
    }

    [Fact]
    public void TheCard_IsPutOnOnePhotoInThree_ByChance_ButTheSameOnesEveryTime()
    {
        var on = Enumerable.Range(0, 3000).Count(i => RandomStyle.Card($"photo{i}.jpg", 9));

        Assert.InRange(on, 3000 / RandomStyle.OneCardIn * 0.8, 3000 / RandomStyle.OneCardIn * 1.2);
        Assert.Equal(RandomStyle.Card("x.jpg", 9), RandomStyle.Card("x.jpg", 9));
        Assert.NotEqual(
            Enumerable.Range(0, 100).Select(i => RandomStyle.Card($"photo{i}.jpg", 9)),
            Enumerable.Range(0, 100).Select(i => RandomStyle.Card($"photo{i}.jpg", 10)));
    }

    [Fact]
    public void RandomPages_HoldOneToFourPhotos_AllPhotosInOrder_AndTheSameWithTheSameSeed()
    {
        var taken = Enumerable.Range(0, 200).Select(i => (DateTime?)new DateTime(2020, 1, 1).AddMinutes(i)).ToArray();

        var pages = RandomStyle.Pages(taken, 5);

        Assert.Equal(Enumerable.Range(0, 200), pages.SelectMany(page => page)); // every photo once, in the order of the list
        Assert.All(pages, page => Assert.InRange(page.Length, 1, CollagePlan.MostPhotos));
        Assert.Equal(new[] { 1, 2, 3, 4 }, pages.Select(page => page.Length).Distinct().Order().ToArray());
        Assert.Equal(pages.Select(page => page.Length), RandomStyle.Pages(taken, 5).Select(page => page.Length));
        Assert.NotEqual(pages.Select(page => page.Length), RandomStyle.Pages(taken, 6).Select(page => page.Length));
    }

    [Fact]
    public void RandomPages_StillEndWhereAnEventEnds()
    {
        // Two photos an hour apart, then a day, then two more: no page spans the gap of a day
        var taken = new DateTime?[]
        {
            new DateTime(2020, 1, 1, 10, 0, 0), new DateTime(2020, 1, 1, 11, 0, 0),
            new DateTime(2020, 1, 2, 12, 0, 0), new DateTime(2020, 1, 2, 13, 0, 0),
        };

        foreach (var seed in Enumerable.Range(0, 30))
        {
            var pages = RandomStyle.Pages(taken, seed);
            Assert.DoesNotContain(pages, page => page.Contains(1) && page.Contains(2));
        }
    }

    [Fact]
    public void RandomPages_OfNoPhotos_AreNoPages() =>
        Assert.Empty(RandomStyle.Pages([], 1));
}
