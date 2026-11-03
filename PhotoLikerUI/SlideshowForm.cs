using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PhotoLikerUI
{
    /// <summary>
    /// The slideshow: the photos of the list one after the other on a whole screen, black around them, with the year or the date and time they were
    /// taken in the bottom right corner (see <see cref="SlideshowWatermark"/>). Alt+Enter and Esc go back to the window.
    ///
    /// Right and Left show the photo next to the shown one in the list, whatever the mode is (in the random mode only the timer picks at random), and
    /// start the time of that photo over. Two things make them quick. The photos are ready before they are asked for (<see cref="SlideshowFrames"/>): the few
    /// next to the shown one, more of them in the direction the person is going, and the next random ones, which are decided ahead (<see cref="SlideshowShuffle"/>).
    /// And a key that comes while the photo it wants is still being made is kept (up to <see cref="MaxQueuedSteps"/>) and shown when it is there,
    /// the nearest one that is ready first, rather than skipped, so holding a key scrolls as fast as the photos can be made and never freezes.
    /// </summary>
    internal sealed class SlideshowForm : Form
    {
        private const long MaxFrameBytes = 512L * 1024 * 1024; // of screen-sized frames kept ready
        private const int MinFrames = 3;                       // kept whatever they weigh (an 8K screen has 130 MB frames)
        private const int PrefetchAhead = 6;                   // photos made ready in the direction the person goes ...
        private const int PrefetchBehind = 2;                  // ... and on the other side
        private const int PrefetchRandom = 3;                  // the next photos of the random order
        private const int MaxQueuedSteps = 3;                  // keys kept while their photo is being made
        private const float WatermarkHeightDivisor = 40f;      // the text is this much of the height of the screen
        private const float MinWatermarkFontPixels = 14f;
        private const int WatermarkBackgroundAlpha = 150;

        private readonly PhotoFile[] _photos;
        private readonly int _start;
        private readonly SlideshowWatermark _watermark;
        private readonly SlideshowFrames _frames;
        private readonly SlideshowShuffle? _shuffle; // in the random mode
        private readonly System.Windows.Forms.Timer _timer = new();
        private readonly Font _watermarkFont;

        private int _shown = -1;                // the place in the list of the photo on the screen
        private SlideshowFrame? _frame;         // what is on the screen; it belongs to _frames
        private int _direction = 1;             // the way the person goes, 1 right and -1 left
        private int _steps;                     // Right (+1) and Left (-1) keys not shown yet, up to MaxQueuedSteps either way
        private int? _jump;                     // the photo the timer or the start asks for, while it is being made
        private bool _scrolling;                // a key was used since the timer last picked a random photo
        private string? _message = SlideshowStrings.Loading;
        private bool _cursorHidden;

        public SlideshowForm(IReadOnlyList<PhotoFile> photos, int start, SlideshowMode mode, double timeoutSeconds, SlideshowWatermark watermark, Rectangle bounds)
        {
            ArgumentOutOfRangeException.ThrowIfZero(photos.Count);

            _photos = [.. photos];
            _start = Math.Clamp(start, 0, _photos.Length - 1);
            _watermark = watermark;
            _shuffle = mode == SlideshowMode.Random ? new SlideshowShuffle(_photos.Length, shown: _start) : null;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            ForeColor = Color.Gray;
            Text = "PhotoLiker";
            DoubleBuffered = true;

            _watermarkFont = new Font(Font.FontFamily, Math.Max(MinWatermarkFontPixels, bounds.Height / WatermarkHeightDivisor), FontStyle.Bold, GraphicsUnit.Pixel);

            _frames = new SlideshowFrames(bounds.Size, MaxFrameBytes, MinFrames);
            _frames.Loaded += (_, _) => Advance();
            _frames.LoadFailed += (_, _) => Advance();

            _timer.Interval = (int)Math.Round(Slideshow.TimeoutOf(timeoutSeconds) * 1000);
            _timer.Tick += (_, _) => AdvanceByTimer();
        }

        /// <summary>The file of the photo that was on the screen last; null if none was shown.</summary>
        public string? LastShownPath => _shown >= 0 ? _photos[_shown].OriginalFilePath : null;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            Cursor.Hide(); // nothing to point at; Esc goes back
            _cursorHidden = true;
            Begin();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ShowCursor();
            base.OnFormClosed(e);
        }

        private void ShowCursor()
        {
            if (_cursorHidden)
                Cursor.Show(); // Hide and Show count: every Hide needs its Show
            _cursorHidden = false;
        }

        /// <summary>Starts with the photo that was shown in the window.</summary>
        internal void Begin()
        {
            _jump = _start;
            Prefetch();
            Advance();
        }

        // ── keys ───────────────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

        internal bool HandleKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                case Keys.Alt | Keys.Enter: // the key that started it: the same key goes back, as full screen does everywhere
                    Close();
                    return true;
                case Keys.Right:
                    Step(1);
                    return true;
                case Keys.Left:
                    Step(-1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Right / Left: the photo next to the shown one in the list, in either mode.</summary>
        internal void Step(int direction)
        {
            if (_photos.Length < 2 || _shown < 0)
                return;

            _direction = direction;
            _scrolling = true;
            _jump = null; // the person takes over from the timer
            _steps = Math.Clamp(_steps + direction, -MaxQueuedSteps, MaxQueuedSteps);
            _timer.Stop(); // the photo that comes starts it again
            Prefetch();    // the way may have changed
            Advance();

            if (_steps == 0 && _jump is null)
                RestartTimer(); // Right and Left cancelled each other: nothing new comes to start it
        }

        // ── what comes next ────────────────────────────────────────────────

        private void AdvanceByTimer()
        {
            _timer.Stop(); // the photo that comes starts it again

            if (_shuffle is not null)
            {
                _scrolling = false;
                _steps = 0;
                _jump = _shuffle.Take(avoid: _shown);
            }
            else
            {
                _direction = 1;
                _steps = Math.Clamp(Math.Max(_steps, 0) + 1, 1, MaxQueuedSteps);
            }

            Prefetch();
            Advance();
        }

        /// <summary>
        /// Shows what is wanted (the photo the timer asked for, or the Right and Left keys that came) as far as it is ready. It is called again whenever
        /// a frame is made or fails, so what was not ready comes onto the screen the moment it is.
        /// </summary>
        private void Advance()
        {
            for (var attempts = 0; _jump is { } jump; attempts++)
            {
                var path = _photos[jump].OriginalFilePath;
                if (_frames.TryGet(path, out var frame))
                {
                    _jump = null;
                    ShowFrame(jump, frame);
                    return;
                }

                if (!_frames.HasFailed(path))
                    return; // still being made

                if (attempts >= _photos.Length)
                {
                    GiveUp(); // all of them were tried
                    return;
                }

                _jump = _shuffle is not null
                    ? _shuffle.Take(avoid: _shown)
                    : Slideshow.Move(jump, _photos.Length, 1, IsUnreadable);
                Prefetch(); // the new one first
            }

            while (_steps != 0 && TryStep())
            {
            }
        }

        /// <summary>
        /// Shows the photo that is as many steps away as the keys that came, or the nearest one before it that is ready: the person asked for them
        /// in this order, so none of the ones in between is lost, and none is waited for.
        /// </summary>
        private bool TryStep()
        {
            var direction = Math.Sign(_steps);
            for (var steps = Math.Abs(_steps); steps > 0; steps--)
            {
                var index = Slideshow.Move(_shown, _photos.Length, direction * steps, IsUnreadable);
                if (index != _shown && _frames.TryGet(_photos[index].OriginalFilePath, out var frame))
                {
                    _steps -= direction * steps;
                    ShowFrame(index, frame);
                    return true;
                }
            }
            return false;
        }

        private bool IsUnreadable(int index) => _frames.HasFailed(_photos[index].OriginalFilePath);

        /// <summary>No photo can be had: say so if the screen is empty, otherwise stay on the one that is there.</summary>
        private void GiveUp()
        {
            _jump = null;
            if (_frame is null)
            {
                _message = SlideshowStrings.NothingToShow;
                Invalidate();
            }
            else
            {
                RestartTimer();
            }
        }

        private void ShowFrame(int index, SlideshowFrame frame)
        {
            _shown = index;
            _frame = frame;
            _message = null;

            Prefetch(); // the photo that was shown before can go now
            Invalidate();
            RestartTimer();
        }

        private void RestartTimer()
        {
            if (_photos.Length < 2)
                return; // one photo stays

            _timer.Stop();
            _timer.Start();
        }

        /// <summary>Tells the frames which photos to have ready, the one that is wanted first at the front.</summary>
        private void Prefetch()
        {
            var count = _photos.Length;
            var center = _shown >= 0 ? _shown : _jump ?? _start;
            var order = new List<int>();
            var seen = new HashSet<int>();

            void Add(int index)
            {
                if (seen.Add(index))
                    order.Add(index);
            }

            if (_jump is { } jump)
                Add(jump);
            Add(center);

            // In the random mode the neighbours are there for the keys only, and every new photo has new neighbours: until a key is used there are
            // just the two next to it, then they follow the way the person goes
            var full = _shuffle is null || _scrolling;
            var ahead = full ? PrefetchAhead : 1;
            var behind = full ? PrefetchBehind : 1;

            var neighbours = new List<int>();
            for (var distance = 1; distance <= ahead; distance++)
            {
                neighbours.Add(Slideshow.Wrap(center + _direction * distance, count));
                if (distance <= behind)
                    neighbours.Add(Slideshow.Wrap(center - _direction * distance, count));
            }

            var random = _shuffle?.Upcoming(Math.Min(PrefetchRandom, count)) ?? [];

            // Taken in turns: a key may come any moment, and so may the timer
            for (var i = 0; i < Math.Max(neighbours.Count, random.Count); i++)
            {
                if (i < neighbours.Count)
                    Add(neighbours[i]);
                if (i < random.Count)
                    Add(random[i]);
            }

            _frames.SetWindow([.. order.Select(index => _photos[index])], LastShownPath);
        }

        // ── painting ───────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_frame is { } frame)
            {
                var image = frame.Image;
                var bounds = new Rectangle((ClientSize.Width - image.Width) / 2, (ClientSize.Height - image.Height) / 2, image.Width, image.Height);

                // A copy of the pixels, no smoothing: the frame was made for this screen
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, bounds);

                g.CompositingMode = CompositingMode.SourceOver;
                DrawWatermark(g, frame.Taken);
            }
            else if (_message is not null)
            {
                TextRenderer.DrawText(g, _message, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
        }

        /// <summary>Writes the year or the date and time in the bottom right corner of the screen, white on a dark band so that it reads on any photo.</summary>
        private void DrawWatermark(Graphics g, DateTime? taken)
        {
            var text = Slideshow.WatermarkText(_watermark, taken);
            if (text is null)
                return;

            var format = StringFormat.GenericTypographic; // shared instance, must not be disposed
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            var size = g.MeasureString(text, _watermarkFont, PointF.Empty, format);
            var pad = _watermarkFont.Size / 3f;
            var margin = ClientSize.Height / 40f;

            var box = new RectangleF(
                ClientSize.Width - margin - size.Width - pad * 2,
                ClientSize.Height - margin - size.Height - pad * 2,
                size.Width + pad * 2,
                size.Height + pad * 2);

            using var background = new SolidBrush(Color.FromArgb(WatermarkBackgroundAlpha, Color.Black));
            g.FillRectangle(background, box);
            g.DrawString(text, _watermarkFont, Brushes.White, box.Left + pad, box.Top + pad, format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ShowCursor();
                _timer.Dispose();
                _frames.Dispose(); // the frame on the screen goes with it
                _frame = null;
                _watermarkFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
