using System.ComponentModel;
using PhotoLiker.Drawing;
using PhotoLiker.Effects.Models;
using PhotoLiker.Imaging;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Draw window: a photo to draw on (the pen, a highlighter, a straight line, in any colour), write text on, crop, and scan for text, with Undo and Redo for each step, and Save (over the photo) or
    /// Save as (another file). It opens around the photo, as large as the screen allows; the photo is shown as large as the window lets it be. The pixels are worked on by <see cref="DrawingSession"/> and
    /// its neighbours in PhotoLiker.Drawing, which have no window; see <see cref="DrawingCanvas"/> for the picture and the mouse, and <see cref="DrawingDialog.Actions"/> for scanning, saving and the keys.
    /// </summary>
    internal sealed partial class DrawingDialog : Form
    {
        private const int WidthMaximum = 60;
        private const int SizeMinimum = 8;
        private const int SizeMaximum = 160;

        /// <summary>The colours of the buttons of the toolbar, in the order they are shown.</summary>
        private static readonly (Color Colour, Func<string> Name)[] Palette =
        [
            (Color.FromArgb(230, 30, 40), () => DrawingStrings.ColorRed),
            (Color.FromArgb(250, 140, 20), () => DrawingStrings.ColorOrange),
            (Color.FromArgb(250, 220, 30), () => DrawingStrings.ColorYellow),
            (Color.FromArgb(40, 170, 70), () => DrawingStrings.ColorGreen),
            (Color.FromArgb(30, 110, 230), () => DrawingStrings.ColorBlue),
            (Color.FromArgb(150, 60, 200), () => DrawingStrings.ColorPurple),
            (Color.FromArgb(0, 0, 0), () => DrawingStrings.ColorBlack),
            (Color.FromArgb(255, 255, 255), () => DrawingStrings.ColorWhite),
        ];

        private readonly string _photoPath;
        private readonly bool _dark;
        private readonly EffectModels _models;
        private readonly Func<Task<bool>> _ensureModelsAsync;
        private readonly DrawingSession _session;
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };

        // The picture
        internal readonly DrawingCanvas Canvas = new();

        // The toolbar
        internal readonly ToolStrip Bar = new();
        internal readonly ToolStripButton PenButton = new();
        internal readonly ToolStripButton HighlighterButton = new();
        internal readonly ToolStripButton LineButton = new();
        internal readonly ToolStripButton TextButton = new();
        internal readonly ToolStripButton CropButton = new();
        internal readonly ToolStripButton MoreColorsButton = new();
        internal readonly List<ToolStripButton> ColorButtons = [];
        internal readonly ToolStripLabel WidthLabel = new();
        internal readonly TrackBar WidthSlider = new();
        internal readonly ToolStripButton UndoButton = new();
        internal readonly ToolStripButton RedoButton = new();
        internal readonly ToolStripButton RotateLeftButton = new();
        internal readonly ToolStripButton RotateRightButton = new();
        internal readonly ToolStripButton ApplyCropButton = new();
        internal readonly ToolStripButton CancelCropButton = new();
        internal readonly ToolStripButton ScanButton = new();
        internal readonly ToolStripButton SaveButton = new();
        internal readonly ToolStripButton SaveAsButton = new();
        internal readonly ToolStripButton CloseButton = new();

        // What a scan found
        internal readonly Panel ScanPanel = new();
        internal readonly TextBox ScanBox = new();
        internal readonly Button CopyButton = new();
        internal readonly Button HideButton = new();
        private readonly Label _scanTitle = new();

        // The status bar
        private readonly StatusStrip _statusBar = new();
        private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _infoLabel = new();
        private readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false, Width = 120 };

        private DrawTool _tool = DrawTool.Pen;
        private int _penWidth = 5;
        private int _textSize = 28;
        private bool _syncingSlider;
        private int _version;
        private int _savedVersion;

        /// <summary>The photo is read, whole, when the window is made.</summary>
        /// <param name="photoPath">The photo to draw on.</param>
        /// <param name="models">Where the models of the scan are, and how many threads they may use.</param>
        /// <param name="ensureModelsAsync">Makes sure the models of the scan are on this computer (asks, and downloads them, if they are not) and says whether they are.</param>
        /// <exception cref="NotSupportedException">The photo is no picture that Windows can read, or it is too large to draw on.</exception>
        /// <exception cref="IOException">The photo could not be read.</exception>
        public DrawingDialog(string photoPath, bool dark, EffectModels models, Func<Task<bool>> ensureModelsAsync)
        {
            _photoPath = photoPath;
            _dark = dark;
            _models = models;
            _ensureModelsAsync = ensureModelsAsync;

            var decoded = WicPhotoDecoder.DecodeFull(File.ReadAllBytes(photoPath));
            _session = new DrawingSession(new Bitmap32(decoded.Width, decoded.Height, decoded.Bgra));

            Text = $"{DrawingStrings.DialogTitle} - {Path.GetFileName(photoPath)}";
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = DialogIcons.Drawing;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            KeyPreview = false;

            // Around the photo: as large as the photo (at most nine tenths of the screen it opens on), never smaller than what the toolbar needs
            var screen = Screen.FromPoint(MousePosition).WorkingArea;
            var barRoom = 120;
            var scale = Math.Min(1.0, Math.Min((screen.Width * 0.92 - 40) / decoded.Width, (screen.Height * 0.92 - barRoom) / decoded.Height));
            var width = Math.Max(Math.Min(980, screen.Width - 40), (int)(decoded.Width * scale) + 24);
            var height = Math.Max(Math.Min(560, screen.Height - 60), (int)(decoded.Height * scale) + barRoom);
            MinimumSize = new Size(Math.Min(880, screen.Width - 40), Math.Min(480, screen.Height - 60));
            ClientSize = new Size(Math.Min(width, screen.Width - 20), Math.Min(height, screen.Height - 60));

            BuildBar();
            BuildScanPanel();
            BuildStatusBar();

            Canvas.Dock = DockStyle.Fill;
            Controls.Add(Canvas);
            Controls.Add(ScanPanel);
            Controls.Add(_statusBar);
            Controls.Add(Bar);
            Canvas.Session = _session;

            WireEvents();
            UseRealQuestions();
            ApplyTheme(dark);
            SelectTool(DrawTool.Pen);
            SelectColor(0);
            UpdateButtons();
            SetStatus(DrawingStrings.StatusReady);
            UpdateInfo();

            FormClosing += OnFormClosing;
            Disposed += (_, _) =>
            {
                _toolTip.Dispose();
                _scan?.Cancel();
                _scanner?.Dispose();
            };
        }

        /// <summary>The file that was saved last, once one was.</summary>
        public string? SavedPath { get; private set; }

        /// <summary>True when the photo itself was written over (not only a copy made).</summary>
        public bool PhotoWasChanged { get; private set; }

        /// <summary>The photo this window draws on.</summary>
        public string PhotoPath => _photoPath;

        /// <summary>The picture as it is now (for the tests).</summary>
        internal DrawingSession Session => _session;

        /// <summary>Whether the picture has changed since it was last saved (or opened).</summary>
        internal bool HasUnsavedChanges => _version != _savedVersion;

        // ── Building the window ─────────────────────────────────────────────

        private void BuildBar()
        {
            Bar.GripStyle = ToolStripGripStyle.Hidden;
            Bar.Dock = DockStyle.Top;
            Bar.Name = "drawingBar";
            Bar.ImageScalingSize = new Size(18, 18);
            Bar.Padding = new Padding(4, 2, 4, 2);

            Tool(PenButton, "penButton", DrawingStrings.ToolPen, DrawingStrings.HintPen);
            Tool(HighlighterButton, "highlighterButton", DrawingStrings.ToolHighlighter, DrawingStrings.HintHighlighter);
            Tool(LineButton, "lineButton", DrawingStrings.ToolLine, DrawingStrings.HintLine);
            Tool(TextButton, "textButton", DrawingStrings.ToolText, DrawingStrings.HintText);
            Tool(CropButton, "cropButton", DrawingStrings.ToolCrop, DrawingStrings.HintCrop);
            Bar.Items.AddRange([PenButton, HighlighterButton, LineButton, TextButton, CropButton, new ToolStripSeparator()]);

            for (var i = 0; i < Palette.Length; i++)
            {
                var button = new ToolStripButton
                {
                    Name = "color" + i,
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    Image = Swatch(Palette[i].Colour),
                    ToolTipText = string.Format(DrawingStrings.ColorTipFormat, Palette[i].Name()),
                    AccessibleName = Palette[i].Name(),
                    Padding = new Padding(1),
                };
                ColorButtons.Add(button);
                Bar.Items.Add(button);
            }

            MoreColorsButton.Name = "moreColorsButton";
            MoreColorsButton.Text = DrawingStrings.MoreColors;
            MoreColorsButton.ToolTipText = DrawingStrings.MoreColorsTip;
            Bar.Items.Add(MoreColorsButton);
            Bar.Items.Add(new ToolStripSeparator());

            WidthLabel.Name = "widthLabel";
            WidthSlider.AutoSize = false;
            WidthSlider.Width = 110;
            WidthSlider.Height = 26;
            WidthSlider.TickStyle = TickStyle.None;
            WidthSlider.Minimum = 1;
            WidthSlider.Maximum = WidthMaximum;
            WidthSlider.Value = _penWidth;
            WidthSlider.Name = "widthSlider";
            var host = new ToolStripControlHost(WidthSlider) { Name = "widthHost", AutoSize = false, Width = 116, Height = 26 };
            Bar.Items.AddRange([WidthLabel, host, new ToolStripSeparator()]);

            Plain(UndoButton, "undoButton", DrawingStrings.Undo, DrawingStrings.UndoTip);
            Plain(RedoButton, "redoButton", DrawingStrings.Redo, DrawingStrings.RedoTip);
            Plain(ApplyCropButton, "applyCropButton", DrawingStrings.ApplyCrop, DrawingStrings.ApplyCropTip);
            Plain(CancelCropButton, "cancelCropButton", DrawingStrings.CancelCrop, DrawingStrings.CancelCropTip);
            Plain(ScanButton, "scanTextButton", DrawingStrings.ScanText, DrawingStrings.ScanTextTip);
            Plain(RotateLeftButton, "rotateLeftButton", DrawingStrings.RotateLeft, DrawingStrings.RotateLeftTip);
            Plain(RotateRightButton, "rotateRightButton", DrawingStrings.RotateRight, DrawingStrings.RotateRightTip);
            RotateLeftButton.Image = AppIcons.RotateCounterClockwise();
            RotateRightButton.Image = AppIcons.RotateClockwise();
            RotateLeftButton.DisplayStyle = RotateRightButton.DisplayStyle = ToolStripItemDisplayStyle.Image; // the text is still the accessible name
            Bar.Items.AddRange([UndoButton, RedoButton, new ToolStripSeparator(), RotateLeftButton, RotateRightButton, new ToolStripSeparator(), ApplyCropButton, CancelCropButton, new ToolStripSeparator(), ScanButton]);

            // The right end of the bar: Close, Save as, Save (a right aligned item goes to the far end, so they are added the other way round)
            Plain(CloseButton, "closeDrawingButton", DrawingStrings.Close, string.Empty);
            Plain(SaveAsButton, "saveAsButton", DrawingStrings.SaveAs, DrawingStrings.SaveAsTip);
            Plain(SaveButton, "saveButton", DrawingStrings.Save, DrawingStrings.SaveTip);
            foreach (var button in new[] { CloseButton, SaveAsButton, SaveButton })
                button.Alignment = ToolStripItemAlignment.Right;
            Bar.Items.AddRange([CloseButton, SaveAsButton, SaveButton]);

            static void Tool(ToolStripButton button, string name, string text, string tip)
            {
                button.Name = name;
                button.Text = text;
                button.ToolTipText = tip;
                button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            }

            static void Plain(ToolStripButton button, string name, string text, string tip)
            {
                button.Name = name;
                button.Text = text;
                button.ToolTipText = tip;
                button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            }
        }

        /// <summary>A small square of a colour, for the button that picks it.</summary>
        private static Bitmap Swatch(Color colour)
        {
            var bitmap = new Bitmap(18, 18);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(colour);
            graphics.FillRectangle(fill, 2, 2, 14, 14);
            using var edge = new Pen(Color.FromArgb(150, 128, 128, 128));
            graphics.DrawRectangle(edge, 2, 2, 13, 13);
            return bitmap;
        }

        private void BuildScanPanel()
        {
            ScanPanel.Name = "scanPanel";
            ScanPanel.Dock = DockStyle.Right;
            ScanPanel.Width = 320;
            ScanPanel.Padding = new Padding(8);
            ScanPanel.Visible = false;

            _scanTitle.Text = DrawingStrings.ScanTitle;
            _scanTitle.AutoSize = true;
            _scanTitle.Dock = DockStyle.Top;
            _scanTitle.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);
            _scanTitle.Padding = new Padding(0, 0, 0, 6);

            ScanBox.Name = "scanBox";
            ScanBox.Multiline = true;
            ScanBox.ReadOnly = true;
            ScanBox.ScrollBars = ScrollBars.Vertical;
            ScanBox.Dock = DockStyle.Fill;
            ScanBox.HideSelection = false;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            HideButton.Name = "hideScanButton";
            HideButton.Text = DrawingStrings.ScanHide;
            HideButton.AutoSize = true;
            CopyButton.Name = "copyScanButton";
            CopyButton.Text = DrawingStrings.ScanCopy;
            CopyButton.AutoSize = true;
            _toolTip.SetToolTip(CopyButton, DrawingStrings.ScanCopyTip);
            buttons.Controls.Add(HideButton);
            buttons.Controls.Add(CopyButton);

            ScanPanel.Controls.Add(ScanBox);
            ScanPanel.Controls.Add(buttons);
            ScanPanel.Controls.Add(_scanTitle);
        }

        private void BuildStatusBar()
        {
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.SizingGrip = false;
            _statusBar.Items.AddRange([_statusLabel, _progress, _infoLabel]);
        }

        private void WireEvents()
        {
            PenButton.Click += (_, _) => SelectTool(DrawTool.Pen);
            HighlighterButton.Click += (_, _) => SelectTool(DrawTool.Highlighter);
            LineButton.Click += (_, _) => SelectTool(DrawTool.Line);
            TextButton.Click += (_, _) => SelectTool(DrawTool.Text);
            CropButton.Click += (_, _) => SelectTool(DrawTool.Crop);

            for (var i = 0; i < ColorButtons.Count; i++)
            {
                var index = i;
                ColorButtons[i].Click += (_, _) => SelectColor(index);
            }
            MoreColorsButton.Click += (_, _) => PickMoreColors();

            WidthSlider.ValueChanged += (_, _) => OnWidthChanged();
            UndoButton.Click += (_, _) => Undo();
            RedoButton.Click += (_, _) => Redo();
            RotateLeftButton.Click += (_, _) => Rotate(-1);
            RotateRightButton.Click += (_, _) => Rotate(1);
            ApplyCropButton.Click += (_, _) => ApplyCrop();
            CancelCropButton.Click += (_, _) => Canvas.CancelCrop();
            ScanButton.Click += async (_, _) => await ScanAsync();
            SaveButton.Click += async (_, _) => await SaveAsync();
            SaveAsButton.Click += async (_, _) => await SaveAsAsync();
            CloseButton.Click += (_, _) => Close();
            CopyButton.Click += (_, _) => CopyScannedText();
            HideButton.Click += (_, _) => HideScan();

            Canvas.Edited += (_, _) =>
            {
                _version++;
                UpdateButtons();
                UpdateInfo();
            };
            Canvas.CropChanged += (_, _) => UpdateCropState();
        }

        // ── The tools, the colours, the width ───────────────────────────────

        internal DrawTool CurrentTool => _tool;

        /// <summary>Picks a tool (a note that is being typed is put on the photo first).</summary>
        internal void SelectTool(DrawTool tool)
        {
            Canvas.CommitNote();
            _tool = tool;
            Canvas.Tool = tool;
            PenButton.Checked = tool == DrawTool.Pen;
            HighlighterButton.Checked = tool == DrawTool.Highlighter;
            LineButton.Checked = tool == DrawTool.Line;
            TextButton.Checked = tool == DrawTool.Text;
            CropButton.Checked = tool == DrawTool.Crop;

            // The one slider is the width of the line, or the size of the text
            _syncingSlider = true;
            try
            {
                var text = tool == DrawTool.Text;
                WidthLabel.Text = text ? DrawingStrings.LabelSize : DrawingStrings.LabelWidth;
                WidthLabel.ToolTipText = text ? DrawingStrings.HintSize : DrawingStrings.HintWidth;
                _toolTip.SetToolTip(WidthSlider, WidthLabel.ToolTipText);
                WidthSlider.Minimum = text ? SizeMinimum : 1;
                WidthSlider.Maximum = text ? SizeMaximum : WidthMaximum;
                WidthSlider.Value = Math.Clamp(text ? _textSize : _penWidth, WidthSlider.Minimum, WidthSlider.Maximum);
            }
            finally
            {
                _syncingSlider = false;
            }

            Canvas.PenWidth = _penWidth;
            Canvas.TextSize = _textSize;
            UpdateCropState();
            SetStatus(tool switch
            {
                DrawTool.Pen => DrawingStrings.HintPen,
                DrawTool.Highlighter => DrawingStrings.HintHighlighter,
                DrawTool.Line => DrawingStrings.HintLine,
                DrawTool.Text => DrawingStrings.HintText,
                _ => DrawingStrings.HintCrop,
            });
        }

        private void OnWidthChanged()
        {
            if (_syncingSlider)
                return;

            if (_tool == DrawTool.Text)
            {
                _textSize = WidthSlider.Value;
                Canvas.TextSize = _textSize;
            }
            else
            {
                _penWidth = WidthSlider.Value;
                Canvas.PenWidth = _penWidth;
            }
        }

        /// <summary>The width of the line the slider has now, as it looks on the screen (for the tests).</summary>
        internal int PenWidthValue => _penWidth;

        internal void SelectColor(int index)
        {
            Canvas.PenColor = Palette[index].Colour;
            for (var i = 0; i < ColorButtons.Count; i++)
                ColorButtons[i].Checked = i == index;
            MoreColorsButton.Checked = false;
        }

        /// <summary>Asked for a colour that is not on the toolbar. A test answers it.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<Color, Color?> AskColor { get; set; } = current =>
        {
            using var dialog = new ColorDialog { Color = current, FullOpen = true, AnyColor = true };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.Color : null;
        };

        private void PickMoreColors()
        {
            if (AskColor(Canvas.PenColor) is not { } colour)
                return;

            Canvas.PenColor = colour;
            foreach (var button in ColorButtons)
                button.Checked = false;
            MoreColorsButton.Checked = true;
        }

        // ── Undo, redo, crop ────────────────────────────────────────────────

        internal void Undo()
        {
            Canvas.CommitNote();
            if (!_session.Undo())
                return;

            _version++;
            Canvas.ShowMarks(null);
            Canvas.PictureChanged();
            UpdateButtons();
            UpdateInfo();
        }

        internal void Redo()
        {
            Canvas.CommitNote();
            if (!_session.Redo())
                return;

            _version++;
            Canvas.ShowMarks(null);
            Canvas.PictureChanged();
            UpdateButtons();
            UpdateInfo();
        }

        /// <summary>
        /// Turns the picture a quarter (clockwise for 1, counterclockwise for -1) as a step of its own: it can be taken back, and what is drawn after it is on the turned picture. Nothing is written until Save.
        /// </summary>
        internal void Rotate(int quarterTurns)
        {
            if (_busy)
                return;

            Canvas.CommitNote();
            _session.Add(new RotateStep(quarterTurns));
            _version++;
            Canvas.ShowMarks(null); // the lines a scan found are where they were before the turn
            Canvas.PictureChanged();
            UpdateButtons();
            UpdateInfo();
            SetStatus(string.Format(DrawingStrings.InfoFormat, _session.Width, _session.Height));
        }

        internal void ApplyCrop()
        {
            if (Canvas.ApplyCrop())
                SetStatus(string.Format(DrawingStrings.StatusCroppedFormat, _session.Width, _session.Height));
        }

        private void UpdateCropState()
        {
            var cropping = _tool == DrawTool.Crop;
            var area = Canvas.CropArea;
            ApplyCropButton.Visible = CancelCropButton.Visible = cropping;
            ApplyCropButton.Enabled = CancelCropButton.Enabled = area is not null;
            if (cropping && area is { } frame)
                SetStatus(string.Format(DrawingStrings.StatusCropFormat, frame.Width, frame.Height));
            else if (cropping)
                SetStatus(DrawingStrings.HintCrop);
        }

        private void UpdateButtons()
        {
            var busy = _busy;
            UndoButton.Enabled = _session.CanUndo && !busy;
            RedoButton.Enabled = _session.CanRedo && !busy;
            SaveButton.Enabled = SaveAsButton.Enabled = ScanButton.Enabled = !busy;
        }

        private void UpdateInfo() => _infoLabel.Text = string.Format(DrawingStrings.InfoFormat, _session.Width, _session.Height);

        internal void SetStatus(string text) => _statusLabel.Text = text;

        internal string StatusText => _statusLabel.Text ?? string.Empty;

        internal string InfoText => _infoLabel.Text ?? string.Empty;

        internal bool ProgressShown => _progress.Visible;

        // ── Colours of the window ───────────────────────────────────────────

        private void ApplyTheme(bool dark)
        {
            ThemeManager.Apply(this, dark);
            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            ScanBox.BackColor = inputBack;
            _scanTitle.BackColor = Color.Transparent;
            Canvas.Surround = dark ? Color.FromArgb(24, 24, 26) : Color.FromArgb(214, 214, 218);
            _statusBar.BackColor = dark ? ThemeManager.DarkBackAlt : SystemColors.Control;
            _statusBar.ForeColor = dark ? ThemeManager.DarkFore : SystemColors.ControlText;
            _statusBar.Renderer = dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
            foreach (var button in new[] { CopyButton, HideButton })
            {
                button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                if (dark)
                    button.FlatAppearance.BorderColor = ThemeManager.DarkBorder;
            }

            WidthSlider.BackColor = dark ? ThemeManager.DarkBackAlt : SystemColors.Control;
        }
    }
}
