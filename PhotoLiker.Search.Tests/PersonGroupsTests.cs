using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

/// <summary>The groups the people are sorted into: the file, the changes, and what they mean for naming and searching.</summary>
public class PersonGroupsTests : IDisposable
{
    private readonly TempFolder _data = new();

    private string GroupsFile => Path.Combine(_data.Path, "people-groups.json");

    public void Dispose() => _data.Dispose();

    private PersonGroups Open() => PersonGroups.Load(GroupsFile);

    [Fact]
    public void WithoutAFile_ThereAreNoGroups_AndNothingIsCreatedUntilThereIsOne()
    {
        var groups = Open();

        Assert.Empty(groups.Names);
        Assert.Null(groups.LoadError);
        Assert.False(File.Exists(GroupsFile));
    }

    [Fact]
    public void AGroup_KeepsItsPeople_AndIsStillThereWhenTheFileIsReadAgain()
    {
        var groups = Open();
        groups.Create("Family");
        groups.SetMembers("Family", ["Mom", "Dad"], member: true);
        groups.SetMember("Friends", "Dad", member: true); // a group is made when somebody is put into it

        var again = Open();

        Assert.Equal(["Family", "Friends"], again.Names);
        Assert.Equal(["Dad", "Mom"], again.MembersOf("Family"));
        Assert.Equal(["Family", "Friends"], again.GroupsOf("Dad")); // a person can be in several groups
        Assert.Equal(["Family"], again.GroupsOf("Mom"));
        Assert.Empty(again.GroupsOf("Nobody"));
    }

    [Fact]
    public void Names_AreFoundInAnyCase_AndTheFirstSpellingStays()
    {
        var groups = Open();
        Assert.Equal("Family", groups.Create("Family"));

        Assert.Equal("Family", groups.Create("  FAMILY "));
        groups.SetMember("family", "Grandma Olga", member: true);
        groups.SetMember("Family", "grandma olga", member: true); // the same person

        Assert.Single(groups.Names);
        Assert.True(groups.TryFind("fAmIlY", out var name));
        Assert.Equal("Family", name);
        Assert.Equal(["Grandma Olga"], groups.MembersOf("FAMILY"));
        Assert.True(groups.IsMember("family", "GRANDMA OLGA"));
    }

    [Fact]
    public void TheNamesAreSorted_NotInTheOrderTheyWereMade()
    {
        var groups = Open();
        groups.Create("Work");
        groups.Create("Family");
        groups.Create("Friends");

        Assert.Equal(["Family", "Friends", "Work"], groups.Names);
        groups.SetMembers("Family", ["Zoe", "Anna", "mike"], member: true);
        Assert.Equal(["Anna", "mike", "Zoe"], groups.MembersOf("Family"));
    }

    [Fact]
    public void ANameThatIsEmpty_IsRefused()
    {
        var groups = Open();

        Assert.Throws<ArgumentException>(() => groups.Create("   "));
        groups.Create("Family");
        Assert.Throws<ArgumentException>(() => groups.Rename("Family", ""));
        Assert.Equal(["Family"], groups.Names);
    }

    [Fact]
    public void TakingSomebodyOut_LeavesTheGroup_AndTheOtherGroups()
    {
        var groups = Open();
        groups.SetMembers("Family", ["Mom", "Dad"], member: true);
        groups.SetMembers("Friends", ["Dad"], member: true);

        groups.SetMember("Family", "dad", member: false);

        Assert.Equal(["Mom"], groups.MembersOf("Family"));
        Assert.Equal(["Friends"], groups.GroupsOf("Dad"));
        Assert.Equal(["Family", "Friends"], Open().Names);
    }

    [Fact]
    public void ARenamedGroup_KeepsItsPeople_AndCanNotTakeTheNameOfAnother()
    {
        var groups = Open();
        groups.SetMembers("Family", ["Mom"], member: true);
        groups.Create("Friends");

        groups.Rename("Family", "Relatives");
        var clash = Assert.Throws<ArgumentException>(() => groups.Rename("Relatives", "friends"));

        Assert.Contains("Friends", clash.Message);
        Assert.Equal(["Friends", "Relatives"], groups.Names);
        Assert.Equal(["Mom"], Open().MembersOf("Relatives"));
        Assert.Empty(Open().MembersOf("Family"));

        groups.Rename("Relatives", "RELATIVES"); // another spelling of its own name is not a clash
        Assert.Equal(["Friends", "RELATIVES"], Open().Names);
    }

