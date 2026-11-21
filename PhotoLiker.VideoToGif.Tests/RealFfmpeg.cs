using System.Diagnostics;
using System.Globalization;

namespace PhotoLiker.VideoToGif.Tests;

/// <summary>A folder in the temporary folder that is removed when the test is over.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PhotoLikerGifTests_" + Guid.NewGuid().ToString("N"));

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

/// <summary>The ffmpeg that comes with the program (PhotoLikerUI\ffmpeg\ffmpeg.exe, in the repository) and short test videos made with it. The tests that need it do nothing where it is not there.</summary>
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

    /// <summary>
    /// A video of <paramref name="seconds"/> seconds in which every frame is different (a test pattern with a running counter), <paramref name="width"/> x <paramref name="height"/>,
    /// with a sound track (a GIF has none, and must come out without trouble from a video that has one). Null where there is no ffmpeg.
    /// </summary>
    internal static string? MakeVideo(string folder, string name, double seconds = 4, int rate = 25, int width = 320, int height = 240)
    {
        if (Path is null)
            return null;

        var file = System.IO.Path.Combine(folder, name);
        Run(
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate={rate}",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", seconds.ToString(CultureInfo.InvariantCulture), "-c:v", "mpeg4", "-q:v", "3", "-pix_fmt", "yuv420p", "-c:a", "aac", file);
        return file;
    }

    /// <summary>The same video marked as one that is shown turned by 90 degrees (what a phone writes for a video held upright): stored as it was, shown with width and height traded.</summary>
    internal static string? Turned(string video)
    {
        if (Path is null)
            return null;

        var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(video)!, "turned-" + System.IO.Path.GetFileName(video));
        Run("-y", "-hide_banner", "-loglevel", "error", "-display_rotation", "90", "-i", video, "-c", "copy", file);
        return file;
    }

    private static void Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var messages = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("ffmpeg could not make the test video: " + messages);
    }
}
