using System.Buffers.Binary;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class JpegOrientationTests : IDisposable
{
    private static readonly DateTime Taken = new(2015, 4, 18, 10, 11, 12, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2016, 5, 19, 6, 7, 8, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLJpegOrientationTests_" + Guid.NewGuid());

    public JpegOrientationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    private string Save(byte[] jpeg, string name = "photo.jpg")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, jpeg);
        File.SetCreationTimeUtc(path, Created);
        File.SetLastWriteTimeUtc(path, Taken);
        return path;
    }

    // ── the numbers ─────────────────────────────────────────────────────────

    // The 2x2 matrix that turns the stored pixels into the shown ones for each orientation (x to the right, y down), worked out
    // from the definition in the EXIF standard and not from the table of the code
    private static readonly Dictionary<int, int[]> Shown = new()
    {
        [1] = [1, 0, 0, 1],      // as stored
        [2] = [-1, 0, 0, 1],     // mirrored left to right
        [3] = [-1, 0, 0, -1],    // turned half
        [4] = [1, 0, 0, -1],     // mirrored top to bottom
        [5] = [0, 1, 1, 0],      // mirrored along the main diagonal
        [6] = [0, -1, 1, 0],     // turned a quarter clockwise
        [7] = [0, -1, -1, 0],    // mirrored along the other diagonal
        [8] = [0, 1, -1, 0],     // turned a quarter counter-clockwise
    };

    private static int[] Multiply(int[] a, int[] b) =>
        [a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3], a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3]];

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void Turn_ATurnedPhotoIsShownAQuarterTurnedClockwise(int orientation)
    {
        var clockwise = new[] { 0, -1, 1, 0 };
        var expected = Multiply(clockwise, Shown[orientation]);

        var turned = JpegOrientation.Turn(orientation, 1);

        Assert.Equal(expected, Shown[turned]);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void Turn_FourQuartersAreBackWhereItStarted(int orientation)
    {
        Assert.Equal(orientation, JpegOrientation.Turn(JpegOrientation.Turn(JpegOrientation.Turn(JpegOrientation.Turn(orientation, 1), 1), 1), 1));
        Assert.Equal(orientation, JpegOrientation.Turn(orientation, 4));
        Assert.Equal(JpegOrientation.Turn(orientation, 3), JpegOrientation.Turn(orientation, -1));
        Assert.Equal(JpegOrientation.Turn(orientation, 2), JpegOrientation.Turn(JpegOrientation.Turn(orientation, 1), 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-3)]
    public void Turn_ANumberThatIsNoOrientationIsTakenForNoTurning(int orientation)
    {
        Assert.Equal(6, JpegOrientation.Turn(orientation, 1));
    }

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("A.JPEG", true)]
    [InlineData("a.png", false)]
    [InlineData("a.jpg.bak", false)]
    [InlineData("jpg", false)]
    public void IsJpeg_GoesByTheExtension(string name, bool expected)
    {
        Assert.Equal(expected, JpegOrientation.IsJpeg(name));
    }

    // ── a photo that has an orientation: two bytes change ──────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePhotoWithAnOrientation_IsTurnedInPlace_NothingElseChanges(bool bigEndian)
    {
        var original = TestJpeg.Build(orientation: 1, bigEndian: bigEndian);
        var path = Save(original);

        var result = JpegOrientation.TurnClockwise(path);

        Assert.Equal(6, result.Orientation);
        Assert.False(result.Rewritten);
        Assert.True(result.DateKept);
        Assert.Equal(original.Length, result.Length);

        var turned = File.ReadAllBytes(path);
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(turned)));
        var differing = Enumerable.Range(0, original.Length).Where(i => original[i] != turned[i]).ToList();
        Assert.True(differing.Count <= 2, $"{differing.Count} bytes differ");
    }

    [Fact]
    public void ThePhotoWithAnOrientation_KeepsTheDatesOfTheFile()
    {
        var path = Save(TestJpeg.Build(orientation: 6));

        var result = JpegOrientation.TurnClockwise(path);

        Assert.Equal(3, result.Orientation);
        Assert.True(result.DateKept);
        Assert.Equal(Taken, File.GetLastWriteTimeUtc(path));
        Assert.Equal(Created, File.GetCreationTimeUtc(path));
    }

    [Fact]
    public void TurningFourTimes_GivesTheSameFileBack()
    {
        var original = TestJpeg.Build(orientation: 1);
        var path = Save(original);

        for (var i = 0; i < 4; i++)
            JpegOrientation.TurnClockwise(path);

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ANumberThatIsNoOrientation_BecomesAQuarterTurn()
    {
        var path = Save(TestJpeg.Build(orientation: 9));

        var result = JpegOrientation.TurnClockwise(path);

        Assert.Equal(6, result.Orientation);
    }

    [Fact]
    public void NoTurning_LeavesTheFileAlone()
    {
        var original = TestJpeg.Build(orientation: 6);
        var path = Save(original);

        var result = JpegOrientation.TurnClockwise(path, quarterTurns: 0);

        Assert.Equal(6, result.Orientation);
        Assert.False(result.Rewritten);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(Taken, File.GetLastWriteTimeUtc(path));
    }

    // ── a photo that has none: the Exif block grows ────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePhotoWithExifButNoOrientation_GetsOne_AndKeepsWhatItHad(bool bigEndian)
    {
        var original = TestJpeg.Build(orientation: null, bigEndian: bigEndian);
        var path = Save(original);

        var result = JpegOrientation.TurnClockwise(path);

        Assert.Equal(6, result.Orientation);
        Assert.True(result.Rewritten);
        Assert.True(result.DateKept);
        Assert.True(result.Length > original.Length);
        Assert.Equal(result.Length, new FileInfo(path).Length);

        var turned = File.ReadAllBytes(path);
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(turned)));

        // The directory is in order, and everything the other entries pointed to is still where they say
        var tags = TestJpeg.Ifd0Tags(turned);
        Assert.Equal(tags.OrderBy(t => t), tags);
        Assert.Equal(TestJpeg.Ifd0Tags(original).Append((ushort)0x0112).OrderBy(t => t), tags);
        Assert.Equal(TestJpeg.DateTimeText, TestJpeg.ReadDateTime(turned));
        Assert.Equal(TestJpeg.PictureData, TestJpeg.AfterExif(turned));
    }

    [Fact]
    public void ThePhotoWithExifButNoOrientation_KeepsItsDates_AndTurnsAgainInPlace()
    {
        var path = Save(TestJpeg.Build(orientation: null));

        JpegOrientation.TurnClockwise(path);

        Assert.Equal(Taken, File.GetLastWriteTimeUtc(path));
        Assert.Equal(Created, File.GetCreationTimeUtc(path));

        // It has the number now, so the next turn is two bytes
        var size = new FileInfo(path).Length;
        var second = JpegOrientation.TurnClockwise(path);
        Assert.False(second.Rewritten);
        Assert.Equal(3, second.Orientation);
        Assert.Equal(size, second.Length);
    }

    [Fact]
    public void ThePhotoWithoutExif_GetsAnExifBlockAfterTheStartOfTheFile()
    {
        var original = TestJpeg.Build(exif: false);
        var path = Save(original);

        var result = JpegOrientation.TurnClockwise(path);

        Assert.Equal(6, result.Orientation);
        Assert.True(result.Rewritten);
        var turned = File.ReadAllBytes(path);
        Assert.Equal(original.Length + 36, turned.Length);
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(turned)));
        Assert.Equal(original.AsSpan(0, 2).ToArray(), turned.AsSpan(0, 2).ToArray());
        Assert.Equal(original.AsSpan(2).ToArray(), turned.AsSpan(38).ToArray());
    }

    [Fact]
    public void ThePhotoWithoutExif_KeepsJfifAsTheFirstBlock()
    {
        var original = TestJpeg.Build(exif: false, jfif: true);
        var path = Save(original);

        JpegOrientation.TurnClockwise(path);

        var turned = File.ReadAllBytes(path);
        Assert.Equal(0xE0, turned[3]);                     // the JFIF block is still the first
        Assert.Equal(0xE1, turned[2 + 2 + 16 + 1]);        // the Exif block follows it
        Assert.Equal(6, JpegOrientation.Read(new MemoryStream(turned)));
        Assert.Equal(original.Length + 36, turned.Length);
    }

    [Fact]
    public void ThePhotoWithoutExif_CanBeTurnedAroundAndEndsAsItStartedExceptForTheBlock()
    {
        var path = Save(TestJpeg.Build(exif: false));

        for (var i = 0; i < 4; i++)
            JpegOrientation.TurnClockwise(path);

        Assert.Equal(1, JpegOrientation.Read(new MemoryStream(File.ReadAllBytes(path))));
        Assert.Equal(TestJpeg.PictureData, TestJpeg.AfterExif(File.ReadAllBytes(path)));
    }

    [Fact]
    public void TheTemporaryFile_IsNotLeftBehind()
    {
        var path = Save(TestJpeg.Build(orientation: null));

        JpegOrientation.TurnClockwise(path);

        Assert.Equal(new[] { path }, Directory.GetFiles(_folder));
    }

    // ── what is refused ────────────────────────────────────────────────────

    [Fact]
    public void ANonJpeg_IsRefused_AndLeftAlone()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var path = Save(png, "photo.jpg");

        Assert.Throws<InvalidDataException>(() => JpegOrientation.TurnClockwise(path));

        Assert.Equal(png, File.ReadAllBytes(path));
        Assert.Equal(Taken, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ADamagedExifBlock_IsRefused_AndLeftAlone()
    {
        // The directory says it has 500 entries, in a block that is a few bytes long
        var jpeg = TestJpeg.Build(orientation: null);
        var tiffAt = 2 + 4 + 6; // after the start of the file, the marker and length of the block, and "Exif"
        var ifd0 = tiffAt + BinaryPrimitives.ReadInt32LittleEndian(jpeg.AsSpan(tiffAt + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(jpeg.AsSpan(ifd0), 500);
        var path = Save(jpeg);

        Assert.Throws<InvalidDataException>(() => JpegOrientation.TurnClockwise(path));

        Assert.Equal(jpeg, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnExifBlockWithNoRoomForTheEntry_IsRefused_AndLeftAlone()
    {
        // The block is 65,506 bytes, nearly the 64 KB a block may have: the grown directory does not fit
        var jpeg = TestJpeg.Build(orientation: null, padding: 65_440);
        var path = Save(jpeg);

        Assert.Throws<InvalidDataException>(() => JpegOrientation.TurnClockwise(path));

        Assert.Equal(jpeg, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_folder));
    }

    [Fact]
    public void AMissingFile_Throws()
    {
        Assert.ThrowsAny<IOException>(() => JpegOrientation.TurnClockwise(Path.Combine(_folder, "missing.jpg")));
    }
}

/// <summary>JPEG files made of the parts that matter here: markers, an Exif block, and some bytes for the picture.</summary>
internal static class TestJpeg
{
    public const string DateTimeText = "2015:04:18 10:11:12";

    public static readonly byte[] PictureData = [0xFF, 0xDA, 0x00, 0x04, 0x12, 0x34, 0xAA, 0xBB, 0xCC, 0xFF, 0xD9];

    /// <param name="orientation">The orientation in the directory; none when null.</param>
    /// <param name="exif">Whether the photo has an Exif block at all.</param>
    /// <param name="padding">Bytes of data in the block that nothing points to, to make it large.</param>
    public static byte[] Build(int? orientation = null, bool exif = true, bool jfif = false, bool bigEndian = false, int padding = 0)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write([0xFF, 0xD8]);
        if (jfif)
        {
            jpeg.Write([0xFF, 0xE0, 0x00, 0x10]);
            jpeg.Write("JFIF\0\x01\x01\0\0\x01\0\x01\0\0"u8);
        }

        if (exif)
        {
            var tiff = Tiff(orientation, bigEndian, padding);
            var length = 2 + 6 + tiff.Length;
            jpeg.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
            jpeg.Write("Exif\0\0"u8);
            jpeg.Write(tiff);
        }

        jpeg.Write([0xFF, 0xDB, 0x00, 0x04, 0x00, 0x10]); // some other block between the Exif block and the picture
        jpeg.Write(PictureData);
        return jpeg.ToArray();
    }

    // Directory 0: Make (0x010F, in the entry), [Orientation (0x0112)], DateTime (0x0132, text elsewhere in the block)
    private static byte[] Tiff(int? orientation, bool big, int padding)
    {
        var entries = 2 + (orientation is null ? 0 : 1);
        var dataAt = 8 + 2 + entries * 12 + 4;
        using var tiff = new MemoryStream();

        void U16(int value) => tiff.Write(big ? [(byte)(value >> 8), (byte)value] : new[] { (byte)value, (byte)(value >> 8) });
        void U32(uint value) => tiff.Write(big
            ? [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]
            : new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });

        tiff.Write(big ? "MM"u8 : "II"u8);
        U16(42);
        U32(8);

        U16(entries);
        U16(0x010F); U16(2); U32(4); tiff.Write("Cam\0"u8);
        if (orientation is { } value)
        {
            U16(0x0112); U16(3); U32(1); U16(value); U16(0);
        }
        U16(0x0132); U16(2); U32(20); U32((uint)dataAt);
        U32(0);

        tiff.Write(System.Text.Encoding.ASCII.GetBytes(DateTimeText + "\0"));
        tiff.Write(new byte[padding]);
        return tiff.ToArray();
    }

    private static (int ExifPayloadAt, int ExifPayloadLength)? FindExif(byte[] jpeg)
    {
        for (var at = 2; at + 4 < jpeg.Length;)
        {
            var length = jpeg[at + 2] << 8 | jpeg[at + 3];
            if (jpeg[at + 1] == 0xE1)
                return (at + 4, length - 2);
            at += 2 + length;
        }
        return null;
    }

    private static ushort U16(byte[] data, int at, bool big) =>
        big ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));

    private static uint U32(byte[] data, int at, bool big) =>
        big ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));

    /// <summary>The tags of directory 0 of the Exif block, in the order the directory has them.</summary>
    public static List<ushort> Ifd0Tags(byte[] jpeg)
    {
        var tiff = FindExif(jpeg)!.Value.ExifPayloadAt + 6;
        var big = jpeg[tiff] == 'M';
        var ifd0 = tiff + (int)U32(jpeg, tiff + 4, big);
        return [.. Enumerable.Range(0, U16(jpeg, ifd0, big)).Select(i => U16(jpeg, ifd0 + 2 + i * 12, big))];
    }

    /// <summary>The text the DateTime entry points to, found through the directory the way a reader finds it.</summary>
    public static string ReadDateTime(byte[] jpeg)
    {
        var tiff = FindExif(jpeg)!.Value.ExifPayloadAt + 6;
        var big = jpeg[tiff] == 'M';
        var ifd0 = tiff + (int)U32(jpeg, tiff + 4, big);
        for (var i = 0; i < U16(jpeg, ifd0, big); i++)
        {
            var entry = ifd0 + 2 + i * 12;
            if (U16(jpeg, entry, big) == 0x0132)
                return System.Text.Encoding.ASCII.GetString(jpeg, tiff + (int)U32(jpeg, entry + 8, big), 19);
        }
        throw new InvalidOperationException("No DateTime entry.");
    }

    /// <summary>What follows the Exif block (and the other block that comes after it): the picture data.</summary>
    public static byte[] AfterExif(byte[] jpeg) => jpeg[^PictureData.Length..];
}
