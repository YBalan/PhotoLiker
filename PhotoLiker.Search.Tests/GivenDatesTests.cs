namespace PhotoLiker.Search.Tests;

/// <summary>The rules of "Use a date..." of the Align dates tool that do not need a window: who may be given a date, what planning does with it, when it is written into the file.</summary>
public class GivenDatesTests
{
    private static readonly DateTime FileDate = new(2011, 5, 23, 0, 25, 4, DateTimeKind.Utc);
    private static readonly DateTime Given = new(2012, 7, 14, 9, 30, 0);

    private static PhotoDateFacts Facts(string name, DateTime? taken, string? problem = null, ExifDateSlots? exif = null) =>
        new(Path.Combine(@"C:\Library", name), exif ?? ExifDateSlots.None, taken, FileDate, FileDate, problem);

    // ── who can be given a date ────────────────────────────────────────

    [Fact]
    public void AFileThatCouldBeRead_CanBeGivenADate_WhetherOrNotItHasOne()
    {
        Assert.True(GivenDates.CanBeGiven(Facts("a.jpg", null)));
        Assert.True(GivenDates.CanBeGiven(Facts("a.jpg", new DateTime(2009, 9, 1))));  // the person may want it dated by another rule than the rest
        Assert.False(GivenDates.CanBeGiven(Facts("a.jpg", null, problem: "locked")));
    }

    [Fact]
    public void OnlyAReadableFileWithoutABelievedDate_IsWaitingForOne()
    {
        Assert.True(GivenDates.WaitsForDate(Facts("a.jpg", null)));
        Assert.False(GivenDates.WaitsForDate(Facts("a.jpg", new DateTime(2009, 9, 1))));
        Assert.False(GivenDates.WaitsForDate(Facts("a.jpg", null, problem: "locked")));
    }

    // ── when the date goes into the file ───────────────────────────────

    [Theory]
    [InlineData("a.jpg", true, true)]
    [InlineData("a.jpg", false, false)]      // the tick is off
    [InlineData("clip.mp4", true, true)]
    [InlineData("clip.MOV", true, true)]
    [InlineData("clip.avi", true, true)]
    [InlineData("clip.3gp", true, true)]
    [InlineData("clip.mp4", false, false)]
    [InlineData("clip.mkv", true, false)]    // a kind that keeps no date
    [InlineData("clip.mts", true, false)]
    [InlineData("clip.wmv", true, false)]
    public void TheDateIsWrittenIntoAFile_WhenAskedFor_AndItsKindHasAPlace(string name, bool tick, bool written)
    {
        Assert.Equal(written, GivenDates.WritesInside(Facts(name, new DateTime(2003, 1, 31, 18, 6, 0)), Given, tick));
    }

    [Fact]
    public void AVideoThatSaysTheGivenDateAlready_IsNotWrittenAgain_ButOneThatSaysNoneOrAnotherIs()
    {
        Assert.False(GivenDates.WritesInside(Facts("clip.mp4", Given), Given, tick: true));
        Assert.False(GivenDates.WritesInside(Facts("clip.mp4", Given.AddSeconds(1)), Given, tick: true)); // within the two seconds a file system keeps
        Assert.True(GivenDates.WritesInside(Facts("clip.mp4", Given.AddMinutes(1)), Given, tick: true));
        Assert.True(GivenDates.WritesInside(Facts("clip.mp4", null), Given, tick: true));                // a date that is not believed is no date
    }

    [Fact]
    public void APhotoIsWrittenEvenIfItSaysThatDateAlready_BecauseThePlannerLooksAtTheExifDateItself()
    {
        Assert.True(GivenDates.WritesInside(Facts("a.jpg", Given), Given, tick: true));
    }

    // ── planning ───────────────────────────────────────────────────────