    [Fact]
    public void ADeletedGroup_IsGone_AndNobodyElseIsTouched()
    {
        var groups = Open();
        groups.SetMembers("Family", ["Mom"], member: true);
        groups.SetMembers("Friends", ["Mom"], member: true);

        groups.Delete("family");
        groups.Delete("Nonexistent"); // nothing to do, nothing wrong

        Assert.Equal(["Friends"], Open().Names);
        Assert.Equal(["Friends"], Open().GroupsOf("Mom"));
    }

    [Fact]
    public void TakingSomebodyOutOfAGroupThatIsNotThere_DoesNotMakeIt()
    {
        var groups = Open();

        groups.SetMember("Family", "Mom", member: false);
        groups.SetMembers("Family", [], member: true);

        Assert.Empty(groups.Names);
        Assert.False(File.Exists(GroupsFile));
    }

    [Fact]
    public void ACopyOfTheApp_DoesNotLoseWhatAnotherCopyDidMeanwhile()
    {
        var first = Open();
        var second = Open(); // both read the file while it was empty

        first.SetMembers("Family", ["Mom"], member: true);
        second.SetMembers("Work", ["Boss"], member: true); // writes the file, but reads it first

        var all = Open();
        Assert.Equal(["Family", "Work"], all.Names);
        Assert.Equal(["Mom"], all.MembersOf("Family"));
        Assert.Equal(["Family", "Work"], second.Names); // and the second one knows the first one's group now
    }

    [Fact]
    public void ADamagedFile_IsSetAside_AndTheGroupsStartAgain()
    {
        File.WriteAllText(GroupsFile, "{ this is not json");

        var groups = Open();

        Assert.Empty(groups.Names);
        Assert.Contains("people-groups.json.bad", groups.LoadError);
        Assert.True(File.Exists(GroupsFile + ".bad"));
        groups.Create("Family"); // the next change must not run into the broken file
        Assert.Equal(["Family"], Open().Names);
    }

    [Fact]
    public void AFileWrittenByHand_IsReadForgivingly()
    {
        File.WriteAllText(GroupsFile, """
            { "Groups": [
                { "Name": " Family ", "Members": [ "Mom", " Dad ", "", "mom" ] },
                { "Name": "family", "Members": [ "Aunt" ] },
                { "Name": "", "Members": [ "Nobody" ] },
                { "Name": "Empty" }
            ] }
            """);

        var groups = Open();

        Assert.Equal(["Empty", "Family"], groups.Names);
        Assert.Equal(["Aunt", "Dad", "Mom"], groups.MembersOf("Family"));
        Assert.Null(groups.LoadError);
    }

    [Fact]
    public void WithoutAFile_TheGroupsLiveInMemory()
    {
        var groups = new PersonGroups();

        groups.SetMembers("Family", ["Mom"], member: true);
        groups.Rename("Family", "Relatives");

        Assert.Equal(["Mom"], groups.MembersOf("relatives"));
        Assert.Empty(new PersonGroups().Names);
    }
}

/// <summary>Groups in the library of people: names stay apart, and a group has the faces of its people.</summary>
public class GroupsInPeopleLibraryTests : IDisposable
{
    private static readonly float[] Mom = Vectors.Unit(1, 0, 0);
    private static readonly float[] Dad = Vectors.Unit(0, 1, 0);
    private static readonly float[] Friend = Vectors.Unit(0, 0, 1);

    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();

    public void Dispose()
    {
        _photos.Dispose();
        _data.Dispose();
    }

    private string GroupsFile => Path.Combine(_data.Path, "people-groups.json");

    private PeopleLibrary Open() => PeopleLibrary.Load(_photos.Path, sharedFile: null, GroupsFile);

    private static IndexedFace Face(string photo, float[] embedding) =>
        new(new IndexedPhoto(photo, 1, 1), 0.2f, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, embedding);

    private PeopleLibrary FamilyOfMomAndDad()
    {
        var people = Open();
        people.Label("Mom", Face("mom.jpg", Mom));
        people.Label("Mom", Face("mom2.jpg", Vectors.Unit(1, 0.1f, 0)));
        people.Label("Dad", Face("dad.jpg", Dad));
        people.Label("Friend", Face("friend.jpg", Friend));
        people.SetGroupMembers("Family", ["Mom", "Dad"], member: true);
        return people;
    }

