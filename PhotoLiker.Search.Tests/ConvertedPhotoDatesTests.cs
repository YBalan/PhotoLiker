using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Imaging;

namespace PhotoLiker.Search.Tests;

/// <summary>
/// The picture layer converts a photo to another format and takes its Exif dates along; this program reads the date taken of a JPEG with <see cref="ExifReader"/>, so a TIFF converted to a JPEG has to have
/// a date that this reader finds (the two layers meet here).
/// </summary>
public class ConvertedPhotoDatesTests
{
    private static byte[] Tiff(string taken)
    {
        var metadata = new BitmapMetadata("tiff");
        metadata.SetQuery("/ifd/exif/{ushort=36867}", taken);
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgr24, null, new byte[8 * 8 * 3], 8 * 3);
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Theory]
    [InlineData("2012:06:01 09:30:15", 2012, 6, 1, 9, 30, 15)]
    [InlineData("1925:06:01 09:00:00", 1925, 6, 1, 9, 0, 0)] // a date that a two-digit year would turn into 2025
    public void ATiffConvertedToAJpeg_HasTheDateTakenThatThisProgramReads(string taken, int year, int month, int day, int hour, int minute, int second)
    {
        var jpeg = PhotoConverter.Encode(Tiff(taken), PhotoFormat.Jpeg);

        Assert.Equal(new DateTime(year, month, day, hour, minute, second), ExifReader.Read(new MemoryStream(jpeg)).Original);
    }
}
