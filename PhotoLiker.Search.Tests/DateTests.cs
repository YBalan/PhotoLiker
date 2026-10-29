using PhotoLiker.Core;
using Microsoft.Data.Sqlite;

namespace PhotoLiker.Search.Tests;

/// <summary>A JPEG that is only a header with an EXIF block: enough to read dates from, not to look at.</summary>
internal static class ExifJpeg
{
    public static byte[] Build(string? original = null, string? digitized = null, string? modified = null, bool bigEndian = false, bool withJfif = false)
    {
        var tiff = Tiff(original, digitized, modified, bigEndian);
        using var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);
        if (withJfif)
        {
            jpeg.Write([0xFF, 0xE0, 0x00, 0x10]);
            jpeg.Write("JFIF\0\x01\x01\0\0\x01\0\x01\0\0"u8);
        }

        var length = 2 + 6 + tiff.Length;
        jpeg.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
        jpeg.Write("Exif\0\0"u8);
        jpeg.Write(tiff);
        jpeg.Write([0xFF, 0xD9]);
        return jpeg.ToArray();
    }

    public static byte[] Tiff(string? original, string? digitized, string? modified, bool big)
    {
        var data = new List<byte>();
        var ifd0Entries = 1 + (modified is null ? 0 : 1);
        var exifEntries = (original is null ? 0 : 1) + (digitized is null ? 0 : 1);
        var exifIfdOffset = 8 + 2 + ifd0Entries * 12 + 4;
        var dataStart = exifIfdOffset + 2 + exifEntries * 12 + 4;

        uint Place(string text)
        {
            var offset = dataStart + data.Count;
            data.AddRange(System.Text.Encoding.ASCII.GetBytes(text + "\0"));
            return (uint)offset;
        }

        using var tiff = new MemoryStream();
        void U16(int value) => tiff.Write(big ? [(byte)(value >> 8), (byte)value] : new[] { (byte)value, (byte)(value >> 8) });
        void U32(uint value) => tiff.Write(big
            ? [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]
            : new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });
        void Text(int tag, string text) { U16(tag); U16(2); U32((uint)text.Length + 1); U32(Place(text)); }

        tiff.Write(big ? "MM"u8 : "II"u8);
        U16(42);
        U32(8);

        U16(ifd0Entries);
        if (modified is not null)
            Text(0x0132, modified);
        U16(0x8769); U16(4); U32(1); U32((uint)exifIfdOffset);
        U32(0);

        U16(exifEntries);
        if (original is not null)
            Text(0x9003, original);
        if (digitized is not null)
            Text(0x9004, digitized);
        U32(0);

        tiff.Write(data.ToArray());
        return tiff.ToArray();
    }
}

