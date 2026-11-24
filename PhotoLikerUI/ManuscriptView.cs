using System.ComponentModel;
using System.Drawing.Drawing2D;
using OcrLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLikerUI
{
    /// <summary>
    /// The photo of a page in the Scan manuscripts window, with a box round each line the scan found: the whole photo to start with (fitted to the window), the wheel of the mouse zooms about the
    /// point it is over, dragging moves the photo, a double-click fits it again. A click on a box says which line it is (<see cref="LineClicked"/>); the line that is selected is drawn thicker and its
    /// box is brought into view (when the photo is zoomed in: a photo that is fitted is all in view, and stays where it is). The color of a box says how sure the reader was of the line: green,
    /// orange or red. The pictures that were found on the page are outlined in blue-green.
    /// </summary>
    internal sealed class ManuscriptView : Control
    {
        private const float MinimumZoom = 0.02f;
        private const float MaximumZoom = 16f;
        private const int DragThreshold = 4;

        private Bitmap? _image;
        private float _photoScale = 1f;      // the pixels of the image per pixel of the photo (the image may be made smaller than the photo)
        private float _zoom = 1f;            // the pixels of the screen per pixel of the image
        private PointF _origin;              // where the top left of the image is on the control
        private bool _fitted = true;
        private IReadOnlyList<OcrLine> _lines = [];
        private IReadOnlyList<PointF[]> _pictures = [];
        private int _selected = -1;
        private bool _showLines = true;
        private Point _downAt;
        private PointF _downOrigin;
        private bool _dragging;
        private bool _pressed;

        public ManuscriptView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.FromArgb(40, 40, 44);
        }

        /// <summary>Raised on a click on the box of a line, with the number of the line in <see cref="Lines"/>.</summary>
        public event Action<int>? LineClicked;

        /// <summary>The lines whose boxes are drawn, in the pixels of the photo.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<OcrLine> Lines
        {
            get => _lines;
            set
            {
                _lines = value ?? [];
                if (_selected >= _lines.Count)
                    _selected = -1;
                Invalidate();
            }
        }

        /// <summary>The outlines of the pictures found on the page, each a closed shape of corners in the pixels of the photo.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<PointF[]> Pictures
        {
            get => _pictures;
            set
            {
                _pictures = value ?? [];
                Invalidate();
            }
        }

        /// <summary>The line that is selected (drawn thicker, brought into view), -1 for none.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedLine
        {
            get => _selected;
            set
            {
                var line = value >= 0 && value < _lines.Count ? value : -1;
                if (line == _selected)
                    return;

                _selected = line;
                if (line >= 0)
                    BringIntoView(line);
                Invalidate();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowLines
        {
            get => _showLines;
            set
            {
                _showLines = value;
                Invalidate();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool HasImage => _image is not null;

        /// <summary>How large the photo is drawn: the pixels of the screen per pixel of the photo.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float ZoomOfPhoto => _zoom * _photoScale;

        /// <summary>Whether the photo is fitted to the window (until it is zoomed or moved).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsFitted => _fitted;

        /// <summary>Where the top left of the picture is on the control (for the tests).</summary>
        internal PointF Origin => _origin;

        /// <summary>The colour the outline of a picture is drawn in.</summary>
        internal static Color PictureColor => Color.FromArgb(0, 170, 190);

        /// <summary>The colour a line of this sureness is drawn in.</summary>
        internal static Color ColorOf(float confidence) =>
            confidence >= 0.8f ? Color.FromArgb(46, 184, 92) : confidence >= 0.5f ? Color.FromArgb(240, 160, 30) : Color.FromArgb(230, 70, 60);

        /// <summary>
        /// Shows a photo (the view keeps the picture and gives it back to nobody: it is disposed when it is replaced). <paramref name="photoWidth"/> is the width of the photo the boxes are in the pixels of,
        /// which may be more than the width of <paramref name="image"/> (a big photo is shown from a smaller picture).
        /// </summary>
        public void SetImage(Bitmap? image, int photoWidth)
        {
            var old = _image;
            _image = image;
            _photoScale = image is null || photoWidth <= 0 ? 1f : (float)image.Width / photoWidth;
            _lines = [];
            _pictures = [];
            _selected = -1;
            _fitted = true;
            Fit();
            old?.Dispose();
        }

        public void Fit()
        {
            _fitted = true;
            if (_image is null || ClientSize.Width < 4 || ClientSize.Height < 4)
            {
                Invalidate();
                return;
            }

            _zoom = Math.Min((float)ClientSize.Width / _image.Width, (float)ClientSize.Height / _image.Height);
            _zoom = Math.Clamp(_zoom, MinimumZoom, MaximumZoom);
            _origin = new PointF((ClientSize.Width - _image.Width * _zoom) / 2f, (ClientSize.Height - _image.Height * _zoom) / 2f);
            Invalidate();
        }

        /// <summary>Zooms by a factor about a point of the control (the middle when it is not given).</summary>
        public void ZoomBy(float factor, Point? about = null)
        {
            if (_image is null)
                return;

            var anchor = about ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
            var zoom = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);
            var ratio = zoom / _zoom;
            _origin = new PointF(anchor.X - (anchor.X - _origin.X) * ratio, anchor.Y - (anchor.Y - _origin.Y) * ratio);
            _zoom = zoom;
            _fitted = false;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_fitted)
                Fit();
        }

        // ── Drawing ─────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_image is null)
                return;

            g.InterpolationMode = _zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, new RectangleF(_origin.X, _origin.Y, _image.Width * _zoom, _image.Height * _zoom));

            if (!_showLines)
                return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPictures(g);
            for (var i = 0; i < _lines.Count; i++)
            {
                if (i == _selected)
                    continue;

                DrawBox(g, i, false);
            }

            if (_selected >= 0)
                DrawBox(g, _selected, true);
        }

        private void DrawPictures(Graphics g)
        {
            var scale = _zoom * _photoScale;
            foreach (var outline in _pictures)
            {
                if (outline.Length < 3)
                    continue;

                var points = outline.Select(point => new PointF(_origin.X + point.X * scale, _origin.Y + point.Y * scale)).ToArray();
                using var fill = new SolidBrush(Color.FromArgb(40, PictureColor));
                g.FillPolygon(fill, points);
                using var pen = new Pen(Color.FromArgb(230, PictureColor), 2.4f) { LineJoin = LineJoin.Round };
                g.DrawPolygon(pen, points);
            }
        }

        private void DrawBox(Graphics g, int index, bool selected)
        {
            var line = _lines[index];
            var points = Polygon(line);
            var colour = ColorOf(line.Confidence);
            if (selected)
            {
                using var fill = new SolidBrush(Color.FromArgb(55, 0, 122, 204));
                g.FillPolygon(fill, points);
                using var outline = new Pen(Color.FromArgb(0, 122, 204), 3f) { LineJoin = LineJoin.Round };
                g.DrawPolygon(outline, points);
                return;
            }

            using var pen = new Pen(Color.FromArgb(210, colour), 1.6f) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, points);
        }

        /// <summary>The corners of the box of a line on the control.</summary>
        private PointF[] Polygon(OcrLine line)
        {
            var scale = _zoom * _photoScale;
            return [.. line.Box.Corners.Select(corner => new PointF(_origin.X + corner.X * scale, _origin.Y + corner.Y * scale))];
        }

        private void BringIntoView(int index)
        {
            // A photo that is fitted is all in view, and a line at its edge is as near to the edge of the control as it can be: moving the photo to put that line in the middle would only take the
            // photo off the middle of the window (it did, whenever the first line of a page was selected as the page opened)
            if (_fitted || _image is null || index < 0 || index >= _lines.Count)
                return;

            var points = Polygon(_lines[index]);
            var left = points.Min(p => p.X);
            var right = points.Max(p => p.X);
            var top = points.Min(p => p.Y);
            var bottom = points.Max(p => p.Y);
            const int margin = 24;

            // Inside the control, with room: nothing to do. Otherwise it goes to the middle of the control
            if (left >= margin && right <= ClientSize.Width - margin && top >= margin && bottom <= ClientSize.Height - margin)
                return;

            _origin = new PointF(_origin.X + ClientSize.Width / 2f - (left + right) / 2f, _origin.Y + ClientSize.Height / 2f - (top + bottom) / 2f);
            _fitted = false;
            Invalidate();
        }

        // ── The mouse ───────────────────────────────────────────────────────

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomBy(e.Delta > 0 ? 1.15f : 1f / 1.15f, e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left)
                return;

            _pressed = true;
            _dragging = false;
            _downAt = e.Location;
            _downOrigin = _origin;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_pressed || _image is null)
                return;

            if (!_dragging && (Math.Abs(e.X - _downAt.X) > DragThreshold || Math.Abs(e.Y - _downAt.Y) > DragThreshold))
            {
                _dragging = true;
                Cursor = Cursors.SizeAll;
            }

            if (_dragging)
            {
                _origin = new PointF(_downOrigin.X + e.X - _downAt.X, _downOrigin.Y + e.Y - _downAt.Y);
                _fitted = false;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            var wasClick = _pressed && !_dragging;
            _pressed = false;
            _dragging = false;
            Cursor = Cursors.Default;
            if (wasClick)
                ClickAt(e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Fit();
        }

        /// <summary>The line whose box holds the point (the last drawn of those that do, which is the one on top), -1 for none.</summary>
        internal int LineAt(Point point)
        {
            if (!_showLines)
                return -1;

            for (var i = _lines.Count - 1; i >= 0; i--)
            {
                using var path = new GraphicsPath();
                path.AddPolygon(Polygon(_lines[i]));
                if (path.IsVisible(point))
                    return i;
            }

            return -1;
        }

        private void ClickAt(Point point)
        {
            var line = LineAt(point);
            if (line < 0)
                return;

            _selected = line;
            Invalidate();
            LineClicked?.Invoke(line);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
                _image = null;
            }

            base.Dispose(disposing);
        }
    }
}
