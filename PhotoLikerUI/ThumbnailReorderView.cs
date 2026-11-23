using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PhotoLikerUI
{
    /// <summary>
    /// The photos of a book as thumbnails, in the order of the book, which is changed here: drag the picked photos to a place (a line shows where they will go, and the view scrolls when
    /// the pointer is at its top or bottom), step them with Alt and the arrows, send them to the start or the end, put them at a number, reverse or sort all. Ctrl and Shift pick
    /// several, Delete leaves the picked photos out of the book (they stay in the list, dimmed). Every change can be undone. The photos are read, small, only when they come on the
    /// screen (see <see cref="ThumbnailCache"/>), so a list of thousands opens at once.
    /// </summary>
    internal sealed class ThumbnailReorderView : ScrollableControl
    {
        private const int MinThumbnail = 80;
        private const int MaxThumbnail = 320;
        private const int UndoDepth = 100;
        private const int AutoScrollEdge = 36;

        private List<BookEntry> _items = [];
        private readonly SortedSet<int> _selected = [];
        private int _focus = -1;
        private int _anchor = -1;
        private int _thumbnailSize = 150;
        private ThumbnailCache _cache;
        private readonly ToolTip _toolTip = new();
        private int _tipIndex = -1;

        private readonly Stack<Snapshot> _undo = new();
        private readonly Stack<Snapshot> _redo = new();

        // A press that may become a drag: where it was, and what it did to the selection if it was not a drag
        private bool _pressed;
        private Point _pressPoint;
        private int _pressIndex = -1;
        private bool _collapseOnRelease;
        private bool _dragging;
        private Point _mouse;
        private int _dropIndex = -1;
        private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 40 };

        public ThumbnailReorderView(ThumbnailCache? cache = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AutoScroll = true;
            DoubleBuffered = true;

            _cache = cache ?? new ThumbnailCache();
            _cache.Loaded += OnThumbnailLoaded;
            _scrollTimer.Tick += (_, _) => AutoScrollWhileDragging();
        }

        // ── What it shows ───────────────────────────────────────────────────

        /// <summary>The colour of the picked photos, the line that shows where they go, and the marks.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = SystemColors.Highlight;

        /// <summary>Said when the list is empty.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EmptyText { get; set; } = string.Empty;

        /// <summary>The label of a photo that is left out.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string LeftOutText { get; set; } = "left out";

        /// <summary>The tip of the mark that says there is a text above a photo, and the one for below.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string AboveTip { get; set; } = string.Empty;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string BelowTip { get; set; } = string.Empty;

        public IReadOnlyList<BookEntry> Entries => _items;

        /// <summary>The places of the picked photos, in order.</summary>
        public IReadOnlyList<int> SelectedIndices => [.. _selected];

        /// <summary>The place of the photo with the keyboard focus; -1 when there is none.</summary>
        public int FocusIndex => _focus;

        /// <summary>The photo the pointer is dragging them to: the place in front of which they will be put (the count of the list is the end); -1 when nothing is dragged.</summary>
        public int DropIndex => _dragging ? _dropIndex : -1;

        public bool IsDragging => _dragging;

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        /// <summary>The size of the thumbnails in pixels (80 to 320).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ThumbnailSize
        {
            get => _thumbnailSize;
            set
            {
                value = Math.Clamp(value, MinThumbnail, MaxThumbnail);
                if (value == _thumbnailSize)
                    return;

                _thumbnailSize = value;
                UpdateScrollSize();
                if (_focus >= 0)
                    EnsureVisible(_focus);
                Invalidate();
            }
        }

        /// <summary>Raised when the picked photos are not the same as before.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Raised when the order, or which photos are left out, has changed (by a person, or by undo).</summary>
        public event EventHandler? OrderChanged;

        /// <summary>Raised when a photo is double-clicked: its place in the list.</summary>
        public event EventHandler<int>? EntryActivated;

        /// <summary>The photos to show, in the order they have. The list is kept (and changed by the moves); nothing is picked, and what could be undone is forgotten.</summary>
        public void SetEntries(List<BookEntry> entries)
        {
            _items = entries;
            _selected.Clear();
            _focus = entries.Count > 0 ? 0 : -1;
            _anchor = _focus;
            _undo.Clear();
            _redo.Clear();
            CancelDrag();
            AutoScrollPosition = Point.Empty;
            UpdateScrollSize();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Picks these photos (and no others); the first of them gets the focus and is brought into view.</summary>
        public void Select(params int[] indices) => SetSelection(indices, indices.Length > 0 ? indices.Min() : _focus);

        private void SetSelection(IEnumerable<int> indices, int focus, bool keepAnchor = false)
        {
            var wanted = indices.Where(index => index >= 0 && index < _items.Count).ToList();
            var changed = !_selected.SetEquals(wanted);
            _selected.Clear();
            foreach (var index in wanted)
                _selected.Add(index);

            _focus = Math.Clamp(focus, -1, _items.Count - 1);
            if (!keepAnchor)
                _anchor = _focus;
            if (_focus >= 0)
                EnsureVisible(_focus);

            Invalidate();
            if (changed)
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Where the cells are ─────────────────────────────────────────────

        private float DpiScale => DeviceDpi / 96f;

        private int Pad => (int)Math.Round(8 * DpiScale);

        private int CaptionHeight => (int)Math.Round(20 * DpiScale);

        private int ThumbHeight => _thumbnailSize * 3 / 4;

        private int CellWidth => _thumbnailSize + 2 * Pad;

        private int CellHeight => ThumbHeight + CaptionHeight + 2 * Pad;

        /// <summary>How many cells are in a row. The room the scroll bar takes is always counted, so that it showing up does not change the rows.</summary>
        public int Columns => Math.Max(1, (ClientSize.Width - (VScroll ? 0 : SystemInformation.VerticalScrollBarWidth)) / CellWidth);

        private int Rows => _items.Count == 0 ? 0 : (_items.Count + Columns - 1) / Columns;

        private void UpdateScrollSize() => AutoScrollMinSize = new Size(0, Rows * CellHeight);

        /// <summary>The rectangle of a cell in the coordinates of the window (scrolled).</summary>
        public Rectangle CellBounds(int index)
        {
            var column = index % Columns;
            var row = index / Columns;
            return new Rectangle(column * CellWidth + AutoScrollPosition.X, row * CellHeight + AutoScrollPosition.Y, CellWidth, CellHeight);
        }

        /// <summary>The photo under a point of the window, or -1.</summary>
        public int IndexAt(Point point)
        {
            var x = point.X - AutoScrollPosition.X;
            var y = point.Y - AutoScrollPosition.Y;
            if (x < 0 || y < 0 || x >= Columns * CellWidth)
                return -1;

            var index = (y / CellHeight) * Columns + x / CellWidth;
            return index < _items.Count ? index : -1;
        }

        /// <summary>
        /// Where photos dropped at a point go: in front of the photo whose left half the point is in, behind the one whose right half it is in, at the end when it is under the last row, and in front
        /// of the first photo of the row for a point in the empty room at the end of a row.
        /// </summary>
        public int InsertionIndexAt(Point point)
        {
            if (_items.Count == 0)
                return 0;

            var x = point.X - AutoScrollPosition.X;
            var y = point.Y - AutoScrollPosition.Y;
            var row = Math.Clamp(y / CellHeight, 0, Rows - 1);
            if (y < 0)
                return 0;
            if (y >= Rows * CellHeight)
                return _items.Count;

            var column = Math.Clamp(x / CellWidth, 0, Columns);
            var index = row * Columns + column;
            if (column < Columns && x >= 0 && x % CellWidth >= CellWidth / 2)
                index++; // the right half of a cell: behind it

            return Math.Clamp(index, 0, _items.Count);
        }

        /// <summary>The places that are on the screen now (first and last, both included).</summary>
        private (int First, int Last) VisibleRange()
        {
            if (_items.Count == 0)
                return (0, -1);

            var top = -AutoScrollPosition.Y;
            var firstRow = Math.Max(0, top / CellHeight);
            var lastRow = Math.Min(Rows - 1, (top + ClientSize.Height) / CellHeight);
            return (firstRow * Columns, Math.Min(_items.Count - 1, (lastRow + 1) * Columns - 1));
        }

        /// <summary>Scrolls so that the cell of a photo is all in view.</summary>
        public void EnsureVisible(int index)
        {
            if (index < 0 || index >= _items.Count)
                return;

            var cell = CellBounds(index);
            var shift = 0;
            if (cell.Top < 0)
                shift = cell.Top;
            else if (cell.Bottom > ClientSize.Height)
                shift = cell.Bottom - ClientSize.Height;

            if (shift != 0)
                AutoScrollPosition = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y + shift);
        }

        // ── Painting ────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);

            if (_items.Count == 0)
            {
                TextRenderer.DrawText(graphics, EmptyText, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            var (first, last) = VisibleRange();
            _cache.Want(Enumerable.Range(first, last - first + 1).Select(index => _items[index].Path)); // before the photos are asked for, or they would be skipped as off the screen

            for (var index = first; index <= last; index++)
            {
                var cell = CellBounds(index);
                if (cell.IntersectsWith(e.ClipRectangle))
                    PaintCell(graphics, index, cell);
            }

            if (_dragging)
                PaintDrop(graphics);
        }

        private void PaintCell(Graphics graphics, int index, Rectangle cell)
        {
            var entry = _items[index];
            var selected = _selected.Contains(index);
            var card = Rectangle.Inflate(cell, -Pad / 2, -Pad / 2);

            if (selected)
            {
                using var fill = new SolidBrush(Color.FromArgb(70, AccentColor));
                using var border = new Pen(AccentColor, 2f);
                graphics.FillRectangle(fill, card);
                graphics.DrawRectangle(border, card.X, card.Y, card.Width - 1, card.Height - 1);
            }
            else if (index == _focus && Focused)
            {
                using var border = new Pen(ForeColor) { DashStyle = DashStyle.Dot };
                graphics.DrawRectangle(border, card.X, card.Y, card.Width - 1, card.Height - 1);
            }

            var box = new Rectangle(cell.X + Pad, cell.Y + Pad, _thumbnailSize, ThumbHeight);
            var bitmap = _cache.TryGet(entry.Path, out var failed);
            PaintThumbnail(graphics, box, bitmap, failed, entry.LeftOut, entry.Rotation);

            // The number the photo has in the book (a photo that is left out has none)
            var number = entry.LeftOut ? "-" : (index + 1 - _items.Take(index).Count(other => other.LeftOut)).ToString();
            PaintBadge(graphics, number, new Point(box.Left + 3, box.Top + 3), Color.FromArgb(200, 30, 30, 30), Color.White);

            // The marks of a text above and below
            var right = box.Right - 3;
            if (!string.IsNullOrWhiteSpace(entry.BelowRtf))
            {
                PaintArrow(graphics, new Rectangle(right - 14, box.Top + 3, 14, 14), up: false);
                right -= 17;
            }
            if (!string.IsNullOrWhiteSpace(entry.AboveRtf))
                PaintArrow(graphics, new Rectangle(right - 14, box.Top + 3, 14, 14), up: true);

            var caption = new Rectangle(cell.X + Pad, box.Bottom + 2, _thumbnailSize, CaptionHeight - 2);
            TextRenderer.DrawText(graphics, Path.GetFileName(entry.Path), Font, caption, entry.LeftOut ? SystemColors.GrayText : ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void PaintThumbnail(Graphics graphics, Rectangle box, Bitmap? bitmap, bool failed, bool leftOut, int rotation)
        {
            using (var back = new SolidBrush(Color.FromArgb(leftOut ? 40 : 70, ForeColor)))
                graphics.FillRectangle(back, box);

            if (bitmap is null)
            {
                TextRenderer.DrawText(graphics, failed ? "?" : "…", Font, box, failed ? Color.IndianRed : SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            // Fitted into the box, in proportion, as it is turned in the book (a quarter turn puts it on its side)
            var sideways = BookRotation.IsSideways(rotation);
            var shownWidth = sideways ? bitmap.Height : bitmap.Width;
            var shownHeight = sideways ? bitmap.Width : bitmap.Height;
            var scale = Math.Min((double)box.Width / shownWidth, (double)box.Height / shownHeight);
            var width = (int)Math.Round(shownWidth * scale);
            var height = (int)Math.Round(shownHeight * scale);
            var target = new Rectangle(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height);

            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            if (leftOut)
            {
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.3f }, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                DrawTurned(graphics, bitmap, target, rotation, attributes);

                using var line = new Pen(Color.FromArgb(160, Color.IndianRed), 2f);
                graphics.DrawLine(line, box.Left + 6, box.Top + 6, box.Right - 6, box.Bottom - 6);
                graphics.DrawLine(line, box.Right - 6, box.Top + 6, box.Left + 6, box.Bottom - 6);
                using var shade = new SolidBrush(Color.FromArgb(190, 30, 30, 30));
                var label = TextRenderer.MeasureText(LeftOutText, Font);
                var strip = new Rectangle(box.X + (box.Width - label.Width - 10) / 2, box.Bottom - label.Height - 8, label.Width + 10, label.Height + 2);
                graphics.FillRectangle(shade, strip);
                TextRenderer.DrawText(graphics, LeftOutText, Font, strip, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                DrawTurned(graphics, bitmap, target, rotation, null);
            }
        }

        /// <summary>
        /// Draws the thumbnail turned clockwise by the turn of the photo (0, 90, 180, 270) into the rectangle that holds it as turned: the corners of the picture are put on the corners of
        /// the rectangle, which is how GDI+ turns a picture without making another one.
        /// </summary>
        private static void DrawTurned(Graphics graphics, Bitmap bitmap, Rectangle target, int rotation, ImageAttributes? attributes)
        {
            // Where the top left, the top right and the bottom left corner of the picture go
            Point[] corners = BookRotation.Normalize(rotation) switch
            {
                90 => [new(target.Right, target.Top), new(target.Right, target.Bottom), new(target.Left, target.Top)],
                180 => [new(target.Right, target.Bottom), new(target.Left, target.Bottom), new(target.Right, target.Top)],
                270 => [new(target.Left, target.Bottom), new(target.Left, target.Top), new(target.Right, target.Bottom)],
                _ => [new(target.Left, target.Top), new(target.Right, target.Top), new(target.Left, target.Bottom)],
            };
            graphics.DrawImage(bitmap, corners, new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel, attributes);
        }

        private void PaintBadge(Graphics graphics, string text, Point at, Color back, Color fore)
        {
            var size = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding);
            var rectangle = new Rectangle(at.X, at.Y, size.Width + 8, size.Height + 2);
            using var brush = new SolidBrush(back);
            graphics.FillRectangle(brush, rectangle);
            TextRenderer.DrawText(graphics, text, Font, rectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>The mark of a text: a small square with a triangle that points to where the text is (up: above the photo).</summary>
        private void PaintArrow(Graphics graphics, Rectangle square, bool up)
        {
            using var back = new SolidBrush(AccentColor);
            using var arrow = new SolidBrush(Color.White);
            graphics.FillRectangle(back, square);
            var middle = square.X + square.Width / 2f;
            PointF[] triangle = up
                ? [new(middle, square.Y + 3), new(square.Right - 3, square.Bottom - 4), new(square.X + 3, square.Bottom - 4)]
                : [new(middle, square.Bottom - 3), new(square.Right - 3, square.Y + 4), new(square.X + 3, square.Y + 4)];
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPolygon(arrow, triangle);
            graphics.SmoothingMode = smoothing;
        }

        /// <summary>The line where the dragged photos will go, and the first of them under the pointer.</summary>
        private void PaintDrop(Graphics graphics)
        {
            if (_dropIndex >= 0)
            {
                Rectangle at;
                if (_dropIndex < _items.Count)
                {
                    var cell = CellBounds(_dropIndex);
                    at = new Rectangle(cell.Left + Pad / 2 - 2, cell.Top + Pad / 2, 4, cell.Height - Pad);
                }
                else
                {
                    var cell = CellBounds(_items.Count - 1);
                    at = new Rectangle(cell.Right - Pad / 2 - 2, cell.Top + Pad / 2, 4, cell.Height - Pad);
                }

                using var brush = new SolidBrush(AccentColor);
                graphics.FillRectangle(brush, at);
            }

            var lead = _selected.Count > 0 ? _items[_selected.Min] : null;
            if (lead is not null)
            {
                var bitmap = _cache.TryGet(lead.Path, out _);
                var box = new Rectangle(_mouse.X - _thumbnailSize / 4, _mouse.Y - ThumbHeight / 4, _thumbnailSize / 2, ThumbHeight / 2);
                if (bitmap is not null)
                {
                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.7f }, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    var sideways = BookRotation.IsSideways(lead.Rotation);
                    var shownWidth = sideways ? bitmap.Height : bitmap.Width;
                    var shownHeight = sideways ? bitmap.Width : bitmap.Height;
                    var scale = Math.Min((double)box.Width / shownWidth, (double)box.Height / shownHeight);
                    var target = new Rectangle(box.X, box.Y, (int)(shownWidth * scale), (int)(shownHeight * scale));
                    DrawTurned(graphics, bitmap, target, lead.Rotation, attributes);
                }
                else
                {
                    using var brush = new SolidBrush(Color.FromArgb(120, AccentColor));
                    graphics.FillRectangle(brush, box);
                }

                if (_selected.Count > 1)
                    PaintBadge(graphics, _selected.Count.ToString(), new Point(box.Left - 6, box.Top - 6), AccentColor, Color.White);
            }
        }

        private void OnThumbnailLoaded(string path)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            var (first, last) = VisibleRange();
            for (var index = first; index <= last; index++)
            {
                if (string.Equals(_items[index].Path, path, StringComparison.OrdinalIgnoreCase))
                    Invalidate(CellBounds(index));
            }
        }

        // ── The mouse ───────────────────────────────────────────────────────

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left)
                Press(e.Location, ModifierKeys);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_pressed)
                DragTo(e.Location);
            else
                ShowTip(e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
                Activate(e.Location);
        }

        /// <summary>The double-click of the left button at a point (the mouse does this; a test does it without a mouse): the photo there is opened.</summary>
        internal void Activate(Point point)
        {
            var index = IndexAt(point);
            if (index >= 0)
                EntryActivated?.Invoke(this, index);
        }

        /// <summary>The file of a photo was changed: its thumbnail is read again from it.</summary>
        public void ReloadThumbnail(string path)
        {
            _cache.Forget(path);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _pressed)
                Release(e.Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_pressed && !Capture)
                CancelDrag(); // the window lost the mouse (another window came up): the drag is over
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollSize();
        }

        /// <summary>The press of the left button at a point (the mouse does this; a test does it without a mouse).</summary>
        internal void Press(Point point, Keys modifiers)
        {
            var index = IndexAt(point);
            if (index < 0)
            {
                if (!modifiers.HasFlag(Keys.Control) && !modifiers.HasFlag(Keys.Shift))
                    SetSelection([], _focus);
                return;
            }

            _collapseOnRelease = false;
            if (modifiers.HasFlag(Keys.Shift) && _anchor >= 0)
            {
                var from = Math.Min(_anchor, index);
                var to = Math.Max(_anchor, index);
                SetSelection(Enumerable.Range(from, to - from + 1), index, keepAnchor: true);
            }
            else if (modifiers.HasFlag(Keys.Control))
            {
                var picked = new SortedSet<int>(_selected);
                if (!picked.Remove(index))
                    picked.Add(index);
                SetSelection(picked, index);
            }
            else if (_selected.Contains(index))
            {
                // On a photo of a selection: it may be the start of a drag of all of them, so the selection is kept until the button goes up without a drag
                _collapseOnRelease = _selected.Count > 1;
                _focus = index;
                _anchor = index;
                Invalidate();
            }
            else
            {
                SetSelection([index], index);
            }

            _pressed = true;
            _pressPoint = point;
            _pressIndex = index;
            _mouse = point;
            Capture = true;
        }

        /// <summary>The pointer moves, with the button down: after a few pixels it is a drag.</summary>
        internal void DragTo(Point point)
        {
            if (!_pressed)
                return;

            _mouse = point;
            if (!_dragging)
            {
                var slop = SystemInformation.DragSize;
                if (Math.Abs(point.X - _pressPoint.X) < slop.Width / 2 && Math.Abs(point.Y - _pressPoint.Y) < slop.Height / 2)
                    return;

                if (!_selected.Contains(_pressIndex))
                    return;

                _dragging = true;
                _collapseOnRelease = false;
                Cursor = Cursors.SizeAll;
                _scrollTimer.Start();
            }

            _dropIndex = InsertionIndexAt(point);
            Invalidate();
        }

        /// <summary>The button goes up: a drag ends where the pointer is, and photos are moved there; a press without a drag was a click.</summary>
        internal void Release(Point point)
        {
            var wasDragging = _dragging;
            var drop = wasDragging ? InsertionIndexAt(point) : -1;
            var collapse = _collapseOnRelease;
            var index = _pressIndex;
            EndPress();

            if (wasDragging)
            {
                if (ClientRectangle.Contains(point))
                    MoveSelected(drop);
            }
            else if (collapse)
            {
                SetSelection([index], index); // a click on a photo of a selection of several picks that one
            }
        }

        /// <summary>Ends a drag without moving anything (Esc, or the window lost the mouse).</summary>
        internal void CancelDrag()
        {
            EndPress();
            Invalidate();
        }

        private void EndPress()
        {
            _pressed = false;
            _dragging = false;
            _collapseOnRelease = false;
            _dropIndex = -1;
            _pressIndex = -1;
            _scrollTimer.Stop();
            Cursor = Cursors.Default;
            if (Capture)
                Capture = false;
            Invalidate();
        }

        private void AutoScrollWhileDragging()
        {
            if (!_dragging)
                return;

            var step = 0;
            if (_mouse.Y < AutoScrollEdge)
                step = -Math.Max(8, (AutoScrollEdge - _mouse.Y) / 2);
            else if (_mouse.Y > ClientSize.Height - AutoScrollEdge)
                step = Math.Max(8, (_mouse.Y - (ClientSize.Height - AutoScrollEdge)) / 2);

            if (step == 0)
                return;

            AutoScrollPosition = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y + step);
            _dropIndex = InsertionIndexAt(_mouse);
            Invalidate();
        }

        private void ShowTip(Point point)
        {
            var index = IndexAt(point);
            if (index == _tipIndex)
                return;

            _tipIndex = index;
            if (index < 0)
            {
                _toolTip.SetToolTip(this, null);
                return;
            }

            var entry = _items[index];
            var lines = new List<string> { entry.Path };
            if (entry.LeftOut)
                lines.Add(LeftOutText);
            if (!string.IsNullOrWhiteSpace(entry.AboveRtf) && AboveTip.Length > 0)
                lines.Add(AboveTip);
            if (!string.IsNullOrWhiteSpace(entry.BelowRtf) && BelowTip.Length > 0)
                lines.Add(BelowTip);
            _toolTip.SetToolTip(this, string.Join(Environment.NewLine, lines));
        }

        // ── The keyboard ────────────────────────────────────────────────────

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Delete || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            e.Handled = HandleKey(e.KeyData);
            e.SuppressKeyPress = e.Handled;
        }

        /// <summary>A key pressed in the view; true when it was one the view uses.</summary>
        internal bool HandleKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var alt = keyData.HasFlag(Keys.Alt);
            var ctrl = keyData.HasFlag(Keys.Control);
            var shift = keyData.HasFlag(Keys.Shift);
            var columns = Columns;
            var page = Math.Max(1, ClientSize.Height / CellHeight) * columns;

            if (key == Keys.Escape && _pressed)
            {
                CancelDrag();
                return true;
            }

            if (alt) // the picked photos are moved
            {
                switch (key)
                {
                    case Keys.Left: MoveSelectedBy(-1); return true;
                    case Keys.Right: MoveSelectedBy(1); return true;
                    case Keys.Up: MoveSelectedBy(-columns); return true;
                    case Keys.Down: MoveSelectedBy(columns); return true;
                    case Keys.Home: MoveSelectedToStart(); return true;
                    case Keys.End: MoveSelectedToEnd(); return true;
                }
                return false;
            }

            if (ctrl)
            {
                switch (key)
                {
                    case Keys.A: SetSelection(Enumerable.Range(0, _items.Count), _focus); return true;
                    case Keys.Z: Undo(); return true;
                    case Keys.Y: Redo(); return true;
                    case Keys.Oemplus or Keys.Add: RotateSelected(1); return true;
                    case Keys.OemMinus or Keys.Subtract: RotateSelected(-1); return true;
                }
                return false;
            }

            int target;
            switch (key)
            {
                case Keys.Left: target = _focus - 1; break;
                case Keys.Right: target = _focus + 1; break;
                case Keys.Up: target = _focus - columns; break;
                case Keys.Down: target = _focus + columns; break;
                case Keys.Home: target = 0; break;
                case Keys.End: target = _items.Count - 1; break;
                case Keys.PageUp: target = _focus - page; break;
                case Keys.PageDown: target = _focus + page; break;
                case Keys.Delete: ToggleLeftOut(); return true;
                default: return false;
            }

            if (_items.Count == 0)
                return true;

            target = Math.Clamp(target, 0, _items.Count - 1);
            if (_focus < 0)
                target = 0;

            if (shift && _anchor >= 0)
            {
                var from = Math.Min(_anchor, target);
                var to = Math.Max(_anchor, target);
                SetSelection(Enumerable.Range(from, to - from + 1), target, keepAnchor: true);
            }
            else
            {
                SetSelection([target], target);
            }

            return true;
        }

        // ── Changing the order ──────────────────────────────────────────────

        /// <summary>What can be undone: the order, which photos are left out, and what was picked.</summary>
        private sealed record Snapshot(BookEntry[] Order, bool[] LeftOut, int[] Rotation, int[] Selected, int Focus);

        private Snapshot TakeSnapshot() => new([.. _items], [.. _items.Select(entry => entry.LeftOut)], [.. _items.Select(entry => entry.Rotation)], [.. _selected], _focus);

        private static bool Same(Snapshot a, Snapshot b) => a.Order.SequenceEqual(b.Order) && a.LeftOut.SequenceEqual(b.LeftOut) && a.Rotation.SequenceEqual(b.Rotation);

        private void Restore(Snapshot snapshot)
        {
            _items.Clear();
            _items.AddRange(snapshot.Order);
            for (var i = 0; i < snapshot.Order.Length; i++)
            {
                snapshot.Order[i].LeftOut = snapshot.LeftOut[i];
                snapshot.Order[i].Rotation = snapshot.Rotation[i];
            }

            UpdateScrollSize();
            SetSelection(snapshot.Selected, snapshot.Focus);
            Invalidate();
            OrderChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Does a change of the order, so that it can be undone; nothing is noted when it changed nothing.</summary>
        private void Change(Func<IEnumerable<int>?> change)
        {
            var before = TakeSnapshot();
            var afterSelection = change();
            if (Same(before, TakeSnapshot()))
                return;

            _undo.Push(before);
            while (_undo.Count > UndoDepth)
            {
                var kept = _undo.Reverse().Skip(1).Reverse().ToArray(); // the oldest goes
                _undo.Clear();
                foreach (var snapshot in kept.Reverse())
                    _undo.Push(snapshot);
            }

            _redo.Clear();
            if (afterSelection is not null)
                SetSelection(afterSelection, afterSelection.DefaultIfEmpty(_focus).Min());

            UpdateScrollSize();
            Invalidate();
            OrderChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts the picked photos in front of the photo at <paramref name="insertBefore"/> (the count of the list is the end).</summary>
        public void MoveSelected(int insertBefore) => Change(() => BookOrder.Move(_items, _selected, insertBefore));

        /// <summary>Moves each picked photo by some places (negative: earlier).</summary>
        public void MoveSelectedBy(int places) => Change(() => BookOrder.Nudge(_items, _selected, places));

        public void MoveSelectedToStart() => Change(() => BookOrder.ToStart(_items, _selected));

        public void MoveSelectedToEnd() => Change(() => BookOrder.ToEnd(_items, _selected));

        /// <summary>Puts the picked photos so that the first of them is at this number (counting from 1).</summary>
        public void MoveSelectedToPlace(int number) => Change(() => BookOrder.ToPlace(_items, _selected, number));

        public void ReverseAll() => Change(() =>
        {
            var picked = _selected.Select(index => _items[index]).ToList();
            BookOrder.Reverse(_items);
            return picked.Select(entry => _items.IndexOf(entry));
        });

        /// <summary>Puts all the photos in an order (the picked ones stay picked).</summary>
        public void SortAll(PhotoSortOrder order) => Change(() =>
        {
            var picked = _selected.Select(index => _items[index]).ToList();
            BookOrder.Sort(_items, order);
            return picked.Select(entry => _items.IndexOf(entry));
        });

        /// <summary>Leaves the picked photos out of the book, or puts them back when they were all left out.</summary>
        public void ToggleLeftOut() => Change(() =>
        {
            if (_selected.Count == 0)
                return null;

            var leave = _selected.Any(index => !_items[index].LeftOut);
            foreach (var index in _selected)
                _items[index].LeftOut = leave;
            return _selected.ToList();
        });

        /// <summary>
        /// Turns each picked photo in the book by quarter turns (positive: clockwise), on top of how it is turned already. Only the book turns them: the photo files are not changed.
        /// </summary>
        public void RotateSelected(int quarterTurns) => Change(() =>
        {
            if (_selected.Count == 0)
                return null;

            foreach (var index in _selected)
                _items[index].Rotation = BookRotation.Add(_items[index].Rotation, quarterTurns);
            return _selected.ToList();
        });

        public void Undo()
        {
            if (_undo.Count == 0)
                return;

            _redo.Push(TakeSnapshot());
            Restore(_undo.Pop());
        }

        public void Redo()
        {
            if (_redo.Count == 0)
                return;

            _undo.Push(TakeSnapshot());
            Restore(_redo.Pop());
        }

        /// <summary>Tells the view that what is written for a photo changed, so that its marks are drawn again.</summary>
        public void RefreshEntry(int index)
        {
            if (index >= 0 && index < _items.Count)
                Invalidate(CellBounds(index));
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scrollTimer.Dispose();
                _toolTip.Dispose();
                _cache.Loaded -= OnThumbnailLoaded;
                _cache.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