    [Fact]
    public void AGroupName_IsFoundLikeAPersonsName_ButIsNotAPerson()
    {
        var people = FamilyOfMomAndDad();

        Assert.True(people.TryFindName("family", out var name));
        Assert.Equal("Family", name);
        Assert.True(people.IsGroup("FAMILY"));
        Assert.False(people.IsGroup("Mom"));
        Assert.Equal(["Dad", "Friend", "Mom"], people.Names); // the people only
    }

    [Fact]
    public void AGroup_HasTheFacesOfAllItsPeople()
    {
        var people = FamilyOfMomAndDad();

        Assert.Equal(3, people.ExamplesOf("Family").Count); // two of Mom, one of Dad
        Assert.Equal(2, people.ExamplesOf("Mom").Count);
        Assert.Contains(Dad, people.ExamplesOf("family"));
        Assert.DoesNotContain(Friend, people.ExamplesOf("Family"));
    }

    [Fact]
    public void AGroup_HasTheFacesOfThePeopleNamedInOtherFolders()
    {
        using var other = new TempFolder();
        var shared = Path.Combine(_data.Path, "people.json");
        PeopleLibrary.Load(other.Path, shared, GroupsFile).Label("Grandma", Face("g.jpg", Friend));

        var here = PeopleLibrary.Load(_photos.Path, shared, GroupsFile);
        here.SetGroupMembers("Family", ["Grandma"], member: true);

        Assert.Equal([Friend], here.ExamplesOf("Family"));
    }

    [Fact]
    public void ThePeopleOfAGroupWhoAreNotKnown_AreNoFaces_AndNoTrouble()
    {
        var people = Open();
        people.SetGroupMembers("Family", ["Nobody Yet"], member: true);

        Assert.Empty(people.ExamplesOf("Family"));
        Assert.Empty(people.Names);
    }

    [Fact]
    public void ASearchTextWithAGroup_TakesTheGroupOut_LikeAName()
    {
        var people = FamilyOfMomAndDad();
        List<string> names = [];

        var words = people.TakeNames("the family playing guitar with Friend", names);

        Assert.Equal(["Family", "Friend"], names);
        Assert.Equal("the playing guitar", words);
    }

    [Fact]
    public void AGroupIsNotTakenOutOfWordsThatMerelyContainIt()
    {
        var people = FamilyOfMomAndDad();
        List<string> names = [];

        var words = people.TakeNames("familyroom and families", names);

        Assert.Empty(names);
        Assert.Equal("familyroom and families", words);
    }

    [Fact]
    public void AGroupCanNotTakeTheNameOfAPerson_AndAPersonCanNotTakeTheNameOfAGroup()
    {
        var people = FamilyOfMomAndDad();

        var group = Assert.Throws<ArgumentException>(() => people.CreateGroup("mom"));
        Assert.Contains("Mom", group.Message);
        Assert.Throws<ArgumentException>(() => people.RenameGroup("Family", "DAD"));
        Assert.Throws<ArgumentException>(() => people.SetGroupMembers("Friend", ["Mom"], member: true));

        var person = Assert.Throws<ArgumentException>(() => people.Label("family", Face("x.jpg", Friend)));
        Assert.Contains("Family", person.Message);
        Assert.Equal(["Family"], people.Groups.Names);
        Assert.Equal(["Dad", "Friend", "Mom"], people.Names);
    }

    [Fact]
    public void ANewFaceOfSomebodyWhoIsKnown_IsFine_AndWhyNotAName_SaysWhy()
    {
        var people = FamilyOfMomAndDad();

        people.Label("MOM", Face("mom3.jpg", Mom)); // one more face of her

        Assert.Equal(3, people.ExamplesOf("Mom").Count);
        Assert.Null(people.WhyNotAName("mom"));
        Assert.Null(people.WhyNotAName("Somebody New"));
        Assert.Null(people.WhyNotAName("  "));
        Assert.Contains("Family", people.WhyNotAName("family"));
    }

