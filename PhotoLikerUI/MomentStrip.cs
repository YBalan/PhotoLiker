using System.ComponentModel;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// The moments of a video as a row of small pictures with their times, under the viewer: where the picture changes, the places a search found
    /// marked in orange (with a teal speech bubble when it was found in what is said, and the words over it), the moment that is being played marked in blue. A click goes to that place. It scrolls with the mouse wheel and its scroll bar, and never
    /// takes the keyboard focus. It owns the pictures it is given.
    /// </summary>
    internal sealed class MomentStrip : Control
    {
        private const int ThumbWidth = 104;
        private const int ThumbHeight = 78;
        private const int LabelHeight = 16;
        private const int Gap = 6;
        private static readonly Color Accent = Color.FromArgb(0, 122, 204);
        private static readonly Color Hit = Color.FromArgb(255, 170, 0);

        private readonly HScrollBar _scroll = new() { Dock = DockStyle.Bottom, TabStop = false };
        private List<(long TimeMs, Bitmap Picture)> _moments = [];
        private HashSet<int> _hits = []; // the moments that stand for a place a search found: the last moment at or before it
        private HashSet<int> _spoken = []; // the ones among them that were found in what is said
        private readonly ToolTip _tip = new() { InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
        private int _current = -1;
        private int _tipIndex = -1;

        public MomentStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            Height = ThumbHeight + LabelHeight + 2 * Gap + SystemInformation.HorizontalScrollBarHeight;
            Controls.Add(_scroll);
            _scroll.Scroll += (_, _) => Invalidate();
            MouseWheel += (_, e) => ScrollBy(-Math.Sign(e.Delta) * (ThumbWidth + Gap) * 2);
        }

        /// <summary>The person clicked a moment: its time (milliseconds).</summary>
        public event Action<long>? MomentClicked;

        /// <summary>The person used the strip: the owner gives the keyboard back to the viewer.</summary>
        public event Action? Used;

        /// <summary>How many moments are shown.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Count => _moments.Count;

        /// <summary>The times of the moments that are shown, in order.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<long> Times => [.. _moments.Select(m => m.TimeMs)];

        /// <summary>
        /// The words that are said in a stretch of the video (from, to: milliseconds), for the text over a moment; none when nothing is said or known. Asked while the mouse
        /// is over a moment, so it must be quick.
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<long, long, string?>? SaidIn { get; set; }

        /// <summary>
        /// Shows these moments (the strip takes the pictures over and disposes them); <paramref name="hits"/> are the times that are marked as found, and <paramref name="spoken"/>
        /// the ones among them that were found in what is said (they get a speech bubble).
        /// </summary>
        public void SetMoments(IEnumerable<(long TimeMs, Bitmap Picture)> moments, IEnumerable<long>? hits, IEnumerable<long>? spoken = null)
        {
            Clear();
            _moments = [.. moments];
            _hits = [.. (hits ?? []).Select(place => _moments.FindLastIndex(m => m.TimeMs <= place + 250)).Where(index => index >= 0)];
            _spoken = [.. (spoken ?? []).Select(place => _moments.FindLastIndex(m => m.TimeMs <= place + 250)).Where(index => index >= 0)];
            _current = -1;
            UpdateScroll();
            Invalidate();
        }

        public void Clear()
        {
            foreach (var (_, picture) in _moments)
                picture.Dispose();
            _moments = [];
            _hits = [];
            _spoken = [];
            _current = -1;
            _tipIndex = -1;
            UpdateScroll();
            Invalidate();
        }

        /// <summary>Marks the moment the video is at (the last one that is not after <paramref name="timeMs"/>), and scrolls to it if it is out of sight.</summary>
        public void SetCurrent(long timeMs)
        {
            var index = -1;
            for (var i = 0; i < _moments.Count && _moments[i].TimeMs <= timeMs + 250; i++)
                index = i;

            if (index == _current)
                return;

            _current = index;
            if (index >= 0)
            {
                var left = index * (ThumbWidth + Gap);
                if (left < _scroll.Value || left + ThumbWidth > _scroll.Value + ClientSize.Width)
                    _scroll.Value = Math.Clamp(left - ClientSize.Width / 3, 0, Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1));
            }
            Invalidate();
        }

        /// <summary>Scrolls so that the first moment that is marked as found is in view.</summary>
        public void ScrollToFirstHit()
        {
            if (_hits.Count == 0)
                return;

            var index = _hits.Min();

            _scroll.Value = Math.Clamp(index * (ThumbWidth + Gap) - Gap, 0, Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1));
            Invalidate();
        }

        private void ScrollBy(int delta)
        {
            _scroll.Value = Math.Clamp(_scroll.Value + delta, 0, Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1));
            Invalidate();
        }

        private void UpdateScroll()
        {
            var content = _moments.Count * (ThumbWidth + Gap) + Gap;
            _scroll.Visible = content > ClientSize.Width;
            _scroll.Minimum = 0;
            _scroll.LargeChange = Math.Max(1, ClientSize.Width);
            _scroll.SmallChange = ThumbWidth + Gap;
            _scroll.Maximum = Math.Max(content, 1);
            _scroll.Value = Math.Clamp(_scroll.Value, 0, Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
        }

        // The moment under a point of the strip, -1 between the pictures and outside them
        private int MomentAt(Point point)
        {
            var index = (point.X + _scroll.Value - Gap) / (ThumbWidth + Gap);
            var within = (point.X + _scroll.Value - Gap) % (ThumbWidth + Gap);
            return point.X + _scroll.Value >= Gap && index >= 0 && index < _moments.Count && within <= ThumbWidth && point.Y >= Gap && point.Y <= Gap + ThumbHeight + LabelHeight ? index : -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Used?.Invoke();
            if (e.Button != MouseButtons.Left)
                return;

            if (MomentAt(e.Location) is var index and >= 0)
                MomentClicked?.Invoke(_moments[index].TimeMs);
        }

        // What is said at the moment under the mouse, as the text over it
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = MomentAt(e.Location);
            if (index == _tipIndex)
                return;

            _tipIndex = index;
            string? said = null;
            if (index >= 0 && SaidIn is { } saidIn)
                said = saidIn(_moments[index].TimeMs, index + 1 < _moments.Count ? _moments[index + 1].TimeMs : long.MaxValue);
            _tip.SetToolTip(this, said is { Length: > 0 } ? VideoTime.Format(_moments[index].TimeMs) + " - " + said : string.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tipIndex = -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var first = Math.Max(0, (_scroll.Value - Gap) / (ThumbWidth + Gap));
            for (var i = first; i < _moments.Count; i++)
            {
                var x = Gap + i * (ThumbWidth + Gap) - _scroll.Value;
                if (x > ClientSize.Width)
                    break;

                var (time, picture) = _moments[i];
                var box = new Rectangle(x, Gap, ThumbWidth, ThumbHeight);
                g.FillRectangle(Brushes.Black, box);
                var scale = Math.Min((double)ThumbWidth / picture.Width, (double)ThumbHeight / picture.Height);
                var w = (int)(picture.Width * scale);
                var h = (int)(picture.Height * scale);
                g.DrawImage(picture, box.X + (ThumbWidth - w) / 2, box.Y + (ThumbHeight - h) / 2, w, h);

                var found = _hits.Contains(i);
                if (found || i == _current)
                {
                    using var pen = new Pen(found ? Hit : Accent, found && i == _current ? 4 : 3);
                    g.DrawRectangle(pen, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);
                    if (found && i == _current)
                    {
                        using var inner = new Pen(Accent, 2);
                        g.DrawRectangle(inner, box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
                    }
                }

                if (_spoken.Contains(i))
                    SpeechMark.Draw(g, new Rectangle(box.Right - 32, box.Top + 6, 26, 23)); // found in what is said, not in the picture

                TextRenderer.DrawText(g, VideoTime.Format(time), Font, new Rectangle(x, Gap + ThumbHeight, ThumbWidth, LabelHeight), ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Clear();
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
