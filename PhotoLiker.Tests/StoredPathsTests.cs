using PhotoLikerUI;

namespace PhotoLiker.Tests;

public class StoredPathsTests
{
    // The paths below needn't exist
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "PLStoredPaths");

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    // ── ToStored ────────────────────────────────────────────────────────────

    [Fact]
    public void ToStored_PhotoInASubfolder_IsRelativeToTheBaseFolder()
    {
        var stored = StoredPaths.ToStored(P("Photos"), P("Photos", "!1000CANON", "IMG_1349.JPG"));

        Assert.Equal(Path.Combine("!1000CANON", "IMG_1349.JPG"), stored);
    }

    [Fact]
    public void ToStored_LikedFolderInsideTheBaseFolder_IsJustItsName()
    {
        Assert.Equal("Liked", StoredPaths.ToStored(P("Photos"), P("Photos", "Liked")));
    }

    [Fact]
    public void ToStored_TheBaseFolderItself_IsADot()
    {
        Assert.Equal(".", StoredPaths.ToStored(P("Photos"), P("Photos")));
    }

    [Fact]
    public void ToStored_BaseFolderWithTrailingSeparator_StillRelative()
    {
        var baseFolder = P("Photos") + Path.DirectorySeparatorChar;

        Assert.Equal("Liked", StoredPaths.ToStored(baseFolder, P("Photos", "Liked")));
    }

    [Fact]
    public void ToStored_FolderBesideTheBaseFolder_GoesThroughTheParent()
    {
        var stored = StoredPaths.ToStored(P("Photos"), P("Favourites"));

        Assert.Equal(Path.Combine("..", "Favourites"), stored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToStored_BlankPath_IsLeftAlone(string path)
    {
        Assert.Equal(path, StoredPaths.ToStored(P("Photos"), path));
    }

    [Fact]
    public void ToStored_AlreadyRelative_IsLeftAlone()
    {
        Assert.Equal("Liked", StoredPaths.ToStored(P("Photos"), "Liked"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    public void ToStored_NoUsableBaseFolder_LeavesThePathAlone(string baseFolder)
    {
        var path = P("Photos", "a.jpg");

        Assert.Equal(path, StoredPaths.ToStored(baseFolder, path));
    }

    [Fact]
    public void ToStored_OtherDrive_KeepsTheFullPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(@"D:\Favourites", StoredPaths.ToStored(@"C:\Photos", @"D:\Favourites"));
    }

    // ── FromStored ──────────────────────────────────────────────────────────

    [Fact]
    public void FromStored_RelativePath_IsResolvedAgainstTheBaseFolder()
    {
        var path = StoredPaths.FromStored(P("Photos"), Path.Combine("!1000CANON", "IMG_1349.JPG"));

        Assert.Equal(P("Photos", "!1000CANON", "IMG_1349.JPG"), path);
    }

    [Fact]
    public void FromStored_Dot_IsTheBaseFolder()
    {
        Assert.Equal(P("Photos"), StoredPaths.FromStored(P("Photos"), "."));
    }

    [Fact]
    public void FromStored_ParentSegments_AreResolved()
    {
        Assert.Equal(P("Favourites"), StoredPaths.FromStored(P("Photos"), Path.Combine("..", "Favourites")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void FromStored_BlankPath_IsLeftAlone(string stored)
    {
        Assert.Equal(stored, StoredPaths.FromStored(P("Photos"), stored));
    }

    [Fact]
    public void FromStored_BlankBaseFolder_LeavesThePathAlone()
    {
        Assert.Equal("Liked", StoredPaths.FromStored("", "Liked"));
    }

    [Fact]
    public void FromStored_FullPathWithoutLegacyRoot_IsLeftAlone()
    {
        var full = P("Elsewhere", "Liked");

        Assert.Equal(full, StoredPaths.FromStored(P("Photos"), full));
    }

    // ── FromStored: files written by an older version ───────────────────────

    [Fact]
    public void FromStored_FullPathBelowTheLegacyRoot_MovesToTheBaseFolder()
    {
        var oldRoot = P("OldMachine", "Lizka");
        var stored = P("OldMachine", "Lizka", "!1000CANON", "IMG_1349.JPG");

        var path = StoredPaths.FromStored(P("Photos", "Lizka"), stored, oldRoot);

        Assert.Equal(P("Photos", "Lizka", "!1000CANON", "IMG_1349.JPG"), path);
    }

    [Fact]
    public void FromStored_LegacyRootItself_IsTheBaseFolder()
    {
        var oldRoot = P("OldMachine", "Lizka");

        Assert.Equal(P("Photos"), StoredPaths.FromStored(P("Photos"), oldRoot, oldRoot));
    }

    [Fact]
    public void FromStored_FullPathOutsideTheLegacyRoot_IsLeftAlone()
    {
        var stored = P("OldMachine", "Favourites");

        Assert.Equal(stored, StoredPaths.FromStored(P("Photos"), stored, P("OldMachine", "Lizka")));
    }

    [Fact]
    public void FromStored_FolderThatOnlySharesThePrefixOfTheLegacyRoot_IsNotInsideIt()
    {
        var stored = P("OldMachine", "Lizka2", "a.jpg");

        Assert.Equal(stored, StoredPaths.FromStored(P("Photos"), stored, P("OldMachine", "Lizka")));
    }

    [Fact]
    public void FromStored_RelativePathWithLegacyRoot_IsStillResolvedAgainstTheBaseFolder()
    {
        var path = StoredPaths.FromStored(P("Photos"), "Liked", P("OldMachine", "Lizka"));

        Assert.Equal(P("Photos", "Liked"), path);
    }

    [Fact]
    public void FromStored_RelativeLegacyRoot_IsIgnored()
    {
        var stored = P("Elsewhere", "a.jpg");

        Assert.Equal(stored, StoredPaths.FromStored(P("Photos"), stored, "Lizka"));
    }

    [Fact]
    public void FromStored_PhotoFolderOpenedFromAnotherMachine_FindsItsFilesThere()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // The settings file says drive Y:, this machine reaches the same folder through a share
        const string oldRoot = @"Y:\OldFotik\!!Lizka";
        const string opened = @"\\nas\photos\OldFotik\!!Lizka";

        Assert.Equal(@"\\nas\photos\OldFotik\!!Lizka\Liked",
            StoredPaths.FromStored(opened, @"Y:\OldFotik\!!Lizka\Liked", oldRoot));
        Assert.Equal(@"\\nas\photos\OldFotik\!!Lizka\!1000CANON\IMG_1349.JPG",
            StoredPaths.FromStored(opened, @"Y:\OldFotik\!!Lizka\!1000CANON\IMG_1349.JPG", oldRoot));
    }

    [Fact]
    public void FromStored_LegacyRootInOtherLetterCase_StillMatches()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = StoredPaths.FromStored(@"D:\Photos", @"Y:\OldFotik\!!Lizka\Liked", @"y:\oldfotik\!!lizka");

        Assert.Equal(@"D:\Photos\Liked", path);
    }

    // ── both directions ─────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FolderMovedToAnotherLocation_PathsFollowIt()
    {
        var photo = P("Machine1", "Photos", "2019", "IMG_1.JPG");

        var stored = StoredPaths.ToStored(P("Machine1", "Photos"), photo);
        var onOtherMachine = StoredPaths.FromStored(P("Machine2", "Mounted", "Photos"), stored);

        Assert.Equal(P("Machine2", "Mounted", "Photos", "2019", "IMG_1.JPG"), onOtherMachine);
    }

    [Fact]
    public void RoundTrip_SameLocation_GivesTheOriginalPath()
    {
        var photo = P("Photos", "2019", "IMG_1.JPG");

        var stored = StoredPaths.ToStored(P("Photos"), photo);

        Assert.Equal(photo, StoredPaths.FromStored(P("Photos"), stored));
    }

    [Fact]
    public void RoundTrip_FolderOnAnotherDrive_KeepsItsFullPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var stored = StoredPaths.ToStored(@"C:\Photos", @"D:\Favourites");

        Assert.Equal(@"D:\Favourites", StoredPaths.FromStored(@"C:\Photos", stored));
    }
}
