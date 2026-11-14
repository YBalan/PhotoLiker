using PhotoLiker.Core;
using PhotoLiker.Maui.Services;

namespace PhotoLiker.Maui.Tests;

public class ViewerDatesTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0);

    private static PhotoFile Photo(DateTime createdUtc, DateTime modifiedUtc) =>
        new("photo.jpg", string.Empty) { CreationTimeUtc = createdUtc, LastWriteTimeUtc = modifiedUtc };

    [Fact]
    public void TakenOf_PrefersTheCreationTime()
    {
        var photo = Photo(new DateTime(2025, 7, 10, 9, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 1, 9, 0, 0, DateTimeKind.Utc));

        var taken = ViewerDates.TakenOf(photo, Now);

        Assert.Equal(new DateTime(2025, 7, 10, 9, 0, 0, DateTimeKind.Utc).ToLocalTime(), taken);
    }

    [Fact]
    public void TakenOf_FallsBackToTheLastChange_WhenTheFileHasNoCreationTime()
    {
        // A file without a creation time reads as 1601
        var photo = Photo(new DateTime(1601, 1, 1), new DateTime(2025, 8, 14, 9, 0, 0, DateTimeKind.Utc));

        var taken = ViewerDates.TakenOf(photo, Now);

        Assert.Equal(new DateTime(2025, 8, 14, 9, 0, 0, DateTimeKind.Utc).ToLocalTime(), taken);
    }

    [Fact]
    public void TakenOf_OfAFileWithNoBelievableDate_IsNull()
    {
        // A reset camera clock (1980) and a time in the future are both unbelievable
        var photo = Photo(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc), Now.AddYears(1).ToUniversalTime());

        Assert.Null(ViewerDates.TakenOf(photo, Now));
        Assert.Null(ViewerDates.MonthOf(photo, Now));
    }

    [Fact]
    public void MonthOf_CountsTheMonthLikeMonthNavigation()
    {
        var photo = Photo(new DateTime(2025, 9, 2, 12, 0, 0, DateTimeKind.Utc), new DateTime(2025, 9, 2, 12, 0, 0, DateTimeKind.Utc));

        var expected = MonthNavigation.MonthOf(new DateTime(2025, 9, 2, 12, 0, 0, DateTimeKind.Utc).ToLocalTime());
        Assert.Equal(expected, ViewerDates.MonthOf(photo, Now));
    }
}
