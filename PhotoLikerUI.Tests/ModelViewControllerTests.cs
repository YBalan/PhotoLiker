using PhotoLiker.VectorFiles;
using PhotoLiker.VectorFiles.Tests;

namespace PhotoLikerUI.Tests;

/// <summary>The viewer of a 3D model without a window: where the camera goes with the mouse, what is drawn and when, and the buttons over it.</summary>
public sealed class ModelViewControllerTests : IDisposable
{
    private static readonly int Stage = ImageHelper.ModelStyle.Background;

    private readonly List<Image?> _shown = [];
    private Size _client = new(400, 300);
    private float _scale = 1f;
    private int _invalidated;
    private readonly ModelViewController _controller;

    public ModelViewControllerTests()
    {
        _controller = new ModelViewController(() => _client, image => _shown.Add(image), () => _invalidated++, () => _scale, () => SystemFonts.DefaultFont);
    }

    public void Dispose() => _controller.Dispose();

    /// <summary>A colour as the bitmap reports its pixels: the stage is 0xRRGGBB, and a pixel is opaque.</summary>
    internal static int Opaque(int rgb) => Color.FromArgb(255, Color.FromArgb(rgb)).ToArgb();

    private static StlMesh Box(float x = 40, float y = 20, float z = 10) => TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, x, y, z));

    private Bitmap Picture => (Bitmap)_shown.Last(image => image is not null)!;

    private static MouseEventArgs Mouse(MouseButtons button, int x, int y) => new(button, 1, x, y, 0);

    /// <summary>The box of the part of the picture that is not the stage.</summary>
    private static Rectangle Silhouette(Bitmap picture)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < picture.Height; y += 2)
        {
            for (var x = 0; x < picture.Width; x += 2)
            {
                if (picture.GetPixel(x, y).ToArgb() == Opaque(Stage))
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right, bottom);
    }

    // ── what is shown ──────────────────────────────────────────────────

    [Fact]
    public async Task AModel_IsDrawnWhole_AtTheSizeOfTheViewer()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        Assert.True(_controller.Active);
        Assert.Equal(_client, Picture.Size);
        var shape = Silhouette(Picture);
        Assert.InRange(shape.Left + shape.Width / 2, _client.Width / 2 - 6, _client.Width / 2 + 6);
        Assert.InRange(shape.Top + shape.Height / 2, _client.Height / 2 - 6, _client.Height / 2 + 6);
        Assert.True(_invalidated > 0);
    }

    [Fact]
    public async Task ADifferentModel_ReplacesTheFirst_AndOnlyTheLastIsKept()
    {
        _controller.Attach(TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 10, 10, 60)));
        _controller.Attach(Box(60, 60, 5)); // before the first one was drawn
        await _controller.SettledAsync();

        var shape = Silhouette(Picture);
        Assert.True(shape.Width > shape.Height, "a flat plate seen from above and the front, not the tall pillar");
        Assert.Equal(60, _controller.Mesh!.Bounds.SizeX); // and it is the second model that is held
    }

    [Fact]
    public async Task LettingGo_LeavesNothingShown_AndNothingIsDrawnAfterwards()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        var before = _shown.Count;

        _controller.Detach();
        await _controller.WhenIdle();

        Assert.False(_controller.Active);
        Assert.Null(_shown.Last());
        Assert.Equal(before + 1, _shown.Count);
        Assert.False(_controller.MouseDown(Mouse(MouseButtons.Left, 10, 10)));   // the mouse is not its any more
        Assert.False(_controller.Wheel(120, new Point(10, 10)));
    }

    [Fact]
    public async Task EveryPictureButTheLast_IsLetGoOfAfterTheNextTookItsPlace()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        _controller.Look(MeshView.Top);
        await _controller.SettledAsync();

        var first = _shown.OfType<Bitmap>().First();

        Assert.NotSame(first, _shown.OfType<Bitmap>().Last());
        Assert.Throws<ArgumentException>(() => first.GetPixel(0, 0)); // disposed: a picture that nothing shows any more is not kept
    }

    [Fact]
    public async Task WhenTheViewerChangesItsSize_TheModelIsDrawnAgainToFitIt()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        _client = new Size(600, 200);
        _controller.Resized();
        await _controller.SettledAsync();

        Assert.Equal(new Size(600, 200), Picture.Size);
    }

    [Fact]
    public async Task AViewerTooSmallToDrawIn_IsLeftAlone()
    {
        _client = new Size(3, 3);
        _controller.Attach(Box());
        await _controller.SettledAsync();

        Assert.Empty(_shown);
    }

    // ── the mouse ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheLeftButton_TurnsTheModel_HalfADegreeAPixel()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        var start = _controller.View;

        Assert.True(_controller.MouseDown(Mouse(MouseButtons.Left, 100, 100)));
        Assert.True(_controller.Dragging);
        Assert.True(_controller.MouseMove(Mouse(MouseButtons.Left, 140, 110))); // 40 right, 10 down
        Assert.True(_controller.MouseUp(Mouse(MouseButtons.Left, 140, 110)));
        await _controller.SettledAsync();

        Assert.False(_controller.Dragging);
        Assert.Equal(start.Yaw - 20, _controller.View.Yaw, 9);
        Assert.Equal(start.Pitch + 5, _controller.View.Pitch, 9);
        Assert.Equal(start.Zoom, _controller.View.Zoom);
    }

    [Fact]
    public async Task TheRightButton_MovesTheModel_AsAFractionOfTheShorterSide()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        _controller.MouseDown(Mouse(MouseButtons.Right, 100, 100));
        _controller.MouseMove(Mouse(MouseButtons.Right, 130, 80));   // 30 right, 20 up of a viewer 300 high
        _controller.MouseUp(Mouse(MouseButtons.Right, 130, 80));

        Assert.Equal(30 / 300.0, _controller.View.PanX, 9);
        Assert.Equal(20 / 300.0, _controller.View.PanY, 9);
        Assert.Equal(MeshView.Isometric.Yaw, _controller.View.Yaw); // moved, not turned
    }

    [Fact]
    public async Task TheMovingMouse_DrawsTheModelFromAPartOfItsTriangles_AndTheRestWhenItRests()
    {
        // 200,000 triangles: more than the picture that follows the mouse is drawn from
        _controller.Attach(TestMeshes.Mesh(TestMeshes.Sphere(10, 250, 400)));
        await _controller.SettledAsync();
        var whole = Silhouette(Picture);

        _controller.MouseDown(Mouse(MouseButtons.Left, 100, 100));
        _controller.MouseMove(Mouse(MouseButtons.Left, 101, 100));
        _controller.MouseUp(Mouse(MouseButtons.Left, 101, 100));
        await _controller.WhenIdle(); // the picture of the move, not yet the whole one

        Assert.Equal(MeshView.Isometric.Yaw - 0.5, _controller.View.Yaw, 9);
        await _controller.SettledAsync();
        var settled = Silhouette(Picture);
        Assert.InRange(settled.Width, whole.Width - 4, whole.Width + 4); // the same ball
    }

    [Fact]
    public async Task TheWheel_ZoomsByAStepOfFifteenPercent_AboutTheMiddleWhenTheMouseIsThere()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        Assert.True(_controller.Wheel(120, new Point(200, 150)));
        Assert.Equal(1.15, _controller.View.Zoom, 9);
        Assert.Equal(0, _controller.View.PanX, 9);
        Assert.Equal(0, _controller.View.PanY, 9);

        _controller.Wheel(-120, new Point(200, 150));
        Assert.Equal(1.0, _controller.View.Zoom, 9);
    }

    [Fact]
    public async Task TheWheel_KeepsThePlaceUnderTheMouseStill()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        var at = new Point(300, 100); // right of the middle and above it
        _controller.Wheel(240, at);

        // The place under the mouse in fractions of the shorter side; its place in the picture is pan + (what it was) x zoom, so after zooming by f it is the same if pan' = c - f (c - pan)
        var factor = _controller.View.Zoom;
        var cx = (300 - 200) / 300.0;
        var cy = -(100 - 150) / 300.0;
        Assert.Equal(cx - factor * cx, _controller.View.PanX, 9);
        Assert.Equal(cy - factor * cy, _controller.View.PanY, 9);
    }

    [Fact]
    public async Task TheZoom_StopsAtItsLimit_AndTheModelStaysWhereItIs()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        for (var i = 0; i < 60; i++)
            _controller.Wheel(120, new Point(100, 100));
        var atLimit = _controller.View;

        Assert.Equal(MeshView.MaxZoom, atLimit.Zoom);
        Assert.True(_controller.Wheel(120, new Point(10, 10))); // more of the same changes nothing, not even the move
        Assert.Equal(atLimit, _controller.View);
    }

    [Fact]
    public async Task ADoubleClickOnTheModel_StartsOver()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        _controller.Wheel(480, new Point(10, 10));
        _controller.MouseDown(Mouse(MouseButtons.Left, 100, 100));
        _controller.MouseMove(Mouse(MouseButtons.Left, 160, 100));
        _controller.MouseUp(Mouse(MouseButtons.Left, 160, 100));

        _controller.Reset();

        Assert.Equal(MeshView.Isometric, _controller.View);
    }

    [Fact]
    public void WithNoModel_TheMouseIsNotTheViewers()
    {
        Assert.False(_controller.MouseDown(Mouse(MouseButtons.Left, 10, 10)));
        Assert.False(_controller.MouseMove(Mouse(MouseButtons.None, 10, 10)));
        Assert.False(_controller.MouseUp(Mouse(MouseButtons.Left, 10, 10)));
        Assert.False(_controller.Wheel(120, new Point(10, 10)));
        Assert.False(_controller.IsOverButton(new Point(390, 10)));
    }

    [Fact]
    public async Task AMouseThatMovesWithoutAButton_IsNotConsumed()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        Assert.False(_controller.MouseMove(Mouse(MouseButtons.None, 50, 50))); // faces and the rest of the window may still want it
    }

    // ── the buttons ────────────────────────────────────────────────────

    [Fact]
    public async Task TheButtons_AreInTheTopRightCorner_InsideTheViewer_AndDoNotTouch()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();

        var buttons = _controller.ButtonBounds();

        Assert.Equal([ModelStrings.ViewIso, ModelStrings.ViewFront, ModelStrings.ViewBack, ModelStrings.ViewLeft, ModelStrings.ViewRight, ModelStrings.ViewTop, ModelStrings.ViewBottom, ModelStrings.ViewWireframe],
            buttons.Select(button => button.Text));
        var viewer = new Rectangle(Point.Empty, _client);
        Assert.All(buttons, button => Assert.True(viewer.Contains(button.Bounds), button.Text));
        for (var i = 0; i < buttons.Count; i++)
            for (var j = i + 1; j < buttons.Count; j++)
                Assert.False(buttons[i].Bounds.IntersectsWith(buttons[j].Bounds), $"{buttons[i].Text} and {buttons[j].Text} overlap");
        Assert.True(buttons.Max(button => button.Bounds.Right) <= _client.Width - 4);
        Assert.True(buttons.Min(button => button.Bounds.Top) < 20);
        Assert.True(buttons.Max(button => button.Bounds.Right) > _client.Width - 20); // it is the right edge that they are lined up to
    }

    [Fact]
    public async Task InANarrowViewer_TheButtonsGoOnInAnotherRow_AndStillFit()
    {
        _client = new Size(220, 300);
        _controller.Attach(Box());
        await _controller.SettledAsync();

        var buttons = _controller.ButtonBounds();

        Assert.True(buttons.Select(button => button.Bounds.Top).Distinct().Count() > 1);
        Assert.All(buttons, button => Assert.True(button.Bounds.Left >= 0 && button.Bounds.Right <= _client.Width, button.Text));
    }

    [Fact]
    public async Task OnAScreenWithBiggerLetters_TheButtonsAreBigger()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        var normal = _controller.ButtonBounds()[0].Bounds;

        _scale = 2f;
        var big = _controller.ButtonBounds()[0].Bounds;

        Assert.True(big.Height > normal.Height);
    }

    [Theory]
    [InlineData(1, 0, 0)]      // Front
    [InlineData(2, 180, 0)]    // Back
    [InlineData(3, -90, 0)]    // Left
    [InlineData(4, 90, 0)]     // Right
    [InlineData(5, 0, 90)]     // Top
    [InlineData(6, 0, -90)]    // Bottom
    [InlineData(0, 35, 28)]    // Iso
    public async Task AButton_LooksFromItsSide(int index, double yaw, double pitch)
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        _controller.Wheel(240, new Point(10, 10)); // zoomed and moved: a side is looked at from the middle, at the size that fits
        var button = _controller.ButtonBounds()[index].Bounds;

        Assert.True(_controller.IsOverButton(Center(button)));
        Assert.True(_controller.MouseDown(Mouse(MouseButtons.Left, Center(button).X, Center(button).Y)));
        await _controller.SettledAsync();

        Assert.Equal(yaw, _controller.View.Yaw);
        Assert.Equal(pitch, _controller.View.Pitch);
        Assert.False(_controller.Dragging); // a click on a button is no drag of the model
    }

    [Fact]
    public async Task TheWireframeButton_ShowsAndHidesTheEdges()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        var plain = Hash(Picture);
        var wire = _controller.ButtonBounds()[7].Bounds;

        _controller.MouseDown(Mouse(MouseButtons.Left, Center(wire).X, Center(wire).Y));
        await _controller.SettledAsync();
        Assert.True(_controller.Wireframe);
        var edges = Hash(Picture);
        Assert.NotEqual(plain, edges);

        _controller.MouseDown(Mouse(MouseButtons.Left, Center(wire).X, Center(wire).Y));
        await _controller.SettledAsync();
        Assert.False(_controller.Wireframe);
        Assert.Equal(plain, Hash(Picture));
    }

    [Fact]
    public async Task AButtonOnlyAnswersTheLeftButton()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        var top = _controller.ButtonBounds()[5].Bounds;

        _controller.MouseDown(Mouse(MouseButtons.Right, Center(top).X, Center(top).Y));

        Assert.NotEqual(90, _controller.View.Pitch);
        Assert.True(_controller.Dragging); // a right button over a button still moves the model
    }

    [Fact]
    public async Task PaintingTheOverlay_DrawsTheButtonsAndTheAxes_AndLeavesTheGraphicsAsItWas()
    {
        _controller.Attach(Box());
        await _controller.SettledAsync();
        using var canvas = new Bitmap(_client.Width, _client.Height);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Magenta);
        var smoothing = g.SmoothingMode;

        _controller.Paint(g);

        Assert.Equal(smoothing, g.SmoothingMode);
        var button = _controller.ButtonBounds()[0].Bounds;
        Assert.NotEqual(Color.Magenta.ToArgb(), canvas.GetPixel(button.Left + 3, button.Top + 3).ToArgb());
        var drawn = 0;
        for (var y = 0; y < canvas.Height; y += 3)
            for (var x = 0; x < canvas.Width; x += 3)
                if (canvas.GetPixel(x, y).ToArgb() != Color.Magenta.ToArgb())
                    drawn++;
        Assert.True(drawn > 100);
    }

    [Fact]
    public void PaintingWithNoModel_DrawsNothing()
    {
        using var canvas = new Bitmap(100, 100);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.Magenta);

        _controller.Paint(g);

        Assert.Equal(Color.Magenta.ToArgb(), canvas.GetPixel(95, 5).ToArgb());
    }

    private static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static string Hash(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream.ToArray()));
    }
}
