using System.Drawing.Drawing2D;
using PhotoLiker.VectorFiles;

namespace PhotoLikerUI
{
    /// <summary>
    /// Looks at a 3D model in the viewer: it owns where the camera is, draws the model at the size of the viewer on a thread of the pool, and puts the picture where a photo would be (the
    /// picture box of the main window), so the check mark of a liked photo, the date and the legend of the groups stay over it as they are over a photo. The mouse turns it (the left button),
    /// moves it (the right or the middle one) and zooms it (the wheel); a double click starts over; the buttons drawn in the corner look from the front, the back, the top ... or show the edges.
    ///
    /// While the mouse is busy the model is drawn from a part of its triangles (<see cref="DraftTriangles"/>) so that it follows the hand; when the mouse rests for a moment, it is drawn
    /// whole, with smooth edges. A picture that is being drawn is not waited for: the next one is drawn from the newest place of the camera as soon as it is done. Everything but the drawing is
    /// on the thread of the window.
    /// </summary>
    internal sealed class ModelViewController : IDisposable
    {
        /// <summary>How many triangles the picture that follows the mouse is drawn from at most.</summary>
        public const int DraftTriangles = 80_000;

        private const double DegreesPerPixel = 0.5;
        private const double WheelStep = 1.15;
        private const int SettleMilliseconds = 160;
        private const int SmoothTriangles = 1_500_000;     // a model with more is drawn whole without smoothing: the time goes into the triangles
        private const int SmoothPixels = 2_500_000;

        private readonly Func<Size> _clientSize;
        private readonly Action<Image?> _show;
        private readonly Action _invalidate;
        private readonly Func<float> _scale;
        private readonly Func<Font> _font;
        private readonly System.Windows.Forms.Timer _settle = new() { Interval = SettleMilliseconds };

        private StlMesh? _mesh;
        private MeshView _view = MeshView.Isometric;
        private bool _wireframe;
        private Bitmap? _frame;
        private bool _busy;
        private bool _dirty;
        private bool _wantDraft;
        private bool _runningDraft;
        private CancellationTokenSource? _stop;
        private TaskCompletionSource? _idle;
        private bool _disposed;

        private MouseButtons _dragging = MouseButtons.None;
        private Point _last;
        private int _hover = -1;

        /// <param name="clientSize">The size of the viewer: the model is drawn to fit it.</param>
        /// <param name="show">Puts a picture (or none) in the viewer; the controller owns the pictures it hands over and lets go of each one after the next has taken its place.</param>
        /// <param name="invalidate">The overlay (the buttons) has to be drawn again.</param>
        /// <param name="scale">The dpi of the screen over 96, for the sizes of the buttons.</param>
        public ModelViewController(Func<Size> clientSize, Action<Image?> show, Action invalidate, Func<float> scale, Func<Font> font)
        {
            _clientSize = clientSize;
            _show = show;
            _invalidate = invalidate;
            _scale = scale;
            _font = font;
            _settle.Tick += (_, _) =>
            {
                _settle.Stop();
                RequestRender(draft: false);
            };
        }

        /// <summary>A model is shown (and the mouse is for it).</summary>
        public bool Active => _mesh is not null;

        public StlMesh? Mesh => _mesh;

        /// <summary>The place of the camera now.</summary>
        public MeshView View => _view;

        public bool Wireframe => _wireframe;

        /// <summary>The mouse is moving the model: the cursor of the viewer is the cross of arrows.</summary>
        public bool Dragging => _dragging != MouseButtons.None;

        /// <summary>Raised when drawing the model fails (not enough memory for a picture of this size): what was shown stays.</summary>
        public event Action<Exception>? DrawFailed;

        // ── what is shown ──────────────────────────────────────────────────

        /// <summary>Starts looking at a model, from the front right above with the whole of it in the picture.</summary>
        public void Attach(StlMesh mesh)
        {
            _mesh = mesh;
            _view = MeshView.Isometric;
            _dragging = MouseButtons.None;
            _hover = -1;
            RequestRender(draft: false);
        }

        /// <summary>Stops looking at the model: what is drawn is let go of, and the viewer is left empty for whoever shows the next picture.</summary>
        public void Detach()
        {
            _settle.Stop();
            _stop?.Cancel();
            _mesh = null;
            _dragging = MouseButtons.None;
            _hover = -1;
            Release();
        }

