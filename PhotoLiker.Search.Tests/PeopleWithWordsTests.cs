using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

/// <summary>
/// A name with words in a search ("Yurii playing guitar"), and several names ("Mom and Dad"). Pictures, faces and the pretend text model
/// are made of vectors: every description is made into Guitar, so a photo fits the words if its picture is Guitar.
/// </summary>
public class PeopleWithWordsTests : IDisposable
{
    // What the pictures show, and what a description is made into
    private static readonly float[] Guitar = Vectors.Unit(1);
    private static readonly float[] Beach = Vectors.Unit(0, 1);

    // How two people look (their faces are far apart), and somebody who looks a little like both
    private static readonly float[] Me = Vectors.Unit(1, 0);
    private static readonly float[] You = Vectors.Unit(0, 1);
    private static readonly float[] Between = Vectors.Unit(1, 1);

    private readonly TempFolder _photos = new();
    private readonly FolderIndex _search;
    private readonly List<string> _asked = [];
    private int _added;

    public PeopleWithWordsTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private float[] Encode(string words)
    {
        _asked.Add(words);
        return Guitar;
    }

    private SearchResult Search(string text) => _search.Search(Encode, text);

    private IndexedPhoto Make(string path, IReadOnlyDictionary<string, ClipVector> clips, DateTime? taken, float[][] faces)
    {
        // A size of its own: photos of one size, time and picture are copies of one photo, which a search shows once
        var photo = new IndexedPhoto(path, length: 10 + _added++, modifiedTicks: 20)
        {
            Clips = clips,
            FaceVersion = 1,
            Date = new PhotoDate(taken, taken is null ? DateSource.None : DateSource.Exif, PhotoDates.Version),
        };
        photo.Faces = [.. faces.Select((v, i) => new IndexedFace(photo, 0.1f + 0.3f * i, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, v))];
        _search.Index.Save([photo]);
        return photo;
    }

    /// <summary>A photo with this picture (the vector the default model made of it) and these faces.</summary>
    private IndexedPhoto Add(string path, float[] picture, params float[][] faces) => Make(path, Vectors.Clips(picture), null, faces);

    private IndexedPhoto AddTaken(string path, float[] picture, DateTime taken, params float[][] faces) => Make(path, Vectors.Clips(picture), taken, faces);

    /// <summary>A photo that has only been through another model.</summary>
    private IndexedPhoto AddThrough(string path, ClipModelInfo model, params float[][] faces) => Make(path, Vectors.Clips(model, 1), null, faces);

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    private IEnumerable<string> Found(SearchResult result) => result.Hits.Select(h => h.FullPath);

    /// <summary>Photos of Yurii (named by the first one) and of somebody else, with a picture of a guitar or of a beach.</summary>
    private void YuriiAndOthers()
    {
        var first = Add("yurii-guitar.jpg", Guitar, Me);
        Add("yurii-beach.jpg", Beach, Vectors.Unit(1, 0.05f));
        Add("other-guitar.jpg", Guitar, You);
        Add("nobody-guitar.jpg", Guitar);
        _search.NameFace(first.Faces[0], "Yurii");
    }

    [Fact]
    public void ANameWithWords_FindsThePhotosOfThatPersonThatFitTheWords()
    {
        YuriiAndOthers();

        var result = Search("Yurii playing guitar");

        Assert.Equal([FullPath("yurii-guitar.jpg")], Found(result));
        Assert.Equal("Yurii, \"playing guitar\"", result.Description);
        Assert.Equal(["playing guitar"], _asked); // the name is not part of what the text model is asked
        Assert.False(result.Weak);
    }

