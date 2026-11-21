using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Imaging.Tests;

public class PhotoFormatsTests
{
    [Theory]
    [InlineData(@"C:\x\a.jpg", PhotoFormat.Jpeg)]
    [InlineData(@"C:\x\a.JPEG", PhotoFormat.Jpeg)]
    [InlineData(@"C:\x\a.png", PhotoFormat.Png)]
    [InlineData(@"C:\x\a.Bmp", PhotoFormat.Bmp)]
    [InlineData(@"C:\x\a.gif", PhotoFormat.Gif)]
    [InlineData(@"C:\x\a.tif", PhotoFormat.Tiff)]
    [InlineData(@"C:\x\a.TIFF", PhotoFormat.Tiff)]
    public void TheExtensionOfAName_SaysTheFormat(string path, PhotoFormat format) => Assert.Equal(format, PhotoFormats.ForPath(path));

    [Theory]
    [InlineData(@"C:\x\a.webp")]  // can be read, cannot be written
    [InlineData(@"C:\x\a.heic")]
    [InlineData(@"C:\x\a.mp4")]
    [InlineData(@"C:\x\a")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNoFormat(string? path) => Assert.Null(PhotoFormats.ForPath(path));

    [Fact]
    public void EveryFormat_HasAnExtensionThatSaysItself_AndAFilterThatBeginsWithIt()
    {
        foreach (var format in PhotoFormats.All)
        {
            Assert.Equal(format, PhotoFormats.ForPath("x" + PhotoFormats.Extension(format)));

            var kind = PhotoFormats.Filter(format).Split('|');
            Assert.Equal(2, kind.Length); // the text and the patterns: a window refuses a filter that is not made of such pairs
            Assert.StartsWith("*" + PhotoFormats.Extension(format), kind[1]);
        }
    }

    [Fact]
    public void TheExtensionsAreTheOnesTheProgramListsPhotosBy_SoASavedPictureJoinsTheList()
    {
        var listed = new PhotoLiker.Core.Settings().Extensions;
        foreach (var format in PhotoFormats.All)
            Assert.Contains(PhotoFormats.Extension(format), listed);
    }

    [Theory]
    [InlineData(@"C:\x\a.png", @"C:\x\a.png")]
    [InlineData(@"C:\x\a", @"C:\x\a.jpg")]
    [InlineData(@"C:\x\a.xyz", @"C:\x\a.xyz.jpg")]
    [InlineData(@"C:\x\a.tif", @"C:\x\a.tif")]
    public void AName_GetsTheFallbackExtension_OnlyWhenItSaysNoFormat(string typed, string result) => Assert.Equal(result, PhotoFormats.WithExtension(typed, PhotoFormat.Jpeg));
}

public sealed class PhotoConverterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "photoliker-convert-" + Guid.NewGuid().ToString("N"));

    public PhotoConverterTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string Path_(string name) => Path.Combine(_folder, name);

    /// <summary>A picture whose left half is red and whose right half is blue (so it can be seen whether it was turned), in the format of <paramref name="format"/>.</summary>
    private static byte[] Picture(int width, int height, PhotoFormat format, ushort? orientation = null, BitmapMetadata? metadata = null)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 3;
            var left = x < width / 2;
            pixels[i] = (byte)(left ? 0 : 255);       // blue
            pixels[i + 2] = (byte)(left ? 255 : 0);   // red
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);

        if (orientation is { } value)
        {
            metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", value);
        }

        BitmapEncoder encoder = format switch
        {
            PhotoFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = 100 },
            PhotoFormat.Png => new PngBitmapEncoder(),
            PhotoFormat.Bmp => new BmpBitmapEncoder(),
            PhotoFormat.Gif => new GifBitmapEncoder(),
            _ => new TiffBitmapEncoder(),
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapFrame Open(byte[] bytes) => BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

    private static (byte Blue, byte Green, byte Red) Pixel(byte[] bytes, int x, int y)
    {
        var bgr = new FormatConvertedBitmap(Open(bytes), PixelFormats.Bgr24, null, 0);
        var pixel = new byte[3];
        bgr.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 3, 0);
        return (pixel[0], pixel[1], pixel[2]);
    }

    private static bool Starts(byte[] bytes, params byte[] signature) => bytes.AsSpan().StartsWith(signature);

    // ── the format that comes out ───────────────────────────────────────────

    [Theory]
    [InlineData(PhotoFormat.Jpeg, new byte[] { 0xFF, 0xD8 })]
    [InlineData(PhotoFormat.Png, new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(PhotoFormat.Bmp, new byte[] { 0x42, 0x4D })]
    [InlineData(PhotoFormat.Gif, new byte[] { 0x47, 0x49, 0x46, 0x38 })]
    public void APhoto_BecomesTheFormatThatWasAskedFor(PhotoFormat format, byte[] signature)
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Png), format);

        Assert.True(Starts(converted, signature), $"the bytes are a {format}");
        var frame = Open(converted);
        Assert.Equal((40, 20), (frame.PixelWidth, frame.PixelHeight));
    }

    [Fact]
    public void ATiff_BeginsAsATiffDoes()
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Png), PhotoFormat.Tiff);

        Assert.True(Starts(converted, 0x49, 0x49, 0x2A, 0x00) || Starts(converted, 0x4D, 0x4D, 0x00, 0x2A));
    }

    [Theory]
    [InlineData(PhotoFormat.Jpeg, PhotoFormat.Png)]
    [InlineData(PhotoFormat.Png, PhotoFormat.Jpeg)]
    [InlineData(PhotoFormat.Png, PhotoFormat.Bmp)]
    [InlineData(PhotoFormat.Bmp, PhotoFormat.Tiff)]
    [InlineData(PhotoFormat.Tiff, PhotoFormat.Png)]
    [InlineData(PhotoFormat.Jpeg, PhotoFormat.Tiff)]
    public void ThePicture_IsTheSame_InTheNewFormat(PhotoFormat from, PhotoFormat to)
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, from), to);

        var left = Pixel(converted, 5, 10);
        var right = Pixel(converted, 35, 10);
        Assert.True(left.Red > 200 && left.Blue < 60, $"the left side is red, it is {left}");
        Assert.True(right.Blue > 200 && right.Red < 60, $"the right side is blue, it is {right}");
    }

    [Fact]
    public void TheFullSizeIsKept()
    {
        var converted = PhotoConverter.Encode(Picture(3000, 2000, PhotoFormat.Jpeg), PhotoFormat.Png);

        var frame = Open(converted);
        Assert.Equal((3000, 2000), (frame.PixelWidth, frame.PixelHeight));
    }

    // ── the way up ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 40, 20)]  // upside down: the same size
    [InlineData(6, 20, 40)]  // a quarter turn: the sides swap
    [InlineData(8, 20, 40)]
    public void APhotoThatSaysItIsTurned_IsWrittenUpright_BecauseTheNewFileSaysNothingOfIt(int orientation, int width, int height)
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Jpeg, (ushort)orientation), PhotoFormat.Png);

        var frame = Open(converted);
        Assert.Equal((width, height), (frame.PixelWidth, frame.PixelHeight));
    }

    [Fact]
    public void ARotatedPhoto_IsTurnedTheRightWay_RedLeftHalfGoesToTheTop_WhenTurnedClockwise()
    {
        // Orientation 6: the picture is to be turned a quarter clockwise to be upright. The left half (red) of the stored picture then is the top.
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Jpeg, orientation: 6), PhotoFormat.Png);

        var top = Pixel(converted, 10, 5);
        var bottom = Pixel(converted, 10, 35);
        Assert.True(top.Red > 200 && top.Blue < 60, $"the top is red, it is {top}");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"the bottom is blue, it is {bottom}");
    }

    // ── transparency ────────────────────────────────────────────────────────

    private static byte[] TransparentPng()
    {
        // 48 x 16 in three zones of 16 pixels: opaque red, fully transparent (its colour is black), half transparent blue
        var pixels = new byte[48 * 16 * 4];
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 48; x++)
        {
            var i = (y * 48 + x) * 4;
            switch (x / 16)
            {
                case 0: pixels[i + 2] = 255; pixels[i + 3] = 255; break;
                case 2: pixels[i] = 255; pixels[i + 3] = 128; break;
            }
        }
        var bitmap = BitmapSource.Create(48, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 48 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(PhotoFormat.Jpeg)]
    [InlineData(PhotoFormat.Bmp)]
    public void WhereAFormatHasNoTransparency_ItIsWhite_NotBlack(PhotoFormat format)
    {
        var converted = PhotoConverter.Encode(TransparentPng(), format);

        var red = Pixel(converted, 8, 8);
        var clear = Pixel(converted, 24, 8);
        var half = Pixel(converted, 40, 8);
        Assert.True(red.Red > 230 && red.Green < 40 && red.Blue < 40, $"the opaque pixel stays red: {red}");
        Assert.True(clear.Red > 235 && clear.Green > 235 && clear.Blue > 235, $"the transparent pixel is white: {clear}");
        Assert.True(half.Blue > 230 && half.Red is > 100 and < 160 && half.Green is > 100 and < 160, $"the half transparent blue is a pale blue: {half}");
    }

    [Fact]
    public void WhereAFormatHasTransparency_ItIsKept()
    {
        var converted = PhotoConverter.Encode(TransparentPng(), PhotoFormat.Tiff);

        var pixel = new byte[4];
        new FormatConvertedBitmap(Open(converted), PixelFormats.Bgra32, null, 0).CopyPixels(new System.Windows.Int32Rect(24, 8, 1, 1), pixel, 4, 0);
        Assert.Equal(0, pixel[3]);
    }

    // ── what goes with the picture ──────────────────────────────────────────

    /// <summary>The Exif a camera writes, with a date from 1925: a date that goes through a text with a two-digit year would come back as 2025.</summary>
    private static BitmapMetadata Notes()
    {
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", "1925:06:01 09:00:00");
        metadata.SetQuery("/app1/ifd/{ushort=271}", "Canon");
        metadata.SetQuery("/app1/ifd/{ushort=272}", "Canon EOS 5D");
        metadata.SetQuery("/app1/ifd/{ushort=33432}", "Granny's family");
        metadata.SetQuery("/app1/ifd/{ushort=270}", "Summer at the lake");
        metadata.SetQuery("/app1/ifd/{ushort=315}", "Uncle Ivan");
        return metadata;
    }

    private static string Text(byte[] picture, string query) => (string)((BitmapMetadata)Open(picture).Metadata).GetQuery(query);

    [Fact]
    public void ThePlaceOfTheNotes_IsAsTheTestsThinkOfIt_ASourceJpegHasThem()
    {
        var source = Picture(40, 20, PhotoFormat.Jpeg, metadata: Notes());

        Assert.Equal("1925:06:01 09:00:00", Text(source, "/app1/ifd/exif/{ushort=36867}"));
        Assert.Equal("jpg", ((BitmapMetadata)Open(source).Metadata).Format);
    }

    [Fact]
    public void TheDatesAndTheCamera_GoWithAPhoto_ToATiff_AsTheCameraWroteThem()
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Jpeg, metadata: Notes()), PhotoFormat.Tiff);

        Assert.Equal("1925:06:01 09:00:00", Text(converted, "/ifd/exif/{ushort=36867}"));
        Assert.Equal("Canon EOS 5D", Text(converted, "/ifd/{ushort=272}"));
        Assert.Equal("Canon", Text(converted, "/ifd/{ushort=271}"));
        Assert.Equal("Granny's family", Text(converted, "/ifd/{ushort=33432}"));
        Assert.Equal("Summer at the lake", Text(converted, "/ifd/{ushort=270}"));
        Assert.Equal("Uncle Ivan", Text(converted, "/ifd/{ushort=315}"));
    }

    [Fact]
    public void ATiffWithNotes_GivesThemToAJpeg()
    {
        var tiff = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Jpeg, metadata: Notes()), PhotoFormat.Tiff);

        var converted = PhotoConverter.Encode(tiff, PhotoFormat.Jpeg);

        Assert.Equal("1925:06:01 09:00:00", Text(converted, "/app1/ifd/exif/{ushort=36867}"));
        Assert.Equal("Canon EOS 5D", Text(converted, "/app1/ifd/{ushort=272}"));
    }

    [Fact]
    public void ThePng_GetsTheDateTaken_InAFormThatCannotBeMisread()
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Jpeg, metadata: Notes()), PhotoFormat.Png);

        Assert.Contains("1925-06-01T09:00:00", System.Text.Encoding.Latin1.GetString(converted));
        Assert.Equal("Canon EOS 5D", ((BitmapMetadata)Open(converted).Metadata).CameraModel);
    }

    [Fact]
    public void APhotoWithNoNotes_IsConvertedAllTheSame()
    {
        var converted = PhotoConverter.Encode(Picture(40, 20, PhotoFormat.Bmp), PhotoFormat.Jpeg);

        Assert.True(Starts(converted, 0xFF, 0xD8));
    }

    // ── the file ────────────────────────────────────────────────────────────

    [Fact]
    public void AFile_IsConverted_ToTheFormatTheNewNameSays_WithTheDatesOfTheOne_AndNothingElseLeftBehind()
    {
        var source = Path_("granny.jpg");
        File.WriteAllBytes(source, Picture(60, 40, PhotoFormat.Jpeg));
        File.SetCreationTimeUtc(source, new DateTime(2012, 6, 1, 9, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(source, new DateTime(2012, 6, 2, 10, 30, 0, DateTimeKind.Utc));
        var destination = Path_("granny.png");

        var result = PhotoConverter.Convert(source, destination);

        Assert.Equal(destination, result);
        Assert.True(Starts(File.ReadAllBytes(destination), 0x89, 0x50, 0x4E, 0x47));
        Assert.Equal(new DateTime(2012, 6, 1, 9, 0, 0, DateTimeKind.Utc), File.GetCreationTimeUtc(destination));
        Assert.Equal(new DateTime(2012, 6, 2, 10, 30, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(destination));
        Assert.Equal(["granny.jpg", "granny.png"], Directory.GetFiles(_folder).Select(file => Path.GetFileName(file)).Order().ToArray()); // no half-written file
        Assert.True(Starts(File.ReadAllBytes(source), 0xFF, 0xD8), "the photo is as it was");
    }

    [Fact]
    public void AFileThatIsThere_IsWrittenOver()
    {
        var source = Path_("granny.jpg");
        File.WriteAllBytes(source, Picture(60, 40, PhotoFormat.Jpeg));
        var destination = Path_("old.png");
        File.WriteAllText(destination, "something else");

        PhotoConverter.Convert(source, destination);

        Assert.True(Starts(File.ReadAllBytes(destination), 0x89, 0x50, 0x4E, 0x47));
    }

    [Fact]
    public void AFileThatIsNoPicture_IsNotConverted_AndNothingIsLeftBehind()
    {
        var source = Path_("notes.jpg");
        File.WriteAllText(source, "this is not a picture at all");
        var destination = Path_("notes.png");

        Assert.Throws<NotSupportedException>(() => PhotoConverter.Convert(source, destination));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public void AnExtensionThatIsNoFormat_IsRefused_AndNothingIsWritten()
    {
        var source = Path_("granny.jpg");
        File.WriteAllBytes(source, Picture(60, 40, PhotoFormat.Jpeg));
        var destination = Path_("granny.webp");

        Assert.Throws<NotSupportedException>(() => PhotoConverter.Convert(source, destination));

        Assert.False(File.Exists(destination));
    }
}
