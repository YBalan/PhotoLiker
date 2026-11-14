namespace PhotoLiker.Maui.SmokeTour;

/// <summary>
/// A folder of generated gradient photos for the tour, like the WinForms tour's DemoFolder but without System.Drawing:
/// plain 24-bit BMPs written by hand, so they decode on every platform. The dates of the files are July (two photos),
/// August (one) and September (two) 2025: the months PageUp / PageDown move between.
/// </summary>
public static class SmokeDemoFolder
{
    public static readonly string[] FileNames =
        ["01-beach.bmp", "02-harbour.bmp", "03-sunset.bmp", "04-forest.bmp", "05-mountains.bmp"];

    public static readonly DateTime[] FileDates =
    [
        new(2025, 7, 10, 9, 0, 0), new(2025, 7, 12, 9, 0, 0), new(2025, 8, 14, 9, 12, 7), new(2025, 9, 2, 9, 0, 0), new(2025, 9, 3, 9, 0, 0),
    ];

    /// <summary>The top and the bottom colour of each photo's gradient (R, G, B), so the five photos are told apart on screen.</summary>
    private static readonly ((byte R, byte G, byte B) Top, (byte R, byte G, byte B) Bottom)[] Colours =
    [
        ((70, 140, 220), (250, 220, 170)),
        ((40, 90, 160), (240, 170, 120)),
        ((90, 40, 110), (255, 140, 80)),
        ((120, 190, 220), (220, 240, 200)),
        ((60, 100, 170), (200, 215, 240)),
    ];

    public static int Count => FileNames.Length;

    /// <summary>Recreates <paramref name="folder"/> with the five demo photos, their file dates set to <see cref="FileDates"/>.</summary>
    public static void Create(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        for (var i = 0; i < FileNames.Length; i++)
        {
            var path = Path.Combine(folder, FileNames[i]);
            WriteBmp(path, 320, 200, Colours[i].Top, Colours[i].Bottom);
            File.SetCreationTime(path, FileDates[i]);
            File.SetLastWriteTime(path, FileDates[i]);
        }
    }

    /// <summary>A 24-bit uncompressed BMP of a vertical gradient: 54 bytes of headers, then the rows bottom-up, each padded to four bytes.</summary>
    public static void WriteBmp(string path, int width, int height, (byte R, byte G, byte B) top, (byte R, byte G, byte B) bottom)
    {
        var stride = (width * 3 + 3) & ~3;
        var pixelBytes = stride * height;

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + pixelBytes); // file size
        writer.Write(0);               // reserved
        writer.Write(54);              // offset of the pixels
        writer.Write(40);              // BITMAPINFOHEADER
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);        // planes
        writer.Write((short)24);       // bits per pixel
        writer.Write(0);               // BI_RGB
        writer.Write(pixelBytes);
        writer.Write(2835);            // 72 dpi
        writer.Write(2835);
        writer.Write(0);               // colours used
        writer.Write(0);               // colours important

        var row = new byte[stride];
        for (var y = height - 1; y >= 0; y--) // rows are stored bottom-up
        {
            var t = height <= 1 ? 0f : (float)y / (height - 1);
            var r = (byte)(top.R + (bottom.R - top.R) * t);
            var g = (byte)(top.G + (bottom.G - top.G) * t);
            var b = (byte)(top.B + (bottom.B - top.B) * t);
            for (var x = 0; x < width; x++)
            {
                row[x * 3] = b;
                row[x * 3 + 1] = g;
                row[x * 3 + 2] = r;
            }

            writer.Write(row);
        }
    }
}
