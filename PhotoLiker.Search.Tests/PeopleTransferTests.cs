using System.Text.Json.Nodes;
using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

/// <summary>Taking the people and the groups to another computer: one file, added to what is there and never replacing it.</summary>
public class PeopleTransferTests : IDisposable
{
    private static readonly float[] MomLooks = Vectors.Unit(1, 0, 0);
    private static readonly float[] DadLooks = Vectors.Unit(0, 1, 0);
    private static readonly float[] GranLooks = Vectors.Unit(0, 0, 1);

    // The photo folders of the old computer, and one that only the new computer has
    private readonly TempFolder _photosA = new();
    private readonly TempFolder _photosB = new();
    private readonly TempFolder _newPhotos = new();
    private readonly TempFolder _oldData = new();
    private readonly TempFolder _newData = new();
    private readonly TempFolder _files = new();

    public void Dispose()
    {
        _photosA.Dispose();
        _photosB.Dispose();
        _newPhotos.Dispose();
        _oldData.Dispose();
        _newData.Dispose();
        _files.Dispose();
    }

    private string TheFile => _files.File("people.plpeople");

    private static PeopleLibrary Open(TempFolder photos, TempFolder data) =>
        PeopleLibrary.Load(photos.Path, Path.Combine(data.Path, "people.json"), Path.Combine(data.Path, "people-groups.json"));

    private static IndexedFace Face(string photo, float[] embedding, float x = 0.2f) =>
        new(new IndexedPhoto(photo, 1, 1), x, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, embedding);

    /// <summary>The old computer: Mom named in folder A, Dad in folder B, a group Family with both.</summary>
    private PeopleLibrary OldComputer()
    {
        Open(_photosA, _oldData).Label("Mom", Face("a.jpg", MomLooks));
        Open(_photosB, _oldData).Label("Dad", Face("b.jpg", DadLooks));
        var people = Open(_photosA, _oldData);
        people.SetGroupMembers("Family", ["Mom", "Dad"], member: true);
        return people;
    }

    /// <summary>The photos of a folder came to the new computer, but not the files that hold the names and the groups.</summary>
    private static void PhotosWithoutNames(TempFolder photos)
    {
        File.Delete(SearchPaths.PeopleFile(photos.Path));
        File.Delete(SearchPaths.GroupsFile(photos.Path));
    }

    private void ChangeTheFile(Action<JsonNode> change)
    {
        var node = JsonNode.Parse(File.ReadAllText(TheFile))!;
        change(node);
        File.WriteAllText(TheFile, node.ToJsonString());
    }

    // ── the file ───────────────────────────────────────────────────────

    [Fact]
    public void TheFile_HoldsThePeopleOfAllFolders_AndTheGroups()
    {
        var info = OldComputer().Export(TheFile);

        Assert.Equal(2, info.People);
        Assert.Equal(2, info.Faces);
        Assert.Equal(2, info.Folders);
        Assert.Equal(1, info.Groups);
        Assert.True(info.FacesUsable);
        Assert.Equal(Path.TrimEndingDirectorySeparator(_photosA.Path), info.Folder);
        Assert.NotNull(info.ExportedAt);
        Assert.Equal(info with { ExportedAt = null }, PeopleLibrary.Inspect(TheFile) with { ExportedAt = null });
    }

    [Fact]
    public void Exporting_ChangesNothingHere()
    {
        var people = OldComputer();
        var before = File.ReadAllText(SearchPaths.PeopleFile(_photosA.Path));

        people.Export(TheFile);

        Assert.Equal(before, File.ReadAllText(SearchPaths.PeopleFile(_photosA.Path)));
        Assert.Equal(["Dad", "Mom"], Open(_photosA, _oldData).Names);
        Assert.False(File.Exists(TheFile + ".tmp"));
    }

    [Fact]
    public void ABadFile_IsToldWhatItIs()
    {
        File.WriteAllText(TheFile, "not json at all");
        Assert.Throws<InvalidDataException>(() => PeopleLibrary.Inspect(TheFile));

        File.WriteAllText(TheFile, "[1, 2, 3]");
        Assert.Throws<InvalidDataException>(() => PeopleLibrary.Inspect(TheFile));

        // The people file of a folder is JSON too, and not this one
        OldComputer();
        Assert.Throws<InvalidDataException>(() => PeopleLibrary.Inspect(SearchPaths.PeopleFile(_photosA.Path)));
        Assert.Throws<InvalidDataException>(() => Open(_newPhotos, _newData).Import(SearchPaths.PeopleFile(_photosA.Path)));
        Assert.Empty(Open(_newPhotos, _newData).Names);
    }

    [Fact]
    public void AFileOfANewerVersion_IsRefused()
    {
        File.WriteAllText(TheFile, """{ "Format": "PhotoLiker people", "Version": 99 }""");

        var ex = Assert.Throws<InvalidDataException>(() => PeopleLibrary.Inspect(TheFile));
        Assert.Contains("99", ex.Message);
    }

