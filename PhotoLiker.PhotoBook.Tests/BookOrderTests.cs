namespace PhotoLiker.PhotoBook.Tests;

public class BookOrderTests
{
    private static List<char> Letters(string text) => [.. text];

    private static string Show(List<char> items) => new([.. items]);

    // ── Move: dragging a block to a place ───────────────────────────────────

    [Fact]
    public void Move_OnePhotoLater_GoesInFrontOfThePlaceAsItWasListed()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Move(items, [0], 3); // A in front of D

        Assert.Equal("BCADE", Show(items));
        Assert.Equal([2], now);
    }

    [Fact]
    public void Move_OnePhotoEarlier()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Move(items, [3], 1); // D in front of B

        Assert.Equal("ADBCE", Show(items));
        Assert.Equal([1], now);
    }

    [Fact]
    public void Move_ToTheEnd_IsTheCountOfTheList()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Move(items, [1], 5);

        Assert.Equal("ACDEB", Show(items));
        Assert.Equal([4], now);
    }

    [Fact]
    public void Move_ABlock_KeepsItsOrder_AndGapsAreClosed()
    {
        var items = Letters("ABCDEF");

        var now = BookOrder.Move(items, [4, 1], 3); // B and E in front of D

        Assert.Equal("ACBEDF", Show(items));
        Assert.Equal([2, 3], now);
    }

    [Theory]
    [InlineData(1)] // in front of itself
    [InlineData(2)] // behind itself
    public void Move_ToWhereItIs_ChangesNothing(int place)
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Move(items, [1], place);

        Assert.Equal("ABCDE", Show(items));
        Assert.Equal([1], now);
    }

    [Fact]
    public void Move_ABlockIntoItself_ChangesNothing()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Move(items, [1, 2, 3], 2);

        Assert.Equal("ABCDE", Show(items));
        Assert.Equal([1, 2, 3], now);
    }

    [Fact]
    public void Move_WithPlacesThatAreNotThere_IgnoresThemAndClamps()
    {
        var items = Letters("ABC");

        var now = BookOrder.Move(items, [-3, 0, 17], 99);

        Assert.Equal("BCA", Show(items));
        Assert.Equal([2], now);
    }

    [Fact]
    public void Move_NothingSelected_ChangesNothing()
    {
        var items = Letters("ABC");

        Assert.Empty(BookOrder.Move(items, [], 1));
        Assert.Equal("ABC", Show(items));
    }

    [Fact]
    public void Move_NeverLosesOrDoublesAPhoto()
    {
        var random = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            var items = Enumerable.Range(0, 12).ToList();
            var selected = Enumerable.Range(0, 12).Where(_ => random.Next(3) == 0).ToList();

            var now = BookOrder.Move(items, selected, random.Next(0, 13));

            Assert.Equal(Enumerable.Range(0, 12), items.Order());
            Assert.Equal(selected.Order(), now.Select(index => items[index]).Order()); // the places returned are those of the photos that were moved
        }
    }

    // ── Nudge: one step earlier or later ────────────────────────────────────

    [Fact]
    public void Nudge_Later_MovesEachPhotoOnePlace()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Nudge(items, [1], 1);

        Assert.Equal("ACBDE", Show(items));
        Assert.Equal([2], now);
    }

    [Fact]
    public void Nudge_Earlier_MovesEachPhotoOnePlace()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Nudge(items, [1], -1);

        Assert.Equal("BACDE", Show(items));
        Assert.Equal([0], now);
    }

    [Fact]
    public void Nudge_ABlock_MovesTogether()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Nudge(items, [1, 2], 1);

        Assert.Equal("ADBCE", Show(items));
        Assert.Equal([2, 3], now);
    }

    [Fact]
    public void Nudge_PhotosApart_EachStepsPastItsNeighbour()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Nudge(items, [0, 2], 1);

        Assert.Equal("BADCE", Show(items));
        Assert.Equal([1, 3], now);
    }

    [Fact]
    public void Nudge_AtTheEnd_StaysAndTheOthersCloseUpBehindIt()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.Nudge(items, [2, 4], 1);

        Assert.Equal("ABDCE", Show(items));
        Assert.Equal([3, 4], now);
    }

    [Fact]
    public void Nudge_ThePhotoAtTheStart_CannotGoEarlier()
    {
        var items = Letters("ABC");

        var now = BookOrder.Nudge(items, [0], -1);

        Assert.Equal("ABC", Show(items));
        Assert.Equal([0], now);
    }

    [Fact]
    public void Nudge_SeveralPlaces_IsSeveralSteps()
    {
        var items = Letters("ABCDEF");

        var now = BookOrder.Nudge(items, [1], 3);

        Assert.Equal("ACDEBF", Show(items)); // B past C, D and E
        Assert.Equal([4], now);
    }

    [Fact]
    public void Nudge_NeverLosesAPhoto()
    {
        var random = new Random(11);
        for (var round = 0; round < 200; round++)
        {
            var items = Enumerable.Range(0, 10).ToList();
            var selected = Enumerable.Range(0, 10).Where(_ => random.Next(3) == 0).ToList();

            BookOrder.Nudge(items, selected, random.Next(-4, 5));

            Assert.Equal(Enumerable.Range(0, 10), items.Order());
        }
    }

    // ── To the start, to the end, to a place, reverse ───────────────────────

    [Fact]
    public void ToStart_AndToEnd()
    {
        var items = Letters("ABCDE");

        Assert.Equal([0, 1], BookOrder.ToStart(items, [3, 1]));
        Assert.Equal("BDACE", Show(items));

        Assert.Equal([3, 4], BookOrder.ToEnd(items, [0, 2])); // B and A, in the order they have, behind D C E
        Assert.Equal("DCEBA", Show(items));
    }

    [Fact]
    public void ToEnd_KeepsTheOrderOfTheMovedAndOfTheOthers()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.ToEnd(items, [0, 2]);

        Assert.Equal("BDEAC", Show(items));
        Assert.Equal([3, 4], now);
    }

    [Fact]
    public void ToPlace_PutsTheFirstOfThemAtThatNumber_CountingFromOne()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.ToPlace(items, [0], 3);

        Assert.Equal("BCADE", Show(items));
        Assert.Equal([2], now);
    }

    [Fact]
    public void ToPlace_ABlock_StaysTogether_AndIsKeptInsideTheList()
    {
        var items = Letters("ABCDE");

        var now = BookOrder.ToPlace(items, [0, 1], 99);

        Assert.Equal("CDEAB", Show(items));
        Assert.Equal([3, 4], now);
    }

    [Fact]
    public void ToPlace_NumberZeroOrLess_IsTheStart()
    {
        var items = Letters("ABCDE");

        BookOrder.ToPlace(items, [4], -5);

        Assert.Equal("EABCD", Show(items));
    }

    [Fact]
    public void Reverse_TurnsTheListAround()
    {
        var items = Letters("ABC");

        BookOrder.Reverse(items);

        Assert.Equal("CBA", Show(items));
    }

    // ── Arrange: where the photos start ─────────────────────────────────────

    private static PhotoFile Photo(string path, int day = 1) => new(path, string.Empty)
    {
        CreationTimeUtc = new DateTime(2020, 1, day, 0, 0, 0, DateTimeKind.Utc),
        LastWriteTimeUtc = new DateTime(2020, 1, day, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Arrange_WithoutASavedBook_IsTheSortOrder()
    {
        PhotoFile[] listed = [Photo(@"C:\p\b.jpg", 3), Photo(@"C:\p\a.jpg", 2), Photo(@"C:\p\c.jpg", 1)];

        var byDate = BookOrder.Arrange(listed, null, PhotoSortOrder.OldestFirst);
        var byName = BookOrder.Arrange(listed, null, PhotoSortOrder.ByName);

        Assert.Equal([@"C:\p\c.jpg", @"C:\p\a.jpg", @"C:\p\b.jpg"], byDate.Select(entry => entry.Path));
        Assert.Equal([@"C:\p\a.jpg", @"C:\p\b.jpg", @"C:\p\c.jpg"], byName.Select(entry => entry.Path));
    }

    [Fact]
    public void Arrange_TheSavedOrderWinsOverTheSortOrder_AndNewPhotosGoToTheEnd()
    {
        PhotoFile[] listed = [Photo(@"C:\p\a.jpg", 1), Photo(@"C:\p\b.jpg", 2), Photo(@"C:\p\c.jpg", 3), Photo(@"C:\p\new2.jpg", 5), Photo(@"C:\p\new1.jpg", 4)];
        BookEntry[] saved = [new(@"C:\p\c.jpg") { AboveRtf = "c-above" }, new(@"C:\p\a.jpg") { BelowRtf = "a-below", LeftOut = true }, new(@"C:\p\b.jpg")];

        var book = BookOrder.Arrange(listed, saved, PhotoSortOrder.OldestFirst);

        Assert.Equal([@"C:\p\c.jpg", @"C:\p\a.jpg", @"C:\p\b.jpg", @"C:\p\new1.jpg", @"C:\p\new2.jpg"], book.Select(entry => entry.Path));
        Assert.Equal("c-above", book[0].AboveRtf);
        Assert.Equal("a-below", book[1].BelowRtf);
        Assert.True(book[1].LeftOut);
        Assert.False(book[3].LeftOut);
    }

    [Fact]
    public void Arrange_APhotoThatIsGone_IsLeftOut_WithItsText()
    {
        PhotoFile[] listed = [Photo(@"C:\p\a.jpg")];
        BookEntry[] saved = [new(@"C:\p\gone.jpg") { AboveRtf = "lost" }, new(@"C:\p\a.jpg")];

        var book = BookOrder.Arrange(listed, saved, PhotoSortOrder.None);

        Assert.Equal([@"C:\p\a.jpg"], book.Select(entry => entry.Path));
    }

    [Fact]
    public void Arrange_APhotoSavedTwice_IsInTheBookOnce()
    {
        PhotoFile[] listed = [Photo(@"C:\p\a.jpg"), Photo(@"C:\p\b.jpg")];
        BookEntry[] saved = [new(@"C:\p\a.jpg"), new(@"C:\P\A.JPG")];

        var book = BookOrder.Arrange(listed, saved, PhotoSortOrder.ByName);

        Assert.Equal(2, book.Count);
    }

    [Fact]
    public void Arrange_TheTurnOfASavedPhoto_Stays()
    {
        PhotoFile[] listed = [Photo(@"C:\p\a.jpg"), Photo(@"C:\p\b.jpg")];
        BookEntry[] saved = [new(@"C:\p\b.jpg") { Rotation = 180 }];

        var book = BookOrder.Arrange(listed, saved, PhotoSortOrder.ByName);

        Assert.Equal([@"C:\p\b.jpg", @"C:\p\a.jpg"], book.Select(entry => entry.Path));
        Assert.Equal([180, 0], book.Select(entry => entry.Rotation));
    }

    [Fact]
    public void Arrange_Videos_AreNotPhotos()
    {
        PhotoFile[] listed = [Photo(@"C:\p\a.jpg"), Photo(@"C:\p\clip.mp4")];

        var book = BookOrder.Arrange(listed, null, PhotoSortOrder.ByName);

        Assert.Equal([@"C:\p\a.jpg"], book.Select(entry => entry.Path));
    }

    [Fact]
    public void Arrange_TheSavedPathIsMatchedWithoutRegardToCase_AndTakesTheSpellingOfTheListing()
    {
        PhotoFile[] listed = [Photo(@"C:\p\Photo.JPG")];
        BookEntry[] saved = [new(@"c:\P\photo.jpg") { AboveRtf = "x" }];

        var book = BookOrder.Arrange(listed, saved, PhotoSortOrder.None);

        var entry = Assert.Single(book);
        Assert.Equal(@"C:\p\Photo.JPG", entry.Path);
        Assert.Equal("x", entry.AboveRtf);
    }

    // ── Sort: by the files ──────────────────────────────────────────────────

    [Fact]
    public void Sort_ByName_UsesTheNaturalOrderOfNames()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PhotoBookSort_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var entries = new[] { "IMG_10.jpg", "IMG_2.jpg", "IMG_1.jpg" }.Select(name => new BookEntry(Path.Combine(folder, name))).ToList();
            foreach (var entry in entries)
                File.WriteAllText(entry.Path, "x");

            BookOrder.Sort(entries, PhotoSortOrder.ByName);

            Assert.Equal(["IMG_1.jpg", "IMG_2.jpg", "IMG_10.jpg"], entries.Select(entry => Path.GetFileName(entry.Path)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Sort_ByDate_AndNewestFirst()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PhotoBookSort_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var entries = new List<BookEntry>();
            foreach (var (name, day) in new[] { ("b.jpg", 3), ("a.jpg", 1), ("c.jpg", 2) })
            {
                var path = Path.Combine(folder, name);
                File.WriteAllText(path, "x");
                File.SetCreationTimeUtc(path, new DateTime(2020, 5, day, 0, 0, 0, DateTimeKind.Utc));
                entries.Add(new BookEntry(path) { AboveRtf = name });
            }

            BookOrder.Sort(entries, PhotoSortOrder.OldestFirst);
            Assert.Equal(["a.jpg", "c.jpg", "b.jpg"], entries.Select(entry => entry.AboveRtf));

            BookOrder.Sort(entries, PhotoSortOrder.NewestFirst);
            Assert.Equal(["b.jpg", "c.jpg", "a.jpg"], entries.Select(entry => entry.AboveRtf));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Sort_None_LeavesTheOrder_AndAFileThatIsGoneIsStillInTheBook()
    {
        var entries = new List<BookEntry> { new(@"C:\nowhere\b.jpg"), new(@"C:\nowhere\a.jpg") };

        BookOrder.Sort(entries, PhotoSortOrder.None);
        Assert.Equal(["b.jpg", "a.jpg"], entries.Select(entry => Path.GetFileName(entry.Path)));

        BookOrder.Sort(entries, PhotoSortOrder.ByName);
        Assert.Equal(2, entries.Count);
        Assert.Equal(["a.jpg", "b.jpg"], entries.Select(entry => Path.GetFileName(entry.Path)));
    }
}
