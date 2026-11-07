using System.Drawing.Drawing2D;
using System.Reflection;
using PhotoLiker.Core;

namespace SmokeTour;

/// <summary>
/// Videos, for the pictures of the README and the user manual (<c>--only-video</c>): the demo folder has two clips made of its photos and
/// <c>SupportVideo</c> on. The poster of a video, the player, a real index of photos and videos with the models the app has downloaded, a search
/// that finds a place in a video, and the slideshow (photos, then a video in it).
/// </summary>
internal sealed partial class Tour
{
    private static readonly BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public async Task RunVideo()
    {
        form.TopMost = true; // nothing else on the desktop may cover the window while it is photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        checks.Step("1. SupportVideo is on: the videos are listed with the photos, a video shows its poster");
        var total = DemoFolder.CountTopLevel(".jpg") + DemoFolder.VideoCount;
        checks.That(await Until(() => form.Config.Files.Count == total && PhotoShown()), $"{total} items listed (photos and videos) and the first one shown");

        form.Find<SplitContainer>("splitContainer1").SplitterDistance = SidePanelWidth;
        form.Find<SplitContainer>("splitContainer2").SplitterDistance = SettingsGridHeight;
        await Settle();

        var firstVideo = form.Config.Files.FindIndex(f => f.IsVideo);
        checks.That(firstVideo > 0 && form.Config.Files.Count(f => f.IsVideo) == DemoFolder.VideoCount, $"the {DemoFolder.VideoCount} videos are in the list, after the photos (the first one at {firstVideo})");
        if (firstVideo <= 0)
            return;

        await GoTo(firstVideo - 1);
        form.Press(Keys.Right);
        var video = form.Config.Files[firstVideo].OriginalFilePath;
        checks.That(await Until(() => form.Viewer.Tag as string == video && form.Viewer.Image is not null, 30_000), $"the poster of {Path.GetFileName(video)} is shown");
        await Settle(1500); // the metadata grid and the thumbnail's play mark come a moment later
        checks.That(MetadataCategories().Any(c => c.StartsWith("0.")), $"the grid under the settings has the video's rows ({string.Join(" | ", MetadataCategories())})");
        (await Shot("34-video-poster")).Dispose();

        await Playing();
        await Searching();
        await Slideshow();
    }

    private IEnumerable<string> MetadataCategories()
    {
        var root = form.MetadataGrid.SelectedGridItem;
        while (root?.Parent is not null)
            root = root.Parent;
        return root?.GridItems.Cast<GridItem>().Select(c => c.Label ?? "") ?? [];
    }

    private Control? Player() => AllControls(form).FirstOrDefault(c => c.GetType().Name == "VideoPlayerPanel");

    private static T Get<T>(object owner, string property) => (T)owner.GetType().GetProperty(property, AnyMember)!.GetValue(owner)!;

    private long PlayerTime() => Player() is { } p ? Get<long>(p, "TimeMs") : 0;

    private bool PlayerPlaying() => Player() is { Visible: true } p && Get<bool>(p, "IsPlaying");

    // ── 2. playing ──────────────────────────────────────────────────────────────────────────────

    private async Task Playing()
    {
        checks.Step("2. Enter plays the video: the bar under it, a click on the slider goes anywhere");

        form.Press(Keys.Enter);
        checks.That(await Until(PlayerPlaying, 60_000), "the player starts (the engine can take seconds the first time)");
        checks.That(await Until(() => PlayerTime() > 1500, 15_000), $"and the video runs ({PlayerTime()} ms)");

        Player()!.GetType().GetMethod("SeekTo", AnyMember)!.Invoke(Player(), [8_500L]); // the third scene: the sunset
        checks.That(await Until(() => PlayerTime() >= 8_500, 10_000), "a jump to 0:08 works");
        (await Shot("35-video-playing")).Dispose();

        form.Press(Keys.Escape);
        checks.That(await Until(() => !PlayerPlaying() && Player() is not { Visible: true }), "Esc goes back to the poster");
    }

    // ── 3. a real index of the photos and the videos, and a search that finds a place in a video ─

