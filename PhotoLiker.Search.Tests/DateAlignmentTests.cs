using System.Text;
using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

public class ExifDateSlotTests
{
    private static string TextAt(byte[] jpeg, long? offset) =>
        Encoding.ASCII.GetString(jpeg, (int)offset!.Value, ExifReader.DateTextLength);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheSlotOfADate_IsWhereItsTextIsInTheFile(bool bigEndian, bool withJfif)
    {
        var jpeg = ExifJpeg.Build("2009:09:01 14:30:00", "2009:09:02 08:00:01", "2015:01:20 10:00:00", bigEndian, withJfif);

        var slots = ExifReader.Locate(new MemoryStream(jpeg));

        Assert.Equal("2009:09:01 14:30:00", TextAt(jpeg, slots.OriginalOffset));
        Assert.Equal("2009:09:02 08:00:01", TextAt(jpeg, slots.DigitizedOffset));
        Assert.Equal("2015:01:20 10:00:00", TextAt(jpeg, slots.ModifiedOffset));
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), slots.Dates.Original);
    }

    [Fact]
    public void TheSlots_AreTheSameForAStreamThatCanNotSeek()
    {
        var jpeg = ExifJpeg.Build("2009:09:01 14:30:00", withJfif: true);

        using var stream = new ExifReaderTests.NonSeekingStream(jpeg);

        Assert.Equal(ExifReader.Locate(new MemoryStream(jpeg)), ExifReader.Locate(stream));
    }

    [Fact]
    public void ADateThatIsNotThere_HasNoSlot()
    {
        var slots = ExifReader.Locate(new MemoryStream(ExifJpeg.Build(original: "2009:09:01 14:30:00")));

        Assert.NotNull(slots.OriginalOffset);
        Assert.Null(slots.DigitizedOffset);
        Assert.Null(slots.ModifiedOffset);
    }

    [Fact]
    public void ADateThatIsNotStoredAsTheStandardSays_HasNoSlot()
    {
        // 18 characters and a zero: it would not fill the place of a date
        var slots = ExifReader.Locate(new MemoryStream(ExifJpeg.Build(original: "2009-9-1 14:30:00")));

        Assert.Null(slots.OriginalOffset);
    }

    [Fact]
    public void ABlankDate_HasASlot_ButNoValue()
    {
        var slots = ExifReader.Locate(new MemoryStream(ExifJpeg.Build(original: "    :  :     :  :  ")));

        Assert.NotNull(slots.OriginalOffset);
        Assert.Null(slots.Dates.Original);
    }

    [Fact]
    public void AFileWithoutExif_HasNoSlots()
    {
        Assert.Equal(ExifDateSlots.None, ExifReader.Locate(new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9])));
    }

    [Fact]
    public void TheTextOfADate_IsWhatTheStandardWrites_AndReadsBack()
    {
        var date = new DateTime(2009, 9, 1, 4, 5, 6);

        Assert.Equal("2009:09:01 04:05:06", Encoding.ASCII.GetString(ExifReader.FormatDate(date)));
        Assert.Equal(date, ExifReader.ParseDate(ExifReader.FormatDate(date)));
    }
}

public class DateAlignmentPlanTests
{
    private static readonly DateTime Taken = new(2009, 9, 1, 14, 30, 0);
    private static readonly DateTime TakenUtc = DateAlignment.ToUtc(Taken);

    private static PhotoDateFacts Facts(DateTime? taken, DateTime createdUtc, DateTime modifiedUtc, ExifDates? dates = null, bool slots = true) =>
        new("a.jpg", new ExifDateSlots(dates ?? new ExifDates(taken, taken, taken), slots ? 100 : null, slots ? 200 : null, slots ? 300 : null),
            taken, createdUtc, modifiedUtc);

