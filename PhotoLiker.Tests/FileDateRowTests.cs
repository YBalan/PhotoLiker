using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class FileDateRowTests : IDisposable
{
    private static readonly DateTime Taken = new(2015, 1, 1, 12, 30, 0);

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PLFileDateTests_" + Guid.NewGuid());

    public FileDateRowTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void TheDatesOfTheFile_AreWrittenWithTheMonthAsAWord()
    {
        Assert.Equal("01 Jan 2015 12:30:00", FileDateRow.Describe(Taken, Taken, null));
    }

    [Fact]
    public void TwoDatesThatDiffer_AreBothShown()
    {
        var text = FileDateRow.Describe(Taken, Taken.AddDays(3), null);

        Assert.Equal("created 01 Jan 2015 12:30:00, modified 04 Jan 2015 12:30:00", text);
    }

    [Fact]
    public void ADateThatIsTheDateTaken_SaysSo_WithinTheToleranceOfAFileSystem()
    {
        Assert.EndsWith("(= Date Taken (Original))", FileDateRow.Describe(Taken, Taken.AddSeconds(2), Taken));
    }

    [Fact]
    public void ADateThatIsNotTheDateTaken_SaysSo_IfEitherOfTheTwoDiffers()
    {
        Assert.EndsWith("(≠ Date Taken (Original))", FileDateRow.Describe(Taken.AddYears(9), Taken.AddYears(9), Taken));
        Assert.EndsWith("(≠ Date Taken (Original))", FileDateRow.Describe(Taken, Taken.AddYears(9), Taken));
    }

    [Fact]
    public void WithoutADateTaken_NothingIsClaimed()
    {
        var text = FileDateRow.Describe(Taken, Taken, null);

        Assert.DoesNotContain("Date Taken", text);
    }

    [Fact]
    public void TheDateTaken_IsTheOriginalDate_ElseTheDigitizedOne_AndOnlyIfItIsADate()
    {
        MetadataEntry Entry(int tag, string value) => new(tag, ExifTagMap.GetName(tag), value);

        Assert.Equal(Taken, FileDateRow.TakenOf([Entry(0x9004, "2001:02:03 04:05:06"), Entry(0x9003, "2015:01:01 12:30:00")]));
        Assert.Equal(new DateTime(2001, 2, 3, 4, 5, 6), FileDateRow.TakenOf([Entry(0x9004, "2001:02:03 04:05:06")]));
        Assert.Null(FileDateRow.TakenOf([Entry(0x9003, "    :  :     :  :  "), Entry(0x010F, "Canon")]));
        Assert.Null(FileDateRow.TakenOf([]));
    }

    [Fact]
    public void TheRowOfAFile_IsNamedAndCarriesTheDatesOfTheFile()
    {
        var path = Path.Combine(_tempDir, "a.jpg");
        File.WriteAllText(path, "photo");
        File.SetCreationTime(path, Taken);
        File.SetLastWriteTime(path, Taken);
        MetadataEntry[] exif = [new(0x9003, "Date Taken (Original)", "2015:01:01 12:30:00")];

        var row = FileDateRow.Create(path, exif);

        Assert.NotNull(row);
        Assert.Equal("File Date", row.Name);
        Assert.Equal(ExifTagMap.FileDateTagId, row.TagId);
        Assert.Equal("01 Jan 2015 12:30:00 (= Date Taken (Original))", row.Value);
    }

    [Fact]
    public void APhotoThatWasSavedAgainByAProgram_IsComparedWithTheTimeOfShooting_NotWithTheDayItWasSaved()
    {
        // Y:\OldFotik\Photo\305CANON\IMG_3037.JPG: shot on 28 Oct 2009, turned in Windows Photo Viewer on 16 Dec 2009
        var path = Path.Combine(_tempDir, "IMG_3037.JPG");
        File.WriteAllText(path, "photo");
        var shot = new DateTime(2009, 10, 28, 0, 19, 51);
        File.SetCreationTime(path, shot);
        File.SetLastWriteTime(path, shot);
        MetadataEntry[] exif =
        [
            new(0x0132, ExifTagMap.GetName(0x0132), "16 Dec 2009 14:32:47"),
            new(0x9003, ExifTagMap.GetName(0x9003), "28 Oct 2009 00:19:51"),
            new(0x9004, ExifTagMap.GetName(0x9004), "28 Oct 2009 00:19:51"),
        ];

        var row = FileDateRow.Create(path, exif);

        Assert.Equal("28 Oct 2009 00:19:51 (= Date Taken (Original))", row!.Value);
        Assert.Equal("Date Last Saved", ExifTagMap.GetName(0x0132)); // so the row above it in the grid is not taken for the date of shooting
    }

    [Fact]
    public void TheNoteNamesTheRowItWasComparedWith_AlsoWhenThatIsTheDigitizedDate()
    {
        var path = Path.Combine(_tempDir, "a.jpg");
        File.WriteAllText(path, "photo");
        File.SetCreationTime(path, Taken);
        File.SetLastWriteTime(path, Taken);
        MetadataEntry[] exif = [new(0x9004, ExifTagMap.GetName(0x9004), "01 Jan 2015 12:30:00")];

        Assert.EndsWith("(= Date Time Digitized)", FileDateRow.Create(path, exif)!.Value);
    }

    [Fact]
    public void NoRowIsCalledJustDateTaken_BecauseOnlyTheTimeOfShootingIs()
    {
        Assert.DoesNotContain("Date Taken", ExifTagMap.Tags.Values);
        Assert.Contains("Date Taken (Original)", ExifTagMap.Tags.Values);
    }

    [Fact]
    public void AFileThatIsNotThere_HasNoRow()
    {
        Assert.Null(FileDateRow.Create(Path.Combine(_tempDir, "gone.jpg"), []));
    }

    [Fact]
    public void TheRow_IsListedWithTheImageTags_RightAfterDateLastSaved()
    {
        var row = new MetadataEntry(ExifTagMap.FileDateTagId, ExifTagMap.GetName(ExifTagMap.FileDateTagId), "x");
        var dateSaved = new MetadataEntry(0x0132, ExifTagMap.GetName(0x0132), "x");

        Assert.Equal(ExifTagMap.GetCategory(dateSaved).Name, ExifTagMap.GetCategory(row).Name);
        Assert.True(ExifTagMap.GetCategory(row).IsBrowsable);

        // The grid lists a category alphabetically: nothing of the Image tags may sort between the two
        var names = ExifTagMap.Tags
            .Where(t => ExifTagMap.GetCategory(t.Key).Name == ExifTagMap.GetCategory(row).Name)
            .Select(t => t.Value)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Assert.Equal(names.IndexOf("Date Last Saved") + 1, names.IndexOf("File Date"));
    }

    [Fact]
    public void TheRow_HasADescriptionLikeEveryOtherRow()
    {
        var row = new MetadataEntry(ExifTagMap.FileDateTagId, ExifTagMap.GetName(ExifTagMap.FileDateTagId), "x");

        Assert.Contains("file", MetadataDescriptions.For(row));
    }
}