    [Fact]
    public void WithoutAGivenDate_AFileIsPlannedByItsOwn()
    {
        var entry = GivenDates.Plan(Facts("a.jpg", new DateTime(2009, 9, 1, 14, 30, 0)), DateAlignmentOptions.Default, null);

        Assert.False(entry.Assigned);
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), entry.Facts.Taken);
        Assert.True(entry.SetCreated && entry.SetModified);
    }

    [Fact]
    public void ADateGivenToAVideoOfTheFileDate_LeavesItsFileDatesAlone_AndWritesTheDateRecorded()
    {
        // the case of the report: recorded in 2003, the file dates are those of 2011, and the person says the date of the file is the date
        var video = Facts("SDV_0667.mp4", new DateTime(2003, 1, 31, 18, 6, 0));
        var fileDate = DateTime.SpecifyKind(FileDate, DateTimeKind.Utc).ToLocalTime();

        var entry = GivenDates.Plan(video, DateAlignmentOptions.Default, (fileDate, true));

        Assert.Equal(DateAlignmentStatus.Change, entry.Status);
        Assert.False(entry.SetCreated || entry.SetModified);
        Assert.Equal(ExifDateFields.Original, entry.SetExif);
        Assert.True(entry.Assigned);
    }

    [Fact]
    public void WithTheTickOff_ADateGivenToAVideoOfTheFileDate_NeedsNoChangeAtAll()
    {
        var video = Facts("SDV_0667.mp4", new DateTime(2003, 1, 31, 18, 6, 0));
        var fileDate = DateTime.SpecifyKind(FileDate, DateTimeKind.Utc).ToLocalTime();

        var entry = GivenDates.Plan(video, DateAlignmentOptions.Default, (fileDate, false));

        Assert.Equal(DateAlignmentStatus.Aligned, entry.Status);
        Assert.Equal(ExifDateFields.None, entry.SetExif);
    }

    [Fact]
    public void ADateGivenToAVideoOfAKindWithNoPlace_ChangesItsFileDatesOnly()
    {
        var entry = GivenDates.Plan(Facts("clip.mkv", new DateTime(2003, 1, 31, 18, 6, 0)), DateAlignmentOptions.Default, (Given, true));

        Assert.True(entry.SetCreated && entry.SetModified);
        Assert.Equal(ExifDateFields.None, entry.SetExif);
    }

    [Fact]
    public void TheDatesInsideThePhotoToo_AreForPhotosOnly_AVideoHasNoExif()
    {
        var options = new DateAlignmentOptions(Created: true, Modified: true, ExifFields: true);
        var slots = new ExifDateSlots(new ExifDates(new DateTime(2009, 9, 1), new DateTime(2009, 9, 1), null), 100, 200, null);

        var photo = GivenDates.Plan(Facts("a.jpg", new DateTime(2010, 1, 1), exif: slots), options, null);
        var video = GivenDates.Plan(Facts("a.mp4", new DateTime(2010, 1, 1), exif: slots), options, null);

        Assert.NotEqual(ExifDateFields.None, photo.SetExif);
        Assert.Equal(ExifDateFields.None, video.SetExif);
    }

    [Fact]
    public void ADateGivenToAPhotoThatHasOne_ReplacesIt_AndIsWrittenWhenAsked()
    {
        var photo = Facts("a.jpg", new DateTime(2009, 9, 1, 14, 30, 0));

        var written = GivenDates.Plan(photo, DateAlignmentOptions.Default, (Given, true));
        var notWritten = GivenDates.Plan(photo, DateAlignmentOptions.Default, (Given, false));

        Assert.Equal(Given, written.Facts.Taken);
        Assert.True(written.SetExif.HasFlag(ExifDateFields.Original));
        Assert.Equal(Given, notWritten.Facts.Taken);
        Assert.False(notWritten.SetExif.HasFlag(ExifDateFields.Original));
    }

    [Fact]
    public void ADateGivenToAnUnreadableFile_ChangesNothing()
    {
        var entry = GivenDates.Plan(Facts("a.jpg", null, problem: "locked"), DateAlignmentOptions.Default, (Given, true));

        Assert.Equal(DateAlignmentStatus.Unreadable, entry.Status);
    }

    // ── the files that cannot hold the date ────────────────────────────

    [Fact]
    public void WithTheTickOn_OnlyVideosOfAKindWithNoPlace_CannotHoldTheDate()
    {
        var scope = new[] { Facts("a.jpg", null), Facts("a.mp4", null), Facts("a.mkv", null), Facts("a.mts", null) };

        Assert.Equal(2, GivenDates.WithoutPlace(scope, tick: true));
    }

    [Fact]
    public void WithTheTickOff_OnlyPhotosThatHaveNoPlaceForADate_AreCounted()
    {
        var placed = new ExifDateSlots(new ExifDates(new DateTime(2009, 9, 1), null, null), 100, null, null);
        var scope = new[] { Facts("a.jpg", null), Facts("b.jpg", null, exif: placed), Facts("a.mkv", null), Facts("a.mp4", null) };

        Assert.Equal(1, GivenDates.WithoutPlace(scope, tick: false)); // a.jpg; the videos are not counted: nothing is written into them with the tick off
    }
}
