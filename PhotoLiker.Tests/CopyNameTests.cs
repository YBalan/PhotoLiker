using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class CopyNameTests
{
    private static readonly DateTime Taken = new(2015, 4, 18, 10, 11, 12, DateTimeKind.Utc);

    private static PhotoFile Photo(string path, long length = 12345, DateTime? lastWriteUtc = null) =>
        new(path, string.Empty) { Length = length, LastWriteTimeUtc = lastWriteUtc ?? Taken };

    // ── the id ──────────────────────────────────────────────────────────────

    [Fact]
    public void IdOf_IsFixedByTheSizeAndTheSecondOfTheTime()
    {
        // Worked out apart from the code (SHA-256 of the two 64-bit numbers, first 4 bytes): a name made on another
        // computer, or by a later version, has to come out the same, or the copies of the photos are not found any more
        Assert.Equal("1bbfa943", CopyName.IdOf(Photo(@"C:\a\IMG.jpg", 12345, Taken)));
        Assert.Equal("c7ef13ca", CopyName.IdOf(Photo(@"C:\a\IMG.jpg", 12346, Taken)));
        Assert.Equal("3553b7b8", CopyName.IdOf(Photo(@"C:\a\IMG.jpg", 12345, Taken.AddSeconds(1))));
    }

    [Fact]
    public void IdOf_DoesNotDependOnTheFolderTheDriveOrTheFileName()
    {
        var id = CopyName.IdOf(Photo(@"C:\Photos\2015\IMG_0001.jpg"));

        Assert.Equal(id, CopyName.IdOf(Photo(@"Y:\OldFotik\Photo\100CANON\IMG_0001.jpg")));
        Assert.Equal(id, CopyName.IdOf(Photo(@"\\YURA\MiAX9000\somewhere\else\renamed.jpeg")));
        Assert.Equal(id, CopyName.IdOf(Photo("/home/me/photos/IMG_0001.jpg")));
    }

    [Fact]
    public void IdOf_IgnoresWhatIsBelowASecond()
    {
        // NTFS keeps 100 ns, other file systems whole seconds
        Assert.Equal(CopyName.IdOf(Photo("a.jpg")), CopyName.IdOf(Photo("a.jpg", lastWriteUtc: Taken.AddTicks(9_999_999))));
    }

    [Fact]
    public void IdOf_DiffersWhenThePhotoIsAnother()
    {
        var id = CopyName.IdOf(Photo("a.jpg"));

        Assert.NotEqual(id, CopyName.IdOf(Photo("a.jpg", length: 12346)));
        Assert.NotEqual(id, CopyName.IdOf(Photo("a.jpg", lastWriteUtc: Taken.AddSeconds(1))));
    }

    [Fact]
    public void IdOf_IsEightLowercaseHexDigits()
    {
        var id = CopyName.IdOf(Photo("a.jpg"));

        Assert.Equal(CopyName.IdLength, id.Length);
        Assert.Matches("^[0-9a-f]+$", id);
    }

    // ── the name ────────────────────────────────────────────────────────────

    [Fact]
    public void For_PutsTheIdBetweenTheNameAndTheExtension()
    {
        Assert.Equal("IMG_0001 [1bbfa943].JPG", CopyName.For(Photo(@"Y:\Photo\100CANON\IMG_0001.JPG")));
    }

    [Fact]
    public void For_KeepsDotsInTheNameAndAFileWithoutAnExtension()
    {
        Assert.Equal("a.b.c [1bbfa943].jpg", CopyName.For(Photo(@"C:\a.b.c.jpg")));
        Assert.Equal("raw [1bbfa943]", CopyName.For(Photo(@"C:\raw")));
    }

    [Fact]
    public void For_PhotosOfTheSameNameInDifferentFolders_GetDifferentNames()
    {
        var first = Photo(@"C:\Photos\100CANON\IMG_0001.JPG", length: 4_100_000, lastWriteUtc: Taken);
        var second = Photo(@"C:\Photos\101CANON\IMG_0001.JPG", length: 3_900_000, lastWriteUtc: Taken.AddDays(40));

        Assert.NotEqual(CopyName.For(first), CopyName.For(second));
    }

    // ── reading a name back ─────────────────────────────────────────────────

    [Fact]
    public void TryGetId_GivesTheNameOfThePhotoAndTheId()
    {
        Assert.True(CopyName.TryGetId("IMG_0001 [1bbfa943].JPG", out var photoName, out var id));
        Assert.Equal("IMG_0001.JPG", photoName);
        Assert.Equal("1bbfa943", id);
    }

    [Fact]
    public void TryGetId_ReadsWhatForWrote()
    {
        var photo = Photo(@"C:\x\some [odd] name (2).jpg");

        Assert.True(CopyName.TryGetId(CopyName.For(photo), out var photoName, out var id));
        Assert.Equal("some [odd] name (2).jpg", photoName);
        Assert.Equal(CopyName.IdOf(photo), id);
    }

    [Fact]
    public void TryGetId_OnlyTakesOffTheLastSuffix()
    {
        // The photo itself was called "IMG [1bbfa943].jpg"
        Assert.True(CopyName.TryGetId("IMG [1bbfa943] [c7ef13ca].jpg", out var photoName, out var id));
        Assert.Equal("IMG [1bbfa943].jpg", photoName);
        Assert.Equal("c7ef13ca", id);
    }

    [Theory]
    [InlineData("IMG_0001.JPG")]
    [InlineData("IMG_0001 (2).JPG")]
    [InlineData("IMG_0001[1bbfa943].JPG")]      // no space
    [InlineData("IMG_0001 [1bbfa94].JPG")]      // seven digits
    [InlineData("IMG_0001 [1bbfa9433].JPG")]    // nine digits
    [InlineData("IMG_0001 [1bbfa94g].JPG")]     // not hex
    [InlineData(" [1bbfa943].JPG")]             // nothing before it
    [InlineData("IMG_0001 [1bbfa943] .JPG")]    // not at the end
    public void TryGetId_OtherNames_HaveNoId(string name)
    {
        Assert.False(CopyName.TryGetId(name, out var photoName, out var id));
        Assert.Equal(name, photoName);
        Assert.Equal(string.Empty, id);
    }

    [Fact]
    public void TryGetId_AcceptsUppercaseDigits()
    {
        Assert.True(CopyName.TryGetId("IMG [1BBFA943].jpg", out var photoName, out var id));
        Assert.Equal("IMG.jpg", photoName);
        Assert.Equal("1BBFA943", id);
    }

    // ── which photos a file can be the copy of ──────────────────────────────

    [Fact]
    public void PhotoNamesOf_ACopyWithAnId_BelongsToThePhotoWithoutIt()
    {
        Assert.Equal(["a [1bbfa943].jpg", "a.jpg"], CopyName.PhotoNamesOf("a [1bbfa943].jpg"));
    }

    [Fact]
    public void PhotoNamesOf_ACopyMadeBeforeTheIds_BelongsToThePhotoOfThatName()
    {
        Assert.Equal(["a.jpg"], CopyName.PhotoNamesOf("a.jpg"));
    }

    [Fact]
    public void PhotoNamesOf_ASecondCopyMadeBeforeTheIds_IsAlsoTheCopyOfThePlainName()
    {
        Assert.Equal(["a (2).jpg", "a.jpg"], CopyName.PhotoNamesOf("a (2).jpg"));
        Assert.Equal(["a (12).jpg", "a.jpg"], CopyName.PhotoNamesOf("a (12).jpg"));
    }

    [Theory]
    [InlineData("a (1).jpg")]    // the first is "a.jpg"; "(1)" is what Windows makes
    [InlineData("a (0).jpg")]
    [InlineData("a(2).jpg")]
    [InlineData("a (2) x.jpg")]
    [InlineData("a (99999999999999999999).jpg")]
    public void PhotoNamesOf_OtherNames_AreOnlyThemselves(string name)
    {
        Assert.Equal([name], CopyName.PhotoNamesOf(name));
    }
}
