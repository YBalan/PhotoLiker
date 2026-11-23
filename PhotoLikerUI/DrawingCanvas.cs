using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PhotoLiker.Drawing;
using PhotoLiker.Drawing.Ocr;

namespace PhotoLikerUI
{
    /// <summary>The tools of the Draw window.</summary>
    internal enum DrawTool
    {
        Pen,
        Highlighter,
        Line,
        Text,
        Crop,
    }

    /// <summary>
    /// The picture of the Draw window: the photo with what was drawn on it, shown as large as the room allows (never larger than it is), with the pen, the highlighter, the line, the text and the crop frame
    /// done on it with the mouse. What is drawn is kept in a <see cref="DrawingSession"/> (so it can be taken back); a stroke in progress is shown here and put in the session when the button goes up.
    /// The widths and the size of the text are in pixels of the screen, as they look, whatever the size of the photo is.
    /// </summary>
    internal sealed class DrawingCanvas : Control
    {
        /// <summary>The room round the picture.</summary>
        private const int Border = 8;

        /// <summary>How much wider than a pen the highlighter is, and how much of what is below it shows through.</summary>
        public const float HighlighterWidthFactor = 2.4f;
        public const byte HighlighterAlpha = 105;

        /// <summary>How far, in pixels of the screen, the edge of a crop frame is taken hold of.</summary>
        private const int HandleReach = 9;

        private DrawingSession? _session;
        private Bitmap? _shown;
        private Rectangle _view;
        private float _scaleX = 1;
        private float _scaleY = 1;

        private DrawTool _tool = DrawTool.Pen;
        private Color _penColor = Color.FromArgb(230, 30, 40);
        private int _penWidth = 5;
        private int _textSize = 28;
        private bool _readOnly;

        // A stroke or a line in progress, in the pixels of the picture
        private List<PointF>? _live;
        private bool _drawing;

        // The crop frame
        private CropFrame? _crop;
        private CropHandle _grab = CropHandle.None;
        private bool _dragging;
        private Point _cropAnchor;
        private Point _cropLast;

        // A note being typed
        private TextBox? _note;
        private Point _noteAt;

        // The lines of text that a scan found
        private IReadOnlyList<TextRegion> _marks = [];

        public DrawingCanvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            TabStop = true;
            Cursor = Cursors.Cross;
            Name = "drawingCanvas";
        }

        // ── What it shows and how it draws ──────────────────────────────────

        /// <summary>The colour round the picture.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Surround { get; set; } = Color.FromArgb(40, 40, 42);

