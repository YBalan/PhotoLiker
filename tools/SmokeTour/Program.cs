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
///   --only-dialogs         instead of the tour: like four photos, make a video in the Generate Video dialog and open the About box
///                          (the pictures of the user manual and of docs/how-to-generate-video.md, in a minute or so)
///   --only-language        instead of the tour: change the Language row of the settings to Ukrainian and back, open the dialogs in
///                          Ukrainian and check their texts
///   --only-search          instead of the tour: index the demo folder for real and photograph the search strip (the pictures 17-19).
///                          It needs the models the app has downloaded: they are linked, read only, into the tour's own data folder
///                          (a junction, removed again), so the real %LocalAppData%\PhotoLiker is not written to. A minute or two.
///   --only-video           instead of the tour: a demo folder with two clips; the poster, the player, a real index with a search that finds a
///                          place in a video, and the slideshow (the pictures 34-40). Needs the models like --only-search. A few minutes.
///   --only-people          instead of the tour: the People window with 28 invented people in groups (the picture 28), written to the tour's
///                          own data folder. No models needed, a few seconds.
///   --only-layout          instead of the tour: the toolbar at the left edge with the names of its buttons, the panel with the grids docked at the bottom and
///                          floating over the window (the pictures 41-43). No models needed, a few seconds.
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
        var onlyDialogs = args.Contains("--only-dialogs");
        var onlyLanguage = args.Contains("--only-language");
        var onlySearch = args.Contains("--only-search");
        var onlyVideo = args.Contains("--only-video");
        var onlyPeople = args.Contains("--only-people");
        var onlyLayout = args.Contains("--only-layout");
        var demoFolder = Path.GetFullPath(ValueOf(args, "--demo-folder") ?? DefaultDemoFolder());

        // Keep the real %LocalAppData%\PhotoLiker (last folder, crash log) out of this. Has to happen before anything reads AppPaths.
        var appData = Path.Combine(Path.GetTempPath(), "PhotoLikerSmokeTour_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, appData);

        var checks = new Checks();
        var modelsLinked = false;
        if (onlySearch || onlyVideo)
        {
            modelsLinked = LinkModels(appData);
            Console.WriteLine(modelsLinked ? "The models of the app are linked into the tour's data folder." : "The app has no models folder: the search cannot be indexed.");
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => checks.Fail("unhandled exception in the window: " + e.Exception);

        // The app does this in its own Program.Main (the class is internal to the app)
        typeof(MainForm).Assembly.GetType("PhotoLikerUI.SettingsPresentation")!.GetMethod("Register")!.Invoke(null, null);

        var area = Screen.PrimaryScreen!.WorkingArea;
        // The search strip is about a thousand pixels long: the window of that mode takes the width of the screen
        var window = new Rectangle(area.Left + 30, area.Top + 20, onlySearch || onlyVideo ? area.Width - 60 : Math.Min(1500, area.Width - 60), Math.Min(900, area.Height - 40));
        try
        {
            DemoFolder.Create(demoFolder, window, onlyVideo);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine(ex.Message);
            TryDeleteAppData(appData);
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
                await (onlyDialogs ? tour.RunDialogs() : onlyLanguage ? tour.RunLanguage() : onlySearch ? tour.RunSearch() : onlyVideo ? tour.RunVideo() : onlyPeople ? tour.RunPeople() : onlyLayout ? tour.RunLayout() : tour.Run());
            }
            catch (Exception ex)
            {
                checks.Fail("the tour stopped: " + ex);
            }
            finally
            {
                if (!form.IsDisposed) // installing an update from the About box has closed it already
                    form.Close();
            }
        };

        Application.Run(form);

        if (!onlyDialogs && !onlyLanguage && !onlySearch && !onlyVideo && !onlyPeople && !onlyLayout)
        {
            try
            {
                tour.VerifySavedSettings();
            }
            catch (Exception ex)
            {
                checks.Fail("reading the saved settings: " + ex.Message);
            }
        }

        checks.Step("12. Nothing leaked");
        checks.That(!File.Exists(CrashLog.FilePath), "no crash log was written");
        checks.That(File.Exists(Path.Combine(appData, "global-config.json")), "the last folder went to the redirected app-data folder, not the real one");

        TryDeleteAppData(appData);
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

    /// <summary>
    /// Makes %LocalAppData%\PhotoLiker\models of the tour's own data folder a junction to the real one, so that the search finds the models
    /// the app has downloaded (they are hundreds of megabytes: not copied). Nothing is written there while they are whole.
    /// </summary>
    private static bool LinkModels(string appData)
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");
        if (!Directory.Exists(real))
            return false;

        Directory.CreateDirectory(appData);
        var link = Path.Combine(appData, "models");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{real}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return IsLink(link);
    }

    private static bool IsLink(string path) => Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null;

    /// <summary>
    /// Deletes the tour's data folder, after taking the link to the models out of it (deleting a junction without recursion removes the link
    /// and nothing of what it points to). If the link cannot be removed the folder is left, rather than risk the real models.
    /// </summary>
    private static void TryDeleteAppData(string appData)
    {
        var link = Path.Combine(appData, "models");
        try
        {
            if (IsLink(link))
                Directory.Delete(link, recursive: false);
        }
        catch
        {
            // fall through: the check below
        }

        if (IsLink(link))
        {
            Console.WriteLine($"The link to the models could not be removed, so {appData} is left for you to delete (take the link 'models' out of it first).");
            return;
        }

        TryDelete(appData);
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* temp files */ }
    }
}
