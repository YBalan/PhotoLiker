using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PhotoLikerUI
{
    /// <summary>
    /// The window of the slideshow on one screen: black, with the photo on it as large as the screen lets it be and the year or the date and time it was taken
    /// in the bottom right corner (see <see cref="SlideshowWatermark"/>). It only shows what <see cref="SlideshowSession"/> gives it, and passes every key to it.
    /// It has frames of its own (<see cref="Frames"/>) because they are made for the size of this screen, and screens differ.
    /// </summary>
    internal sealed class SlideshowScreen : Form
    {
        private const float WatermarkHeightDivisor = 40f; // the text is this much of the height of the screen
        private const float MinWatermarkFontPixels = 14f;
        private const int WatermarkBackgroundAlpha = 150;

        private readonly SlideshowSession _session;
        private readonly SlideshowWatermark _watermark;
        private readonly Font _watermarkFont;
        private SlideshowFrame? _frame; // what is on the screen; it belongs to Frames
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
            Frames = new SlideshowFrames(bounds.Size, frameBytes, minFrames, parallelLoads);
        }

        /// <summary>The photos made ready for this screen.</summary>
        public SlideshowFrames Frames { get; }

        /// <summary>Whether a photo is on this screen yet.</summary>
        public bool HasPhoto => _frame is not null;

        public void ShowFrame(SlideshowFrame frame)
        {
            _frame = frame;
            _message = null;
            Invalidate();
        }

        /// <summary>Says something in the middle of an empty screen.</summary>
        public void ShowMessage(string text)
        {
            _message = text;
            Invalidate();
        }

        // ── keys ───────────────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            _session.HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _session.ScreenClosed(this); // the others go with it
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
                Frames.Dispose(); // the frame on the screen goes with it
                _frame = null;
                _watermarkFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
