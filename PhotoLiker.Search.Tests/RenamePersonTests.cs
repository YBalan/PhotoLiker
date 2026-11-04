using System.Text.Json;
using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

/// <summary>Giving a person another name: in this folder, in the other folders (which follow when they are opened), and in the groups.</summary>
public class RenamePersonTests : IDisposable
{
    private static readonly float[] MomLooks = Vectors.Unit(1, 0, 0);
    private static readonly float[] DadLooks = Vectors.Unit(0, 1, 0);

    private readonly TempFolder _a = new();
    private readonly TempFolder _b = new();
    private readonly TempFolder _data = new();

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
        _data.Dispose();
    }

    private string Shared => Path.Combine(_data.Path, "people.json");

    private string GroupsFile => Path.Combine(_data.Path, "people-groups.json");

    private PeopleLibrary Open(TempFolder folder) => PeopleLibrary.Load(folder.Path, Shared, GroupsFile);

    /// <summary>What the folder's own file says, without the shared file or the groups.</summary>
    private static PeopleLibrary OwnFileOf(TempFolder folder) => PeopleLibrary.Load(folder.Path, sharedFile: null);

    private static IndexedFace Face(string photo, float[] embedding, float x = 0.2f) =>
        new(new IndexedPhoto(photo, 1, 1), x, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, embedding);

    [Fact]
    public void ARenamedPerson_KeepsAllHerFaces_UnderTheNewName()
    {
        var people = Open(_a);
        var face = Face("a.jpg", MomLooks);
        people.Label("Mom", face);
        people.Label("Mom", Face("b.jpg", Vectors.Unit(1, 0.1f, 0)));

        var now = people.RenamePerson("Mom", "Mum");

        Assert.Equal("Mum", now);
        Assert.Equal(["Mum"], people.Names);
        Assert.Equal(2, people.ExamplesOf("Mum").Count);
        Assert.Empty(people.ExamplesOf("Mom"));
        Assert.False(people.TryFindName("mom", out _));
        Assert.Equal("Mum", people.LabelOf(face));
        Assert.Equal("Mum", people.Recognize(Vectors.Unit(1, 0.05f, 0))!.Value.Name);
    }

    [Fact]
    public void TheNewName_IsInTheFileOfTheFolder()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));

        Open(_a).RenamePerson("Mom", "Mum");

        Assert.Equal(["Mum"], OwnFileOf(_a).Names);
        Assert.Equal(["Mum"], Open(_a).Names);
    }

    [Fact]
    public void ANameThatDiffersOnlyInCase_IsAnotherSpelling()
    {
        var people = Open(_a);
        people.Label("mom", Face("a.jpg", MomLooks));

        Assert.Equal("Mom", people.RenamePerson("mom", "Mom"));

        Assert.Equal(["Mom"], people.Names);
        Assert.Equal(["Mom"], OwnFileOf(_a).Names);
    }

    [Fact]
    public void ANameThatSomebodyHas_MakesTwoPeopleOne_WithTheSpellingOfTheOneWhoWasThere()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Mum", Face("b.jpg", Vectors.Unit(1, 0.1f, 0)));

        Assert.True(people.WouldMerge("Mum", "mom"));
        Assert.False(people.WouldMerge("Mom", "MOM")); // her own name in another case
        Assert.False(people.WouldMerge("Mom", "Nobody"));

        Assert.Equal("Mom", people.RenamePerson("Mum", "MOM"));

        Assert.Equal(["Mom"], people.Names);
        Assert.Equal(2, people.ExamplesOf("Mom").Count);
        Assert.Equal(["Mom"], OwnFileOf(_a).Names);
        Assert.Equal(2, OwnFileOf(_a).ExamplesOf("Mom").Count);
    }

    [Fact]
    public void ANameThatCanNotBeUsed_IsRefused_AndNothingChanges()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);

        Assert.Throws<ArgumentException>(() => people.RenamePerson("Mom", "   "));
        var nobody = Assert.Throws<ArgumentException>(() => people.RenamePerson("Grandpa", "Pops"));
        Assert.Contains("Grandpa", nobody.Message);
        var group = Assert.Throws<ArgumentException>(() => people.RenamePerson("Mom", "family"));
        Assert.Contains("Family", group.Message);

        Assert.Equal(["Mom"], people.Names);
        Assert.Equal(["Mom"], people.Groups.MembersOf("Family"));
        Assert.Equal("Mom", people.RenamePerson("MOM", "Mom")); // the same name, for the record: nothing to do
    }

    [Fact]
    public void TheGroupsOfAPerson_FollowTheNewName()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Dad", Face("b.jpg", DadLooks));
        people.SetGroupMembers("Family", ["Mom", "Dad"], member: true);
        people.SetGroupMembers("Friends", ["Mom"], member: true);

        people.RenamePerson("Mom", "Mum");

        Assert.Equal(["Dad", "Mum"], people.Groups.MembersOf("Family"));
        Assert.Equal(["Mum"], people.Groups.MembersOf("Friends"));
        Assert.Equal(["Family", "Friends"], people.Groups.GroupsOf("Mum"));
        Assert.Empty(people.Groups.GroupsOf("Mom"));
        Assert.Equal(["Dad", "Mum"], PersonGroups.Load(SearchPaths.GroupsFile(_a.Path)).MembersOf("Family")); // and so does the copy in the folder
    }

    [Fact]
    public void WhenTwoPeopleBecomeOne_TheyAreInTheGroupOnce()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Mum", Face("b.jpg", MomLooks, x: 0.5f));
        people.SetGroupMembers("Family", ["Mom", "Mum"], member: true);
        people.SetGroupMembers("Friends", ["Mum"], member: true);

        people.RenamePerson("Mum", "Mom");

        Assert.Equal(["Mom"], people.Groups.MembersOf("Family"));
        Assert.Equal(["Mom"], people.Groups.MembersOf("Friends"));
    }

    [Fact]
    public void TheGroupOfAPerson_HasHerFacesUnderTheNewName()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);

        people.RenamePerson("Mom", "Mum");

        Assert.Equal([MomLooks], people.ExamplesOf("Family"));
    }

    // ── other folders ──────────────────────────────────────────────────

    [Fact]
    public void ThePersonOfAnotherFolder_IsRenamedHere_WithoutAFileHere()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        var inB = Open(_b);
        Assert.Equal(["Mom"], inB.Names); // known from the other folder

        Assert.Equal("Mum", inB.RenamePerson("Mom", "Mum"));

        Assert.Equal(["Mum"], inB.Names);
        Assert.False(File.Exists(SearchPaths.PeopleFile(_b.Path))); // nobody was named in B: B gets no file
        Assert.Equal([MomLooks], inB.ExamplesOf("Mum"));
    }

    [Fact]
    public void AFolderThatHasThePerson_FollowsWhenItIsOpened_AndItsFileIsChanged()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_b).RenamePerson("Mom", "Mum"); // renamed from B, which does not hold her; A has not heard yet

        Assert.Equal(["Mom"], OwnFileOf(_a).Names);

        var inA = Open(_a);

        Assert.Equal(["Mum"], inA.Names); // one person, not two
        Assert.Equal(["Mum"], OwnFileOf(_a).Names); // and A's own file says so now
        Assert.Equal(["Mum"], Open(_b).Names);
    }

    [Fact]
    public void ASecondFolder_FollowsToo_AndTheFolderThatRenamedSeesOnePerson()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_b).Label("Mom", Face("b.jpg", Vectors.Unit(1, 0.1f, 0)));
        var inA = Open(_a);

        inA.RenamePerson("Mom", "Mum");

        Assert.Equal(["Mum"], inA.Names); // B's faces are hers too, under the new name at once
        Assert.Equal(2, inA.ExamplesOf("Mum").Count);
        Assert.Equal(["Mom"], OwnFileOf(_b).Names); // B's own file has not been opened since

        var inB = Open(_b);

        Assert.Equal(["Mum"], inB.Names);
        Assert.Equal(["Mum"], OwnFileOf(_b).Names);
        Assert.Equal(["Mum"], Open(_a).Names);
        Assert.Null(inB.SharedError);
    }

    [Fact]
    public void ASomebodyNamedLater_WithTheOldName_IsNotTheOnePersonRenamedBefore()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_b).Label("Mom", Face("b.jpg", MomLooks));
        Open(_a).RenamePerson("Mom", "Mum");
        var inB = Open(_b); // follows the rename
        Assert.Equal(["Mum"], inB.Names);

        inB.Label("Mom", Face("c.jpg", DadLooks)); // a new Mom (another person with that name)

        var again = Open(_b);
        Assert.Equal(["Mom", "Mum"], again.Names); // not renamed a second time
        Assert.Equal(["Mom", "Mum"], OwnFileOf(_b).Names);
        Assert.Equal(["Mom", "Mum"], Open(_a).Names);
    }

    [Fact]
    public void AFolderThatNeverMetTheSharedFile_FollowsTheRenameWhenItJoins()
    {
        OwnFileOf(_b).Label("Mom", Face("b.jpg", MomLooks)); // named before there was a shared file
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_a).RenamePerson("Mom", "Mum");

        Assert.Equal(["Mum"], Open(_b).Names);
        Assert.Equal(["Mum"], OwnFileOf(_b).Names);
    }

    [Fact]
    public void WhenTheFileOfTheFolderCanNotBeWritten_NothingIsRenamed_NowhereElseEither()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);
        File.Delete(SearchPaths.PeopleFile(_a.Path));
        Directory.CreateDirectory(SearchPaths.PeopleFile(_a.Path)); // nothing can be written where the file should be

        var failure = Record.Exception(() => people.RenamePerson("Mom", "Mum"));

        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().Name);
        Assert.Equal(["Mom"], people.Names);
        Assert.Single(people.ExamplesOf("Mom"));
        Assert.Equal(["Mom"], people.Groups.MembersOf("Family"));
        Assert.Equal(["Mom"], Open(_b).Names); // the shared file was not touched
    }

    [Fact]
    public void TheSharedFile_RemembersTheLastHundredRenames()
    {
        var people = Open(_a);
        people.Label("N0", Face("a.jpg", MomLooks));
        for (var i = 1; i <= 105; i++)
            people.RenamePerson($"N{i - 1}", $"N{i}");

        using var document = JsonDocument.Parse(File.ReadAllText(Shared));
        var renames = document.RootElement.GetProperty("Renames");

        Assert.Equal(100, renames.GetArrayLength());
        Assert.Equal("N5", renames[0].GetProperty("From").GetString()); // the oldest five are forgotten
        Assert.Equal("N105", renames[99].GetProperty("To").GetString());
        Assert.Equal(["N105"], Open(_a).Names);
    }

    [Fact]
    public void WithoutASharedFile_ThePersonIsRenamedInTheFolderAndTheGroups()
    {
        var people = PeopleLibrary.Load(_a.Path, sharedFile: null, GroupsFile);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);

        people.RenamePerson("Mom", "Mum");

        Assert.Equal(["Mum"], PeopleLibrary.Load(_a.Path, sharedFile: null).Names);
        Assert.Equal(["Mum"], people.Groups.MembersOf("Family"));
        Assert.False(File.Exists(Shared));
    }
}