    // ── import: people ─────────────────────────────────────────────────

    [Fact]
    public void OnANewComputer_ThePeopleAreKnown_AndRecognizedAndSearchedBy()
    {
        OldComputer().Export(TheFile);

        var people = Open(_newPhotos, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(2, result.NewPeople);
        Assert.Equal(2, result.FacesAdded);
        Assert.Equal(0, result.FacesKnown);
        Assert.Equal(0, result.FacesUnusable);
        Assert.Empty(result.Refused);
        Assert.Equal(["Dad", "Mom"], people.Names);
        Assert.Equal([MomLooks], people.ExamplesOf("Mom"));
        Assert.Equal("Dad", people.Recognize(Vectors.Unit(0, 1, 0.1f))!.Value.Name);
        Assert.Null(people.LabelOf(Face("a.jpg", MomLooks))); // her face is named in another folder, not here
        Assert.Null(people.SharedError);
    }

    [Fact]
    public void WhatWasImported_StaysWhenTheAppIsOpenedAgain()
    {
        OldComputer().Export(TheFile);
        Open(_newPhotos, _newData).Import(TheFile);

        var again = Open(_newPhotos, _newData);

        Assert.Equal(["Dad", "Mom"], again.Names);
        Assert.Equal([DadLooks], again.ExamplesOf("Dad"));
        Assert.Empty(PeopleLibrary.Load(_newPhotos.Path, sharedFile: null).Names); // the folder itself has no names
        Assert.False(File.Exists(SearchPaths.PeopleFile(_newPhotos.Path)));
    }

    [Fact]
    public void ThePartOfTheSameFolder_BecomesTheNamesOfThatFolder()
    {
        OldComputer().Export(TheFile);
        PhotosWithoutNames(_photosA); // folder A came to the new computer at the same place

        var people = Open(_photosA, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(2, result.FacesAdded);
        Assert.Equal("Mom", people.LabelOf(Face("a.jpg", MomLooks)));
        Assert.Equal(["Mom"], PeopleLibrary.Load(_photosA.Path, sharedFile: null).Names); // in the file of the folder, so it travels with the photos
        Assert.Equal(["Dad", "Mom"], Open(_photosA, _newData).Names);
    }

    [Fact]
    public void ImportingTheSameFileAgain_ChangesNothing()
    {
        OldComputer().Export(TheFile);
        var people = Open(_newPhotos, _newData);
        people.Import(TheFile);
        var shared = File.ReadAllText(Path.Combine(_newData.Path, "people.json"));

        var again = people.Import(TheFile);

        Assert.Equal(0, again.NewPeople);
        Assert.Equal(0, again.FacesAdded);
        Assert.Equal(2, again.FacesKnown);
        Assert.Equal(0, again.NewGroups);
        Assert.Equal(0, again.GroupMembersAdded);
        Assert.Equal(shared, File.ReadAllText(Path.Combine(_newData.Path, "people.json")));
        Assert.Single(people.ExamplesOf("Mom"));
    }

    [Fact]
    public void AFaceThatIsNamedHere_KeepsItsName_WhateverTheFileSays()
    {
        OldComputer().Export(TheFile);
        PhotosWithoutNames(_photosA);
        var people = Open(_photosA, _newData);
        people.Label("Dad", Face("a.jpg", DadLooks)); // the same place in the same photo that the file calls Mom

        var result = people.Import(TheFile);

        Assert.Equal("Dad", people.LabelOf(Face("a.jpg", MomLooks)));
        Assert.Equal(1, result.FacesKnown);
        Assert.Equal(1, result.FacesAdded); // Dad of folder B
        Assert.Equal(0, result.NewPeople);
        Assert.Equal(["Dad"], people.Names); // Mom was only at that place
        Assert.Equal(2, people.ExamplesOf("Dad").Count);
    }

    [Fact]
    public void APersonKeepsTheSpellingTheyHaveHere()
    {
        Open(_photosB, _newData).Label("Grandma Olga", Face("b.jpg", GranLooks));
        Open(_photosA, _oldData).Label("grandma olga", Face("a.jpg", Vectors.Unit(0, 0, 0.9f, 0.1f)));
        Open(_photosA, _oldData).Export(TheFile);

        var people = Open(_newPhotos, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(0, result.NewPeople);
        Assert.Equal(1, result.FacesAdded);
        Assert.Equal(["Grandma Olga"], people.Names);
        Assert.Equal(2, people.ExamplesOf("Grandma Olga").Count);
    }

    [Fact]
    public void FacesOfAnotherFaceModel_AreNotUsed_ButTheGroupsAre()
    {
        OldComputer().Export(TheFile);
        ChangeTheFile(file => file["FaceVersion"] = 99);
        Assert.False(PeopleLibrary.Inspect(TheFile).FacesUsable);

        var people = Open(_newPhotos, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(0, result.FacesAdded);
        Assert.Equal(2, result.FacesUnusable);
        Assert.Empty(people.Names);
        Assert.Equal(["Family"], people.Groups.Names);
        Assert.Equal(["Dad", "Mom"], people.Groups.MembersOf("Family")); // nobody carries the names, the group waits for them
    }

    [Fact]
    public void ADamagedFace_IsLeftOut_AndTheRestComesIn()
    {
        OldComputer().Export(TheFile);
        ChangeTheFile(file => file["Folders"]![0]!["People"]![0]!["Faces"]![0]!["Embedding"] = "!!not base64!!");

        var people = Open(_newPhotos, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(1, result.FacesAdded);
        Assert.Equal(1, result.FacesUnusable);
        Assert.Single(people.Names);
    }

    [Fact]
    public void WhileTheFileOfTheFolderCanNotBeRead_NothingIsImportedOverIt()
    {
        OldComputer().Export(TheFile);
        File.WriteAllText(SearchPaths.PeopleFile(_photosA.Path), "{ broken");
        var people = Open(_photosA, _newData);
        Assert.NotNull(people.LoadError);

        var ex = Assert.Throws<IOException>(() => people.Import(TheFile));

        Assert.Contains(people.LoadError!, ex.Message);
        Assert.Equal("{ broken", File.ReadAllText(SearchPaths.PeopleFile(_photosA.Path)));
        Assert.False(File.Exists(Path.Combine(_newData.Path, "people.json")));
    }

    // ── import: groups ─────────────────────────────────────────────────

    [Fact]
    public void TheGroups_ComeWithTheirPeople()
    {
        OldComputer().Export(TheFile);

        var people = Open(_newPhotos, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(1, result.NewGroups);
        Assert.Equal(2, result.GroupMembersAdded);
        Assert.Equal(["Family"], people.Groups.Names);
        Assert.Equal(["Dad", "Mom"], people.Groups.MembersOf("Family"));
        Assert.Equal(["Family"], people.Groups.GroupsOf("Mom"));
        Assert.Equal(["Family"], Open(_newPhotos, _newData).Groups.Names); // saved
    }

    [Fact]
    public void AGroupThatIsHere_GetsThePeopleItDidNotHave_AndLosesNone()
    {
        OldComputer().Export(TheFile);
        var people = Open(_newPhotos, _newData);
        people.SetGroupMembers("family", ["Dad", "Aunt Zoe"], member: true); // another spelling of the name, and somebody the file does not know

        var result = people.Import(TheFile);

        Assert.Equal(0, result.NewGroups);
        Assert.Equal(1, result.GroupMembersAdded); // Mom
        Assert.Equal(["family"], people.Groups.Names);
        Assert.Equal(["Aunt Zoe", "Dad", "Mom"], people.Groups.MembersOf("family"));
    }

    [Fact]
    public void AGroupWithNoOneInIt_IsMadeAll_TheSame()
    {
        var old = Open(_photosA, _oldData);
        old.CreateGroup("Work");
        old.Export(TheFile);

        var people = Open(_newPhotos, _newData);
        var result = people.Import(TheFile);

        Assert.Equal(1, result.NewGroups);
        Assert.Equal(["Work"], people.Groups.Names);
        Assert.Empty(people.Groups.MembersOf("Work"));
    }

    [Fact]
    public void AGroup_WithTheNameOfAPersonHere_IsLeftOut_WithAReason()
    {
        OldComputer().Export(TheFile);
        Open(_photosB, _newData).Label("Family", Face("x.jpg", GranLooks)); // somebody here is called Family
        var people = Open(_newPhotos, _newData);

        var result = people.Import(TheFile);

        Assert.Equal(0, result.NewGroups);
        Assert.Empty(people.Groups.Names);
        Assert.Contains(result.Refused, sentence => sentence.Contains("Family"));
    }

    [Fact]
    public void APerson_WithTheNameOfAGroupHere_IsLeftOut_WithAReason()
    {
        OldComputer().Export(TheFile);
        var people = Open(_newPhotos, _newData);
        people.CreateGroup("Mom");

        var result = people.Import(TheFile);

        Assert.Equal(1, result.NewPeople); // Dad
        Assert.Equal(1, result.FacesUnusable);
        Assert.Equal(["Dad"], people.Names);
        Assert.Contains(result.Refused, sentence => sentence.Contains("Mom"));
    }

    [Fact]
    public void AnImport_IsAChangeOfTheGroups_SoTheLaterSavedCopiesFollow()
    {
        OldComputer().Export(TheFile);
        Open(_newPhotos, _newData).Label("Zed", Face("z.jpg", GranLooks)); // a folder keeps a copy of the groups once somebody is named in it
        var people = Open(_newPhotos, _newData);

        people.Import(TheFile);

        Assert.True(File.Exists(SearchPaths.GroupsFile(_newPhotos.Path)));
        Assert.Contains("Family", File.ReadAllText(SearchPaths.GroupsFile(_newPhotos.Path)));
    }
}