public class ExifReaderTests
{
    private static ExifDates Read(byte[] bytes) => ExifReader.Read(new MemoryStream(bytes));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheThreeDatesOfAJpeg_AreRead_InEitherByteOrder(bool bigEndian)
    {
        var dates = Read(ExifJpeg.Build("2009:09:01 14:30:00", "2009:09:02 08:00:01", "2015:01:20 10:00:00", bigEndian));

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), dates.Original);
        Assert.Equal(new DateTime(2009, 9, 2, 8, 0, 1), dates.Digitized);
        Assert.Equal(new DateTime(2015, 1, 20, 10, 0, 0), dates.Modified);
    }

    [Fact]
    public void ADateThatIsNotThere_IsNull()
    {
        var dates = Read(ExifJpeg.Build(original: "2009:09:01 14:30:00"));

        Assert.NotNull(dates.Original);
        Assert.Null(dates.Digitized);
        Assert.Null(dates.Modified);
    }

    [Fact]
    public void AnotherSegmentBeforeTheExifBlock_IsSkipped()
    {
        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), Read(ExifJpeg.Build("2009:09:01 14:30:00", withJfif: true)).Original);
    }

    [Fact]
    public void ReadingDoesNotNeedAStreamThatCanSeek()
    {
        using var stream = new NonSeekingStream(ExifJpeg.Build("2009:09:01 14:30:00", withJfif: true));

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), ExifReader.Read(stream).Original);
    }

    [Fact]
    public void AFileThatIsNotAJpeg_HasNoDates()
    {
        Assert.Equal(ExifDates.None, Read([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal(ExifDates.None, Read([]));
    }

    [Fact]
    public void AJpegWithoutExif_HasNoDates()
    {
        Assert.Equal(ExifDates.None, Read([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9]));
    }

    [Theory]
    [InlineData("    :  :     :  :  ")]
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("not a date at all!")]
    public void ADateThatIsBlankOrNonsense_IsNull(string text)
    {
        Assert.Null(Read(ExifJpeg.Build(original: text)).Original);
    }

    [Fact]
    public void ADamagedOrCutOffBlock_GivesNoDates_AndNoException()
    {
        var whole = ExifJpeg.Build("2009:09:01 14:30:00", "2009:09:01 14:30:00", "2009:09:01 14:30:00");

        Assert.Equal(ExifDates.None, Read(whole[..40]));                      // cut inside the block
        var damaged = (byte[])whole.Clone();
        damaged[12 + 8 + 2 + 12 + 8] = 0xFF;                                  // the pointer to the EXIF directory (after the header of the file, of TIFF, the count and the first entry) now points far outside
        Assert.Equal(ExifDates.None, Read(damaged));
        Assert.Equal(ExifDates.None, Read([.. whole[..4], 0x00, 0x01, 0xFF])); // a segment shorter than its own length field
    }

    internal sealed class NonSeekingStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class PhotoDatesTests
{
    private static readonly DateTime Now = new(2024, 6, 1, 12, 0, 0);
    private static readonly DateTime FileCreated = new(2010, 5, 5, 9, 0, 0);
    private static readonly DateTime FileModified = new(2003, 4, 4, 9, 0, 0);

    private static PhotoDate Resolve(DateTime? original, DateTime? fileCreated = null, DateTime? fileModified = null, DateTime? digitized = null, DateTime? exifModified = null) =>
        PhotoDates.Resolve(new ExifDates(original, digitized, exifModified), fileCreated, fileModified, Now);

    [Fact]
    public void TheDateOfTheCamera_IsUsedWhenItIsBelievable()
    {
        var date = Resolve(new DateTime(2009, 9, 1, 14, 30, 0), FileCreated, FileModified);

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), date.Taken);
        Assert.Equal(DateSource.Exif, date.Source);
        Assert.Equal(PhotoDates.Version, date.Version);
    }

    [Fact]
    public void ACameraThatLostItsClock_GivesWayToTheDateTheFileWasCreated()
    {
        var date = Resolve(new DateTime(2001, 1, 1, 0, 0, 5), FileCreated, FileModified);

        Assert.Equal(FileCreated, date.Taken); // not the date it was last changed, which the same clock may have written
        Assert.Equal(DateSource.File, date.Source);
    }

    [Theory]
    [InlineData(2003, 1, 1, 0, 0, false)]   // the first minute of a year: a camera that was reset
    [InlineData(2003, 1, 1, 11, 59, false)] // and the morning that follows
    [InlineData(2003, 1, 1, 12, 0, true)]   // New Year's Day, from noon: a real photo
    [InlineData(2003, 1, 2, 0, 0, true)]    // January 2 at midnight: a real photo
    [InlineData(2003, 12, 31, 0, 0, true)]
    [InlineData(1994, 6, 1, 12, 0, false)]  // before there were such cameras
    [InlineData(1995, 6, 1, 12, 0, true)]
    [InlineData(2024, 6, 2, 11, 0, true)]   // a day ahead is a clock in another time zone
    [InlineData(2024, 6, 3, 12, 0, false)]  // a camera of the future
    public void WhichDatesOfTheCamera_AreBelieved(int year, int month, int day, int hour, int minute, bool believed)
    {
        var date = Resolve(new DateTime(year, month, day, hour, minute, 0), FileCreated);

        Assert.Equal(believed ? DateSource.Exif : DateSource.File, date.Source);
    }

    [Theory]
    [InlineData(2015, 1, 1, 0, 10, 2015, 1, 1, 9, 0)]     // fireworks, and the file made that day: a real photo
    [InlineData(2016, 1, 1, 8, 58, 2016, 1, 1, 8, 58)]
    [InlineData(2015, 1, 1, 0, 10, 2014, 12, 31, 22, 10)] // the card copied with the time shifted by the time zone, over midnight
    [InlineData(2015, 1, 1, 0, 10, 2015, 1, 3, 0, 10)]    // two days is still "about then"
    public void AJanuaryFirstThatTheFileConfirms_IsARealPhoto(int year, int month, int day, int hour, int minute, int fileYear, int fileMonth, int fileDay, int fileHour, int fileMinute)
    {
        var taken = new DateTime(year, month, day, hour, minute, 0);

        var date = Resolve(taken, new DateTime(fileYear, fileMonth, fileDay, fileHour, fileMinute, 0));

        Assert.Equal(taken, date.Taken);
        Assert.Equal(DateSource.Exif, date.Source);
    }

    [Theory]
    [InlineData(2012, 1, 1, 2, 44, 2012, 8, 1)]   // the file says August: the clock was reset, it counted up from January 1
    [InlineData(2011, 1, 1, 11, 32, 2013, 5, 11)]
    [InlineData(2015, 1, 1, 0, 10, 2015, 1, 4)]   // three days apart is not "about then"
    public void AJanuaryFirstThatTheFileContradicts_IsADeadClock(int year, int month, int day, int hour, int minute, int fileYear, int fileMonth, int fileDay)
    {
        var created = new DateTime(fileYear, fileMonth, fileDay, 9, 0, 0);

        var date = Resolve(new DateTime(year, month, day, hour, minute, 0), created);

        Assert.Equal(created, date.Taken);
        Assert.Equal(DateSource.File, date.Source);
    }

    [Fact]
    public void OnlyTheCreationDateConfirms_NotTheDateOfTheLastChange()
    {
        // A dead clock writes its date into the photo and into the file; copying the file gives it a new creation date
        var date = Resolve(new DateTime(2001, 1, 1, 0, 0, 5), FileCreated, fileModified: new DateTime(2001, 1, 1, 0, 0, 6));

        Assert.Equal(FileCreated, date.Taken);
        Assert.Equal(DateSource.File, date.Source);
    }

    [Fact]
    public void AJanuaryFirstWithoutAnyFileDate_IsDoubted_AndHasNoDateToFallBackOn()
    {
        var date = Resolve(new DateTime(2015, 1, 1, 0, 10, 0));

        Assert.Null(date.Taken);
        Assert.Equal(DateSource.None, date.Source);
    }

    [Fact]
    public void WhenTheOriginalIsNotBelievable_AnotherDateOfTheCameraIsTried()
    {
        var date = Resolve(new DateTime(2001, 1, 1), FileCreated, digitized: new DateTime(2009, 9, 1, 14, 30, 0));

        Assert.Equal(new DateTime(2009, 9, 1, 14, 30, 0), date.Taken);
        Assert.Equal(DateSource.Exif, date.Source);
    }

    [Fact]
    public void WithoutAnExifDate_TheDateTheFileWasCreatedIsUsed()
    {
        var date = Resolve(null, FileCreated, FileModified);

        Assert.Equal(FileCreated, date.Taken);
        Assert.Equal(DateSource.File, date.Source);
    }

    [Fact]
    public void WhenTheCreationDateIsUseless_TheDateOfTheLastChangeIsTheLastResort()
    {
        Assert.Equal(FileModified, Resolve(null, null, FileModified).Taken);
        Assert.Equal(FileModified, Resolve(null, new DateTime(1980, 1, 1), FileModified).Taken);
    }

    [Fact]
    public void WithNoDateThatCanBeBelieved_ThePhotoHasNone_ButIsMarkedAsLookedAt()
    {
        var date = Resolve(new DateTime(2001, 1, 1), null, new DateTime(1980, 1, 1));

        Assert.Null(date.Taken);
        Assert.Equal(DateSource.None, date.Source);
        Assert.Equal(PhotoDates.Version, date.Version); // so it is not looked at again and again
    }
}

