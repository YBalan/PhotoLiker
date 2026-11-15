using System.Drawing;
using PhotoLiker.Core;
using PhotoLiker.Effects.Looks;

namespace PhotoLiker.Effects.Tests.Looks;

public class CollagePlanTests
{
    private static readonly DateTime Day = new(2014, 8, 9, 10, 0, 0);

    private static DateTime?[] Every(int count, TimeSpan step) => Enumerable.Range(0, count).Select(i => (DateTime?)(Day + step * i)).ToArray();

    private static string Show(IReadOnlyList<int[]> pages) => string.Join(" | ", pages.Select(page => string.Join(",", page)));

    // ── which photos make a page ────────────────────────────────────────────

    [Fact]
    public void WithNoCollage_EveryPhotoIsAPageOfItsOwn() =>
        Assert.Equal("0 | 1 | 2", Show(CollagePlan.Pages(Every(3, TimeSpan.FromMinutes(1)), SlideshowCollage.None)));

    [Theory]
    [InlineData(SlideshowCollage.Two, "0,1 | 2,3 | 4,5 | 6")]
    [InlineData(SlideshowCollage.Three, "0,1,2 | 3,4,5 | 6")]
    [InlineData(SlideshowCollage.Four, "0,1,2,3 | 4,5,6")]
    [InlineData(SlideshowCollage.Mixed, "0,1,2 | 3,4 | 5,6")]   // three, two, four (but only two are left)
    public void PhotosThatFollowEachOtherAreDividedIntoPagesOfTheSize(SlideshowCollage collage, string expected) =>
        Assert.Equal(expected, Show(CollagePlan.Pages(Every(7, TimeSpan.FromMinutes(5)), collage)));

    [Fact]
    public void Mixed_GoesThreeTwoFourInTurn() =>
        Assert.Equal("0,1,2 | 3,4 | 5,6,7,8 | 9,10,11", Show(CollagePlan.Pages(Every(12, TimeSpan.FromMinutes(1)), SlideshowCollage.Mixed)));

    [Fact]
    public void AnEventThatEnds_EndsThePage_AndThePhotoAfterTheGapStartsANewOne()
    {
        // Three photos in the morning, a gap of a day, then three in the evening of the next day
        DateTime?[] taken =
        [
            Day, Day.AddMinutes(10), Day.AddMinutes(20),
            Day.AddDays(1), Day.AddDays(1).AddMinutes(5), Day.AddDays(1).AddMinutes(9),
        ];

        Assert.Equal("0,1,2 | 3,4,5", Show(CollagePlan.Pages(taken, SlideshowCollage.Four)));
        Assert.Equal("0,1 | 2 | 3,4 | 5", Show(CollagePlan.Pages(taken, SlideshowCollage.Two)));
    }

    [Fact]
    public void TheEventLimit_IsSixHoursBetweenNeighbours_NotInAll()
    {
        // Neighbours five hours apart are one event however long the chain; seven hours apart are two
        Assert.Equal("0,1,2,3", Show(CollagePlan.Pages(Every(4, TimeSpan.FromHours(5)), SlideshowCollage.Four)));
        Assert.Equal("0 | 1 | 2 | 3", Show(CollagePlan.Pages(Every(4, TimeSpan.FromHours(7)), SlideshowCollage.Four)));
        Assert.Equal("0,1", Show(CollagePlan.Pages([Day, Day + CollagePlan.SameEvent], SlideshowCollage.Two))); // exactly six: still one
    }

    [Fact]
    public void APhotoWithNoDate_DoesNotEndAnEvent()
    {
        DateTime?[] taken = [Day, null, Day.AddMinutes(3), null];

        Assert.Equal("0,1,2,3", Show(CollagePlan.Pages(taken, SlideshowCollage.Four)));
    }

