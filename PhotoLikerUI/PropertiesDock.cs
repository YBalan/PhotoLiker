using System.ComponentModel;
using PhotoLiker.Core;

namespace PhotoLikerUI
{
    /// <summary>
    /// The panel that holds the two property grids (the settings and the properties of the photo). A title bar on top has a menu to dock the panel
    /// at the left, right or bottom edge of the window or to float it in a window of its own, and a button that folds it into a thin tab
    /// (<see cref="PropertiesTab"/>) at its edge. The window (<c>MainForm</c>) does the moving; this control only says what was asked for.
    /// </summary>
    internal sealed class PropertiesDock : Panel
    {
        public const int HeaderHeight = 26;

        private readonly Panel _header = new() { Name = "propertiesDockHeader", Dock = DockStyle.Top, Height = HeaderHeight };
        private readonly Label _title = new() { Name = "propertiesDockTitle", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), AutoEllipsis = true };
        private readonly GlyphButton _menuButton = new() { Name = "propertiesDockMenuButton", Dock = DockStyle.Right, Width = HeaderHeight, Glyph = GlyphButton.Shape.Dots };
        private readonly GlyphButton _collapseButton = new() { Name = "propertiesDockCollapseButton", Dock = DockStyle.Right, Width = HeaderHeight };
        private readonly ToolTip _tips = new();

        private string _place = PanelPlace.Left;

        /// <summary>Asks to be moved: one of the <see cref="PanelPlace"/> names.</summary>
        public event Action<string>? PlaceRequested;

        /// <summary>Asks to be folded into the tab at its edge.</summary>
        public event Action? CollapseRequested;

        public ContextMenuStrip Menu { get; } = new() { Name = "propertiesDockMenu" };

        public ToolStripMenuItem LeftItem { get; } = new() { Name = "propertiesDockLeftItem" };
        public ToolStripMenuItem RightItem { get; } = new() { Name = "propertiesDockRightItem" };
        public ToolStripMenuItem BottomItem { get; } = new() { Name = "propertiesDockBottomItem" };
        public ToolStripMenuItem FloatItem { get; } = new() { Name = "propertiesDockFloatItem" };
        public ToolStripMenuItem CollapseItem { get; } = new() { Name = "propertiesDockCollapseItem" };

        public PropertiesDock(Control content)
        {
            Name = "propertiesDock";

            content.Dock = DockStyle.Fill;
            Controls.Add(content);

            _header.Controls.Add(_title);
            _header.Controls.Add(_menuButton);
            _header.Controls.Add(_collapseButton);
            Controls.Add(_header);

            Menu.Items.AddRange([LeftItem, RightItem, BottomItem, FloatItem, new ToolStripSeparator(), CollapseItem]);
            LeftItem.Click += (_, _) => PlaceRequested?.Invoke(PanelPlace.Left);
            RightItem.Click += (_, _) => PlaceRequested?.Invoke(PanelPlace.Right);
            BottomItem.Click += (_, _) => PlaceRequested?.Invoke(PanelPlace.Bottom);
            FloatItem.Click += (_, _) => PlaceRequested?.Invoke(PanelPlace.Float);
            CollapseItem.Click += (_, _) => CollapseRequested?.Invoke();

            _menuButton.Click += (_, _) => Menu.Show(_menuButton, new Point(_menuButton.Width, _menuButton.Height), ToolStripDropDownDirection.BelowLeft);
            _collapseButton.Click += (_, _) => CollapseRequested?.Invoke();
            _title.DoubleClick += (_, _) => PlaceRequested?.Invoke(_place == PanelPlace.Float ? DockedPlace : PanelPlace.Float);
            _header.DoubleClick += (_, _) => PlaceRequested?.Invoke(_place == PanelPlace.Float ? DockedPlace : PanelPlace.Float); // the bar that is left of the title in a window of its own

            ApplyTexts();
            Place = PanelPlace.Left;
        }

        /// <summary>Where a double click on the title docks a floating panel back to: where it was before it floated.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string DockedPlace { get; set; } = PanelPlace.Left;

