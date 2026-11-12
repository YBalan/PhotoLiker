using PhotoLiker.Core;

namespace PhotoLikerUI
{
    /// <summary>
    /// The frame of the main window: the toolbar standing at the left edge (the "rail": icons only, or icons and names after the hamburger button on top is
    /// pressed) and the panel with the two property grids, which can be docked to the left, right or bottom edge, folded into a thin tab, or floated in a window of its own.
    /// Where things are is kept in the config of the program (<see cref="GlobalConfig"/>) and comes back at the next start.
    /// </summary>
    public partial class MainForm
    {
        // The rail. The icons stay where they are when the names appear, so the picture does not jump: 16 px icons at the same left edge in both widths
        private const int RailCollapsedWidth = 44;
        private const int RailItemHeight = 34;
        private const int RailItemMargin = 3;
        private const int RailIconLeft = 14;     // from the left edge of the rail to the icon
        private const int RailTextGap = 10;      // between the icon and the name
        private const int RailTextRight = 14;    // after the longest name
        private const int RailMinExpandedWidth = 150;
        private const string RailTitle = "PhotoLiker";

        private ToolStripButton _railToggle = null!;
        private ToolStripButton _propertiesButton = null!;
        private ToolTip _layoutTips = null!;

        private PropertiesDock _dock = null!;
        private Splitter _dockSplitter = null!;
        private PropertiesTab _dockTab = null!;
        private PropertiesFloatWindow? _floatWindow;
        private string _place = PanelPlace.Left;          // where the panel is now
        private string _dockedPlace = PanelPlace.Left;    // the edge it docks back to from its own window
        private bool _collapsed;                          // folded into the tab (docked), or its window put away (floating)

        /// <summary>A size in pixels at 100 % scaling, in the pixels of this window (the program is aware of the scaling of the screen; the config keeps the sizes at 100 %).</summary>
        private int Px(int logical) => LogicalToDeviceUnits(logical);

        private int Logical(int pixels) => (int)Math.Round(pixels * 96.0 / DeviceDpi);

        private void InitializeLayout()
        {
            components ??= new System.ComponentModel.Container();
            _layoutTips = new ToolTip(components);

            InitializeRail();
            InitializePropertiesPanel();

            FormClosing += (_, _) => RememberLayout();
            // The window is scaled to the screen when it is shown, which also scales the sizes that were set before: they are set again, in the pixels it has now. The
            // window that floats is made now too (it needs a main window that is on screen to be owned by).
            Shown += (_, _) =>
            {
                ApplyPlace();
                LayoutRail();
            };
        }

        // ── the rail ───────────────────────────────────────────────────────

        private void InitializeRail()
        {
            _railToggle = new ToolStripButton(RailTitle)
            {
                Name = "railToggleToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = AppIcons.Hamburger(ThemeManager.LightFore),
                Font = new Font(toolStrip1.Font, FontStyle.Bold),
            };
            _railToggle.Click += (_, _) => SetRailExpanded(!_globalConfig.RailExpanded);

            _propertiesButton = new ToolStripButton(LayoutStrings.PropertiesButton)
            {
                Name = "propertiesToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = AppIcons.Properties(),
                ToolTipText = LayoutStrings.PropertiesButtonTooltip,
            };
            _propertiesButton.Click += (_, _) => SetCollapsed(!_collapsed);

            toolStrip1.Items.Insert(0, _railToggle);
            toolStrip1.Items.Insert(1, _propertiesButton);
            toolStrip1.Items.Insert(2, new ToolStripSeparator { Name = "railToolStripSeparator" });
        }

        /// <summary>Shows or hides the names of the buttons, and keeps that for the next start.</summary>
        private void SetRailExpanded(bool expanded)
        {
            _globalConfig.RailExpanded = expanded;
            _globalConfig.Save();
            LayoutRail();
        }

