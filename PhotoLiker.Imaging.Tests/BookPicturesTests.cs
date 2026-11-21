using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Imaging.Tests;

/// <summary>The pictures of a book: upright JPEGs at the size the book needs.</summary>
public sealed class BookPicturesTests
{
    /// <summary>A picture whose left half is red and whose right half is blue, in the format of <paramref name="png"/>, with an EXIF orientation when asked.</summary>
    private static byte[] Picture(int width, int height, bool png = false, ushort? orientation = null, byte alpha = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var left = x < width / 2;
            pixels[i] = (byte)(left ? 0 : 255);       // blue
            pixels[i + 2] = (byte)(left ? 255 : 0);   // red
            pixels[i + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);

        BitmapMetadata? metadata = null;
        if (orientation is { } value && !png)
        {
            metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", value);
        }

        BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 100 };
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

    [Fact]
    public void TheResult_IsAJpeg_OfTheSizeItSays()
    {
        var picture = BookPictures.ToJpeg(Picture(400, 300), 1000, 85);

        Assert.True(picture.Bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }));
        var frame = Open(picture.Bytes);
        Assert.Equal((400, 300), (picture.Width, picture.Height));
        Assert.Equal((picture.Width, picture.Height), (frame.PixelWidth, frame.PixelHeight));
    }

    [Theory]
    [InlineData(1600, 1200, 800, 800, 600)]
    [InlineData(1200, 1600, 800, 600, 800)]
    [InlineData(300, 200, 800, 300, 200)] // never enlarged
    public void TheLongSide_IsAtMostTheSizeAskedFor_AndTheProportionsStay(int width, int height, int max, int expectedWidth, int expectedHeight)
    {
        var picture = BookPictures.ToJpeg(Picture(width, height), max, 85);

        Assert.Equal((expectedWidth, expectedHeight), (picture.Width, picture.Height));
    }

    [Theory]
    [InlineData((ushort)6, true)]  // turned a quarter: the stored 40 x 20 is 20 x 40 upright
    [InlineData((ushort)8, true)]
    [InlineData((ushort)3, false)] // upside down: the same size
    [InlineData((ushort)1, false)]
    public void ThePhoto_IsTurnedByItsOrientation_SoTheNewFileNeedsNone(ushort orientation, bool sidewaysTurn)
    {
        var picture = BookPictures.ToJpeg(Picture(40, 20, orientation: orientation), 1000, 90);

        Assert.Equal(sidewaysTurn ? (20, 40) : (40, 20), (picture.Width, picture.Height));
        // and it says nothing of it
        var frame = Open(picture.Bytes);
        var orientationInFile = frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/app1/ifd/{ushort=274}") ? Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}")) : 1;
        Assert.Equal(1, orientationInFile);
    }

    [Fact]
    public void TurnedByOrientation6_TheLeftHalfIsAtTheTop()
    {
        // Orientation 6 is "turn 90 degrees clockwise": the left side of the stored picture (red) becomes the top
        var picture = BookPictures.ToJpeg(Picture(40, 20, orientation: 6), 1000, 100);

        var top = Pixel(picture.Bytes, 10, 4);
        var bottom = Pixel(picture.Bytes, 10, 35);
        Assert.True(top.Red > 200 && top.Blue < 60, $"top is red: {top}");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"bottom is blue: {bottom}");
    }

    [Fact]
    public void ATransparentPicture_IsOnWhite()
    {
        var picture = BookPictures.ToJpeg(Picture(20, 20, png: true, alpha: 0), 1000, 95);

        var (blue, green, red) = Pixel(picture.Bytes, 10, 10);
        Assert.True(blue > 240 && green > 240 && red > 240, $"white, not black: {(blue, green, red)}");
    }

    [Fact]
    public void APng_BecomesAJpeg()
    {
        var picture = BookPictures.ToJpeg(Picture(30, 30, png: true), 1000, 90);

        Assert.True(picture.Bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8 }));
    }

    [Fact]
    public void ALowerQuality_IsASmallerFile()
    {
        var original = Picture(600, 400);

        var small = BookPictures.ToJpeg(original, 1000, 30);
        var large = BookPictures.ToJpeg(original, 1000, 95);

        Assert.True(small.Bytes.Length < large.Bytes.Length);
    }

    [Fact]
    public void AQualityOutOfRange_IsBroughtIn()
    {
        _ = BookPictures.ToJpeg(Picture(20, 20), 1000, 0);
        _ = BookPictures.ToJpeg(Picture(20, 20), 1000, 500);
    }

    [Fact]
    public void WhatIsNotAPicture_IsRefused()
    {
        Assert.Throws<NotSupportedException>(() => BookPictures.ToJpeg([1, 2, 3, 4, 5, 6, 7, 8], 1000, 85));
        Assert.Throws<NotSupportedException>(() => BookPictures.ToJpeg([], 1000, 85));
    }

    [Fact]
    public void AFile_IsReadOnce_WholeAndLeftAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), "photoliker-bookpic-" + Guid.NewGuid().ToString("N") + ".jpg");
        var original = Picture(64, 48);
        File.WriteAllBytes(path, original);
        try
        {
            var picture = BookPictures.ToJpeg(path, 1000, 85);

            Assert.Equal((64, 48), (picture.Width, picture.Height));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── The turn the book gives a photo ─────────────────────────────────────

    [Theory]
    [InlineData(0, 40, 20)]
    [InlineData(90, 20, 40)]
    [InlineData(180, 40, 20)]
    [InlineData(270, 20, 40)]
    [InlineData(-90, 20, 40)]  // counterclockwise
    [InlineData(360, 40, 20)]
    [InlineData(45, 40, 20)]   // not a quarter: no turn
    public void TheBookTurn_PutsThePhotoOnItsSide_ForAQuarter(int rotation, int width, int height)
    {
        var picture = BookPictures.ToJpeg(Picture(40, 20), 1000, 95, rotation);

        Assert.Equal((width, height), (picture.Width, picture.Height));
        var frame = Open(picture.Bytes);
        Assert.Equal((width, height), (frame.PixelWidth, frame.PixelHeight));
    }

    [Fact]
    public void AQuarterClockwise_PutsTheLeftHalfAtTheTop_AndAQuarterBackPutsItAtTheBottom()
    {
        var clockwise = BookPictures.ToJpeg(Picture(40, 20), 1000, 100, 90);
        var back = BookPictures.ToJpeg(Picture(40, 20), 1000, 100, 270);

        Assert.True(Pixel(clockwise.Bytes, 10, 4).Red > 200, "red (the left half) is at the top after a turn clockwise");
        Assert.True(Pixel(clockwise.Bytes, 10, 35).Blue > 200, "blue is at the bottom");
        Assert.True(Pixel(back.Bytes, 10, 4).Blue > 200, "blue (the right half) is at the top after a turn back");
        Assert.True(Pixel(back.Bytes, 10, 35).Red > 200, "red is at the bottom");
    }

    [Fact]
    public void Half_ATurn_PutsTheLeftHalfOnTheRight()
    {
        var picture = BookPictures.ToJpeg(Picture(40, 20), 1000, 100, 180);

        Assert.True(Pixel(picture.Bytes, 35, 10).Red > 200, "red is on the right");
        Assert.True(Pixel(picture.Bytes, 4, 10).Blue > 200, "blue is on the left");
    }

    [Fact]
    public void TheBookTurn_IsOnTopOfTheOrientationOfThePhoto()
    {
        // Orientation 6 makes the stored 40 x 20 upright as 20 x 40 (red on top); a turn of the book, a quarter clockwise, then puts that red on the right
        var picture = BookPictures.ToJpeg(Picture(40, 20, orientation: 6), 1000, 100, 90);

        Assert.Equal((40, 20), (picture.Width, picture.Height));
        Assert.True(Pixel(picture.Bytes, 35, 10).Red > 200, "red on the right");
        Assert.True(Pixel(picture.Bytes, 4, 10).Blue > 200, "blue on the left");
    }

    [Fact]
    public void ATurnedPhoto_IsStillNoBiggerThanTheSizeAskedFor()
    {
        var picture = BookPictures.ToJpeg(Picture(1600, 800), 1000, 85, 90);

        Assert.Equal((500, 1000), (picture.Width, picture.Height));
    }
}
