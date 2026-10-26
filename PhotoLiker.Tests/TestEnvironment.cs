using System.Runtime.CompilerServices;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// PhotoBrowserService saves the global config (the last folder opened) as the app does. Without this the tests
    /// would overwrite the one in the real %LocalAppData%\PhotoLiker with a temp folder. Runs when the test assembly
    /// loads, before anything reads <see cref="AppPaths.DataFolder"/>.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepTheRealAppDataUntouched() =>
        Environment.SetEnvironmentVariable(
            AppPaths.DataFolderVariable,
            Path.Combine(Path.GetTempPath(), "PhotoLikerTests_AppData"));
}

public class AppPathsTests
{
    [Fact]
    public void WhileTesting_TheAppDataFolderIsNotTheRealOne()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker");

        Assert.NotEqual(real, AppPaths.DataFolder, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), AppPaths.DataFolder, StringComparison.OrdinalIgnoreCase);
    }
}