public class DateQueryTests
{
    private const int Year = 2026;

    private static (string Words, string? Dates) Parse(string text)
    {
        var parsed = DateQuery.Parse(text, Year);
        return (parsed.Words, parsed.Dates?.Describe());
    }

    [Theory]
    [InlineData("september 1", "September 1 (any year)")]
    [InlineData("September 1", "September 1 (any year)")]
    [InlineData("Sept 1st", "September 1 (any year)")]
    [InlineData("sep. 1", "September 1 (any year)")]
    [InlineData("1 September", "September 1 (any year)")]
    [InlineData("1st of september", "September 1 (any year)")]
    [InlineData("february 29", "February 29 (any year)")]
    [InlineData("september 1, 2009", "September 1, 2009")]
    [InlineData("september 1 2009", "September 1, 2009")]
    [InlineData("1 sep 2009", "September 1, 2009")]
    [InlineData("12 May 2009", "May 12, 2009")]
    [InlineData("2009-09-01", "September 1, 2009")]
    [InlineData("september 2009", "September 2009")]
    [InlineData("May 2009", "May 2009")]
    [InlineData("2009-09", "September 2009")]
    [InlineData("september", "September (any year)")]
    [InlineData("2009", "2009")]
    [InlineData("2009-2011", "2009-2011")]
    [InlineData("2011 - 2009", "2009-2011")]
    public void ADateAlone_IsUnderstood(string text, string described)
    {
        Assert.Equal(("", described), Parse(text));
    }

