using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

public class DateSourcesTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0);
    private const string Root = @"C:\Library";

    [Theory]
    [InlineData("2015-05-01", 2015, 5, 1)]
    [InlineData("2015_05_01", 2015, 5, 1)]
    [InlineData("2015.05.01", 2015, 5, 1)]
    [InlineData("20150501", 2015, 5, 1)]
    [InlineData("01.05.2015", 2015, 5, 1)]
    [InlineData("2015-05-01 Anna's birthday", 2015, 5, 1)]
    [InlineData("Crete 2012-07-14", 2012, 7, 14)]
    [InlineData("2016-01-01", 2016, 1, 1)]
    public void ADayInTheNameOfAFolder_IsRead(string name, int year, int month, int day)
    {
        Assert.True(DateSources.TryParseFolderDate(name, out var date));
        Assert.Equal(new DateTime(year, month, day), date);
    }

    [Theory]
    [InlineData("2015-05")]      // a month is not a day
    [InlineData("2015")]
    [InlineData("107_2311")]     // a camera's folder
    [InlineData("150_0105")]
    [InlineData("135___07")]
    [InlineData("314CANON")]
    [InlineData("2015-13-01")]   // no such month
    [InlineData("2015-02-30")]   // no such day
    [InlineData("12345678")]
    [InlineData("Holidays")]
    public void ANameThatHoldsNoDay_IsNotADate(string name)
    {
        Assert.False(DateSources.TryParseFolderDate(name, out _));
    }

    [Fact]
    public void TheInnermostFolderWithADate_Decides_AtNoonUnlessThePhotoHasATimeOfItsOwn()
    {
        var path = Path.Combine(Root, "2015", "2015-05-01 Anna", "sub", "a.jpg");

        Assert.Equal(new DateTime(2015, 5, 1, 12, 0, 0), DateSources.FromFolderName(Root, path, null));
        Assert.Equal(new DateTime(2015, 5, 1, 0, 30, 12), DateSources.FromFolderName(Root, path, new DateTime(2001, 1, 1, 0, 30, 12)));
        Assert.Equal(new DateTime(2014, 11, 23, 12, 0, 0), DateSources.FromFolderName(Root, Path.Combine(Root, "2014-11-23", "2015-05", "a.jpg"), null));
    }

    [Fact]
    public void WithoutAFolderThatHasADate_ThereIsNoDateFromTheFolderName()
    {
        Assert.Null(DateSources.FromFolderName(Root, Path.Combine(Root, "107_2311", "a.jpg"), null));
        Assert.Null(DateSources.FromFolderName(Root, Path.Combine(Root, "a.jpg"), null)); // directly in the photo folder
        Assert.Null(DateSources.FromFolderName(Root, @"D:\Elsewhere\2015-05-01\a.jpg", null)); // outside of it: the folders above the photo folder do not count
    }

    [Fact]
    public void TheDateOfTheFile_IsWhenItWasCreated_ElseWhenItWasChanged_AsLongAsItIsBelievable()
    {
        var created = new DateTime(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);
        var modified = new DateTime(2009, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        PhotoDateFacts Facts(DateTime c, DateTime m) => new("a.jpg", ExifDateSlots.None, null, c, m);

        Assert.Equal(created.ToLocalTime(), DateSources.FromFile(Facts(created, modified), Now));
        Assert.Equal(modified.ToLocalTime(), DateSources.FromFile(Facts(default, modified), Now));          // no creation date: the year 1
        Assert.Equal(modified.ToLocalTime(), DateSources.FromFile(Facts(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc), modified), Now)); // before there were such cameras
        Assert.Null(DateSources.FromFile(Facts(default, default), Now));
        Assert.Null(DateSources.FromFile(Facts(Now.AddYears(3).ToUniversalTime(), default), Now));          // in the future
    }

    [Fact]
    public void TheDateTheCameraWrote_IsTakenWhetherItIsBelievedOrNot_FirstTheOriginalOne()
    {
        var original = new DateTime(2001, 1, 1, 0, 0, 5);
        var digitized = new DateTime(2002, 2, 2, 2, 2, 2);

        Assert.Equal(original, DateSources.FromExif(new ExifDates(original, digitized, null)));
        Assert.Equal(digitized, DateSources.FromExif(new ExifDates(null, digitized, new DateTime(2003, 3, 3))));
        Assert.Null(DateSources.FromExif(ExifDates.None));
    }

    [Fact]
    public void EveryChoice_GivesTheDateFromItsOwnPlace()
    {
        var exif = new ExifDates(new DateTime(2016, 1, 1, 0, 30, 12), null, null);
        var facts = new PhotoDateFacts(Path.Combine(Root, "2015-05-01", "a.jpg"), new ExifDateSlots(exif, null, null, null), null,
            new DateTime(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc), default);
        var typed = new DateTime(1999, 12, 31, 23, 59, 0);

        Assert.Equal(exif.Original, DateSources.Resolve(DateChoice.ExifAsItIs, facts, Root, typed, Now));
        Assert.Equal(new DateTime(2015, 5, 1, 0, 30, 12), DateSources.Resolve(DateChoice.FolderName, facts, Root, typed, Now));
        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc).ToLocalTime(), DateSources.Resolve(DateChoice.FileDate, facts, Root, typed, Now));
        Assert.Equal(typed, DateSources.Resolve(DateChoice.Typed, facts, Root, typed, Now));
    }
}

