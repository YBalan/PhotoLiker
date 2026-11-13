using PhotoLiker.Core;
using PhotoLiker.Maui.Services;

namespace PhotoLiker.Maui.SmokeTour;

/// <summary>
/// One run through the real MAUI viewer, the way a person would use it, like the WinForms tour in tools\SmokeTour:
/// the window on screen is the real app on a generated demo folder, the keys of the viewer are pressed, and every step
/// checks what happened. Runs on the UI thread (started from the page's Loaded), writes the log and exits the process.
/// </summary>
internal sealed class MauiSmokeTour(MainPage page, SmokeTourOptions options)
{
    private readonly SmokeChecks _checks = new();

    private PhotoBrowserService Service => page.Service;
    private Settings Config => page.Service.Settings;

    public async Task RunAsync()
    {
        try
        {
            await OpenFolder();
            await Browse();
            await LikeAndUnlike();
            await MonthJumps();
            NothingLeaked();
        }
        catch (Exception ex)
        {
            _checks.Fail("the tour stopped: " + ex);
        }
        finally
        {
            Finish();
        }
    }

    // ── 1. the folder opens ─────────────────────────────────────────────────────────────────────

    private async Task OpenFolder()
    {
        _checks.Step("1. Open the demo folder");
        SmokeDemoFolder.Create(options.DemoFolder);
        _checks.Note($"demo folder: {options.DemoFolder}");

        page.OpenFolderForTour(options.DemoFolder);

        _checks.That(await Until(() => Config.Files.Count == SmokeDemoFolder.Count && page.IsMainImageShown),
            $"{SmokeDemoFolder.Count} photos listed and the first one shown");
        _checks.That(Config.CurrentIndex == 0, $"the first photo is the current one ({Config.CurrentFileName})");
        _checks.That(await Until(() => page.VisibleThumbnailCount == SmokeDemoFolder.Count),
            $"the strip shows all {SmokeDemoFolder.Count} thumbnails ({page.VisibleThumbnailCount})");
        _checks.That(page.CurrentFileText.Contains($"1 / {SmokeDemoFolder.Count}"), $"the header counts the photos ({page.CurrentFileText})");
        _checks.That(page.StatusText.Contains(SmokeDemoFolder.Count.ToString()), $"status: {page.StatusText}");
        _checks.That(!page.IsPreviousArrowVisible && page.IsNextArrowVisible, "only the right arrow overlays the first photo");
    }

    // ── 2. arrow keys, Home / End ───────────────────────────────────────────────────────────────

    private async Task Browse()
    {
        _checks.Step("2. Next / previous / first / last photo, with the keyboard");

        page.HandleViewerKey(ViewerKey.Next);
        _checks.That(await Until(() => Config.CurrentIndex == 1 && page.IsMainImageShown),
            $"Right arrow shows the next photo ({Config.CurrentFileName})");
        _checks.That(page.IsPreviousArrowVisible && page.IsNextArrowVisible, "both arrows overlay a photo in the middle");

        page.HandleViewerKey(ViewerKey.Previous);
        _checks.That(await Until(() => Config.CurrentIndex == 0 && page.IsMainImageShown), "Left arrow goes back");

        page.HandleViewerKey(ViewerKey.Last);
        var last = Config.Files.Count - 1;
        _checks.That(await Until(() => Config.CurrentIndex == last && page.IsMainImageShown),
            $"End shows the last photo ({Config.CurrentFileName})");
        _checks.That(!page.IsNextArrowVisible, "the right arrow is gone on the last photo");

        page.HandleViewerKey(ViewerKey.First);
        _checks.That(await Until(() => Config.CurrentIndex == 0 && page.IsMainImageShown),
            $"Home shows the first photo ({Config.CurrentFileName})");
    }

    // ── 3. Space likes and unlikes ──────────────────────────────────────────────────────────────

    private async Task LikeAndUnlike()
    {
        _checks.Step("3. Like and unlike, with the Space key");

        var likedFolder = Path.Combine(options.DemoFolder, AppStrings.DefaultLikedFolderName);

        page.HandleViewerKey(ViewerKey.ToggleLike);
        _checks.That(await Until(() => Directory.Exists(likedFolder) && Directory.GetFiles(likedFolder).Length == 1),
            "Space copies the photo to the Liked folder");
        _checks.That(Service.IsCurrentLiked, "the service knows the photo is liked");
        _checks.That(await Until(() => page.IsLikedBadgeVisible), "the heart shows on the viewer");

        page.HandleViewerKey(ViewerKey.ToggleLike);
        _checks.That(await Until(() => Directory.GetFiles(likedFolder).Length == 0), "Space again removes the copy");
        _checks.That(await Until(() => !page.IsLikedBadgeVisible), "and the heart is gone");
    }

    // ── 4. PageUp / PageDown between the months of the file dates ───────────────────────────────

    private async Task MonthJumps()
    {
        _checks.Step("4. PageDown / PageUp between the months (July, August, September 2025 by the dates of the files)");

        page.HandleViewerKey(ViewerKey.NextMonth);
        _checks.That(await Until(() => Config.CurrentIndex == 2 && page.IsMainImageShown && page.StatusText.Contains("August 2025")),
            $"PageDown goes to the first photo of the next month ({page.StatusText})");

        page.HandleViewerKey(ViewerKey.NextMonth);
        _checks.That(await Until(() => Config.CurrentIndex == 3 && page.StatusText.Contains("September 2025")),
            $"PageDown again goes to September ({page.StatusText})");

        page.HandleViewerKey(ViewerKey.NextMonth);
        _checks.That(await Until(() => Config.CurrentIndex == 3 && page.StatusText.Contains("last month")),
            $"there is no month after the last ({page.StatusText})");

        page.HandleViewerKey(ViewerKey.PreviousMonth);
        _checks.That(await Until(() => Config.CurrentIndex == 2 && page.StatusText.Contains("August 2025")),
            "PageUp goes back to August");

        page.HandleViewerKey(ViewerKey.PreviousMonth);
        _checks.That(await Until(() => Config.CurrentIndex == 0 && page.StatusText.Contains("July 2025")),
            $"PageUp again goes to the first photo of July ({page.StatusText})");
    }

    // ── 5. nothing leaked ───────────────────────────────────────────────────────────────────────

    private void NothingLeaked()
    {
        _checks.Step("5. Nothing leaked");
        _checks.That(File.Exists(Path.Combine(options.AppDataFolder, "global-config.json")),
            "the last folder went to the redirected app-data folder, not the real one");
    }

    private void Finish()
    {
        var exitCode = _checks.Failures == 0 ? 0 : 1;
        try
        {
            File.WriteAllText(options.LogFile, _checks.Render());
        }
        catch
        {
            exitCode = 2; // without the log nobody sees the checks: that is a failure of its own
        }

        if (!options.KeepDemoFolder)
        {
            try { Directory.Delete(options.RootFolder, recursive: true); } catch { /* temp files */ }
        }

        Environment.Exit(exitCode);
    }

    /// <summary>Waits (on the UI thread, so the window keeps living) until the condition holds, at most ten seconds.</summary>
    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 10_000)
    {
        var stopAt = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < stopAt)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }

        return condition();
    }
}
