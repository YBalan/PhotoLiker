using System.Drawing;

namespace PhotoLiker.Drawing
{
    /// <summary>The parts of a crop frame that can be taken hold of with the mouse.</summary>
    public enum CropHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,

        /// <summary>The inside of the frame: it is moved as a whole.</summary>
        Inside,
    }

    /// <summary>
    /// The frame a person draws over a picture to say what is to be kept: made by dragging over the picture, then moved by its inside and made larger or smaller by its edges and corners, the way the
    /// crop of the Snipping Tool is. It never leaves the picture and is never smaller than <see cref="MinimumSide"/> pixels. All in the pixels of the picture.
    /// </summary>
    public sealed class CropFrame
    {
        /// <summary>The smallest side of a frame, in pixels of the picture.</summary>
        public const int MinimumSide = 8;

        private readonly Rectangle _bounds;
        private Rectangle _area;

        public CropFrame(Size picture, Rectangle? area = null)
        {
            if (picture.Width < 1 || picture.Height < 1)
                throw new ArgumentOutOfRangeException(nameof(picture), "A picture has at least one pixel.");

            _bounds = new Rectangle(Point.Empty, picture);
            _area = Keep(area ?? _bounds);
        }

        /// <summary>The part of the picture inside the frame.</summary>
        public Rectangle Area => _area;

        /// <summary>The frame that is made by dragging from <paramref name="from"/> to <paramref name="to"/> (in either direction), cut to the picture.</summary>
        public static Rectangle FromDrag(Point from, Point to, Size picture)
        {
            var left = Math.Clamp(Math.Min(from.X, to.X), 0, picture.Width);
            var top = Math.Clamp(Math.Min(from.Y, to.Y), 0, picture.Height);
            var right = Math.Clamp(Math.Max(from.X, to.X), 0, picture.Width);
            var bottom = Math.Clamp(Math.Max(from.Y, to.Y), 0, picture.Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>Makes a new frame by dragging from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public void Draw(Point from, Point to) => _area = Keep(FromDrag(from, to, _bounds.Size));

        /// <summary>The part of the frame that is at <paramref name="point"/>, where an edge or a corner takes in <paramref name="reach"/> pixels around it (None outside the frame and its reach).</summary>
        public CropHandle HitTest(Point point, int reach)
        {
            var nearLeft = Math.Abs(point.X - _area.Left) <= reach;
            var nearRight = Math.Abs(point.X - _area.Right) <= reach;
            var nearTop = Math.Abs(point.Y - _area.Top) <= reach;
            var nearBottom = Math.Abs(point.Y - _area.Bottom) <= reach;
            var withinX = point.X >= _area.Left - reach && point.X <= _area.Right + reach;
            var withinY = point.Y >= _area.Top - reach && point.Y <= _area.Bottom + reach;

            if (nearTop && nearLeft)
                return CropHandle.TopLeft;
            if (nearTop && nearRight)
                return CropHandle.TopRight;
            if (nearBottom && nearRight)
                return CropHandle.BottomRight;
            if (nearBottom && nearLeft)
                return CropHandle.BottomLeft;
            if (nearTop && withinX)
                return CropHandle.Top;
            if (nearBottom && withinX)
                return CropHandle.Bottom;
            if (nearLeft && withinY)
                return CropHandle.Left;
            if (nearRight && withinY)
                return CropHandle.Right;
            return _area.Contains(point) ? CropHandle.Inside : CropHandle.None;
        }

        /// <summary>Moves the frame, or the edges that <paramref name="handle"/> is on, by <paramref name="delta"/> pixels, and keeps it in the picture and at least <see cref="MinimumSide"/> wide.</summary>
        public void Drag(CropHandle handle, Size delta)
        {
            if (handle == CropHandle.None)
                return;

            if (handle == CropHandle.Inside)
            {
                var x = Math.Clamp(_area.X + delta.Width, 0, _bounds.Width - _area.Width);
                var y = Math.Clamp(_area.Y + delta.Height, 0, _bounds.Height - _area.Height);
                _area = new Rectangle(x, y, _area.Width, _area.Height);
                return;
            }

            int left = _area.Left, top = _area.Top, right = _area.Right, bottom = _area.Bottom;
            var minimum = Math.Min(MinimumSide, Math.Min(_bounds.Width, _bounds.Height));
            if (handle is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft)
                left = Math.Clamp(left + delta.Width, 0, right - minimum);
            if (handle is CropHandle.TopRight or CropHandle.Right or CropHandle.BottomRight)
                right = Math.Clamp(right + delta.Width, left + minimum, _bounds.Width);
            if (handle is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight)
                top = Math.Clamp(top + delta.Height, 0, bottom - minimum);
            if (handle is CropHandle.BottomLeft or CropHandle.Bottom or CropHandle.BottomRight)
                bottom = Math.Clamp(bottom + delta.Height, top + minimum, _bounds.Height);

            _area = Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>The frame as a whole picture again.</summary>
        public void Reset() => _area = _bounds;

        /// <summary>Whether the frame leaves out anything of the picture (a frame that is the whole picture is no crop).</summary>
        public bool IsCrop => _area != _bounds;

        /// <summary>The area made at least <see cref="MinimumSide"/> wide and high, and inside the picture.</summary>
        private Rectangle Keep(Rectangle area)
        {
            var minimum = Math.Min(MinimumSide, Math.Min(_bounds.Width, _bounds.Height));
            var width = Math.Clamp(area.Width, minimum, _bounds.Width);
            var height = Math.Clamp(area.Height, minimum, _bounds.Height);
            var x = Math.Clamp(area.X, 0, _bounds.Width - width);
            var y = Math.Clamp(area.Y, 0, _bounds.Height - height);
            return new Rectangle(x, y, width, height);
        }
    }
}
