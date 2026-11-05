using System.ComponentModel;
using PhotoLiker.Core;

namespace PhotoLikerUI
{
    /// <summary>
    /// The rhythm of an audio file as bars (how hard the sound hits, at each moment), with the beats found drawn over them as lines and the
    /// places where the photos change as marks above. A stretch of the audio is shown at a time, and a scroll bar (or the mouse wheel)
    /// moves along it. Experimental, for the "Cut photos on the beat" of the Generate Video dialog.
    /// </summary>
    internal sealed class RhythmView : Panel
    {
        /// <summary>The length of the stretch shown: about ten beats at 30 BPM, thirty at 90, so that single beats can be told apart.</summary>
        private const double WindowSeconds = 20;

        private const int CutBandHeight = 12;
        private const int RulerHeight = 16;
        private const int BarStride = 3;   // pixels from one bar to the next
        private const int BarWidth = 2;
        private const int ScrollUnitsPerSecond = 10;

        private readonly Strip _strip = new();
        private readonly HScrollBar _scrollBar = new() { Dock = DockStyle.Bottom, SmallChange = ScrollUnitsPerSecond };

        public RhythmView()
        {
            Controls.Add(_strip);
            Controls.Add(_scrollBar);
            _strip.Dock = DockStyle.Fill;
            _strip.Owner = this;
            _scrollBar.ValueChanged += (_, _) => _strip.Invalidate();
            _strip.MouseWheel += (_, e) => ScrollBy(-Math.Sign(e.Delta) * 2);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BarColor { get; set; } = Color.FromArgb(90, 150, 220);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BeatColor { get; set; } = Color.FromArgb(240, 140, 40);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color CutColor { get; set; } = Color.FromArgb(60, 200, 120);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TextColor { get; set; } = SystemColors.GrayText;

        private RhythmAnalysis? _analysis;
        private string _message = string.Empty;
        private IReadOnlyList<double> _cuts = [];

        /// <summary>The rhythm to show, or none (then the message is shown in its place).</summary>
        public void SetAnalysis(RhythmAnalysis? analysis, string message)
        {
            var sameAudio = analysis is not null && _analysis is not null && Math.Abs(analysis.DurationSeconds - _analysis.DurationSeconds) < 1e-9;
            _analysis = analysis;
            _message = message;
            UpdateScrollBar(keepPlace: sameAudio);
            _strip.Invalidate();
        }

        /// <summary>The times, in seconds from the start, where one picture gives way to the next; from the smallest.</summary>
        public void SetCuts(IReadOnlyList<double> cuts)
        {
            _cuts = cuts;
            _strip.Invalidate();
        }

        /// <summary>The first second of the stretch that is shown.</summary>
        public double ViewStartSeconds => _scrollBar.Value / (double)ScrollUnitsPerSecond;

        /// <summary>How many seconds are shown: the stretch, or the whole audio when that is shorter.</summary>
        public double ViewSeconds => _analysis is null ? WindowSeconds : Math.Min(WindowSeconds, Math.Max(1, _analysis.DurationSeconds));

        /// <summary>Moves the view so that this second is in it (the start of the stretch when it is not).</summary>
        public void ScrollTo(double seconds) =>
            _scrollBar.Value = Math.Clamp((int)(seconds * ScrollUnitsPerSecond), 0, MaxScrollValue);

        private int MaxScrollValue => Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);

        private void ScrollBy(double seconds) => ScrollTo(ViewStartSeconds + seconds);

        private void UpdateScrollBar(bool keepPlace)
        {
            var place = keepPlace ? _scrollBar.Value : 0;
            if (_analysis is null || _analysis.DurationSeconds <= WindowSeconds)
            {
                _scrollBar.Enabled = false;
                _scrollBar.Minimum = 0;
                _scrollBar.Maximum = 0;
                _scrollBar.LargeChange = 1;
                _scrollBar.Value = 0;
                return;
            }

            _scrollBar.Enabled = true;
            _scrollBar.Minimum = 0;
            _scrollBar.LargeChange = (int)(WindowSeconds * ScrollUnitsPerSecond);
            _scrollBar.Maximum = (int)Math.Ceiling(_analysis.DurationSeconds * ScrollUnitsPerSecond) + _scrollBar.LargeChange - 1;
            _scrollBar.Value = Math.Clamp(place, 0, MaxScrollValue);
        }

