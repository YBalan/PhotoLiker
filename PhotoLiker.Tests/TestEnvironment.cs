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

    /// <summary>
    /// The language files of the program are beside the app, which the tests are not: look in the repository, where they are kept
    /// (<see cref="AppLanguage.FolderVariable"/>). The path is also what <see cref="Languages"/> hands out.
    /// </summary>
    [ModuleInitializer]
    internal static void FindTheLanguageFiles() =>
        Environment.SetEnvironmentVariable(AppLanguage.FolderVariable, Languages);

    internal static string RepositoryRoot { get; } = FindRoot();

    internal static string Languages => Path.Combine(RepositoryRoot, "languages");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PhotoLiker.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("The source tree (PhotoLiker.sln) was not found above the tests.");
    }
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
