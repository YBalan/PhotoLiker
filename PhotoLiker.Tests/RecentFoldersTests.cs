using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>The rules of the list of the folders that were opened last: newest first, each folder once however it is written, at most ten, and names for a narrow place.</summary>
public class RecentFoldersTests
{
    [Theory]
    [InlineData(@"D:\Photos\Trip", @"D:\Photos\Trip")]
    [InlineData(@"D:\Photos\Trip\", @"D:\Photos\Trip")]
    [InlineData(@"D:\Photos\Trip\\", @"D:\Photos\Trip")]
    [InlineData("D:/Photos/Trip/", @"D:\Photos\Trip")]
    [InlineData(@"  D:\Photos\Trip  ", @"D:\Photos\Trip")]
    [InlineData(@"D:\Photos\.\Trip\..\Trip", @"D:\Photos\Trip")]
    [InlineData(@"Y:\", @"Y:\")]
    [InlineData(@"\\router\share\Photo\", @"\\router\share\Photo")]
    public void AFolder_IsKeptAsItsFullPath_WithoutTheSeparatorAtItsEnd_AndADriveKeepsItsBackslash(string written, string kept) =>
        Assert.Equal(kept, RecentFolders.Normalize(written));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path")]
    public void WhatIsNotAPath_IsNoFolder(string? written) => Assert.Equal(string.Empty, RecentFolders.Normalize(written));

    [Theory]
    [InlineData(@"D:\Photos\Trip", @"d:\photos\TRIP\", true)]
    [InlineData(@"D:\Photos\Trip", "D:/Photos/Trip", true)]
    [InlineData(@"D:\Photos\Trip", @"D:\Photos\Trip 2", false)]
    [InlineData(@"D:\Photos\Trip", "", false)]
    [InlineData("", "", false)] // nothing is not the same folder as nothing
    public void TheSameFolder_IsTheSame_WhateverTheCaseAndTheSeparator(string a, string b, bool same) => Assert.Equal(same, RecentFolders.Same(a, b));

    [Fact]
    public void TheFolderThatIsOpened_GoesFirst()
    {
        var list = RecentFolders.Remember([@"D:\A", @"D:\B"], @"D:\C");

        Assert.Equal([@"D:\C", @"D:\A", @"D:\B"], list);
    }

    [Fact]
    public void AFolderThatWasThere_MovesToTheFront_AndIsNotThereTwice_WhateverItIsWrittenLike()
    {
        var list = RecentFolders.Remember([@"D:\A", @"D:\B", @"D:\C"], @"d:\b\");

        Assert.Equal([@"d:\b", @"D:\A", @"D:\C"], list); // as it was written this time
    }

    [Fact]
    public void TheList_HoldsTenFolders_AndTheOldestIsLetGo()
    {
        var list = new List<string>();
        for (var i = 1; i <= 12; i++)
            list = RecentFolders.Remember(list, @"D:\Folder" + i);

        Assert.Equal(RecentFolders.Capacity, list.Count);
        Assert.Equal(10, RecentFolders.Capacity);
        Assert.Equal(@"D:\Folder12", list[0]);
        Assert.Equal(@"D:\Folder3", list[^1]);
        Assert.DoesNotContain(@"D:\Folder1", list);
        Assert.DoesNotContain(@"D:\Folder2", list);
    }

    [Fact]
    public void ABlankFolder_ChangesNothing_ButTheListIsTidied()
    {
        var list = RecentFolders.Remember([@"D:\A", "", @"d:\a\", "   ", @"D:\B"], null);

        Assert.Equal([@"D:\A", @"D:\B"], list);
    }

    [Fact]
    public void RememberingAFolder_DoesNotChangeTheListItWasGiven()
    {
        var given = new List<string> { @"D:\A", @"D:\B" };

        RecentFolders.Remember(given, @"D:\B");
        RecentFolders.Forget(given, @"D:\A");

        Assert.Equal([@"D:\A", @"D:\B"], given);
    }

    [Fact]
    public void NoList_IsAnEmptyList() => Assert.Equal([@"D:\A"], RecentFolders.Remember(null, @"D:\A"));

    [Fact]
    public void AFolder_CanBeTakenOffTheList_WhateverItIsWrittenLike()
    {
        var list = RecentFolders.Forget([@"D:\A", @"D:\B", @"D:\C"], "d:/b/");

        Assert.Equal([@"D:\A", @"D:\C"], list);
        Assert.Equal([@"D:\A"], RecentFolders.Forget([@"D:\A"], @"D:\Other"));
        Assert.Equal([@"D:\A"], RecentFolders.Forget([@"D:\A"], null));
    }

    [Fact]
    public void TheTitles_AreTheNamesOfTheFolders_AndWhereTwoAreCalledTheSame_BothAreTheWholePath()
    {
        var titles = RecentFolders.Titles([@"D:\Photos\Trip", @"D:\Photos\Home", @"E:\Backup\Home", @"Y:\"]);

        Assert.Equal(["Trip", @"D:\Photos\Home", @"E:\Backup\Home", @"Y:\"], titles);
    }

    [Fact]
    public void TwoFoldersCalledTheSame_AreToldApart_EvenWhenTheCaseDiffers() =>
        Assert.Equal([@"D:\a\Trip", @"D:\b\trip"], RecentFolders.Titles([@"D:\a\Trip", @"D:\b\trip"]));

    [Theory]
    [InlineData(@"D:\Photos\Trip", "Trip")]
    [InlineData(@"D:\Photos\Trip\", "Trip")]
    [InlineData(@"Y:\", "")]
    public void TheName_IsTheFolderItself(string folder, string name) => Assert.Equal(name, RecentFolders.Leaf(folder));

    [Fact]
    public void AShortPath_IsWrittenWhole() => Assert.Equal(@"D:\Photos\Trip", RecentFolders.Shorten(@"D:\Photos\Trip", 90));

    [Fact]
    public void ALongPath_KeepsItsDriveAndItsLastFolders_AndTheMiddleIsDots()
    {
        var path = @"D:\Archive\Family\2019\Summer\Seaside\Day one";

        var shortened = RecentFolders.Shorten(path, 30);

        Assert.StartsWith(@"D:\...\", shortened);
        Assert.EndsWith(@"\Day one", shortened);
        Assert.True(shortened.Length <= 30, shortened);
        Assert.True(shortened.Length < path.Length);
    }

    [Fact]
    public void ALongPath_NeverCutsTheNameOfTheFolderItself()
    {
        var path = @"D:\Archive\" + new string('x', 60);

        var shortened = RecentFolders.Shorten(path, 30);

        Assert.EndsWith(new string('x', 60), shortened); // too long for the room: it is shown whole rather than cut
        Assert.StartsWith(@"D:\", shortened);
    }

    [Fact]
    public void ANetworkPath_KeepsItsShare()
    {
        var shortened = RecentFolders.Shorten(@"\\router\share\Photo\Canon\2008\Holiday\Pictures", 36);

        Assert.StartsWith(@"\\router\share\...\", shortened);
        Assert.EndsWith(@"\Pictures", shortened);
    }

    [Fact]
    public void TheConfigFile_KeepsTheList()
    {
        // Through the same serializer the config file is written with: the list comes back as it was, in the same order
        var written = System.Text.Json.JsonSerializer.Serialize(new GlobalConfig { RecentFolders = [@"D:\A", @"D:\B"] });
        var read = System.Text.Json.JsonSerializer.Deserialize<GlobalConfig>(written)!;

        Assert.Equal([@"D:\A", @"D:\B"], read.RecentFolders);
        Assert.Empty(System.Text.Json.JsonSerializer.Deserialize<GlobalConfig>("{\"LastFolder\":\"D:\\\\A\"}")!.RecentFolders); // a file of an older version has none
    }
}
