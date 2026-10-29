namespace PhotoLiker.Search.Tests;

/// <summary>The people named in one photo folder are known in the others, through the file in the app data folder.</summary>
public class SharedPeopleTests : IDisposable
{
    private readonly TempFolder _folderA = new();
    private readonly TempFolder _folderB = new();
    private readonly TempFolder _data = new();

    private string Shared => Path.Combine(_data.Path, SearchPaths.SharedPeopleFileName);

    public void Dispose()
    {
        _folderA.Dispose();
        _folderB.Dispose();
        _data.Dispose();
    }

    private static IndexedFace Face(string photoPath, float x, float y, float[] embedding) =>
        new(new IndexedPhoto(photoPath, 1, 1), x, y, 0.1f, 0.1f, 0.9f, manual: false, embedding);

    private PeopleLibrary OpenA() => PeopleLibrary.Load(_folderA.Path, Shared);

    private PeopleLibrary OpenB() => PeopleLibrary.Load(_folderB.Path, Shared);

    [Fact]
    public void APersonNamedInOneFolder_IsKnownInAnother()
    {
        OpenA().Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));

        var inB = OpenB();

        Assert.Equal(["Mom"], inB.Names);
        Assert.True(inB.TryFindName("mom", out var name));
        Assert.Equal("Mom", name);
        Assert.Equal([Vectors.Unit(1, 0)], inB.ExamplesOf("Mom"));
        Assert.Equal("Mom", inB.Recognize(Vectors.Unit(1, 0.1f))!.Value.Name);
        Assert.Null(inB.Recognize(Vectors.Unit(0, 1)));
        Assert.Null(inB.SharedError);
    }

    [Fact]
    public void AFaceNamedInAnotherFolder_IsNotNamedHere_EvenInAPhotoOfTheSameName()
    {
        OpenA().Label("Mom", Face("IMG_0001.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));

        var inB = OpenB();

        Assert.Null(inB.LabelOf(Face("IMG_0001.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0))));
        Assert.False(inB.Unlabel(Face("IMG_0001.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0))));
        Assert.Equal(["Mom"], OpenA().Names); // her face in A was not touched
    }

    [Fact]
    public void TheFileOfAFolder_HoldsOnlyThePeopleNamedThere()
    {
        OpenA().Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));
        OpenB().Label("Dad", Face("b.jpg", 0.2f, 0.2f, Vectors.Unit(0, 1)));

        Assert.Equal(["Mom"], PeopleLibrary.Load(_folderA.Path, sharedFile: null).Names);
        Assert.Equal(["Dad"], PeopleLibrary.Load(_folderB.Path, sharedFile: null).Names);
        Assert.Equal(["Dad", "Mom"], OpenB().Names);
    }

    [Fact]
    public void ThePersonOfAnotherFolder_KeepsHerSpelling_AndGainsTheFacesNamedHere()
    {
        OpenA().Label("Grandma Olga", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));
        var inB = OpenB();

        inB.Label("grandma olga", Face("b.jpg", 0.3f, 0.3f, Vectors.Unit(1, 0.5f)));

        Assert.Equal(["Grandma Olga"], inB.Names);
        Assert.Equal([Vectors.Unit(1, 0.5f), Vectors.Unit(1, 0)], inB.ExamplesOf("Grandma Olga"));
        Assert.Equal(["Grandma Olga"], PeopleLibrary.Load(_folderB.Path, sharedFile: null).Names);
        Assert.Equal(2, OpenA().ExamplesOf("Grandma Olga").Count); // the other way round too
    }

    [Fact]
    public void TakingAName_OffTheLastFaceOfAPerson_RemovesHerFromTheOtherFolders()
    {
        var face = Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0));
        var inA = OpenA();
        inA.Label("Mom", face);
        Assert.Equal(["Mom"], OpenB().Names);

        Assert.True(inA.Unlabel(face));

        Assert.Empty(OpenB().Names);
    }

    [Fact]
    public void ThePeopleOfAFolderThatWasNamedBeforeThereWasAShareFile_JoinItWhenTheFolderIsOpened()
    {
        var file = SearchPaths.PeopleFile(_folderA.Path);
        new PeopleLibrary(file).Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));
        Assert.False(File.Exists(Shared));
        Assert.Empty(OpenB().Names);

        OpenA();

        Assert.Equal(["Mom"], OpenB().Names);
    }

    [Fact]
    public void OpeningAFolderWithNobodyNamed_WritesNothing()
    {
        OpenA();

        Assert.False(File.Exists(Shared));
    }

    [Fact]
    public void WhenTheFileOfAFolderIsGone_ItsPeopleLeaveTheSharedFileToo()
    {
        OpenA().Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));
        File.Delete(SearchPaths.PeopleFile(_folderA.Path));

        OpenA();

        Assert.Empty(OpenB().Names);
    }

    [Fact]
    public void WhenTheFileOfAFolderCannotBeRead_ItsPeopleStayInTheSharedFile()
    {
        OpenA().Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));
        File.WriteAllText(SearchPaths.PeopleFile(_folderA.Path), "{ this is not json");

        var inA = OpenA();

        Assert.NotNull(inA.LoadError);
        Assert.Equal(["Mom"], OpenB().Names);
    }

    [Fact]
    public void TheSameFolderUnderAnotherSpelling_IsTheSameFolder()
    {
        OpenA().Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));

        var again = PeopleLibrary.Load(_folderA.Path.ToUpperInvariant() + Path.DirectorySeparatorChar, Shared);

        Assert.Equal(["Mom"], again.Names);
        Assert.Single(again.ExamplesOf("Mom")); // not once as hers and once as someone else's
    }

    [Fact]
    public void ANameGivenByAnotherOpenCopyOfTheApp_IsKnownAfterNamingSomeoneHere_AndIsNotLost()
    {
        var inA = OpenA();
        var inB = OpenB();

        inB.Label("Dad", Face("b.jpg", 0.2f, 0.2f, Vectors.Unit(0, 1)));
        Assert.Empty(inA.Names);

        inA.Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));

        Assert.Equal(["Dad", "Mom"], inA.Names);
        Assert.Equal(["Dad", "Mom"], PeopleLibrary.Load(_data.Path, Shared).Names); // a third folder
    }

    [Fact]
    public void ASharedFileOfOtherFaceModels_IsNotUsed_AndIsReplacedWhenSomeoneIsNamed()
    {
        OpenA().Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));
        var text = File.ReadAllText(Shared);
        var other = text.Replace($"\"FaceVersion\": {IndexingJob.FaceVersion}", $"\"FaceVersion\": {IndexingJob.FaceVersion + 1}");
        Assert.NotEqual(text, other);
        File.WriteAllText(Shared, other);

        var inB = OpenB();
        Assert.Empty(inB.Names);

        inB.Label("Dad", Face("b.jpg", 0.2f, 0.2f, Vectors.Unit(0, 1)));
        Assert.Equal(["Dad"], OpenA().Names.Where(n => n != "Mom"));
        Assert.Contains($"\"FaceVersion\": {IndexingJob.FaceVersion}", File.ReadAllText(Shared));
    }

    [Fact]
    public void ADamagedSharedFile_IsSetAsideAndStartedAgain_WithoutHurtingTheFolder()
    {
        File.WriteAllText(Shared, "{ this is not json");

        var inA = OpenA();
        Assert.NotNull(inA.SharedError);
        Assert.Null(inA.LoadError);
        Assert.True(File.Exists(Shared + ".bad"));

        inA.Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));

        Assert.Null(inA.SharedError);
        Assert.Equal(["Mom"], OpenB().Names);
    }

    [Fact]
    public void AnUnwritableSharedFile_IsReported_ButTheNameIsKeptInTheFolder()
    {
        Directory.CreateDirectory(Shared); // a folder where the file should be
        var inA = OpenA();

        inA.Label("Mom", Face("a.jpg", 0.2f, 0.2f, Vectors.Unit(1, 0)));

        Assert.NotNull(inA.SharedError);
        Assert.Equal(["Mom"], PeopleLibrary.Load(_folderA.Path, sharedFile: null).Names);
        Assert.False(File.Exists(Shared + ".tmp"));
    }

    [Fact]
    public void ASharedFileNeedsToKnowTheFolder()
    {
        Assert.Throws<ArgumentException>(() => new PeopleLibrary(file: null, sharedFile: Shared));
    }
}