    [Theory]
    [InlineData("playing guitar yurii", "playing guitar")]
    [InlineData("YURII playing guitar", "playing guitar")]
    [InlineData("Yurii's guitar", "guitar")]
    [InlineData("Yurii’s guitar", "guitar")]
    [InlineData("with Yurii on the beach", "the beach")]
    [InlineData("on the beach with Yurii", "the beach")]
    [InlineData("beach and Yurii", "beach")]
    [InlineData("a   guitar,  Yurii", "a guitar,")]
    public void TheNameCanBeAnywhere_InAnyCase_AndWordsThatOnlyJoinItAreDropped(string text, string words)
    {
        YuriiAndOthers();

        var result = Search(text);

        Assert.Equal([words], _asked);
        Assert.NotEmpty(result.Hits);
        Assert.All(Found(result), path => Assert.StartsWith("yurii", Path.GetFileName(path))); // only photos of him
    }

    [Fact]
    public void ANameInsideAnotherWord_IsNotAName()
    {
        YuriiAndOthers();

        var result = Search("Yuriix guitar");

        Assert.Equal(["Yuriix guitar"], _asked);
        Assert.Contains(FullPath("other-guitar.jpg"), Found(result)); // nobody is asked for: all photos are searched
    }

    [Fact]
    public void ANameInUkrainian_IsFoundToo()
    {
        var photo = Add("yurii.jpg", Guitar, Me);
        Add("other.jpg", Guitar, You);
        _search.NameFace(photo.Faces[0], "Юрій");

        var result = Search("юрій грає на гітарі");

        Assert.Equal([FullPath("yurii.jpg")], Found(result));
        Assert.Equal(["грає на гітарі"], _asked);
    }

    [Fact]
    public void WhenOneNameContainsAnother_TheLongerNameWins()
    {
        var grandma = Add("grandma.jpg", Guitar, Me);
        var olga = Add("olga.jpg", Guitar, You);
        _search.NameFace(grandma.Faces[0], "Grandma Olga");
        _search.NameFace(olga.Faces[0], "Olga");

        Assert.Equal([FullPath("grandma.jpg")], Found(Search("Grandma Olga cooking")));
        Assert.Equal(["cooking"], _asked);
        Assert.Equal([FullPath("olga.jpg")], Found(Search("Olga cooking")));
    }

    [Fact]
    public void SeveralNames_FindThePhotosWhereEveryoneHasAFaceOfTheirOwn()
    {
        var both = Add("both.jpg", Guitar, Me, You);
        Add("mom-only.jpg", Guitar, Me);
        Add("dad-only.jpg", Guitar, You);
        Add("mom-twice.jpg", Guitar, Me, Vectors.Unit(1, 0.05f));
        Add("one-face-like-both.jpg", Guitar, Between); // close enough to either, but one face is one person
        _search.NameFace(both.Faces[0], "Mom");
        _search.NameFace(both.Faces[1], "Dad");

        var result = Search("Mom and Dad");

        Assert.Equal([FullPath("both.jpg")], Found(result));
        Assert.Equal("Mom and Dad", result.Description);
        Assert.Empty(_asked); // only faces: no text model needed
    }

    [Fact]
    public void SeveralNames_RankThePhotosWhereTheFacesAreClosestFirst()
    {
        var best = Add("best.jpg", Guitar, Me, You);
        Add("worse.jpg", Guitar, Vectors.Unit(1, 0.6f), Vectors.Unit(0.6f, 1));
        _search.NameFace(best.Faces[0], "Mom");
        _search.NameFace(best.Faces[1], "Dad");

        var result = Search("dad mom");

        Assert.Equal([FullPath("best.jpg"), FullPath("worse.jpg")], Found(result));
        Assert.True(result.Hits[0].Score > result.Hits[1].Score);
    }

    [Fact]
    public void SeveralNamesWithWords_FindThePhotosOfAllOfThemThatFitTheWords()
    {
        var both = Add("both-guitar.jpg", Guitar, Me, You);
        Add("both-beach.jpg", Beach, Me, You);
        Add("mom-guitar.jpg", Guitar, Me);
        _search.NameFace(both.Faces[0], "Mom");
        _search.NameFace(both.Faces[1], "Dad");

        var result = Search("Mom and Dad on the beach");

        Assert.Equal([FullPath("both-guitar.jpg")], Found(result)); // the pretend text model makes every description a guitar
        Assert.Equal("Mom and Dad, \"the beach\"", result.Description);
        Assert.Equal(["the beach"], _asked);
    }

