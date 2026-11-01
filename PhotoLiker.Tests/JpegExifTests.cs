using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Writing dates into the Exif block of a JPEG: in place when the block has the date, added when it does not.</summary>
public class JpegExifTests : IDisposable
{
    private const string Before = "2001:01:01 00:00:05";
    private static readonly DateTime NewDate = new(2009, 9, 1, 14, 30, 0);
    private const string NewText = "2009:09:01 14:30:00";

    private static readonly DateTime FileModified = new(2015, 4, 18, 10, 11, 12, DateTimeKind.Utc);
    private static readonly DateTime FileCreated = new(2016, 5, 19, 6, 7, 8, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLJpegExifTests_" + Guid.NewGuid());

    public JpegExifTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    private string Save(byte[] jpeg)
    {
        var path = Path.Combine(_folder, "photo.jpg");
        File.WriteAllBytes(path, jpeg);
        File.SetCreationTimeUtc(path, FileCreated);
        File.SetLastWriteTimeUtc(path, FileModified);
        return path;
    }

    // ── the date is there: it is overwritten where it is ───────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDateTaken_ThatIsThere_IsOverwrittenInPlace(bool bigEndian)
    {
        var original = TestJpeg.Build(orientation: 1, bigEndian: bigEndian, original: Before, digitized: Before);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { Original = NewDate });

        Assert.False(result.Rewritten);
        Assert.True(result.DateKept);
        Assert.Equal(original.Length, result.Length);

        var saved = File.ReadAllBytes(path);
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal(Before, TestJpeg.ReadExifDate(saved, 0x9004)); // the other date is not touched
        Assert.True(Enumerable.Range(0, original.Length).Count(i => original[i] != saved[i]) <= 19);
        Assert.Equal(FileModified, File.GetLastWriteTimeUtc(path));
        Assert.Equal(FileCreated, File.GetCreationTimeUtc(path));
    }

    [Fact]
    public void TheDateLastSavedAndDigitized_ThatAreThere_AreOverwrittenInPlace()
    {
        var original = TestJpeg.Build(orientation: 1, original: Before, digitized: Before);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { LastSaved = NewDate, Digitized = NewDate });