        /// <summary>The viewer changed its size: the model is drawn again to fit it.</summary>
        public void Resized()
        {
            if (Active)
                RequestRender(draft: true);
        }

        /// <summary>The camera goes back to where it was, with the model in the middle at the size that fits.</summary>
        public void Reset() => Look(MeshView.Isometric);

        public void Look(MeshView view)
        {
            if (!Active)
                return;

            _view = view;
            RequestRender(draft: true);
        }

        public void ToggleWireframe()
        {
            if (!Active)
                return;

            _wireframe = !_wireframe;
            RequestRender(draft: false);
        }

        // ── the mouse ──────────────────────────────────────────────────────

        /// <summary>A button of the mouse went down in the viewer. True when it is the model's (the caller does nothing more with it).</summary>
        public bool MouseDown(MouseEventArgs e)
        {
            if (!Active)
                return false;

            if (e.Button == MouseButtons.Left && ButtonAt(e.Location) is { } button)
            {
                button.Button.Run();
                _invalidate();
                return true;
            }

            if (e.Button is MouseButtons.Left or MouseButtons.Right or MouseButtons.Middle)
            {
                _dragging = e.Button;
                _last = e.Location;
                return true;
            }
            return false;
        }

        public bool MouseMove(MouseEventArgs e)
        {
            if (!Active)
                return false;

            if (_dragging == MouseButtons.None)
            {
                var hover = Buttons().FindIndex(item => item.Bounds.Contains(e.Location));
                if (hover != _hover)
                {
                    _hover = hover;
                    _invalidate();
                }
                return false;
            }

            int dx = e.X - _last.X, dy = e.Y - _last.Y;
            _last = e.Location;
            if (dx == 0 && dy == 0)
                return true;

            if (_dragging == MouseButtons.Left)
            {
                _view = _view.Orbited(dx * DegreesPerPixel, dy * DegreesPerPixel);
            }
            else
            {
                var shorter = Math.Max(1, Math.Min(_clientSize().Width, _clientSize().Height));
                _view = _view.Panned(dx / (double)shorter, -dy / (double)shorter);
            }

            RequestRender(draft: true);
            return true;
        }

        public bool MouseUp(MouseEventArgs e)
        {
            if (_dragging == MouseButtons.None)
                return false;

            _dragging = MouseButtons.None;
            return true;
        }

        /// <summary>The wheel turned over the viewer, at <paramref name="at"/>: zooms in or out, keeping the point of the model that is under the mouse where it is.</summary>
        public bool Wheel(int delta, Point at)
        {
            if (!Active || delta == 0)
                return false;

            var factor = Math.Pow(WheelStep, delta / 120.0);
            var before = _view.Zoom;
            var zoomed = _view.Zoomed(factor);
            var actual = zoomed.Zoom / before;
            if (actual == 1)
                return true;

            // The place under the mouse, as fractions of the shorter side from the middle: it stays still when the move changes with the zoom
            var size = _clientSize();
            var shorter = (double)Math.Max(1, Math.Min(size.Width, size.Height));
            var x = (at.X - size.Width / 2.0) / shorter;
            var y = -(at.Y - size.Height / 2.0) / shorter;
            _view = zoomed with { PanX = x - (x - _view.PanX) * actual, PanY = y - (y - _view.PanY) * actual };

            RequestRender(draft: true);
            return true;
        }

        // ── drawing the model ──────────────────────────────────────────────