    [Fact]
    public void ThePagesCoverEveryPhotoOnce_InOrder_WhateverTheDates()
    {
        var random = new Random(5);
        foreach (var collage in Enum.GetValues<SlideshowCollage>())
        {
            var taken = Enumerable.Range(0, 200).Select(_ => random.Next(4) == 0 ? (DateTime?)null : Day.AddHours(random.Next(0, 400))).ToArray();
            var pages = CollagePlan.Pages(taken, collage);

            Assert.Equal(Enumerable.Range(0, 200), pages.SelectMany(page => page));
            Assert.All(pages, page => Assert.InRange(page.Length, 1, CollagePlan.MostPhotos));
        }
    }

    [Fact]
    public void NoPhotos_NoPages() => Assert.Empty(CollagePlan.Pages([], SlideshowCollage.Four));

    // ── where the photos go ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1920, 1080)]
    [InlineData(2, 1920, 1080)]
    [InlineData(3, 1920, 1080)]
    [InlineData(4, 1920, 1080)]
    [InlineData(2, 1080, 1920)]
    [InlineData(3, 1080, 1920)]
    [InlineData(4, 1080, 1920)]
    [InlineData(3, 500, 500)]
    public void TheCells_AreInsideTheScreen_NeverOverlap_AndLeaveTheGap(int count, int width, int height)
    {
        const int gap = 12;
        var cells = CollagePlan.Cells(count, width, height, gap);

        Assert.Equal(count, cells.Length);
        var screen = new RectangleF(0, 0, width, height);
        foreach (var cell in cells)
        {
            Assert.True(cell.Width > 0 && cell.Height > 0);
            Assert.True(RectangleF.Union(screen, cell) == screen, $"{cell} is inside the screen");
            Assert.True(cell.Left >= gap - 0.01 && cell.Top >= gap - 0.01 && cell.Right <= width - gap + 0.01 && cell.Bottom <= height - gap + 0.01, "the gap round the edge");
        }

        for (var a = 0; a < cells.Length; a++)
        for (var b = a + 1; b < cells.Length; b++)
        {
            var inflated = RectangleF.Inflate(cells[a], gap / 2f - 0.01f, gap / 2f - 0.01f);
            Assert.False(inflated.IntersectsWith(RectangleF.Inflate(cells[b], gap / 2f - 0.01f, gap / 2f - 0.01f)), $"cells {a} and {b} are a gap apart");
        }

        // And they fill the screen: the cells take almost all of what the gaps leave
        var area = cells.Sum(cell => cell.Width * cell.Height);
        Assert.True(area > 0.85 * (width - 2 * gap) * (height - 2 * gap), "the screen is used");
    }

    [Fact]
    public void ThreePhotos_AreOneLargeAndTwoSmall_TheLargeOnTheLeftOnAWideScreen_AndOnTopOnATallOne()
    {
        var wide = CollagePlan.Cells(3, 1920, 1080, 10);
        Assert.True(wide[0].Width > wide[1].Width && wide[0].Height > wide[1].Height && wide[1].Left > wide[0].Right);
        Assert.Equal(wide[1].Left, wide[2].Left);
        Assert.True(wide[2].Top > wide[1].Bottom);

        var tall = CollagePlan.Cells(3, 1080, 1920, 10);
        Assert.True(tall[0].Height > tall[1].Height && tall[0].Width > tall[1].Width && tall[1].Top > tall[0].Bottom);
        Assert.Equal(tall[1].Top, tall[2].Top);
    }

    [Fact]
    public void TwoPhotos_AreSideBySideOnAWideScreen_AndOneAboveTheOtherOnATallOne()
    {
        var wide = CollagePlan.Cells(2, 1920, 1080, 10);
        Assert.True(wide[1].Left > wide[0].Right && wide[0].Top == wide[1].Top);

        var tall = CollagePlan.Cells(2, 1080, 1920, 10);
        Assert.True(tall[1].Top > tall[0].Bottom && tall[0].Left == tall[1].Left);
    }

    [Fact]
    public void ACountThatIsNotAPage_IsBroughtToOne()
    {
        Assert.Single(CollagePlan.Cells(0, 100, 100, 5));
        Assert.Equal(4, CollagePlan.Cells(9, 100, 100, 5).Length);
        Assert.NotEmpty(CollagePlan.Cells(3, 10, 10, 200)); // a gap larger than the screen is still a layout
    }
}
