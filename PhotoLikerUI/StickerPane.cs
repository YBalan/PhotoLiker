using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PhotoLikerUI
{
    /// <summary>What a sticker is shown on in its window (it is only the view: the file has no background).</summary>
    internal enum StickerBackdrop
    {
        /// <summary>The grey and white squares that mean "see-through".</summary>
        Checker,

        /// <summary>The dark blue of Telegram's night chat.</summary>
        Dark,

        /// <summary>The pale blue of Telegram's day chat.</summary>
        Light,
    }

    /// <summary>
    /// A picture shown as large as it fits in the space (never enlarged past what a person can use, never distorted), in the middle of it, optionally on a backdrop: the Telegram Sticker window has one
    /// for the photo with its cut line and one for the sticker. With no picture it says what it is waiting for.
    /// </summary>
    internal sealed class StickerPane : Control
    {
        private const int Edge = 6;
        private const int CheckerSize = 8;

        private Bitmap? _picture;
        private StickerBackdrop _backdrop = StickerBackdrop.Checker;
        private string _emptyText = string.Empty;
        private bool _dark;

        public StickerPane()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>The picture; the pane owns it and lets go of the one it had before. Null for none.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap? Picture
        {
            get => _picture;
            set
            {
                var old = _picture;
                _picture = value;
                if (!ReferenceEquals(old, value))
                    old?.Dispose();
                Invalidate();
            }
        }

        /// <summary>Whether the picture has a backdrop behind it (the photo does not, the sticker does).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowBackdrop { get; set; }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public StickerBackdrop Backdrop
        {
            get => _backdrop;
            set
            {
                _backdrop = value;
                Invalidate();
            }
        }

        /// <summary>The colour round the picture.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Surround { get; set; } = Color.Gray;

        /// <summary>What the pane says when it has no picture.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EmptyText
        {
            get => _emptyText;
            set
            {
                _emptyText = value;
                Invalidate();
            }
        }

        /// <summary>Whether the window is dark (the squares of the checkerboard are then dark too).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Dark
        {
            get => _dark;
            set
            {
                _dark = value;
                Invalidate();
            }
        }

        /// <summary>Where the picture is drawn (in the pane's own coordinates), or an empty rectangle when there is none.</summary>
        public Rectangle PictureRectangle
        {
            get
            {
                if (_picture is null || _picture.Width <= 0 || _picture.Height <= 0)
                    return Rectangle.Empty;

                var room = new Rectangle(Edge, Edge, Math.Max(1, Width - 2 * Edge), Math.Max(1, Height - 2 * Edge));
                var scale = Math.Min(room.Width / (double)_picture.Width, room.Height / (double)_picture.Height);
                var width = Math.Max(1, (int)Math.Round(_picture.Width * scale));
                var height = Math.Max(1, (int)Math.Round(_picture.Height * scale));
                return new Rectangle(room.X + (room.Width - width) / 2, room.Y + (room.Height - height) / 2, width, height);
            }
        }

        /// <summary>The colour of a backdrop that is a plain colour.</summary>
        internal static Color PlainBackdrop(StickerBackdrop backdrop) => backdrop == StickerBackdrop.Dark ? Color.FromArgb(14, 22, 33) : Color.FromArgb(214, 228, 238);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Surround);

            var rectangle = PictureRectangle;
            if (_picture is null || rectangle.IsEmpty)
            {
                if (_emptyText.Length > 0)
                {
                    using var brush = new SolidBrush(ForeColor);
                    using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(_emptyText, Font, brush, new RectangleF(Edge, Edge, Width - 2 * Edge, Height - 2 * Edge), format);
                }
                return;
            }

            if (ShowBackdrop)
                DrawBackdrop(g, rectangle);

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_picture, rectangle);
        }

        private void DrawBackdrop(Graphics g, Rectangle rectangle)
        {
            if (_backdrop != StickerBackdrop.Checker)
            {
                using var plain = new SolidBrush(PlainBackdrop(_backdrop));
                g.FillRectangle(plain, rectangle);
                return;
            }

            using var light = new SolidBrush(_dark ? Color.FromArgb(68, 68, 72) : Color.FromArgb(255, 255, 255));
            using var shade = new SolidBrush(_dark ? Color.FromArgb(48, 48, 52) : Color.FromArgb(214, 214, 218));
            var state = g.Save();
            g.SetClip(rectangle);
            g.FillRectangle(light, rectangle);
            for (var y = rectangle.Top; y < rectangle.Bottom; y += CheckerSize)
            {
                for (var x = rectangle.Left + (((y - rectangle.Top) / CheckerSize) % 2 == 0 ? 0 : CheckerSize); x < rectangle.Right; x += 2 * CheckerSize)
                    g.FillRectangle(shade, x, y, CheckerSize, CheckerSize);
            }
            g.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _picture?.Dispose();
                _picture = null;
            }
            base.Dispose(disposing);
        }
    }
}