public class AssignedDateTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0);
    private static readonly DateTime CopyDate = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _photos = new();

    public void Dispose() => _photos.Dispose();

    private string Photo(string relative, byte[] bytes)
    {
        var path = _photos.File(relative);
        File.WriteAllBytes(path, bytes);
        File.SetCreationTimeUtc(path, CopyDate);
        File.SetLastWriteTimeUtc(path, CopyDate);
        return path;
    }

    private static PhotoDateFacts Look(string path) => DateAlignment.Inspect(new PhotoFile(path, string.Empty), Now);

    private DateAlignmentReport Run(DateAlignmentEntry entry) =>
        DateAligner.Apply([entry], _photos.Path, DateAligner.NewLogFile(_photos.Path, Now), null, null, CancellationToken.None);

    [Fact]
    public void APhotoWithoutAUsableDate_GetsOneFromThePerson_AndIsThenPlannedLikeAnyOther()
    {
        var path = Photo(@"2015-05-01\a.jpg", ExifJpeg.Build(original: "2001:01:01 00:00:05")); // a camera that lost its clock
        var facts = Look(path);
        var given = new DateTime(2015, 5, 1, 12, 0, 0);

        var without = DateAlignment.Plan(facts, DateAlignmentOptions.Default);
        var with = DateAlignment.Plan(facts, DateAlignmentOptions.Default, given);

        Assert.Equal(DateAlignmentStatus.NoDate, without.Status);
        Assert.False(without.Assigned);
        Assert.Equal(DateAlignmentStatus.Change, with.Status);
        Assert.True(with.Assigned);
        Assert.Equal(given, with.Facts.Taken);
        Assert.True(with.SetCreated && with.SetModified);
    }

    [Fact]
    public void AGivenDate_IsIgnored_ForAPhotoThatHasAUsableOne()
    {
        var path = Photo(@"a.jpg", ExifJpeg.Build(original: "2009:09:01 14:30:00"));

        var entry = DateAlignment.Plan(Look(path), DateAlignmentOptions.Default, new DateTime(2015, 5, 1));

        Assert.False(entry.Assigned);
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), entry.Facts.Taken);
    }

    [Fact]
    public void AGivenDate_DoesNotMakeAnUnreadablePhotoReadable()
    {
        var entry = DateAlignment.Plan(new PhotoDateFacts("gone.jpg", ExifDateSlots.None, null, default, default, "locked"), DateAlignmentOptions.Default, new DateTime(2015, 5, 1));

        Assert.Equal(DateAlignmentStatus.Unreadable, entry.Status);
    }

    [Fact]
    public void TheDateIsWrittenInsideThePhotoToo_AndThePhotoIsUsableAfterwards()
    {
        var path = Photo(@"2015-05-01\a.jpg", ExifJpeg.Build(original: "2001:01:01 00:00:05", digitized: "2001:01:01 00:00:05"));
        var facts = Look(path);
        Assert.Null(facts.Taken);
        var given = DateSources.Resolve(DateChoice.FolderName, facts, _photos.Path, null, Now)!.Value; // the folder says 1 May 2015, the time stays

        var entry = DateAlignment.Plan(facts, new DateAlignmentOptions(ExifFields: true), given);
        Run(entry);

        var after = Look(path);
        Assert.Equal(new DateTime(2015, 5, 1, 0, 0, 5), after.Taken); // believed now: the camera's own date says it
        Assert.Equal(new DateTime(2015, 5, 1, 0, 0, 5), after.Exif.Dates.Original);
        Assert.Equal(new DateTime(2015, 5, 1, 0, 0, 5), after.Exif.Dates.Digitized);
        Assert.Equal(DateAlignmentStatus.Aligned, DateAlignment.Plan(after, new DateAlignmentOptions(true, true, true)).Status);
    }

    [Fact]
    public void APhotoWithNoPlaceForADateInsideIt_GetsItsFileDatesOnly()
    {
        var path = Photo(@"2015-05-01\a.jpg", [0xFF, 0xD8, 0xFF, 0xD9]); // no EXIF at all
        var facts = Look(path);

        var entry = DateAlignment.Plan(facts, new DateAlignmentOptions(ExifFields: true), new DateTime(2015, 5, 1, 12, 0, 0));
        Run(entry);

        Assert.Equal(ExifDateFields.None, entry.SetExif);
        var info = new FileInfo(path);
        Assert.Equal(new DateTime(2015, 5, 1, 12, 0, 0), PhotoDates.FileTime(info.CreationTimeUtc));
        Assert.Equal(new DateTime(2015, 5, 1, 12, 0, 0), PhotoDates.FileTime(info.LastWriteTimeUtc));
        // which is the date the app goes by for such a photo
        Assert.Equal(new DateTime(2015, 5, 1, 12, 0, 0), PhotoDates.FromFile(new PhotoFile(path, string.Empty) { CreationTimeUtc = info.CreationTimeUtc, LastWriteTimeUtc = info.LastWriteTimeUtc }, Now));
    }

    [Fact]
    public void ARunWithAGivenDate_CanBeUndoneLikeAnyOther()
    {
        var path = Photo(@"2015-05-01\a.jpg", ExifJpeg.Build(original: "2001:01:01 00:00:05"));
        var before = File.ReadAllBytes(path);
        var report = Run(DateAlignment.Plan(Look(path), new DateAlignmentOptions(ExifFields: true), new DateTime(2015, 5, 1, 12, 0, 0)));
        Assert.NotEqual(before, File.ReadAllBytes(path));

        DateAligner.Undo(report.LogFile, null, CancellationToken.None);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(DateAlignment.SameMoment(CopyDate, new FileInfo(path).CreationTimeUtc));
    }
}
