using System.ComponentModel;
using PhotoLiker.VideoToGif;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Video to GIF window: an animated GIF of a part of a video. The part is picked by the first and the last frame (all of the video unless changed), each with a picture of that frame, and the GIF is
    /// made as small or as good as asked (frames a second, width, colours, dithering, repeats). What the GIF will be (its frames and size) is told before it is made; when it is made it is shown, playing,
    /// beside the two frames. The work is done by <see cref="GifMaker"/>, which has no window; see <see cref="VideoToGifDialog.Run"/> for the part that reads the video and makes the GIF.
    /// </summary>
    internal sealed partial class VideoToGifDialog : Form
    {
        private readonly string? _firstVideo;
        private readonly string _settingsFile;
        private readonly bool _dark;
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };

        // The video
        internal readonly TextBox VideoBox = new();
        internal readonly Button BrowseButton = new();
        private readonly Label _infoLabel = new();

        // The frames
        internal readonly TrackBar FirstSlider = Slider();
        internal readonly TrackBar LastSlider = Slider();
        internal readonly NumericUpDown FirstNumeric = Number(1, 1, 1, 1);
        internal readonly NumericUpDown LastNumeric = Number(1, 1, 1, 1);
        internal readonly Label FirstTimeLabel = new();
        internal readonly Label LastTimeLabel = new();
        internal readonly Button AllFramesButton = new();
        internal readonly PreviewBox FirstPicture = new();
        internal readonly PreviewBox LastPicture = new();
        internal readonly PreviewBox GifPicture = new();

        // The GIF
        internal readonly NumericUpDown FrameEveryNumeric = Number((decimal)GifSettings.MinFrameSeconds, (decimal)GifSettings.MaxFrameSeconds, 0.05m, (decimal)GifSettings.DefaultFrameSeconds, 64, decimals: 2);
        internal readonly NumericUpDown FramePlaysNumeric = Number((decimal)GifSettings.MinFrameSeconds, (decimal)GifSettings.MaxFrameSeconds, 0.05m, (decimal)GifSettings.DefaultFrameSeconds, 64, decimals: 2);
        internal readonly CheckBox VideoSpeedCheck = new();
        internal readonly NumericUpDown WidthNumeric = Number(GifSettings.MinWidth, GifSettings.MaxWidth, 10, GifSettings.DefaultWidth, 70);
        internal readonly CheckBox OriginalCheck = new();
        internal readonly NumericUpDown ColorsNumeric = Number(GifSettings.MinColors, GifSettings.MaxColors, 1, GifSettings.MaxColors);
        internal readonly ComboBox DitherCombo = new();
        internal readonly ComboBox RepeatCombo = new();
        private readonly Label _summaryLabel = new();

        // The work
        internal readonly Button MakeButton = new();
        internal readonly Button StopButton = new();
        internal readonly Button CloseButton = new();
        private readonly ProgressBar _progress = new();
        private readonly Label _statusLabel = new();
        private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 250 };

        private bool _loading = true;
        private bool _syncing;

        /// <param name="videoPath">The video to make the GIF of; null opens the window empty and asks for one.</param>
        /// <param name="settingsFile">Where the values of the window are kept between two uses.</param>
        public VideoToGifDialog(string? videoPath, bool dark, string settingsFile)
        {
            _firstVideo = videoPath;
            _dark = dark;
            _settingsFile = settingsFile;

            Text = VideoToGifStrings.DialogTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = DialogIcons.VideoToGif;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            // Big enough for three pictures side by side, but never bigger than the screen it opens on (a laptop has 768 lines)
            var screen = Screen.FromPoint(MousePosition).WorkingArea;
            MinimumSize = new Size(Math.Min(940, screen.Width - 40), Math.Min(660, screen.Height - 60));
            ClientSize = new Size(Math.Min(1060, screen.Width - 60), Math.Min(740, screen.Height - 100));

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(BuildVideoRow(), 0, 0);
            root.Controls.Add(BuildRangeRows(), 0, 1);
            root.Controls.Add(BuildPictures(), 0, 2);
            root.Controls.Add(BuildOptions(), 0, 3);
            root.Controls.Add(_summaryLabel, 0, 4);
            root.Controls.Add(BuildBottom(), 0, 5);
            Controls.Add(root);

            CancelButton = CloseButton;
            _summaryLabel.AutoSize = true;
            _summaryLabel.Margin = new Padding(0, 2, 0, 4);

            Apply(GifSettings.Load(settingsFile));
            WireEvents();
            ApplyTheme(dark);
            _loading = false;
            SetNoVideo();

            Shown += async (_, _) => await OpenAsync();
            FormClosing += (_, _) => OnClosing();
            Disposed += (_, _) =>
            {
                // A form can be disposed twice (when it is closed, and by whoever made it), so nothing here may mind being done again
                _previewTimer.Dispose();
                _toolTip.Dispose();
                _making?.Cancel();
                ReleasePictures();
            };
        }

        /// <summary>The GIF that was made, once one was (what the window hands back).</summary>
        public string? SavedPath { get; private set; }

        // ── Building the window ─────────────────────────────────────────────

        private static NumericUpDown Number(decimal min, decimal max, decimal step, decimal value, int width = 56, int decimals = 0) => new()
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            DecimalPlaces = decimals,
            Value = value,
            Width = width,
            Margin = new Padding(0, 1, 6, 1),
            TextAlign = HorizontalAlignment.Right,
        };

        private static TrackBar Slider() => new()
        {
            AutoSize = false,
            Height = 28,
            Dock = DockStyle.Fill,
            TickStyle = TickStyle.None,
            Margin = new Padding(4, 0, 8, 0),
            Minimum = 1,
            Maximum = 1,
        };

        private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 8, 2) };

        private static Label Heading(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 3),
        };

        private TableLayoutPanel BuildVideoRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            VideoBox.ReadOnly = true;
            VideoBox.Dock = DockStyle.Fill;
            VideoBox.Margin = new Padding(0, 2, 6, 2);
            BrowseButton.Text = VideoToGifStrings.Browse;
            BrowseButton.AutoSize = true;
            BrowseButton.MinimumSize = new Size(88, 26);
            BrowseButton.Margin = new Padding(0, 1, 0, 1);
            _toolTip.SetToolTip(BrowseButton, VideoToGifStrings.HintBrowse);
            _infoLabel.AutoSize = true;
            _infoLabel.Margin = new Padding(0, 2, 0, 2);

            row.Controls.Add(Caption(VideoToGifStrings.LabelVideo), 0, 0);
            row.Controls.Add(VideoBox, 1, 0);
            row.Controls.Add(BrowseButton, 2, 0);
            row.Controls.Add(_infoLabel, 1, 1);
            return row;
        }

        private TableLayoutPanel BuildRangeRows()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Margin = new Padding(0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var heading = Heading(VideoToGifStrings.GroupRange);
            table.Controls.Add(heading, 0, 0);
            table.SetColumnSpan(heading, 5);

            AddRangeRow(table, 1, VideoToGifStrings.LabelFirst, FirstSlider, FirstNumeric, FirstTimeLabel, VideoToGifStrings.HintFirst);
            AddRangeRow(table, 2, VideoToGifStrings.LabelLast, LastSlider, LastNumeric, LastTimeLabel, VideoToGifStrings.HintLast);

            AllFramesButton.Text = VideoToGifStrings.AllFrames;
            AllFramesButton.AutoSize = true;
            AllFramesButton.MinimumSize = new Size(88, 26);
            AllFramesButton.Margin = new Padding(0, 1, 0, 1);
            _toolTip.SetToolTip(AllFramesButton, VideoToGifStrings.HintAllFrames);
            table.Controls.Add(AllFramesButton, 4, 1);
            table.SetRowSpan(AllFramesButton, 2);
            AllFramesButton.Anchor = AnchorStyles.None;
            return table;
        }

        private void AddRangeRow(TableLayoutPanel table, int row, string caption, TrackBar slider, NumericUpDown number, Label time, string tip)
        {
            table.Controls.Add(Caption(caption), 0, row);
            table.Controls.Add(slider, 1, row);
            table.Controls.Add(number, 2, row);
            time.AutoSize = true;
            time.Anchor = AnchorStyles.Left;
            time.Margin = new Padding(0, 2, 0, 2);
            table.Controls.Add(time, 3, row);
            _toolTip.SetToolTip(slider, tip);
            _toolTip.SetToolTip(number, tip);
        }

        private TableLayoutPanel BuildPictures()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 6, 0, 0) };
            for (var i = 0; i < 3; i++)
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            panel.Controls.Add(Heading(VideoToGifStrings.LabelFirst), 0, 0);
            panel.Controls.Add(Heading(VideoToGifStrings.LabelLast), 1, 0);
            panel.Controls.Add(Heading(VideoToGifStrings.ResultHeading), 2, 0);

            FirstPicture.Placeholder = LastPicture.Placeholder = VideoToGifStrings.PreviewNone;
            GifPicture.Placeholder = VideoToGifStrings.ResultPlaceholder;
            var column = 0;
            foreach (var picture in new[] { FirstPicture, LastPicture, GifPicture })
            {
                picture.Dock = DockStyle.Fill;
                picture.Margin = new Padding(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0);
                panel.Controls.Add(picture, column++, 1);
            }

            return panel;
        }

        // The heading, and beside it two lines of options (the lines are made on purpose: a line that is left to wrap breaks where the texts of a language happen to end)
        private TableLayoutPanel BuildOptions()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 6, 0, 0) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var first = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var second = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var third = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var heading = Heading(VideoToGifStrings.GroupGif);
            heading.Margin = new Padding(0, 4, 24, 0);

            DitherCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            DitherCombo.Width = 170;
            DitherCombo.Margin = new Padding(0, 1, 14, 1);
            DitherCombo.Items.AddRange(
            [
                new Choice<GifDither>(GifDither.None, VideoToGifStrings.DitherNone),
                new Choice<GifDither>(GifDither.Bayer, VideoToGifStrings.DitherBayer),
                new Choice<GifDither>(GifDither.Sierra, VideoToGifStrings.DitherSierra),
            ]);
            RepeatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            RepeatCombo.Width = 90;
            RepeatCombo.Margin = new Padding(0, 1, 0, 1);
            RepeatCombo.Items.AddRange([new Choice<GifLoop>(GifLoop.Forever, VideoToGifStrings.RepeatForever), new Choice<GifLoop>(GifLoop.Once, VideoToGifStrings.RepeatOnce)]);

            OriginalCheck.Text = VideoToGifStrings.OriginalSize;
            OriginalCheck.AutoSize = true;
            OriginalCheck.Margin = new Padding(0, 3, 14, 0);

            VideoSpeedCheck.Text = VideoToGifStrings.VideoSpeed;
            VideoSpeedCheck.AutoSize = true;
            VideoSpeedCheck.Margin = new Padding(0, 3, 14, 0);

            // The frames: how far apart they are taken, how long each plays (which is the speed of the GIF), and the tick that makes the two the same
            AddOption(first, VideoToGifStrings.LabelFrameEvery, FrameEveryNumeric, VideoToGifStrings.HintFrameEvery);
            AddOption(first, VideoToGifStrings.LabelFramePlays, FramePlaysNumeric, VideoToGifStrings.HintFramePlays);
            first.Controls.Add(VideoSpeedCheck);
            _toolTip.SetToolTip(VideoSpeedCheck, VideoToGifStrings.HintVideoSpeed);
            AddOption(second, VideoToGifStrings.LabelWidth, WidthNumeric, VideoToGifStrings.HintWidth);
            second.Controls.Add(OriginalCheck);
            _toolTip.SetToolTip(OriginalCheck, VideoToGifStrings.HintOriginalSize);
            AddOption(third, VideoToGifStrings.LabelColors, ColorsNumeric, VideoToGifStrings.HintColors);
            AddOption(third, VideoToGifStrings.LabelDither, DitherCombo, VideoToGifStrings.HintDither);
            AddOption(third, VideoToGifStrings.LabelRepeat, RepeatCombo, VideoToGifStrings.HintRepeat);

            table.Controls.Add(heading, 0, 0);
            table.SetRowSpan(heading, 3);
            table.Controls.Add(first, 1, 0);
            table.Controls.Add(second, 1, 1);
            table.Controls.Add(third, 1, 2);
            return table;
        }

        private void AddOption(FlowLayoutPanel flow, string caption, Control box, string tip)
        {
            var label = new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 5, 6, 0) };
            flow.Controls.Add(label);
            flow.Controls.Add(box);
            _toolTip.SetToolTip(label, tip);
            _toolTip.SetToolTip(box, tip);
        }

        private TableLayoutPanel BuildBottom()
        {
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 3; i++)
                bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, Margin = new Padding(0) };
            _statusLabel.AutoSize = true;
            _progress.Dock = DockStyle.Top;
            _progress.Height = 8;
            _progress.Minimum = 0;
            _progress.Maximum = 1000;
            _progress.Visible = false;
            _progress.Margin = new Padding(0, 4, 12, 0);
            status.Controls.Add(_statusLabel, 0, 0);
            status.Controls.Add(_progress, 0, 1);

            foreach (var (button, text) in new[] { (MakeButton, VideoToGifStrings.Make), (StopButton, VideoToGifStrings.Stop), (CloseButton, VideoToGifStrings.Close) })
            {
                button.Text = text;
                button.AutoSize = true;
                button.MinimumSize = new Size(96, 28);
                button.Margin = new Padding(4, 0, 0, 0);
            }

            StopButton.Enabled = false;
            _toolTip.SetToolTip(MakeButton, VideoToGifStrings.HintMake);

            bottom.Controls.Add(status, 0, 0);
            bottom.Controls.Add(MakeButton, 1, 0);
            bottom.Controls.Add(StopButton, 2, 0);
            bottom.Controls.Add(CloseButton, 3, 0);
            return bottom;
        }

        // ── The values of the window ────────────────────────────────────────

        private static T Pick<T>(ComboBox combo, T fallback) => combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

        private static void Select<T>(ComboBox combo, T value)
        {
            foreach (var item in combo.Items)
            {
                if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        /// <summary>Puts the values of a settings file into the window.</summary>
        private void Apply(GifSettings settings)
        {
            var wasLoading = _loading;
            _loading = true;
            FrameEveryNumeric.Value = Math.Clamp((decimal)settings.FrameSeconds, FrameEveryNumeric.Minimum, FrameEveryNumeric.Maximum);
            VideoSpeedCheck.Checked = settings.DelaySeconds is null;
            FramePlaysNumeric.Value = Math.Clamp((decimal)(settings.DelaySeconds ?? settings.FrameSeconds), FramePlaysNumeric.Minimum, FramePlaysNumeric.Maximum);
            FramePlaysNumeric.Enabled = !VideoSpeedCheck.Checked;
            WidthNumeric.Value = settings.Width;
            OriginalCheck.Checked = settings.OriginalSize;
            ColorsNumeric.Value = settings.Colors;
            Select(DitherCombo, settings.Dither);
            Select(RepeatCombo, settings.Loop);
            WidthNumeric.Enabled = !settings.OriginalSize;
            _loading = wasLoading;
        }

        /// <summary>The settings of the GIF as the window has them now.</summary>
        internal GifSettings ReadSettings() => new()
        {
            FrameSeconds = (double)FrameEveryNumeric.Value,
            DelaySeconds = VideoSpeedCheck.Checked ? null : (double)FramePlaysNumeric.Value,
            Width = (int)WidthNumeric.Value,
            OriginalSize = OriginalCheck.Checked,
            Colors = (int)ColorsNumeric.Value,
            Dither = Pick(DitherCombo, GifDither.Sierra),
            Loop = Pick(RepeatCombo, GifLoop.Forever),
        };

        // ── Colours ─────────────────────────────────────────────────────────

        private void ApplyTheme(bool dark)
        {
            ThemeManager.Apply(this, dark);
            var inputBack = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            foreach (var control in All(this))
            {
                if (control is Label or CheckBox)
                    control.BackColor = Color.Transparent;
                if (control is TextBox or NumericUpDown or ComboBox)
                    control.BackColor = inputBack;
            }

            // A slider has no frame of its own, so it takes the colour of the panel it stands on (the theme painted it as an input)
            foreach (var slider in new[] { FirstSlider, LastSlider })
                slider.BackColor = dark ? ThemeManager.DarkBack : ThemeManager.LightBack;

            foreach (var picture in new[] { FirstPicture, LastPicture, GifPicture })
            {
                picture.BackColor = dark ? Color.FromArgb(24, 24, 26) : Color.FromArgb(214, 214, 218);
                picture.ForeColor = SystemColors.GrayText;
            }

            _infoLabel.ForeColor = SystemColors.GrayText;
            foreach (var button in new[] { BrowseButton, AllFramesButton, MakeButton, StopButton, CloseButton })
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

        /// <summary>A picture that shows a few words (<see cref="Placeholder"/>) in the middle while it has none, and the picture as big as it fits otherwise.</summary>
        internal sealed class PreviewBox : PictureBox
        {
            public PreviewBox() => SizeMode = PictureBoxSizeMode.Zoom;

            [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string Placeholder { get; set; } = string.Empty;

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Image is null && Placeholder.Length > 0)
                    TextRenderer.DrawText(e.Graphics, Placeholder, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        }
    }
}
