using System.Diagnostics;

namespace PhotoLiker.Stickers.Tests;

/// <summary>A folder in the temporary folder that is removed when the test is over.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PhotoLikerStickerTests_" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Something still holds a file; the temporary folder is cleaned up some day
        }
    }
}

/// <summary>Small pictures and masks made on purpose, so that a test knows exactly what is in them.</summary>
internal static class Make
{
    /// <summary>A photo whose every point is given by <paramref name="colour"/> (blue, green, red).</summary>
    public static PhotoPixels Photo(int width, int height, Func<int, int, (byte B, byte G, byte R)> colour)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = colour(x, y);
                var i = (y * width + x) * 3;
                bgr[i] = b;
                bgr[i + 1] = g;
                bgr[i + 2] = r;
            }
        }
        return new PhotoPixels(width, height, bgr);
    }

    public static PhotoPixels Plain(int width, int height, byte b = 10, byte g = 120, byte r = 230) => Photo(width, height, (_, _) => (b, g, r));

    /// <summary>A mask that is 1 inside the rectangle (left, top, width, height) and 0 elsewhere.</summary>
    public static float[] Rectangle(int width, int height, int left, int top, int rectangleWidth, int rectangleHeight)
    {
        var mask = new float[width * height];
        for (var y = top; y < top + rectangleHeight; y++)
        {
            for (var x = left; x < left + rectangleWidth; x++)
                mask[y * width + x] = 1;
        }
        return mask;
    }

    /// <summary>A mask that is 1 inside the circle and 0 elsewhere.</summary>
    public static float[] Disc(int width, int height, double centreX, double centreY, double radius)
    {
        var mask = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                mask[y * width + x] = (x + 0.5 - centreX) * (x + 0.5 - centreX) + (y + 0.5 - centreY) * (y + 0.5 - centreY) <= radius * radius ? 1 : 0;
        }
        return mask;
    }

    public static int Count(float[] mask, float atLeast = 0.5f) => mask.Count(value => value >= atLeast);

    /// <summary>The settings with the bells off: a bare cutout at 512 pixels, for tests of one thing at a time.</summary>
    public static StickerSettings Bare() => new() { Outline = false, Shadow = false, EdgeShift = 0 };

    /// <summary>The colour (B, G, R, A) at a point of a sticker.</summary>
    public static (byte B, byte G, byte R, byte A) At(StickerPicture picture, int x, int y)
    {
        var i = (y * picture.Width + x) * 4;
        return (picture.Bgra[i], picture.Bgra[i + 1], picture.Bgra[i + 2], picture.Bgra[i + 3]);
    }

    /// <summary>The model's map for a photo, drawn by hand: <paramref name="mask"/> at the size of the photo, as a map of its own size.</summary>
    public static SubjectMap Map(int width, int height, float[] values) => new(width, height, values);
}

/// <summary>The ffmpeg that comes with the program (PhotoLikerUI\ffmpeg\ffmpeg.exe, in the repository). The tests that need it do nothing where it is not there.</summary>
internal static class RealFfmpeg
{
    internal static string? Path { get; } = Find();

    private static string? Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "PhotoLiker.sln")))
            directory = directory.Parent;

        var path = directory is null ? null : System.IO.Path.Combine(directory.FullName, "PhotoLikerUI", "ffmpeg", "ffmpeg.exe");
        return path is not null && System.IO.File.Exists(path) ? path : null;
    }

    /// <summary>Decodes any picture ffmpeg reads to raw RGBA (width x height x 4 bytes) and returns it; null where there is no ffmpeg.</summary>
    internal static byte[]? DecodeToRgba(string file, int width, int height)
    {
        if (Path is null)
            return null;

        var start = new ProcessStartInfo(Path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-i", file, "-f", "rawvideo", "-pix_fmt", "rgba", "-" })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("ffmpeg could not read the picture: " + errors.Result);
        if (output.Length != width * height * 4)
            throw new InvalidOperationException($"ffmpeg gave {output.Length} bytes for a picture of {width} x {height}.");
        return output.ToArray();
    }
}
