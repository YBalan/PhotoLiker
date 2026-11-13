using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>The date that is written in the corner of the viewer: the date taken by the rules of the slideshow and the search, out of the rows the grid already has.</summary>
public class ShownDateTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0);

    // A copy: the file is much younger than the photo
    private static readonly DateTime CopiedUtc = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    private static MetadataEntry Row(int tagId, string value) => new(tagId, "row " + tagId, value);

    private static DateTime? ForPhoto(params MetadataEntry[] rows) => ShownDate.Of(rows, video: false, CopiedUtc, CopiedUtc, Now);

    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    [Fact]
    public void TheDateTaken_IsTheDateOfThePhoto_NotThatOfItsFile()
    {
        var date = ForPhoto(Row(DateRowEdit.DateTakenTagId, "01 Sep 2009 14:30:00"));

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), date);
    }

    [Fact]
    public void TheDatesAreReadTheWayTheGridShowsThem_AndTheWayExifHasThem()
    {
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), ForPhoto(Row(DateRowEdit.DateTakenTagId, "2009:09:01 14:30:00")));
    }

    [Fact]
    public void WithoutADateTaken_TheDigitizedDateAndThenTheLastSavedOneAreUsed()
    {
        var digitized = ForPhoto(Row(DateRowEdit.DigitizedTagId, "02 Sep 2009 08:00:00"), Row(DateRowEdit.LastSavedTagId, "03 Sep 2009 09:00:00"));
        var saved = ForPhoto(Row(DateRowEdit.LastSavedTagId, "03 Sep 2009 09:00:00"));

        Assert.Equal(new DateTime(2009, 9, 2, 8, 0, 0), digitized);
        Assert.Equal(new DateTime(2009, 9, 3, 9, 0, 0), saved);
    }

    [Fact]
    public void ADateThatCannotBeBelieved_GivesWayToTheDateOfTheFile()
    {
        // A camera that lost its clock writes the first of January into every photo
        var date = ForPhoto(Row(DateRowEdit.DateTakenTagId, "01 Jan 2001 00:00:00"));

        Assert.Equal(Local(CopiedUtc), date);
    }

    [Fact]
    public void APhotoWithNoDateInIt_HasTheDateOfItsFile()
    {
        Assert.Equal(Local(CopiedUtc), ForPhoto(Row(0x010F, "Canon")));
        Assert.Equal(Local(CopiedUtc), ForPhoto());
    }

    [Fact]
    public void WithNoDateAnywhere_ThereIsNoneToWrite()
    {
        Assert.Null(ShownDate.Of([], video: false, default, default, Now));
    }

    [Fact]
    public void ADateThatIsNotADate_IsIgnored()
    {
        Assert.Equal(Local(CopiedUtc), ForPhoto(Row(DateRowEdit.DateTakenTagId, "yesterday")));
    }

    [Fact]
    public void AVideo_HasTheDateItWasRecorded()
    {
        var date = ShownDate.Of([Row(VideoMetadata.RecordedTagId, DateText.Display(new DateTime(2003, 1, 31, 15, 6, 0)))], video: true, CopiedUtc, CopiedUtc, Now);

        Assert.Equal(new DateTime(2003, 1, 31, 15, 6, 0), date);
    }

    [Fact]
    public void AVideoThatSaysItHasNoDate_HasNoneToWrite_EvenIfItsFileHasOne()
    {
        // The Recorded row says "none" when the catalog could not believe any date; the file is not asked again behind its back
        Assert.Null(ShownDate.Of([Row(VideoMetadata.RecordedTagId, "none")], video: true, CopiedUtc, CopiedUtc, Now));
        Assert.Null(ShownDate.Of([], video: true, CopiedUtc, CopiedUtc, Now));
    }
}
