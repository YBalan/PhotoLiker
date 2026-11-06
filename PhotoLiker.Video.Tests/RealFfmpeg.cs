using System.Diagnostics;

namespace PhotoLiker.Video.Tests;

/// <summary>
/// The ffmpeg that comes with the program (PhotoLikerUI\ffmpeg\ffmpeg.exe, in the repository) and short test videos made with it. Tests that need it
/// do nothing where it is not there. They share one collection because they point the library at this ffmpeg (a setting for the whole process).
/// </summary>
[CollectionDefinition(Name)]
public sealed class FfmpegCollection
{
    public const string Name = "Real ffmpeg";
}

internal static class RealFfmpeg
{
    internal static string? Path { get; } = Find();

    private static string? Find()
    {
        var path = System.IO.Path.Combine(TestEnvironmentRoot, "PhotoLikerUI", "ffmpeg", "ffmpeg.exe");
        return File.Exists(path) ? path : null;
    }

    private static string TestEnvironmentRoot => PhotoLiker.Tests.TestEnvironment.RepositoryRoot;

    /// <summary>Makes the library use this ffmpeg (or a missing one, to show that nothing is read again).</summary>
    internal static IDisposable Use(string? ffmpeg) => new Scope(ffmpeg);

    private sealed class Scope : IDisposable
    {
        private readonly string? _before = Ffmpeg.ExecutableOverride;

        public Scope(string? ffmpeg) => Ffmpeg.ExecutableOverride = ffmpeg;

        public void Dispose() => Ffmpeg.ExecutableOverride = _before;
    }

    /// <summary>
    /// A video of <paramref name="seconds"/> seconds, 320x240, with a sound, in <paramref name="folder"/>. <paramref name="recorded"/> is the date the
    /// container says (UTC text, "2012-03-04T05:06:07Z"), or none. Returns null where there is no ffmpeg.
    /// </summary>
    internal static string? MakeVideo(string folder, string name, int seconds = 3, string? recorded = null)
    {
        if (Path is null)
            return null;

        Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, name);
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-c:v", "mpeg4", "-q:v", "5", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest",
        };
        if (recorded is not null)
            arguments.AddRange(["-metadata", $"creation_time={recorded}"]);
        arguments.Add(file);

        var start = new ProcessStartInfo(Path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var messages = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(file))
            throw new InvalidOperationException("ffmpeg could not make the test video: " + messages);
        return file;
    }
}