    [Fact]
    public void WhenAFileHasTheSameNameForAPersonAndAGroup_ThePersonWins()
    {
        var people = Open();
        people.Label("Mom", Face("mom.jpg", Mom));
        people.Label("Dad", Face("dad.jpg", Dad));
        File.WriteAllText(GroupsFile, """{ "Groups": [ { "Name": "Mom", "Members": [ "Dad" ] } ] }"""); // edited by hand afterwards
        people.Groups.Reload();

        Assert.False(people.IsGroup("Mom"));
        Assert.Equal([Mom], people.ExamplesOf("Mom"));
        List<string> names = [];
        people.TakeNames("Mom", names);
        Assert.Equal(["Mom"], names); // once
    }

    [Fact]
    public void TheGroupsAreTheSameInAnotherFolder()
    {
        FamilyOfMomAndDad();
        using var other = new TempFolder();

        var inOther = PeopleLibrary.Load(other.Path, sharedFile: null, GroupsFile);

        Assert.Equal(["Family"], inOther.Groups.Names);
        Assert.Equal(["Dad", "Mom"], inOther.Groups.MembersOf("Family"));
    }
}

/// <summary>Searching for a group: the photos of anybody in it. Pictures, faces and the pretend text model are made of vectors.</summary>
public class GroupSearchTests : IDisposable
{
    private static readonly float[] Guitar = Vectors.Unit(1);
    private static readonly float[] Beach = Vectors.Unit(0, 1);

    private static readonly float[] Mom = Vectors.Unit(1, 0, 0);
    private static readonly float[] Dad = Vectors.Unit(0, 1, 0);
    private static readonly float[] Friend = Vectors.Unit(0, 0, 1);

    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();
    private readonly FolderIndex _search;
    private readonly List<string> _asked = [];
    private int _added;

    public GroupSearchTests() =>
        _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null, groupsFile: Path.Combine(_data.Path, "people-groups.json"));

    public void Dispose()
    {
        _search.Dispose();
        _photos.Dispose();
        _data.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private float[] Encode(string words)
    {
        _asked.Add(words);
        return Guitar;
    }

    private SearchResult Search(string text) => _search.Search(Encode, text);

    private IndexedPhoto Add(string path, float[] picture, params float[][] faces)
    {
        var photo = new IndexedPhoto(path, length: 10 + _added++, modifiedTicks: 20)
        {
            Clips = Vectors.Clips(picture),
            FaceVersion = 1,
            Date = new PhotoDate(null, DateSource.None, PhotoDates.Version),
        };
        photo.Faces = [.. faces.Select((v, i) => new IndexedFace(photo, 0.1f + 0.3f * i, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, v))];
        _search.Index.Save([photo]);
        return photo;
    }

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    private IEnumerable<string> Found(SearchResult result) => result.Hits.Select(h => Path.GetFileName(h.FullPath)).Order();

    /// <summary>Mom, Dad and a friend are named; Mom and Dad are the Family.</summary>
    private void Family()
    {
        var mom = Add("mom.jpg", Beach, Mom);
        var dad = Add("dad.jpg", Beach, Dad);
        var friend = Add("friend.jpg", Beach, Friend);
        Add("mom-and-dad-guitar.jpg", Guitar, Mom, Dad);
        Add("mom-guitar.jpg", Guitar, Mom);
        Add("friend-guitar.jpg", Guitar, Friend);
        Add("mom-and-friend.jpg", Beach, Friend, Mom);
        Add("nobody.jpg", Beach);

        _search.NameFace(mom.Faces[0], "Mom");
        _search.NameFace(dad.Faces[0], "Dad");
        _search.NameFace(friend.Faces[0], "Friend");
        _search.People.SetGroupMembers("Family", ["Mom", "Dad"], member: true);
    }

    [Fact]
    public void AGroup_FindsThePhotosOfAnybodyInIt()
    {
        Family();

        var result = Search("Family");

        Assert.Equal(["dad.jpg", "mom-and-dad-guitar.jpg", "mom-and-friend.jpg", "mom-guitar.jpg", "mom.jpg"], Found(result));
        Assert.Equal("Family", result.Description);
        Assert.Empty(_asked); // a group, like a name, needs no text model
        Assert.False(_search.NeedsTextModel("Family"));
    }

    [Theory]
    [InlineData("family")]
    [InlineData("FAMILY")]
    [InlineData("  Family ")]
    public void AGroupIsFoundInAnyCase(string text)
    {
        Family();

        Assert.Equal(5, Search(text).Hits.Count);
    }

    [Fact]
    public void AGroupAndAPerson_NeedTheirOwnFaces()
    {
        Family();

        var result = Search("Family and Friend");

        // Friend with somebody of the family: Mom in the same photo; Mom's own face is not used twice
        Assert.Equal(["mom-and-friend.jpg"], Found(result));
    }

    [Fact]
    public void AGroupWithWords_FindsThePhotosOfItsPeopleThatFitTheWords()
    {
        Family();

        var result = Search("Family playing guitar");

        Assert.Equal(["mom-and-dad-guitar.jpg", "mom-guitar.jpg"], Found(result));
        Assert.Equal(["playing guitar"], _asked);
        Assert.False(result.Weak);
    }

    [Fact]
    public void TwoGroupsInOneText_NeedSomebodyOfEach()
    {
        Family();
        _search.People.SetGroupMembers("Friends", ["Friend"], member: true);

        var result = Search("Family and Friends");

        Assert.Equal(["mom-and-friend.jpg"], Found(result));
    }

    [Fact]
    public void ASomebodyWhoLeavesTheGroup_IsNoLongerFound()
    {
        Family();

        _search.People.SetGroupMembers("Family", ["Dad"], member: false);

        Assert.Equal(["mom-and-dad-guitar.jpg", "mom-and-friend.jpg", "mom-guitar.jpg", "mom.jpg"], Found(Search("Family")));
    }

    [Fact]
    public void AGroupWithNobodyInIt_FindsNothing()
    {
        Family();
        _search.People.CreateGroup("Colleagues");

        Assert.Empty(Search("Colleagues").Hits);
    }

    [Fact]
    public void ADeletedGroup_IsNoLongerAName_SoItsWordIsDescribedAgain()
    {
        Family();
        _search.People.Groups.Delete("Family");

        Search("family");

        Assert.Equal(["family"], _asked);
    }

    [Fact]
    public void ThePersonKeepsFindingOnlyThemselves_WhenTheyAreInAGroup()
    {
        Family();

        Assert.Equal(["mom-and-dad-guitar.jpg", "mom-and-friend.jpg", "mom-guitar.jpg", "mom.jpg"], Found(Search("Mom")));
    }
}