        /// <summary>The first of the sorted times that is not below <paramref name="value"/>.</summary>
        private static int LowerBound(IReadOnlyList<double> times, double value)
        {
            var low = 0;
            var high = times.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (times[middle] < value)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private static string Clock(double seconds)
        {
            var whole = (int)Math.Round(seconds);
            return $"{whole / 60}:{whole % 60:00}";
        }

        /// <summary>The part that draws (a panel's own painting is not double buffered, a control that sets the style is).</summary>
        private sealed class Strip : Control
        {
            public Strip()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public RhythmView Owner { get; set; } = null!;

            // Dragging moves along the audio; a click gives the strip the focus, which is what the mouse wheel needs (it goes to the
            // control that has the focus, and taking the focus when the mouse only passes would take it from a box being typed in)
            private int _dragFromX;
            private double _dragFromSeconds;

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                _dragFromX = e.X;
                _dragFromSeconds = Owner.ViewStartSeconds;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (e.Button == MouseButtons.Left && Width > 0)
                    Owner.ScrollTo(_dragFromSeconds - ((e.X - _dragFromX) * Owner.ViewSeconds / Width));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var graphics = e.Graphics;
                graphics.Clear(BackColor);
                var view = Owner;
                if (view._analysis is not { } analysis)
                {
                    TextRenderer.DrawText(graphics, view._message, Font, ClientRectangle, view.TextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    return;
                }

                var top = CutBandHeight;
                var bottom = Height - RulerHeight;
                var plot = Math.Max(1, bottom - top);
                var seconds = view.ViewSeconds;
                var start = view.ViewStartSeconds;
                var perSecond = Width / seconds;

                // The bars: the strongest hit of the stretch of time that each one stands for
                using (var barBrush = new SolidBrush(view.BarColor))
                {
                    for (var x = 0; x < Width; x += BarStride)
                    {
                        var from = (int)Math.Floor((start + (x / perSecond)) / analysis.HopSeconds);
                        var to = (int)Math.Floor((start + ((x + BarStride) / perSecond)) / analysis.HopSeconds);
                        var strongest = 0f;
                        for (var i = Math.Max(0, from); i <= to && i < analysis.Strength.Count; i++)
                            strongest = Math.Max(strongest, analysis.Strength[i]);

                        var height = (int)Math.Round(Math.Pow(strongest, 0.7) * plot);
                        if (height > 0)
                            graphics.FillRectangle(barBrush, x, bottom - height, BarWidth, height);
                    }
                }

                // The beats, as lines over the bars
                using (var beatPen = new Pen(view.BeatColor))
                {
                    for (var i = LowerBound(analysis.Beats, start - 0.05); i < analysis.Beats.Count && analysis.Beats[i] <= start + seconds + 0.05; i++)
                    {
                        var x = (int)Math.Round((analysis.Beats[i] - start) * perSecond);
                        graphics.DrawLine(beatPen, x, top, x, bottom);
                    }
                }

                // Where the photos change: a mark in the band above the bars, and a faint line down through them
                using (var cutBrush = new SolidBrush(view.CutColor))
                using (var cutPen = new Pen(Color.FromArgb(110, view.CutColor)))
                {
                    for (var i = LowerBound(view._cuts, start - 0.05); i < view._cuts.Count && view._cuts[i] <= start + seconds + 0.05; i++)
                    {
                        var x = (int)Math.Round((view._cuts[i] - start) * perSecond);
                        graphics.FillPolygon(cutBrush, [new Point(x - 4, 1), new Point(x + 4, 1), new Point(x, CutBandHeight - 2)]);
                        graphics.DrawLine(cutPen, x, CutBandHeight - 2, x, bottom);
                    }
                }

                // The time: a tick every second, and the clock every five
                using (var tickPen = new Pen(view.TextColor))
                {
                    graphics.DrawLine(tickPen, 0, bottom, Width, bottom);
                    for (var second = (int)Math.Ceiling(start); second <= start + seconds; second++)
                    {
                        var x = (int)Math.Round((second - start) * perSecond);
                        var labelled = second % 5 == 0;
                        graphics.DrawLine(tickPen, x, bottom, x, bottom + (labelled ? 4 : 2));
                        if (labelled && x > 14 && x < Width - 14)
                        {
                            TextRenderer.DrawText(graphics, Clock(second), Font, new Rectangle(x - 20, bottom + 3, 40, RulerHeight - 3), view.TextColor,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        }
                    }
                }
            }
        }
    }
}
