using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PhotoLikerUI
{
    /// <summary>
    /// How <see cref="SlideshowScreen.ShowFrame"/> puts a photo on a screen: with no effect at all (the default of the struct), or taking the place of the last
    /// photo by <see cref="Transition"/> for <see cref="TransitionSeconds"/>, and/or moving slowly for <see cref="MotionSeconds"/>. <see cref="StartedAt"/> is a
    /// <see cref="Stopwatch"/> time, the same for all the screens of one change so that they move together.
    /// </summary>
    internal readonly record struct SlideshowShowPlan(
        SlideshowTransition Transition, double TransitionSeconds, bool Motion, double MotionSeconds, long StartedAt);

    /// <summary>
    /// The window of the slideshow on one screen: black, with the photo on it as large as the screen lets it be and the year or the date and time it was taken
    /// in the bottom right corner (see <see cref="SlideshowWatermark"/>). It only shows what <see cref="SlideshowSession"/> gives it, and passes every key to it.
    /// It has frames of its own (<see cref="Frames"/>) because they are made for the size of this screen, and screens differ.
    ///
    /// A photo stands still, and is then drawn as it is (a copy of the pixels, as quick as it gets). While an effect lasts (see <see cref="SlideshowEffect"/>) the
    /// picture is put together by <see cref="FrameCompositor"/> at every tick of a timer, from the time that has passed rather than the ticks that came, so
    /// a computer that can not keep up shows fewer steps and not a longer effect.
    /// </summary>
    internal sealed class SlideshowScreen : Form
    {
        private const float WatermarkHeightDivisor = 40f; // the text is this much of the height of the screen
        private const float MinWatermarkFontPixels = 14f;
        private const int WatermarkBackgroundAlpha = 150;
        private const int AnimationIntervalMs = 10;       // Windows rounds it up to about 16: 60 steps a second at most
        private const int NoticeMs = 1800;                // how long the name of a new effect stays in the corner

        private readonly SlideshowSession _session;
        private readonly SlideshowWatermark _watermark;
        private readonly Font _watermarkFont;
        private readonly System.Windows.Forms.Timer _animation = new() { Interval = AnimationIntervalMs };
        private readonly System.Windows.Forms.Timer _noticeTimer = new() { Interval = NoticeMs };
        private readonly CompositeLayer[] _layers = new CompositeLayer[2];
        private string? _notice;                           // a few words in the top left corner (the effect that was just chosen), for a moment
        private PhotoOnScreen? _current;                           // what is on the screen; its frame belongs to Frames
        private PhotoOnScreen? _outgoing;                          // while one photo takes the place of another: the one that goes; also belongs to Frames
        private SlideshowTransition _transition;
        private long _transitionStartedAt;
        private double _transitionSeconds;
        private PixelCanvas? _canvas;                      // what the effects are put together on, the size of the screen
        private string? _message = SlideshowStrings.Loading;

        public SlideshowScreen(SlideshowSession session, Rectangle bounds, SlideshowWatermark watermark, long frameBytes, int minFrames, int parallelLoads)
        {
            _session = session;
            _watermark = watermark;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            ForeColor = Color.Gray;
            Text = "PhotoLiker";
            DoubleBuffered = true;

            _watermarkFont = new Font(Font.FontFamily, Math.Max(MinWatermarkFontPixels, bounds.Height / WatermarkHeightDivisor), FontStyle.Bold, GraphicsUnit.Pixel);
            _animation.Tick += (_, _) => OnAnimationTick();
            _noticeTimer.Tick += (_, _) => HideNotice();
            Frames = new SlideshowFrames(bounds.Size, frameBytes, minFrames, parallelLoads);
        }

        /// <summary>The photos made ready for this screen.</summary>
        public SlideshowFrames Frames { get; }

        /// <summary>Whether a photo is on this screen yet.</summary>
        public bool HasPhoto => _current is not null;

        /// <summary>The file of the photo that is leaving the screen while an effect lasts; null when none is. <see cref="Frames"/> must not let go of it before.</summary>
        public string? OutgoingPath => _outgoing?.Frame.Path;

        /// <summary>Whether an effect is under way: the photo is changing or moving.</summary>
        internal bool IsAnimating => _animation.Enabled;

        /// <summary>Puts a photo on the screen, in place of the one that is there, as <paramref name="plan"/> says.</summary>
        public void ShowFrame(SlideshowFrame frame, SlideshowShowPlan plan = default)
        {
            var previous = _current;
            _message = null;
            _outgoing = null;
            _transition = SlideshowTransition.None;

            if (previous is not null && !ReferenceEquals(previous.Frame, frame) && plan.Transition != SlideshowTransition.None && plan.TransitionSeconds > 0)
            {
                _outgoing = previous; // it goes on moving as it did while it leaves
                _transition = plan.Transition;
                _transitionStartedAt = plan.StartedAt;
                _transitionSeconds = plan.TransitionSeconds;
            }

            var path = plan.Motion && plan.MotionSeconds > 0 ? KenBurnsPath.Random(Random.Shared.NextDouble) : (KenBurnsPath?)null;
            _current = new PhotoOnScreen(frame, path, plan.StartedAt, plan.MotionSeconds);

            if (_outgoing is not null || path is not null)
                _animation.Start();
            else
                _animation.Stop();

            Invalidate();
        }

        /// <summary>
        /// Starts the slow movement of the photo that is on the screen if it has none: the effect was changed to one that moves the photos while the photo stays.
        /// A photo that is moving already goes on as it is, whatever the plan says; one that is not moving is not made to.
        /// </summary>
        public void StartMotion(SlideshowShowPlan plan)
        {
            if (_current is not { HasMotion: false } current || !plan.Motion || plan.MotionSeconds <= 0)
                return;

            _current = new PhotoOnScreen(current.Frame, KenBurnsPath.Random(Random.Shared.NextDouble), plan.StartedAt, plan.MotionSeconds);
            _animation.Start();
            Invalidate();
        }

        /// <summary>Writes a few words in the top left corner for a moment (what the Up and Down keys chose); a new one takes the place of the last.</summary>
        public void ShowNotice(string text)
        {
            _notice = text;
            _noticeTimer.Stop();
            _noticeTimer.Start();
            Invalidate();
        }

        private void HideNotice()
        {
            _noticeTimer.Stop();
            _notice = null;
            Invalidate();
        }

        /// <summary>Says something in the middle of an empty screen.</summary>
        public void ShowMessage(string text)
        {
            _message = text;
            Invalidate();
        }

        // ── effects ────────────────────────────────────────────────────────

        private void OnAnimationTick()
        {
            var now = Stopwatch.GetTimestamp();
            if (_outgoing is not null && TransitionProgress(now) >= 1)
            {
                _outgoing = null;
                _transition = SlideshowTransition.None;
            }

            Invalidate(); // this is the last picture of the effect if it is over: the paint after this finds it so

            if (_outgoing is null && _current?.IsMoving(now) != true)
                _animation.Stop();
        }

        /// <summary>How far the change of photo has come, 0 to 1 (1 when there is none).</summary>
        private double TransitionProgress(long now) =>
            _outgoing is null ? 1 : Math.Clamp(PhotoOnScreen.SecondsBetween(_transitionStartedAt, now) / _transitionSeconds, 0, 1);

        // ── keys ───────────────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            _session.HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animation.Stop();
            _noticeTimer.Stop();
            base.OnFormClosed(e);
            _session.ScreenClosed(this); // the others go with it
        }

        // ── painting ───────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_current is { } current)
            {
                var now = Stopwatch.GetTimestamp();
                var motion = current.MotionAt(now);

                if (_outgoing is null && motion.IsPlain)
                {
                    DrawPlain(g, current.Frame);
                    DrawWatermark(g, current.Frame.Taken, 1);
                }
                else
                {
                    DrawComposed(g, now, current, motion);
                }
            }
            else if (_message is not null)
            {
                TextRenderer.DrawText(g, _message, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }

            if (_notice is not null)
                DrawLabel(g, _notice, atRight: false, 1);
        }

        /// <summary>A photo standing still: the pixels of the frame, copied, with no smoothing: the frame was made for this screen.</summary>
        private void DrawPlain(Graphics g, SlideshowFrame frame)
        {
            var image = frame.Image;
            var bounds = new Rectangle((ClientSize.Width - image.Width) / 2, (ClientSize.Height - image.Height) / 2, image.Width, image.Height);

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, bounds);
            g.CompositingMode = CompositingMode.SourceOver;
        }

        /// <summary>An effect at this moment: the pictures put together on a canvas of the size of the screen, which is then copied on it.</summary>
        private void DrawComposed(Graphics g, long now, PhotoOnScreen current, SlideshowLayer currentMotion)
        {
            var width = ClientSize.Width;
            var height = ClientSize.Height;
            if (_canvas is null || _canvas.Width != width || _canvas.Height != height)
            {
                _canvas?.Dispose();
                _canvas = new PixelCanvas(width, height);
            }

            var count = 0;
            var progress = TransitionProgress(now);
            if (_outgoing is { } outgoing)
            {
                var (leaving, coming) = SlideshowEffects.Transition(_transition, progress);
                var comingWeight = WeightOf(coming.Alpha);

                // What dissolves is the whole screen, the black around a photo too: the shares of the two add up to all. What slides is not over one another
                var leavingWeight = _transition == SlideshowTransition.Slide ? WeightOf(leaving.Alpha) : FrameCompositor.FullWeight - comingWeight;
                _layers[count++] = LayerOf(outgoing.Frame, outgoing.MotionAt(now), leaving, leavingWeight, width, height);
                _layers[count++] = LayerOf(current.Frame, currentMotion, coming, comingWeight, width, height);
            }
            else
            {
                _layers[count++] = LayerOf(current.Frame, currentMotion, SlideshowLayer.Plain, FrameCompositor.FullWeight, width, height);
            }

            FrameCompositor.Compose(_canvas.Pixels, width, height, _layers.AsSpan(0, count));

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_canvas.Image, new Rectangle(0, 0, width, height));
            g.CompositingMode = CompositingMode.SourceOver;

            DrawWatermarks(g, progress);
        }

        private static int WeightOf(double alpha) => Math.Clamp((int)Math.Round(alpha * FrameCompositor.FullWeight), 0, FrameCompositor.FullWeight);

        /// <summary>Where a picture goes at this moment: the middle of the screen, moved by its own movement (a fraction of the picture) and by the change (a fraction of the screen, whole pixels).</summary>
        private static CompositeLayer LayerOf(SlideshowFrame frame, SlideshowLayer motion, SlideshowLayer change, int weight, int width, int height) =>
            new(frame.Pixels, frame.Width, frame.Height,
                width / 2.0 + motion.OffsetX * frame.Width + Math.Round(change.OffsetX * width),
                height / 2.0 + motion.OffsetY * frame.Height + Math.Round(change.OffsetY * height),
                motion.Scale * change.Scale,
                weight);

        /// <summary>
        /// The writing of the photos: while one photo takes the place of the other the old date fades out in the first half of the change and the new one fades in in
        /// the second, so two dates are never written over each other. A date that is the same for both is not touched.
        /// </summary>
        private void DrawWatermarks(Graphics g, double progress)
        {
            if (_current is not { } current)
                return;

            if (_outgoing is not { } outgoing)
            {
                DrawWatermark(g, current.Frame.Taken, 1);
                return;
            }

            var before = Slideshow.WatermarkText(_watermark, outgoing.Frame.Taken);
            var after = Slideshow.WatermarkText(_watermark, current.Frame.Taken);
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                DrawWatermark(g, current.Frame.Taken, 1);
                return;
            }

            DrawWatermark(g, outgoing.Frame.Taken, Math.Max(0, 1 - 2 * progress));
            DrawWatermark(g, current.Frame.Taken, Math.Max(0, 2 * progress - 1));
        }

        /// <summary>Writes the year or the date and time in the bottom right corner of the screen, white on a dark band so that it reads on any photo; <paramref name="opacity"/> 1 is as it is, less lets the photo through.</summary>
        private void DrawWatermark(Graphics g, DateTime? taken, double opacity)
        {
            if (Slideshow.WatermarkText(_watermark, taken) is { } text)
                DrawLabel(g, text, atRight: true, opacity);
        }

        /// <summary>Text, white on a dark band: in the bottom right corner (the watermark) or the top left one (the notice); <paramref name="opacity"/> 1 is as it is, less lets what is behind through.</summary>
        private void DrawLabel(Graphics g, string text, bool atRight, double opacity)
        {
            if (opacity <= 0)
                return;

            var format = StringFormat.GenericTypographic; // shared instance, must not be disposed
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            var size = g.MeasureString(text, _watermarkFont, PointF.Empty, format);
            var pad = _watermarkFont.Size / 3f;
            var margin = ClientSize.Height / 40f;

            var box = atRight
                ? new RectangleF(ClientSize.Width - margin - size.Width - pad * 2, ClientSize.Height - margin - size.Height - pad * 2, size.Width + pad * 2, size.Height + pad * 2)
                : new RectangleF(margin, margin, size.Width + pad * 2, size.Height + pad * 2);

            using var background = new SolidBrush(Color.FromArgb((int)Math.Round(WatermarkBackgroundAlpha * Math.Min(1, opacity)), Color.Black));
            using var ink = new SolidBrush(Color.FromArgb((int)Math.Round(255 * Math.Min(1, opacity)), Color.White));
            g.FillRectangle(background, box);
            g.DrawString(text, _watermarkFont, ink, box.Left + pad, box.Top + pad, format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _noticeTimer.Dispose();
                Frames.Dispose(); // the frames on the screen go with it
                _current = null;
                _outgoing = null;
                _canvas?.Dispose();
                _canvas = null;
                _watermarkFont.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>A photo on the screen: its frame, and how it moves while it stays (if it does) from the time it came.</summary>
        private sealed class PhotoOnScreen(SlideshowFrame frame, KenBurnsPath? path, long startedAt, double motionSeconds)
        {
            public SlideshowFrame Frame { get; } = frame;

            public static double SecondsBetween(long from, long to) => (to - from) / (double)Stopwatch.Frequency;

            private double Progress(long now) => path is null || motionSeconds <= 0 ? 1 : Math.Clamp(SecondsBetween(startedAt, now) / motionSeconds, 0, 1);

            /// <summary>Whether it moves at all (it may be done moving).</summary>
            public bool HasMotion => path is not null;

            /// <summary>Whether it is still moving; a photo that has reached the end of its movement stays as it is.</summary>
            public bool IsMoving(long now) => path is not null && Progress(now) < 1;

            /// <summary>How it is magnified and where it lies at this moment (offsets are fractions of the picture).</summary>
            public SlideshowLayer MotionAt(long now) => path is { } p ? p.At(Progress(now)) : SlideshowLayer.Plain;
        }
    }
}
