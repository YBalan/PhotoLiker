using System.ComponentModel;

namespace PhotoLikerUI
{
    /// <summary>
    /// The bar that floats over the photo in the viewer while it is being refocused (see <see cref="DepthOfField"/>): the blur, the sharp zone and the haze as sliders, a button that saves a copy, and ✕ that
    /// closes it. Where the focus is, is told by a click on the photo itself. It only holds the controls and tells what the person did; <see cref="MainForm"/> does the work (see MainForm.RefocusBar.cs).
    /// It never takes the keyboard: whatever the person presses on it, the owner gives the keys back to the viewer (<see cref="Used"/>).
    /// </summary>
    internal sealed class RefocusBar : Panel
    {
        public const int Maximum = 100;

        private readonly Label _grip = new() { Text = "⋮⋮", AutoSize = false, Size = new Size(18, 26), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.SizeAll, Margin = new Padding(0, 0, 2, 0) };
        private readonly Label _title = new() { AutoSize = false, Size = new Size(88, 26), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.SizeAll, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold) };
        private readonly Label _status = new() { AutoSize = false, Size = new Size(470, 26), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private const int IconSize = 18;
        private readonly Bitmap _saveIcon = AppIcons.Sized(IconSize, AppIcons.SaveCopy);

        private readonly Button _close = new() { Text = "✕", Size = new Size(30, 26), TabStop = false, Margin = new Padding(0) };
        private readonly Label _blurLabel = new() { AutoSize = true, Margin = new Padding(0, 9, 4, 0) };
        private readonly TrackBar _blur = NewSlider((int)Math.Round(FocusLook.UsualBlur * Maximum));
        private readonly Label _blurValue = new() { AutoSize = false, Size = new Size(44, 20), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 9, 10, 0) };
        private readonly Label _rangeLabel = new() { AutoSize = true, Margin = new Padding(0, 9, 4, 0) };
        private readonly TrackBar _range = NewSlider((int)Math.Round(FocusLook.UsualRange * Maximum));
        private readonly Label _rangeValue = new() { AutoSize = false, Size = new Size(44, 20), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 9, 10, 0) };
        private readonly Label _hazeLabel = new() { AutoSize = true, Margin = new Padding(0, 9, 4, 0) };
        private readonly TrackBar _haze = NewSlider((int)Math.Round(FocusLook.UsualHaze * Maximum));
        private readonly Label _hazeValue = new() { AutoSize = false, Size = new Size(44, 20), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 9, 14, 0) };
        private readonly Button _save = new() { Size = new Size(40, 28), TabStop = false, Margin = new Padding(0, 3, 0, 0) };
        private readonly ToolTip _tip = new() { InitialDelay = 400, ShowAlways = true };

        private bool _busy;
        private bool _ready;
        private bool _setting;           // a slider is being set by the code, not by the person
        private Point _dragFrom;
        private bool _dragging;

        public RefocusBar()
        {
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(8, 5, 8, 7);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);

            var first = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), FlowDirection = FlowDirection.LeftToRight };
            first.Controls.AddRange([_grip, _title, _status, _close]);
            var sliders = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), FlowDirection = FlowDirection.LeftToRight };
            sliders.Controls.AddRange([_blurLabel, _blur, _blurValue, _rangeLabel, _range, _rangeValue, _hazeLabel, _haze, _hazeValue]);

            // The second row is as wide as the first, with the save button at its right end (under ✕), whatever the width of the texts
            var second = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 0), Dock = DockStyle.Fill };
            second.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            second.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            second.Controls.Add(sliders, 0, 0);
            second.Controls.Add(_save, 1, 0);

            var rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Dock = DockStyle.Fill };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rows.Controls.Add(first, 0, 0);
            rows.Controls.Add(second, 0, 1);
            Controls.Add(rows);

            _save.Image = _saveIcon;

            _save.Click += (_, _) => { SaveClicked?.Invoke(); Used?.Invoke(); };
            _close.Click += (_, _) => { CloseClicked?.Invoke(); Used?.Invoke(); };
            foreach (var slider in new[] { _blur, _range, _haze })
            {
                slider.ValueChanged += (_, _) => SliderChanged();
                slider.MouseUp += (_, _) => { LookCommitted?.Invoke(); Used?.Invoke(); };
                slider.MouseDoubleClick += (_, _) => ResetSlider(slider);
            }

            foreach (var handle in new Control[] { _grip, _title, _status })
            {
                handle.MouseDown += StartDrag;
                handle.MouseMove += Drag;
                handle.MouseUp += (_, _) => { _dragging = false; Used?.Invoke(); };
            }

            ApplyTexts();
            ShowValues();
            UpdateButtons();
        }

        /// <summary>The blur, the zone or the haze is being changed (many times a second while a slider is dragged).</summary>
        public event Action? LookChanged;

        /// <summary>The person let go of a slider (or brought it back): the values to keep.</summary>
        public event Action? LookCommitted;

        public event Action? SaveClicked;
        public event Action? CloseClicked;

        /// <summary>The bar was moved by the person: the owner remembers where it is.</summary>
        public event Action? Moved;

        /// <summary>The person used the bar: the owner gives the keyboard back to the viewer.</summary>
        public event Action? Used;

        /// <summary>The blur, the sharp zone and the haze as the sliders say, each 0 to <see cref="Maximum"/>, as they are kept in the settings.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public (int Blur, int Range, int Haze) Values
        {
            get => (_blur.Value, _range.Value, _haze.Value);
            set
            {
                _setting = true;
                try
                {
                    _blur.Value = Math.Clamp(value.Blur, 0, Maximum);
                    _range.Value = Math.Clamp(value.Range, 0, Maximum);
                    _haze.Value = Math.Clamp(value.Haze, 0, Maximum);
                }
                finally
                {
                    _setting = false;
                }
                ShowValues();
            }
        }

        /// <summary>Something is going on (the depth is being worked out, or a copy is being saved): the save button waits.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Busy
        {
            get => _busy;
            set
            {
                _busy = value;
                UpdateButtons();
            }
        }

        /// <summary>The photo is there to be refocused (its depth is known) and can be saved.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Ready
        {
            get => _ready;
            set
            {
                _ready = value;
                UpdateButtons();
            }
        }

        /// <summary>Whether the save button can be pressed.</summary>
        internal bool CanSave => _save.Enabled;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Status
        {
            get => _status.Text;
            set
            {
                _status.Text = value;
                _tip.SetToolTip(_status, value); // the label cuts a long text; the whole of it is in the tip
            }
        }

        /// <summary>Writes the texts again in the language that is on.</summary>
        public void ApplyTexts()
        {
            _title.Text = RefocusStrings.Title;
            _blurLabel.Text = RefocusStrings.BlurLabel;
            _rangeLabel.Text = RefocusStrings.RangeLabel;
            _hazeLabel.Text = RefocusStrings.HazeLabel;
            _tip.SetToolTip(_grip, RefocusStrings.BarGripTooltip);
            _tip.SetToolTip(_title, RefocusStrings.BarGripTooltip);
            _tip.SetToolTip(_close, RefocusStrings.BarCloseTooltip);
            _tip.SetToolTip(_blur, RefocusStrings.BlurTooltip);
            _tip.SetToolTip(_blurLabel, RefocusStrings.BlurTooltip);
            _tip.SetToolTip(_range, RefocusStrings.RangeTooltip);
            _tip.SetToolTip(_rangeLabel, RefocusStrings.RangeTooltip);
            _tip.SetToolTip(_haze, RefocusStrings.HazeTooltip);
            _tip.SetToolTip(_hazeLabel, RefocusStrings.HazeTooltip);
            AccessibleName = RefocusStrings.Title;
            _close.AccessibleName = RefocusStrings.BarCloseTooltip;

            // A button with a picture has no word on it: its name goes first in the tip, then what it does
            _save.AccessibleName = RefocusStrings.SaveButton;
            _tip.SetToolTip(_save, $"{RefocusStrings.SaveButton.TrimEnd('.', '…')}: {RefocusStrings.SaveTooltip}");
        }

        /// <summary>
        /// The theme paints every control like a control of a window (labels and sliders in a shade of their own); on a bar they sit on its background, so they are given that. Called after the theme was applied.
        /// </summary>
        public void Restyle()
        {
            foreach (var control in Descendants(this))
            {
                if (control is Label or TrackBar)
                    control.BackColor = BackColor;
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var inner in Descendants(child))
                    yield return inner;
            }
        }

        private static TrackBar NewSlider(int value) => new()
        {
            Minimum = 0,
            Maximum = RefocusBar.Maximum,
            Value = value,
            SmallChange = 5,
            LargeChange = 20,
            TickStyle = TickStyle.None,
            AutoSize = false,
            Size = new Size(110, 26),
            TabStop = false,
            Margin = new Padding(0, 4, 4, 0),
        };

        private void SliderChanged()
        {
            ShowValues();
            if (!_setting)
                LookChanged?.Invoke();
        }

        private void ResetSlider(TrackBar slider)
        {
            slider.Value = slider == _blur ? (int)Math.Round(FocusLook.UsualBlur * Maximum)
                : slider == _range ? (int)Math.Round(FocusLook.UsualRange * Maximum)
                : (int)Math.Round(FocusLook.UsualHaze * Maximum);
            LookCommitted?.Invoke();
            Used?.Invoke();
        }

        private void ShowValues()
        {
            _blurValue.Text = $"{_blur.Value} %";
            _rangeValue.Text = $"{_range.Value} %";
            _hazeValue.Text = $"{_haze.Value} %";
        }

        private void UpdateButtons() => _save.Enabled = _ready && !_busy;

        // ── moving the bar by its handle ──────────────────────────────────

        private void StartDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            _dragging = true;
            _dragFrom = Cursor.Position;
        }

        private void Drag(object? sender, MouseEventArgs e)
        {
            if (!_dragging || Parent is null)
                return;

            var now = Cursor.Position;
            var area = Parent.ClientSize;
            Location = new Point(
                Math.Clamp(Left + now.X - _dragFrom.X, 0, Math.Max(0, area.Width - Width)),
                Math.Clamp(Top + now.Y - _dragFrom.Y, 0, Math.Max(0, area.Height - Height)));
            _dragFrom = now;
            Moved?.Invoke();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
                _title.Font.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
                _saveIcon.Dispose(); // after the button: it no longer paints it
        }
    }
}
