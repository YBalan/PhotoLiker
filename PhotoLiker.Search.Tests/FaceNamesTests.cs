using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

/// <summary>The line of names the slideshow writes in the corner of a photo.</summary>
public class FaceNamesTests
{
    private static readonly IndexedPhoto Photo = new("a.jpg", 1000, 1000);

    // A face whose middle is at 'middle' (a fraction of the width of the photo); a name of null is a face nobody is known by
    private static FaceLabel Label(string? name, float middle, bool confirmed = true, float width = 0.1f) =>
        new(new IndexedFace(Photo, middle - width / 2, 0.3f, width, width, 0.9f, manual: false, [1f]), name, confirmed, confirmed ? 1f : 0.5f);

    [Fact]
    public void NoFaces_HaveNothingToWrite() => Assert.Null(FaceNames.Caption([]));

    [Fact]
    public void FacesNobodyIsKnownBy_HaveNothingToWrite() => Assert.Null(FaceNames.Caption([Label(null, 0.2f), Label(null, 0.7f, confirmed: false)]));

    [Fact]
    public void ThePeople_AreWrittenLeftToRight_NotInTheOrderTheDetectorFoundThem()
    {
        var caption = FaceNames.Caption([Label("Mom", 0.8f), Label("Anna", 0.2f), Label("Dad", 0.5f)]);

        Assert.Equal("Anna, Dad, Mom", caption);
    }

    [Fact]
    public void ALookAlike_HasAQuestionMark_ANamedFaceHasNot()
    {
        var caption = FaceNames.Caption([Label("Anna", 0.2f), Label("Oleh", 0.5f, confirmed: false)]);

        Assert.Equal("Anna, Oleh?", caption);
    }

    [Fact]
    public void FacesNobodyIsKnownBy_AreLeftOut_AmongTheOthers()
    {
        var caption = FaceNames.Caption([Label("Anna", 0.2f), Label(null, 0.5f), Label("Dad", 0.8f)]);

        Assert.Equal("Anna, Dad", caption);
    }

    [Fact]
    public void APersonWithTwoFaces_IsWrittenOnce_AtTheLeftOne()
    {
        var caption = FaceNames.Caption([Label("Dad", 0.9f), Label("Anna", 0.5f), Label("Dad", 0.1f)]);

        Assert.Equal("Dad, Anna", caption);
    }

    [Fact]
    public void APersonIsSure_IfAnyOfTheirFacesIsNamed()
    {
        // One face is named Anna, another only looks like her: it is Anna all the same, there is no doubt about her being there
        var caption = FaceNames.Caption([Label("Anna", 0.2f, confirmed: false), Label("Anna", 0.7f, confirmed: true)]);

        Assert.Equal("Anna", caption);
    }

    [Fact]
    public void TheSameNameInAnotherCase_IsTheSamePerson()
    {
        var caption = FaceNames.Caption([Label("Anna", 0.2f), Label("anna", 0.7f, confirmed: false)]);

        Assert.Equal("Anna", caption);
    }

    [Fact]
    public void FacesAtTheSamePlace_KeepTheirOrder()
    {
        var caption = FaceNames.Caption([Label("Bob", 0.5f), Label("Al", 0.5f)]);

        Assert.Equal("Bob, Al", caption);
    }

    [Fact]
    public void ABigFace_DoesNotJumpAheadOfASmallOneBesideIt_ItIsTheMiddleThatCounts()
    {
        // The big face starts further left (X 0.10) but its middle (0.35) is right of the small one's (0.25)
        var big = Label("Big", 0.35f, width: 0.5f);
        var small = Label("Small", 0.25f, width: 0.05f);

        Assert.Equal("Small, Big", FaceNames.Caption([big, small]));
    }
}
