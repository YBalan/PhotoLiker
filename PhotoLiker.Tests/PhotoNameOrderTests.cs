using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>The order of "By name": the way Explorer lists files, the same on every machine.</summary>
public class PhotoNameOrderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "photos");

    private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string[] Sorted(params string[] paths)
    {
        var list = paths.ToList();
        list.Sort(PhotoNameOrder.Compare);
        return [.. list];
    }

    [Fact]
    public void NumbersCountAsNumbers_SoTwoComesBeforeTen()
    {
        var sorted = Sorted(At("IMG_10.jpg"), At("IMG_2.jpg"), At("IMG_1.jpg"), At("IMG_100.jpg"));

        Assert.Equal([At("IMG_1.jpg"), At("IMG_2.jpg"), At("IMG_10.jpg"), At("IMG_100.jpg")], sorted);
    }

    [Fact]
    public void CaseDoesNotMatter()
    {
        var sorted = Sorted(At("b.jpg"), At("A.jpg"), At("C.JPG"), At("a2.jpg"));

        Assert.Equal([At("A.jpg"), At("a2.jpg"), At("b.jpg"), At("C.JPG")], sorted);
    }

    [Fact]
    public void LeadingZeros_DoNotChangeTheNumber_ButTheOrderStaysFixed()
    {
        // 007 and 7 are the same number: they are told apart by the text, so the order of the list is the same however it came
        var one = Sorted(At("a7.jpg"), At("a007.jpg"), At("a8.jpg"), At("a06.jpg"));
        var other = Sorted(At("a06.jpg"), At("a8.jpg"), At("a007.jpg"), At("a7.jpg"));

        Assert.Equal(one, other);
        Assert.Equal([At("a06.jpg"), At("a007.jpg"), At("a7.jpg"), At("a8.jpg")], one);
    }

    [Fact]
    public void ANumberTooLongForAnyIntegerType_StillCountsAsANumber()
    {
        var sorted = Sorted(At("p123456789012345678901234567890.jpg"), At("p99999999999999999999999999999.jpg"));

        Assert.Equal([At("p99999999999999999999999999999.jpg"), At("p123456789012345678901234567890.jpg")], sorted);
    }

    [Fact]
    public void AShortNameComesBeforeALongerOneThatStartsTheSame()
    {
        // Files without an extension, so that nothing but the length tells them apart
        var sorted = Sorted(At("photo10"), At("photo2"), At("photo"), At("photo2b"));

        Assert.Equal([At("photo"), At("photo2"), At("photo2b"), At("photo10")], sorted);
    }

    [Fact]
    public void TheFolderComesFirst_ThePhotosOfAFolderBeforeThoseOfItsSubfolders()
    {
        var sorted = Sorted(At("2024-10", "a.jpg"), At("z.jpg"), At("2024-2", "b.jpg"), At("a.jpg"));

        Assert.Equal([At("a.jpg"), At("z.jpg"), At("2024-2", "b.jpg"), At("2024-10", "a.jpg")], sorted);
    }

    [Fact]
    public void ASamePhotoNameInTwoFolders_IsToldApartByTheFolder()
    {
        var sorted = Sorted(At("b", "IMG_1.jpg"), At("a", "IMG_1.jpg"));

        Assert.Equal([At("a", "IMG_1.jpg"), At("b", "IMG_1.jpg")], sorted);
    }

    [Fact]
    public void PathsThatAreNotTheSame_NeverCompareEqual()
    {
        Assert.NotEqual(0, PhotoNameOrder.Compare(At("a.jpg"), At("A.jpg")));
        Assert.NotEqual(0, PhotoNameOrder.Compare(At("a01.jpg"), At("a1.jpg")));
        Assert.Equal(0, PhotoNameOrder.Compare(At("a.jpg"), At("a.jpg")));
    }

    [Fact]
    public void TheOtherWayRound_IsTheOppositeAnswer()
    {
        string[] paths = [At("IMG_2.jpg"), At("IMG_10.jpg"), At("img_2.jpg"), At("x", "IMG_1.jpg"), At("IMG_02.jpg")];

        foreach (var a in paths)
            foreach (var b in paths)
                Assert.Equal(Math.Sign(PhotoNameOrder.Compare(a, b)), -Math.Sign(PhotoNameOrder.Compare(b, a)));
    }
}
