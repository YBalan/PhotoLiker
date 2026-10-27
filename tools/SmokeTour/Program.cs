using PhotoLiker.Core;
using PhotoLikerUI;

namespace SmokeTour;

/// <summary>
/// Smoke tour of the PhotoLiker window in dark mode: opens a generated demo folder, drives the real window with key
/// presses and grid edits, checks the results and optionally photographs each step.
///
///   dotnet run --project tools/SmokeTour                                  just the checks
///   dotnet run --project tools/SmokeTour -- --screenshots docs/images     checks and screenshots for the README
///   --demo-folder &lt;path&gt;   where the demo photos go (recreated every run; default: PhotoLikerDemo\Holiday on the drive of the repo)
///   --keep                 leave the demo folder behind
///
/// The window takes the screen for about a minute (it is photographed as it is on screen, and one step clicks and types
/// for real): leave the mouse and the keyboard alone.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var screenshots = ValueOf(args, "--screenshots");
        var keep = args.Contains("--keep");
        var demoFolder = Path.GetFullPath(ValueOf(args, "--demo-folder") ?? DefaultDemoFolder());

        // Keep the real %LocalAppData%\PhotoLiker (last folder, crash log) out of this. Has to happen before anything reads AppPaths.
        var appData = Path.Combine(Path.GetTempPath(), "PhotoLikerSmokeTour_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, appData);

        var checks = new Checks();

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => checks.Fail("unhandled exception in the window: " + e.Exception);

        // The app does this in its own Program.Main (the class is internal to the app)
        typeof(MainForm).Assembly.GetType("PhotoLikerUI.SettingsPresentation")!.GetMethod("Register")!.Invoke(null, null);

        var area = Screen.PrimaryScreen!.WorkingArea;
        var window = new Rectangle(area.Left + 30, area.Top + 20, Math.Min(1500, area.Width - 60), Math.Min(900, area.Height - 40));
        try
        {
            DemoFolder.Create(demoFolder, window);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine(ex.Message);
            TryDelete(appData);
            return 2;
        }

        Console.WriteLine($"Demo folder: {demoFolder}");
        Console.WriteLine(screenshots is null ? "No screenshots (pass --screenshots <folder> to take them)" : $"Screenshots go to: {Path.GetFullPath(screenshots)}");

        var form = new DrivenForm(demoFolder);
        var tour = new Tour(form, demoFolder, screenshots, checks);
        form.Shown += async (_, _) =>
        {
            try
            {
                await tour.Run();
            }
            catch (Exception ex)
            {
                checks.Fail("the tour stopped: " + ex);
            }
            finally
            {
                form.Close();
            }
        };

        Application.Run(form);

        try
        {
            tour.VerifySavedSettings();
        }
        catch (Exception ex)
        {
            checks.Fail("reading the saved settings: " + ex.Message);
        }

        checks.Step("11. Nothing leaked");
        checks.That(!File.Exists(CrashLog.FilePath), "no crash log was written");
        checks.That(File.Exists(Path.Combine(appData, "global-config.json")), "the last folder went to the redirected app-data folder, not the real one");

        TryDelete(appData);
        if (!keep)
            TryDelete(demoFolder);

        return checks.Summary();
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string DefaultDemoFolder() =>
        Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, "PhotoLikerDemo", "Holiday");

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* temp files */ }
    }
}