    private async Task Searching()
    {
        checks.Step("3. Update index goes through the videos too; the search finds the place in one where the cake is");

        var models = Path.Combine(AppPaths.DataFolder, "models");
        if (!Directory.Exists(models) || Directory.GetDirectories(models).Length == 0)
        {
            checks.Fail($"the models of the app are not there ({models}): there is nothing to index with");
            return;
        }

        var strip = form.Find<ToolStrip>("searchToolStrip");
        var index = strip.Items["indexToolStripButton"]!;
        var label = strip.Items["indexToolStripLabel"]!;
        checks.That(strip.Items["searchPhotosToolStripButton"]!.Visible && strip.Items["searchVideosToolStripButton"]!.Visible, "the Photos and Videos buttons are in the search strip");

        index.PerformClick();
        var done = false;
        for (var waited = 0; waited < 600_000; waited += 250)
        {
            var box = ScreenCapture.FindDialogOfThisThread(Window);
            if (box != IntPtr.Zero)
            {
                checks.Fail("a window opened while indexing (a question about the models or the engine?)");
                ScreenCapture.Close(box);
                return;
            }

            if (index.Text == "Update index" && label.Text.Contains("photos"))
            {
                done = true;
                break;
            }

            await Task.Delay(250);
        }

        checks.That(done, $"the index is made ('{index.Text}' / '{label.Text}')");
        if (!done)
            return;

        await Settle(800);
        await ShotOfSearchStrip(strip, "38-search-strip-video");

        var box2 = (ToolStripTextBox)strip.Items["searchToolStripTextBox"]!;
        box2.Text = "birthday cake";
        strip.Items["searchToolStripButton"]!.PerformClick();
        var close = strip.Items["closeResultsToolStripButton"]!;
        checks.That(await Until(() => close.Visible, 120_000), "the results are open (Close results shows)");
        checks.Note($"results: {form.Status}");

        form.Press(Keys.End);
        for (var step = 0; step < 8 && !(form.Viewer.Tag is string shown && MediaKinds.IsVideo(shown)); step++)
        {
            await Settle(400);
            form.Press(Keys.Left);
        }

        await Settle(600);
        var onVideo = form.Viewer.Tag is string current && MediaKinds.IsVideo(current);
        checks.That(onVideo, $"a video is among the results ({form.Viewer.Tag})");
        if (!onVideo)
            return;

        await Settle(2500); // the strip of moments is made in the background
        var moments = AllControls(form).FirstOrDefault(c => c.GetType().Name == "MomentStrip");
        checks.That(moments is { Visible: true }, "the strip of the moments of the video shows under its picture");
        (await Shot("36-video-search")).Dispose();

        form.Press(Keys.Enter);
        checks.That(await Until(PlayerPlaying, 60_000), "Enter plays from the best fitting place");
        checks.That(await Until(() => PlayerTime() > 800, 15_000), $"and it runs ({PlayerTime()} ms)");
        (await Shot("37-video-search-playing")).Dispose();

        form.Press(Keys.Escape); // the player
        await Until(() => !PlayerPlaying());
        form.Press(Keys.Escape); // the results
        checks.That(await Until(() => !close.Visible, 20_000), "Esc closes the results");
    }

    // ── 4. the slideshow ────────────────────────────────────────────────────────────────────────

    private async Task Slideshow()
    {
        checks.Step("4. Alt+Enter: the slideshow on the whole screen, with the effect's name and the date in the corners");

        form.Config.SlideshowTimeout = 30; // the shot must not catch a change of photo
        form.Config.SlideshowEffect = SlideshowEffect.Slide;
        form.Press(Keys.Home);
        await Until(() => form.Config.CurrentIndex == 0 && PhotoShown());

        form.TopMost = false; // the window of the tour is in front of everything: the slideshow has to be the one that is photographed
        form.BeginInvoke(() => form.Press(Keys.Alt | Keys.Enter)); // the slideshow is modal: the key only returns when it is over
        Form? screen = null;
        checks.That(await Until(() => (screen = OpenDialogLike("SlideshowScreen")) is not null, 20_000), "Alt+Enter opens the slideshow");
        if (screen is null)
            return;

        screen.TopMost = true;
        screen.Activate();

        await Settle(2500); // the frames of the photos ahead are made
        SendKeys.SendWait("{DOWN}"); // the next effect: its name is written in the corner for two seconds
        await Settle(700);
        using (var picture = ScreenCapture.Capture(screen.Bounds))
            SaveScaled(picture, "39-slideshow", 1280);

        SendKeys.SendWait("{ESC}");
        checks.That(await Until(() => OpenDialogLike("SlideshowScreen") is null, 20_000), "Esc goes back to the window");

        // A video in the show: UseVideo on, the shown item is a video (it plays first, on the screen of the window)
        form.Config.SlideshowUseVideo = true;
        form.Config.SlideshowMuteVideo = true;
        var firstVideo = form.Config.Files.FindIndex(f => f.IsVideo);
        await GoTo(firstVideo - 1);
        form.Press(Keys.Right);
        await Until(() => form.Viewer.Tag as string == form.Config.Files[firstVideo].OriginalFilePath && form.Viewer.Image is not null, 30_000);
        await Settle(800);

        form.BeginInvoke(() => form.Press(Keys.Alt | Keys.Enter));
        checks.That(await Until(() => (screen = OpenDialogLike("SlideshowScreen")) is not null, 20_000), "Alt+Enter opens the slideshow on a video");
        if (screen is null)
            return;

        screen.TopMost = true;
        screen.Activate();

        await Settle(6000); // the video plays (the second scene of it, the boat, from 4 seconds on)
        using (var picture = ScreenCapture.Capture(screen.Bounds))
            SaveScaled(picture, "40-slideshow-video", 1280);

        SendKeys.SendWait("{ESC}");
        checks.That(await Until(() => OpenDialogLike("SlideshowScreen") is null, 20_000), "Esc goes back to the window");
        form.TopMost = true;
    }

    private static Form? OpenDialogLike(string typeName) => Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == typeName);

    /// <summary>A picture of a whole screen is bigger than a page needs: saved at this width.</summary>
    private void SaveScaled(Bitmap picture, string name, int width)
    {
        var height = (int)Math.Round(picture.Height * (double)width / picture.Width);
        using var small = new Bitmap(width, height);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(picture, 0, 0, width, height);
        }

        Save(small, name);
    }
}
