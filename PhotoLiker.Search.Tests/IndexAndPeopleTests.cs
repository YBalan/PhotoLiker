namespace PhotoLiker.Search.Tests;

public class PhotoIndexTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();
    private string IndexFile => Path.Combine(_data.Path, "index.db");

    public void Dispose()
    {
        _photos.Dispose();
        _data.Dispose();
    }

    private static IndexedPhoto Photo(string path, float[]? clip, params (float X, float Y, float W, float H, float[] Embedding)[] faces)
    {
        var photo = new IndexedPhoto(path, length: 1000, modifiedTicks: 5000)
        {
            Clip = clip,
            ClipVersion = 1,
            FaceVersion = 1,
        };
        photo.Faces = [.. faces.Select(f => new IndexedFace(photo, f.X, f.Y, f.W, f.H, 0.9f, manual: false, f.Embedding))];
        return photo;
    }

    [Fact]
    public void WhatWasSaved_IsThereAfterReopening()
    {
        var clip = Vectors.Unit(1, 2, 3);
        var face = Vectors.Unit(3, 2, 1);
        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
            index.Save([Photo("a/one.jpg", clip, (0.1f, 0.2f, 0.3f, 0.4f, face))]);

        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);

        var photo = reopened.Find("a/one.jpg");
        Assert.NotNull(photo);
        Assert.Equal(1000, photo.Length);
        Assert.Equal(5000, photo.ModifiedTicks);
        Assert.Equal(clip, photo.Clip);
        var saved = Assert.Single(photo.Faces);
        Assert.Equal(0.3f, saved.Width);
        Assert.Equal(face, saved.Embedding);
        Assert.Same(photo, saved.Photo);
    }

    [Fact]
    public void SavingAPhotoAgain_ReplacesItAndItsFaces()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        index.Save([Photo("one.jpg", Vectors.Unit(1), (0, 0, 0.1f, 0.1f, Vectors.Unit(1)), (0.5f, 0.5f, 0.1f, 0.1f, Vectors.Unit(2)))]);

        index.Save([Photo("one.jpg", Vectors.Unit(5), (0, 0, 0.2f, 0.2f, Vectors.Unit(3)))]);

        var photo = index.Find("one.jpg")!;
        Assert.Equal(Vectors.Unit(5), photo.Clip);
        Assert.Single(photo.Faces);
        Assert.Equal(1, index.FaceCount);
        Assert.Equal(1, index.PhotoCount);
    }

    [Fact]
    public void Paths_AreFoundWithoutCaringAboutCase_AndSeparator()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        index.Save([Photo("Sub/Photo.JPG", Vectors.Unit(1))]);

        Assert.NotNull(index.Find("sub/photo.jpg"));
        Assert.NotNull(index.FindFullPath(Path.Combine(_photos.Path, "Sub", "Photo.JPG")));
        Assert.Null(index.FindFullPath(Path.Combine(Path.GetTempPath(), "somewhere else.jpg")));
    }

    [Fact]
    public void RelativePaths_UseSlashes_AndRefuseFilesOutsideTheFolder()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);

        Assert.True(index.TryGetRelativePath(Path.Combine(_photos.Path, "a", "b.jpg"), out var relative));
        Assert.Equal("a/b.jpg", relative);
        Assert.False(index.TryGetRelativePath(Path.Combine(_photos.Path, "..", "outside.jpg"), out _));
        Assert.Equal(Path.Combine(_photos.Path, "a", "b.jpg"), index.ToFullPath("a/b.jpg"));
    }

    [Fact]
    public void Remove_DropsThePhotoAndItsFaces()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        index.Save([Photo("one.jpg", Vectors.Unit(1), (0, 0, 0.1f, 0.1f, Vectors.Unit(1))), Photo("two.jpg", Vectors.Unit(2))]);

        index.Remove(["one.jpg"]);

        Assert.Null(index.Find("one.jpg"));
        Assert.Equal(0, index.FaceCount);
        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);
        Assert.Equal(1, reopened.PhotoCount);
        Assert.Equal(0, reopened.FaceCount);
    }

    [Fact]
    public void AddFaceAndRemoveFace_ChangeThePhoto_AndSurviveReopening()
    {
        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
        {
            index.Save([Photo("one.jpg", Vectors.Unit(1), (0, 0, 0.1f, 0.1f, Vectors.Unit(1)))]);
            var photo = index.Find("one.jpg")!;

            index.AddFace(new IndexedFace(photo, 0.5f, 0.5f, 0.2f, 0.2f, 0f, manual: true, Vectors.Unit(7)));
            Assert.Equal(2, photo.Faces.Count);

            index.RemoveFace(photo.Faces[0]);
            Assert.Single(photo.Faces);
        }

        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);
        var face = Assert.Single(reopened.Find("one.jpg")!.Faces);
        Assert.True(face.Manual);
        Assert.Equal(Vectors.Unit(7), face.Embedding);
    }

    [Fact]
    public void APhotoThatFailed_IsRememberedAsFailed_WithoutAVector()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        index.Save([new IndexedPhoto("broken.jpg", 1, 1) { Failure = "not a picture", ClipVersion = 1, FaceVersion = 1 }]);

        Assert.Equal(1, index.FailedCount);
        Assert.Equal(0, index.PhotoCount);
        Assert.Equal("not a picture", index.Find("broken.jpg")!.Failure);
    }

    [Fact]
    public void TheIndexFileOfAFolder_IsStableAndDiffersPerFolder()
    {
        var one = SearchPaths.IndexFile(_photos.Path);

        Assert.Equal(one, SearchPaths.IndexFile(_photos.Path + Path.DirectorySeparatorChar));
        Assert.Equal(one, SearchPaths.IndexFile(_photos.Path.ToUpperInvariant()));
        Assert.NotEqual(one, SearchPaths.IndexFile(_data.Path));
    }
}