    [Fact]
    public void FilesWithOtherDates_AreChanged_ByDefaultOnlyTheDatesOfTheFile()
    {
        var entry = DateAlignment.Plan(Facts(Taken, TakenUtc.AddDays(900), TakenUtc.AddDays(900), new ExifDates(Taken, Taken.AddHours(1), Taken.AddYears(1))), DateAlignmentOptions.Default);

        Assert.Equal(DateAlignmentStatus.Change, entry.Status);
        Assert.True(entry.SetCreated);
        Assert.True(entry.SetModified);
        Assert.Equal(ExifDateFields.None, entry.SetExif); // the dates inside the photo have to be asked for
    }

    [Fact]
    public void FilesThatHaveTheDateAlready_AreAligned_WithinTheTolerance()
    {
        var exact = DateAlignment.Plan(Facts(Taken, TakenUtc, TakenUtc), DateAlignmentOptions.Default);
        var rounded = DateAlignment.Plan(Facts(Taken, TakenUtc.AddSeconds(1.9), TakenUtc.AddSeconds(-2)), DateAlignmentOptions.Default);
        var off = DateAlignment.Plan(Facts(Taken, TakenUtc.AddSeconds(3), TakenUtc), DateAlignmentOptions.Default);

        Assert.Equal(DateAlignmentStatus.Aligned, exact.Status);
        Assert.Equal(DateAlignmentStatus.Aligned, rounded.Status);
        Assert.Equal(DateAlignmentStatus.Change, off.Status);
        Assert.True(off.SetCreated);
        Assert.False(off.SetModified);
    }

    [Fact]
    public void OnlyTheDatesThatAreAskedFor_AreChanged()
    {
        var facts = Facts(Taken, TakenUtc.AddDays(5), TakenUtc.AddDays(9));

        var created = DateAlignment.Plan(facts, new DateAlignmentOptions(Created: true, Modified: false));
        var modified = DateAlignment.Plan(facts, new DateAlignmentOptions(Created: false, Modified: true));
        var nothing = DateAlignment.Plan(facts, new DateAlignmentOptions(false, false, false));

        Assert.True(created.SetCreated);
        Assert.False(created.SetModified);
        Assert.False(modified.SetCreated);
        Assert.True(modified.SetModified);
        Assert.Equal(DateAlignmentStatus.Aligned, nothing.Status);
    }

    [Fact]
    public void TheDatesInsideThePhoto_FollowTheDateTaken_WhenAskedFor()
    {
        var facts = Facts(Taken, TakenUtc, TakenUtc, new ExifDates(Taken, Taken.AddHours(1), null));

        var entry = DateAlignment.Plan(facts, new DateAlignmentOptions(ExifFields: true));

        Assert.Equal(DateAlignmentStatus.Change, entry.Status);
        Assert.Equal(ExifDateFields.Digitized | ExifDateFields.Modified, entry.SetExif); // a blank date is written too; the original is right
    }

    [Fact]
    public void ADateWithoutAPlaceInTheFile_IsNotWritten()
    {
        var facts = Facts(Taken, TakenUtc, TakenUtc, new ExifDates(Taken, Taken.AddHours(1), Taken.AddHours(2)), slots: false);

        Assert.Equal(DateAlignmentStatus.Aligned, DateAlignment.Plan(facts, new DateAlignmentOptions(ExifFields: true)).Status);
    }

    [Fact]
    public void APhotoWithoutABelievableExifDate_IsLeftAlone_WhateverIsAskedFor()
    {
        var entry = DateAlignment.Plan(Facts(null, TakenUtc, TakenUtc, new ExifDates(new DateTime(2001, 1, 1), null, null)), new DateAlignmentOptions(true, true, true));

        Assert.Equal(DateAlignmentStatus.NoDate, entry.Status);
        Assert.False(entry.SetCreated);
        Assert.False(entry.SetModified);
        Assert.Equal(ExifDateFields.None, entry.SetExif);
    }

    [Fact]
    public void APhotoThatCanNotBeRead_IsReportedAsSuch()
    {
        var entry = DateAlignment.Plan(Facts(Taken, TakenUtc, TakenUtc) with { Problem = "locked" }, DateAlignmentOptions.Default);

        Assert.Equal(DateAlignmentStatus.Unreadable, entry.Status);
    }

