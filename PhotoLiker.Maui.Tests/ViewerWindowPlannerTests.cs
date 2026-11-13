using PhotoLiker.Core;
using PhotoLiker.Maui.Services;

namespace PhotoLiker.Maui.Tests;

public class ViewerWindowPlannerTests
{
    private static List<PhotoFile> Files(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new PhotoFile($"photo{i:00}.jpg", string.Empty))];

    [Fact]
    public void StripRange_CentresOnTheShownPhoto()
    {
        Assert.Equal((4, 16), ViewerWindowPlanner.StripRange(count: 30, currentIndex: 10, siblingCount: 6));
    }

    [Fact]
    public void StripRange_IsCutAtTheEdgesOfTheList()
    {
        Assert.Equal((0, 6), ViewerWindowPlanner.StripRange(count: 30, currentIndex: 0, siblingCount: 6));
        Assert.Equal((23, 29), ViewerWindowPlanner.StripRange(count: 30, currentIndex: 29, siblingCount: 6));
    }

    [Fact]
    public void StripRange_OfAnEmptyList_IsEmpty()
    {
        Assert.Equal((0, -1), ViewerWindowPlanner.StripRange(count: 0, currentIndex: 0, siblingCount: 6));
    }

    [Fact]
    public void StripRange_ClampsAnIndexOutsideTheList()
    {
        Assert.Equal((0, 2), ViewerWindowPlanner.StripRange(count: 3, currentIndex: 99, siblingCount: 6));
        Assert.Equal((0, 2), ViewerWindowPlanner.StripRange(count: 3, currentIndex: -4, siblingCount: 6));
    }

    [Fact]
    public void NearestFirst_StartsWithTheShownPhoto_ThenAlternatesOutwards()
    {
        var order = ViewerWindowPlanner.NearestFirst(Files(10), currentIndex: 5, siblingCount: 2);

        Assert.Equal(["photo05.jpg", "photo06.jpg", "photo04.jpg", "photo07.jpg", "photo03.jpg"], order);
    }

    [Fact]
    public void NearestFirst_AtTheStartOfTheList_OnlyLooksForward()
    {
        var order = ViewerWindowPlanner.NearestFirst(Files(10), currentIndex: 0, siblingCount: 2);

        Assert.Equal(["photo00.jpg", "photo01.jpg", "photo02.jpg"], order);
    }

    [Fact]
    public void NearestFirst_OfAnEmptyList_IsEmpty()
    {
        Assert.Empty(ViewerWindowPlanner.NearestFirst([], currentIndex: 0, siblingCount: 6));
    }

    [Fact]
    public void NearestFirst_CoversTheWholeStrip()
    {
        var files = Files(30);
        var order = ViewerWindowPlanner.NearestFirst(files, currentIndex: 10, siblingCount: 6);
        var (from, to) = ViewerWindowPlanner.StripRange(30, 10, 6);

        Assert.Equal(to - from + 1, order.Count);
        Assert.Equal(
            [.. Enumerable.Range(from, to - from + 1).Select(i => files[i].OriginalFilePath).Order()],
            [.. order.Order()]);
    }
}