public class DateTextTests
{
    [Theory]
    [InlineData("2025:08:14 09:12:07", 2025, 8, 14, 9, 12, 7)]
    [InlineData("14 Aug 2025 09:12:07", 2025, 8, 14, 9, 12, 7)]
    [InlineData("14 Aug 2025 09:12", 2025, 8, 14, 9, 12, 0)]
    [InlineData("2025-08-14 09:12:07", 2025, 8, 14, 9, 12, 7)]
    [InlineData("2025-08-14 09:12", 2025, 8, 14, 9, 12, 0)]
    [InlineData("2025-08-14T09:12:07", 2025, 8, 14, 9, 12, 7)]
    [InlineData("14.08.2025 09:12:07", 2025, 8, 14, 9, 12, 7)]
    [InlineData("14.08.2025 09:12", 2025, 8, 14, 9, 12, 0)]
    [InlineData("  2025-08-14 09:12:07  ", 2025, 8, 14, 9, 12, 7)]
    [InlineData("2025-08-14", 2025, 8, 14, 12, 0, 0)]       // a day alone: noon
    [InlineData("14.08.2025", 2025, 8, 14, 12, 0, 0)]
    [InlineData("14 Aug 2025", 2025, 8, 14, 12, 0, 0)]
    [InlineData("2025:08:14", 2025, 8, 14, 12, 0, 0)]
    public void ATypedDate_IsUnderstoodEitherWay(string text, int year, int month, int day, int hour, int minute, int second)
    {
        Assert.True(DateText.TryParseTyped(text, out var date));
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("yesterday")]
    [InlineData("08/14/2025")]           // can be read two ways
    [InlineData("2025-13-01 10:00:00")]
    [InlineData("2025-02-30")]
    [InlineData("(none)")]
    public void ATextThatIsNoDate_IsNotUnderstood(string? text)
    {
        Assert.False(DateText.TryParseTyped(text, out _));
    }

    [Fact]
    public void TheFirstShownDate_IsFoundInTheText()
    {
        Assert.True(DateText.TryFindDisplayed("created 14 Aug 2025 09:12:07, modified 15 Aug 2025 10:00:00 (≠ Date Taken (Original))", out var date));
        Assert.Equal(new DateTime(2025, 8, 14, 9, 12, 7), date);
        Assert.True(DateText.TryFindDisplayed("01 Jan 2015 12:30:00 (= Date Taken (Original))", out date));
        Assert.Equal(new DateTime(2015, 1, 1, 12, 30, 0), date);
        Assert.False(DateText.TryFindDisplayed("no date here 12:30:00", out _));
        Assert.False(DateText.TryFindDisplayed(null, out _));
    }