        /// <summary>
        /// Gives the buttons of the rail their size and shows their names (or not): all as wide as the rail, so that the whole row lights up under the mouse, and the rail
        /// as wide as the longest name needs. Runs again whenever a text may have changed (the language, the theme button, the registered context menu).
        /// </summary>
        private void LayoutRail()
        {
            var expanded = _globalConfig.RailExpanded;
            var buttons = toolStrip1.Items.OfType<ToolStripButton>().ToList();

            var width = Px(RailCollapsedWidth);
            if (expanded)
            {
                var longest = buttons.Max(button => TextRenderer.MeasureText(RailName(button), button.Font).Width);
                width = Math.Max(Px(RailMinExpandedWidth), Px(RailIconLeft) + toolStrip1.ImageScalingSize.Width + Px(RailTextGap) + longest + Px(RailTextRight));
            }

            toolStrip1.SuspendLayout();
            try
            {
                foreach (var button in buttons)
                {
                    button.AutoSize = false;
                    button.DisplayStyle = expanded ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
                    button.ImageAlign = ContentAlignment.MiddleLeft;
                    button.TextAlign = ContentAlignment.MiddleLeft;
                    button.TextImageRelation = TextImageRelation.ImageBeforeText;
                    button.Margin = new Padding(Px(RailItemMargin), 1, Px(RailItemMargin), 1);
                    button.Padding = new Padding(Px(RailIconLeft - RailItemMargin), 0, 0, 0);
                    button.Size = new Size(width - 2 * Px(RailItemMargin), Px(RailItemHeight));
                }

                foreach (var separator in toolStrip1.Items.OfType<ToolStripSeparator>())
                    separator.Margin = new Padding(8, 2, 8, 2);

                toolStrip1.Width = width;
                _railToggle.ToolTipText = expanded ? LayoutStrings.RailHideNames : LayoutStrings.RailShowNames;
            }
            finally
            {
                toolStrip1.ResumeLayout(performLayout: true);
            }
        }

        /// <summary>What the button is called in the rail: its text without the accelerator mark.</summary>
        private static string RailName(ToolStripItem button) => button.Text?.Replace("&", string.Empty) ?? string.Empty;

        /// <summary>
        /// A label of the theme button without the picture that stands before it ("🌙 Dark" is "Dark"): the rail has its own picture, a drawn moon or sun, and
        /// a text that starts with an emoji would show two.
        /// </summary>
        internal static string WithoutLeadingSymbols(string text)
        {
            var start = 0;
            while (start < text.Length && !char.IsLetterOrDigit(text, start))
                start++;

            return text[start..];
        }

        /// <summary>The theme button shows what a press turns on: the sun while the theme is dark, the moon while it is light; the hamburger takes the colour of the text.</summary>
        private void ApplyRailTheme(bool dark)
        {
            themeToggleToolStripButton.Text = WithoutLeadingSymbols(dark ? MainFormStrings.ThemeLightLabel : MainFormStrings.ThemeDarkLabel);
            var oldTheme = themeToggleToolStripButton.Image; // drawn again each time, so the one that was on is let go
            themeToggleToolStripButton.Image = dark ? AppIcons.ThemeLight() : AppIcons.ThemeDark();
            oldTheme?.Dispose();

            var old = _railToggle.Image;
            _railToggle.Image = AppIcons.Hamburger(dark ? ThemeManager.DarkFore : ThemeManager.LightFore);
            old?.Dispose();

            _dock.Restyle(dark);
            if (_floatWindow is not null)
            {
                ThemeManager.Apply(_floatWindow, dark);
                _dock.Restyle(dark);
            }

            LayoutRail();
        }

        /// <summary>The texts of the rail and the panel, in the language that is on.</summary>
        private void ApplyLayoutTexts()
        {
            _propertiesButton.Text = LayoutStrings.PropertiesButton;
            _propertiesButton.ToolTipText = LayoutStrings.PropertiesButtonTooltip;
            _dock.ApplyTexts();
            _dockTab.Text = LayoutStrings.PanelTitle;
            _layoutTips.SetToolTip(_dockTab, LayoutStrings.PanelExpandTooltip);
            if (_floatWindow is not null)
                _floatWindow.Text = LayoutStrings.PanelTitle;

            LayoutRail();
        }

        // ── the panel with the property grids ──────────────────────────────