        /// <summary>The place the panel is in: it marks that one in the menu, and points the collapse button at the edge.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Place
        {
            get => _place;
            set
            {
                _place = value;
                LeftItem.Checked = value == PanelPlace.Left;
                RightItem.Checked = value == PanelPlace.Right;
                BottomItem.Checked = value == PanelPlace.Bottom;
                FloatItem.Checked = value == PanelPlace.Float;

                var floating = value == PanelPlace.Float;
                _collapseButton.Visible = !floating; // a window of its own has nothing to fold into
                _title.Visible = !floating;          // and its title bar says what it is already
                _collapseButton.Glyph = value switch
                {
                    PanelPlace.Right => GlyphButton.Shape.ChevronRight,
                    PanelPlace.Bottom => GlyphButton.Shape.ChevronDown,
                    _ => GlyphButton.Shape.ChevronLeft,
                };
            }
        }

        /// <summary>Writes the texts again in the language that is on.</summary>
        public void ApplyTexts()
        {
            _title.Text = LayoutStrings.PanelTitle;
            LeftItem.Text = LayoutStrings.PanelDockLeft;
            RightItem.Text = LayoutStrings.PanelDockRight;
            BottomItem.Text = LayoutStrings.PanelDockBottom;
            FloatItem.Text = LayoutStrings.PanelFloat;
            CollapseItem.Text = LayoutStrings.PanelCollapse;

            _tips.SetToolTip(_title, LayoutStrings.PanelTitleTooltip);
            _tips.SetToolTip(_menuButton, LayoutStrings.PanelMenuTooltip);
            _tips.SetToolTip(_collapseButton, LayoutStrings.PanelCollapseTooltip);
            _menuButton.AccessibleName = LayoutStrings.PanelMenuTooltip;
            _collapseButton.AccessibleName = LayoutStrings.PanelCollapseTooltip;
        }

        /// <summary>The colours of the title bar: a shade off the panel, so that it reads as a bar in both themes.</summary>
        public void Restyle(bool dark)
        {
            _header.BackColor = dark ? ThemeManager.DarkBorder : SystemColors.ControlLight;
            _title.BackColor = _header.BackColor;
            _title.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            _menuButton.BackColor = _collapseButton.BackColor = _header.BackColor;
            _menuButton.ForeColor = _collapseButton.ForeColor = _title.ForeColor;
            _menuButton.Invalidate();
            _collapseButton.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
                Menu.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The folded panel: a thin tab at the edge of the window that carries the title of the panel (turned to read along the edge); a click opens
    /// the panel again.
    /// </summary>
    internal sealed class PropertiesTab : Control
    {
        public const int Thickness = 26;

        private bool _hover;
        private string _place = PanelPlace.Left;

        public PropertiesTab()
        {
            Name = "propertiesTab";
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Visible = false;
        }

        /// <summary>The edge the tab sits at: the text turns to read along it.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Place
        {
            get => _place;
            set
            {
                _place = value;
                Dock = value switch
                {
                    PanelPlace.Right => DockStyle.Right,
                    PanelPlace.Bottom => DockStyle.Bottom,
                    _ => DockStyle.Left,
                };
                if (value == PanelPlace.Bottom)
                    Height = LogicalToDeviceUnits(Thickness);
                else
                    Width = LogicalToDeviceUnits(Thickness);
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var back = new SolidBrush(_hover ? Color.FromArgb(70, ForeColor.R, ForeColor.G, ForeColor.B).Over(BackColor) : BackColor))
                g.FillRectangle(back, ClientRectangle);

            using var edge = new Pen(Color.FromArgb(90, ForeColor));
            if (_place == PanelPlace.Left)
                g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            else if (_place == PanelPlace.Right)
                g.DrawLine(edge, 0, 0, 0, Height);
            else
                g.DrawLine(edge, 0, 0, Width, 0);

            if (_place == PanelPlace.Bottom)
            {
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, flags);
                return;
            }

            // Along the edge: from the bottom up at the left, from the top down at the right (as the tool windows of Visual Studio do). TextRenderer ignores
            // the rotation of the graphics, so this one is drawn by GDI+.
            using var format = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            using var brush = new SolidBrush(ForeColor);
            var state = g.Save();
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.TranslateTransform(Width / 2f, Height / 2f);
            g.RotateTransform(_place == PanelPlace.Left ? -90f : 90f);
            g.DrawString(Text, Font, brush, new RectangleF(-Height / 2f, -Width / 2f, Height, Width), format);
            g.Restore(state);
        }
    }

