using System.Drawing.Drawing2D;
using PhotoLiker.TraceBitmap;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Trace Bitmap window: turns the photo that is shown into a vector drawing, an SVG or a DXF file of a size in millimetres. The scans and the options are those of the Trace Bitmap window of Inkscape
    /// (a single scan by a brightness cutoff, the edges or reduced colours; multiple scans by steps of brightness, colours or grays; speckles, corners and the optimizing of paths). The result is shown
    /// next to the photo and can be switched to and from it, zoomed and moved. The work is done by <see cref="BitmapTracer"/>, which has no window; see <see cref="TraceBitmapDialog.Run"/> for the
    /// part that reads, traces and saves.
    /// </summary>
    internal sealed partial class TraceBitmapDialog : Form
    {
        private static readonly int[] DetailChoices = [600, 1000, 1500, 2000, 3000, 4000];

        private readonly string _photoPath;
        private readonly string _settingsFile;
        private readonly bool _dark;
        private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000, InitialDelay = 400 };

        // The scans
        internal readonly RadioButton CutoffRadio = new();
        internal readonly RadioButton EdgesRadio = new();
        internal readonly RadioButton QuantizeRadio = new();
        internal readonly RadioButton StepsRadio = new();
        internal readonly RadioButton ColorsRadio = new();
        internal readonly RadioButton GraysRadio = new();
        internal readonly NumericUpDown CutoffNumeric = Number(0, 1, 0.01m, 3, 0.45m);
        internal readonly NumericUpDown EdgesNumeric = Number(0, 1, 0.01m, 3, 0.65m);
        internal readonly NumericUpDown QuantizeNumeric = Number(TraceSettings.MinScans, TraceSettings.MaxScans, 1, 0, 8);
        internal readonly NumericUpDown StepsNumeric = Number(TraceSettings.MinScans, TraceSettings.MaxScans, 1, 0, 8);
        internal readonly NumericUpDown ColorsNumeric = Number(TraceSettings.MinScans, TraceSettings.MaxScans, 1, 0, 8);
        internal readonly NumericUpDown GraysNumeric = Number(TraceSettings.MinScans, TraceSettings.MaxScans, 1, 0, 8);
        internal readonly CheckBox InvertCheck = new();
        internal readonly CheckBox SmoothCheck = new();
        internal readonly CheckBox StackCheck = new();
        internal readonly CheckBox BackgroundCheck = new();

        // The options
        internal readonly CheckBox SpecklesCheck = new();
        internal readonly NumericUpDown SpecklesNumeric = Number(0, TraceSettings.MaxSpeckleSize, 1, 0, 2);
        internal readonly CheckBox CornersCheck = new();
        internal readonly NumericUpDown CornersNumeric = Number(0, 1.34m, 0.05m, 2, 1.0m);
        internal readonly CheckBox OptimizeCheck = new();
        internal readonly NumericUpDown OptimizeNumeric = Number(0, 5, 0.05m, 2, 0.2m);

        // The sliders that stand beside the numbers: each moves its number, and follows it when the number is typed or stepped
        internal readonly TrackBar CutoffSlider = Slider();
        internal readonly TrackBar EdgesSlider = Slider();
        internal readonly TrackBar QuantizeSlider = Slider();
        internal readonly TrackBar StepsSlider = Slider();
        internal readonly TrackBar ColorsSlider = Slider();
        internal readonly TrackBar GraysSlider = Slider();
        internal readonly TrackBar SpecklesSlider = Slider();
        internal readonly TrackBar CornersSlider = Slider();
        internal readonly TrackBar OptimizeSlider = Slider();
        private readonly List<(NumericUpDown Number, TrackBar Slider)> _sliders = [];
        private bool _syncingSlider;

        // The drawing
        internal readonly ComboBox DetailCombo = new();
        internal readonly NumericUpDown WidthNumeric = Number(0.1m, 100000, 1, 2, 100, 84);
        internal readonly NumericUpDown HeightNumeric = Number(0.1m, 100000, 1, 2, 100, 84);
        internal readonly CheckBox KeepProportionsButton = new();
        internal readonly ComboBox FormatCombo = new();
        internal readonly NumericUpDown AccuracyNumeric = Number(0.001m, 10, 0.01m, 3, 0.02m);
        private readonly Label _accuracyCaption = new();

        // The preview and the buttons
        internal readonly TracePreview Preview = new();
        internal readonly RadioButton ShowPhotoRadio = new();
        internal readonly RadioButton ShowTraceRadio = new();
        internal readonly CheckBox LiveCheck = new();
        internal readonly Button UpdateButton = new();
        internal readonly Button SaveButton = new();
        internal readonly Button CloseButton = new();
        private readonly ProgressBar _progress = new();
        private readonly Label _statusLabel = new();
        private readonly Label _countsLabel = new();
        private readonly Label _hintLabel = new();
        private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 600 };

        private bool _loading = true;

        /// <param name="photoPath">The photo to trace.</param>
        /// <param name="settingsFile">Where the values of the window are kept between two uses.</param>
        public TraceBitmapDialog(string photoPath, bool dark, string settingsFile)
        {
            _photoPath = photoPath;
            _dark = dark;
            _settingsFile = settingsFile;

            Text = $"{TraceStrings.DialogTitle} - {Path.GetFileName(photoPath)}";
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = DialogIcons.Trace;
            StartPosition = FormStartPosition.CenterParent;

            // As big as is useful, but never bigger than the screen it opens on (a laptop has 768 lines): the settings scroll when they do not fit
            var screen = Screen.FromPoint(MousePosition).WorkingArea;
            MinimumSize = new Size(Math.Min(1000, screen.Width - 40), Math.Min(660, screen.Height - 60));
            ClientSize = new Size(Math.Min(1180, screen.Width - 60), Math.Min(740, screen.Height - 100));
            ShowInTaskbar = false;
            MinimizeBox = false;

            var left = BuildSettingsPanel();
            var right = BuildPreviewPanel();
            var bottom = BuildBottom();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);
            Controls.Add(root);

            CancelButton = CloseButton;

            Apply(TraceDialogSettings.Load(settingsFile));
            WireEvents();
            ApplyTheme(dark);
            _loading = false;
            UpdateEnabled();
            SetStatus(TraceStrings.StatusReading);

            Shown += async (_, _) => await OpenAsync();
            FormClosing += (_, _) => OnClosing();
            Disposed += (_, _) =>
            {
                // A form can be disposed twice (when it is closed, and by whoever made it), so nothing here may mind being done again
                _liveTimer.Dispose();
                _cancellation?.Cancel();
                Preview.Photo?.Dispose();
                _toolTip.Dispose();
            };
        }

        /// <summary>The file that was saved, once one was (what the window hands back).</summary>
        public string? SavedPath { get; private set; }

        // ── Building the window ─────────────────────────────────────────────

        private static NumericUpDown Number(decimal min, decimal max, decimal step, int decimals, decimal value, int width = 74) => new()
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            DecimalPlaces = decimals,
            Value = value,
            Width = width,
            Margin = new Padding(0, 1, 0, 1),
            TextAlign = HorizontalAlignment.Right,
        };

        private static TrackBar Slider() => new()
        {
            AutoSize = false,
            Width = 140,
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
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            int row = 0;
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
                    _toolTip.SetToolTip(first, tip);
                    if (second is not null)
                        _toolTip.SetToolTip(second, tip);
                    if (third is not null)
                        _toolTip.SetToolTip(third, tip);
                }

                row++;
            }

            static void Setup(ButtonBase button, string text)
            {
                button.Text = text;
                button.AutoSize = true;
            }

            AddHeading(TraceStrings.GroupSingle);
            Setup(CutoffRadio, TraceStrings.ModeBrightnessCutoff);
            Setup(EdgesRadio, TraceStrings.ModeEdgeDetection);
            Setup(QuantizeRadio, TraceStrings.ModeColorQuantization);
            AddRow(CutoffRadio, CutoffNumeric, TraceStrings.HintBrightnessCutoff, CutoffSlider);
            AddRow(EdgesRadio, EdgesNumeric, TraceStrings.HintEdgeDetection, EdgesSlider);
            AddRow(QuantizeRadio, QuantizeNumeric, TraceStrings.HintColorQuantization, QuantizeSlider);
            Setup(InvertCheck, TraceStrings.InvertImage);
            AddRow(InvertCheck, null, TraceStrings.HintInvertImage);

            AddHeading(TraceStrings.GroupMultiple);
            Setup(StepsRadio, TraceStrings.ModeBrightnessSteps);
            Setup(ColorsRadio, TraceStrings.ModeColors);
            Setup(GraysRadio, TraceStrings.ModeGrays);
            AddRow(StepsRadio, StepsNumeric, TraceStrings.HintBrightnessSteps, StepsSlider);
            AddRow(ColorsRadio, ColorsNumeric, TraceStrings.HintColors, ColorsSlider);
            AddRow(GraysRadio, GraysNumeric, TraceStrings.HintGrays, GraysSlider);
            Setup(SmoothCheck, TraceStrings.Smooth);
            Setup(StackCheck, TraceStrings.StackScans);
            Setup(BackgroundCheck, TraceStrings.RemoveBackground);
            AddRow(SmoothCheck, null, TraceStrings.HintSmooth);
            AddRow(StackCheck, null, TraceStrings.HintStackScans);
            AddRow(BackgroundCheck, null, TraceStrings.HintRemoveBackground);

            AddHeading(TraceStrings.GroupOptions);
            Setup(SpecklesCheck, TraceStrings.SuppressSpeckles);
            Setup(CornersCheck, TraceStrings.SmoothCorners);
            Setup(OptimizeCheck, TraceStrings.OptimizePaths);
            AddRow(SpecklesCheck, SpecklesNumeric, TraceStrings.HintSuppressSpeckles, SpecklesSlider);
            AddRow(CornersCheck, CornersNumeric, TraceStrings.HintSmoothCorners, CornersSlider);
            AddRow(OptimizeCheck, OptimizeNumeric, TraceStrings.HintOptimizePaths, OptimizeSlider);

            AddHeading(TraceStrings.GroupDrawing);
            DetailCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            DetailCombo.Width = 84;
            DetailCombo.Margin = new Padding(0, 1, 0, 1);
            DetailCombo.Items.AddRange([.. DetailChoices.Cast<object>()]);
            AddRow(new Label { Text = TraceStrings.LabelDetail, AutoSize = true, Margin = new Padding(0, 6, 0, 2) }, DetailCombo, TraceStrings.HintDetail);

            // The width, the button that keeps the proportions, and the height, in one row
            var size = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            var widthColumn = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            widthColumn.Controls.Add(new Label { Text = TraceStrings.LabelWidth, AutoSize = true, Margin = new Padding(0, 0, 0, 1) });
            widthColumn.Controls.Add(WidthNumeric);
            var heightColumn = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            heightColumn.Controls.Add(new Label { Text = TraceStrings.LabelHeight, AutoSize = true, Margin = new Padding(0, 0, 0, 1) });
            heightColumn.Controls.Add(HeightNumeric);
            KeepProportionsButton.Appearance = Appearance.Button;
            KeepProportionsButton.AutoSize = false;
            KeepProportionsButton.Size = new Size(30, 26);
            KeepProportionsButton.FlatStyle = FlatStyle.Flat;
            KeepProportionsButton.Margin = new Padding(6, 16, 6, 0);
            KeepProportionsButton.ImageAlign = ContentAlignment.MiddleCenter;
            KeepProportionsButton.Checked = true;
            size.Controls.Add(widthColumn);
            size.Controls.Add(KeepProportionsButton);
            size.Controls.Add(heightColumn);
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(size, 0, row);
            table.SetColumnSpan(size, 3);
            row++;

            FormatCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            FormatCombo.Width = 240;
            FormatCombo.Items.AddRange([new Choice<TraceFileFormat>(TraceFileFormat.Svg, TraceStrings.FormatSvg), new Choice<TraceFileFormat>(TraceFileFormat.Dxf, TraceStrings.FormatDxf)]);
            FormatCombo.SelectedIndex = 0;
            FormatCombo.Margin = new Padding(0, 1, 0, 1);
            var formatCaption = new Label { Text = TraceStrings.LabelFormat, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
            AddRow(formatCaption);
            AddRow(FormatCombo);

            _accuracyCaption.Text = TraceStrings.LabelAccuracy;
            _accuracyCaption.AutoSize = true;
            AddRow(_accuracyCaption, AccuracyNumeric, TraceStrings.HintAccuracy);

            scroll.Controls.Add(table);
            return scroll;
        }

        private TableLayoutPanel BuildPreviewPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            ShowTraceRadio.Text = TraceStrings.ShowTrace;
            ShowTraceRadio.AutoSize = true;
            ShowTraceRadio.Checked = true;
            ShowPhotoRadio.Text = TraceStrings.ShowPhoto;
            ShowPhotoRadio.AutoSize = true;
            ShowTraceRadio.Appearance = ShowPhotoRadio.Appearance = Appearance.Button;
            ShowTraceRadio.FlatStyle = ShowPhotoRadio.FlatStyle = FlatStyle.Flat;
            ShowTraceRadio.Padding = ShowPhotoRadio.Padding = new Padding(10, 2, 10, 2);
            ShowTraceRadio.Margin = ShowPhotoRadio.Margin = new Padding(0, 0, 4, 0);
            top.Controls.Add(ShowTraceRadio);
            top.Controls.Add(ShowPhotoRadio);

            _hintLabel.Text = TraceStrings.PreviewHint;
            _hintLabel.AutoSize = true;
            _hintLabel.Margin = new Padding(14, 6, 0, 0);
            top.Controls.Add(_hintLabel);

            Preview.Dock = DockStyle.Fill;
            Preview.Margin = new Padding(0);

            _countsLabel.AutoSize = true;
            _countsLabel.Margin = new Padding(0, 4, 0, 0);

            panel.Controls.Add(top, 0, 0);
            panel.Controls.Add(Preview, 0, 1);
            panel.Controls.Add(_countsLabel, 0, 2);
            return panel;
        }

        private TableLayoutPanel BuildBottom()
        {
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 4; i++)
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

            LiveCheck.Text = TraceStrings.LivePreview;
            LiveCheck.AutoSize = true;
            LiveCheck.Margin = new Padding(8, 6, 8, 0);
            _toolTip.SetToolTip(LiveCheck, TraceStrings.HintLivePreview);

            foreach (var (button, text) in new[] { (UpdateButton, TraceStrings.Update), (SaveButton, TraceStrings.Save), (CloseButton, TraceStrings.Close) })
            {
                button.Text = text;
                button.AutoSize = true;
                button.MinimumSize = new Size(88, 28);
                button.Margin = new Padding(4, 0, 0, 0);
            }

            bottom.Controls.Add(status, 0, 0);
            bottom.Controls.Add(LiveCheck, 1, 0);
            bottom.Controls.Add(UpdateButton, 2, 0);
            bottom.Controls.Add(SaveButton, 3, 0);
            bottom.Controls.Add(CloseButton, 4, 0);
            return bottom;
        }

        // ── The values of the window ────────────────────────────────────────

        private static int NearestDetail(int detail) => DetailChoices.OrderBy(choice => Math.Abs(choice - detail)).First();

        private static T Pick<T>(ComboBox combo, T fallback) => combo.SelectedItem is Choice<T> choice ? choice.Value : fallback;

        /// <summary>Puts the values of a settings file into the window.</summary>
        private void Apply(TraceDialogSettings settings)
        {
            var wasLoading = _loading;
            _loading = true;
            var trace = settings.Trace;
            switch (trace.Mode)
            {
                case TraceMode.EdgeDetection: EdgesRadio.Checked = true; break;
                case TraceMode.ColorQuantization: QuantizeRadio.Checked = true; break;
                case TraceMode.BrightnessSteps: StepsRadio.Checked = true; break;
                case TraceMode.Colors: ColorsRadio.Checked = true; break;
                case TraceMode.Grays: GraysRadio.Checked = true; break;
                default: CutoffRadio.Checked = true; break;
            }

            CutoffNumeric.Value = (decimal)trace.BrightnessCutoff;
            EdgesNumeric.Value = (decimal)trace.EdgeThreshold;
            QuantizeNumeric.Value = trace.QuantizeColors;
            StepsNumeric.Value = trace.BrightnessSteps;
            ColorsNumeric.Value = trace.Colors;
            GraysNumeric.Value = trace.Grays;
            InvertCheck.Checked = trace.InvertImage;
            SmoothCheck.Checked = trace.Smooth;
            StackCheck.Checked = trace.StackScans;
            BackgroundCheck.Checked = trace.RemoveBackground;
            SpecklesCheck.Checked = trace.SuppressSpeckles;
            SpecklesNumeric.Value = trace.SpeckleSize;
            CornersCheck.Checked = trace.SmoothCorners;
            CornersNumeric.Value = Math.Min(CornersNumeric.Maximum, (decimal)trace.CornerThreshold);
            OptimizeCheck.Checked = trace.OptimizePaths;
            OptimizeNumeric.Value = (decimal)trace.OptimizeTolerance;

            DetailCombo.SelectedItem = NearestDetail(settings.Detail);
            FormatCombo.SelectedIndex = settings.Format == TraceFileFormat.Dxf ? 1 : 0;
            AccuracyNumeric.Value = (decimal)settings.CurveAccuracyMillimetres;
            KeepProportionsButton.Checked = settings.KeepProportions;
            LiveCheck.Checked = settings.LivePreview;
            _longestSide = settings.LongestSideMillimetres;
            WidthNumeric.Value = (decimal)Math.Clamp(settings.LongestSideMillimetres, (double)WidthNumeric.Minimum, (double)WidthNumeric.Maximum);
            HeightNumeric.Value = WidthNumeric.Value;
            _loading = wasLoading;
        }

        private double _longestSide = TraceDialogSettings.DefaultLongestSideMillimetres;

        /// <summary>The settings of the scan as the window has them now.</summary>
        internal TraceSettings ReadTrace() => new()
        {
            Mode = CutoffRadio.Checked ? TraceMode.BrightnessCutoff
                : EdgesRadio.Checked ? TraceMode.EdgeDetection
                : QuantizeRadio.Checked ? TraceMode.ColorQuantization
                : StepsRadio.Checked ? TraceMode.BrightnessSteps
                : ColorsRadio.Checked ? TraceMode.Colors
                : TraceMode.Grays,
            BrightnessCutoff = (double)CutoffNumeric.Value,
            EdgeThreshold = (double)EdgesNumeric.Value,
            QuantizeColors = (int)QuantizeNumeric.Value,
            BrightnessSteps = (int)StepsNumeric.Value,
            Colors = (int)ColorsNumeric.Value,
            Grays = (int)GraysNumeric.Value,
            InvertImage = InvertCheck.Checked,
            Smooth = SmoothCheck.Checked,
            StackScans = StackCheck.Checked,
            RemoveBackground = BackgroundCheck.Checked,
            SuppressSpeckles = SpecklesCheck.Checked,
            SpeckleSize = (int)SpecklesNumeric.Value,
            SmoothCorners = CornersCheck.Checked,
            CornerThreshold = (double)CornersNumeric.Value,
            OptimizePaths = OptimizeCheck.Checked,
            OptimizeTolerance = (double)OptimizeNumeric.Value,
        };

        /// <summary>The longest side, in pixels, the photo is traced at.</summary>
        internal int Detail => DetailCombo.SelectedItem is int detail ? detail : TraceDialogSettings.DefaultDetail;

        internal TraceFileFormat Format => Pick(FormatCombo, TraceFileFormat.Svg);

        /// <summary>Everything the window keeps between two uses.</summary>
        internal TraceDialogSettings ReadSettings() => new()
        {
            Trace = ReadTrace(),
            Detail = Detail,
            LongestSideMillimetres = _longestSide,
            Format = Format,
            CurveAccuracyMillimetres = (double)AccuracyNumeric.Value,
            KeepProportions = KeepProportionsButton.Checked,
            LivePreview = LiveCheck.Checked,
        };

        /// <summary>The size of the drawing that is saved, in millimetres.</summary>
        internal (double Width, double Height) DrawingSize => ((double)WidthNumeric.Value, (double)HeightNumeric.Value);

        // ── What can be changed when ────────────────────────────────────────

        /// <summary>
        /// Makes a slider the other face of a number: the slider has whole steps of 1/<paramref name="scale"/> (a number of 0 to 1 with a scale of 100 is 0 to 100 on the slider), it runs up to
        /// <paramref name="sliderMax"/> (which may be less than what can be typed: the slider of the speckle size ends at 100, the number goes on to 1000), moving it sets the number, and a number
        /// that is typed or stepped moves it (to its end, when the number is beyond it, without changing the number).
        /// </summary>
        private void Link(NumericUpDown number, TrackBar slider, decimal sliderMax, decimal scale)
        {
            slider.Minimum = (int)Math.Round(number.Minimum * scale);
            slider.Maximum = (int)Math.Round(sliderMax * scale);
            slider.SmallChange = 1;
            slider.LargeChange = Math.Max(1, (slider.Maximum - slider.Minimum) / 10);
            slider.Value = OnSlider(number, slider, scale);

            number.ValueChanged += (_, _) =>
            {
                if (_syncingSlider)
                    return;

                _syncingSlider = true;
                try
                {
                    slider.Value = OnSlider(number, slider, scale);
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
                    number.Value = Math.Clamp(Math.Round(slider.Value / scale, number.DecimalPlaces), number.Minimum, number.Maximum);
                }
                finally
                {
                    _syncingSlider = false;
                }
            };
            _sliders.Add((number, slider));
        }

        private static int OnSlider(NumericUpDown number, TrackBar slider, decimal scale) =>
            Math.Clamp((int)Math.Round(number.Value * scale), slider.Minimum, slider.Maximum);

        private void LinkSliders()
        {
            Link(CutoffNumeric, CutoffSlider, 1m, 100m);
            Link(EdgesNumeric, EdgesSlider, 1m, 100m);
            Link(QuantizeNumeric, QuantizeSlider, QuantizeNumeric.Maximum, 1m);
            Link(StepsNumeric, StepsSlider, StepsNumeric.Maximum, 1m);
            Link(ColorsNumeric, ColorsSlider, ColorsNumeric.Maximum, 1m);
            Link(GraysNumeric, GraysSlider, GraysNumeric.Maximum, 1m);
            Link(SpecklesNumeric, SpecklesSlider, 100m, 1m);
            Link(CornersNumeric, CornersSlider, CornersNumeric.Maximum, 100m);
            Link(OptimizeNumeric, OptimizeSlider, OptimizeNumeric.Maximum, 100m);
        }

        private void WireEvents()
        {
            LinkSliders();
            foreach (var radio in new[] { CutoffRadio, EdgesRadio, QuantizeRadio, StepsRadio, ColorsRadio, GraysRadio })
                radio.CheckedChanged += (_, _) => SettingChanged();
            foreach (var number in new[] { CutoffNumeric, EdgesNumeric, QuantizeNumeric, StepsNumeric, ColorsNumeric, GraysNumeric, SpecklesNumeric, CornersNumeric, OptimizeNumeric })
                number.ValueChanged += (_, _) => SettingChanged();
            foreach (var check in new[] { InvertCheck, SmoothCheck, StackCheck, BackgroundCheck, SpecklesCheck, CornersCheck, OptimizeCheck })
                check.CheckedChanged += (_, _) => SettingChanged();

            DetailCombo.SelectedIndexChanged += async (_, _) => await DetailChangedAsync();
            WidthNumeric.ValueChanged += (_, _) => DrawingSizeEdited(widthChanged: true);
            HeightNumeric.ValueChanged += (_, _) => DrawingSizeEdited(widthChanged: false);
            KeepProportionsButton.CheckedChanged += (_, _) => ProportionsChanged();
            FormatCombo.SelectedIndexChanged += (_, _) => UpdateEnabled();

            ShowTraceRadio.CheckedChanged += (_, _) => Preview.ShowTrace = ShowTraceRadio.Checked;
            LiveCheck.CheckedChanged += (_, _) =>
            {
                if (LiveCheck.Checked && IsStale)
                    RestartLiveTimer();
            };
            _liveTimer.Tick += async (_, _) =>
            {
                _liveTimer.Stop();
                await TraceNowAsync();
            };
            UpdateButton.Click += async (_, _) => await TraceNowAsync();
            SaveButton.Click += async (_, _) => await SaveAsync();
            CloseButton.Click += (_, _) => Close();
        }

        private void SettingChanged()
        {
            if (_loading)
                return;

            UpdateEnabled();
            if (_trace is not null)
                SetStatus(IsStale ? TraceStrings.StatusStale : StatusOfTrace());
            if (LiveCheck.Checked)
                RestartLiveTimer();
        }

        private void RestartLiveTimer()
        {
            _liveTimer.Stop();
            _liveTimer.Start();
        }

        /// <summary>Greys what does nothing in the mode that is picked, as Inkscape's window does.</summary>
        private void UpdateEnabled()
        {
            CutoffNumeric.Enabled = CutoffRadio.Checked;
            EdgesNumeric.Enabled = EdgesRadio.Checked;
            QuantizeNumeric.Enabled = QuantizeRadio.Checked;
            StepsNumeric.Enabled = StepsRadio.Checked;
            ColorsNumeric.Enabled = ColorsRadio.Checked;
            GraysNumeric.Enabled = GraysRadio.Checked;

            var multiple = StepsRadio.Checked || ColorsRadio.Checked || GraysRadio.Checked;
            InvertCheck.Enabled = !multiple;
            SmoothCheck.Enabled = StackCheck.Enabled = BackgroundCheck.Enabled = multiple;

            SpecklesNumeric.Enabled = SpecklesCheck.Checked;
            CornersNumeric.Enabled = CornersCheck.Checked;
            OptimizeNumeric.Enabled = OptimizeCheck.Checked;
            foreach (var (number, slider) in _sliders)
                slider.Enabled = number.Enabled;

            var dxf = Format == TraceFileFormat.Dxf;
            _accuracyCaption.Enabled = AccuracyNumeric.Enabled = dxf;
        }

        // ── The size ────────────────────────────────────────────────────────

        private bool _changingSize;

        /// <summary>The width over the height of the photo, which the drawing keeps while the proportions are kept.</summary>
        private double Aspect => _image is { } image ? image.Width / (double)image.Height : 1.0;

        private void DrawingSizeEdited(bool widthChanged)
        {
            if (_loading || _changingSize)
                return;

            _changingSize = true;
            try
            {
                if (KeepProportionsButton.Checked && _image is not null)
                {
                    if (widthChanged)
                        HeightNumeric.Value = Clamp(WidthNumeric.Value / (decimal)Aspect, HeightNumeric);
                    else
                        WidthNumeric.Value = Clamp(HeightNumeric.Value * (decimal)Aspect, WidthNumeric);
                }
            }
            finally
            {
                _changingSize = false;
            }

            _longestSide = (double)Math.Max(WidthNumeric.Value, HeightNumeric.Value);
        }

        private static decimal Clamp(decimal value, NumericUpDown box) => Math.Clamp(Math.Round(value, box.DecimalPlaces), box.Minimum, box.Maximum);

        private void ProportionsChanged()
        {
            UpdateProportionsImage();
            if (_loading)
                return;

            // Keeping them again: the height follows the width
            if (KeepProportionsButton.Checked)
                DrawingSizeEdited(widthChanged: true);
        }

        /// <summary>Sets the drawing to a longest side in millimetres, in the proportions of the photo.</summary>
        private void SetDefaultSize()
        {
            _changingSize = true;
            try
            {
                var aspect = Aspect;
                var longest = (decimal)_longestSide;
                var width = aspect >= 1 ? longest : longest * (decimal)aspect;
                var height = aspect >= 1 ? longest / (decimal)aspect : longest;
                WidthNumeric.Value = Clamp(width, WidthNumeric);
                HeightNumeric.Value = Clamp(height, HeightNumeric);
            }
            finally
            {
                _changingSize = false;
            }
        }

        private void UpdateProportionsImage()
        {
            var old = KeepProportionsButton.Image;
            KeepProportionsButton.Image = ChainImage(KeepProportionsButton.Checked, _dark ? ThemeManager.DarkFore : ThemeManager.LightFore);
            old?.Dispose();
            _toolTip.SetToolTip(KeepProportionsButton, KeepProportionsButton.Checked ? TraceStrings.HintKeepProportions : TraceStrings.HintDoNotKeepProportions);
        }

        /// <summary>A chain of two links; with a gap in the middle when the proportions are not kept.</summary>
        internal static Bitmap ChainImage(bool linked, Color color)
        {
            var bitmap = new Bitmap(20, 20);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.8f);
            if (linked)
            {
                g.DrawArc(pen, 2f, 6f, 10f, 8f, 0, 360);
                g.DrawArc(pen, 8f, 6f, 10f, 8f, 0, 360);
            }
            else
            {
                g.DrawArc(pen, 1.5f, 6f, 8f, 8f, 0, 360);
                g.DrawArc(pen, 10.5f, 6f, 8f, 8f, 0, 360);
            }

            return bitmap;
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

            Preview.Surround = dark ? Color.FromArgb(24, 24, 26) : Color.FromArgb(214, 214, 218);
            _hintLabel.ForeColor = SystemColors.GrayText;
            _countsLabel.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;

            // The two buttons that switch the preview look pressed when they are on
            foreach (var radio in new[] { ShowTraceRadio, ShowPhotoRadio })
            {
                radio.FlatAppearance.BorderColor = dark ? ThemeManager.DarkBorder : ThemeManager.LightBorder;
                radio.FlatAppearance.CheckedBackColor = dark ? ThemeManager.DarkHighlight : ThemeManager.LightHighlight;
                radio.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
                radio.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            }

            KeepProportionsButton.FlatAppearance.BorderColor = dark ? ThemeManager.DarkBorder : ThemeManager.LightBorder;
            KeepProportionsButton.FlatAppearance.CheckedBackColor = dark ? ThemeManager.DarkHighlight : ThemeManager.LightHighlight;
            KeepProportionsButton.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            UpdateProportionsImage();

            foreach (var button in new[] { UpdateButton, SaveButton, CloseButton })
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
