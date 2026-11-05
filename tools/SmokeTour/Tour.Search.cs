using PhotoLiker.Core;

namespace SmokeTour;

/// <summary>
/// The search strip, for the pictures of the README and the user manual (<c>--only-search</c>): the demo folder is indexed for real, with
/// the models the app has already downloaded (Program links them into the tour's own data folder), and "More like this" is pressed on
/// the first photo, at the two ends of the precision slider.
/// </summary>
internal sealed partial class Tour
{
    public async Task RunSearch()
    {
        form.TopMost = true; // nothing else on the desktop may cover the window while it is photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        checks.Step("1. Search: the demo folder is indexed with the real models, then similar photos are listed");
        var models = Path.Combine(AppPaths.DataFolder, "models");
        if (!Directory.Exists(models) || Directory.GetDirectories(models).Length == 0)
        {
            checks.Fail($"the models of the app are not there ({models}): there is nothing to index with");
            return;
        }

        var jpegs = DemoFolder.CountTopLevel(".jpg");
        checks.That(await Until(() => form.Config.Files.Count == jpegs && PhotoShown()), $"{jpegs} photos listed and the first one shown");

        var strip = form.Find<ToolStrip>("searchToolStrip");
        var index = strip.Items["indexToolStripButton"]!;
        var label = strip.Items["indexToolStripLabel"]!;
        checks.That(index.Text == "Index photos", $"the folder is not indexed yet ({index.Text})");

        index.PerformClick(); // the handler is async: it returns when the work starts
        var done = false;
        var waited = 0;
        while (waited < 300_000)
        {
            // A message box (the models are asked to be downloaded) would hold the window for ever: it is a failure, and is closed
            var box = ScreenCapture.FindDialogOfThisThread(Window);
            if (box != IntPtr.Zero)
            {
                checks.Fail("a window opened while indexing (the models of the app are not whole?)");
                ScreenCapture.Close(box);
                return;
            }

            if (index.Text == "Update index" && label.Text.Contains("photos"))
            {
                done = true;
                break;
            }

            await Task.Delay(250);
            waited += 250;
        }

        checks.That(done, $"the index is made ('{index.Text}' / '{label.Text}')");
        if (!done)
            return;

        await Settle(600);
        await ShotOfSearchStrip(strip, "17-search-bar");

        var similar = strip.Items["similarToolStripButton"]!;
        checks.That(await Until(() => similar.Enabled), "More like this is on for the shown photo");
        similar.PerformClick();
        var close = strip.Items["closeResultsToolStripButton"]!;
        checks.That(await Until(() => close.Visible, 60_000), "the results are open (Close results shows)");
        checks.Note($"results: {form.Status}");

        // The slider re-runs the search a moment after it stops moving
        var slider = (TrackBar)((ToolStripControlHost)strip.Items["precisionToolStripSlider"]!).Control;
        slider.Value = slider.Minimum;
        await Settle(2000);
        checks.Note($"Wide: {form.Status}");
        await ShotOfSearchStrip(strip, "18-search-wide");

        slider.Value = slider.Maximum;
        await Settle(2000);
        checks.Note($"Precise: {form.Status}");
        await ShotOfSearchStrip(strip, "19-search-precise");
    }

    /// <summary>The search strip as far as the "Precise" end of its slider, as the README shows it.</summary>
    private async Task ShotOfSearchStrip(ToolStrip strip, string name)
    {
        using var window = await Shot(null);
        var area = AreaOf(strip);
        var right = strip.Items["precisionPreciseToolStripLabel"]!.Bounds.Right + 8;
        using var part = window.Clone(new Rectangle(area.X, area.Y, Math.Min(right, area.Width), area.Height), window.PixelFormat);
        Save(part, name);
    }
}
