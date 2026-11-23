using System.ComponentModel;
using PhotoLiker.Stickers;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Telegram Sticker window: turns the photo that is shown into a sticker, a picture with a see-through background that is 512 pixels on its longer side, in the file Telegram takes (WebP or PNG,
    /// under 512 KB). The subject of the photo is found by a model that runs on this computer (the first time it is asked to be downloaded), the cut line is shown on the photo, and the sticker is shown
    /// on a checkerboard, a dark chat or a light one as the settings are moved. The work is done by <see cref="StickerRenderer"/> and its neighbours in PhotoLiker.Stickers, which have no window;
    /// see <see cref="StickerDialog.Run"/> for the part that reads, finds, makes and saves.
    /// </summary>
    internal sealed partial class StickerDialog : Form
    {
        private readonly string _photoPath;
        private readonly string _settingsFile;
        private readonly bool _dark;
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };

        // The cutout
        internal readonly ComboBox CutoutCombo = new();
        internal readonly NumericUpDown SensitivityNumeric = Number(StickerSettings.MinSensitivity, StickerSettings.MaxSensitivity, 1, StickerSettings.DefaultSensitivity);
        internal readonly NumericUpDown SmallPartsNumeric = Number(0, 100, 1, StickerSettings.DefaultSmallParts);
        internal readonly NumericUpDown FollowEdgesNumeric = Number(0, 100, 1, StickerSettings.DefaultFollowEdges);
        internal readonly NumericUpDown EdgeNumeric = Number(-StickerSettings.MaxEdgeShift, StickerSettings.MaxEdgeShift, 1, 0);

        // The outline and the shadow
        internal readonly CheckBox OutlineCheck = new();
        internal readonly NumericUpDown OutlineWidthNumeric = Number(0, StickerSettings.MaxOutlineWidth, 1, StickerSettings.DefaultOutlineWidth);
        internal readonly Button OutlineColorButton = new();
        internal readonly CheckBox ShadowCheck = new();

        // The file
        internal readonly ComboBox FormatCombo = new();
        internal readonly NumericUpDown QualityNumeric = Number(StickerSettings.MinQuality, StickerSettings.MaxQuality, 1, StickerSettings.DefaultQuality);

        // The sliders that stand beside the numbers: each moves its number, and follows it when the number is typed or stepped
        internal readonly TrackBar SensitivitySlider = Slider();
        internal readonly TrackBar SmallPartsSlider = Slider();
        internal readonly TrackBar FollowEdgesSlider = Slider();
        internal readonly TrackBar EdgeSlider = Slider();
        internal readonly TrackBar OutlineWidthSlider = Slider();
        internal readonly TrackBar QualitySlider = Slider();
        private readonly List<(NumericUpDown Number, TrackBar Slider)> _sliders = [];
        private bool _syncingSlider;

        // The pictures and the buttons
        internal readonly ComboBox BackgroundCombo = new();
        internal readonly StickerPane PhotoPane = new();
        internal readonly StickerPane StickerPane = new();
        internal readonly Button SaveButton = new();
        internal readonly Button CloseButton = new();
        private readonly ProgressBar _progress = new();
        private readonly Label _statusLabel = new();
        private readonly Label _infoLabel = new();
        private readonly Label _howToLabel = new();
        private readonly Label _photoCaption = new();
        private readonly Label _stickerCaption = new();
        private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };

        private Color _outlineColor = Color.White;
        private bool _loading = true;

        /// <param name="photoPath">The photo to make a sticker of.</param>
        /// <param name="settingsFile">Where the values of the window are kept between two uses.</param>
        /// <param name="models">Where the model that finds the subject is, and how many threads it may use.</param>
        /// <param name="ensureModelAsync">Makes sure the model that finds the subject is on this computer (asks, and downloads it, if it is not) and says whether it is.</param>
        public StickerDialog(string photoPath, bool dark, string settingsFile, EffectModels models, Func<Task<bool>> ensureModelAsync)
        {
            _photoPath = photoPath;
            _dark = dark;
            _settingsFile = settingsFile;
            _models = models;
            _ensureModelAsync = ensureModelAsync;

            Text = $"{StickerStrings.DialogTitle} - {Path.GetFileName(photoPath)}";
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = DialogIcons.Sticker;
            StartPosition = FormStartPosition.CenterParent;

            // As big as is useful, but never bigger than the screen it opens on (a laptop has 768 lines): the settings scroll when they do not fit
            var screen = Screen.FromPoint(MousePosition).WorkingArea;
            MinimumSize = new Size(Math.Min(1000, screen.Width - 40), Math.Min(640, screen.Height - 60));
            ClientSize = new Size(Math.Min(1180, screen.Width - 60), Math.Min(720, screen.Height - 100));
            ShowInTaskbar = false;
            MinimizeBox = false;

            var left = BuildSettingsPanel();
            var right = BuildPicturesPanel();
            var bottom = BuildBottom();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);
            Controls.Add(root);

            CancelButton = CloseButton;

            Apply(StickerSettings.Load(settingsFile));
            WireEvents();
            ApplyTheme(dark);
            _loading = false;
            UpdateEnabled();
            SetStatus(StickerStrings.StatusReading);

            Shown += async (_, _) => await OpenAsync();
            FormClosing += (_, _) => OnClosing();
            Disposed += (_, _) =>
            {
                // A form can be disposed twice (when it is closed, and by whoever made it), so nothing here may mind being done again
                _debounce.Dispose();
                _cancellation?.Cancel();
                _toolTip.Dispose();
            };
        }

        /// <summary>The file that was saved, once one was (what the window hands back).</summary>
        public string? SavedPath { get; private set; }

        // ── Building the window ─────────────────────────────────────────────

        private static NumericUpDown Number(int min, int max, int step, int value) => new()
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            Value = value,
            Width = 64,
            Margin = new Padding(0, 1, 0, 1),
            TextAlign = HorizontalAlignment.Right,
        };

        private static TrackBar Slider() => new()
        {
            AutoSize = false,
            Width = 120,
            Height = 28,
            TickStyle = TickStyle.None,
            Margin = new Padding(6, 0, 0, 0),
        };

        private static Label Heading(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 3),
        };

        private Panel BuildSettingsPanel()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 8, 0) };
            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Dock = DockStyle.Top };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var row = 0;
            void AddHeading(string text)
            {
                var heading = Heading(text);
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(heading, 0, row);
                table.SetColumnSpan(heading, 3);
                row++;
            }

            void AddRow(Control first, Control? second = null, string? tip = null, Control? third = null)
            {
                first.Margin = new Padding(0, 2, 0, 2);
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(first, 0, row);
                if (second is null)
                    table.SetColumnSpan(first, 3);
                else
                    table.Controls.Add(second, 1, row);
                if (third is not null)
                    table.Controls.Add(third, 2, row);
                if (tip is not null)
                {
                    foreach (var control in new[] { first, second, third })
                    {
                        if (control is not null)
                            _toolTip.SetToolTip(control, tip);
                    }
                }

                row++;
            }

            Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };

            AddHeading(StickerStrings.GroupCutout);
            CutoutCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            CutoutCombo.Width = 280;
            CutoutCombo.Margin = new Padding(0, 1, 0, 1);
            CutoutCombo.Items.AddRange([new Choice<CutoutMode>(CutoutMode.Detect, StickerStrings.ChoiceDetect), new Choice<CutoutMode>(CutoutMode.WholePicture, StickerStrings.ChoiceWholePicture)]);
            CutoutCombo.SelectedIndex = 0;
            AddRow(Caption(StickerStrings.LabelSubject), tip: StickerStrings.HintSubject);
            AddRow(CutoutCombo, tip: StickerStrings.HintSubject);
            AddRow(Caption(StickerStrings.LabelSensitivity), SensitivityNumeric, StickerStrings.HintSensitivity, SensitivitySlider);
            AddRow(Caption(StickerStrings.LabelSmallParts), SmallPartsNumeric, StickerStrings.HintSmallParts, SmallPartsSlider);
            AddRow(Caption(StickerStrings.LabelFollowEdges), FollowEdgesNumeric, StickerStrings.HintFollowEdges, FollowEdgesSlider);
            AddRow(Caption(StickerStrings.LabelEdge), EdgeNumeric, StickerStrings.HintEdge, EdgeSlider);

            AddHeading(StickerStrings.GroupOutline);
            OutlineCheck.Text = StickerStrings.OutlineCheck;
            OutlineCheck.AutoSize = true;
            AddRow(OutlineCheck, tip: StickerStrings.HintOutline);
            AddRow(Caption(StickerStrings.LabelOutlineWidth), OutlineWidthNumeric, StickerStrings.HintOutlineWidth, OutlineWidthSlider);

            OutlineColorButton.Width = 64;
            OutlineColorButton.Height = 24;
            OutlineColorButton.FlatStyle = FlatStyle.Flat;
            OutlineColorButton.Margin = new Padding(0, 1, 0, 1);
            OutlineColorButton.TabStop = true;
            AddRow(Caption(StickerStrings.LabelOutlineColor), OutlineColorButton, StickerStrings.HintOutlineColor);
            ShadowCheck.Text = StickerStrings.ShadowCheck;
            ShadowCheck.AutoSize = true;
            AddRow(ShadowCheck, tip: StickerStrings.HintShadow);

            AddHeading(StickerStrings.GroupFile);
            FormatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            FormatCombo.Width = 280;
            FormatCombo.Margin = new Padding(0, 1, 0, 1);
            FormatCombo.Items.AddRange([new Choice<StickerFormat>(StickerFormat.WebP, StickerStrings.FormatWebP), new Choice<StickerFormat>(StickerFormat.Png, StickerStrings.FormatPng)]);
            FormatCombo.SelectedIndex = 0;
            AddRow(Caption(StickerStrings.LabelFormat), tip: StickerStrings.HintFormat);
            AddRow(FormatCombo, tip: StickerStrings.HintFormat);
            AddRow(Caption(StickerStrings.LabelQuality), QualityNumeric, StickerStrings.HintQuality, QualitySlider);

            scroll.Controls.Add(table);
            return scroll;
        }

        private TableLayoutPanel BuildPicturesPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = new Padding(0) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _photoCaption.Text = StickerStrings.PaneCutline;
            _photoCaption.AutoSize = true;
            _photoCaption.Margin = new Padding(0, 6, 0, 4);
            _photoCaption.Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold);

            _stickerCaption.Text = StickerStrings.PaneSticker;
            _stickerCaption.AutoSize = true;
            _stickerCaption.Margin = new Padding(0, 6, 0, 4);
            _stickerCaption.Font = _photoCaption.Font;

            BackgroundCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            BackgroundCombo.Width = 150;
            BackgroundCombo.Margin = new Padding(0, 2, 0, 0);
            BackgroundCombo.Items.AddRange([
                new Choice<StickerBackdrop>(StickerBackdrop.Checker, StickerStrings.BackgroundChecker),
                new Choice<StickerBackdrop>(StickerBackdrop.Dark, StickerStrings.BackgroundDark),
                new Choice<StickerBackdrop>(StickerBackdrop.Light, StickerStrings.BackgroundLight),
            ]);
            BackgroundCombo.SelectedIndex = 0;
            _toolTip.SetToolTip(BackgroundCombo, StickerStrings.HintBackground);

            var stickerHeader = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
            stickerHeader.Controls.Add(_stickerCaption);
            stickerHeader.Controls.Add(new Label { Text = StickerStrings.LabelBackground, AutoSize = true, Margin = new Padding(16, 9, 4, 0) });
            stickerHeader.Controls.Add(BackgroundCombo);

            PhotoPane.Dock = DockStyle.Fill;
            PhotoPane.Margin = new Padding(0, 0, 6, 0);
            PhotoPane.EmptyText = StickerStrings.PaneEmptyPhoto;
            StickerPane.Dock = DockStyle.Fill;
            StickerPane.Margin = new Padding(6, 0, 0, 0);
            StickerPane.ShowBackdrop = true;
            StickerPane.EmptyText = StickerStrings.PaneEmptySticker;

            _infoLabel.AutoSize = true;
            _infoLabel.Margin = new Padding(0, 6, 0, 0);
            _howToLabel.Text = StickerStrings.HowTo;
            _howToLabel.AutoSize = true;
            _howToLabel.Margin = new Padding(0, 2, 0, 0);

            panel.Controls.Add(_photoCaption, 0, 0);
            panel.Controls.Add(stickerHeader, 1, 0);
            panel.Controls.Add(PhotoPane, 0, 1);
            panel.Controls.Add(StickerPane, 1, 1);
            panel.Controls.Add(_infoLabel, 0, 2);
            panel.SetColumnSpan(_infoLabel, 2);
            panel.Controls.Add(_howToLabel, 0, 3);
            panel.SetColumnSpan(_howToLabel, 2);
            return panel;
        }

        private TableLayoutPanel BuildBottom()
        {
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, Margin = new Padding(0) };
            _statusLabel.AutoSize = true;
            _progress.Dock = DockStyle.Top;
            _progress.Height = 8;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Visible = false;
            _progress.Margin = new Padding(0, 4, 12, 0);
            status.Controls.Add(_statusLabel, 0, 0);
            status.Controls.Add(_progress, 0, 1);

            foreach (var (button, text) in new[] { (SaveButton, StickerStrings.Save), (CloseButton, StickerStrings.Close) })
            {
                button.Text = text;
                button.AutoSize = true;
                button.MinimumSize = new Size(110, 28);
                button.Margin = new Padding(4, 0, 0, 0);
            }
            SaveButton.Enabled = false;

            bottom.Controls.Add(status, 0, 0);
            bottom.Controls.Add(SaveButton, 1, 0);
            bottom.Controls.Add(CloseButton, 2, 0);
            return bottom;
        }

        // ── The values of the window ────────────────────────────────────────

        private static T Pick<T>(ComboBox combo, T fallback) => combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

        private static void Select<T>(ComboBox combo, T value)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>Puts the values of a settings file into the window.</summary>
        private void Apply(StickerSettings settings)
        {
            var wasLoading = _loading;
            _loading = true;
            Select(CutoutCombo, settings.Cutout);
            SensitivityNumeric.Value = settings.Sensitivity;
            SmallPartsNumeric.Value = settings.SmallParts;
            FollowEdgesNumeric.Value = settings.FollowEdges;
            EdgeNumeric.Value = settings.EdgeShift;
            OutlineCheck.Checked = settings.Outline;
            OutlineWidthNumeric.Value = settings.OutlineWidth;
            var (red, green, blue) = settings.OutlineRgb;
            SetOutlineColor(Color.FromArgb(red, green, blue));
            ShadowCheck.Checked = settings.Shadow;
            Select(FormatCombo, settings.Format);
            QualityNumeric.Value = settings.Quality;
            _loading = wasLoading;
        }

        /// <summary>Everything the window keeps between two uses, and what the sticker is made by.</summary>
        internal StickerSettings ReadSettings() => new StickerSettings
        {
            Cutout = Pick(CutoutCombo, CutoutMode.Detect),
            Sensitivity = (int)SensitivityNumeric.Value,
            SmallParts = (int)SmallPartsNumeric.Value,
            FollowEdges = (int)FollowEdgesNumeric.Value,
            EdgeShift = (int)EdgeNumeric.Value,
            Outline = OutlineCheck.Checked,
            OutlineWidth = (int)OutlineWidthNumeric.Value,
            OutlineColor = StickerSettings.ColorOf(_outlineColor.R, _outlineColor.G, _outlineColor.B),
            Shadow = ShadowCheck.Checked,
            Format = Pick(FormatCombo, StickerFormat.WebP),
            Quality = (int)QualityNumeric.Value,
        }.Normalized();

        /// <summary>The colour of the outline as the window has it.</summary>
        internal Color OutlineColor => _outlineColor;

        internal StickerFormat Format => Pick(FormatCombo, StickerFormat.WebP);

        private void SetOutlineColor(Color color)
        {
            _outlineColor = Color.FromArgb(color.R, color.G, color.B);
            OutlineColorButton.BackColor = _outlineColor;
            OutlineColorButton.FlatAppearance.BorderColor = _dark ? ThemeManager.DarkBorder : ThemeManager.LightBorder;
            OutlineColorButton.FlatAppearance.MouseOverBackColor = _outlineColor;
            OutlineColorButton.FlatAppearance.MouseDownBackColor = _outlineColor;
        }

        // ── What can be changed when ────────────────────────────────────────

        /// <summary>Makes a slider the other face of a number: moving it sets the number, and a number that is typed or stepped moves it.</summary>
        private void Link(NumericUpDown number, TrackBar slider)
        {
            slider.Minimum = (int)number.Minimum;
            slider.Maximum = (int)number.Maximum;
            slider.SmallChange = 1;
            slider.LargeChange = Math.Max(1, (slider.Maximum - slider.Minimum) / 10);
            slider.Value = Math.Clamp((int)number.Value, slider.Minimum, slider.Maximum);

            number.ValueChanged += (_, _) =>
            {
                if (_syncingSlider)
                    return;

                _syncingSlider = true;
                try
                {
                    slider.Value = Math.Clamp((int)number.Value, slider.Minimum, slider.Maximum);
                }
                finally
                {
                    _syncingSlider = false;
                }
            };
            slider.ValueChanged += (_, _) =>
            {
                if (_syncingSlider)
                    return;

                _syncingSlider = true;
                try
                {
                    number.Value = Math.Clamp(slider.Value, (int)number.Minimum, (int)number.Maximum);
                }
                finally
                {
                    _syncingSlider = false;
                }
            };
            _sliders.Add((number, slider));
        }

        private void WireEvents()
        {
            Link(SensitivityNumeric, SensitivitySlider);
            Link(SmallPartsNumeric, SmallPartsSlider);
            Link(FollowEdgesNumeric, FollowEdgesSlider);
            Link(EdgeNumeric, EdgeSlider);
            Link(OutlineWidthNumeric, OutlineWidthSlider);
            Link(QualityNumeric, QualitySlider);

            foreach (var number in new[] { SensitivityNumeric, SmallPartsNumeric, FollowEdgesNumeric, EdgeNumeric, OutlineWidthNumeric, QualityNumeric })
                number.ValueChanged += (_, _) => SettingChanged();
            foreach (var check in new[] { OutlineCheck, ShadowCheck })
                check.CheckedChanged += (_, _) => SettingChanged();
            FormatCombo.SelectedIndexChanged += (_, _) => SettingChanged();
            CutoutCombo.SelectedIndexChanged += async (_, _) => await CutoutChangedAsync();
            OutlineColorButton.Click += (_, _) => PickOutlineColor();
            BackgroundCombo.SelectedIndexChanged += (_, _) => StickerPane.Backdrop = Pick(BackgroundCombo, StickerBackdrop.Checker);

            _debounce.Tick += async (_, _) =>
            {
                _debounce.Stop();
                await MakeAsync();
            };
            SaveButton.Click += async (_, _) => await SaveAsync();
            CloseButton.Click += (_, _) => Close();
        }

        private void SettingChanged()
        {
            if (_loading)
                return;

            UpdateEnabled();
            _debounce.Stop();
            _debounce.Start();
        }

        /// <summary>Greys what does nothing for the choices that are made.</summary>
        private void UpdateEnabled()
        {
            var detect = Pick(CutoutCombo, CutoutMode.Detect) == CutoutMode.Detect;
            foreach (var control in new Control[] { SensitivityNumeric, SensitivitySlider, SmallPartsNumeric, SmallPartsSlider, FollowEdgesNumeric, FollowEdgesSlider })
                control.Enabled = detect;

            var outline = OutlineCheck.Checked;
            OutlineWidthNumeric.Enabled = OutlineWidthSlider.Enabled = OutlineColorButton.Enabled = outline;
            QualityNumeric.Enabled = QualitySlider.Enabled = Format == StickerFormat.WebP;
        }

        /// <summary>Asks for a colour (the colour window of Windows); a test answers it. Gets the colour it begins with; null is "left as it was".</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<Color, Color?>? PickColor { get; set; }

        private void PickOutlineColor()
        {
            Color? chosen;
            if (PickColor is { } answer)
            {
                chosen = answer(_outlineColor);
            }
            else
            {
                using var dialog = new ColorDialog { Color = _outlineColor, FullOpen = true, AnyColor = true };
                chosen = dialog.ShowDialog(this) == DialogResult.OK ? dialog.Color : null;
            }

            if (chosen is not { } color)
                return;

            SetOutlineColor(color);
            SettingChanged();
        }

        // ── Colours ─────────────────────────────────────────────────────────

        private void ApplyTheme(bool dark)
        {
            ThemeManager.Apply(this, dark);
            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            foreach (var control in All(this))
            {
                if (control is Label or CheckBox or RadioButton)
                    control.BackColor = Color.Transparent;
                if (control is TextBox or NumericUpDown or ComboBox)
                    control.BackColor = inputBack;
            }

            // A slider has no frame of its own, so it takes the colour of the panel it stands on (the theme painted it as an input)
            foreach (var (_, slider) in _sliders)
                slider.BackColor = dark ? ThemeManager.DarkBack : ThemeManager.LightBack;

            var surround = dark ? Color.FromArgb(24, 24, 26) : Color.FromArgb(214, 214, 218);
            foreach (var pane in new[] { PhotoPane, StickerPane })
            {
                pane.Surround = surround;
                pane.ForeColor = SystemColors.GrayText;
                pane.Dark = dark;
            }
            _howToLabel.ForeColor = SystemColors.GrayText;
            _infoLabel.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            SetOutlineColor(_outlineColor);

            foreach (var button in new[] { SaveButton, CloseButton })
            {
                button.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                if (dark)
                    button.FlatAppearance.BorderColor = ThemeManager.DarkBorder;
            }
        }

        private static IEnumerable<Control> All(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (var descendant in All(child))
                    yield return descendant;
        }

        /// <summary>An item of a combo box: the value, shown as its text.</summary>
        private sealed class Choice<T>(T value, string text)
        {
            public T Value { get; } = value;

            public override string ToString() => text;
        }
    }
}