    [Fact]
    public void WhatTheCameraSaid_IsTakenAsTheClockOfThisComputer()
    {
        Assert.Equal(DateTimeKind.Utc, DateAlignment.ToUtc(Taken).Kind);
        Assert.Equal(Taken, DateAlignment.ToUtc(Taken).ToLocalTime());
    }
}

public class DateAlignerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0);
    private static readonly DateTime CopyDate = new(2025, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    private readonly TempFolder _photos = new();

    public void Dispose() => _photos.Dispose();

    private string Photo(string relative, string original, string? digitized = null, string? modified = null, DateTime? created = null, DateTime? lastWrite = null)
    {
        var path = _photos.File(relative);
        File.WriteAllBytes(path, ExifJpeg.Build(original, digitized, modified));
        File.SetCreationTimeUtc(path, created ?? CopyDate);
        File.SetLastWriteTimeUtc(path, lastWrite ?? created ?? CopyDate);
        return path;
    }

    private static PhotoDateFacts Look(string path) => DateAlignment.Inspect(new PhotoFile(path, string.Empty), Now);

    private static DateAlignmentEntry Plan(string path, DateAlignmentOptions? options = null) => DateAlignment.Plan(Look(path), options ?? DateAlignmentOptions.Default);

    private DateAlignmentReport Run(IReadOnlyList<DateAlignmentEntry> entries, Func<string, IReadOnlyList<string>>? copiesOf = null, CancellationToken token = default) =>
        DateAligner.Apply(entries, _photos.Path, DateAligner.NewLogFile(_photos.Path, Now), copiesOf, null, token);

    private static void AssertDates(string path, DateTime takenLocal, DateTime? modifiedLocal = null)
    {
        var info = new FileInfo(path);
        Assert.Equal(takenLocal, PhotoDates.FileTime(info.CreationTimeUtc));
        Assert.Equal(modifiedLocal ?? takenLocal, PhotoDates.FileTime(info.LastWriteTimeUtc));
    }

    [Fact]
    public void TheDatesOfTheFile_BecomeTheDateTaken_AndThePictureIsNotTouched()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00");
        var before = File.ReadAllBytes(path);

        var report = Run([Plan(path)]);

        AssertDates(path, new DateTime(2009, 9, 1, 14, 30, 0));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal([path], report.Photos.Select(p => p.Path));
        Assert.Equal(CopyDate, report.Photos[0].PreviousModifiedUtc);
        Assert.Empty(report.Failures);
        Assert.Equal(DateAlignmentStatus.Aligned, Plan(path).Status); // and looking again confirms it
    }

    [Fact]
    public void TheDatesInsideThePhoto_AreWrittenInPlace_WithoutMovingAnything()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07", "2015:01:20 10:00:00");
        var length = new FileInfo(path).Length;

        Run([Plan(path, new DateAlignmentOptions(Created: false, Modified: false, ExifFields: true))]);

        var dates = ExifReader.Read(new MemoryStream(File.ReadAllBytes(path)));
        var taken = new DateTime(2009, 9, 1, 14, 30, 0);
        Assert.Equal(new ExifDates(taken, taken, taken), dates);
        Assert.Equal(length, new FileInfo(path).Length);
    }

    [Fact]
    public void WritingThePhoto_DoesNotMoveItsDateOfChange_WhenThatWasNotAskedFor()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07");

        Run([Plan(path, new DateAlignmentOptions(Created: true, Modified: false, ExifFields: true))]);

        var info = new FileInfo(path);
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), PhotoDates.FileTime(info.CreationTimeUtc));
        Assert.True(DateAlignment.SameMoment(CopyDate, info.LastWriteTimeUtc), $"the date of change is {info.LastWriteTimeUtc:O}");
    }

    [Fact]
    public void APhotoWhoseDatesAreRight_IsNotWrittenAtAll()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:00", "2009:09:01 14:30:00", DateAlignment.ToUtc(new DateTime(2009, 9, 1, 14, 30, 0)));

        var entry = Plan(path, new DateAlignmentOptions(true, true, true));

        Assert.Equal(DateAlignmentStatus.Aligned, entry.Status);
    }

    [Fact]
    public void ARunIsLogged_AndTheLogIsKeptNextToThePhotos()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07");

        var report = Run([Plan(path, new DateAlignmentOptions(ExifFields: true))]);

        Assert.Equal(_photos.Path, Path.GetDirectoryName(report.LogFile));
        Assert.Equal(report.LogFile, DateAligner.LatestLog(_photos.Path));
        var lines = File.ReadAllLines(report.LogFile);
        Assert.Equal(2, lines.Length); // the header and the photo
        Assert.Contains("a.jpg", lines[1]);
        Assert.Contains("2009:09:01 14:30:07", lines[1]); // what was there before
    }

    [Fact]
    public void AnUndo_PutsEverythingBack_AndTheLogIsDone()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07", "2015:01:20 10:00:00", CopyDate, CopyDate.AddHours(5));
        var before = File.ReadAllBytes(path);
        var report = Run([Plan(path, new DateAlignmentOptions(true, true, true))]);
        Assert.NotEqual(before, File.ReadAllBytes(path));

        var undone = DateAligner.Undo(report.LogFile, null, CancellationToken.None);

        Assert.Equal(before, File.ReadAllBytes(path));
        var info = new FileInfo(path);
        Assert.True(DateAlignment.SameMoment(CopyDate, info.CreationTimeUtc));
        Assert.True(DateAlignment.SameMoment(CopyDate.AddHours(5), info.LastWriteTimeUtc));
        Assert.Equal([path], undone.Photos.Select(p => p.Path));
        Assert.Equal(0, undone.Skipped);
        Assert.Null(DateAligner.LatestLog(_photos.Path)); // renamed: the next undo takes the run before
        Assert.True(File.Exists(report.LogFile[..^".jsonl".Length] + ".undone.jsonl"));
    }

    [Fact]
    public void AnUndo_LeavesAPhotoAlone_ThatWasChangedSinceTheRun()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00");
        var report = Run([Plan(path)]);
        var edited = new DateTime(2020, 5, 5, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, edited); // someone saved the photo again

        var undone = DateAligner.Undo(report.LogFile, null, CancellationToken.None);

        var info = new FileInfo(path);
        Assert.Equal(1, undone.Skipped);
        Assert.True(DateAlignment.SameMoment(edited, info.LastWriteTimeUtc)); // the edit stays
        Assert.True(DateAlignment.SameMoment(CopyDate, info.CreationTimeUtc)); // the created date was still the run's
    }

    [Fact]
    public void CopiesInTheLikedFolders_GetTheSameTimes_AndGoBackWithTheUndo()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00");
        var copy = Photo("liked/a.jpg", "2009:09:01 14:30:00");

        var report = Run([Plan(path)], _ => [copy]);

        Assert.Equal(1, report.Copies);
        AssertDates(copy, new DateTime(2009, 9, 1, 14, 30, 0));
        Assert.Single(report.Photos); // the copy is not a photo of the folder

        var undone = DateAligner.Undo(report.LogFile, null, CancellationToken.None);

        Assert.Equal(1, undone.Copies);
        Assert.True(DateAlignment.SameMoment(CopyDate, new FileInfo(copy).LastWriteTimeUtc));
    }

    [Fact]
    public void APhotoThatChangedAfterItWasLookedAt_IsNotWrittenInto()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07");
        var entry = Plan(path, new DateAlignmentOptions(ExifFields: true));
        File.WriteAllBytes(path, ExifJpeg.Build("2009:09:01 14:30:00", "2011:11:11 11:11:11")); // another photo under the same name

        var report = Run([entry]);

        Assert.Single(report.Failures);
        Assert.Empty(report.Photos);
        Assert.Equal(new DateTime(2011, 11, 11, 11, 11, 11), ExifReader.Read(new MemoryStream(File.ReadAllBytes(path))).Digitized);
    }

    [Fact]
    public void APhotoThatIsGone_IsAFailure_NotACrash()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00");
        var entry = Plan(path);
        File.Delete(path);

        var report = Run([entry]);

        Assert.Equal(path, Assert.Single(report.Failures).Path);
    }

    [Fact]
    public void AReadOnlyPhoto_IsChangedAndStaysReadOnly()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var report = Run([Plan(path, new DateAlignmentOptions(ExifFields: true))]);

            Assert.Empty(report.Failures);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
            AssertDates(path, new DateTime(2009, 9, 1, 14, 30, 0));
            Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), ExifReader.Read(new MemoryStream(File.ReadAllBytes(path))).Digitized);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void ARunThatIsStoppedBeforeItStarts_ChangesNothing()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var report = Run([Plan(path)], token: stop.Token);

        Assert.True(report.Cancelled);
        Assert.Empty(report.Photos);
        Assert.True(DateAlignment.SameMoment(CopyDate, new FileInfo(path).CreationTimeUtc));
    }

    [Fact]
    public void ManyPhotos_AreDoneInChunks_AndReported()
    {
        var entries = Enumerable.Range(0, 250).Select(i => Plan(Photo($"p{i:000}.jpg", "2009:09:01 14:30:00"))).ToList();
        var reports = new List<int>();

        var report = DateAligner.Apply(entries, _photos.Path, DateAligner.NewLogFile(_photos.Path, Now), null, new Immediate(reports.Add), CancellationToken.None);

        Assert.Equal(250, report.Photos.Count);
        Assert.Equal([100, 200, 250], reports);
        Assert.Equal(251, File.ReadAllLines(report.LogFile).Length);
    }

    [Fact]
    public void ALogFileThatIsNotOne_IsRefused()
    {
        var file = _photos.File("photo-liker-datefix-x.jsonl");
        File.WriteAllText(file, "this is not json");

        Assert.Throws<InvalidDataException>(() => DateAligner.Undo(file, null, CancellationToken.None));
    }

    [Fact]
    public void TheNewestLogThatIsNotUndone_IsTheOneToUndo()
    {
        File.WriteAllText(_photos.File("photo-liker-datefix-20261001-100000.jsonl"), "");
        File.WriteAllText(_photos.File("photo-liker-datefix-20261002-100000.jsonl"), "");
        File.WriteAllText(_photos.File("photo-liker-datefix-20261003-100000.undone.jsonl"), "");

        Assert.Equal("photo-liker-datefix-20261002-100000.jsonl", Path.GetFileName(DateAligner.LatestLog(_photos.Path)));
        Assert.Null(DateAligner.LatestLog(Path.Combine(_photos.Path, "nothing here")));
    }

    [Fact]
    public void LookingAtPhotos_ReadsTheirDatesAndTimes_AndNamesWhyAPhotoCanNotBeRead()
    {
        var good = Photo("a.jpg", "2009:09:01 14:30:00");
        var reset = Photo("b.jpg", "2001:01:01 00:00:05");
        var missing = new PhotoFile(Path.Combine(_photos.Path, "gone.jpg"), string.Empty);

        var facts = DateAlignment.InspectAll([new PhotoFile(good, string.Empty), new PhotoFile(reset, string.Empty), missing], Now, null, CancellationToken.None);

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), facts[0].Taken);
        Assert.Equal(CopyDate, facts[0].CreatedUtc);
        Assert.Null(facts[1].Taken); // a camera that lost its clock
        Assert.Equal(new DateTime(2001, 1, 1, 0, 0, 5), facts[1].Exif.Dates.Original);
        Assert.NotNull(facts[2].Problem);
    }

    [Fact]
    public void ThePhotosOfAFolderListing_BringTheirTimesAlong()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00");
        var listed = PhotoScanner.Scan(_photos.Path, recursive: true, [".jpg"], excludedFolder: null);

        var facts = DateAlignment.Inspect(listed.Single(), Now);

        Assert.True(DateAlignment.SameMoment(CopyDate, facts.CreatedUtc));
        Assert.True(DateAlignment.SameMoment(CopyDate, facts.ModifiedUtc));
        Assert.Equal(path, facts.Path);
    }

    [Fact]
    public void WhatWasWritten_IsKnownAfterwardsWithoutLookingAgain()
    {
        var path = Photo("a.jpg", "2009:09:01 14:30:00", "2009:09:01 14:30:07");
        var entry = Plan(path, new DateAlignmentOptions(ExifFields: true));
        var report = Run([entry]);
        var done = report.Photos.Single();

        var after = DateAlignment.After(entry.Facts, done.Written, done.CreatedUtc, done.ModifiedUtc);

        Assert.Equal(DateAlignmentStatus.Aligned, DateAlignment.Plan(after, new DateAlignmentOptions(true, true, true)).Status);
    }

    private sealed class Immediate(Action<int> handler) : IProgress<int>
    {
        public void Report(int value) => handler(value);
    }
}