    [Fact]
    public void ANameWithWordsAndADate_FindsThePersonInThatTimeOnly()
    {
        var first = AddTaken("sep.jpg", Guitar, new DateTime(2009, 9, 5), Me);
        AddTaken("later.jpg", Guitar, new DateTime(2010, 9, 5), Me);
        AddTaken("other.jpg", Guitar, new DateTime(2009, 9, 6), You);
        _search.NameFace(first.Faces[0], "Yurii");

        var result = Search("Yurii playing guitar September 2009");

        Assert.Equal([FullPath("sep.jpg")], Found(result));
        Assert.Equal("Yurii, \"playing guitar\", September 2009", result.Description);
        Assert.Equal(["playing guitar"], _asked);
    }

    [Fact]
    public void WhenNoPhotoOfThePersonFitsTheWords_TheClosestOnesAreShownAsWeak()
    {
        var first = Add("yurii-beach.jpg", Beach, Me);
        Add("yurii-beach2.jpg", Beach, Me);
        Add("other-guitar.jpg", Guitar, You);
        _search.NameFace(first.Faces[0], "Yurii");

        var result = Search("Yurii playing guitar");

        Assert.True(result.Weak);
        Assert.Equal([FullPath("yurii-beach.jpg"), FullPath("yurii-beach2.jpg")], Found(result).Order()); // his, not the stranger's guitar
    }

    [Fact]
    public void PhotosOfThePersonThatHaveNotBeenThroughTheModel_AreCounted_TheOthersAreNot()
    {
        var first = Add("yurii-guitar.jpg", Guitar, Me);
        AddThrough("yurii-siglip.jpg", ClipModels.SigLip2, Me);
        AddThrough("stranger-siglip.jpg", ClipModels.SigLip2, You);
        _search.NameFace(first.Faces[0], "Yurii");

        var result = Search("Yurii playing guitar");

        Assert.Equal([FullPath("yurii-guitar.jpg")], Found(result));
        Assert.Equal(1, result.OtherModelPhotos);
    }

    [Fact]
    public void ATextInQuotes_IsOnlyWords_NoNameIsLookedFor()
    {
        YuriiAndOthers();

        var result = Search("\"Yurii playing guitar\"");

        Assert.Equal(["Yurii playing guitar"], _asked);
        Assert.Equal("\"Yurii playing guitar\"", result.Description);
        Assert.Equal(3, result.Hits.Count); // every photo with a guitar, whoever is in it
    }

    [Fact]
    public void WhichSearchesNeedTheTextModel()
    {
        YuriiAndOthers();
        _search.NameFace(_search.Index.Find("other-guitar.jpg")!.Faces[0], "Dad");

        Assert.False(_search.NeedsTextModel("Yurii"));
        Assert.False(_search.NeedsTextModel("Yurii 2009"));
        Assert.False(_search.NeedsTextModel("Yurii and Dad"));
        Assert.True(_search.NeedsTextModel("Yurii playing guitar"));
        Assert.True(_search.NeedsTextModel("Yurii and Dad on the beach"));
        Assert.True(_search.NeedsTextModel("Yurii playing guitar 2009"));
        Assert.True(_search.NeedsTextModel("playing guitar"));
        Assert.True(_search.NeedsTextModel("\"Yurii\"")); // quotes: the word, not the person
        Assert.False(_search.NeedsTextModel("\"\""));
    }

    [Fact]
    public void ANameAlone_AndANameWithADate_StillSearchByFacesOnly()
    {
        var first = AddTaken("sep.jpg", Guitar, new DateTime(2009, 9, 5), Me);
        AddTaken("later.jpg", Guitar, new DateTime(2010, 9, 5), Me);
        _search.NameFace(first.Faces[0], "Yurii");

        Assert.Equal(2, Search("yurii").Hits.Count);
        Assert.Equal([FullPath("sep.jpg")], Found(Search("Yurii 2009")));
        Assert.Empty(_asked);
    }
}