        private void InitializePropertiesPanel()
        {
            _dock = new PropertiesDock(splitContainer2);
            _dockSplitter = new Splitter { Name = "propertiesSplitter", Width = 5, MinExtra = 250 }; // sizes are set in ApplyPlace, in the pixels of the screen
            _dockTab = new PropertiesTab();

            Controls.Add(_dockSplitter);
            Controls.Add(_dock);
            Controls.Add(_dockTab);

            _dock.PlaceRequested += SetPlace;
            _dock.CollapseRequested += () => SetCollapsed(true);
            _dock.Menu.Opening += (_, _) => ApplyMenuTheme(_dock.Menu);
            _dockTab.Click += (_, _) => SetCollapsed(false);
            _dockSplitter.SplitterMoved += (_, _) => RememberDockSize();

            _place = PanelPlace.Normalize(_globalConfig.PropertiesPlace);
            _dockedPlace = _place == PanelPlace.Float ? PanelPlace.Left : _place;
            _collapsed = _globalConfig.PropertiesCollapsed;
            ApplyPlace();
        }

        /// <summary>Moves the panel to one of the <see cref="PanelPlace"/> places (from its menu, or by a double click on its title). It is shown there, even if it was folded.</summary>
        private void SetPlace(string place)
        {
            place = PanelPlace.Normalize(place);
            if (place == _place && !_collapsed)
                return;

            RememberDockSize();
            if (place != PanelPlace.Float)
                _dockedPlace = place;

            _place = place;
            _collapsed = false;
            ApplyPlace();
            SaveLayoutNow();
            FocusViewer();
        }

        /// <summary>Folds the panel into its tab (or puts its window away) or shows it again.</summary>
        private void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed)
                return;