public class PeopleLibraryTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static IndexedFace Face(string photoPath, float x, float y, float size, float[] embedding)
    {
        var photo = new IndexedPhoto(photoPath, 1, 1);
        return new IndexedFace(photo, x, y, size, size, 0.9f, manual: false, embedding);
    }

    [Fact]
    public void ANamedFace_IsKnownByItsName_AndFoundByLookingAlike()
    {
        var people = new PeopleLibrary();
        var mom = Face("a.jpg", 0.2f, 0.2f, 0.1f, Vectors.Unit(1, 0));

        people.Label("Mom", mom);

        Assert.Equal("Mom", people.LabelOf(mom));
        Assert.Equal(["Mom"], people.Names);
        var (name, score) = people.Recognize(Vectors.Unit(1, 0.1f))!.Value;
        Assert.Equal("Mom", name);
        Assert.True(score > 0.9f);
        Assert.Null(people.Recognize(Vectors.Unit(0, 1))); // someone else
    }

    [Fact]
    public void TheSameFace_IsRecognizedByWhereItIs_EvenIfTheBoxMovedALittle()
    {
        var people = new PeopleLibrary();
        people.Label("Dad", Face("a.jpg", 0.20f, 0.20f, 0.10f, Vectors.Unit(1)));

        Assert.Equal("Dad", people.LabelOf(Face("A.JPG", 0.21f, 0.20f, 0.10f, Vectors.Unit(1))));  // same face, box nudged
        Assert.Null(people.LabelOf(Face("a.jpg", 0.70f, 0.70f, 0.10f, Vectors.Unit(1))));           // another face in the same photo
        Assert.Null(people.LabelOf(Face("b.jpg", 0.20f, 0.20f, 0.10f, Vectors.Unit(1))));           // another photo
    }

    [Fact]
    public void NamingAFaceAgain_ReplacesTheEarlierName()
    {
        var people = new PeopleLibrary();
        var face = Face("a.jpg", 0.2f, 0.2f, 0.1f, Vectors.Unit(1));
        people.Label("Mom", face);

        people.Label("Aunt", face);

        Assert.Equal("Aunt", people.LabelOf(face));
        Assert.Equal(["Aunt"], people.Names); // Mom has no faces left, so she is gone
    }

    [Fact]
    public void Unlabel_TakesTheNameOff()
    {
        var people = new PeopleLibrary();
        var face = Face("a.jpg", 0.2f, 0.2f, 0.1f, Vectors.Unit(1));
        people.Label("Mom", face);

        Assert.True(people.Unlabel(face));
        Assert.False(people.Unlabel(face));
        Assert.Null(people.LabelOf(face));
        Assert.Empty(people.Names);
    }

    [Fact]
    public void Names_AreMatchedWithoutCaringAboutCase()
    {
        var people = new PeopleLibrary();
        people.Label("Grandma Olga", Face("a.jpg", 0, 0, 0.1f, Vectors.Unit(1)));

        Assert.True(people.TryFindName("  grandma olga ", out var name));
        Assert.Equal("Grandma Olga", name);
        Assert.False(people.TryFindName("olga", out _));
    }

    [Fact]
    public void ThePeopleFile_IsWrittenNextToThePhotos_AndReadBack()
    {
        var file = Path.Combine(_folder.Path, SearchPaths.PeopleFileName);
        var first = new PeopleLibrary(file);
        var face = Face("2009/a.jpg", 0.25f, 0.5f, 0.125f, Vectors.Unit(4, 3, 2, 1));
        first.Label("Mom", face);
        first.Label("Dad", Face("2009/b.jpg", 0.1f, 0.1f, 0.1f, Vectors.Unit(1, 2)));

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));

        var second = PeopleLibrary.Load(_folder.Path);

        Assert.Null(second.LoadError);
        Assert.Equal(["Dad", "Mom"], second.Names);
        Assert.Equal("Mom", second.LabelOf(face));
        Assert.Equal([Vectors.Unit(4, 3, 2, 1)], second.ExamplesOf("Mom"));
    }

    [Fact]
    public void ABrokenPeopleFile_IsReportedButDoesNotStopTheApp()
    {
        File.WriteAllText(Path.Combine(_folder.Path, SearchPaths.PeopleFileName), "{ this is not json");

        var people = PeopleLibrary.Load(_folder.Path);

        Assert.NotNull(people.LoadError);
        Assert.Empty(people.Names);
    }

    [Fact]
    public void NoPeopleFile_MeansNoPeople()
    {
        var people = PeopleLibrary.Load(_folder.Path);

        Assert.Null(people.LoadError);
        Assert.Empty(people.Names);
    }

    [Fact]
    public void AnEmptyName_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new PeopleLibrary().Label("   ", Face("a.jpg", 0, 0, 0.1f, Vectors.Unit(1))));
    }
}