        /// <summary>The photo with its steps. Setting it shows the picture again, from the start.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DrawingSession? Session
        {
            get => _session;
            set
            {
                CancelNote();
                _session = value;
                _crop = null;
                _marks = [];
                ResetCropFrame();
                PictureChanged();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DrawTool Tool
        {
            get => _tool;
            set
            {
                if (value == _tool)
                    return;

                CommitNote();
                _tool = value;
                _live = null;
                _drawing = false;
                ResetCropFrame();
                if (IsHandleCreated)
                    UpdateCursor(PointToClient(MousePosition));
                Invalidate();
            }
        }

        /// <summary>The colour of the pen, the line, the highlighter and the text.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color PenColor
        {
            get => _penColor;
            set
            {
                _penColor = Color.FromArgb(255, value);
                if (_note is not null)
                {
                    _note.ForeColor = _penColor;
                    _note.BackColor = NoteBack(_penColor);
                }
                Invalidate();
            }
        }

        /// <summary>How thick the pen and the line are, as they look on the screen (1 to 80).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int PenWidth
        {
            get => _penWidth;
            set => _penWidth = Math.Clamp(value, 1, 80);
        }

        /// <summary>How large the letters of a note are, as they look on the screen (8 to 300).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TextSize
        {
            get => _textSize;
            set
            {
                _textSize = Math.Clamp(value, 8, 300);
                if (_note is not null)
                {
                    _note.Font = NoteFont();
                    FitNote();
                }
            }
        }

        /// <summary>True while the picture must not be changed (a scan is reading it).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ReadOnly
        {
            get => _readOnly;
            set
            {
                _readOnly = value;
                if (value)
                {
                    _live = null;
                    _drawing = false;
                    _dragging = false;
                }
                Invalidate();
            }
        }

        /// <summary>Raised when a stroke, a note or a crop was put on the picture by the mouse or the keyboard.</summary>
        public event EventHandler? Edited;

        /// <summary>Raised when the crop frame appears, moves, changes or goes.</summary>
        public event EventHandler? CropChanged;

        /// <summary>How large the picture is shown on the screen (1 is as large as it is). Not <c>Scale</c>: a control has a method of that name.</summary>
        public float ViewScale => _scaleX;

        /// <summary>The rectangle of the picture in the control.</summary>
        public Rectangle PictureBounds => _view;

        /// <summary>The part of the picture inside the crop frame; null when there is no frame or it holds the whole picture.</summary>
        public Rectangle? CropArea => _crop is { IsCrop: true } ? _crop.Area : null;

        /// <summary>True when a note is being typed.</summary>
        public bool IsTyping => _note is not null;

        /// <summary>The note that is being typed (for the tests, which type into it).</summary>
        internal TextBox? NoteBox => _note;

        public IReadOnlyList<TextRegion> Marks => _marks;

        /// <summary>The picture is not what was shown: it is drawn again (after an undo, a redo, or a new session).</summary>
        public void PictureChanged()
        {
            RebuildShown();
            ResetCropFrame(); // a picture of another size (a crop was taken back or brought back) has a frame of its own size
            Invalidate();
        }

        /// <summary>Shows where a scan found lines of text (null or empty: nothing is shown).</summary>
        public void ShowMarks(IReadOnlyList<TextRegion>? marks)
        {
            _marks = marks ?? [];
            Invalidate();
        }

        // ── Where things are ────────────────────────────────────────────────

        /// <summary>The place in the picture (its pixels) of a point of the control.</summary>
        public PointF ToImage(Point point) => new((point.X - _view.X) / _scaleX, (point.Y - _view.Y) / _scaleY);

        /// <summary>The point of the control a place in the picture is shown at.</summary>
        public PointF ToControl(PointF place) => new(_view.X + place.X * _scaleX, _view.Y + place.Y * _scaleY);

        private Point ToImageRounded(Point point)
        {
            var place = ToImage(point);
            return new Point((int)Math.Round(place.X), (int)Math.Round(place.Y));
        }

        private void RebuildShown()
        {
            _shown?.Dispose();
            _shown = null;
            if (_session is null || Width < 1 || Height < 1)
            {
                _view = Rectangle.Empty;
                return;
            }

            var picture = _session.Picture;
            var room = new Size(Math.Max(1, Width - 2 * Border), Math.Max(1, Height - 2 * Border));
            var scale = Math.Min(1f, Math.Min((float)room.Width / picture.Width, (float)room.Height / picture.Height));
            var width = Math.Max(1, (int)Math.Round(picture.Width * scale));
            var height = Math.Max(1, (int)Math.Round(picture.Height * scale));
            _scaleX = (float)width / picture.Width;
            _scaleY = (float)height / picture.Height;
            _view = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
            _shown = Shrunk(picture, width, height);
        }

        /// <summary>The picture drawn at another size: straight from the pixels (they are held still while it is drawn, so nothing is copied).</summary>
        private static Bitmap Shrunk(Bitmap32 picture, int width, int height)
        {
            var handle = GCHandle.Alloc(picture.Bgra, GCHandleType.Pinned);
            try
            {
                using var source = new Bitmap(picture.Width, picture.Height, picture.Width * 4, PixelFormat.Format32bppArgb, handle.AddrOfPinnedObject());
                var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using var graphics = Graphics.FromImage(result);
                graphics.InterpolationMode = width < picture.Width ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, attributes);
                return result;
            }
            finally
            {
                handle.Free();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_session is null)
                return;

            RebuildShown();
            FitNote();
            Invalidate();
        }

        // ── Painting ────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Surround);
            if (_shown is null)
                return;

            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            DrawBackdrop(graphics, _view);
            graphics.DrawImageUnscaled(_shown, _view.Location);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PaintLive(graphics);
            PaintMarks(graphics);
            PaintCrop(graphics);
            graphics.SmoothingMode = SmoothingMode.Default;
        }

