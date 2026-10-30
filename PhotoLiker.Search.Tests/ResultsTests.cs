using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

/// <summary>What a search shows: every match (no limit), each photo once, in the order of the SortOrder setting.</summary>
public class ResultsTests : IDisposable
{
    private const int Many = 520; // more than the 500 a search once stopped at

    private readonly TempFolder _photos = new();
    private readonly FolderIndex _search;
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));
    private int _added;

    public ResultsTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A photo of a size of its own unless the size is given: photos of one size and time are copies of one photo.</summary>
    private IndexedPhoto Add(string path, float[] clip, long? length = null, long modifiedSeconds = 1000, DateTime? taken = null, float[]? face = null)
    {
        var photo = new IndexedPhoto(path, length ?? 1000 + _added++, modifiedSeconds * TimeSpan.TicksPerSecond)
        {
            Clip = clip,
            ClipVersion = 1,
            FaceVersion = 1,
            Date = new PhotoDate(taken, taken is null ? DateSource.None : DateSource.Exif, PhotoDates.Version),
        };
        if (face is not null)
            photo.Faces = [new IndexedFace(photo, 0.1f, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, face)];
        _search.Index.Save([photo]);
        return photo;
    }

    // ── no limit ───────────────────────────────────────────────────────────

    [Fact]
    public void MoreLikeThis_ListsEveryAlikePhoto_WithoutALimit()
    {
        for (var i = 0; i < Many; i++)
            Add($"p{i:D4}.jpg", Vectors.Unit(1, 0));

        Assert.Equal(Many, _search.SearchSimilar(FullPath("p0000.jpg")).Hits.Count);
    }

    [Fact]
    public void ThePhotosOfAPerson_AreAllListed_WithoutALimit()
    {
        IndexedPhoto? first = null;
        for (var i = 0; i < Many; i++)
        {
            var photo = Add($"p{i:D4}.jpg", Vectors.Unit(0, 1), face: Vectors.Unit(1, 0));
            first ??= photo;
        }

        Assert.Equal(Many, _search.SearchPerson(first!.Faces[0], "Mom").Hits.Count);
    }

    // ── each photo once ────────────────────────────────────────────────────

    [Fact]
    public void CopiesOfOnePhoto_ComeOnce_AndThePhotoThatIsNotASavedCopyStays()
    {
        Add("b-folder/a.jpg", Vectors.Unit(1, 0), length: 5000);
        Add("Liked/A - Group/a [3f9a1c2e].jpg", Vectors.Unit(1, 0), length: 5000);
        Add("a-folder/a.jpg", Vectors.Unit(1, 0), length: 5000);
        Add("other.jpg", Vectors.Unit(0, 1), length: 6000);

        var result = _search.SearchSimilar(FullPath("b-folder/a.jpg"));

        // The folders sort a-folder first; the copy in the group folder has the id in its name, and never wins
        Assert.Equal([FullPath("a-folder/a.jpg")], result.Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void WhichCopyStays_DoesNotDependOnTheOrderTheIndexHoldsThem()
    {
        Add("z/a.jpg", Vectors.Unit(1, 0), length: 5000);
        Add("m/a.jpg", Vectors.Unit(1, 0), length: 5000);
        Add("c/a.jpg", Vectors.Unit(1, 0), length: 5000);

        Assert.Equal([FullPath("c/a.jpg")], _search.SearchSimilar(FullPath("z/a.jpg")).Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void TwoPhotosOfOneSizeAndTime_ThatShowDifferentPictures_AreBoth()
    {
        // The frames of a burst of bitmaps have the size of a bitmap and the second of the camera
        Add("frame1.bmp", Vectors.Unit(1, 0), length: 5000);
        Add("frame2.bmp", Vectors.Unit(1, 0.05f), length: 5000);

        Assert.Equal(2, _search.SearchSimilar(FullPath("frame1.bmp")).Hits.Count);
    }

    [Theory]
    [InlineData(5001, 1000)] // another size
    [InlineData(5000, 1001)] // another second
    public void PhotosOfAnotherSizeOrTime_AreNotCopies(long length, long seconds)
    {
        Add("one.jpg", Vectors.Unit(1, 0), length: 5000, modifiedSeconds: 1000);
        Add("two.jpg", Vectors.Unit(1, 0), length: length, modifiedSeconds: seconds);

        Assert.Equal(2, _search.SearchSimilar(FullPath("one.jpg")).Hits.Count);
    }

    [Fact]
    public void ThePhotosOfAPerson_ComeOncePerPhoto()
    {
        var first = Add("one/a.jpg", Vectors.Unit(0, 1), length: 5000, face: Vectors.Unit(1, 0));
        Add("two/a.jpg", Vectors.Unit(0, 1), length: 5000, face: Vectors.Unit(1, 0));

        Assert.Single(_search.SearchPerson(first.Faces[0], "Mom").Hits);
    }

    [Fact]
    public void ADateSearch_ListsCopiesOnce()
    {
        var taken = new DateTime(2009, 9, 1);
        Add("one/a.jpg", Vectors.Unit(1), length: 5000, taken: taken);
        Add("two/a.jpg", Vectors.Unit(1), length: 5000, taken: taken);
        Add("b.jpg", Vectors.Unit(1), taken: taken);

        var result = _search.Search(_models, "2009");

        Assert.Equal([FullPath("b.jpg"), FullPath("one/a.jpg")], result.Hits.Select(h => h.FullPath).Order(StringComparer.OrdinalIgnoreCase));
    }

    // ── the order ──────────────────────────────────────────────────────────

    private static SearchHit Hit(string path, DateTime? taken, float score = 0.5f, DateTime? modified = null) =>
        new(path, 10, modified ?? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), score, DateSource.Exif, taken);

    private static SearchResult Found(params SearchHit[] hits) => new("beach", hits, hits.Length);

    [Fact]
    public void OldestFirst_OrdersTheResultsByTheDateTheyWereTaken()
    {
        var result = Found(
            Hit("best.jpg", new DateTime(2012, 5, 1), score: 0.9f),
            Hit("old.jpg", new DateTime(2003, 5, 1), score: 0.3f),
            Hit("middle.jpg", new DateTime(2009, 5, 1), score: 0.6f));

        Assert.Equal(["old.jpg", "middle.jpg", "best.jpg"], result.OrderedBy(PhotoSortOrder.OldestFirst).Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void NewestFirst_IsOldestFirstBackwards_AlsoForPhotosOfOneMoment()
    {
        var moment = new DateTime(2009, 5, 1, 12, 0, 0);
        var result = Found(Hit("b.jpg", moment), Hit("c.jpg", moment), Hit("a.jpg", moment), Hit("new.jpg", moment.AddDays(1)));

        var oldest = result.OrderedBy(PhotoSortOrder.OldestFirst).Hits.Select(h => h.FullPath).ToList();
        var newest = result.OrderedBy(PhotoSortOrder.NewestFirst).Hits.Select(h => h.FullPath).ToList();

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg", "new.jpg"], oldest);
        Assert.Equal(Enumerable.Reverse(oldest), newest);
    }

    [Fact]
    public void ByName_OrdersTheResultsByTheNameOfTheFile_NotByDateOrScore()
    {
        var result = Found(
            Hit(@"C:\p\IMG_10.jpg", new DateTime(2001, 5, 1), score: 0.9f),
            Hit(@"C:\p\IMG_2.jpg", new DateTime(2012, 5, 1), score: 0.3f),
            Hit(@"C:\p\IMG_1.jpg", new DateTime(2009, 5, 1), score: 0.6f));

        Assert.Equal([@"C:\p\IMG_1.jpg", @"C:\p\IMG_2.jpg", @"C:\p\IMG_10.jpg"], result.OrderedBy(PhotoSortOrder.ByName).Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void Random_ShufflesTheResults_AndKeepsEveryHitAndWhatElseTheResultSays()
    {
        var hits = Enumerable.Range(0, 12).Select(i => Hit($"p{i:00}.jpg", new DateTime(2009, 5, 1).AddDays(i))).ToArray();
        var result = new SearchResult("beach", hits, 99, Weak: true);

        var orders = Enumerable.Range(0, 6).Select(_ => result.OrderedBy(PhotoSortOrder.Random)).ToList();

        Assert.All(orders, shuffled =>
        {
            Assert.Equal(hits.Select(h => h.FullPath), shuffled.Hits.Select(h => h.FullPath).Order(StringComparer.Ordinal));
            Assert.Equal(("beach", 99, true), (shuffled.Description, shuffled.SearchedPhotos, shuffled.Weak));
        });
        Assert.True(orders.Select(r => string.Join(",", r.Hits.Select(h => h.FullPath))).Distinct().Count() > 1);
        Assert.Equal(hits.Select(h => h.FullPath), result.Hits.Select(h => h.FullPath)); // the result itself keeps its order
    }

    [Fact]
    public void NoSortOrder_KeepsTheOrderOfTheSearch()
    {
        var result = Found(Hit("best.jpg", new DateTime(2012, 5, 1), 0.9f), Hit("old.jpg", new DateTime(2003, 5, 1), 0.3f));

        Assert.Same(result, result.OrderedBy(PhotoSortOrder.None));
    }

    [Fact]
    public void APhotoWithoutADate_GoesByTheTimeItsFileWasWritten()
    {
        var result = Found(
            Hit("dated.jpg", new DateTime(2009, 5, 1)),
            Hit("undated-old.jpg", null, modified: new DateTime(2001, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
            Hit("undated-new.jpg", null, modified: new DateTime(2015, 1, 1, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(["undated-old.jpg", "dated.jpg", "undated-new.jpg"], result.OrderedBy(PhotoSortOrder.OldestFirst).Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void Ordering_KeepsWhatElseTheResultSays()
    {
        var result = new SearchResult("beach, 2009", [Hit("b.jpg", new DateTime(2009, 1, 1)), Hit("a.jpg", new DateTime(2008, 1, 1))], 77, Weak: true);

        var ordered = result.OrderedBy(PhotoSortOrder.OldestFirst);

        Assert.Equal("beach, 2009", ordered.Description);
        Assert.Equal(77, ordered.SearchedPhotos);
        Assert.True(ordered.Weak);
    }

    [Fact]
    public void ASearchResult_CarriesTheDateTheIndexHas()
    {
        var taken = new DateTime(2009, 9, 1, 14, 30, 0);
        Add("a.jpg", Vectors.Unit(1, 0), taken: taken);

        Assert.Equal(taken, Assert.Single(_search.SearchSimilar(FullPath("a.jpg")).Hits).Taken);
    }
}
