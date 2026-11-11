using System.Text.Json;
using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

/// <summary>Deleting people: their faces lose the names in this folder and in the other folders (which follow when they are opened), and they leave the groups.</summary>
public class DeletePeopleTests : IDisposable
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

    // ── this folder ────────────────────────────────────────────────────

    [Fact]
    public void ADeletedPerson_LosesAllHerFaces_AndOthersStay()
    {
        var people = Open(_a);
        var momFace = Face("a.jpg", MomLooks);
        var dadFace = Face("b.jpg", DadLooks);
        people.Label("Mom", momFace);
        people.Label("Mom", Face("c.jpg", Vectors.Unit(1, 0.1f, 0)));
        people.Label("Dad", dadFace);

        people.DeletePeople(["Mom"]);

        Assert.Equal(["Dad"], people.Names);
        Assert.Empty(people.ExamplesOf("Mom"));
        Assert.False(people.TryFindName("mom", out _));
        Assert.Null(people.LabelOf(momFace));
        Assert.Equal("Dad", people.LabelOf(dadFace));
        Assert.Null(people.Recognize(MomLooks)); // nobody looks like her any more
    }

    [Fact]
    public void TheFileOfTheFolder_HasNoLongerThePerson()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_a).Label("Dad", Face("b.jpg", DadLooks));

        Open(_a).DeletePeople(["Mom"]);

        Assert.Equal(["Dad"], OwnFileOf(_a).Names);
        Assert.Equal(["Dad"], Open(_a).Names);
    }

    [Fact]
    public void SeveralPeopleAreDeletedTogether_NamesInAnyCase_EachOnce()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Dad", Face("b.jpg", DadLooks));
        people.Label("Aunt", Face("c.jpg", Vectors.Unit(0, 0, 1)));

        people.DeletePeople(["mom", "DAD", "Mom"]);

        Assert.Equal(["Aunt"], people.Names);
        Assert.Equal(["Aunt"], OwnFileOf(_a).Names);
    }

    [Fact]
    public void DeletingNobody_ChangesNothing_AndWritesNothing()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        var before = File.ReadAllText(Shared);

        people.DeletePeople([]);
        people.DeletePeople(["  "]);

        Assert.Equal(["Mom"], people.Names);
        Assert.Equal(before, File.ReadAllText(Shared));
    }

    [Fact]
    public void ANameNobodyHas_IsRefused_AndNobodyIsDeleted()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));

        var nobody = Assert.Throws<ArgumentException>(() => people.DeletePeople(["Mom", "Grandpa"]));

        Assert.Contains("Grandpa", nobody.Message);
        Assert.Equal(["Mom"], people.Names);
        Assert.Equal(["Mom"], OwnFileOf(_a).Names);
    }

    [Fact]
    public void AGroupIsNoPerson_AndCanNotBeDeletedHere()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);

        Assert.Throws<ArgumentException>(() => people.DeletePeople(["Family"]));

        Assert.Equal(["Family"], people.Groups.Names);
        Assert.Equal(["Mom"], people.Groups.MembersOf("Family"));
    }

    // ── groups ─────────────────────────────────────────────────────────

    [Fact]
    public void ADeletedPerson_LeavesEveryGroup_TheGroupsStay()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Dad", Face("b.jpg", DadLooks));
        people.SetGroupMembers("Family", ["Mom", "Dad"], member: true);
        people.SetGroupMembers("Friends", ["Mom"], member: true);

        people.DeletePeople(["Mom"]);

        Assert.Equal(["Dad"], people.Groups.MembersOf("Family"));
        Assert.Empty(people.Groups.MembersOf("Friends"));
        Assert.Equal(["Family", "Friends"], people.Groups.Names); // an empty group is still a group
        Assert.Empty(people.Groups.GroupsOf("Mom"));
        Assert.Equal(["Dad"], PersonGroups.Load(SearchPaths.GroupsFile(_a.Path)).MembersOf("Family")); // the copy in the folder too
        Assert.Equal([DadLooks], people.ExamplesOf("Family"));
    }

    [Fact]
    public void ASomebodyNamedLaterWithTheSameName_StartsWithNoGroup()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);
        people.DeletePeople(["Mom"]);

        people.Label("Mom", Face("b.jpg", DadLooks));

        Assert.Empty(people.Groups.GroupsOf("Mom"));
    }

    // ── other folders ──────────────────────────────────────────────────

    [Fact]
    public void ThePersonOfAnotherFolder_IsDeletedHere_WithoutAFileHere()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        var inB = Open(_b);
        Assert.Equal(["Mom"], inB.Names); // known from the other folder

        inB.DeletePeople(["Mom"]);

        Assert.Empty(inB.Names);
        Assert.Empty(inB.ExamplesOf("Mom"));
        Assert.False(File.Exists(SearchPaths.PeopleFile(_b.Path))); // nobody was named in B: B gets no file
    }

    [Fact]
    public void AFolderThatHasThePerson_FollowsWhenItIsOpened_AndItsFileIsChanged()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_a).Label("Dad", Face("b.jpg", DadLooks));
        Open(_b).DeletePeople(["Mom"]); // deleted from B, which does not hold her; A has not heard yet

        Assert.Equal(["Dad", "Mom"], OwnFileOf(_a).Names);

        var inA = Open(_a);

        Assert.Equal(["Dad"], inA.Names);
        Assert.Equal(["Dad"], OwnFileOf(_a).Names); // and A's own file says so now
        Assert.Equal(["Dad"], Open(_b).Names);
        Assert.Null(inA.SharedError);
    }

    [Fact]
    public void TheOtherFolderOfASecondFolder_FollowsToo_AndTheFolderThatDeletedSeesNobody()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_b).Label("Mom", Face("b.jpg", Vectors.Unit(1, 0.1f, 0)));
        var inA = Open(_a);

        inA.DeletePeople(["Mom"]);

        Assert.Empty(inA.Names); // B's faces are gone from here at once
        Assert.Empty(inA.ExamplesOf("Mom"));
        Assert.Equal(["Mom"], OwnFileOf(_b).Names); // B's own file has not been opened since

        var inB = Open(_b);

        Assert.Empty(inB.Names);
        Assert.Empty(OwnFileOf(_b).Names);
        Assert.Empty(Open(_a).Names);
    }

    [Fact]
    public void ASomebodyNamedLater_WithTheSameName_IsNotTheOnePersonDeletedBefore()
    {
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_b).Label("Mom", Face("b.jpg", MomLooks));
        Open(_a).DeletePeople(["Mom"]);
        var inB = Open(_b); // follows the deletion
        Assert.Empty(inB.Names);

        inB.Label("Mom", Face("c.jpg", DadLooks)); // a new Mom (another person with that name)

        Assert.Equal(["Mom"], Open(_b).Names); // not deleted a second time
        Assert.Equal(["Mom"], OwnFileOf(_b).Names);
        Assert.Equal(["Mom"], Open(_a).Names);
    }

    [Fact]
    public void AFolderTheSharedFileNeverHeard_KeepsItsPeople()
    {
        OwnFileOf(_b).Label("Mom", Face("b.jpg", MomLooks)); // named before there was a shared file (or on another computer)
        Open(_a).Label("Mom", Face("a.jpg", MomLooks));
        Open(_a).DeletePeople(["Mom"]);

        var inB = Open(_b);

        Assert.Equal(["Mom"], inB.Names); // nobody told B it had her: its photos stay named
        Assert.Equal(["Mom"], OwnFileOf(_b).Names);
    }

    [Fact]
    public void ARenameAndADeletion_AreMadeInTheOrderTheyHappened()
    {
        var momInB = Face("b1.jpg", MomLooks);
        var momInA = Face("a1.jpg", MomLooks);
        Open(_a).Label("Mom", momInA);
        Open(_a).Label("Mum", Face("a2.jpg", DadLooks));
        Open(_b).Label("Mom", momInB);
        Open(_b).Label("Mum", Face("b2.jpg", DadLooks));

        Open(_a).DeletePeople(["Mum"]);
        Thread.Sleep(50); // the shared file tells the order by the clock
        Open(_a).RenamePerson("Mom", "Mum"); // Mom now has the name the deleted one had

        var inB = Open(_b);

        Assert.Equal(["Mum"], inB.Names);
        Assert.Equal("Mum", inB.LabelOf(momInB)); // the old Mum is gone, the old Mom is Mum
        Assert.Single(OwnFileOf(_b).ExamplesOf("Mum"));
    }

    // ── failures and the shared file ───────────────────────────────────

    [Fact]
    public void WhenTheFileOfTheFolderCanNotBeWritten_NobodyIsDeleted_NowhereElseEither()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.SetGroupMembers("Family", ["Mom"], member: true);
        File.Delete(SearchPaths.PeopleFile(_a.Path));
        Directory.CreateDirectory(SearchPaths.PeopleFile(_a.Path)); // nothing can be written where the file should be

        var failure = Record.Exception(() => people.DeletePeople(["Mom"]));

        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().Name);
        Assert.Equal(["Mom"], people.Names);
        Assert.Single(people.ExamplesOf("Mom"));
        Assert.Equal(["Mom"], people.Groups.MembersOf("Family"));
        Assert.Equal(["Mom"], Open(_b).Names); // the shared file was not touched
    }

    [Fact]
    public void TheSharedFile_RemembersWhoWasDeleted()
    {
        var people = Open(_a);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Dad", Face("b.jpg", DadLooks));

        people.DeletePeople(["Mom", "Dad"]);

        using var document = JsonDocument.Parse(File.ReadAllText(Shared));
        var deletions = document.RootElement.GetProperty("Deletions");
        Assert.Equal(["Dad", "Mom"], deletions.EnumerateArray().Select(d => d.GetProperty("Name").GetString()!).Order().ToArray());
        Assert.Equal(0, document.RootElement.GetProperty("Folders").GetArrayLength()); // A has nobody left, so it has no slice
    }

    [Fact]
    public void WithoutASharedFile_ThePersonIsDeletedFromTheFolderAndTheGroups()
    {
        var people = PeopleLibrary.Load(_a.Path, sharedFile: null, GroupsFile);
        people.Label("Mom", Face("a.jpg", MomLooks));
        people.Label("Dad", Face("b.jpg", DadLooks));
        people.SetGroupMembers("Family", ["Mom", "Dad"], member: true);

        people.DeletePeople(["Mom"]);

        Assert.Equal(["Dad"], PeopleLibrary.Load(_a.Path, sharedFile: null).Names);
        Assert.Equal(["Dad"], people.Groups.MembersOf("Family"));
        Assert.False(File.Exists(Shared));
    }
}
