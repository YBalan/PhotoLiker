using PhotoLiker.Core;

namespace PhotoLiker.Maui.SmokeTour;

/// <summary>
/// The command line of the smoke tour (see tools\maui-smoke-tour.ps1): the real app started with <c>--smoke-tour</c>
/// opens a generated demo folder, drives its own viewer, checks what happens, writes the log and exits with 0 or 1.
///
///   --smoke-tour             run the tour instead of a normal session
///   --smoke-tour-out &lt;file&gt;  where the log of the checks goes (default: photoliker-maui-smoke-tour.log in %TEMP%)
///   --demo-folder &lt;path&gt;     where the demo photos go (recreated every run; default: inside the tour's own temp folder)
///   --keep                   leave the demo folder behind
///
/// The app data (global config) is redirected into the tour's own temp folder, so a run never touches the real
/// %LocalAppData%\PhotoLiker. The temp folder is deterministic per process: parsing twice gives the same folders.
/// </summary>
public sealed class SmokeTourOptions
{
    public required string DemoFolder { get; init; }
    public required string LogFile { get; init; }
    public required string AppDataFolder { get; init; }
    public required string RootFolder { get; init; }
    public bool KeepDemoFolder { get; init; }

    public static SmokeTourOptions? Parse(string[] args)
    {
        if (!args.Contains("--smoke-tour"))
            return null;

        var root = Path.Combine(Path.GetTempPath(), $"PhotoLikerMauiSmokeTour_{Environment.ProcessId}");
        return new SmokeTourOptions
        {
            RootFolder = root,
            DemoFolder = Path.GetFullPath(ValueOf(args, "--demo-folder") ?? Path.Combine(root, "Holiday")),
            LogFile = Path.GetFullPath(ValueOf(args, "--smoke-tour-out") ?? Path.Combine(Path.GetTempPath(), "photoliker-maui-smoke-tour.log")),
            AppDataFolder = Path.Combine(root, "appdata"),
            KeepDemoFolder = args.Contains("--keep"),
        };
    }

    /// <summary>
    /// Keeps the real %LocalAppData%\PhotoLiker (last folder, crash log) out of a tour. Must run before anything
    /// reads <see cref="AppPaths"/>: it is the first thing MauiProgram.CreateMauiApp does.
    /// </summary>
    public static void RedirectAppData(string[] args)
    {
        if (Parse(args) is { } options)
            Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, options.AppDataFolder);
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
