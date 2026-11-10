using System.ComponentModel;
using PhotoLiker.Effects.Colorize;

namespace PhotoLikerUI
{
    /// <summary>
    /// The bar that floats over a black and white photo in the viewer: a button that shows the photo with colours (and back to the original), the strength and the warmth of the colours as sliders,
    /// a button that saves a copy, and one that opens the window for many photos at once. It only holds the controls and tells what the person did; <see cref="MainForm"/> does the work (see
    /// MainForm.ColorizeBar.cs). It never takes the keyboard: whatever the person presses on it, the owner gives the keys back to the viewer (<see cref="Used"/>).
    /// </summary>
    internal sealed class ColorizeBar : Panel
    {
        public const int MinStrength = 20;
        public const int MaxStrength = 200;
        public const int MaxWarmth = 100;

        private readonly Label _grip = new() { Text = "⋮⋮", AutoSize = false, Size = new Size(18, 26), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.SizeAll, Margin = new Padding(0, 0, 2, 0) };
        private readonly Label _title = new() { AutoSize = false, Size = new Size(88, 26), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.SizeAll, Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold) };
        private readonly Label _status = new() { AutoSize = false, Size = new Size(440, 26), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Button _more = new() { AutoSize = true, MinimumSize = new Size(0, 26), TabStop = false, Margin = new Padding(6, 0, 6, 0) };
        private readonly Button _close = new() { Text = "✕", Size = new Size(30, 26), TabStop = false, Margin = new Padding(0) };
        private readonly Button _colorize = new() { AutoSize = true, MinimumSize = new Size(116, 28), TabStop = false, Margin = new Padding(0, 4, 14, 0) };
        private readonly Label _strengthLabel = new() { AutoSize = true, Margin = new Padding(0, 9, 4, 0) };
        private readonly TrackBar _strength = NewSlider(MinStrength, MaxStrength, 100, small: 5, large: 20, width: 120);
        private readonly Label _strengthValue = new() { AutoSize = false, Size = new Size(48, 20), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 9, 12, 0) };
        private readonly Label _warmthLabel = new() { AutoSize = true, Margin = new Padding(0, 9, 4, 0) };
        private readonly TrackBar _warmth = NewSlider(-MaxWarmth, MaxWarmth, 0, small: 5, large: 20, width: 120);
        private readonly Label _warmthValue = new() { AutoSize = false, Size = new Size(40, 20), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 9, 14, 0) };
        private readonly Button _save = new() { AutoSize = true, MinimumSize = new Size(110, 28), TabStop = false, Margin = new Padding(0, 4, 0, 0) };
        private readonly ToolTip _tip = new() { InitialDelay = 400, ShowAlways = true };

        private bool _coloursShown;
        private bool _busy;
        private bool _ready;
        private bool _setting;           // a slider is being set by the code, not by the person
        private Point _dragFrom;
        private bool _dragging;

        public ColorizeBar()
        {
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(8, 5, 8, 7);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);

            var first = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), FlowDirection = FlowDirection.LeftToRight };
            first.Controls.AddRange([_grip, _title, _status, _more, _close]);
            var second = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0), FlowDirection = FlowDirection.LeftToRight };
            second.Controls.AddRange([_colorize, _strengthLabel, _strength, _strengthValue, _warmthLabel, _warmth, _warmthValue, _save]);

            var rows = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0), Dock = DockStyle.Fill };
            rows.Controls.AddRange([first, second]);
            Controls.Add(rows);

            _colorize.Click += (_, _) => { ColorsToggled?.Invoke(!_coloursShown); Used?.Invoke(); };
            _save.Click += (_, _) => { SaveClicked?.Invoke(); Used?.Invoke(); };
            _more.Click += (_, _) => { MoreClicked?.Invoke(); Used?.Invoke(); };
            _close.Click += (_, _) => { CloseClicked?.Invoke(); Used?.Invoke(); };
            _strength.ValueChanged += (_, _) => SliderChanged();
            _warmth.ValueChanged += (_, _) => SliderChanged();
            foreach (var slider in new[] { _strength, _warmth })
            {
                slider.MouseUp += (_, _) => { TuningCommitted?.Invoke(); Used?.Invoke(); };
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

        /// <summary>The person asked for the colours (true) or for the original (false).</summary>
        public event Action<bool>? ColorsToggled;

        /// <summary>The strength or the warmth is being changed (many times a second while a slider is dragged).</summary>
        public event Action? TuningChanged;

        /// <summary>The person let go of a slider: the value to keep.</summary>
        public event Action? TuningCommitted;

        public event Action? SaveClicked;
        public event Action? MoreClicked;
        public event Action? CloseClicked;

        /// <summary>The bar was moved by the person: the owner remembers where it is.</summary>
        public event Action? Moved;

        /// <summary>The person used the bar: the owner gives the keyboard back to the viewer.</summary>
        public event Action? Used;

        /// <summary>What the sliders say.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ColorTuning Tuning => new(_strength.Value / 100f, _warmth.Value / (float)MaxWarmth);

        /// <summary>The strength in percent and the warmth from -100 to 100, as they are kept in the settings.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public (int Strength, int Warmth) Values
        {
            get => (_strength.Value, _warmth.Value);
            set
            {
                _setting = true;
                try
                {
                    _strength.Value = Math.Clamp(value.Strength, MinStrength, MaxStrength);
                    _warmth.Value = Math.Clamp(value.Warmth, -MaxWarmth, MaxWarmth);
                }
                finally
                {
                    _setting = false;
                }
                ShowValues();
            }
        }

        /// <summary>The photo is shown with its colours (the button says what pressing it does next).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ColoursShown
        {
            get => _coloursShown;
            set
            {
                _coloursShown = value;
                UpdateButtons();
            }
        }

        /// <summary>Something is going on (the model is colouring, or a copy is being saved): the buttons wait.</summary>
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

        /// <summary>The colours are there to be saved: the model has coloured the photo and nothing is going on.</summary>
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
            _title.Text = ColorizeStrings.BarTitle;
            _strengthLabel.Text = ColorizeStrings.BarStrength;
            _warmthLabel.Text = ColorizeStrings.BarWarmth;
            _save.Text = ColorizeStrings.BarSave;
            _more.Text = ColorizeStrings.BarMore;
            _tip.SetToolTip(_grip, ColorizeStrings.BarGripTooltip);
            _tip.SetToolTip(_title, ColorizeStrings.BarGripTooltip);
            _tip.SetToolTip(_more, ColorizeStrings.BarMoreTooltip);
            _tip.SetToolTip(_close, ColorizeStrings.BarCloseTooltip);
            _tip.SetToolTip(_colorize, ColorizeStrings.BarColorizeTooltip);
            _tip.SetToolTip(_strength, ColorizeStrings.BarStrengthTooltip);
            _tip.SetToolTip(_strengthLabel, ColorizeStrings.BarStrengthTooltip);
            _tip.SetToolTip(_warmth, ColorizeStrings.BarWarmthTooltip);
            _tip.SetToolTip(_warmthLabel, ColorizeStrings.BarWarmthTooltip);
            _tip.SetToolTip(_save, ColorizeStrings.BarSaveTooltip);
            AccessibleName = ColorizeStrings.BarTitle;
            _close.AccessibleName = ColorizeStrings.BarCloseTooltip;
            UpdateButtons();
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

        private static TrackBar NewSlider(int minimum, int maximum, int value, int small, int large, int width) => new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            SmallChange = small,
            LargeChange = large,
            TickStyle = TickStyle.None,
            AutoSize = false,
            Size = new Size(width, 26),
            TabStop = false,
            Margin = new Padding(0, 4, 4, 0),
        };

        private void SliderChanged()
        {
            ShowValues();
            if (!_setting)
                TuningChanged?.Invoke();
        }

        private void ResetSlider(TrackBar slider)
        {
            slider.Value = slider == _strength ? 100 : 0;
            TuningCommitted?.Invoke();
            Used?.Invoke();
        }

        private void ShowValues()
        {
            _strengthValue.Text = $"{_strength.Value} %";
            _warmthValue.Text = _warmth.Value > 0 ? $"+{_warmth.Value}" : _warmth.Value.ToString();
        }

        private void UpdateButtons()
        {
            _colorize.Text = _coloursShown ? ColorizeStrings.BarOriginal : ColorizeStrings.BarColorize;
            _colorize.Enabled = !_busy;
            _save.Enabled = _ready && _coloursShown && !_busy;
            _strength.Enabled = _warmth.Enabled = _ready && _coloursShown;
        }

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
        }
    }
}
