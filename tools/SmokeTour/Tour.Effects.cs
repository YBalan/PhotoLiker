using PhotoLiker.Core;
using System.Reflection;

namespace SmokeTour;

/// <summary>
/// The two bars that float over the photo, for the pictures of the README and the user manual (<c>--only-effects</c>): the Colorize bar over a black and white photo, before and after the model gave it
/// its colours, and the Refocus bar over a colour photo, with the focus where the depth model put it and then where a click put it. The real models do the work (the app's models folder is linked into
/// the tour's own data folder, as for <c>--only-search</c>), and the one click is a real one.
/// </summary>
internal sealed partial class Tour
{
    private static Control? FindByTypeName(Control root, string typeName)
    {
        foreach (Control child in root.Controls)
        {
            if (child.GetType().Name == typeName)
                return child;
            if (FindByTypeName(child, typeName) is { } found)
                return found;
        }
        return null;
    }

    private static IEnumerable<T> All<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var inner in All<T>(child))
                yield return inner;
        }
    }

    private static T ReadProperty<T>(object owner, string property) =>
        (T)owner.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(owner)!;

    /// <summary>How much colour the viewer shows: the average distance of red, green and blue from each other (0 for a grey picture).</summary>
    private static double Colourfulness(Image? image)
    {
        if (image is null)
            return -1;

        using var small = new Bitmap(image, 160, 100);
        double sum = 0;
        for (var y = 0; y < small.Height; y++)
        for (var x = 0; x < small.Width; x++)
        {
            var c = small.GetPixel(x, y);
            sum += Math.Abs(c.R - c.G) + Math.Abs(c.G - c.B) + Math.Abs(c.R - c.B);
        }
        return sum / (small.Width * small.Height);
    }

    /// <summary>The place on the screen where this share of the shown photo is (the viewer draws it as large as fits, in the middle).</summary>
    private Point ScreenPointOnPhoto(double x, double y)
    {
        var viewer = form.Viewer;
        var image = viewer.Image!;
        var scale = Math.Min(viewer.ClientSize.Width / (double)image.Width, viewer.ClientSize.Height / (double)image.Height);
        var left = (viewer.ClientSize.Width - image.Width * scale) / 2;
        var top = (viewer.ClientSize.Height - image.Height * scale) / 2;
        return viewer.PointToScreen(new Point((int)(left + x * image.Width * scale), (int)(top + y * image.Height * scale)));
    }

    /// <summary>The message the status bar gives after a folder is listed, instead of the "Key pressed: Right" that the tour left there by going from photo to photo.</summary>
    private void CleanStatus() => form.StatusBarItem("toolStripStatusLabel1").Text = $"Loaded {form.Config.Files.Count} photos from '{folder}'";

    public async Task RunEffects()
    {
        form.TopMost = true; // nothing else on the desktop may cover the window while it is photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        var photos = DemoFolder.CountTopLevel(".jpg") + DemoFolder.EffectPhotos;
        checks.Step("1. The demo folder opens, and the panel with the grids is folded away: the photo has the room");
        checks.That(await Until(() => form.Config.Files.Count == photos && PhotoShown()), $"{photos} photos listed and the first one shown");

        var properties = (ToolStripButton)form.ToolbarItem("propertiesToolStripButton");
        properties.PerformClick();
        await Settle(700);
        checks.That(!properties.Checked, "the panel is folded");

        // ── Colorize ────────────────────────────────────────────────────────────────────────
        checks.Step("2. A black and white photo: the Colorize bar floats over it, ready (44-colorize-bar)");
        var grey = form.Config.Files.FindIndex(file => file.OriginalFilePath.EndsWith(DemoFolder.BlackAndWhitePhoto, StringComparison.OrdinalIgnoreCase));
        checks.That(grey >= 0, "the black and white photo is in the list");
        await GoTo(grey);

        Control? colorizeBar = null;
        checks.That(await Until(() => (colorizeBar = FindByTypeName(form, "ColorizeBar")) is { Visible: true }, 20000), "the Colorize bar comes over the photo");
        if (colorizeBar is null)
            return;

        var original = form.Viewer.Image;
        checks.That(Colourfulness(original) < 3, $"the photo is grey ({Colourfulness(original):0.0})");
        await Settle(500);
        CleanStatus();
        (await Shot("44-colorize-bar")).Dispose();

        checks.Step("3. Colorize: the model gives it colours, and the sliders change them (45-colorized)");
        form.ToolbarItem("colorizeToolStripButton").PerformClick();
        checks.That(await Until(() => ReadProperty<bool>(colorizeBar, "ColoursShown") && ReadProperty<bool>(colorizeBar, "Ready") && !ReferenceEquals(form.Viewer.Image, original), 120000), $"the colours are there ({ReadProperty<string>(colorizeBar, "Status")})");
        checks.That(await Until(() => Colourfulness(form.Viewer.Image) > 4, 20000), $"and the viewer shows them ({Colourfulness(form.Viewer.Image):0.0})");

        // The sliders as a person would leave them: a little stronger and a little warmer than the model gave it
        var sliders = All<TrackBar>(colorizeBar).ToList();
        checks.That(sliders.Count == 2, "the bar has two sliders, the strength and the warmth");
        sliders[0].Value = 120;
        sliders[1].Value = 25;
        await Settle(1500);
        checks.That(Colourfulness(form.Viewer.Image) > 4, "the picture follows them");
        CleanStatus();
        (await Shot("45-colorized")).Dispose();

        // ── Refocus ─────────────────────────────────────────────────────────────────────────
        checks.Step("4. A colour photo: the Refocus bar, with the focus where the people (here: the middle) are (46-refocus-bar)");
        var colour = form.Config.Files.FindIndex(file => file.OriginalFilePath.EndsWith(DemoFolder.RefocusPhoto, StringComparison.OrdinalIgnoreCase));
        checks.That(colour >= 0, "the forest is in the list");
        await GoTo(colour);
        checks.That(await Until(() => FindByTypeName(form, "ColorizeBar") is not { Visible: true }, 10000), "no Colorize bar over a photo with colours");

        var shown = form.Viewer.Image;
        form.ToolbarItem("refocusToolStripButton").PerformClick();
        Control? refocusBar = null;
        checks.That(await Until(() => (refocusBar = FindByTypeName(form, "RefocusBar")) is { Visible: true }, 20000), "the Refocus bar opens");
        if (refocusBar is null)
            return;

        checks.That(await Until(() => ReadProperty<bool>(refocusBar, "CanSave") && !ReferenceEquals(form.Viewer.Image, shown), 120000), $"the depth is worked out and the photo is refocused ({ReadProperty<string>(refocusBar, "Status")})");
        var photoTop = form.Find<Control>("scrollPanel").Top;
        checks.That(refocusBar.Top >= photoTop && refocusBar.Top < photoTop + 40, $"the bar is at the top of the photo, below the search strip ({refocusBar.Top}, the photo begins at {photoTop})");
        await Settle(800);
        CleanStatus();
        (await Shot("46-refocus-bar")).Dispose();

        checks.Step("5. A click on the photo puts the focus there; the blur and the haze are turned up (47-refocus-click)");
        var before = form.Viewer.Image;
        var near = ScreenPointOnPhoto(0.5, 0.88); // the nearest hills, at the bottom of the photo
        RealInput.Click(near);
        checks.That(await Until(() => !ReferenceEquals(form.Viewer.Image, before), 20000), "the picture is drawn again with the focus on the near hills");

        var focusSliders = All<TrackBar>(refocusBar).ToList();
        checks.That(focusSliders.Count == 3, "the bar has three sliders: blur, sharp zone, haze");
        focusSliders[0].Value = 85;
        focusSliders[2].Value = 30;
        await Settle(1800);
        CleanStatus();
        (await Shot("47-refocus-click")).Dispose();
    }
}
