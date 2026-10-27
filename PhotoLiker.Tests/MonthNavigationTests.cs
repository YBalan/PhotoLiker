using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class MonthNavigationTests
{
    private const int Jul = 202507, Aug = 202508, Sep = 202509;

    private static int Next(int?[] months, int current) => MonthNavigation.FindMonthStart(months, current, m => m, forward: true);
    private static int Previous(int?[] months, int current) => MonthNavigation.FindMonthStart(months, current, m => m, forward: false);

    // July: 0, 1   August: 2   September: 3, 4
    private static readonly int?[] ThreeMonths = [Jul, Jul, Aug, Sep, Sep];

    [Theory]
    [InlineData(0, 2)]   // from the first photo of July to the first of August
    [InlineData(1, 2)]   // from the middle of July too
    [InlineData(2, 3)]
    [InlineData(3, -1)]  // September is the last month
    [InlineData(4, -1)]
    public void PageDown_GoesToTheFirstPhotoOfTheNextMonth(int current, int expected)
    {
        Assert.Equal(expected, Next(ThreeMonths, current));
    }

    [Theory]
    [InlineData(4, 2)]   // from September to the first (and only) photo of August
    [InlineData(3, 2)]
    [InlineData(2, 0)]   // from August to the first photo of July, not to its last
    [InlineData(1, -1)]  // July is the first month
    [InlineData(0, -1)]
    public void PageUp_GoesToTheFirstPhotoOfThePreviousMonth(int current, int expected)
    {
        Assert.Equal(expected, Previous(ThreeMonths, current));
    }

    [Fact]
    public void AMonthThatComesBackLater_IsAnotherRun()
    {
        // A folder in the order of camera counters, not of dates: A B A. Every change of the month is a step.
        int?[] months = [Jul, Aug, Jul];

        Assert.Equal(1, Next(months, 0));
        Assert.Equal(2, Next(months, 1));
        Assert.Equal(-1, Next(months, 2));
        Assert.Equal(1, Previous(months, 2));
        Assert.Equal(0, Previous(months, 1));
    }

    [Fact]
    public void PhotosWithoutADate_FormARunOfTheirOwn()
    {
        int?[] months = [null, null, Jul, Jul, null];

        Assert.Equal(2, Next(months, 0));
        Assert.Equal(4, Next(months, 3));
        Assert.Equal(2, Previous(months, 4));
        Assert.Equal(0, Previous(months, 2));
    }

    [Fact]
    public void ADecemberAndTheJanuaryAfterIt_AreTwoMonths()
    {
        int?[] months = [MonthNavigation.MonthOf(new DateTime(2024, 12, 31)), MonthNavigation.MonthOf(new DateTime(2025, 1, 1))];

        Assert.Equal(1, Next(months, 0));
        Assert.Equal(0, Previous(months, 1));
    }

    [Fact]
    public void ASameMonthOfAnotherYear_IsAnotherMonth()
    {
        int?[] months = [MonthNavigation.MonthOf(new DateTime(2023, 9, 5)), MonthNavigation.MonthOf(new DateTime(2024, 9, 5))];

        Assert.Equal(1, Next(months, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void AShownPhotoThatIsNotInTheList_GoesNowhere(int current)
    {
        Assert.Equal(-1, Next(ThreeMonths, current));
        Assert.Equal(-1, Previous(ThreeMonths, current));
    }

    [Fact]
    public void AnEmptyOrSingleList_GoesNowhere()
    {
        Assert.Equal(-1, Next([], 0));
        Assert.Equal(-1, Previous([], 0));
        Assert.Equal(-1, Next([Jul], 0));
        Assert.Equal(-1, Previous([Jul], 0));
    }

    [Fact]
    public void TheMonthOfADate_IsItsYearAndMonth()
    {
        Assert.Equal(200909, MonthNavigation.MonthOf(new DateTime(2009, 9, 30, 23, 59, 59)));
        Assert.Null(MonthNavigation.MonthOf(null));
    }

    [Fact]
    public void AMonth_IsDescribedInWords()
    {
        Assert.Equal("September 2009", MonthNavigation.Describe(200909));
        Assert.Equal("January 1999", MonthNavigation.Describe(199901));
        Assert.Equal("no date", MonthNavigation.Describe(null));
    }

    [Fact]
    public void ThePhotosAreAskedForTheirMonthOnlyUntilTheMonthChanges()
    {
        // 20,000 photos of one month must not be looked at one by one just to go to the next: here the next month is 3 photos away
        var asked = 0;
        var months = Enumerable.Range(0, 20000).Select(i => (int?)(i < 3 ? Jul : Aug)).ToArray();

        var target = MonthNavigation.FindMonthStart(months, 0, m => { asked++; return m; }, forward: true);

        Assert.Equal(3, target);
        Assert.Equal(1 + 3, asked); // the shown photo and the three after it
    }
}
