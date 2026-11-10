using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Core;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Tests.Colorize;

/// <summary>The copy that Colorize writes next to a photo: its name, what it keeps of the photo, and that nothing is ever written over.</summary>
public class ColorizedCopyTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // ── names ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\photos\granny.jpg", @"C:\photos\granny (colorized).jpg")]
    [InlineData(@"C:\photos\granny.JPEG", @"C:\photos\granny (colorized).jpg")]
    [InlineData(@"C:\photos\scan 1952.tif", @"C:\photos\scan 1952 (colorized).jpg")] // always a JPEG
    [InlineData(@"granny.png", @"granny (colorized).jpg")]
    public void TheCopyIsNextToThePhoto_WithTheSuffix_AlwaysAJpeg(string photo, string copy) =>
        Assert.Equal(copy, ColorizedCopy.PathFor(photo));

    [Fact]
    public void ATakenName_GetsANumber_NeverOverwritten()
    {
        var photo = _folder.File("a.jpg");
        File.WriteAllText(photo, "photo");
        File.WriteAllText(_folder.File("a (colorized).jpg"), "first");

        Assert.Equal(_folder.File("a (colorized) 2.jpg"), ColorizedCopy.NextFreePath(photo));

        File.WriteAllText(_folder.File("a (colorized) 2.jpg"), "second");
        File.WriteAllText(_folder.File("a (colorized) 3.jpg"), "third");

        Assert.Equal(_folder.File("a (colorized) 4.jpg"), ColorizedCopy.NextFreePath(photo));
    }

    [Fact]
    public void ThePhotoThatHasACopy_IsToldWhere()
    {
        var photo = _folder.File("b.jpg");
        File.WriteAllText(photo, "photo");
        Assert.Null(ColorizedCopy.ExistingCopy(photo));

        File.WriteAllText(_folder.File("b (colorized) 2.jpg"), "numbered");
        Assert.Equal(_folder.File("b (colorized) 2.jpg"), ColorizedCopy.ExistingCopy(photo));

        File.WriteAllText(_folder.File("b (colorized).jpg"), "first");
        Assert.Equal(_folder.File("b (colorized).jpg"), ColorizedCopy.ExistingCopy(photo));
    }

    [Fact]
    public void ACopyOfAnotherPhoto_IsNotThisPhotosCopy()
    {
        var photo = _folder.File("c.jpg");
        File.WriteAllText(photo, "photo");
        File.WriteAllText(_folder.File("cc (colorized).jpg"), "another photo's");
        File.WriteAllText(_folder.File("c (colorized) notanumber.jpg"), "not numbered");

        Assert.Null(ColorizedCopy.ExistingCopy(photo));
    }

    [Fact]
    public void AFolderThatIsNotThere_HasNoCopy() =>
        Assert.Null(ColorizedCopy.ExistingCopy(Path.Combine(_folder.Path, "nowhere", "x.jpg")));

    [Theory]
    [InlineData(@"C:\p\a (colorized).jpg", true)]
    [InlineData(@"C:\p\a (colorized) 2.jpg", true)]
    [InlineData(@"C:\p\a (colorized) 12.jpg", true)]
    [InlineData(@"C:\p\A (COLORIZED).JPG", true)]
    [InlineData(@"C:\p\a.jpg", false)]
    [InlineData(@"C:\p\a 2.jpg", false)]
    [InlineData(@"C:\p\colorized.jpg", false)]
    [InlineData(@"C:\p\a (colorized) x.jpg", false)]
    [InlineData(@"C:\p\a (colorized) copy.jpg", false)]
    public void ACopyIsRecognised_ByItsName(string path, bool isCopy) =>
        Assert.Equal(isCopy, ColorizedCopy.IsCopy(path));

    // ── writing ─────────────────────────────────────────────────────────────

    private static DecodedPhoto Picture(int width, int height, byte blue, byte green, byte red)
    {
        var bgr = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            bgr[i * 3] = blue;
            bgr[i * 3 + 1] = green;
            bgr[i * 3 + 2] = red;
        }
        return new DecodedPhoto(width, height, bgr);
    }

    /// <summary>A JPEG with Exif: a camera, a date taken and an orientation that says it has to be turned a quarter.</summary>
    private static byte[] JpegWithExif(int width, int height, ushort orientation)
    {
        var pixels = new byte[width * height * 3];
        Array.Fill(pixels, (byte)128);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.CameraModel = "Test Camera 3000";
        metadata.DateTaken = "1987-06-05T04:03:02";
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapMetadata MetadataOf(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return (BitmapMetadata)decoder.Frames[0].Metadata;
    }

    private static (int Blue, int Green, int Red) PixelOf(string path, int x, int y)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr24, null, 0);
        var pixel = new byte[3];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 3, 0);
        return (pixel[0], pixel[1], pixel[2]);
    }

    [Fact]
    public void TheCopy_HasTheColours_TheExifOfThePhoto_AndSaysItIsUpright()
    {
        var photo = _folder.File("granny.jpg");
        var bytes = JpegWithExif(30, 40, orientation: 6);
        File.WriteAllBytes(photo, bytes);

        var copy = ColorizedCopy.Save(photo, bytes, Picture(40, 30, 40, 90, 200)); // upright: 40 wide, 30 high (the photo is stored turned)

        Assert.Equal(_folder.File("granny (colorized).jpg"), copy);
        var (blue, green, red) = PixelOf(copy, 20, 15);
        Assert.InRange(red, 190, 210);
        Assert.InRange(green, 80, 100);
        Assert.InRange(blue, 30, 50);

        var metadata = MetadataOf(copy);
        Assert.Equal("Test Camera 3000", metadata.CameraModel);
        using (var stream = File.OpenRead(copy))
            Assert.Equal(new DateTime(1987, 6, 5, 4, 3, 2), ExifReader.Read(stream).Original); // the date taken the way the program reads it
        Assert.Equal((ushort)1, Convert.ToUInt16(metadata.GetQuery("/app1/ifd/{ushort=274}"))); // the pixels are upright: no turn to apply
    }

    [Fact]
    public void TheCopy_IsTheSizeOfTheColorizedPhoto()
    {
        var photo = _folder.File("p.jpg");
        var bytes = JpegWithExif(10, 10, 1);
        File.WriteAllBytes(photo, bytes);

        var copy = ColorizedCopy.Save(photo, bytes, Picture(123, 77, 100, 100, 100));

        using var stream = File.OpenRead(copy);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(123, frame.PixelWidth);
        Assert.Equal(77, frame.PixelHeight);
    }

    [Fact]
    public void TheCopy_HasTheDatesOfThePhotoFile()
    {
        var photo = _folder.File("old.jpg");
        var bytes = JpegWithExif(10, 10, 1);
        File.WriteAllBytes(photo, bytes);
        var created = new DateTime(1999, 12, 31, 23, 59, 58, DateTimeKind.Utc);
        var written = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetCreationTimeUtc(photo, created);
        File.SetLastWriteTimeUtc(photo, written);

        var copy = ColorizedCopy.Save(photo, bytes, Picture(10, 10, 1, 2, 3));

        Assert.Equal(created, File.GetCreationTimeUtc(copy));
        Assert.Equal(written, File.GetLastWriteTimeUtc(copy));
    }

    [Fact]
    public void TheCopy_IsNeverWrittenOverAnotherFile_AndThePhotoIsNeverTouched()
    {
        var photo = _folder.File("keep.jpg");
        var bytes = JpegWithExif(10, 10, 1);
        File.WriteAllBytes(photo, bytes);
        File.WriteAllText(_folder.File("keep (colorized).jpg"), "somebody else's file");

        var copy = ColorizedCopy.Save(photo, bytes, Picture(10, 10, 1, 2, 3));

        Assert.Equal(_folder.File("keep (colorized) 2.jpg"), copy);
        Assert.Equal("somebody else's file", File.ReadAllText(_folder.File("keep (colorized).jpg")));
        Assert.Equal(bytes, File.ReadAllBytes(photo));
    }

    [Fact]
    public void ThePhotoThatIsNotAJpeg_GetsACopyAsAJpeg_WithoutExif()
    {
        var photo = _folder.File("scan.png");
        var pixels = new byte[10 * 10 * 3];
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgr24, null, pixels, 30)));
        using (var file = File.Create(photo))
            png.Save(file);
        var bytes = File.ReadAllBytes(photo);

        var copy = ColorizedCopy.Save(photo, bytes, Picture(10, 10, 10, 20, 30));

        Assert.Equal(_folder.File("scan (colorized).jpg"), copy);
        Assert.True(new FileInfo(copy).Length > 100);
    }

    [Fact]
    public void ABrokenPhotoFile_StillGetsItsCopy()
    {
        var photo = _folder.File("broken.jpg");
        var bytes = new byte[] { 1, 2, 3, 4, 5 }; // not a picture: no Exif to take, but the colours are fine
        File.WriteAllBytes(photo, bytes);

        var copy = ColorizedCopy.Save(photo, bytes, Picture(8, 8, 1, 2, 3));

        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void WhenTheCopyCanNotBeWritten_NothingIsLeft()
    {
        var folder = _folder.File("locked");
        Directory.CreateDirectory(folder);
        var photo = Path.Combine(folder, "p.jpg");
        var bytes = JpegWithExif(10, 10, 1);
        File.WriteAllBytes(photo, bytes);
        File.WriteAllBytes(Path.Combine(folder, "p (colorized).jpg.part"), [0]); // the name the copy is written under is taken by a file that is open
        using var holding = new FileStream(Path.Combine(folder, "p (colorized).jpg.part"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.ThrowsAny<IOException>(() => ColorizedCopy.Save(photo, bytes, Picture(10, 10, 1, 2, 3)));

        Assert.False(File.Exists(Path.Combine(folder, "p (colorized).jpg")));
    }
}

/// <summary>Colorize on files: how much colour a photo file has, and a copy made with the real model.</summary>
public class ColorizeJobTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Jpeg(string name, Func<int, int, (byte B, byte G, byte R)> colour)
    {
        var pixels = new byte[200 * 150 * 3];
        for (var y = 0; y < 150; y++)
        for (var x = 0; x < 200; x++)
        {
            var (b, g, r) = colour(x, y);
            var i = (y * 200 + x) * 3;
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
        }
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            System.Windows.Media.Imaging.BitmapSource.Create(200, 150, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, pixels, 600)));
        var path = _folder.File(name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    [Fact]
    public void AGreyPhotoFile_IsBlackAndWhite_APhotoWithColoursIsNot()
    {
        var grey = Jpeg("grey.jpg", (x, y) => ((byte)(40 + x), (byte)(40 + x), (byte)(40 + x)));
        var brown = Jpeg("brown.jpg", (x, y) => ((byte)((40 + x) * 0.66), (byte)((40 + x) * 0.84), (byte)(40 + x))); // 40 to 239: no channel wraps round
        var colours = Jpeg("colours.jpg", (x, y) => ((byte)(x % 7 * 36), (byte)(y % 5 * 50), (byte)((x + y) % 9 * 28)));

        Assert.True(ColorizeJob.Measure(grey).IsBlackAndWhite);
        Assert.True(ColorizeJob.Measure(brown).IsBlackAndWhite);
        Assert.False(ColorizeJob.Measure(colours).IsBlackAndWhite);
    }

    [Fact]
    public void AFileThatIsNoPicture_IsAnErrorToMeasure()
    {
        var path = _folder.File("not.jpg");
        File.WriteAllText(path, "this is not a picture");

        Assert.ThrowsAny<Exception>(() => ColorizeJob.Measure(path));
    }

    [Fact]
    public void AMissingFile_IsAnErrorToMeasure() =>
        Assert.ThrowsAny<IOException>(() => ColorizeJob.Measure(_folder.File("gone.jpg")));

    private static bool ModelPresent => ToolModels.Colorizer.IsPresentIn(RealModels.Folder);

    [Fact]
    public void ACopyIsMade_WithTheModel_NextToThePhoto_AndItHasColours()
    {
        if (!ModelPresent) return;
        var grey = Jpeg("old.jpg", (x, y) => (
            (byte)Math.Clamp(110 + 90 * Math.Sin(x / 11.0) * Math.Cos(y / 13.0), 0, 255),
            (byte)Math.Clamp(110 + 90 * Math.Sin(x / 11.0) * Math.Cos(y / 13.0), 0, 255),
            (byte)Math.Clamp(110 + 90 * Math.Sin(x / 11.0) * Math.Cos(y / 13.0), 0, 255)));
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(2);

        var copy = ColorizeJob.Colorize(colorizer, grey);

        Assert.Equal(_folder.File("old (colorized).jpg"), copy);
        Assert.True(File.Exists(grey));
        var result = WicPhotoDecoder.Decode(File.ReadAllBytes(copy), 400);
        Assert.Equal(200, result.Width);
        Assert.Equal(150, result.Height);
        Assert.False(BlackAndWhite.Measure(result).IsBlackAndWhite);

        // a second time: another copy, the first is kept
        var second = ColorizeJob.Colorize(colorizer, grey);
        Assert.Equal(_folder.File("old (colorized) 2.jpg"), second);
        Assert.True(File.Exists(copy));
    }
}