public class FolderIndexTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly FolderIndex _search;
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));

    public FolderIndexTests()
    {
        _search = FolderIndex.Open(_photos.Path);
    }

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private IndexedPhoto Add(string path, float[]? clip, params float[][] faceVectors)
    {
        var photo = new IndexedPhoto(path, length: 10, modifiedTicks: 20) { Clip = clip, ClipVersion = 1, FaceVersion = 1 };
        photo.Faces = [.. faceVectors.Select((v, i) => new IndexedFace(photo, 0.1f + 0.3f * i, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, v))];
        _search.Index.Save([photo]);
        return photo;
    }

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    [Fact]
    public void SimilarPhotos_AreRankedByTheirVectors()
    {
        Add("same.jpg", Vectors.Unit(1, 0));
        Add("close.jpg", Vectors.Unit(1, 0.3f));
        Add("far.jpg", Vectors.Unit(0, 1));

        var result = _search.SearchSimilar(FullPath("same.jpg"));

        Assert.Equal([FullPath("same.jpg"), FullPath("close.jpg")], result.Hits.Select(h => h.FullPath));
        Assert.Equal(3, result.SearchedPhotos);
        Assert.True(result.Hits[0].Score > result.Hits[1].Score);
    }

    [Fact]
    public void SimilarPhotos_OfAPhotoThatIsNotIndexed_AreNone()
    {
        Assert.Empty(_search.SearchSimilar(FullPath("missing.jpg")).Hits);
    }

    [Fact]
    public void ASearchByFace_FindsThePhotosWithThatPerson_BestFirst()
    {
        var me = Add("me.jpg", Vectors.Unit(1), Vectors.Unit(1, 0));
        Add("again.jpg", Vectors.Unit(2), Vectors.Unit(0, 1), Vectors.Unit(1, 0.4f)); // second face is the same person
        Add("other.jpg", Vectors.Unit(3), Vectors.Unit(0, 1));

        var result = _search.SearchPerson(me.Faces[0], name: null);

        Assert.Equal([FullPath("me.jpg"), FullPath("again.jpg")], result.Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void ANamedPerson_IsFoundByTypingTheName()
    {
        var mom = Add("mom1.jpg", Vectors.Unit(1), Vectors.Unit(1, 0));
        Add("mom2.jpg", Vectors.Unit(2), Vectors.Unit(1, 0.2f));
        Add("dad.jpg", Vectors.Unit(3), Vectors.Unit(0, 1));
        _search.NameFace(mom.Faces[0], "Mom");

        var result = _search.Search(_models, "mom");

        Assert.Equal("Mom", result.Description);
        Assert.Equal([FullPath("mom1.jpg"), FullPath("mom2.jpg")], result.Hits.Select(h => h.FullPath).Order());
    }

    [Fact]
    public void FacesOfAPhoto_ShowTheNameGivenAndTheOneRecognized()
    {
        var first = Add("one.jpg", Vectors.Unit(1), Vectors.Unit(1, 0));
        var second = Add("two.jpg", Vectors.Unit(2), Vectors.Unit(1, 0.1f), Vectors.Unit(0, 1));
        _search.NameFace(first.Faces[0], "Mom");

        var given = Assert.Single(_search.DescribeFaces(FullPath("one.jpg")));
        Assert.Equal("Mom", given.Name);
        Assert.True(given.Confirmed);

        var faces = _search.DescribeFaces(FullPath("two.jpg"));
        Assert.Equal("Mom", faces[0].Name);       // looks like Mom ...
        Assert.False(faces[0].Confirmed);         // ... but nobody said so
        Assert.Null(faces[1].Name);               // a stranger
    }

    [Fact]
    public void DeletingAFace_RemovesItsName_AndItFromTheSearch()
    {
        var photo = Add("one.jpg", Vectors.Unit(1), Vectors.Unit(1, 0));
        _search.NameFace(photo.Faces[0], "Mom");
        var face = photo.Faces[0];

        _search.DeleteFace(face);

        Assert.Empty(photo.Faces);
        Assert.Null(_search.People.LabelOf(face));
        Assert.Empty(_search.People.Names);
    }

    [Fact]
    public void SearchingForNothing_FindsNothing()
    {
        Add("one.jpg", Vectors.Unit(1));

        Assert.Empty(_search.Search(_models, "   ").Hits);
    }
}
