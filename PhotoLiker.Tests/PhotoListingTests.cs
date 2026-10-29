using System.Diagnostics;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Tests for PhotoListing (the saved list of a folder) against a real temp file system.</summary>
public class PhotoListingTests : IDisposable
{
    private static readonly string[] Extensions = [".jpg", ".png"];
    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "PLListingTests_" + Guid.NewGuid());
    private readonly string _root;

    public PhotoListingTests()
    {
        _root = Path.Combine(_temp, "Photos");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { }

        // The saved listings of this test (the folder is called Photos)
        var listings = Path.Combine(AppPaths.DataFolder, "listing");
        if (Directory.Exists(listings))
        {
            foreach (var file in Directory.GetFiles(listings, "photos-*"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private ListingKey Key(bool recursive = true, string? excluded = null, string[]? extensions = null) =>
        new(_root, recursive, extensions ?? Extensions, excluded);

    private string At(string relative) => Path.Combine(_root, relative);

    private string Put(string relative, string content = "x")
    {
        var path = At(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Gives every folder of the tree an old time. What a test then does to a folder is a change of its time whatever
    /// the resolution of the clock, and what it does not do is not.
    /// </summary>
    private void Age()
    {
        foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
            Directory.SetLastWriteTimeUtc(directory, Old);
        Directory.SetLastWriteTimeUtc(_root, Old);
    }

    private static List<string> Names(PhotoListing listing, string root) =>
        [.. listing.ToPhotoFiles(PhotoSortOrder.None).Select(p => Path.GetRelativePath(root, p.OriginalFilePath)).Order(StringComparer.OrdinalIgnoreCase)];

    private List<string> Names(PhotoListing listing) => Names(listing, _root);

    private static List<(string Path, long Length, DateTime Written, DateTime Created)> Describe(IEnumerable<PhotoFile> photos) =>
        [.. photos.Select(p => (p.OriginalFilePath, p.Length, p.LastWriteTimeUtc, p.CreationTimeUtc)).OrderBy(p => p.OriginalFilePath, StringComparer.OrdinalIgnoreCase)];

    private PhotoListing ScanAndSave(ListingKey? key = null)
    {
        var listing = PhotoListing.Scan(key ?? Key());
        listing.Save();
        return listing;
    }

    // ── Scan ────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan_ListsTheSamePhotosAsTheScanner()
    {
        Put("a.jpg", "one");
        Put("b.PNG", "two two");
        Put("notes.txt");
        Put("._junk.jpg");
        Put(Path.Combine("sub", "c.jpg"), "three");
        Put(Path.Combine("sub", "deep", "d.JPG"), "four");
        Put(Path.Combine("Liked", "l.jpg"));                       // the excluded folder
        Put(Path.Combine("Deleted", "gone.jpg"));                  // the Deleted folder of the folder
        Put(Path.Combine("@eaDir", "thumb.jpg"));                  // a NAS folder of thumbnails
        Put(Path.Combine("sub", "Deleted", "own.jpg"));            // a Deleted folder further down is the user's own
        var liked = At("Liked");

        var listing = PhotoListing.Scan(Key(excluded: liked));

        Assert.Equal(
            Describe(PhotoScanner.Scan(_root, recursive: true, Extensions, liked)),
            Describe(listing.ToPhotoFiles(PhotoSortOrder.None)));
        Assert.Equal(["a.jpg", "b.PNG", @"sub\c.jpg", @"sub\deep\d.JPG", @"sub\Deleted\own.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
    }

    [Fact]
    public void Scan_NotRecursive_ListsOnlyTheFolder()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));

        var listing = PhotoListing.Scan(Key(recursive: false));

        Assert.Equal(["a.jpg"], Names(listing));
    }

    [Fact]
    public void Scan_FolderGivenWithATrailingSeparator_GivesTheSamePaths()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));

        var listing = PhotoListing.Scan(new ListingKey(_root + Path.DirectorySeparatorChar, true, Extensions, null));

        Assert.Equal(
            Describe(PhotoScanner.Scan(_root + Path.DirectorySeparatorChar, recursive: true, Extensions, null)),
            Describe(listing.ToPhotoFiles(PhotoSortOrder.None)));
    }

    [Fact]
    public void Scan_MissingFolder_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => PhotoListing.Scan(new ListingKey(At("nothing"), true, Extensions, null)));
    }

    [Fact]
    public void Scan_BlankFolder_IsEmptyAndHasNothingToSave()
    {
        var listing = PhotoListing.Scan(new ListingKey("", true, Extensions, null));

        Assert.Empty(listing.ToPhotoFiles(PhotoSortOrder.None));
        Assert.False(listing.NeedsSave);
        listing.Save(); // does nothing
        Assert.Null(PhotoListing.Load(new ListingKey("", true, Extensions, null)));
        Assert.Equal(ListingChanges.Unreachable, listing.Verify());
    }

    [Fact]
    public void Scan_FolderWithoutPhotos_IsEmpty()
    {
        Assert.Empty(PhotoListing.Scan(Key()).ToPhotoFiles(PhotoSortOrder.None));
    }

    [Theory]
    [InlineData(PhotoSortOrder.None)]
    [InlineData(PhotoSortOrder.OldestFirst)]
    [InlineData(PhotoSortOrder.NewestFirst)]
    public void ToPhotoFiles_IsInTheOrderOfTheScanner(PhotoSortOrder order)
    {
        var made = new DateTime(2016, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        foreach (var (name, days) in new[] { ("a.jpg", 5), ("b.jpg", 1), (@"sub\c.jpg", 3), (@"sub\d.jpg", 3), (@"other\e.jpg", 2) })
            File.SetCreationTimeUtc(Put(name), made.AddDays(days));

        var listing = PhotoListing.Scan(Key());

        var expected = PhotoScanner.Scan(_root, recursive: true, Extensions, null, order).Select(p => p.OriginalFilePath);
        var actual = listing.ToPhotoFiles(order).Select(p => p.OriginalFilePath);
        if (order == PhotoSortOrder.None)
        {
            Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), actual.Order(StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ToPhotoFiles_GivesNewObjectsEachTime()
    {
        Put("a.jpg");
        var listing = PhotoListing.Scan(Key());

        var first = Assert.Single(listing.ToPhotoFiles(PhotoSortOrder.None));
        first.LikedFilePath = @"C:\Liked\a.jpg";
        var second = Assert.Single(listing.ToPhotoFiles(PhotoSortOrder.None));

        Assert.NotSame(first, second);
        Assert.Equal(string.Empty, second.LikedFilePath);
    }

    [Fact]
    public void Scan_ALinkToAFolderAbove_IsNotFollowedForEver()
    {
        if (!OperatingSystem.IsWindows())
            return; // a junction is made with mklink

        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        var link = At(Path.Combine("sub", "loop"));
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{_root}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            process.WaitForExit();
        Assert.True(Directory.Exists(link), "the test could not make a junction");

        var listing = PhotoListing.Scan(Key());

        Assert.Equal(["a.jpg", @"sub\b.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
    }

    // ── Save and Load ───────────────────────────────────────────────────────

    [Fact]
    public void SaveAndLoad_GiveTheSameListing()
    {
        Put("a.jpg", "one");
        Put(Path.Combine("sub", "b.jpg"), "two two");
        var listing = PhotoListing.Scan(Key());
        Assert.True(listing.NeedsSave);

        listing.Save();
        var loaded = PhotoListing.Load(Key());

        Assert.False(listing.NeedsSave);
        Assert.NotNull(loaded);
        Assert.False(loaded.NeedsSave);
        Assert.Equal(Describe(listing.ToPhotoFiles(PhotoSortOrder.None)), Describe(loaded.ToPhotoFiles(PhotoSortOrder.None)));
    }

    [Fact]
    public void SaveAndLoad_KeepNamesWithOddCharacters()
    {
        Put(Path.Combine("Наташин фотик", "IMG 1 [x] (2).jpg"));
        Put(Path.Combine("2015-04-18", "ü.jpg"));
        ScanAndSave();

        var loaded = PhotoListing.Load(Key())!;

        Assert.Equal(["2015-04-18\\ü.jpg", "Наташин фотик\\IMG 1 [x] (2).jpg"], Names(loaded).Select(n => n.Replace('/', '\\')));
    }

    [Fact]
    public void Load_NothingSaved_IsNull()
    {
        Put("a.jpg");

        Assert.Null(PhotoListing.Load(Key()));
    }

    [Fact]
    public void Load_ForOtherSettings_IsNull()
    {
        Put("a.jpg");
        ScanAndSave(Key(excluded: At("Liked")));

        Assert.NotNull(PhotoListing.Load(Key(excluded: At("Liked"))));
        Assert.NotNull(PhotoListing.Load(Key(excluded: At("LIKED") + Path.DirectorySeparatorChar)));         // the same folder, spelled otherwise
        Assert.NotNull(PhotoListing.Load(Key(excluded: At("Liked"), extensions: [".PNG", ".jpg"])));        // the same extensions
        Assert.Null(PhotoListing.Load(Key(excluded: At("Liked"), recursive: false)));
        Assert.Null(PhotoListing.Load(Key(excluded: At("Liked"), extensions: [".jpg"])));
        Assert.Null(PhotoListing.Load(Key(excluded: At("Favourites"))));
        Assert.Null(PhotoListing.Load(Key(excluded: null)));
    }

    [Fact]
    public void Load_SettingsSwitchedBackAndForth_EachKeepsItsOwnListing()
    {
        Put("a.jpg");
        ScanAndSave(Key(recursive: true));
        ScanAndSave(Key(recursive: false));

        // Both are kept, so switching the subfolders on and off does not list the folder again
        Assert.NotNull(PhotoListing.Load(Key(recursive: true)));
        Assert.NotNull(PhotoListing.Load(Key(recursive: false)));
        Assert.NotEqual(Key(recursive: true).CacheFile, Key(recursive: false).CacheFile);
    }

    [Fact]
    public void Load_DamagedFile_IsNull()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        ScanAndSave();
        var file = Key().CacheFile;
        var good = File.ReadAllBytes(file);

        File.WriteAllBytes(file, [.. good.Take(good.Length / 2)]);          // cut short
        Assert.Null(PhotoListing.Load(Key()));

        File.WriteAllBytes(file, [.. good, 0]);                              // something after the end
        Assert.Null(PhotoListing.Load(Key()));

        File.WriteAllBytes(file, [.. good.Take(good.Length - 1)]);          // the end marker is not whole
        Assert.Null(PhotoListing.Load(Key()));

        File.WriteAllBytes(file, [0xFF, 0xFE, 0x00, 0x13, 0x37]);           // not a listing at all
        Assert.Null(PhotoListing.Load(Key()));

        File.WriteAllBytes(file, []);                                        // empty
        Assert.Null(PhotoListing.Load(Key()));

        File.WriteAllBytes(file, good);                                      // and the real one is fine again
        Assert.NotNull(PhotoListing.Load(Key()));
    }

    [Fact]
    public void Load_AnotherVersionOfTheFile_IsNull()
    {
        Put("a.jpg");
        ScanAndSave();
        var file = Key().CacheFile;
        var bytes = File.ReadAllBytes(file);
        // BinaryWriter: a length byte (4) and "PLLS", then the version as 32 bits
        Assert.Equal(1, bytes[5]);
        bytes[5] = 2;
        File.WriteAllBytes(file, bytes);

        Assert.Null(PhotoListing.Load(Key()));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileAndReplacesTheOldOne()
    {
        Put("a.jpg");
        var listing = ScanAndSave();
        Put("b.jpg");
        var folder = Path.GetDirectoryName(Key().CacheFile)!;

        var second = PhotoListing.Scan(Key());
        second.Save();

        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        Assert.True(File.Exists(Key().CacheFile));
        Assert.Equal(["a.jpg", "b.jpg"], Names(PhotoListing.Load(Key())!));
        Assert.Equal(["a.jpg"], Names(listing));
    }

    [Fact]
    public void CacheFile_IsInTheAppDataFolder_OneFilePerFolderAndSettings()
    {
        var one = Key().CacheFile;
        var other = new ListingKey(Path.Combine(_temp, "Other"), true, Extensions, null).CacheFile;

        Assert.StartsWith(Path.Combine(AppPaths.DataFolder, "listing") + Path.DirectorySeparatorChar, one, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(one, other);
        Assert.Equal(one, Key().CacheFile);
        Assert.Equal(one, new ListingKey(_root.ToUpperInvariant() + Path.DirectorySeparatorChar, true, Extensions, null).CacheFile); // paths do not care about case
    }

    // ── Verify ──────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_NothingChanged_ChangesNothingAndHasNothingToSave()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        Age();
        var listing = ScanAndSave();

        var changes = listing.Verify();

        Assert.True(changes.Reachable);
        Assert.False(changes.HasChanges);
        Assert.Equal(0, changes.Incomplete);
        Assert.False(listing.NeedsSave);
    }

    [Fact]
    public void Verify_PhotoAdded_IsFound()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        Age();
        var listing = ScanAndSave();
        Put(Path.Combine("sub", "new.jpg"), "new");

        var changes = listing.Verify();

        Assert.Equal(1, changes.Added);
        Assert.Equal(0, changes.Removed);
        Assert.Equal(["a.jpg", @"sub\b.jpg", @"sub\new.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
        Assert.True(listing.NeedsSave);
    }

    [Fact]
    public void Verify_PhotoRemoved_IsFound()
    {
        Put("a.jpg");
        var b = Put("b.jpg");
        Age();
        var listing = ScanAndSave();
        File.Delete(b);

        var changes = listing.Verify();

        Assert.Equal(1, changes.Removed);
        Assert.Equal(["a.jpg"], Names(listing));
    }

    [Fact]
    public void Verify_PhotoRenamed_IsOneAddedAndOneRemoved()
    {
        var a = Put("a.jpg");
        Age();
        var listing = ScanAndSave();
        File.Move(a, At("z.jpg"));

        var changes = listing.Verify();

        Assert.Equal((1, 1, 0), (changes.Added, changes.Removed, changes.Changed));
        Assert.Equal(["z.jpg"], Names(listing));
    }

    [Fact]
    public void Verify_PhotoMovedToAnotherFolder_IsFoundInBoth()
    {
        var a = Put(Path.Combine("from", "a.jpg"));
        Put(Path.Combine("to", "keep.jpg"));
        Age();
        var listing = ScanAndSave();
        File.Move(a, At(Path.Combine("to", "a.jpg")));

        var changes = listing.Verify();

        Assert.Equal((1, 1, 0), (changes.Added, changes.Removed, changes.Changed));
        Assert.Equal([@"to\a.jpg", @"to\keep.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
    }

    [Fact]
    public void Verify_NewSubfolders_AreFoundThroughTheirParent()
    {
        Put("a.jpg");
        Age();
        var listing = ScanAndSave();
        Put(Path.Combine("new", "inner", "deeper", "x.jpg"));
        Put(Path.Combine("new", "y.jpg"));

        var changes = listing.Verify();

        Assert.Equal(2, changes.Added);
        Assert.Equal(["a.jpg", @"new\inner\deeper\x.jpg", @"new\y.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
    }

    [Fact]
    public void Verify_FolderDeleted_TakesItsPhotosAndItsSubfoldersWithIt()
    {
        Put("a.jpg");
        Put(Path.Combine("gone", "b.jpg"));
        Put(Path.Combine("gone", "inner", "c.jpg"));
        Age();
        var listing = ScanAndSave();
        Directory.Delete(At("gone"), recursive: true);

        var changes = listing.Verify();

        Assert.Equal(2, changes.Removed);
        Assert.Equal(["a.jpg"], Names(listing));
        Assert.True(listing.NeedsSave);
    }

    [Fact]
    public void Verify_DeepFolderDeleted_IsFoundThroughItsParentToo()
    {
        Put(Path.Combine("a", "b", "c", "x.jpg"));
        Put(Path.Combine("a", "keep.jpg"));
        Age();
        var listing = ScanAndSave();
        Directory.Delete(At(Path.Combine("a", "b")), recursive: true);

        var changes = listing.Verify();

        Assert.Equal(1, changes.Removed);
        Assert.Equal([@"a\keep.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
    }

    [Fact]
    public void Verify_FolderListedAgain_KeepsItsNewTime()
    {
        Put("a.jpg");
        Age();
        var listing = ScanAndSave();
        Put("b.jpg");
        listing.Verify();
        listing.Save();

        var again = listing.Verify();

        Assert.False(again.HasChanges);
        Assert.False(listing.NeedsSave);
    }

    [Fact]
    public void Verify_AFreshRunWithTheSavedListing_FindsWhatChangedBetweenRuns()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        Age();
        ScanAndSave();
        Put(Path.Combine("sub", "c.jpg"));

        var loaded = PhotoListing.Load(Key())!;
        var changes = loaded.Verify();

        Assert.Equal(1, changes.Added);
        Assert.Equal(["a.jpg", @"sub\b.jpg", @"sub\c.jpg"], Names(loaded).Select(n => n.Replace('/', '\\')));
    }

    [Fact]
    public void Verify_PhotoChangedInPlace_IsNotNoticed()
    {
        // By design: a folder's time does not move when a file inside is rewritten. An archive no other program touches does not need it
        var a = Put("a.jpg", "short");
        Age();
        var listing = ScanAndSave();
        File.WriteAllText(a, "a much longer content than before");

        var changes = listing.Verify();

        Assert.False(changes.HasChanges);
        Assert.Equal(5, Assert.Single(listing.ToPhotoFiles(PhotoSortOrder.None)).Length);
        Assert.Equal(33, Assert.Single(PhotoListing.Scan(Key()).ToPhotoFiles(PhotoSortOrder.None)).Length); // a new listing has it
    }

    [Fact]
    public void Verify_ExcludedFolders_AreNotLookedAt()
    {
        Put("a.jpg");
        Put(Path.Combine("Liked", "l.jpg"));
        Age();
        var liked = At("Liked");
        var listing = ScanAndSave(Key(excluded: liked));
        Put(Path.Combine("Liked", "more.jpg"));
        Put(Path.Combine("Deleted", "moved.jpg"));

        var changes = listing.Verify();

        Assert.False(changes.HasChanges);
        Assert.Equal(["a.jpg"], Names(listing));
    }

    [Fact]
    public void Verify_FolderNotReachable_ChangesNothing()
    {
        Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        var listing = ScanAndSave();
        Directory.Delete(_root, recursive: true); // a share that is off looks like this

        var changes = listing.Verify();

        Assert.False(changes.Reachable);
        Assert.False(changes.HasChanges);
        Assert.Equal(["a.jpg", @"sub\b.jpg"], Names(listing).Select(n => n.Replace('/', '\\')));
        Assert.False(listing.NeedsSave);
    }

    [Fact]
    public void Verify_Cancelled_LeavesTheListingAsItWas()
    {
        Put("a.jpg");
        Age();
        var listing = ScanAndSave();
        Put("b.jpg");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => listing.Verify(cancelled.Token));

        Assert.Equal(["a.jpg"], Names(listing));
        Assert.Equal(1, listing.Verify().Added); // and it can be done afterwards
    }

    [Fact]
    public void Verify_ManyFolders_FindsEveryChange()
    {
        for (var folder = 0; folder < 60; folder++)
        {
            for (var photo = 0; photo < 5; photo++)
                Put(Path.Combine($"f{folder:00}", $"p{photo}.jpg"));
        }
        Age();
        var listing = ScanAndSave();
        for (var folder = 0; folder < 60; folder += 7)
            File.Delete(At(Path.Combine($"f{folder:00}", "p0.jpg")));
        Put(Path.Combine("f13", "added.jpg"));
        Directory.Delete(At("f59"), recursive: true);

        var changes = listing.Verify();

        Assert.Equal(1, changes.Added);
        Assert.Equal(9 + 5, changes.Removed); // p0 of nine folders, and the five photos of the folder that is gone
        Assert.Equal(300 - 14 + 1, listing.ToPhotoFiles(PhotoSortOrder.None).Count);
    }

    // ── Remove ──────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_TakesThePhotoOutOfTheListing()
    {
        var a = Put("a.jpg");
        Put(Path.Combine("sub", "b.jpg"));
        var listing = ScanAndSave();
        Assert.False(listing.NeedsSave);

        listing.Remove(a);
        listing.Remove(At(Path.Combine("sub", "B.JPG"))); // names do not care about case

        Assert.Empty(listing.ToPhotoFiles(PhotoSortOrder.None));
        Assert.True(listing.NeedsSave);
        listing.Save();
        Assert.Empty(PhotoListing.Load(Key())!.ToPhotoFiles(PhotoSortOrder.None));
    }

    [Fact]
    public void Remove_APhotoThatIsNotThere_ChangesNothing()
    {
        Put("a.jpg");
        var listing = ScanAndSave();

        listing.Remove(At("nothing.jpg"));
        listing.Remove(At(Path.Combine("nowhere", "a.jpg")));
        listing.Remove(Path.Combine(_temp, "Elsewhere", "a.jpg"));
        listing.Remove(@"C:\a.jpg");

        Assert.Equal(["a.jpg"], Names(listing));
        Assert.False(listing.NeedsSave);
    }

    [Fact]
    public void Remove_ThenTheFileReallyGone_TheNextVerifyFindsNothingToChange()
    {
        Put("a.jpg");
        var b = Put("b.jpg");
        Age();
        var listing = ScanAndSave();

        listing.Remove(b);
        File.Delete(b);
        var changes = listing.Verify();

        Assert.False(changes.HasChanges);
        Assert.Equal(["a.jpg"], Names(listing));
        Assert.True(listing.NeedsSave); // the folder has a new time to keep
    }

    [Fact]
    public void Remove_ThePhotoIsStillOnDiskWhenTheFolderIsListedAgain_TheListingShowsItAgain()
    {
        // The app moved it, but a share showed the old list of the folder: the next run, with the real time, sorts it out
        Put("a.jpg");
        var b = Put("b.jpg");
        Age();
        var listing = ScanAndSave();
        Put("c.jpg");
        listing.Remove(b);

        listing.Verify();

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], Names(listing));
    }
}