/// <summary>The copy of the groups in the photo folder: it travels with the photos, and the set that was saved later is the set.</summary>
public class GroupsInThePhotoFolderTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly TempFolder _a = new();
    private readonly TempFolder _b = new();

    public void Dispose()
    {
        _data.Dispose();
        _a.Dispose();
        _b.Dispose();
    }

    private string Computer(string name) => Path.Combine(_data.Path, name + ".json"); // the file of a computer

    private static string CopyIn(TempFolder folder) => SearchPaths.GroupsFile(folder.Path);

    private PersonGroups Open(TempFolder folder, string computer = "this", Func<bool>? wanted = null) =>
        PersonGroups.Load(Computer(computer), CopyIn(folder), wanted);

    [Fact]
    public void AChange_IsWrittenInThePhotoFolderToo_AndTheCopyIsAFileOfGroups()
    {
        var groups = Open(_a);

        groups.SetMembers("Family", ["Mom", "Dad"], member: true);

        Assert.True(File.Exists(CopyIn(_a)));
        Assert.Equal("photo-liker-groups.json", Path.GetFileName(CopyIn(_a)));
        var copy = PersonGroups.Load(CopyIn(_a)); // the same format: it can be read as a groups file
        Assert.Equal(["Family"], copy.Names);
        Assert.Equal(["Dad", "Mom"], copy.MembersOf("Family"));
        Assert.Null(groups.FolderCopyError);
    }

    [Fact]
    public void AFolderWithoutGroups_GetsNoFile()
    {
        Open(_a);

        Assert.False(File.Exists(CopyIn(_a)));
        Assert.Empty(Directory.GetFiles(_a.Path));
    }

    [Fact]
    public void ANewComputer_LearnsTheGroupsFromTheFolderItIsGivenWith()
    {
        Open(_a, "old computer").SetMembers("Family", ["Mom"], member: true);

        var onNewComputer = Open(_a, "new computer");

        Assert.Equal(["Mom"], onNewComputer.MembersOf("Family"));
        Assert.Equal(["Family"], PersonGroups.Load(Computer("new computer")).Names); // and keeps them, for the other folders
        Assert.Equal(["Mom"], Open(_b, "new computer").MembersOf("Family"));
    }

    [Fact]
    public void ACopyThatWasSavedLater_ReplacesTheGroupsOfThisComputer()
    {
        Open(_a, "this").SetMembers("Family", ["Mom"], member: true);
        // Another computer opens the folder, learns the groups and changes them
        Open(_a, "other").SetMembers("Work", ["Boss"], member: true);

        var back = Open(_a, "this");

        Assert.Equal(["Family", "Work"], back.Names);
        Assert.Equal(["Family", "Work"], PersonGroups.Load(Computer("this")).Names);
    }

    [Fact]
    public void ACopyThatIsOlder_IsBroughtUpToDate_WhenTheFolderIsOpened()
    {
        Open(_a).SetMembers("Family", ["Mom"], member: true);
        Open(_b); // B has never had a copy... and gets one: there are groups
        Open(_a).SetMembers("Friends", ["Anna"], member: true); // changed while A was open: B's copy is behind

        Assert.Equal(["Family"], PersonGroups.Load(CopyIn(_b)).Names);

        var inB = Open(_b);

        Assert.Equal(["Family", "Friends"], inB.Names);
        Assert.Equal(["Family", "Friends"], PersonGroups.Load(CopyIn(_b)).Names);
    }

    [Fact]
    public void AGroupThatWasDeleted_StaysDeleted_InFoldersThatAreOpenedLater()
    {
        var inA = Open(_a);
        inA.SetMembers("Family", ["Mom"], member: true);
        inA.SetMembers("Friends", ["Anna"], member: true);
        Open(_b); // both folders have both groups

        Open(_a).Delete("Friends"); // deleted while A is open

        Assert.Equal(["Family"], Open(_b).Names); // B's copy is older than the deletion: it does not bring the group back
        Assert.Equal(["Family"], PersonGroups.Load(CopyIn(_b)).Names);
        Assert.Equal(["Family"], Open(_a).Names);
    }

    [Fact]
    public void WhenTheLastGroupIsDeleted_TheCopyIsLeftEmpty_NotLeftBehind()
    {
        var groups = Open(_a);
        groups.SetMembers("Family", ["Mom"], member: true);

        groups.Delete("Family");

        Assert.True(File.Exists(CopyIn(_a)));
        Assert.Empty(PersonGroups.Load(CopyIn(_a)).Names);
        Assert.Empty(Open(_a, "a computer that never had groups").Names); // the empty copy is the later one: nothing is brought back
        Assert.Empty(Open(_a).Names);
    }

    [Fact]
    public void ACopyThatIsDeletedByHand_ComesBackWhenTheFolderIsOpened()
    {
        Open(_a).SetMembers("Family", ["Mom"], member: true);
        File.Delete(CopyIn(_a));

        Open(_a);

        Assert.Equal(["Family"], PersonGroups.Load(CopyIn(_a)).Names);
    }

    [Fact]
    public void ACopyThatCanNotBeWritten_DoesNotStopTheChange_AndIsReported()
    {
        Directory.CreateDirectory(CopyIn(_a)); // a folder where the file should be: nothing can be written there
        var groups = Open(_a);

        groups.SetMembers("Family", ["Mom"], member: true);

        Assert.Equal(["Mom"], groups.MembersOf("Family"));
        Assert.Equal(["Family"], PersonGroups.Load(Computer("this")).Names); // the groups of this computer are saved
        Assert.NotNull(groups.FolderCopyError);
        Assert.Empty(Directory.GetFiles(_a.Path, "*.tmp")); // and no litter
    }

    [Fact]
    public void ADamagedCopy_IsSetAside_AndWrittenAgain()
    {
        Open(_a, "this").SetMembers("Family", ["Mom"], member: true);
        File.WriteAllText(CopyIn(_a), "{ not json");

        var groups = Open(_a, "this");

        Assert.Equal(["Family"], groups.Names);
        Assert.Contains("photo-liker-groups.json.bad", groups.FolderCopyError);
        Assert.True(File.Exists(CopyIn(_a) + ".bad"));
        Assert.Equal(["Family"], PersonGroups.Load(CopyIn(_a)).Names);
    }

    [Fact]
    public void ACopyIsMadeOnlyWhereTheFolderWantsOne_ButOneThatIsThereIsKeptUpToDate()
    {
        var wanted = false;
        var groups = Open(_a, wanted: () => wanted);

        groups.SetMembers("Family", ["Mom"], member: true);
        Assert.False(File.Exists(CopyIn(_a))); // no people named in this folder: no file in it

        wanted = true;
        groups.MakeFolderCopyIfMissing(); // the first person was named
        Assert.Equal(["Family"], PersonGroups.Load(CopyIn(_a)).Names);

        wanted = false;
        groups.SetMembers("Work", ["Boss"], member: true);
        Assert.Equal(["Family", "Work"], PersonGroups.Load(CopyIn(_a)).Names);
    }

    [Fact]
    public void WhenBothHaveTheSameGroups_NothingIsWritten()
    {
        Open(_a).SetMembers("Family", ["Mom"], member: true);
        var before = File.GetLastWriteTimeUtc(CopyIn(_a));
        var written = File.ReadAllText(CopyIn(_a));
        Thread.Sleep(30);

        Open(_a);

        Assert.Equal(written, File.ReadAllText(CopyIn(_a)));
        Assert.Equal(before, File.GetLastWriteTimeUtc(CopyIn(_a)));
    }

    [Fact]
    public void TheFileOfTheComputer_SaysWhenItWasSaved_AndAFileWithoutItCountsFromItsLastWrite()
    {
        var groups = Open(_a);
        groups.SetMembers("Family", ["Mom"], member: true);
        Assert.Contains("\"Saved\"", File.ReadAllText(Computer("this")));

        // A file made before the stamp existed: it is as old as it was last written, so a copy made since wins
        File.WriteAllText(Computer("old"), """{ "Groups": [ { "Name": "Work", "Members": [ "Boss" ] } ] }""");
        File.SetLastWriteTimeUtc(Computer("old"), DateTime.UtcNow.AddDays(-30));

        Assert.Equal(["Family"], Open(_a, "old").Names);
    }

    [Fact]
    public void WithoutAFileOfTheComputer_NoFolderCopyIsMade()
    {
        var groups = new PersonGroups(file: null, folderCopy: CopyIn(_a));

        groups.SetMembers("Family", ["Mom"], member: true);

        Assert.False(File.Exists(CopyIn(_a)));
    }
}

