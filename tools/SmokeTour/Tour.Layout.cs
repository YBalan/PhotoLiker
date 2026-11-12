using System.Reflection;

namespace SmokeTour;

/// <summary>
/// The frame of the window, for the pictures of the README and the user manual (<c>--only-layout</c>): the toolbar at the left edge with the names of its buttons shown,
/// the panel with the two grids docked at the bottom, and floating in a window of its own over the main window. The panel is moved by its own menu, as a person does.
/// </summary>
internal sealed partial class Tour
{
    private const string FloatWindowType = "PropertiesFloatWindow";

    /// <summary>One of the entries of the menu of the panel (LeftItem, RightItem, BottomItem, FloatItem, CollapseItem); the panel is internal to the app.</summary>
    private ToolStripMenuItem DockItem(string name)
    {
        var dock = form.Find<Control>("propertiesDock");
        return (ToolStripMenuItem)dock.GetType().GetProperty(name)!.GetValue(dock)!;
    }

    public async Task RunLayout()
    {
        form.TopMost = true; // nothing else on the desktop may cover the windows while they are photographed
        checks.Note($"window brought to the front: {ScreenCapture.BringToFront(Window)}");

        var jpegs = DemoFolder.CountTopLevel(".jpg");
        checks.Step("1. The demo folder opens: the toolbar is a column of icons at the left edge, the panel with the grids is next to it");
        checks.That(await Until(() => form.Config.Files.Count == jpegs && PhotoShown()), $"{jpegs} photos listed and the first one shown");

        form.SetSidePanelWidth(SidePanelWidth);
        form.Find<SplitContainer>("splitContainer2").SplitterDistance = SettingsGridHeight;
        await Settle();

        var rail = form.Find<ToolStrip>("toolStrip1");
        var dock = form.Find<Control>("propertiesDock");
        var narrow = rail.Width;
        checks.That(rail.Dock == DockStyle.Left && rail.Left == 0 && rail.Height == form.ClientSize.Height, $"the toolbar fills the left edge ({rail.Bounds})");
        checks.That(narrow <= 48, $"and is narrow: icons only ({narrow} px)");
        checks.That(dock.Left == rail.Right && dock.Width == SidePanelWidth, $"the panel is next to it, {dock.Width} px wide");

        checks.Step("2. The hamburger button shows the names of the buttons (41-toolbar-names)");
        var hamburger = (ToolStripButton)form.ToolbarItem("railToggleToolStripButton");
        hamburger.PerformClick();
        await Settle(500);
        checks.That(rail.Width > narrow + 80, $"the toolbar is wider ({narrow} -> {rail.Width})");
        checks.That(form.ToolbarItem("openToolStripButton").DisplayStyle == ToolStripItemDisplayStyle.ImageAndText, "its buttons show icon and name");
        (await Shot("41-toolbar-names")).Dispose();
        hamburger.PerformClick();
        await Settle(500);
        checks.That(rail.Width == narrow, "pressed again, it is as narrow as before");

        checks.Step("3. The panel docked at the bottom, the two grids side by side (42-panel-bottom)");
        DockItem("BottomItem").PerformClick();
        await Settle(500);
        dock.Height = 340;
        await Settle();
        checks.That(dock.Dock == DockStyle.Bottom && dock.Bottom == form.ClientSize.Height && dock.Left == rail.Right, $"the panel lies along the bottom, right of the toolbar ({dock.Bounds})");
        checks.That(rail.Height == form.ClientSize.Height, "the toolbar keeps the whole height");
        (await Shot("42-panel-bottom")).Dispose();

        checks.Step("4. The panel in a window of its own, over the main window (43-panel-float)");
        DockItem("LeftItem").PerformClick();
        await Settle(500);
        DockItem("FloatItem").PerformClick();
        Form? window = null;
        checks.That(await Until(() => (window = OpenDialog(FloatWindowType)) is not null), "the panel opens its window");
        if (window is null)
            return;

        window.TopMost = true;
        window.Bounds = new Rectangle(form.Right - 440, form.Top + 90, 400, form.Height - 170); // inside the main window: nothing of the desktop is in the picture
        await Settle(500);
        checks.That(window.Owner == form && Controls(window, "settingsPropertyGrid") && Controls(window, "imageMetaPropertyGrid"), "it holds both grids and belongs to the main window");
        checks.That(form.Find<SplitContainer>("splitContainer3").Left <= rail.Right + 2, "and the photo has the whole width");

        ScreenCapture.BringToFront(Window);
        ScreenCapture.BringToFront(window.Handle);
        ScreenCapture.ParkMouse();
        await Settle(900);
        using (var both = ScreenCapture.Capture(ScreenCapture.FrameBounds(Window))) // the main window as it is on screen, with the floating one over it
            Save(both, "43-panel-float");

        checks.Step("5. Its close button docks the panel back at the left");
        window.Close();
        checks.That(await Until(() => OpenDialog(FloatWindowType) is null), "the window is gone");
        await Settle(500);
        checks.That(dock.Dock == DockStyle.Left && dock.Left == rail.Right && dock.Visible, "the panel is docked at the left again");
    }

    private static bool Controls(Form window, string name) => window.Controls.Find(name, searchAllChildren: true).Length == 1;
}