        Assert.False(result.Rewritten);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(NewText, TestJpeg.ReadDateTime(saved));
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9004));
        Assert.Equal(Before, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal(original.Length, saved.Length);
    }

    [Fact]
    public void TurningAndSettingTheDate_ThatBothArePlaced_IsOneWriteInPlace()
    {
        var original = TestJpeg.Build(orientation: 1, original: Before);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { QuarterTurns = 1, Original = NewDate });

        Assert.False(result.Rewritten);
        Assert.Equal(6, result.Orientation);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(saved)));
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
    }

    // ── the date is not there: it is added ─────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDateTaken_OfAPhotoWithoutAnExifDirectory_IsAdded_WithTheDirectory(bool bigEndian)
    {
        var original = TestJpeg.Build(orientation: 1, bigEndian: bigEndian);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { Original = NewDate });

        Assert.True(result.Rewritten);
        Assert.True(result.DateKept);
        Assert.True(result.Length > original.Length);

        var saved = File.ReadAllBytes(path);
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal([(ushort)0x9003], TestJpeg.ExifIfdTags(saved));

        // The main directory has the pointer, in order, and everything it had is where it says
        var tags = TestJpeg.Ifd0Tags(saved);
        Assert.Equal(tags.OrderBy(t => t), tags);
        Assert.Equal(TestJpeg.Ifd0Tags(original).Append((ushort)0x8769).OrderBy(t => t), tags);
        Assert.Equal(TestJpeg.DateTimeText, TestJpeg.ReadDateTime(saved));
        Assert.Equal(1, JpegOrientation.Read(new MemoryStream(saved)));
        Assert.Equal(TestJpeg.PictureData, TestJpeg.AfterExif(saved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDateTaken_OfAPhotoWithOnlyTheDigitizedDate_IsAddedToItsExifDirectory_InOrder(bool bigEndian)
    {
        var original = TestJpeg.Build(orientation: 1, bigEndian: bigEndian, digitized: Before);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { Original = NewDate });

        Assert.True(result.Rewritten);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal(Before, TestJpeg.ReadExifDate(saved, 0x9004));
        Assert.Equal([(ushort)0x9003, (ushort)0x9004], TestJpeg.ExifIfdTags(saved));
        Assert.Equal(TestJpeg.Ifd0Tags(original), TestJpeg.Ifd0Tags(saved)); // the pointer was there, only its value moved
        Assert.Equal(TestJpeg.DateTimeText, TestJpeg.ReadDateTime(saved));
        Assert.Equal(TestJpeg.PictureData, TestJpeg.AfterExif(saved));
    }

    [Fact]
    public void ADateTakenThatIsStoredWrongly_IsReplacedByAnEntryThatIsRight()
    {
        // The entry says it is one character long: there is no room for a date in it
        var jpeg = TestJpeg.Build(orientation: 1, original: Before);
        var tiff = 2 + 4 + 6;
        var exifIfd = tiff + (int)TestJpeg.ExifIfdOffset(jpeg);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(jpeg.AsSpan(exifIfd + 2 + 4), 1);
        var path = Save(jpeg);

        var result = JpegExif.Save(path, new ExifChange { Original = NewDate });

        Assert.True(result.Rewritten);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal([(ushort)0x9003], TestJpeg.ExifIfdTags(saved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePhotoWithoutAnOrientation_GetsBothAtOnce(bool bigEndian)
    {
        var original = TestJpeg.Build(orientation: null, bigEndian: bigEndian);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { QuarterTurns = 1, Original = NewDate });

        Assert.True(result.Rewritten);
        Assert.Equal(6, result.Orientation);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(saved)));
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        var tags = TestJpeg.Ifd0Tags(saved);
        Assert.Equal(tags.OrderBy(t => t), tags);
        Assert.Equal(TestJpeg.Ifd0Tags(original).Concat([(ushort)0x0112, (ushort)0x8769]).OrderBy(t => t), tags);
        Assert.Equal(TestJpeg.PictureData, TestJpeg.AfterExif(saved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOrientationThatIsThere_AndTheDateThatIsNot_AreBothWritten(bool bigEndian)
    {
        var path = Save(TestJpeg.Build(orientation: 1, bigEndian: bigEndian));

        var result = JpegExif.Save(path, new ExifChange { QuarterTurns = 1, Original = NewDate });

        Assert.True(result.Rewritten);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(saved)));
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal(TestJpeg.DateTimeText, TestJpeg.ReadDateTime(saved));
    }

    [Fact]
    public void TheDateThatIsThere_AndTheOrientationThatIsNot_AreBothWritten()
    {
        var path = Save(TestJpeg.Build(orientation: null, original: Before));

        var result = JpegExif.Save(path, new ExifChange { QuarterTurns = 3, Original = NewDate });

        Assert.True(result.Rewritten);
        Assert.Equal(8, result.Orientation);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(8, JpegOrientation.Read(new MemoryStream(saved)));
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
    }

    [Fact]
    public void ThePhotoWithoutExif_GetsABlockWithTheDate()
    {
        var original = TestJpeg.Build(exif: false);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange { Original = NewDate });

        Assert.True(result.Rewritten);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
        Assert.Equal(1, JpegOrientation.Read(new MemoryStream(saved)));
        Assert.Equal(original.AsSpan(0, 2).ToArray(), saved.AsSpan(0, 2).ToArray());
        Assert.Equal(original.AsSpan(2).ToArray(), saved.AsSpan(saved.Length - original.Length + 2).ToArray()); // the rest of the photo follows the block, untouched
    }

    [Fact]
    public void ThePhotoWithoutExif_KeepsJfifAsTheFirstBlock()
    {
        var original = TestJpeg.Build(exif: false, jfif: true);
        var path = Save(original);

        JpegExif.Save(path, new ExifChange { Original = NewDate });

        var saved = File.ReadAllBytes(path);
        Assert.Equal(0xE0, saved[3]);
        Assert.Equal(0xE1, saved[2 + 2 + 16 + 1]);
        Assert.Equal(NewText, TestJpeg.ReadExifDate(saved, 0x9003));
    }

    [Fact]
    public void TheDates_OfTheFile_StayWhenABlockIsAdded_OrAreTheOnesGiven()
    {
        var path = Save(TestJpeg.Build(orientation: 1));
        Assert.True(JpegExif.Save(path, new ExifChange { Original = NewDate }).DateKept);
        Assert.Equal(FileModified, File.GetLastWriteTimeUtc(path));
        Assert.Equal(FileCreated, File.GetCreationTimeUtc(path));

        var given = new DateTime(2009, 9, 1, 11, 30, 0, DateTimeKind.Utc);
        var again = JpegExif.Save(path, new ExifChange { Digitized = NewDate }, given);
        Assert.True(again.DateKept);
        Assert.Equal(given, File.GetLastWriteTimeUtc(path));
        Assert.Equal(given, File.GetCreationTimeUtc(path));
        Assert.Equal(given, again.LastWriteUtc);
    }

    [Fact]
    public void ACopyOfTheOldBlock_IsNotLeftBehind_AndNoTemporaryFileEither()
    {
        var path = Save(TestJpeg.Build(orientation: 1));

        JpegExif.Save(path, new ExifChange { Original = NewDate });

        Assert.Equal(new[] { path }, Directory.GetFiles(_folder));
    }

    // ── nothing to do, and what is refused ─────────────────────────────────

    [Fact]
    public void AnEmptyChange_LeavesTheFileAlone_UnlessDatesAreGiven()
    {
        var original = TestJpeg.Build(orientation: 1);
        var path = Save(original);

        var result = JpegExif.Save(path, new ExifChange());
        Assert.False(result.Rewritten);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(FileModified, File.GetLastWriteTimeUtc(path));

        var given = new DateTime(2009, 9, 1, 11, 30, 0, DateTimeKind.Utc);
        JpegExif.Save(path, new ExifChange(), given);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(given, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ADamagedExifDirectory_IsRefused_AndLeftAlone()
    {
        var jpeg = TestJpeg.Build(orientation: 1, original: Before);
        var tiff = 2 + 4 + 6;
        // The Exif pointer leads far outside the block
        var pointer = tiff + (int)TestJpeg.Ifd0EntryAt(jpeg, 0x8769) + 8;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(jpeg.AsSpan(pointer), 0x00FFFFFF);
        var path = Save(jpeg);

        Assert.Throws<InvalidDataException>(() => JpegExif.Save(path, new ExifChange { Original = NewDate }));

        Assert.Equal(jpeg, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_folder));
    }

    [Fact]
    public void AnExifBlockWithNoRoomForTheDate_IsRefused_AndLeftAlone()
    {
        var jpeg = TestJpeg.Build(orientation: 1, padding: 65_420);
        var path = Save(jpeg);

        Assert.Throws<InvalidDataException>(() => JpegExif.Save(path, new ExifChange { Original = NewDate }));

        Assert.Equal(jpeg, File.ReadAllBytes(path));
    }

    [Fact]
    public void ANonJpeg_IsRefused()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var path = Save(png);

        Assert.Throws<InvalidDataException>(() => JpegExif.Save(path, new ExifChange { Original = NewDate }));

        Assert.Equal(png, File.ReadAllBytes(path));
    }
}