    [Theory]
    [InlineData("beach september 2009", "beach", "September 2009")]
    [InlineData("september 2009 beach", "beach", "September 2009")]
    [InlineData("beach in september 2009", "beach", "September 2009")]
    [InlineData("children on a stage 2005", "children on a stage", "2005")]
    [InlineData("Mom 2005", "Mom", "2005")]
    [InlineData("Mom 2005-2007", "Mom", "2005-2007")]
    [InlineData("snow september 1", "snow", "September 1 (any year)")]
    [InlineData("2009 september 1", "", "September 1, 2009")]
    [InlineData("september 1 on the beach 2009", "the beach", "September 1, 2009")]
    [InlineData("september beach 2009", "beach", "September 2009")]
    [InlineData("a dog on 1 sep 2009", "a dog", "September 1, 2009")]
    public void WhatIsLeftOfTheText_IsTheRestOfTheSearch(string text, string words, string described)
    {
        Assert.Equal((words, described), Parse(text));
    }

    [Theory]
    [InlineData("sunset")]
    [InlineData("dec")]                 // a short name of a month needs a day or a year with it
    [InlineData("mar")]
    [InlineData("blade runner 2077")]   // not a year yet
    [InlineData("room 1850")]
    [InlineData("february 30")]         // not a day of February
    [InlineData("september 31")]
    public void WhatIsNoDate_IsLeftAlone(string text)
    {
        Assert.Equal((text, null), Parse(text));
    }

    [Fact]
    public void AFullMonthName_IsAMonthWhereverItStands()
    {
        Assert.Equal(("a of penguins", "March (any year)"), Parse("a march of penguins"));
    }

    [Fact]
    public void AMonthNameInsideAWord_IsNotAMonth()
    {
        Assert.Equal(("market stalls", "2009"), Parse("market 2009 stalls"));
    }

