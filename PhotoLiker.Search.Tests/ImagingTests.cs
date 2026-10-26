using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoLiker.Search.Tests;

public class ResamplerTests
{
    [Fact]
    public void AConstantPicture_StaysConstant_WhenShrunk()
    {
        var source = Enumerable.Repeat((byte)200, 40 * 30 * 3).ToArray();

        var result = Resampler.ResizeBgr(source, 40, 30, 8, 6);

        Assert.Equal(8 * 6 * 3, result.Length);
        Assert.All(result, b => Assert.Equal(200, b));
    }

    [Fact]
    public void ShrinkingByTwo_BlendsNeighbouringPixels()
    {
        // 4 x 1 picture: black, white, black, white (all three channels)
        byte[] source = [0, 0, 0, 255, 255, 255, 0, 0, 0, 255, 255, 255];

        var result = Resampler.ResizeBgr(source, 4, 1, 2, 1);

        Assert.Equal(2 * 3, result.Length);
        // Each output pixel is a mix of black and white (the filter reaches a little into the next pixels, so not exactly half) ...
        Assert.All(result, b => Assert.InRange(b, 100, 155));
        // ... and the two outputs are mirror images of each other
        Assert.InRange(result[0] + result[3], 253, 257);
    }

    [Fact]
    public void EnlargingKeepsTheCorners()
    {
        // 2 x 2: top row dark, bottom row bright
        byte[] source = [10, 10, 10, 10, 10, 10, 240, 240, 240, 240, 240, 240];

        var result = Resampler.ResizeBgr(source, 2, 2, 8, 8);

        Assert.Equal(10, result[0]);
        Assert.Equal(240, result[(7 * 8) * 3]);
    }
}

public class DecodedPhotoTests
{
    [Fact]
    public void Crop_CopiesTheRectangle()
    {
        // 3 x 2 photo whose blue byte is the pixel number
        var pixels = new byte[3 * 2 * 3];
        for (var i = 0; i < 6; i++)
            pixels[i * 3] = (byte)i;
        var photo = new DecodedPhoto(3, 2, pixels);

        var crop = photo.Crop(1, 0, 2, 2);

        Assert.Equal(2, crop.Width);
        Assert.Equal(2, crop.Height);
        Assert.Equal([1, 2, 4, 5], new[] { crop.Bgr[0], crop.Bgr[3], crop.Bgr[6], crop.Bgr[9] });
    }

    [Fact]
    public void Crop_IsClampedToThePhoto()
    {
        var photo = new DecodedPhoto(4, 4, new byte[4 * 4 * 3]);

        var crop = photo.Crop(-5, 3, 100, 100);

        Assert.Equal(4, crop.Width);
        Assert.Equal(1, crop.Height);
    }
}

public class WicPhotoDecoderTests
{
    private static byte[] Jpeg(int width, int height, byte blue, byte green, byte red, ushort? orientation = null)
    {
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 3] = blue;
            pixels[i * 3 + 1] = green;
            pixels[i * 3 + 2] = red;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);

        BitmapMetadata? metadata = null;
        if (orientation is { } value)
        {
            metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", value);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void ALargePhoto_IsShrunkToTheLongestSide_KeepingItsProportions()
    {
        var photo = WicPhotoDecoder.Decode(Jpeg(400, 200, blue: 20, green: 40, red: 220), maxLongSide: 100);

        Assert.Equal(100, photo.Width);
        Assert.Equal(50, photo.Height);
        Assert.Equal(100 * 50 * 3, photo.Bgr.Length);
        Assert.InRange(photo.Bgr[2], 205, 235); // red
        Assert.InRange(photo.Bgr[0], 5, 40);    // blue
    }

    [Fact]
    public void ASmallPhoto_KeepsItsSize()
    {
        var photo = WicPhotoDecoder.Decode(Jpeg(64, 48, 0, 0, 0), maxLongSide: 1280);

        Assert.Equal(64, photo.Width);
        Assert.Equal(48, photo.Height);
    }

    [Fact]
    public void TheExifOrientation_TurnsThePhotoUpright()
    {
        // Stored 60 wide and 20 high, but marked as "rotate 90 degrees to view": upright it is 20 wide and 60 high
        var photo = WicPhotoDecoder.Decode(Jpeg(60, 20, 10, 10, 10, orientation: 6));

        Assert.Equal(20, photo.Width);
        Assert.Equal(60, photo.Height);
    }

    [Fact]
    public void NotAPhoto_Throws()
    {
        Assert.ThrowsAny<Exception>(() => WicPhotoDecoder.Decode([1, 2, 3, 4, 5]));
    }
}

public class ClipImageEncoderTests
{
    [Fact]
    public void Prepare_GivesTheCentreSquare_AsNormalizedPlanarRgb()
    {
        // A white 448 x 224 photo: after scaling to 224 high and cutting the centre every value is the same
        var white = new DecodedPhoto(448, 224, Enumerable.Repeat((byte)255, 448 * 224 * 3).ToArray());

        var input = ClipImageEncoder.Prepare(white);

        Assert.Equal(ClipImageEncoder.InputLength, input.Length);
        var plane = ClipImageEncoder.Size * ClipImageEncoder.Size;
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, input[0], 3);         // red
        Assert.Equal((1f - 0.4578275f) / 0.26130258f, input[plane], 3);      // green
        Assert.Equal((1f - 0.40821073f) / 0.27577711f, input[2 * plane], 3); // blue
    }
}