            RememberDockSize();
            _collapsed = collapsed;
            ApplyPlace();
            SaveLayoutNow();
            if (collapsed)
                FocusViewer();
        }

        /// <summary>
        /// Puts everything as <see cref="_place"/> and <see cref="_collapsed"/> say: the panel docked at an edge (with its splitter), or folded into the tab at that edge, or
        /// in the window of its own. Can be run again at any time.
        /// </summary>
        private void ApplyPlace()
        {
            var floating = _place == PanelPlace.Float;
            SuspendLayout();
            try
            {
                _dock.DockedPlace = _dockedPlace;
                _dock.Place = _place;
                _dockTab.Place = floating ? _dockedPlace : _place;

                if (floating)
                {
                    _dockSplitter.Visible = false;
                    _dockTab.Visible = false;
                    ShowFloatWindow();
                }
                else
                {
                    CloseFloatWindow();
                    if (_dock.Parent != this)
                        Controls.Add(_dock);

                    var style = DockOf(_place);
                    _dock.Dock = style;
                    _dockSplitter.Dock = style;
                    var sideBySide = _place == PanelPlace.Bottom;
                    if (sideBySide)
                    {
                        _dock.Height = Px(Math.Max(PanelPlace.MinHeight, _globalConfig.PropertiesHeight));
                        _dockSplitter.Height = Px(5);
                        _dockSplitter.MinSize = Px(PanelPlace.MinHeight);
                    }
                    else
                    {
                        _dock.Width = Px(Math.Max(PanelPlace.MinWidth, _globalConfig.PropertiesWidth));
                        _dockSplitter.Width = Px(5);
                        _dockSplitter.MinSize = Px(PanelPlace.MinWidth);
                    }

                    ArrangeGrids(sideBySide);

                    _dock.Visible = !_collapsed;
                    _dockSplitter.Visible = !_collapsed;
                    _dockTab.Visible = _collapsed;

                    // After the visibility: showing the splitter puts it behind the panel, which would leave it at the edge of the window and the panel inside it
                    RestackDockControls();
                }

                _propertiesButton.Checked = !_collapsed;
            }
            finally
            {
                ResumeLayout(performLayout: true);
            }
        }

        private static DockStyle DockOf(string place) => place switch
        {
            PanelPlace.Right => DockStyle.Right,
            PanelPlace.Bottom => DockStyle.Bottom,
            _ => DockStyle.Left,
        };

        /// <summary>
        /// The order the edges are filled in is the order of the controls, last first: the toolbar takes the left edge of the window, then the tab, the panel and its splitter take theirs
        /// inside it, and the viewer is what is left.
        /// </summary>
        private void RestackDockControls()
        {
            Controls.SetChildIndex(splitContainer3, 0);
            Controls.SetChildIndex(_dockSplitter, 1);
            Controls.SetChildIndex(_dock, 2);
            Controls.SetChildIndex(_dockTab, 3);
            Controls.SetChildIndex(toolStrip1, 4);
        }

        /// <summary>One grid above the other in a tall panel, side by side in a wide one (at the bottom).</summary>
        private void ArrangeGrids(bool sideBySide)
        {
            var orientation = sideBySide ? Orientation.Vertical : Orientation.Horizontal;
            if (splitContainer2.Orientation == orientation)
                return;

            splitContainer2.Orientation = orientation;
            var total = sideBySide ? splitContainer2.Width : splitContainer2.Height;
            var wanted = sideBySide ? total / 2 : total * 2 / 5;
            var most = total - splitContainer2.Panel2MinSize - splitContainer2.SplitterWidth;
            if (most > splitContainer2.Panel1MinSize)
                splitContainer2.SplitterDistance = Math.Clamp(wanted, splitContainer2.Panel1MinSize, most);
        }

        /// <summary>What the panel measures now is what it will have at the next start (the splitter is the only thing that changes it).</summary>
        private void RememberDockSize()
        {
            if (_place == PanelPlace.Float || _collapsed || !_dock.Visible || _dock.Parent != this)
                return;

            if (_place == PanelPlace.Bottom)
                _globalConfig.PropertiesHeight = Logical(_dock.Height);
            else
                _globalConfig.PropertiesWidth = Logical(_dock.Width);
        }

        // ── the window the panel floats in ─────────────────────────────────

        private void ShowFloatWindow()
        {
            // Before the main window is on screen there is nothing for the window to be owned by: it is made when that is so (see Shown)
            if (!IsHandleCreated || !Visible)
            {
                _dock.Visible = false;
                return;
            }

            if (_floatWindow is null)
            {
                _floatWindow = new PropertiesFloatWindow(_dock, this, FloatBounds(), ProcessCmdKey);
                _floatWindow.DockBackRequested += () => SetPlace(_dockedPlace);
                ThemeManager.Apply(_floatWindow, _currentConfig.IsDarkTheme);
                _dock.Restyle(_currentConfig.IsDarkTheme);
            }

            _dock.Visible = true;
            if (_collapsed)
            {
                _floatWindow.Hide();
            }
            else if (!_floatWindow.Visible)
            {
                _floatWindow.Show(this);
            }
        }

        /// <summary>Takes the panel out of its window and closes the window (the panel goes back among the controls of the main window).</summary>
        private void CloseFloatWindow()
        {
            if (_floatWindow is not { } window)
                return;

            RememberFloatBounds();
            _floatWindow = null;
            Controls.Add(_dock); // out of the window before it is closed, so that closing does not take the panel along
            window.CloseForGood();
            window.Dispose();
        }

        private Rectangle FloatBounds()
        {
            var saved = new Rectangle(_globalConfig.FloatLeft, _globalConfig.FloatTop, _globalConfig.FloatWidth, _globalConfig.FloatHeight);
            if (saved.Width > 0 && saved.Height > 0 && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(saved)))
                return saved;

            // Never floated: beside the right edge of the main window, a little below its title bar
            var area = Screen.FromControl(this).WorkingArea;
            var width = 360;
            var height = Math.Min(area.Height - 80, Math.Max(420, Height - 140));
            var left = Math.Min(area.Right - width - 20, Right - width - 24);
            return new Rectangle(Math.Max(area.Left, left), area.Top + 60, width, height);
        }

        private void RememberFloatBounds()
        {
            if (_floatWindow is not { WindowState: FormWindowState.Normal } window)
                return;

            (_globalConfig.FloatLeft, _globalConfig.FloatTop, _globalConfig.FloatWidth, _globalConfig.FloatHeight) =
                (window.Left, window.Top, window.Width, window.Height);
        }

        // ── keeping it ─────────────────────────────────────────────────────

        private void SaveLayoutNow()
        {
            RememberLayout();
            _globalConfig.Save();
        }

        /// <summary>Puts where things are into the config of the program; whoever is closing the window or has just moved something saves the file.</summary>
        private void RememberLayout()
        {
            RememberDockSize();
            RememberFloatBounds();
            _globalConfig.PropertiesPlace = _place;
            _globalConfig.PropertiesCollapsed = _collapsed;
        }
    }
}