        /// <summary>A grey check under the picture, which shows through where the photo is see-through.</summary>
        private static void DrawBackdrop(Graphics graphics, Rectangle area)
        {
            using var light = new SolidBrush(Color.FromArgb(235, 235, 235));
            using var dark = new SolidBrush(Color.FromArgb(200, 200, 200));
            graphics.FillRectangle(light, area);
            const int cell = 8;
            for (var y = 0; y * cell < area.Height; y++)
            {
                for (var x = (y & 1); x * cell < area.Width; x += 2)
                    graphics.FillRectangle(dark, area.X + x * cell, area.Y + y * cell, Math.Min(cell, area.Width - x * cell), Math.Min(cell, area.Height - y * cell));
            }
        }

        private void PaintLive(Graphics graphics)
        {
            if (_live is not { Count: > 0 } live)
                return;

            var colour = _tool == DrawTool.Highlighter ? Color.FromArgb(HighlighterAlpha, _penColor) : _penColor;
            var width = ScreenWidth();
            var points = live.Select(point => ToControl(point)).ToArray();
            if (points.Length == 1)
            {
                using var dot = new SolidBrush(colour);
                graphics.FillEllipse(dot, points[0].X - width / 2, points[0].Y - width / 2, width, width);
                return;
            }

            using var pen = new Pen(colour, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            graphics.DrawLines(pen, points);
        }

        private void PaintMarks(Graphics graphics)
        {
            if (_marks.Count == 0)
                return;

            using var fill = new SolidBrush(Color.FromArgb(50, 0, 160, 255));
            using var edge = new Pen(Color.FromArgb(230, 0, 160, 255), 2f);
            foreach (var mark in _marks)
            {
                var corners = mark.Corners.Select(ToControl).ToArray();
                graphics.FillPolygon(fill, corners);
                graphics.DrawPolygon(edge, corners);
            }
        }

        private void PaintCrop(Graphics graphics)
        {
            if (_crop is null || _tool != DrawTool.Crop)
                return;

            var area = _crop.Area;
            var topLeft = ToControl(area.Location);
            var bottomRight = ToControl(new PointF(area.Right, area.Bottom));
            var frame = Rectangle.FromLTRB((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y));

            using (var veil = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                graphics.FillRectangle(veil, _view.Left, _view.Top, _view.Width, Math.Max(0, frame.Top - _view.Top));
                graphics.FillRectangle(veil, _view.Left, frame.Bottom, _view.Width, Math.Max(0, _view.Bottom - frame.Bottom));
                graphics.FillRectangle(veil, _view.Left, frame.Top, Math.Max(0, frame.Left - _view.Left), frame.Height);
                graphics.FillRectangle(veil, frame.Right, frame.Top, Math.Max(0, _view.Right - frame.Right), frame.Height);
            }

            using (var white = new Pen(Color.White, 1f))
            using (var black = new Pen(Color.Black, 1f) { DashStyle = DashStyle.Dash })
            {
                graphics.DrawRectangle(white, frame);
                graphics.DrawRectangle(black, frame);
            }

            // The handles: the four corners and the middles of the four edges
            const int size = 8;
            var middleX = frame.Left + frame.Width / 2;
            var middleY = frame.Top + frame.Height / 2;
            foreach (var (x, y) in new[] { (frame.Left, frame.Top), (middleX, frame.Top), (frame.Right, frame.Top), (frame.Right, middleY), (frame.Right, frame.Bottom), (middleX, frame.Bottom), (frame.Left, frame.Bottom), (frame.Left, middleY) })
            {
                var handle = new Rectangle(x - size / 2, y - size / 2, size, size);
                graphics.FillRectangle(Brushes.White, handle);
                graphics.DrawRectangle(Pens.Black, handle);
            }
        }

        // ── The mouse ───────────────────────────────────────────────────────

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            Capture = true;
            PointerDown(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drawing || _dragging)
                PointerMove(e.Location);
            else
                UpdateCursor(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            Capture = false;
            PointerUp(e.Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && (_drawing || _dragging))
            {
                // The window lost the mouse (another window came up): the stroke ends where it is
                PointerUp(PointToClient(MousePosition));
            }
        }

        /// <summary>The left button goes down at a point of the control (the mouse does this; a test does it without a mouse).</summary>
        internal void PointerDown(Point point)
        {
            if (_session is null || _readOnly || _shown is null)
                return;

            switch (_tool)
            {
                case DrawTool.Pen or DrawTool.Highlighter or DrawTool.Line:
                    var place = Clamped(ToImage(point));
                    _live = [place];
                    _drawing = true;
                    Invalidate();
                    break;

                case DrawTool.Text:
                    CommitNote();
                    StartNote(point);
                    break;

                case DrawTool.Crop:
                    BeginCropDrag(point);
                    break;
            }
        }

        /// <summary>The pointer moves with the button down.</summary>
        internal void PointerMove(Point point)
        {
            if (_session is null || _readOnly)
                return;

            if (_drawing && _live is { } live)
            {
                var place = Clamped(ToImage(point));
                if (_tool == DrawTool.Line)
                {
                    live.RemoveRange(1, live.Count - 1);
                    live.Add(place);
                }
                else if (live.Count == 0 || Distance(ToControl(live[^1]), ToControl(place)) >= 1.5f)
                {
                    live.Add(place);
                }
                Invalidate();
            }
            else if (_dragging && _crop is not null)
            {
                DragCrop(point);
            }
        }

        /// <summary>The left button goes up: a stroke is put on the picture, a crop frame is let go of.</summary>
        internal void PointerUp(Point point)
        {
            if (_drawing && _live is { } live && _session is not null)
            {
                PointerMove(point);
                _drawing = false;
                _live = null;
                if (live.Count > 0)
                {
                    var (colour, width) = StrokeLook();
                    if (_tool == DrawTool.Line && live.Count == 2 && live[0] == live[1])
                        live.RemoveAt(1);
                    _session.Add(new StrokeStep(live.ToArray(), colour, width));
                    AfterEdit();
                }
                else
                {
                    Invalidate();
                }
            }

            if (_dragging)
            {
                _dragging = false;
                _grab = CropHandle.None;
                Invalidate();
            }
        }

        private (Color Colour, float Width) StrokeLook()
        {
            var width = Math.Max(Painter.MinimumWidth, ScreenWidth() / _scaleX);
            return _tool == DrawTool.Highlighter ? (Color.FromArgb(HighlighterAlpha, _penColor), width) : (_penColor, width);
        }

        /// <summary>The thickness of what is being drawn, in pixels of the screen.</summary>
        private float ScreenWidth() => _penWidth * (_tool == DrawTool.Highlighter ? HighlighterWidthFactor : 1f);

        private PointF Clamped(PointF place)
        {
            var bounds = _session!.Picture;
            return new PointF(Math.Clamp(place.X, 0, bounds.Width), Math.Clamp(place.Y, 0, bounds.Height));
        }

        private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private void AfterEdit()
        {
            _marks = [];
            RebuildShown();
            Invalidate();
            Edited?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateCursor(Point point)
        {
            if (_tool != DrawTool.Crop || _crop is null || _shown is null)
            {
                Cursor = _tool == DrawTool.Text ? Cursors.IBeam : Cursors.Cross;
                return;
            }

            Cursor = _crop.HitTest(ToImageRounded(point), HandleReachInImage()) switch
            {
                CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
                CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
                CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
                CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
                CropHandle.Inside => Cursors.SizeAll,
                _ => Cursors.Cross,
            };
        }

        // ── The crop frame ──────────────────────────────────────────────────

        private int HandleReachInImage() => Math.Max(1, (int)Math.Ceiling(HandleReach / Math.Max(0.01f, _scaleX)));

        /// <summary>The frame is the whole picture while the Crop tool is picked, and gone for any other.</summary>
        private void ResetCropFrame()
        {
            var had = _crop is not null;
            _crop = _tool == DrawTool.Crop && _session is not null ? new CropFrame(_session.Picture.Bounds.Size) : null;
            _dragging = false;
            _grab = CropHandle.None;
            if (had || _crop is not null)
                CropChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BeginCropDrag(Point point)
        {
            _crop ??= new CropFrame(_session!.Picture.Bounds.Size);
            var place = ToImageRounded(point);
            _grab = _crop.HitTest(place, HandleReachInImage());
            _cropLast = place;
            _cropAnchor = place;
            _dragging = true;
            if (_grab == CropHandle.None || (_grab == CropHandle.Inside && !_crop.IsCrop))
            {
                // Not on the frame (or on the frame that is the whole picture): a new frame is drawn from here
                _grab = CropHandle.None;
            }
        }

        private void DragCrop(Point point)
        {
            var place = ToImageRounded(point);
            if (_grab == CropHandle.None)
                _crop!.Draw(_cropAnchor, place);
            else
                _crop!.Drag(_grab, new Size(place.X - _cropLast.X, place.Y - _cropLast.Y));

            _cropLast = place;
            Invalidate();
            CropChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Cuts the picture to the frame (the frame is the whole picture again after). False when there is no frame that cuts anything.</summary>
        public bool ApplyCrop()
        {
            if (_session is null || _crop is not { IsCrop: true } crop)
                return false;

            _session.Add(new CropStep(crop.Area));
            ResetCropFrame();
            AfterEdit();
            return true;
        }

        /// <summary>Forgets the frame: it is the whole picture again.</summary>
        public void CancelCrop()
        {
            if (_crop is null)
                return;

            _crop.Reset();
            Invalidate();
            CropChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── A note of text ──────────────────────────────────────────────────

        private static Color NoteBack(Color text) => (text.R * 299 + text.G * 587 + text.B * 114) / 1000 > 140 ? Color.FromArgb(50, 50, 52) : Color.FromArgb(240, 240, 240);

        private Font NoteFont() => new(TextMasks.FontName, _textSize, FontStyle.Regular, GraphicsUnit.Pixel);

        private void StartNote(Point point)
        {
            _noteAt = ToImageRounded(point);
            _note = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Font = NoteFont(),
                ForeColor = _penColor,
                BackColor = NoteBack(_penColor),
                Location = point,
                Name = "drawingNote",
            };
            _note.TextChanged += (_, _) => FitNote();
            _note.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    e.SuppressKeyPress = true;
                    CommitNote();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    CancelNote();
                }
            };
            Controls.Add(_note);
            FitNote();
            _note.Focus();
            Invalidate();
        }

        /// <summary>The box of the note is as large as its text, with a little room to type in.</summary>
        private void FitNote()
        {
            if (_note is null)
                return;

            var size = TextRenderer.MeasureText(_note.Text.Length == 0 ? "M" : _note.Text + "M", _note.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var lines = Math.Max(1, _note.Text.Split('\n').Length);
            _note.Size = new Size(size.Width + 6, Math.Max(size.Height, _note.Font.Height * lines) + 4);
            _note.Location = Point.Round(ToControl(_noteAt));
        }

        /// <summary>Puts the note on the picture (nothing happens when there is none, or it says nothing).</summary>
        public void CommitNote()
        {
            if (_note is null)
                return;

            var text = _note.Text.TrimEnd();
            var colour = _note.ForeColor;
            var at = _noteAt;
            CancelNote();
            if (_session is null || text.Length == 0)
                return;

            if (TextMasks.Render(text, _textSize / _scaleX) is { } mask)
            {
                _session.Add(new TextStep(new Point(at.X - TextMasks.Margin, at.Y - TextMasks.Margin), mask, colour, text));
                AfterEdit();
            }
        }

        /// <summary>Throws the note away.</summary>
        public void CancelNote()
        {
            if (_note is null)
                return;

            var note = _note;
            _note = null;
            Controls.Remove(note);
            note.Dispose();
            Focus();
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Enter or Keys.Escape || base.IsInputKey(keyData);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shown?.Dispose();
                _shown = null;
            }
            base.Dispose(disposing);
        }
    }
}
