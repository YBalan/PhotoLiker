using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>
/// Tests for GroupLibrary (the WinForms app's group folders) against a real temp file system.
/// They live in PhotoLiker.Core.
/// </summary>
public class GroupLibraryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PLGroupLibTests_" + Guid.NewGuid());
    private readonly string _photosDir;
    private readonly string _likedDir;

    public GroupLibraryTests()
    {
        _photosDir = Path.Combine(_tempDir, "Photos");
        _likedDir = Path.Combine(_photosDir, "Liked");
        Directory.CreateDirectory(_photosDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>A photo on disk, described the way the folder scan describes it (size and timestamp included).</summary>
    private PhotoFile MakePhoto(string relativePath, string content = "photo")
    {
        var path = Path.Combine(_photosDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        var info = new FileInfo(path);
        return new PhotoFile(path, string.Empty)
        {
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
    }

    private string GroupPath(string folderName, string fileName) => Path.Combine(_likedDir, folderName, fileName);

    private GroupLibrary Scan(params PhotoFile[] files) => GroupLibrary.Scan(_likedDir, files);

    /// <summary>Toggles on an existing index and applies the result, like the UI does.</summary>
    private static GroupToggleResult Toggle(GroupLibrary groups, char key, PhotoFile photo, params PhotoFile[] files)
    {
        var result = groups.Toggle(key, photo, files);
        groups.Update(result);
        return result;
    }

    /// <summary>Toggles on a freshly scanned index; the result is not applied.</summary>
    private GroupToggleResult ToggleOnce(char key, PhotoFile photo, params PhotoFile[] files) =>
        Scan(files).Toggle(key, photo, files);

    // ── Toggle: saving ──────────────────────────────────────────────────────

    [Fact]
    public void Toggle_NothingExists_CreatesLikedAndGroupFoldersAndCopiesPhoto()
    {
        var photo = MakePhoto("a.jpg", "pixels");

        var result = ToggleOnce('A', photo, [photo]);

        Assert.True(result.Added);
        Assert.Equal(GroupPath("A - Group", "a.jpg"), result.FilePath);
        Assert.Equal("pixels", File.ReadAllText(result.FilePath));
    }

    [Fact]
    public void Toggle_LeavesTheOriginalInPlace()
    {
        var photo = MakePhoto("a.jpg");

        ToggleOnce('A', photo, [photo]);

        Assert.True(File.Exists(photo.OriginalFilePath));
    }

    [Fact]
    public void Toggle_DigitKey_UsesDigitFolder()
    {
        var photo = MakePhoto("a.jpg");

        var result = ToggleOnce('3', photo, [photo]);

        Assert.Equal(GroupPath("3 - Group", "a.jpg"), result.FilePath);
        Assert.Equal('3', result.Key);
    }

    [Fact]
    public void Toggle_LowercaseKey_UsesUppercaseFolder()
    {
        var photo = MakePhoto("a.jpg");

        var result = ToggleOnce('b', photo, [photo]);

        Assert.Equal(GroupPath("B - Group", "a.jpg"), result.FilePath);
        Assert.Equal('B', result.Key);
    }

    [Fact]
    public void Toggle_RenamedFolderExists_SavesIntoRenamedFolder()
    {
        var photo = MakePhoto("a.jpg");
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Family"));

        var result = ToggleOnce('A', photo, [photo]);

        Assert.True(File.Exists(GroupPath("A - Group Family", "a.jpg")));
        Assert.Equal(Path.Combine(_likedDir, "A - Group Family"), result.Folder);
        Assert.False(Directory.Exists(Path.Combine(_likedDir, "A - Group")));
    }

    [Fact]
    public void Toggle_FolderRenamedAfterTheScan_SavesIntoRenamedFolder()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);
        Directory.Move(Path.Combine(_likedDir, "A - Group"), Path.Combine(_likedDir, "A - Group Friends"));
        var other = MakePhoto("b.jpg");

        var result = Toggle(groups, 'A', other, photo, other);

        Assert.Equal(GroupPath("A - Group Friends", "b.jpg"), result.FilePath);
        Assert.False(Directory.Exists(Path.Combine(_likedDir, "A - Group")));
    }

    [Fact]
    public void Toggle_InvalidKey_ThrowsAndCreatesNothing()
    {
        var photo = MakePhoto("a.jpg");

        Assert.Throws<ArgumentOutOfRangeException>(() => ToggleOnce('0', photo, [photo]));

        Assert.False(Directory.Exists(_likedDir));
    }

    [Fact]
    public void Toggle_SourceMissing_ThrowsAndLeavesNoCopy()
    {
        var photo = MakePhoto("a.jpg");
        File.Delete(photo.OriginalFilePath);

        Assert.ThrowsAny<IOException>(() => ToggleOnce('A', photo, [photo]));

        Assert.False(File.Exists(GroupPath("A - Group", "a.jpg")));
    }

    [Fact]
    public void Toggle_PhotoLikedInTheLikedFolder_LeavesThatCopyAlone()
    {
        var photo = MakePhoto("a.jpg");
        Directory.CreateDirectory(_likedDir);
        var liked = Path.Combine(_likedDir, "a.jpg");
        File.Copy(photo.OriginalFilePath, liked);

        var result = ToggleOnce('A', photo, [photo]);

        Assert.True(result.Added);
        Assert.True(File.Exists(liked));
    }

    // ── Toggle: removing ────────────────────────────────────────────────────

    [Fact]
    public void Toggle_PhotoAlreadyInGroup_RemovesTheCopy()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        var saved = Toggle(groups, 'A', photo, photo);

        var removed = Toggle(groups, 'A', photo, photo);

        Assert.True(saved.Added);
        Assert.False(removed.Added);
        Assert.Equal(saved.FilePath, removed.FilePath);
        Assert.False(File.Exists(saved.FilePath));
        Assert.True(File.Exists(photo.OriginalFilePath));
    }

    [Fact]
    public void Toggle_RemovingTheLastCopy_KeepsTheGroupFolder()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);

        Toggle(groups, 'A', photo, photo);

        Assert.True(Directory.Exists(Path.Combine(_likedDir, "A - Group")));
    }

    [Fact]
    public void Toggle_ThreeTimes_SavesAgain()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);
        Toggle(groups, 'A', photo, photo);

        var third = Toggle(groups, 'A', photo, photo);

        Assert.True(third.Added);
        Assert.True(File.Exists(third.FilePath));
    }

    [Fact]
    public void Toggle_RemovesFromRenamedFolder()
    {
        var photo = MakePhoto("a.jpg");
        var renamed = Path.Combine(_likedDir, "A - Group Family");
        Directory.CreateDirectory(renamed);
        File.Copy(photo.OriginalFilePath, Path.Combine(renamed, "a.jpg"));

        var result = ToggleOnce('A', photo, [photo]);

        Assert.False(result.Added);
        Assert.False(File.Exists(Path.Combine(renamed, "a.jpg")));
    }

    [Fact]
    public void Toggle_CopyAddedToTheFolderSinceTheScan_RemovesIt()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        var folder = Path.Combine(_likedDir, "A - Group");
        Directory.CreateDirectory(folder);
        File.Copy(photo.OriginalFilePath, Path.Combine(folder, "a.jpg"));

        var result = Toggle(groups, 'A', photo, photo);

        Assert.False(result.Added);
        Assert.False(File.Exists(Path.Combine(folder, "a.jpg")));
    }

    [Fact]
    public void Toggle_CopyDeletedFromTheFolderSinceTheScan_SavesItAgain()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        var saved = Toggle(groups, 'A', photo, photo);
        File.Delete(saved.FilePath);

        var result = Toggle(groups, 'A', photo, photo);

        Assert.True(result.Added);
        Assert.True(File.Exists(result.FilePath));
    }

    // ── Toggle: several groups and same-named photos ────────────────────────

    [Fact]
    public void Toggle_DifferentGroups_KeepIndependentCopies()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);
        Toggle(groups, '1', photo, photo);

        var removed = Toggle(groups, 'A', photo, photo);

        Assert.False(removed.Added);
        Assert.True(File.Exists(GroupPath("1 - Group", "a.jpg")));
        Assert.False(File.Exists(GroupPath("A - Group", "a.jpg")));
    }

    [Fact]
    public void Toggle_PhotosWithTheSameName_GetSeparateCopies()
    {
        var first = MakePhoto("a.jpg", "first photo");
        var second = MakePhoto(Path.Combine("sub", "a.jpg"), "the second one");
        var groups = Scan(first, second);

        var firstCopy = Toggle(groups, 'A', first, first, second);
        var secondCopy = Toggle(groups, 'A', second, first, second);

        Assert.True(firstCopy.Added);
        Assert.True(secondCopy.Added);
        Assert.Equal(GroupPath("A - Group", "a.jpg"), firstCopy.FilePath);
        Assert.Equal(GroupPath("A - Group", "a (2).jpg"), secondCopy.FilePath);
        Assert.Equal("first photo", File.ReadAllText(firstCopy.FilePath));
        Assert.Equal("the second one", File.ReadAllText(secondCopy.FilePath));
    }

    [Fact]
    public void Toggle_PhotosWithTheSameName_RemoveOnlyTheirOwnCopy()
    {
        var first = MakePhoto("a.jpg", "first photo");
        var second = MakePhoto(Path.Combine("sub", "a.jpg"), "the second one");
        var groups = Scan(first, second);
        Toggle(groups, 'A', first, first, second);
        Toggle(groups, 'A', second, first, second);

        var removed = Toggle(groups, 'A', second, first, second);

        Assert.False(removed.Added);
        Assert.True(File.Exists(GroupPath("A - Group", "a.jpg")));
        Assert.False(File.Exists(GroupPath("A - Group", "a (2).jpg")));
    }

    // ── Scan / FoldersOf ────────────────────────────────────────────────────

    [Fact]
    public void Scan_LikedFolderMissing_FindsNoFolders()
    {
        var photo = MakePhoto("a.jpg");

        var groups = Scan(photo);

        Assert.Empty(groups.FoldersOf(photo));
    }

    [Fact]
    public void Scan_FindsCopiesInGroupFoldersIncludingRenamedOnes()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Family"));
        Directory.CreateDirectory(Path.Combine(_likedDir, "2 - Group"));
        File.Copy(photo.OriginalFilePath, GroupPath("A - Group Family", "a.jpg"));
        File.Copy(photo.OriginalFilePath, GroupPath("2 - Group", "a.jpg"));
        File.Copy(other.OriginalFilePath, GroupPath("2 - Group", "b.jpg"));

        var groups = Scan(photo, other);

        Assert.Equal(
            [Path.Combine(_likedDir, "2 - Group"), Path.Combine(_likedDir, "A - Group Family")],
            groups.FoldersOf(photo));
        Assert.Equal([Path.Combine(_likedDir, "2 - Group")], groups.FoldersOf(other));
    }

    [Fact]
    public void Scan_IgnoresCopiesInTheLikedFolderItself()
    {
        var photo = MakePhoto("a.jpg");
        Directory.CreateDirectory(_likedDir);
        File.Copy(photo.OriginalFilePath, Path.Combine(_likedDir, "a.jpg"));

        var groups = Scan(photo);

        Assert.Empty(groups.FoldersOf(photo));
    }

    [Fact]
    public void Scan_IgnoresFoldersThatAreNotGroups()
    {
        var photo = MakePhoto("a.jpg");
        Directory.CreateDirectory(Path.Combine(_likedDir, "Holidays"));
        Directory.CreateDirectory(Path.Combine(_likedDir, "0 - Group"));
        File.Copy(photo.OriginalFilePath, GroupPath("Holidays", "a.jpg"));
        File.Copy(photo.OriginalFilePath, GroupPath("0 - Group", "a.jpg"));

        var groups = Scan(photo);

        Assert.Empty(groups.FoldersOf(photo));
    }

    [Fact]
    public void Scan_PhotoThatIsNotInAnyGroup_HasNoFolders()
    {
        var saved = MakePhoto("a.jpg");
        var unsaved = MakePhoto("b.jpg");
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group"));
        File.Copy(saved.OriginalFilePath, GroupPath("A - Group", "a.jpg"));

        var groups = Scan(saved, unsaved);

        Assert.Empty(groups.FoldersOf(unsaved));
    }

    [Fact]
    public void FoldersOf_OrdersByKeyDigitsFirst()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        foreach (var key in "ZA9B1")
            Toggle(groups, key, photo, photo);

        var names = groups.FoldersOf(photo).Select(Path.GetFileName);

        Assert.Equal(["1 - Group", "9 - Group", "A - Group", "B - Group", "Z - Group"], names);
    }

    // ── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public void Update_AfterSaving_ListsTheFolder()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);

        var result = groups.Toggle('A', photo, [photo]);
        Assert.Empty(groups.FoldersOf(photo)); // the index only changes when the UI thread applies the result

        groups.Update(result);

        Assert.Equal([result.Folder], groups.FoldersOf(photo));
    }

    [Fact]
    public void Update_AfterAFolderWasRenamed_DropsTheOldNameAndIndexesTheNewOne()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, 'A', photo, photo, other);
        Directory.Move(Path.Combine(_likedDir, "A - Group"), Path.Combine(_likedDir, "A - Group Family"));

        Toggle(groups, 'A', other, photo, other); // any group action notices the rename

        var expected = Path.Combine(_likedDir, "A - Group Family");
        Assert.Equal([expected], groups.FoldersOf(photo));
        Assert.Equal([expected], groups.FoldersOf(other));
        Assert.Equal(["A - Group Family"], groups.GetSavedInNames(photo));
    }

    [Fact]
    public void Update_AfterAFolderWasDeleted_DropsIt()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, '1', photo, photo, other);
        Directory.Delete(Path.Combine(_likedDir, "1 - Group"), recursive: true);

        Toggle(groups, 'A', other, photo, other);

        Assert.Empty(groups.FoldersOf(photo));
    }

    [Fact]
    public void Update_AfterAGroupFolderWasAddedOutsideTheApp_IndexesIt()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Directory.CreateDirectory(Path.Combine(_likedDir, "B - Group"));
        File.Copy(photo.OriginalFilePath, GroupPath("B - Group", "a.jpg"));

        Toggle(groups, 'A', other, photo, other);

        Assert.Equal([Path.Combine(_likedDir, "B - Group")], groups.FoldersOf(photo));
    }

    [Fact]
    public void Update_KeepsTheIndexOfGroupsThatWereNotTouched()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, '1', photo, photo);

        Toggle(groups, 'A', photo, photo);

        Assert.Equal(
            [Path.Combine(_likedDir, "1 - Group"), Path.Combine(_likedDir, "A - Group")],
            groups.FoldersOf(photo));
    }

    [Fact]
    public void Update_AfterRemoving_NoLongerListsTheFolder()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);

        Toggle(groups, 'A', photo, photo);

        Assert.Empty(groups.FoldersOf(photo));
    }

    // ── IsInAnyGroup ────────────────────────────────────────────────────────

    [Fact]
    public void IsInAnyGroup_PhotoInAGroup_IsTrue()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);

        Assert.True(groups.IsInAnyGroup(photo));
    }

    [Fact]
    public void IsInAnyGroup_PhotoInOneOfSeveralGroups_IsTrue()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, 'A', other, photo, other);
        Toggle(groups, '5', photo, photo, other);

        Assert.True(groups.IsInAnyGroup(photo));
    }

    [Fact]
    public void IsInAnyGroup_PhotoNotInAnyGroup_IsFalse()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, 'A', other, photo, other);

        Assert.False(groups.IsInAnyGroup(photo));
    }

    [Fact]
    public void IsInAnyGroup_OnlyLikedInTheLikedFolder_IsFalse()
    {
        var photo = MakePhoto("a.jpg");
        photo.LikedFilePath = Path.Combine(_likedDir, "a.jpg");

        Assert.False(Scan(photo).IsInAnyGroup(photo));
    }

    [Fact]
    public void IsInAnyGroup_AfterTheCopyWasRemoved_IsFalse()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);
        Toggle(groups, 'A', photo, photo);

        Assert.False(groups.IsInAnyGroup(photo));
    }

    // ── GetLegend ───────────────────────────────────────────────────────────

    [Fact]
    public void GetLegend_NoGroupFolders_IsEmpty()
    {
        var photo = MakePhoto("a.jpg");

        Assert.Empty(Scan(photo).GetLegend(photo));
    }

    [Fact]
    public void GetLegend_ListsEveryGroupFolderInKeyOrder()
    {
        var photo = MakePhoto("a.jpg");
        foreach (var name in new[] { "B - Group", "A - Group Family", "3 - Group", "Holidays" })
            Directory.CreateDirectory(Path.Combine(_likedDir, name));

        var legend = Scan(photo).GetLegend(photo);

        Assert.Equal(['3', 'A', 'B'], legend.Select(e => e.Key));
        Assert.Equal(["3 - Group", "A - Group Family", "B - Group"], legend.Select(e => e.Name));
    }

    [Fact]
    public void GetLegend_MarksTheFoldersHoldingThePhoto()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, 'A', photo, photo, other);
        Toggle(groups, '2', other, photo, other);
        Toggle(groups, 'C', photo, photo, other);

        var legend = groups.GetLegend(photo);

        Assert.Equal([("2 - Group", false), ("A - Group", true), ("C - Group", true)],
            legend.Select(e => (e.Name, e.HasPhoto)));
    }

    [Fact]
    public void GetLegend_ListsFoldersTheCurrentPhotoIsNotIn()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, 'A', other, photo, other);

        var legend = groups.GetLegend(photo);

        Assert.Equal([new GroupLegendEntry('A', "A - Group", HasPhoto: false)], legend);
    }

    [Fact]
    public void GetLegend_NewGroup_AppearsAfterTheResultIsApplied()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);

        var result = groups.Toggle('G', photo, [photo]);
        Assert.Empty(groups.GetLegend(photo));

        groups.Update(result);

        Assert.Equal([new GroupLegendEntry('G', "G - Group", HasPhoto: true)], groups.GetLegend(photo));
    }

    [Fact]
    public void GetLegend_RenamedFolder_AppearsUnderTheNewName()
    {
        var photo = MakePhoto("a.jpg");
        var other = MakePhoto("b.jpg");
        var groups = Scan(photo, other);
        Toggle(groups, 'A', photo, photo, other);
        Directory.Move(Path.Combine(_likedDir, "A - Group"), Path.Combine(_likedDir, "A - Group Family"));

        Toggle(groups, 'B', other, photo, other); // any group action notices the rename

        Assert.Equal(["A - Group Family", "B - Group"], groups.GetLegend(photo).Select(e => e.Name));
    }

    [Fact]
    public void GetLegend_FolderLeftEmpty_StaysListed()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);
        Toggle(groups, 'A', photo, photo); // removes the copy, keeps the folder

        Assert.Equal([new GroupLegendEntry('A', "A - Group", HasPhoto: false)], groups.GetLegend(photo));
    }

    [Fact]
    public void GetLegend_SeveralFoldersForOneKey_ListsThemAll()
    {
        var photo = MakePhoto("a.jpg");
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Work"));
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Family"));

        var legend = Scan(photo).GetLegend(photo);

        Assert.Equal(["A - Group Family", "A - Group Work"], legend.Select(e => e.Name));
    }

    // ── GetSavedInNames / FormatFolderList ──────────────────────────────────

    [Fact]
    public void GetSavedInNames_ListsLikedFirstThenGroups()
    {
        var photo = MakePhoto("a.jpg");
        photo.LikedFilePath = Path.Combine(_likedDir, "a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'B', photo, photo);
        Toggle(groups, '4', photo, photo);

        Assert.Equal(["Liked", "4 - Group", "B - Group"], groups.GetSavedInNames(photo));
    }

    [Fact]
    public void GetSavedInNames_NotSavedAnywhere_IsEmpty()
    {
        var photo = MakePhoto("a.jpg");

        Assert.Empty(Scan(photo).GetSavedInNames(photo));
    }

    [Fact]
    public void FormatFolderList_WrapsEachFolderInBrackets()
    {
        Assert.Equal("[Liked] [3 - Group] [A - Group Family]",
            GroupLibrary.FormatFolderList(["Liked", "3 - Group", "A - Group Family"]));
    }

    [Fact]
    public void FormatFolderList_SingleFolder_HasNoSeparator()
    {
        Assert.Equal("[A - Group]", GroupLibrary.FormatFolderList(["A - Group"]));
    }

    [Fact]
    public void FormatFolderList_KeepNamesTogether_OnlyBreaksBetweenFolders()
    {
        var text = GroupLibrary.FormatFolderList(["Liked", "A - Group Family"], keepNamesTogether: true);

        Assert.Equal("[Liked] [A\u00A0-\u00A0Group\u00A0Family]", text);
        Assert.Equal(2, text.Split(' ').Length); // the only plain space is the separator between the two folders
    }

    [Fact]
    public void FormatFolderList_Empty_IsEmpty()
    {
        Assert.Equal(string.Empty, GroupLibrary.FormatFolderList([]));
    }

    [Fact]
    public void GetSavedInNames_OnlyLiked_NamesTheLikedFolder()
    {
        var photo = MakePhoto("a.jpg");
        photo.LikedFilePath = Path.Combine(_likedDir, "a.jpg");

        Assert.Equal(["Liked"], Scan(photo).GetSavedInNames(photo));
    }

    [Fact]
    public void GetSavedInNames_OnlyInGroup_NamesTheGroupFolder()
    {
        var photo = MakePhoto("a.jpg");
        var groups = Scan(photo);
        Toggle(groups, 'A', photo, photo);

        Assert.Equal(["A - Group"], groups.GetSavedInNames(photo));
    }

    [Fact]
    public void GetSavedInNames_LikedAndRenamedGroups_UsesTheRenamedNamesInKeyOrder()
    {
        var photo = MakePhoto("a.jpg");
        photo.LikedFilePath = Path.Combine(_likedDir, "a.jpg");
        Directory.CreateDirectory(Path.Combine(_likedDir, "A - Group Family"));
        File.Copy(photo.OriginalFilePath, GroupPath("A - Group Family", "a.jpg"));
        var groups = Scan(photo);
        Toggle(groups, '2', photo, photo);

        Assert.Equal(["Liked", "2 - Group", "A - Group Family"], groups.GetSavedInNames(photo));
    }

    [Fact]
    public void GetSavedInNames_CustomLikedFolder_UsesItsName()
    {
        var photo = MakePhoto("a.jpg");
        var customLiked = Path.Combine(_tempDir, "Favourites") + Path.DirectorySeparatorChar;
        photo.LikedFilePath = Path.Combine(customLiked, "a.jpg");
        var groups = GroupLibrary.Scan(customLiked, [photo]);

        Assert.Equal(["Favourites"], groups.GetSavedInNames(photo));
    }
}