/// <summary>The library of people decides which folders keep a copy: the ones that have people named in them.</summary>
public class GroupsCopyInPeopleLibraryTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();

    public void Dispose()
    {
        _photos.Dispose();
        _data.Dispose();
    }

    private PeopleLibrary Open() => PeopleLibrary.Load(_photos.Path, sharedFile: null, Path.Combine(_data.Path, "groups.json"));

    [Fact]
    public void AFolderWithNoPeople_DoesNotGetTheCopy_UntilItsFirstPersonIsNamed()
    {
        var people = Open();
        people.SetGroupMembers("Family", ["Mom"], member: true);

        Assert.False(File.Exists(SearchPaths.GroupsFile(_photos.Path)));

        people.Label("Mom", new IndexedFace(new IndexedPhoto("mom.jpg", 1, 1), 0.2f, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, Vectors.Unit(1)));

        Assert.True(File.Exists(SearchPaths.GroupsFile(_photos.Path)));
        Assert.Equal(["Family"], PersonGroups.Load(SearchPaths.GroupsFile(_photos.Path)).Names);
    }

    [Fact]
    public void AFolderOfPeople_KeepsTheGroupsWithThePhotos()
    {
        var people = Open();
        people.Label("Mom", new IndexedFace(new IndexedPhoto("mom.jpg", 1, 1), 0.2f, 0.2f, 0.1f, 0.1f, 0.9f, manual: false, Vectors.Unit(1)));

        people.SetGroupMembers("Family", ["Mom"], member: true);

        // The folder is copied to another computer: its names and its groups come along
        using var elsewhere = new TempFolder();
        foreach (var file in Directory.GetFiles(_photos.Path))
            File.Copy(file, Path.Combine(elsewhere.Path, Path.GetFileName(file)));
        using var otherData = new TempFolder();

        var there = PeopleLibrary.Load(elsewhere.Path, sharedFile: null, Path.Combine(otherData.Path, "groups.json"));

        Assert.Equal(["Mom"], there.Names);
        Assert.Equal(["Family"], there.Groups.Names);
        Assert.Equal(["Mom"], there.Groups.MembersOf("Family"));
    }
}
