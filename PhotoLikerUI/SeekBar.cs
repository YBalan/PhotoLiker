using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PhotoLikerUI
{
    /// <summary>
    /// A slider for a place in a video (or a volume): a track, the part played, a knob, and ticks where something is (the moments a search found).
    /// It never takes the keyboard focus, so the keys of the viewer keep working after it was used, and it says what the person asked for
    /// (<see cref="Seek"/>) without moving by itself: the owner moves it with <see cref="Value"/> as the video plays.
    /// </summary>
    internal sealed class SeekBar : Control
    {
        private const int Padding_ = 8;
        private static readonly Color Accent = Color.FromArgb(0, 122, 204);
        private static readonly TimeSpan DragInterval = TimeSpan.FromMilliseconds(90);

        private readonly Stopwatch _sinceSeek = Stopwatch.StartNew();
        private readonly ToolTip _tip = new() { InitialDelay = 0, ReshowDelay = 0, ShowAlways = true };
        private long _maximum;
        private long _value;
        private bool _dragging;
        private long _dragValue;
        private IReadOnlyList<long> _marks = [];
        private IReadOnlyList<long> _spokenMarks = [];

        public SeekBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Height = 26;
            Cursor = Cursors.Hand;
        }

        /// <summary>The largest value (the length of the video), 0 when there is nothing to move in.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public long Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(0, value);
                Invalidate();
            }
        }

        /// <summary>Where the knob is. Not moved while the person drags it.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public long Value
        {
            get => _dragging ? _dragValue : _value;
            set
            {
                if (_dragging)
                    return;

                _value = Math.Clamp(value, 0, Math.Max(0, _maximum));
                Invalidate();
            }
        }

        /// <summary>Places (in the unit of <see cref="Value"/>) that are marked on the track.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<long> Marks
        {
            get => _marks;
            set
            {
                _marks = value;
                Invalidate();
            }
        }

        /// <summary>Places (in the unit of <see cref="Value"/>) that are marked in the colour of speech (<see cref="SpeechMark.Colour"/>) over <see cref="Marks"/>: what a search found in what is said.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<long> SpokenMarks
        {
            get => _spokenMarks;
            set
            {
                _spokenMarks = value;
                Invalidate();
            }
        }

        /// <summary>The person asked for this place (clicked or dragged there); raised as the knob is dragged too, a few times a second.</summary>
        public event Action<long>? Seek;

        /// <summary>The person used the bar: the owner gives the keyboard back to the viewer.</summary>
        public event Action? Used;

        /// <summary>Text shown while the mouse is over a place, for a place in the unit of <see cref="Value"/> (default: none).</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Func<long, string>? HoverText { get; set; }

        /// <summary>Where, in pixels, the track begins and ends.</summary>
        private (int Left, int Right) Track => (Padding_, Math.Max(Padding_ + 1, Width - Padding_));

        private long ValueAt(int x)
        {
            var (left, right) = Track;
            var fraction = Math.Clamp((x - left) / (double)(right - left), 0, 1);
            return (long)Math.Round(fraction * _maximum);
        }

        private int XOf(long value)
        {
            var (left, right) = Track;
            return _maximum <= 0 ? left : left + (int)Math.Round((right - left) * (Math.Clamp(value, 0, _maximum) / (double)_maximum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Used?.Invoke();
            if (e.Button != MouseButtons.Left || _maximum <= 0)
                return;

            _dragging = true;
            Capture = true;
            _dragValue = ValueAt(e.X);
            _sinceSeek.Restart();
            Invalidate();
            Seek?.Invoke(_dragValue);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_maximum <= 0)
                return;

            var at = ValueAt(e.X);
            if (HoverText is not null)
                _tip.SetToolTip(this, HoverText(at));

            if (!_dragging)
                return;

            _dragValue = at;
            Invalidate();
            if (_sinceSeek.Elapsed >= DragInterval)
            {
                _sinceSeek.Restart();
                Seek?.Invoke(_dragValue);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
                return;

            _dragging = false;
            Capture = false;
            _value = _dragValue;
            Invalidate();
            Seek?.Invoke(_dragValue);
            Used?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var (left, right) = Track;
            var middle = Height / 2;
            var knob = XOf(Value);

            using (var track = new SolidBrush(Color.FromArgb(70, ForeColor)))
                g.FillRectangle(track, left, middle - 2, right - left, 4);
            if (_maximum > 0)
            {
                using var played = new SolidBrush(Accent);
                g.FillRectangle(played, left, middle - 2, Math.Max(0, knob - left), 4);
            }

            // Ticks stand above the track, so they can be seen over the played part as well
            using (var mark = new SolidBrush(Color.FromArgb(230, 255, 170, 0)))
            {
                foreach (var at in _marks)
                    g.FillRectangle(mark, XOf(at) - 1, middle - 7, 3, 14);
            }
            using (var spoken = new SolidBrush(Color.FromArgb(240, SpeechMark.Colour)))
            {
                foreach (var at in _spokenMarks)
                    g.FillRectangle(spoken, XOf(at) - 1, middle - 7, 3, 14);
            }

            if (_maximum > 0)
            {
                using var fill = new SolidBrush(Enabled ? Color.White : Color.Gray);
                using var edge = new Pen(Accent, 2);
                g.FillEllipse(fill, knob - 6, middle - 6, 12, 12);
                g.DrawEllipse(edge, knob - 6, middle - 6, 12, 12);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
