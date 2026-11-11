using System.Diagnostics;
using System.Runtime.CompilerServices;
using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

internal static class TestEnvironment
{
    /// <summary>The windows save and read what the program keeps on the machine (the index of a folder, the config); a test must not touch the real ones.</summary>
    [ModuleInitializer]
    internal static void KeepTheRealAppDataUntouched() =>
        Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, Path.Combine(Path.GetTempPath(), "PhotoLikerUiTests_AppData"));

    internal static string RepositoryRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PhotoLiker.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("The source tree (PhotoLiker.sln) was not found above the tests.");
    }
}

/// <summary>The ffmpeg that comes with the program, in the repository: it makes the videos for the tests and is what the video library reads them with.</summary>
internal static class TestFfmpeg
{
    internal static string? Path { get; } = Find();

    private static string? Find()
    {
        var path = System.IO.Path.Combine(TestEnvironment.RepositoryRoot, "PhotoLikerUI", "ffmpeg", "ffmpeg.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Makes the video library use this ffmpeg until the result is disposed.</summary>
    internal static IDisposable Use() => new Scope(Path);

    private sealed class Scope : IDisposable
    {
        private readonly string? _before = Ffmpeg.ExecutableOverride;

        public Scope(string? ffmpeg) => Ffmpeg.ExecutableOverride = ffmpeg;

        public void Dispose() => Ffmpeg.ExecutableOverride = _before;
    }

    /// <summary>A one second MP4 (or one of <paramref name="seconds"/>) that says it was recorded at this time (UTC text, "2003-01-31T15:06:00Z"), made by ffmpeg; its file dates are as they come.</summary>
    internal static string MakeMp4(string folder, string name, string recordedUtc, int seconds = 1, bool sound = false)
    {
        var file = System.IO.Path.Combine(folder, name);
        var start = new ProcessStartInfo(Path!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=" + seconds,
        };
        if (sound)
            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:duration=" + seconds, "-c:a", "aac"]); // a sound track: the video can be heard
        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-metadata", "creation_time=" + recordedUtc, file]);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var messages = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && File.Exists(file), "ffmpeg made the test video: " + messages);
        return file;
    }
}