public class SharedPeopleSearchTests : IDisposable
{
    private readonly TempFolder _folderA = new();
    private readonly TempFolder _folderB = new();
    private readonly TempFolder _data = new();
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));
    private readonly List<FolderIndex> _open = [];

    private string Shared => Path.Combine(_data.Path, SearchPaths.SharedPeopleFileName);

    public void Dispose()
    {
        foreach (var index in _open)
            index.Dispose();
        _models.Dispose();
        foreach (var folder in new[] { _folderA, _folderB })
        {
            try { File.Delete(SearchPaths.IndexFile(folder.Path)); } catch { /* best effort */ }
            folder.Dispose();
        }
        _data.Dispose();
    }

    private FolderIndex Open(TempFolder folder)
    {
        var index = FolderIndex.Open(folder.Path, Shared);
        _open.Add(index);
        return index;
    }

    private static IndexedPhoto Add(FolderIndex search, string path, params float[][] faceVectors)
    {
        var photo = new IndexedPhoto(path, length: 10, modifiedTicks: 20) { Clip = Vectors.Unit(1), ClipVersion = 1, FaceVersion = 1 };
        photo.Faces = [.. faceVectors.Select((v, i) => new IndexedFace(photo, 0.1f + 0.3f * i, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, v))];
        search.Index.Save([photo]);
        return photo;
    }

    [Fact]
    public void ThePhotosOfAPersonNamedInAnotherFolder_AreFoundByHerName()
    {
        var inA = Open(_folderA);
        inA.NameFace(Add(inA, "mom.jpg", Vectors.Unit(1, 0)).Faces[0], "Mom");

        var inB = Open(_folderB);
        Add(inB, "party.jpg", Vectors.Unit(0, 1), Vectors.Unit(1, 0.2f));
        Add(inB, "dad.jpg", Vectors.Unit(0, 1));

        var result = inB.Search(_models, "mom");

        Assert.Equal("Mom", result.Description);
        Assert.Equal([Path.Combine(_folderB.Path, "party.jpg")], result.Hits.Select(h => h.FullPath));
        Assert.False(inB.NeedsTextModel("Mom"));
    }

    [Fact]
    public void AFaceOfAnotherFoldersPerson_ShowsAsLookingLikeHer_UntilItIsConfirmed()
    {
        var inA = Open(_folderA);
        inA.NameFace(Add(inA, "mom.jpg", Vectors.Unit(1, 0)).Faces[0], "Mom");
        var inB = Open(_folderB);
        var photo = Add(inB, "party.jpg", Vectors.Unit(1, 0.1f), Vectors.Unit(0, 1));

        var faces = inB.DescribeFaces(Path.Combine(_folderB.Path, "party.jpg"));
        Assert.Equal("Mom", faces[0].Name);
        Assert.False(faces[0].Confirmed);
        Assert.Null(faces[1].Name);

        inB.NameFace(photo.Faces[0], "Mom");

        Assert.True(inB.DescribeFaces(Path.Combine(_folderB.Path, "party.jpg"))[0].Confirmed);
    }
}
