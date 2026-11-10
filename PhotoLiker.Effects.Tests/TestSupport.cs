using System.Runtime.CompilerServices;
using PhotoLiker.Core;

namespace PhotoLiker.Effects.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// The program keeps its files in the app data folder. While testing that is a temp folder, so the tests never touch those of the person running them. Runs when the test assembly loads,
    /// before anything reads <see cref="AppPaths.DataFolder"/>.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepTheRealAppDataUntouched() =>
        Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, Path.Combine(Path.GetTempPath(), "PhotoLikerEffectsTests_AppData"));
}

/// <summary>The models the program downloaded (the tests that run a real model do so only where its file is), or the folder named by PHOTOLIKER_TEST_MODELS.</summary>
internal static class RealModels
{
    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");
}

/// <summary>A folder that is deleted again when the test is over.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PLEffectsTests_" + Guid.NewGuid());

    public TempFolder() => Directory.CreateDirectory(Path);

    public string File(string relativePath)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