        private void RequestRender(bool draft)
        {
            if (_mesh is null || _disposed)
                return;

            var size = _clientSize();
            if (size.Width < 8 || size.Height < 8)
                return;

            _wantDraft = draft;
            _settle.Stop();
            if (draft)
                _settle.Start(); // the whole model follows when the mouse rests

            if (_busy)
            {
                _dirty = true;
                if (!_runningDraft)
                    _stop?.Cancel(); // a whole picture takes its time: it is stale now, the newer one goes first
                return;
            }

            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            _busy = true;
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                while (true)
                {
                    _dirty = false;
                    var mesh = _mesh;
                    var size = _clientSize();
                    if (mesh is null || _disposed || size.Width < 8 || size.Height < 8)
                        return;

                    var view = _view;
                    var draft = _wantDraft;
                    var style = StyleFor(mesh, size, draft);
                    var stop = _stop = new CancellationTokenSource();
                    _runningDraft = draft;

                    RenderedPicture? picture = null;
                    try
                    {
                        picture = await Task.Run(() => MeshRenderer.Render(mesh, view, size.Width, size.Height, style, draft ? DraftTriangles : 0, stop.Token));
                    }
                    catch (OperationCanceledException)
                    {
                        // a newer place of the camera, or the model is gone
                    }
                    catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or InvalidOperationException)
                    {
                        DrawFailed?.Invoke(ex);
                        return;
                    }
                    finally
                    {
                        _stop = null;
                        stop.Dispose();
                    }

                    // Not for a model that was left meanwhile; for the same model a picture a little behind the mouse is still better than none
                    if (picture is not null && !_disposed && ReferenceEquals(mesh, _mesh))
                        Present(picture);

                    if (!_dirty)
                        return;
                }
            }
            finally
            {
                _busy = false;
                _idle.TrySetResult();
            }
        }

        private RenderStyle StyleFor(StlMesh mesh, Size size, bool draft) =>
            ImageHelper.ModelStyle with
            {
                Wireframe = _wireframe,
                Supersample = !draft && mesh.TriangleCount <= SmoothTriangles && (long)size.Width * size.Height * 4 <= SmoothPixels ? 2 : 1,
            };

        private void Present(RenderedPicture picture)
        {
            var next = ImageHelper.ToBitmap(picture);
            var old = _frame;
            _frame = next;
            _show(next);
            old?.Dispose(); // the viewer shows the new one now
            _invalidate();
        }

        private void Release()
        {
            var old = _frame;
            _frame = null;
            if (old is not null)
            {
                _show(null); // so that nothing paints the picture that is about to go
                old.Dispose();
            }
        }

        /// <summary>For a test: draws the model whole now (what the mouse resting does after a moment) and completes when no picture is being drawn any more.</summary>
        internal Task SettledAsync()
        {
            if (_mesh is not null)
            {
                _settle.Stop();
                RequestRender(draft: false);
            }
            return WhenIdle();
        }

        /// <summary>Completes when no picture is being drawn.</summary>
        internal Task WhenIdle() => _busy && _idle is { } idle ? idle.Task : Task.CompletedTask;

        // ── the buttons ────────────────────────────────────────────────────

        private sealed record Button(string Text, Func<bool> IsOn, Action Run);

        private sealed record Placed(Button Button, Rectangle Bounds);

        private List<Button> Items() =>
        [
            new(ModelStrings.ViewIso, () => IsLookingFrom(MeshView.Isometric), () => Look(MeshView.Isometric)),
            new(ModelStrings.ViewFront, () => IsLookingFrom(MeshView.Front), () => Look(MeshView.Front)),
            new(ModelStrings.ViewBack, () => IsLookingFrom(MeshView.Back), () => Look(MeshView.Back)),
            new(ModelStrings.ViewLeft, () => IsLookingFrom(MeshView.Left), () => Look(MeshView.Left)),
            new(ModelStrings.ViewRight, () => IsLookingFrom(MeshView.Right), () => Look(MeshView.Right)),
            new(ModelStrings.ViewTop, () => IsLookingFrom(MeshView.Top), () => Look(MeshView.Top)),
            new(ModelStrings.ViewBottom, () => IsLookingFrom(MeshView.Bottom), () => Look(MeshView.Bottom)),
            new(ModelStrings.ViewWireframe, () => _wireframe, ToggleWireframe),
        ];

        private bool IsLookingFrom(MeshView side) => Math.Abs(_view.Yaw - side.Yaw) < 0.5 && Math.Abs(_view.Pitch - side.Pitch) < 0.5;

        /// <summary>The buttons where they are drawn: in the top right corner, in rows that start from the right, a row more when the viewer is too narrow for one.</summary>
        private List<Placed> Buttons()
        {
            var scale = _scale();
            var size = _clientSize();
            var font = _font();
            var margin = (int)(8 * scale);
            var gap = (int)(4 * scale);
            var pad = (int)(8 * scale);
            var height = font.Height + (int)(8 * scale);
            var room = Math.Max(1, size.Width - 2 * margin);

            var items = Items();
            var widths = items.Select(item => TextRenderer.MeasureText(item.Text, font).Width + 2 * pad).ToList();
            var placed = new List<Placed>();
            var row = new List<int>();
            var rowWidth = 0;
            var top = margin;

            void Flush()
            {
                var x = size.Width - margin - rowWidth;
                foreach (var index in row)
                {
                    placed.Add(new Placed(items[index], new Rectangle(x, top, widths[index], height)));
                    x += widths[index] + gap;
                }
                top += height + gap;
                row.Clear();
                rowWidth = 0;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var needed = widths[i] + (row.Count > 0 ? gap : 0);
                if (row.Count > 0 && rowWidth + needed > room)
                    Flush();
                rowWidth += widths[i] + (row.Count > 0 ? gap : 0);
                row.Add(i);
            }
            if (row.Count > 0)
                Flush();

            return placed;
        }

        private Placed? ButtonAt(Point point) => Buttons().FirstOrDefault(item => item.Bounds.Contains(point));

        /// <summary>The point is on one of the buttons drawn over the model.</summary>
        public bool IsOverButton(Point point) => Active && ButtonAt(point) is not null;

        /// <summary>The bounds of the buttons that are drawn, in the order they are drawn (a test clicks them).</summary>
        internal IReadOnlyList<(string Text, Rectangle Bounds)> ButtonBounds() => Buttons().Select(item => (item.Button.Text, item.Bounds)).ToList();

        // ── over the picture ───────────────────────────────────────────────

        /// <summary>Draws the buttons and the three axes over the picture of the model.</summary>
        public void Paint(Graphics g)
        {
            if (!Active)
                return;

            var state = g.Save(); // what is drawn after this is drawn as it always was
            try
            {
                PaintOverlay(g);
            }
            finally
            {
                g.Restore(state);
            }
        }

        private void PaintOverlay(Graphics g)
        {
            var scale = _scale();
            var font = _font();
            var placed = Buttons();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (var i = 0; i < placed.Count; i++)
            {
                var (button, bounds) = (placed[i].Button, placed[i].Bounds);
                var on = button.IsOn();
                var fill = on ? Color.FromArgb(215, 60, 120, 215) : i == _hover ? Color.FromArgb(190, 70, 74, 80) : Color.FromArgb(150, 0, 0, 0);
                using var path = RoundedRectangle(bounds, (int)(4 * scale));
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                TextRenderer.DrawText(g, button.Text, font, bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            DrawAxes(g, placed, scale, font);
        }

        /// <summary>The three axes of the model as short lines from one point, each as the camera sees it: the way a CAD program shows which way is which.</summary>
        private void DrawAxes(Graphics g, List<Placed> placed, float scale, Font font)
        {
            var size = _clientSize();
            var length = 28 * scale;
            var margin = 8 * scale;
            var below = placed.Count == 0 ? margin : placed.Max(item => item.Bounds.Bottom) + margin;
            var origin = new PointF(size.Width - margin - length - 10 * scale, below + length + 4 * scale);
            var basis = _view.Basis;

            (string Name, double X, double Y, double Z, Color Color)[] axes =
            [
                ("X", 1, 0, 0, Color.FromArgb(235, 80, 80)),
                ("Y", 0, 1, 0, Color.FromArgb(90, 205, 90)),
                ("Z", 0, 0, 1, Color.FromArgb(90, 140, 255)),
            ];

            // The axes that point away from the viewer first, so that the ones pointing at it are over them
            foreach (var axis in axes.OrderBy(axis => basis.Project(axis.X, axis.Y, axis.Z).To))
            {
                var (right, up, _) = basis.Project(axis.X, axis.Y, axis.Z);
                var end = new PointF(origin.X + (float)(right * length), origin.Y - (float)(up * length));
                using var pen = new Pen(axis.Color, 2f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, origin, end);

                var label = new PointF(origin.X + (float)(right * (length + 9 * scale)), origin.Y - (float)(up * (length + 9 * scale)));
                var box = new Rectangle((int)(label.X - 7 * scale), (int)(label.Y - font.Height / 2f), (int)(14 * scale), font.Height);
                TextRenderer.DrawText(g, axis.Name, font, box, axis.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, radius * 2);
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            _disposed = true;
            _settle.Stop();
            _settle.Dispose();
            _stop?.Cancel();
            _mesh = null;
            _frame?.Dispose();
            _frame = null;
        }
    }
}