    [Fact]
    public void ATextInQuotes_IsNotSearchedForDates()
    {
        Assert.Equal(("september 1", null), Parse("\"september 1\""));
        Assert.Equal(("2009", null), Parse("\"2009\""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNothing(string text)
    {
        Assert.Equal(("", null), Parse(text));
    }

    [Fact]
    public void AWordThatOnlyJoinsTheDate_IsNotLeftBehind()
    {
        Assert.Equal(("", "September (any year)"), Parse("in september"));
        Assert.Equal(("", "2009"), Parse("from 2009"));
        Assert.Equal(("", "2009"), Parse("during 2009"));
    }

    [Fact]
    public void AYearIsBelievedUpToNextYear()
    {
        Assert.Equal(("", "2027"), Parse("2027"));
        Assert.Equal(("2028", null), Parse("2028"));
    }

    [Fact]
    public void TheFilter_FindsTheDaysItDescribes()
    {
        var anyYear = DateQuery.Parse("september 1", Year).Dates!;
        Assert.True(anyYear.Matches(new DateTime(2003, 9, 1, 23, 59, 0)));
        Assert.True(anyYear.Matches(new DateTime(2020, 9, 1)));
        Assert.False(anyYear.Matches(new DateTime(2003, 9, 2)));
        Assert.False(anyYear.Matches(new DateTime(2003, 10, 1)));
        Assert.False(anyYear.Matches(null));

        var range = DateQuery.Parse("2009-2011", Year).Dates!;
        Assert.True(range.Matches(new DateTime(2009, 1, 1)));
        Assert.True(range.Matches(new DateTime(2011, 12, 31, 23, 0, 0)));
        Assert.False(range.Matches(new DateTime(2012, 1, 1)));

        var month = DateQuery.Parse("september 2009", Year).Dates!;
        Assert.True(month.Matches(new DateTime(2009, 9, 30)));
        Assert.False(month.Matches(new DateTime(2010, 9, 1)));
    }
}

public class PhotoIndexDatesTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();
    private string IndexFile => Path.Combine(_data.Path, "index.db");

    public void Dispose()
    {
        _photos.Dispose();
        _data.Dispose();
    }

    private static IndexedPhoto Photo(string path, PhotoDate? date = null) =>
        new(path, length: 1000, modifiedTicks: 5000) { Clip = Vectors.Unit(1), ClipVersion = 1, FaceVersion = 1, Date = date ?? PhotoDate.NotRead };

    [Fact]
    public void AnIndexMadeBeforeDatesWereKept_IsKept_AndGetsPlaceForThem()
    {
        using (var db = new SqliteConnection($"Data Source={IndexFile};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO meta (key, value) VALUES ('schema', '1');
                CREATE TABLE photos (
                    id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE, length INTEGER NOT NULL, modified INTEGER NOT NULL,
                    clip BLOB, clip_version INTEGER NOT NULL, face_version INTEGER NOT NULL, failure TEXT);
                CREATE TABLE faces (
                    id INTEGER PRIMARY KEY, photo_id INTEGER NOT NULL REFERENCES photos(id) ON DELETE CASCADE,
                    x REAL NOT NULL, y REAL NOT NULL, w REAL NOT NULL, h REAL NOT NULL, score REAL NOT NULL, manual INTEGER NOT NULL, embedding BLOB NOT NULL);
                INSERT INTO photos (path, length, modified, clip, clip_version, face_version) VALUES ('old.jpg', 1000, 5000, $clip, 1, 1);
                """;
            command.Parameters.AddWithValue("$clip", VectorMath.ToBytes(Vectors.Unit(1, 2)));
            command.ExecuteNonQuery();
        }

        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
        {
            var photo = index.Find("old.jpg")!;
            Assert.Equal(Vectors.Unit(1, 2), photo.Clip);       // the work of hours is still there
            Assert.Equal(PhotoDate.NotRead, photo.Date);
            Assert.Equal(1, index.DatesPending);

            index.SaveDates([(photo, new PhotoDate(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version))]);
            Assert.Equal(0, index.DatesPending);
        }

        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);
        Assert.Equal(new PhotoDate(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version), reopened.Find("old.jpg")!.Date);
    }

    [Fact]
    public void TheDateOfAPhoto_IsSavedWithIt_AndSurvivesReopening()
    {
        var dated = new PhotoDate(new DateTime(2009, 9, 1, 14, 30, 0), DateSource.File, PhotoDates.Version);
        var undated = new PhotoDate(null, DateSource.None, PhotoDates.Version);
        using (var index = PhotoIndex.Open(_photos.Path, IndexFile))
            index.Save([Photo("a.jpg", dated), Photo("b.jpg", undated)]);

        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);

        Assert.Equal(dated, reopened.Find("a.jpg")!.Date);
        Assert.Equal(undated, reopened.Find("b.jpg")!.Date);
        Assert.Equal(0, reopened.DatesPending); // a photo without a date was looked at: it is not pending
    }

    [Fact]
    public void SavingDates_LeavesTheRestOfThePhotoAlone()
    {
        using var index = PhotoIndex.Open(_photos.Path, IndexFile);
        var photo = Photo("a.jpg");
        photo.Faces = [new IndexedFace(photo, 0.1f, 0.2f, 0.3f, 0.4f, 0.9f, manual: true, Vectors.Unit(3))];
        index.Save([photo]);

        index.SaveDates([(photo, new PhotoDate(new DateTime(2009, 9, 1), DateSource.Exif, PhotoDates.Version))]);

        using var reopened = PhotoIndex.Open(_photos.Path, IndexFile);
        var again = reopened.Find("a.jpg")!;
        Assert.Equal(Vectors.Unit(1), again.Clip);
        Assert.True(Assert.Single(again.Faces).Manual);
        Assert.Equal(new DateTime(2009, 9, 1), again.Date.Taken);
    }
}

public class DateSearchTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly FolderIndex _search;
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));

    // More than the 500 photos a search once stopped at
    private const int Many = 520;
    private int _added;

    public DateSearchTests() => _search = FolderIndex.Open(_photos.Path, sharedPeopleFile: null);

    public void Dispose()
    {
        _search.Dispose();
        _models.Dispose();
        _photos.Dispose();
        try { File.Delete(SearchPaths.IndexFile(_photos.Path)); } catch { /* best effort */ }
    }

    private IndexedPhoto Add(string path, DateTime? taken, DateSource source = DateSource.Exif, params float[][] faces)
    {
        // A size of its own: photos of one size, time and picture are copies of one photo, which a search shows once
        var photo = new IndexedPhoto(path, length: 10 + _added++, modifiedTicks: 20)
        {
            Clip = Vectors.Unit(1),
            ClipVersion = 1,
            FaceVersion = 1,
            Date = new PhotoDate(taken, taken is null ? DateSource.None : source, PhotoDates.Version),
        };
        photo.Faces = [.. faces.Select((v, i) => new IndexedFace(photo, 0.1f + 0.3f * i, 0.2f, 0.2f, 0.2f, 0.9f, manual: false, v))];
        _search.Index.Save([photo]);
        return photo;
    }

    private string FullPath(string relative) => Path.Combine(_photos.Path, relative);

    [Fact]
    public void ADayWithoutAYear_ListsThatDayOfEveryYear_OldestFirst()
    {
        Add("c.jpg", new DateTime(2009, 9, 1, 8, 0, 0));
        Add("a.jpg", new DateTime(2003, 9, 1, 20, 0, 0));
        Add("b.jpg", new DateTime(2009, 9, 2));
        Add("d.jpg", new DateTime(2012, 10, 1));
        Add("e.jpg", null);

        var result = _search.Search(_models, "September 1");

        Assert.Equal([FullPath("a.jpg"), FullPath("c.jpg")], result.Hits.Select(h => h.FullPath));
        Assert.Equal("September 1 (any year)", result.Description);
        Assert.Equal(5, result.SearchedPhotos);
        Assert.NotNull(result.Dates);
    }

    [Fact]
    public void AYearOrARange_ListsEveryPhotoOfIt_WithoutALimit()
    {
        for (var i = 0; i < Many; i++)
            Add($"p{i:D4}.jpg", new DateTime(2009, 1, 1).AddHours(12 + i));
        Add("other.jpg", new DateTime(2010, 6, 1));

        Assert.Equal(Many, _search.Search(_models, "2009").Hits.Count);
        Assert.Equal(Many + 1, _search.Search(_models, "2009-2010").Hits.Count);
    }

    [Fact]
    public void AnUndatedPhoto_IsInNoDateSearch()
    {
        Add("none.jpg", null);

        Assert.Empty(_search.Search(_models, "2009").Hits);
        Assert.Empty(_search.Search(_models, "september").Hits);
    }

    [Fact]
    public void ANameAndADate_FindThePersonInThatTimeOnly()
    {
        var mom2005 = Add("mom2005.jpg", new DateTime(2005, 7, 1), DateSource.Exif, Vectors.Unit(1, 0));
        Add("mom2009.jpg", new DateTime(2009, 7, 1), DateSource.Exif, Vectors.Unit(1, 0.1f));
        Add("dad2005.jpg", new DateTime(2005, 7, 2), DateSource.Exif, Vectors.Unit(0, 1));
        _search.NameFace(mom2005.Faces[0], "Mom");

        var result = _search.Search(_models, "mom 2005");

        Assert.Equal([FullPath("mom2005.jpg")], result.Hits.Select(h => h.FullPath));
        Assert.Equal("Mom, 2005", result.Description);
        Assert.Equal(3, result.SearchedPhotos);
    }

    [Fact]
    public void ThePhotosDatedByTheirFile_AreCounted()
    {
        Add("exif.jpg", new DateTime(2009, 9, 1), DateSource.Exif);
        Add("file1.jpg", new DateTime(2009, 9, 2), DateSource.File);
        Add("file2.jpg", new DateTime(2009, 9, 3), DateSource.File);

        var result = _search.Search(_models, "2009");

        Assert.Equal(3, result.Hits.Count);
        Assert.Equal(2, result.FileDated);
    }

    [Fact]
    public void APersonWhoseNameIsAMonth_IsStillFoundByName()
    {
        var may = Add("may.jpg", new DateTime(2009, 7, 1), DateSource.Exif, Vectors.Unit(1, 0));
        _search.NameFace(may.Faces[0], "May");

        var result = _search.Search(_models, "may");

        Assert.Equal("May", result.Description);
        Assert.Equal([FullPath("may.jpg")], result.Hits.Select(h => h.FullPath));
    }

    [Fact]
    public void OnlyWordsThatAreNeitherADateNorAName_NeedTheTextModel()
    {
        var mom = Add("mom.jpg", new DateTime(2005, 7, 1), DateSource.Exif, Vectors.Unit(1, 0));
        _search.NameFace(mom.Faces[0], "Mom");

        Assert.False(_search.NeedsTextModel("September 1"));
        Assert.False(_search.NeedsTextModel("2009-2011"));
        Assert.False(_search.NeedsTextModel("mom"));
        Assert.False(_search.NeedsTextModel("mom 2005"));
        Assert.False(_search.NeedsTextModel("   "));
        Assert.True(_search.NeedsTextModel("beach"));
        Assert.True(_search.NeedsTextModel("beach september 2009"));
    }
}

public class DateIndexingTests : IDisposable
{
    private readonly TempFolder _photos = new();
    private readonly TempFolder _data = new();
    private readonly ModelHub _models = new(modelsFolder: Path.Combine(Path.GetTempPath(), "PLNoModels_" + Guid.NewGuid()));

    public void Dispose()
    {
        _models.Dispose();
        _photos.Dispose();
        _data.Dispose();
    }

    private static IndexedPhoto Indexed(string relative, string file)
    {
        var info = new FileInfo(file);
        return new IndexedPhoto(relative, info.Length, info.LastWriteTimeUtc.Ticks)
        {
            Clip = Vectors.Unit(1), ClipVersion = IndexingJob.ClipVersion, FaceVersion = IndexingJob.FaceVersion,
        };
    }

    [Fact]
    public async Task AnIndexWithoutDates_GetsThemFromTheFiles_WithoutTheModels()
    {
        var exif = _photos.File("a.jpg");
        File.WriteAllBytes(exif, ExifJpeg.Build(original: "2009:09:01 14:30:00"));

        var reset = _photos.File("sub/b.jpg"); // a camera that lost its clock
        File.WriteAllBytes(reset, ExifJpeg.Build(original: "2001:01:01 00:00:05"));
        File.SetCreationTimeUtc(reset, new DateTime(2010, 5, 5, 12, 0, 0, DateTimeKind.Utc));

        var bare = _photos.File("c.jpg"); // no EXIF at all
        File.WriteAllBytes(bare, [0xFF, 0xD8, 0xFF, 0xD9]);
        File.SetCreationTimeUtc(bare, new DateTime(2011, 6, 6, 12, 0, 0, DateTimeKind.Utc));

        using var index = PhotoIndex.Open(_photos.Path, Path.Combine(_data.Path, "index.db"));
        index.Save([Indexed("a.jpg", exif), Indexed("sub/b.jpg", reset), Indexed("c.jpg", bare)]);
        Assert.Equal(3, index.DatesPending);

        var summary = await IndexingJob.RunAsync(index, _models, null, [".jpg"], null, CancellationToken.None);

        Assert.Equal(3, summary.DatesRead);
        Assert.Equal(0, summary.Indexed);
        Assert.False(summary.Cancelled);
        Assert.Equal(0, index.DatesPending);

        Assert.Equal(new PhotoDate(new DateTime(2009, 9, 1, 14, 30, 0), DateSource.Exif, PhotoDates.Version), index.Find("a.jpg")!.Date);
        Assert.Equal(new PhotoDate(new DateTime(2010, 5, 5, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), DateSource.File, PhotoDates.Version), index.Find("sub/b.jpg")!.Date);
        Assert.Equal(new PhotoDate(new DateTime(2011, 6, 6, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), DateSource.File, PhotoDates.Version), index.Find("c.jpg")!.Date);

        var again = await IndexingJob.RunAsync(index, _models, null, [".jpg"], null, CancellationToken.None);
        Assert.Equal(0, again.DatesRead); // nothing is read twice
    }

    [Fact]
    public async Task ReadingDates_ReportsItsProgress_AndLeavesTheOtherDataAlone()
    {
        var file = _photos.File("a.jpg");
        File.WriteAllBytes(file, ExifJpeg.Build(original: "2009:09:01 14:30:00"));
        using var index = PhotoIndex.Open(_photos.Path, Path.Combine(_data.Path, "index.db"));
        var photo = Indexed("a.jpg", file);
        photo.Faces = [new IndexedFace(photo, 0.1f, 0.2f, 0.3f, 0.4f, 0.9f, manual: true, Vectors.Unit(3))];
        index.Save([photo]);
        var reports = new List<IndexProgress>();

        await IndexingJob.RunAsync(index, _models, null, [".jpg"], new SynchronousProgress(reports.Add), CancellationToken.None);

        Assert.Contains(reports, r => r.Phase == IndexPhase.ReadingDates && r.Done == 1 && r.Total == 1);
        var saved = index.Find("a.jpg")!;
        Assert.Equal(Vectors.Unit(1), saved.Clip);
        Assert.Single(saved.Faces);
    }

    [Fact]
    public async Task APhotoThatCanNotBeReadNow_IsTriedAgainNextTime()
    {
        var file = _photos.File("a.jpg");
        File.WriteAllBytes(file, ExifJpeg.Build(original: "2009:09:01 14:30:00"));
        using var index = PhotoIndex.Open(_photos.Path, Path.Combine(_data.Path, "index.db"));
        index.Save([Indexed("a.jpg", file)]);

        // Locked so that it can not be opened for reading (a share mode of none)
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var locked = await IndexingJob.RunAsync(index, _models, null, [".jpg"], null, CancellationToken.None);
            Assert.Equal(0, locked.DatesRead);
            Assert.Equal(1, index.DatesPending);
        }

        var free = await IndexingJob.RunAsync(index, _models, null, [".jpg"], null, CancellationToken.None);
        Assert.Equal(1, free.DatesRead);
        Assert.Equal(0, index.DatesPending);
    }

    private sealed class SynchronousProgress(Action<IndexProgress> handler) : IProgress<IndexProgress>
    {
        public void Report(IndexProgress value) => handler(value);
    }
}

public class PhotoDatesFromFileTests
{
    private static readonly DateTime Now = new(2024, 6, 1, 12, 0, 0);

    [Fact]
    public void ThePhotoOfAFolderListing_IsDatedByWhenItsFileWasCreated()
    {
        var file = new PhotoFile(@"C:\photos\a.jpg", string.Empty)
        {
            CreationTimeUtc = new DateTime(2010, 5, 5, 12, 0, 0, DateTimeKind.Utc),
            LastWriteTimeUtc = new DateTime(2003, 4, 4, 9, 0, 0, DateTimeKind.Utc),
        };

        Assert.Equal(new DateTime(2010, 5, 5, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), PhotoDates.FromFile(file, Now));
    }

    [Fact]
    public void WithoutACreationTime_TheTimeOfTheLastChangeIsUsed_AndWithoutEitherThereIsNoDate()
    {
        var changedOnly = new PhotoFile(@"C:\photos\a.jpg", string.Empty) { LastWriteTimeUtc = new DateTime(2003, 4, 4, 9, 0, 0, DateTimeKind.Utc) };
        var neither = new PhotoFile(@"C:\photos\b.jpg", string.Empty);

        Assert.Equal(new DateTime(2003, 4, 4, 9, 0, 0, DateTimeKind.Utc).ToLocalTime(), PhotoDates.FromFile(changedOnly, Now));
        Assert.Null(PhotoDates.FromFile(neither, Now));
    }
}