    [Fact]
    public void ADate_IsShownWithTheMonthAsAWord_InEnglishWhateverTheCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("uk-UA");

            Assert.Equal("14 Aug 2025 09:12:07", DateText.Display(new DateTime(2025, 8, 14, 9, 12, 7)));
            Assert.Equal("14 Aug 2025 09:12:07", DateText.Reformat("2025:08:14 09:12:07"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("    :  :     :  :  ")]   // a camera without a clock
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("Canon")]
    [InlineData("")]
    [InlineData("14 Aug 2025 09:12:07")]  // shown already
    public void ATextThatIsNotADateOfTheFile_StaysAsItIs(string text)
    {
        Assert.Equal(text, DateText.Reformat(text));
    }

    [Fact]
    public void ADate_IsReadBackInEitherWay()
    {
        var date = new DateTime(2025, 8, 14, 9, 12, 7);

        Assert.True(DateText.TryParse("2025:08:14 09:12:07", out var exif));
        Assert.True(DateText.TryParse(" 14 Aug 2025 09:12:07 ", out var shown));
        Assert.Equal(date, exif);
        Assert.Equal(date, shown);
        Assert.False(DateText.TryParse("yesterday", out _));
        Assert.False(DateText.TryParse(null, out _));
    }

    [Fact]
    public void TheDateOfAGpsFix_IsShownLikeTheOthers_AndAnythingElseIsKeptReadable()
    {
        Assert.Equal("15 Jun 2024", DateText.ReformatGpsDate("2024:06:15"));
        Assert.Equal("2024-6", DateText.ReformatGpsDate("2024:6"));
    }

    [Fact]
    public void TheDatesOfAPhoto_ReachTheGridWithTheMonthAsAWord()
    {
        byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text + "\0");

        var meta = FriendlyImageMetadata.FromProperties(
        [
            new RawExifProperty(0x9003, 2, Ascii("2025:08:14 09:12:07")),
            new RawExifProperty(0x9004, 2, Ascii("    :  :     :  :  ")),
            new RawExifProperty(0x010F, 2, Ascii("Canon")),
        ]);

        Assert.Equal("14 Aug 2025 09:12:07", meta.Entries.Single(e => e.TagId == 0x9003).Value);
        Assert.Equal("    :  :     :  :  ", meta.Entries.Single(e => e.TagId == 0x9004).Value.Trim('\0'));
        Assert.Equal("Canon", meta.Entries.Single(e => e.TagId == 0x010F).Value);
        Assert.Equal(new DateTime(2025, 8, 14, 9, 12, 7), FileDateRow.TakenOf(meta.Entries)); // and the rest of the app can still read it
    }
}

public class DateRowEditTests
{
    private static readonly DateTime Date = new(2009, 9, 1, 14, 30, 5, 600);

    [Theory]
    [InlineData(0x9003, true)]
    [InlineData(0x9004, true)]
    [InlineData(0x0132, true)]
    [InlineData(ExifTagMap.FileDateTagId, true)]
    [InlineData(0x010F, false)]   // Make
    [InlineData(0x9011, false)]   // a time zone, not a date
    public void OnlyTheDateRows_CanBeEdited(int tag, bool editable)
    {
        Assert.Equal(editable, DateRowEdit.IsEditable(tag));
        Assert.Equal(editable, DateRowEdit.For(tag, Date) is not null);
    }

    [Fact]
    public void TheDateTaken_IsAlsoGivenToTheFile()
    {
        var edit = DateRowEdit.For(0x9003, Date)!;

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 5), edit.Change.Original);
        Assert.Null(edit.Change.Digitized);
        Assert.Null(edit.Change.LastSaved);
        Assert.Equal(DateTimeKind.Utc, edit.FileDatesUtc!.Value.Kind);
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 5), edit.FileDatesUtc.Value.ToLocalTime());
    }

    [Fact]
    public void TheOtherDatesInsideThePhoto_ChangeOnlyThemselves()
    {
        var digitized = DateRowEdit.For(0x9004, Date)!;
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 5), digitized.Change.Digitized);
        Assert.Null(digitized.Change.Original);
        Assert.Null(digitized.FileDatesUtc);

        var saved = DateRowEdit.For(0x0132, Date)!;
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 5), saved.Change.LastSaved);
        Assert.Null(saved.FileDatesUtc);
    }

    [Fact]
    public void TheFileDate_ChangesTheFileAndNothingInsideThePhoto()
    {
        var edit = DateRowEdit.For(ExifTagMap.FileDateTagId, Date)!;

        Assert.True(edit.Change.IsEmpty);
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 5), edit.FileDatesUtc!.Value.ToLocalTime());
    }
}
