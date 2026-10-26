using System.Runtime.CompilerServices;
using PhotoLiker.Core;
using PhotoLiker.Search;

namespace PhotoLiker.Search.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// The index files live in the app data folder. While testing that is a temp folder, so the tests never touch the
    /// indexes of the person running them. Runs when the test assembly loads, before anything reads <see cref="AppPaths.DataFolder"/>.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepTheRealAppDataUntouched() =>
        Environment.SetEnvironmentVariable(
            AppPaths.DataFolderVariable,
            Path.Combine(Path.GetTempPath(), "PhotoLikerSearchTests_AppData"));
}

/// <summary>A folder that is deleted again when the test is over.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PLSearchTests_" + Guid.NewGuid());

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

internal static class Vectors
{
    /// <summary>A unit vector of the size of a CLIP / face vector that starts with the given numbers.</summary>
    public static float[] Unit(params float[] leading)
    {
        var vector = new float[ClipImageEncoder.Dimensions];
        leading.CopyTo(vector, 0);
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        for (var i = 0; i < vector.Length; i++)
            vector[i] /= norm;
        return vector;
    }
}
