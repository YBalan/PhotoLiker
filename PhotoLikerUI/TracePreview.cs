using System.ComponentModel;
using System.Drawing.Drawing2D;
using PhotoLiker.TraceBitmap;

namespace PhotoLikerUI
{
    /// <summary>
    /// Shows the photo, or the vector shapes that were traced from it (on a white sheet), in the same place at the same size, so that switching between the two shows what the trace changed. The mouse wheel
    /// zooms where the pointer is, a drag moves the picture, a double click fits it into the window again.
    /// </summary>
    internal sealed class TracePreview : Control
    {
        private const float MinZoomFactor = 0.2f, MaxZoom = 64f;

        private Bitmap? _photo;
        private TraceResult? _trace;
        private List<(GraphicsPath Path, Color Color)> _layers = [];
        private bool _showTrace = true;
        private float _zoom = 1f;
        private PointF _offset;
        private bool _fitted = true;
        private Point _dragFrom;
        private bool _dragging;
        private readonly System.Windows.Forms.Timer _settle = new() { Interval = 160 };

        public TracePreview()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;

            // While the picture is being moved or zoomed it is drawn without smoothing, which is several times faster on a trace of tens of thousands of nodes; a moment after the last move it is drawn well again
            _settle.Tick += (_, _) =>
            {
                _settle.Stop();
                Moving = false;
                Invalidate();
            };
        }

        /// <summary>True while the picture is being moved or zoomed (and a moment after): it is drawn fast and rough then.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool Moving { get; set; }

        private void Moved()
        {
            Moving = true;
            _settle.Stop();
            _settle.Start();
        }

        /// <summary>The color around the sheet.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Surround { get; set; } = Color.FromArgb(60, 60, 60);

        /// <summary>The photo that is shown (the control keeps it; its size is the size the trace's coordinates are in).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap? Photo
        {
            get => _photo;
            set
            {
                _photo = value;
                Invalidate();
            }
        }

        /// <summary>The traced shapes that are shown when <see cref="ShowTrace"/> is on.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TraceResult? Trace
        {
            get => _trace;
            set
            {
                foreach (var (path, _) in _layers)
                    path.Dispose();

                _trace = value;
                _layers = value is null ? [] : [.. value.Layers.Select(layer => (ToPath(layer), Color.FromArgb(layer.Color.R, layer.Color.G, layer.Color.B)))];
                Invalidate();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowTrace
        {
            get => _showTrace;
            set
            {
                _showTrace = value;
                Invalidate();
            }
        }

        /// <summary>How many screen pixels one pixel of the picture is.</summary>
        public float Zoom => _fitted ? FitZoom() : _zoom;

        /// <summary>The size, in pixels, of the picture that is shown: the photo, or else the trace.</summary>
        private Size PictureSize => _photo is { } photo ? photo.Size : _trace is { } trace ? new Size(trace.Width, trace.Height) : Size.Empty;

        private float FitZoom()
        {
            var size = PictureSize;
            if (size.IsEmpty || ClientSize.Width < 1 || ClientSize.Height < 1)
                return 1f;

            return Math.Min((ClientSize.Width - 16f) / size.Width, (ClientSize.Height - 16f) / size.Height);
        }

        /// <summary>Fits the whole picture into the window.</summary>
        public void FitToWindow()
        {
            _fitted = true;
            Invalidate();
        }

        private PointF Origin()
        {
            if (_fitted)
            {
                var size = PictureSize;
                var zoom = FitZoom();
                return new PointF((ClientSize.Width - size.Width * zoom) / 2f, (ClientSize.Height - size.Height * zoom) / 2f);
            }

            return _offset;
        }

        private static GraphicsPath ToPath(TraceLayer layer)
        {
            var path = new GraphicsPath(FillMode.Alternate);
            foreach (var outline in layer.Paths)
            {
                path.StartFigure();
                var current = new PointF((float)outline.Start.X, (float)outline.Start.Y);
                foreach (var segment in outline.Segments)
                {
                    var end = new PointF((float)segment.End.X, (float)segment.End.Y);
                    if (segment.IsCurve)
                        path.AddBezier(current, new PointF((float)segment.Control1.X, (float)segment.Control1.Y), new PointF((float)segment.Control2.X, (float)segment.Control2.Y), end);
                    else
                        path.AddLine(current, end);
                    current = end;
                }

                path.CloseFigure();
            }

            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Surround);
            var size = PictureSize;
            if (size.IsEmpty)
                return;

            var zoom = Zoom;
            var origin = Origin();
            g.TranslateTransform(origin.X, origin.Y);
            g.ScaleTransform(zoom, zoom);

            if (_showTrace && _trace is not null)
            {
                g.FillRectangle(Brushes.White, 0, 0, size.Width, size.Height);
                g.SmoothingMode = Moving ? SmoothingMode.None : SmoothingMode.AntiAlias;
                foreach (var (path, color) in _layers)
                {
                    using var brush = new SolidBrush(color);
                    g.FillPath(brush, path);
                }
            }
            else if (_photo is not null)
            {
                g.InterpolationMode = zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_photo, new RectangleF(0, 0, size.Width, size.Height));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (PictureSize.IsEmpty)
                return;

            // Zoom so that the point of the picture under the pointer stays under it
            var before = Zoom;
            var origin = Origin();
            var factor = e.Delta > 0 ? 1.25f : 1 / 1.25f;
            var after = Math.Clamp(before * factor, FitZoom() * MinZoomFactor, MaxZoom);
            var pictureX = (e.X - origin.X) / before;
            var pictureY = (e.Y - origin.Y) / before;
            _zoom = after;
            _offset = new PointF(e.X - pictureX * after, e.Y - pictureY * after);
            _fitted = false;
            Moved();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            _dragFrom = e.Location;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
                return;

            var origin = Origin();
            _zoom = Zoom;
            _offset = new PointF(origin.X + e.X - _dragFrom.X, origin.Y + e.Y - _dragFrom.Y);
            _fitted = false;
            _dragFrom = e.Location;
            Moved();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            FitToWindow();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _settle.Dispose();
                foreach (var (path, _) in _layers)
                    path.Dispose();
                _layers = [];
            }

            base.Dispose(disposing);
        }
    }
}