    /// <summary>A small flat button that draws its own picture (three dots, a chevron) in the colour of the text, so it is right in every theme and at every scale.</summary>
    internal sealed class GlyphButton : Control
    {
        public enum Shape { Dots, ChevronLeft, ChevronRight, ChevronDown }

        private Shape _glyph;
        private bool _hover;
        private bool _pressed;

        public GlyphButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.StandardClick, true);
            Cursor = Cursors.Hand;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Shape Glyph
        {
            get => _glyph;
            set
            {
                _glyph = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_hover || _pressed)
            {
                using var wash = new SolidBrush(Color.FromArgb(_pressed ? 80 : 45, ForeColor));
                g.FillRectangle(wash, ClientRectangle);
            }

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var cx = Width / 2f;
            var cy = Height / 2f;
            var scale = Math.Min(Width, Height) / 26f;

            using var pen = new Pen(ForeColor, 1.6f * scale) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            using var brush = new SolidBrush(ForeColor);
            var reach = 4f * scale;
            switch (_glyph)
            {
                case Shape.Dots:
                    foreach (var dy in new[] { -4.5f, 0f, 4.5f })
                        g.FillEllipse(brush, cx - 1.4f * scale, cy + dy * scale - 1.4f * scale, 2.8f * scale, 2.8f * scale);
                    break;
                case Shape.ChevronLeft:
                    g.DrawLines(pen, [new PointF(cx + reach / 2, cy - reach), new PointF(cx - reach / 2, cy), new PointF(cx + reach / 2, cy + reach)]);
                    break;
                case Shape.ChevronRight:
                    g.DrawLines(pen, [new PointF(cx - reach / 2, cy - reach), new PointF(cx + reach / 2, cy), new PointF(cx - reach / 2, cy + reach)]);
                    break;
                case Shape.ChevronDown:
                    g.DrawLines(pen, [new PointF(cx - reach, cy - reach / 2), new PointF(cx, cy + reach / 2), new PointF(cx + reach, cy - reach / 2)]);
                    break;
            }
        }
    }

    /// <summary>Hands a key that was pressed in a window of its own to the main window, which has the keys of the viewer (the arrows, Space, Delete, the F keys): they work wherever the focus is.</summary>
    internal delegate bool CommandKeyHandler(ref Message msg, Keys keyData);

    /// <summary>
    /// The window the panel floats in: an ordinary tool window (owned by the main window, so it stays above it and goes with it), that holds the panel
    /// and docks it back when it is closed.
    /// </summary>
    internal sealed class PropertiesFloatWindow : Form
    {
        private readonly CommandKeyHandler _keys;

        /// <summary>The close button was pressed: the panel is to be docked back (the window is not closed by that, the main window does it).</summary>
        public event Action? DockBackRequested;

        public PropertiesFloatWindow(PropertiesDock dock, Form owner, Rectangle bounds, CommandKeyHandler keys)
        {
            _keys = keys;
            Name = "propertiesFloatWindow";
            Text = LayoutStrings.PanelTitle;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            MinimumSize = new Size(PanelPlace.MinWidth, PanelPlace.MinHeight);
            Icon = owner.Icon;
            Owner = owner;

            dock.Dock = DockStyle.Fill;
            Controls.Add(dock);
        }

        private bool _closeForGood;

        /// <summary>Closes the window without the panel being docked back: the main window has done that already (<c>Close</c> alone is a close button pressed, as far as the form can tell).</summary>
        public void CloseForGood()
        {
            _closeForGood = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // The close button docks the panel back; only the main window (or the program) closing really closes this one
            if (!_closeForGood && e.CloseReason == CloseReason.UserClosing && DockBackRequested is not null)
            {
                e.Cancel = true;
                DockBackRequested();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            _keys(ref msg, keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    internal static class ColorBlend
    {
        /// <summary>This colour, which may be see-through, laid over <paramref name="background"/>: the colour that shows.</summary>
        public static Color Over(this Color colour, Color background)
        {
            var a = colour.A / 255f;
            return Color.FromArgb(
                (int)(colour.R * a + background.R * (1 - a)),
                (int)(colour.G * a + background.G * (1 - a)),
                (int)(colour.B * a + background.B * (1 - a)));
        }
    }
}
