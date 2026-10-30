using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoLiker.Core;

namespace PhotoLiker.Search.Tests;

/// <summary>Photos turned by <see cref="JpegOrientation"/>, looked at by the Windows imaging component, which is what shows them upright.</summary>
public class TurnedPhotoTests : IDisposable
{
    private const int Width = 48;
    private const int Height = 16;

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>The left half red, the right half blue, with the metadata the test asks for.</summary>
    private string Photo(Action<BitmapMetadata>? metadata)
    {
        var pixels = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var at = (y * Width + x) * 3;
                var red = x < Width / 2;
                pixels[at] = (byte)(red ? 20 : 230);
                pixels[at + 1] = 20;
                pixels[at + 2] = (byte)(red ? 230 : 20);
            }

        BitmapMetadata? meta = null;
        if (metadata is not null)
        {
            meta = new BitmapMetadata("jpg");
            metadata(meta);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr24, null, pixels, Width * 3), null, meta, null));
        var path = _folder.File("photo.jpg");
        using (var stream = File.Create(path))
            encoder.Save(stream);
        return path;
    }

    private static DecodedPhoto Decode(string path) => WicPhotoDecoder.Decode(File.ReadAllBytes(path));

    /// <summary>The colour of a pixel as "red" or "blue", the way the photo is shown.</summary>
    private static string Colour(DecodedPhoto photo, int x, int y)
    {
        var at = (y * photo.Width + x) * 3;
        return photo.Bgr[at + 2] > photo.Bgr[at] ? "red" : "blue";
    }

    public static TheoryData<string> Kinds => new() { "no Exif", "Exif without the orientation", "orientation 1", "orientation 6" };

    private string PhotoOfKind(string kind) => kind switch
    {
        "no Exif" => Photo(null),
        "Exif without the orientation" => Photo(m => m.SetQuery("/app1/ifd/{ushort=306}", "2015:04:18 10:11:12")),
        "orientation 1" => Photo(m => m.SetQuery("/app1/ifd/{ushort=274}", (ushort)1)),
        _ => Photo(m => m.SetQuery("/app1/ifd/{ushort=274}", (ushort)6)),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ATurnedPhoto_IsShownTurnedAQuarterClockwise(string kind)
    {
        var path = PhotoOfKind(kind);
        var before = Decode(path);

        JpegOrientation.TurnClockwise(path);
        var after = Decode(path);

        // The picture is as wide as it was high, and the left half has gone to the top
        Assert.Equal(before.Height, after.Width);
        Assert.Equal(before.Width, after.Height);
        var beforeLeft = Colour(before, 2, 2);
        var beforeRight = Colour(before, before.Width - 3, 2);
        Assert.Equal(beforeLeft, Colour(after, after.Width - 3, 2));        // the left half is at the top ...
        Assert.Equal(beforeRight, Colour(after, after.Width - 3, after.Height - 3)); // ... the right half at the bottom
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void FourTurns_ShowThePhotoAsItWas(string kind)
    {
        var path = PhotoOfKind(kind);
        var before = Decode(path);

        for (var i = 0; i < 4; i++)
            JpegOrientation.TurnClockwise(path);
        var after = Decode(path);

        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        Assert.Equal(Colour(before, 2, 2), Colour(after, 2, 2));
        Assert.Equal(Colour(before, before.Width - 3, 2), Colour(after, after.Width - 3, 2));
    }

    [Fact]
    public void ThePictureData_IsNotTouched()
    {
        var path = Photo(null);
        var before = File.ReadAllBytes(path);

        JpegOrientation.TurnClockwise(path);
        var after = File.ReadAllBytes(path);

        // Everything of the old file is in the new one, in the same order: only the 36 bytes of the Exif block came in
        Assert.Equal(before.Length + 36, after.Length);
        var block = after.AsSpan().IndexOf(new byte[] { 0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f' });
        Assert.True(block >= 0);
        Assert.Equal(before, after.Take(block).Concat(after.Skip(block + 36)).ToArray());
    }
}