public class IndexStampTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0);

    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));

    public void Dispose()
    {
        _models.Dispose();
        _photos.Dispose();
        _data.Dispose();
    }

    private IndexedPhoto Indexed(string file)
    {
        var info = new FileInfo(file);
        return new IndexedPhoto(Path.GetFileName(file), info.Length, info.LastWriteTimeUtc.Ticks)
        {
            Clip = Vectors.Unit(1), ClipVersion = IndexingJob.ClipVersion, FaceVersion = IndexingJob.FaceVersion,
            Date = new PhotoDate(new DateTime(2009, 9, 1, 14, 30, 0), DateSource.Exif, PhotoDates.Version),
        };
    }

    [Fact]
    public async Task ThePhotosOfARun_AreStillTheIndexedOnes_SoTheyAreNotAnalyzedAgain()
    {
        var file = _photos.File("a.jpg");
        File.WriteAllBytes(file, ExifJpeg.Build("2009:09:01 14:30:00"));
        var dbFile = Path.Combine(_data.Path, "index.db");

        using (var index = PhotoIndex.Open(_photos.Path, dbFile))
        {
            index.Save([Indexed(file)]);

            var facts = DateAlignment.Inspect(new PhotoFile(file, string.Empty), Now);
            var report = DateAligner.Apply(
                [DateAlignment.Plan(facts, DateAlignmentOptions.Default)], _photos.Path, DateAligner.NewLogFile(_photos.Path, Now), null, null, CancellationToken.None);
            var done = report.Photos.Single();
            index.UpdateStamps([(done.Path, done.PreviousModifiedUtc.Ticks, done.ModifiedUtc.Ticks)]);

            Assert.Equal(done.ModifiedUtc.Ticks, index.Find("a.jpg")!.ModifiedTicks);
        }

        // Without the models, any photo that is taken for changed would make this fail
        using var reopened = PhotoIndex.Open(_photos.Path, dbFile);
        Assert.Equal(new FileInfo(file).LastWriteTimeUtc.Ticks, reopened.Find("a.jpg")!.ModifiedTicks);
        var summary = await IndexingJob.RunAsync(reopened, _models, null, [".jpg"], null, CancellationToken.None);
        Assert.Equal(0, summary.Indexed);
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public void APhotoThatWasOutOfDateAlready_StaysSo()
    {
        var file = _photos.File("a.jpg");
        File.WriteAllBytes(file, ExifJpeg.Build("2009:09:01 14:30:00"));
        using var index = PhotoIndex.Open(_photos.Path, Path.Combine(_data.Path, "index.db"));
        var photo = Indexed(file);
        index.Save([photo]);
        var indexed = photo.ModifiedTicks;

        // The index knew the photo by another date of change than the one the run found: it was edited after it was indexed
        index.UpdateStamps([(file, indexed + 12345, indexed + 99999)]);
        index.UpdateStamps([(Path.Combine(_photos.Path, "other.jpg"), indexed, 1)]); // and a photo it does not know is nothing

        Assert.Equal(indexed, index.Find("a.jpg")!.ModifiedTicks);
    }
}
