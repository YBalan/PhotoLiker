using System.Reflection;
using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The frame of the main window made for real: the toolbar standing at the left edge (icons only, names after the hamburger button), and the panel with the two grids docked left,
/// right or bottom, folded into its tab, or floating in a window of its own; and that where things were put is where they come back.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class MainFormLayoutTests : IDisposable
{
    private static readonly string ConfigFile = Path.Combine(AppPaths.DataFolder, "global-config.json");

    public MainFormLayoutTests() => DeleteConfig(); // every test starts from the defaults

    public void Dispose() => DeleteConfig(); // and leaves none behind for the next one

    private static void DeleteConfig()
    {
        try { File.Delete(ConfigFile); } catch (IOException) { }
    }

    private static T Field<T>(object owner, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static ToolStrip Rail(Form form) => Field<ToolStrip>(form, "toolStrip1");

    private static PropertiesDock Dock(Form form) => Field<PropertiesDock>(form, "_dock");

    private static Splitter Splitter(Form form) => Field<Splitter>(form, "_dockSplitter");

    private static PropertiesTab Tab(Form form) => Field<PropertiesTab>(form, "_dockTab");

    private static Control Viewer(Form form) => Field<SplitContainer>(form, "splitContainer3");

    private static SplitContainer Grids(Form form) => Field<SplitContainer>(form, "splitContainer2");

    private static ToolStripButton Hamburger(Form form) => (ToolStripButton)Rail(form).Items["railToggleToolStripButton"]!;

    private static ToolStripButton PropertiesButton(Form form) => (ToolStripButton)Rail(form).Items["propertiesToolStripButton"]!;

    private static GlobalConfig ConfigOf(Form form) => Field<GlobalConfig>(form, "_globalConfig");

    private static Form? FloatWindow() => Ui.Find("PropertiesFloatWindow");

    private static async Task Settle() => await Task.Delay(120);

    /// <summary>A click on a control that has no PerformClick (the tab).</summary>
    private static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);

    private static void DoubleClick(Control control) =>
        typeof(Control).GetMethod("OnDoubleClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);

    /// <summary>What the splitter says when a person has dragged it: it moved.</summary>
    private static void DragSplitter(Form form) =>
        typeof(Splitter).GetMethod("OnSplitterMoved", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Splitter(form), [new SplitterEventArgs(0, 0, 0, 0)]);

    // ── the rail ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheToolbar_StandsAtTheLeftEdge_AsAColumnOfIcons_WithTheHamburgerOnTop()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var rail = Rail(form);

            Assert.Equal(DockStyle.Left, rail.Dock);
            Assert.Equal(ToolStripLayoutStyle.VerticalStackWithOverflow, rail.LayoutStyle);
            Assert.Equal(0, rail.Left);
            Assert.Equal(form.ClientSize.Height, rail.Height);

            var hamburger = rail.Items[0];
            Assert.Equal("railToggleToolStripButton", hamburger.Name);
            Assert.NotNull(hamburger.Image);

            // Icons only, one under the other, all as wide as the rail
            var buttons = rail.Items.OfType<ToolStripButton>().Where(button => button.Visible && !button.IsOnOverflow).ToList();
            Assert.True(buttons.Count >= 10, "the buttons are in the rail");
            Assert.All(buttons, button =>
            {
                Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
                Assert.NotNull(button.Image);
            });
            Assert.True(buttons.Zip(buttons.Skip(1)).All(pair => pair.First.Bounds.Bottom <= pair.Second.Bounds.Top), "one under the other");
            Assert.True(buttons.Select(button => button.Bounds.Left).Distinct().Count() == 1, "all at the same left edge");
            Assert.True(rail.Width <= 48, $"a narrow rail ({rail.Width})");
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void TheHamburger_ShowsTheNamesOfTheButtons_AndHidesThemAgain_AndTheIconsStayPut()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var rail = Rail(form);
            var open = rail.Items["openToolStripButton"]!;
            var narrow = rail.Width;
            var iconLeft = open.Bounds.Left;

            Hamburger(form).PerformClick();
            await Settle();

            Assert.True(rail.Width > narrow + 80, $"the rail is wider ({narrow} -> {rail.Width})");
            Assert.True(ConfigOf(form).RailExpanded);
            var shown = rail.Items.OfType<ToolStripButton>().Where(button => button.Visible).ToList();
            Assert.All(shown, button =>
            {
                Assert.Equal(ToolStripItemDisplayStyle.ImageAndText, button.DisplayStyle);
                Assert.False(string.IsNullOrWhiteSpace(button.Text), $"{button.Name} has a name");
            });
            Assert.Equal(iconLeft, open.Bounds.Left); // the icon is where it was
            var longest = shown.Max(button => TextRenderer.MeasureText(button.Text!.Replace("&", ""), button.Font).Width);
            Assert.True(open.Width >= 16 + longest, "the names fit: the buttons are as wide as the longest");

            Hamburger(form).PerformClick();
            await Settle();

            Assert.Equal(narrow, rail.Width);
            Assert.False(ConfigOf(form).RailExpanded);
            Assert.All(rail.Items.OfType<ToolStripButton>(), button => Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle));
        }, seconds: 60);
    }

    [Fact]
    public void TheThemeButton_ShowsTheSunInTheDarkTheme_AndTheMoonInTheLightOne_WithoutTheEmojiOfItsText()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var theme = (ToolStripButton)Rail(form).Items["themeToggleToolStripButton"]!;
            var dark = Field<Settings>(form, "_currentConfig").IsDarkTheme;
            Assert.Equal(ToolStripItemDisplayStyle.Image, theme.DisplayStyle);
            Assert.NotNull(theme.Image);
            Assert.Equal(MainForm.WithoutLeadingSymbols(dark ? MainFormStrings.ThemeLightLabel : MainFormStrings.ThemeDarkLabel), theme.Text);

            var before = theme.Text;
            theme.PerformClick();
            await Settle();

            Assert.NotEqual(before, theme.Text);
            Assert.All(new[] { before, theme.Text }, text => Assert.True(char.IsLetter(text![0]), $"'{text}' starts with a letter, not an emoji"));
        }, seconds: 60);
    }

    [Theory]
    [InlineData("🌙 Dark", "Dark")]
    [InlineData("☀️ Light", "Light")]
    [InlineData("🌙 Темна", "Темна")]
    [InlineData("Dark", "Dark")]
    [InlineData("", "")]
    public void TheEmojiBeforeAName_IsLeftOut(string text, string name) => Assert.Equal(name, MainForm.WithoutLeadingSymbols(text));

    // ── the panel ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThePanelWithTheGrids_IsDockedAtTheLeft_NextToTheRail_ByDefault()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var rail = Rail(form);
            var dock = Dock(form);

            Assert.Equal(PanelPlace.Left, dock.Place);
            Assert.Equal(DockStyle.Left, dock.Dock);
            Assert.Equal(rail.Right, dock.Left);
            Assert.Equal(PanelPlace.DefaultWidth, dock.Width);
            Assert.Equal(form.ClientSize.Height, dock.Height);
            Assert.Equal(dock.Right, Splitter(form).Left);
            Assert.Equal(Splitter(form).Right, Viewer(form).Left);
            Assert.Equal(form.ClientSize.Width, Viewer(form).Right);

            // Both grids are in it, one above the other
            Assert.True(Ui.Controls<PropertyGrid>(dock).Count() == 2);
            Assert.Equal(Orientation.Horizontal, Grids(form).Orientation);
            Assert.False(Tab(form).Visible);
            Assert.True(PropertiesButton(form).Checked);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void ThePanelCanBeDockedAtTheRight_AndAtTheBottom_AndBackAtTheLeft_FromItsMenu()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var dock = Dock(form);
            var rail = Rail(form);

            dock.RightItem.PerformClick();
            await Settle();
            Assert.Equal(PanelPlace.Right, dock.Place);
            Assert.Equal(DockStyle.Right, dock.Dock);
            Assert.Equal(form.ClientSize.Width, dock.Right);
            Assert.Equal(dock.Left, Splitter(form).Right);
            Assert.Equal(Splitter(form).Left, Viewer(form).Right);
            Assert.Equal(rail.Right, Viewer(form).Left);
            Assert.True(dock.RightItem.Checked && !dock.LeftItem.Checked);
            Assert.Equal(Orientation.Horizontal, Grids(form).Orientation);

            dock.BottomItem.PerformClick();
            await Settle();
            Assert.Equal(PanelPlace.Bottom, dock.Place);
            Assert.Equal(DockStyle.Bottom, dock.Dock);
            Assert.Equal(form.ClientSize.Height, dock.Bottom);
            Assert.Equal(rail.Right, dock.Left);                       // the rail keeps the whole height; the panel lies under the viewer
            Assert.Equal(form.ClientSize.Width, dock.Right);
            Assert.Equal(form.ClientSize.Height, rail.Height);
            Assert.Equal(Splitter(form).Top, Viewer(form).Bottom);
            Assert.Equal(PanelPlace.DefaultHeight, dock.Height);
            Assert.Equal(Orientation.Vertical, Grids(form).Orientation); // the grids side by side in a wide panel
            Assert.All(Ui.Controls<PropertyGrid>(dock), grid => Assert.True(grid.Width > 150 && grid.Height > 100));

            dock.LeftItem.PerformClick();
            await Settle();
            Assert.Equal(PanelPlace.Left, dock.Place);
            Assert.Equal(rail.Right, dock.Left);
            Assert.Equal(Orientation.Horizontal, Grids(form).Orientation);
            Assert.Equal(PanelPlace.Left, ConfigOf(form).PropertiesPlace);
        }, seconds: 60);
    }

    [Fact]
    public void TheSizeOfThePanel_IsKept_WhenTheSplitterIsDragged_AndWhenItMovesAround()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var dock = Dock(form);

            // As a person drags the splitter: the width changes and the splitter says it moved
            dock.Width = 400;
            DragSplitter(form);
            dock.RightItem.PerformClick();
            await Settle();

            Assert.Equal(400, dock.Width);
            Assert.Equal(400, ConfigOf(form).PropertiesWidth);
        }, seconds: 60);
    }

    [Fact]
    public void ThePanel_CanBeFoldedIntoATab_WhichGivesItsRoomToThePhoto_AndOpensItAgain()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var dock = Dock(form);
            var rail = Rail(form);
            var tab = Tab(form);
            var wide = Viewer(form).Width;

            dock.CollapseItem.PerformClick();
            await Settle();

            Assert.False(dock.Visible);
            Assert.False(Splitter(form).Visible);
            Assert.True(tab.Visible);
            Assert.Equal(rail.Right, tab.Left);
            Assert.Equal(PropertiesTab.Thickness, tab.Width);
            Assert.Equal(tab.Right, Viewer(form).Left);
            Assert.True(Viewer(form).Width > wide + 250, $"the photo has the room ({wide} -> {Viewer(form).Width})");
            Assert.False(PropertiesButton(form).Checked);
            Assert.True(ConfigOf(form).PropertiesCollapsed);

            // The tab opens it
            Click(tab);
            await Settle();
            Assert.True(dock.Visible && !tab.Visible);
            Assert.Equal(wide, Viewer(form).Width);
            Assert.Equal(dock.Right, Splitter(form).Left); // the splitter on the inner side of the panel, after it was shown again
            Assert.Equal(Splitter(form).Right, Viewer(form).Left);
            Assert.True(PropertiesButton(form).Checked);

            // So does the button of the rail, which also folds it
            PropertiesButton(form).PerformClick();
            await Settle();
            Assert.False(dock.Visible);
            PropertiesButton(form).PerformClick();
            await Settle();
            Assert.True(dock.Visible);
            Assert.False(ConfigOf(form).PropertiesCollapsed);
        }, seconds: 60);
    }

    [Fact]
    public void TheTab_SitsAtTheEdgeThePanelIsAt()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var dock = Dock(form);
            var tab = Tab(form);

            dock.RightItem.PerformClick();
            dock.CollapseItem.PerformClick();
            await Settle();
            Assert.True(tab.Visible);
            Assert.Equal(DockStyle.Right, tab.Dock);
            Assert.Equal(form.ClientSize.Width, tab.Right);

            dock.BottomItem.PerformClick(); // moving a folded panel opens it at the new place
            await Settle();
            Assert.True(dock.Visible && !tab.Visible);
            dock.CollapseItem.PerformClick();
            await Settle();
            Assert.Equal(DockStyle.Bottom, tab.Dock);
            Assert.Equal(form.ClientSize.Height, tab.Bottom);
            Assert.Equal(PropertiesTab.Thickness, tab.Height);
        }, seconds: 60);
    }

    [Fact]
    public void ThePanel_CanFloatInAWindowOfItsOwn_AndTheCloseButtonDocksItBack()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var dock = Dock(form);
            var wide = Viewer(form).Width;

            dock.RightItem.PerformClick();
            await Settle();
            dock.FloatItem.PerformClick();
            Assert.True(await Ui.Until(() => FloatWindow() is not null, 5000), "the window opens");
            var window = FloatWindow()!;

            Assert.Same(form, window.Owner);
            Assert.False(window.ShowInTaskbar);
            Assert.Same(window, dock.Parent);
            Assert.Equal(PanelPlace.Float, dock.Place);
            Assert.Equal(2, Ui.Controls<PropertyGrid>(window).Count());
            Assert.False(Splitter(form).Visible || Tab(form).Visible);
            Assert.True(Viewer(form).Width > wide, "the photo has the whole width");
            Assert.True(window.Width >= 300 && window.Height >= 300);
            Assert.True(dock.FloatItem.Checked);

            // The close button docks it back to where it came from
            window.Close();
            Assert.True(await Ui.Until(() => FloatWindow() is null, 5000), "the window is gone");
            Assert.Same(form, dock.Parent);
            Assert.Equal(PanelPlace.Right, dock.Place);
            Assert.Equal(form.ClientSize.Width, dock.Right);
            Assert.True(dock.Visible);
        }, seconds: 60);
    }

    [Fact]
    public void ADoubleClickOnTheTitle_FloatsThePanel_AndDocksItBack()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var dock = Dock(form);
            var title = Ui.Controls<Label>(dock).Single(label => label.Name == "propertiesDockTitle");
            Assert.Equal(LayoutStrings.PanelTitle, title.Text);

            DoubleClick(title);
            Assert.True(await Ui.Until(() => FloatWindow() is not null, 5000));
            Assert.Equal(PanelPlace.Float, dock.Place);

            DoubleClick(title);
            Assert.True(await Ui.Until(() => FloatWindow() is null, 5000));
            Assert.Equal(PanelPlace.Left, dock.Place);
        }, seconds: 60);
    }

    [Fact]
    public void AKeyPressedInTheFloatingWindow_GoesToTheMainWindow_LikeTheKeysOfTheViewer()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Dock(form).FloatItem.PerformClick();
            Assert.True(await Ui.Until(() => FloatWindow() is not null, 5000));
            var window = FloatWindow()!;

            // F5 lists the folder again; with no folder it still writes the status that a key was pressed (the viewer's keys all do)
            var status = Field<ToolStripStatusLabel>(form, "toolStripStatusLabel1");
            status.Text = "before";
            var message = new Message { HWnd = window.Handle };
            var handled = (bool)typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [message, Keys.Home])!;

            Assert.NotEqual("before", status.Text);
            Assert.True(handled);
        }, seconds: 60);
    }

    [Fact]
    public void FoldedWhileItFloats_TheWindowIsPutAway_AndTheButtonOfTheRailBringsItBack()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Dock(form).FloatItem.PerformClick();
            Assert.True(await Ui.Until(() => FloatWindow() is not null, 5000));
            var window = FloatWindow()!;
            Assert.True(window.Visible);

            PropertiesButton(form).PerformClick();
            await Settle();
            Assert.False(window.Visible);
            Assert.False(PropertiesButton(form).Checked);
            Assert.False(Tab(form).Visible); // a window of its own has no tab

            PropertiesButton(form).PerformClick();
            await Settle();
            Assert.True(window.Visible);
            Assert.True(PropertiesButton(form).Checked);
        }, seconds: 60);
    }

    // ── what comes back at the next start ─────────────────────────────────────────────────────

    [Fact]
    public void WhereThingsWerePut_IsWhereTheyComeBack()
    {
        // The first window: names shown, the panel at the right, wide, folded
        Ui.Run(() => new MainForm(), async form =>
        {
            Hamburger(form).PerformClick();
            Dock(form).RightItem.PerformClick();
            await Settle();
            Dock(form).Width = 380;
            DragSplitter(form);
            Dock(form).CollapseItem.PerformClick();
            await Settle();
        }, seconds: 60);

        var saved = GlobalConfig.Load();
        Assert.True(saved.RailExpanded);
        Assert.Equal(PanelPlace.Right, saved.PropertiesPlace);
        Assert.True(saved.PropertiesCollapsed);
        Assert.Equal(380, saved.PropertiesWidth);

        // The next one
        Ui.Run(() => new MainForm(), async form =>
        {
            Assert.True(Rail(form).Width > 100, "the names are shown");
            Assert.Equal(PanelPlace.Right, Dock(form).Place);
            Assert.False(Dock(form).Visible);
            Assert.True(Tab(form).Visible);
            Assert.Equal(DockStyle.Right, Tab(form).Dock);

            Click(Tab(form));
            await Settle();
            Assert.Equal(380, Dock(form).Width);
            Assert.Equal(form.ClientSize.Width, Dock(form).Right);
            Assert.Equal(Dock(form).Left, Splitter(form).Right); // the splitter is on the inner side of the panel, not at the edge of the window
        }, seconds: 60);
    }

    [Fact]
    public void AFloatingPanel_ComesBackFloating_WhereItWas()
    {
        Rectangle first = default;
        Ui.Run(() => new MainForm(), async form =>
        {
            Dock(form).FloatItem.PerformClick();
            Assert.True(await Ui.Until(() => FloatWindow() is not null, 5000));
            var window = FloatWindow()!;
            window.Bounds = new Rectangle(window.Left + 20, window.Top + 30, 410, 530);
            first = window.Bounds;
            await Settle();

            // The main window is closed as a person closes it (the test's own clean-up would close the newest window first, which is the close button of the floating one)
            form.Close();
            await Settle();
        }, seconds: 60);

        Assert.Equal(PanelPlace.Float, GlobalConfig.Load().PropertiesPlace);

        Ui.Run(() => new MainForm(), async form =>
        {
            Assert.True(await Ui.Until(() => FloatWindow() is not null, 5000), "the window opens again");
            Assert.Equal(first, FloatWindow()!.Bounds);
            Assert.Equal(2, Ui.Controls<PropertyGrid>(FloatWindow()!).Count());

            // Closing it docks it at the left (where nothing else had put it)
            FloatWindow()!.Close();
            Assert.True(await Ui.Until(() => FloatWindow() is null, 5000));
            Assert.Equal(PanelPlace.Left, Dock(form).Place);
        }, seconds: 60);
    }

    [Fact]
    public void ASettingsFileThatSaysNonsense_OrIsFromBeforeThePanelMoved_OpensWithTheDefaults()
    {
        Directory.CreateDirectory(AppPaths.DataFolder);
        File.WriteAllText(ConfigFile, """{ "LastFolder": "", "PropertiesPlace": "diagonal", "PropertiesWidth": -5 }""");

        Ui.Run(() => new MainForm(), async form =>
        {
            Assert.Equal(PanelPlace.Left, Dock(form).Place);
            Assert.True(Dock(form).Width >= PanelPlace.MinWidth);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    // ── a window that is minimized, or very small ─────────────────────────────────────────────

    [Theory]
    [InlineData(PanelPlace.Left)]
    [InlineData(PanelPlace.Right)]
    [InlineData(PanelPlace.Bottom)]
    public void AMinimizedWindow_ThrowsNothing_AndComesBackAsItWas(string place)
    {
        // The window is minimized to a title bar a few pixels wide: the viewer has no room, and the splitter between the photo and the thumbnails cannot be put anywhere
        // (SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize). The exception of the first version was thrown from the resize.
        Ui.Run(() => new MainForm(), async form =>
        {
            Exception? thrown = null;
            ThreadExceptionEventHandler handler = (_, e) => thrown = e.Exception;
            Application.ThreadException += handler;
            try
            {
                (place == PanelPlace.Right ? Dock(form).RightItem : place == PanelPlace.Bottom ? Dock(form).BottomItem : Dock(form).LeftItem).PerformClick();
                await Settle();
                var photoWidth = Viewer(form).Width;
                var photoHeight = Viewer(form).Height;

                form.WindowState = FormWindowState.Minimized;
                await Settle();
                Assert.Null(thrown);

                form.WindowState = FormWindowState.Normal;
                await Settle();
                Assert.Null(thrown);
                Assert.Equal(photoWidth, Viewer(form).Width);
                Assert.Equal(photoHeight, Viewer(form).Height);
            }
            finally
            {
                Application.ThreadException -= handler;
            }
        }, seconds: 60);
    }

    [Fact]
    public void AWindowTooNarrowForThePhoto_AndTheThumbnails_ThrowsNothing()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Exception? thrown = null;
            ThreadExceptionEventHandler handler = (_, e) => thrown = e.Exception;
            Application.ThreadException += handler;
            try
            {
                // The ruler (the thumbnails) is set to its width whenever the viewer is resized; with the panel and the toolbar at the left the viewer is narrower than it
                var adjust = typeof(MainForm).GetMethod("AdjustRulerWidth", BindingFlags.NonPublic | BindingFlags.Instance)!;
                form.MinimumSize = Size.Empty;
                form.Width = Rail(form).Width + Dock(form).Width + 40;
                await Settle();
                adjust.Invoke(form, null);
                Assert.Null(thrown);
            }
            finally
            {
                Application.ThreadException -= handler;
            }
        }, seconds: 60);
    }

    // ── the language ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheNamesOfTheRail_AndThePanel_FollowTheLanguage()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            Hamburger(form).PerformClick();
            await Settle();
            var english = Rail(form).Width;
            Assert.Equal(LayoutStrings.PropertiesButton, PropertiesButton(form).Text);

            using (Loc.Use(AppLanguage.Find("ua")))
            {
                typeof(MainForm).GetMethod("ApplyControlTexts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                Assert.Equal("Властивості", PropertiesButton(form).Text);
                Assert.Equal("Властивості", Ui.Controls<Label>(Dock(form)).Single(label => label.Name == "propertiesDockTitle").Text);
                Assert.Equal("Властивості", Tab(form).Text);
                Assert.Equal("Закріпити зліва", Dock(form).LeftItem.Text);
                Assert.NotEqual(english, Rail(form).Width); // as wide as its longest name needs, in that language
            }

            await Task.CompletedTask;
        }, seconds: 60);
    }
}
