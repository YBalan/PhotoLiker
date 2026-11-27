namespace PhotoLiker.VectorFiles.Tests;

public class MeshRendererTests
{
    private const int Size = 200;
    private static readonly RenderStyle Plain = new();
    private static readonly int Background = Plain.Background;

    private static RenderedPicture Draw(float[] triangles, MeshView view, int size = Size, RenderStyle? style = null, int maxTriangles = 0) =>
        MeshRenderer.Render(TestMeshes.Mesh(triangles), view, size, size, style ?? Plain, maxTriangles);

    [Fact]
    public void ThePicture_HasTheSizeAskedFor_AndEveryPixelIsOpaque()
    {
        var picture = MeshRenderer.Render(TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 1, 1, 1)), MeshView.Isometric, 120, 80);

        Assert.Equal(120, picture.Width);
        Assert.Equal(80, picture.Height);
        Assert.Equal(120 * 80 * 4, picture.Bgra.Length);
        Assert.All(Enumerable.Range(0, 120 * 80).Select(i => picture.Bgra[i * 4 + 3]), alpha => Assert.Equal(255, alpha));
    }

    [Fact]
    public void WhereNothingIsDrawn_ThePictureIsTheBackground()
    {
        var picture = Draw(TestMeshes.Box(0, 0, 0, 10, 10, 10), MeshView.Isometric, style: new RenderStyle { Background = 0x123456 });

        Assert.Equal(0x123456, picture.ColorAt(0, 0));
        Assert.Equal(0x123456, picture.ColorAt(Size - 1, Size - 1));
        Assert.Equal(0x123456, picture.ColorAt(Size - 1, 0));
    }

    [Fact]
    public void AModelWithNoTriangles_IsOnlyBackground()
    {
        var picture = MeshRenderer.Render(new StlMesh([], 0, "", StlFormat.Binary), MeshView.Isometric, 50, 50, Plain);

        Assert.Null(TestMeshes.Silhouette(picture, Background));
    }

    [Fact]
    public void ABox_SeenFromTheFront_IsASquareInTheMiddleOfThePicture()
    {
        var picture = Draw(TestMeshes.Box(0, 0, 0, 10, 10, 10), MeshView.Front);

        var (left, top, right, bottom) = TestMeshes.Silhouette(picture, Background)!.Value;
        Assert.InRange((left + right) / 2.0, Size / 2.0 - 1.5, Size / 2.0 + 1.5);
        Assert.InRange((top + bottom) / 2.0, Size / 2.0 - 1.5, Size / 2.0 + 1.5);
        Assert.InRange(right - left, bottom - top - 1, bottom - top + 1); // as wide as it is high
        Assert.NotEqual(Background, picture.ColorAt(Size / 2, Size / 2));

        // It fits with room round it: 0.9 of the shorter side is for the whole diagonal of the box, so a face is much smaller
        Assert.InRange(right - left, Size * 0.2, Size * 0.7);
    }

    [Fact]
    public void ASphere_IsRound_AndHasNoGapsBetweenItsTriangles()
    {
        var picture = Draw(TestMeshes.Sphere(10, 100, 200), MeshView.Front, 400);

        // The picture fits the diagonal of the box (the radius of a sphere x 1.73) in 0.9 of the shorter side
        var radius = 0.9 * 400 / (2 * Math.Sqrt(3));
        var covered = TestMeshes.Covered(picture, Background);
        Assert.InRange(covered, Math.PI * radius * radius * 0.98, Math.PI * radius * radius * 1.02);
    }

    [Fact]
    public void ATallPillar_IsTallInFront_AndASmallSquareFromAbove()
    {
        float[] pillar = TestMeshes.Box(-1, -1, 0, 1, 1, 20);

        var front = TestMeshes.Silhouette(Draw(pillar, MeshView.Front), Background)!.Value;
        var top = TestMeshes.Silhouette(Draw(pillar, MeshView.Top), Background)!.Value;

        Assert.True(front.Bottom - front.Top > 8 * (front.Right - front.Left)); // z is up
        Assert.InRange(top.Right - top.Left, top.Bottom - top.Top - 1, top.Bottom - top.Top + 1);
        Assert.True(top.Bottom - top.Top < (front.Bottom - front.Top) / 4);
    }

    [Theory]
    [InlineData("front")]   // x is to the right
    [InlineData("top")]     // x is to the right too
    [InlineData("right")]   // seen from the right of the model, y is to the right
    public void TheAxes_RunTheWayACadProgramShowsThem(string view)
    {
        // A block with a thin stick at the +x end (or, for the view from the right, at the +y end): the stick is the narrow end of the silhouette, and it must be on the right
        var body = TestMeshes.Box(0, 0, 0, 10, 10, 10);
        var stickX = TestMeshes.Box(10, 4, 4, 14, 6, 6);
        var stickY = TestMeshes.Box(4, 10, 4, 6, 14, 6);
        var (camera, stick) = view switch
        {
            "front" => (MeshView.Front, stickX),
            "top" => (MeshView.Top, stickX),
            _ => (MeshView.Right, stickY),
        };

        var picture = Draw(TestMeshes.Join(body, stick), camera, 400);
        var (left, _, right, _) = TestMeshes.Silhouette(picture, Background)!.Value;
        var near = (right - left) / 10;
        var atLeft = TestMeshes.CoveredInColumn(picture, left + near, Background);
        var atRight = TestMeshes.CoveredInColumn(picture, right - near, Background);

        Assert.True(atRight < atLeft, $"the thin end should be on the right in the {view} view (left {atLeft}, right {atRight})");
    }

    [Fact]
    public void Up_IsZ_InTheFrontView_AndYInTheTopView()
    {
        var body = TestMeshes.Box(0, 0, 0, 10, 10, 10);
        var stickUp = TestMeshes.Box(4, 4, 10, 6, 6, 14);
        var stickAway = TestMeshes.Box(4, 10, 4, 6, 14, 6);

        var front = Draw(TestMeshes.Join(body, stickUp), MeshView.Front, 400);
        var frontShape = TestMeshes.Silhouette(front, Background)!.Value;
        var top = Draw(TestMeshes.Join(body, stickAway), MeshView.Top, 400);
        var topShape = TestMeshes.Silhouette(top, Background)!.Value;

        var step = (frontShape.Bottom - frontShape.Top) / 10;
        Assert.True(TestMeshes.CoveredInRow(front, frontShape.Top + step, Background) < TestMeshes.CoveredInRow(front, frontShape.Bottom - step, Background));
        step = (topShape.Bottom - topShape.Top) / 10;
        Assert.True(TestMeshes.CoveredInRow(top, topShape.Top + step, Background) < TestMeshes.CoveredInRow(top, topShape.Bottom - step, Background));
    }

    [Fact]
    public void WhatIsNearer_HidesWhatIsBehind()
    {
        // A big wall facing the viewer, and a small triangle that is tilted so that the light gives it another colour
        float[] wall = TestMeshes.Quad([-10, 5, -10], [10, 5, -10], [10, 5, 10], [-10, 5, 10]);
        float[] tilted = TestMeshes.Triangle(-1, -5, -1, 1, -5, -1, 0, -3, 1);

        var wallAlone = Draw(wall, MeshView.Front).ColorAt(Size / 2, Size / 2);
        var inFront = Draw(TestMeshes.Join(wall, tilted), MeshView.Front);
        var behind = Draw(TestMeshes.Join(TestMeshes.Quad([-10, -8, -10], [10, -8, -10], [10, -8, 10], [-10, -8, 10]), TestMeshes.Triangle(-1, 5, -1, 1, 5, -1, 0, 7, 1)), MeshView.Front);

        Assert.NotEqual(wallAlone, inFront.ColorAt(Size / 2, Size / 2)); // the triangle is seen on the wall
        Assert.Equal(wallAlone, behind.ColorAt(Size / 2, Size / 2));      // here the wall is in front of it
    }

    [Fact]
    public void ATriangleIsLitOnBothSides_SoAModelTurnedInsideOutLooksTheSame()
    {
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 10);

        var outside = Draw(box, MeshView.Isometric);
        var inside = Draw(TestMeshes.Reversed(box), MeshView.Isometric);

        Assert.Equal(outside.Bgra, inside.Bgra);
    }

    [Fact]
    public void TheFacesOfABox_AreLitDifferently_SoItLooksSolid()
    {
        var picture = Draw(TestMeshes.Box(0, 0, 0, 10, 10, 10), MeshView.Isometric, 300);

        var colors = new HashSet<int>();
        for (var y = 0; y < 300; y++)
            for (var x = 0; x < 300; x++)
                if (picture.ColorAt(x, y) != Background)
                    colors.Add(picture.ColorAt(x, y));

        Assert.Equal(3, colors.Count); // three faces can be seen, each flat
    }

    [Fact]
    public void ZoomingIn_MakesTheModelBigger_AndPanningMovesIt()
    {
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 10);
        var plain = TestMeshes.Silhouette(Draw(box, MeshView.Front), Background)!.Value;
        var zoomed = TestMeshes.Silhouette(Draw(box, MeshView.Front.Zoomed(1.5)), Background)!.Value; // more than this and the box is wider than the picture
        var moved = TestMeshes.Silhouette(Draw(box, MeshView.Front.Panned(0.15, 0.1)), Background)!.Value;

        Assert.InRange(zoomed.Right - zoomed.Left, 1.5 * (plain.Right - plain.Left) - 3, 1.5 * (plain.Right - plain.Left) + 3);
        Assert.InRange(moved.Left - plain.Left, Size * 0.15 - 1, Size * 0.15 + 1);       // to the right
        Assert.InRange(plain.Top - moved.Top, Size * 0.1 - 1, Size * 0.1 + 1);           // up
        Assert.Equal(plain.Right - plain.Left, moved.Right - moved.Left);
    }

    [Fact]
    public void ThePictureIsTheSameWhateverTheSizeIsAsked_ForTheSameFractionOfIt()
    {
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 10);
        var small = TestMeshes.Silhouette(Draw(box, MeshView.Front.Panned(0.2, 0), 100), Background)!.Value;
        var big = TestMeshes.Silhouette(Draw(box, MeshView.Front.Panned(0.2, 0), 400), Background)!.Value;

        Assert.InRange(big.Left, small.Left * 4 - 4, small.Left * 4 + 4);
        Assert.InRange(big.Right - big.Left, (small.Right - small.Left) * 4 - 4, (small.Right - small.Left) * 4 + 4);
    }

    [Fact]
    public void TheSameModelAndView_AlwaysMakeTheSamePicture_AlthoughItIsDrawnByManyThreads()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Sphere(10, 80, 160));

        var first = MeshRenderer.Render(mesh, MeshView.Isometric, 300, 300, Plain);
        var second = MeshRenderer.Render(mesh, MeshView.Isometric, 300, 300, Plain);

        Assert.Equal(first.Bgra, second.Bgra);
    }

    [Fact]
    public void Smoothing_AddsShadesAtTheEdges_AndKeepsTheShape()
    {
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 10);
        var rough = Draw(box, MeshView.Isometric.Orbited(7, 0), 150);
        var smooth = Draw(box, MeshView.Isometric.Orbited(7, 0), 150, new RenderStyle { Supersample = 3 });

        Assert.True(Colors(smooth) > Colors(rough));
        var a = TestMeshes.Silhouette(rough, Background)!.Value;
        var b = TestMeshes.Silhouette(smooth, Background)!.Value;
        Assert.InRange(b.Left - a.Left, -2, 2);
        Assert.InRange(b.Right - a.Right, -2, 2);
        Assert.InRange(b.Top - a.Top, -2, 2);

        static int Colors(RenderedPicture p) => Enumerable.Range(0, p.Width * p.Height).Select(i => p.ColorAt(i % p.Width, i / p.Width)).Distinct().Count();
    }

    [Fact]
    public void WithALimit_OnlyEveryNthTriangleIsDrawn()
    {
        // Two far apart triangles: with a limit of one, every second is taken, which leaves the first
        float[] triangles = TestMeshes.Join(TestMeshes.Triangle(-10, 0, -10, -2, 0, -10, -6, 0, 10), TestMeshes.Triangle(2, 0, -10, 10, 0, -10, 6, 0, 10));

        var all = Draw(triangles, MeshView.Front);
        var limited = Draw(triangles, MeshView.Front, maxTriangles: 1);

        Assert.True(TestMeshes.Covered(limited, Background) > 0);
        Assert.True(TestMeshes.Covered(limited, Background) < TestMeshes.Covered(all, Background) * 0.6);
        Assert.Equal(Background, limited.ColorAt(Size * 3 / 4 + 5, Size / 2)); // the right one is not there
    }

    [Fact]
    public void TheColours_FollowTheStyle()
    {
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 10);

        var red = Draw(box, MeshView.Front, style: new RenderStyle { Surface = 0xFF0000, Background = 0x000000 });
        var centre = red.ColorAt(Size / 2, Size / 2);

        Assert.True(((centre >> 16) & 0xFF) > 100);
        Assert.Equal(0, (centre >> 8) & 0xFF);
        Assert.Equal(0, centre & 0xFF);
    }

    [Fact]
    public void TheEdgesOfTheTriangles_AreDrawnOnTheSurfaceThatCanBeSeen_AndNotBehindIt()
    {
        // The front of a box is two triangles with a diagonal: a pixel on it is darker with the edges on. The back of the box has the same diagonal at the same place, hidden
        var box = TestMeshes.Box(0, 0, 0, 10, 10, 10);
        var filled = Draw(box, MeshView.Front);
        var wired = Draw(box, MeshView.Front, style: new RenderStyle { Wireframe = true });

        var (left, top, right, bottom) = TestMeshes.Silhouette(filled, Background)!.Value;
        var diagonalX = left + (right - left) / 3;
        var diagonalY = bottom - (bottom - top) / 3; // a third of the way along the diagonal from the bottom left corner to the top right
        var changed = 0;
        for (var d = -1; d <= 1; d++)
            for (var e = -1; e <= 1; e++)
                if (TestMeshes.Brightness(wired.ColorAt(diagonalX + d, diagonalY + e)) < TestMeshes.Brightness(filled.ColorAt(diagonalX + d, diagonalY + e)))
                    changed++;
        Assert.True(changed > 0, "the diagonal of the front face is drawn");

        // And what is behind a wall gets no line: a triangle behind the wall leaves the picture the same
        float[] wall = TestMeshes.Quad([-10, -8, -10], [10, -8, -10], [10, -8, 10], [-10, -8, 10]);
        float[] hidden = TestMeshes.Triangle(-3, 5, -3, 3, 5, -3, 0, 5, 3);
        var wallOnly = Draw(TestMeshes.Join(wall, hidden), MeshView.Front);
        var wallWired = Draw(TestMeshes.Join(wall, hidden), MeshView.Front, style: new RenderStyle { Wireframe = true });
        var corner = Size / 2 - 6; // inside the hidden triangle's edge, away from the wall's own diagonal
        Assert.Equal(wallOnly.ColorAt(corner, Size / 2 + 8), wallWired.ColorAt(corner, Size / 2 + 8));
    }

    [Fact]
    public void ADrawingThatIsStopped_ThrowsAndLeavesNothing()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            MeshRenderer.Render(TestMeshes.Mesh(TestMeshes.Sphere(10, 80, 160)), MeshView.Isometric, 200, 200, Plain, cancel: stop.Token));
    }

    [Fact]
    public void ATriangleWithNoArea_AndOnesOffThePicture_AreNotDrawn_AndDoNoHarm()
    {
        float[] triangles = TestMeshes.Join(
            TestMeshes.Box(0, 0, 0, 10, 10, 10),
            TestMeshes.Triangle(1, 1, 1, 1, 1, 1, 1, 1, 1),                  // a point
            TestMeshes.Triangle(0, 0, 0, 5, 5, 5, 10, 10, 10));              // a line

        var with = Draw(triangles, MeshView.Isometric);
        var without = Draw(TestMeshes.Box(0, 0, 0, 10, 10, 10), MeshView.Isometric);

        Assert.Equal(without.Bgra, with.Bgra);
    }

    [Fact]
    public void AModelFarOffThePicture_LeavesOnlyTheBackground()
    {
        // Zoomed in to the limit and moved thirty pictures away: every triangle is off it, and none of the arithmetic on the way may fail
        var away = Draw(TestMeshes.Box(0, 0, 0, 10, 10, 10), MeshView.Isometric.Zoomed(40).Panned(30, 30));

        Assert.Null(TestMeshes.Silhouette(away, Background));
    }

    [Fact]
    public void ABigModel_IsDrawnWholeAndWithoutGaps()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Sphere(10, 400, 800)); // 640,000 triangles

        var picture = MeshRenderer.Render(mesh, MeshView.Front, 500, 500, Plain);

        var radius = 0.9 * 500 / (2 * Math.Sqrt(3));
        Assert.InRange(TestMeshes.Covered(picture, Background), Math.PI * radius * radius * 0.99, Math.PI * radius * radius * 1.01);
    }

    [Fact]
    public void ThePictureSize_MustBeAtLeastOnePixel()
    {
        var mesh = TestMeshes.Mesh(TestMeshes.Box(0, 0, 0, 1, 1, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(mesh, MeshView.Front, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshRenderer.Render(mesh, MeshView.Front, 10, 0));
    }

    [Fact]
    public void ALightTheme_HasALightBackground()
    {
        Assert.True(TestMeshes.Brightness(RenderStyle.ForTheme(dark: false).Background) > TestMeshes.Brightness(RenderStyle.ForTheme(dark: true).Background) * 3);
    }
}

public class MeshViewTests
{
    [Fact]
    public void DraggingRight_TurnsTheModelRight_AndDown_TipsItsTopTowardsTheViewer()
    {
        var turned = MeshView.Front.Orbited(degreesRight: 20, degreesDown: 10);

        Assert.Equal(-20, turned.Yaw);
        Assert.Equal(10, turned.Pitch);
    }

    [Fact]
    public void ThePitch_StopsAtStraightUpAndStraightDown()
    {
        Assert.Equal(90, MeshView.Front.Orbited(0, 500).Pitch);
        Assert.Equal(-90, MeshView.Front.Orbited(0, -500).Pitch);
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(360, 0)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    public void TheYaw_StaysWithinAHalfTurnEitherWay(double started, double wrapped)
    {
        Assert.Equal(wrapped, new MeshView(started, 0).Orbited(0, 0).Yaw);
    }

    [Fact]
    public void TheZoom_StaysWithinItsLimits()
    {
        Assert.Equal(MeshView.MaxZoom, MeshView.Front.Zoomed(1e9).Zoom);
        Assert.Equal(MeshView.MinZoom, MeshView.Front.Zoomed(1e-9).Zoom);
        Assert.Equal(3, MeshView.Front.Zoomed(1.5).Zoomed(2).Zoom, 9);
    }

    [Fact]
    public void Centering_KeepsTheDirectionAndForgetsTheZoomAndTheMove()
    {
        var view = MeshView.Isometric.Zoomed(3).Panned(0.2, -0.3).Centered;

        Assert.Equal(MeshView.Isometric, view);
    }

    [Fact]
    public void TheSixSides_AreOppositeInPairs()
    {
        Assert.Equal(MeshView.Front.Yaw + 180, MeshView.Back.Yaw);
        Assert.Equal(-MeshView.Left.Yaw, MeshView.Right.Yaw);
        Assert.Equal(-MeshView.Top.Pitch, MeshView.Bottom.Pitch);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(35, 28)]
    [InlineData(-120, -60)]
    [InlineData(180, 90)]
    public void TheCameraAxes_AreUnitLengthAndAtRightAnglesToEachOther(double yaw, double pitch)
    {
        var b = new MeshView(yaw, pitch).Basis;
        double Dot(double ax, double ay, double az, double bx, double by, double bz) => ax * bx + ay * by + az * bz;

        Assert.Equal(1, Dot(b.RightX, b.RightY, b.RightZ, b.RightX, b.RightY, b.RightZ), 9);
        Assert.Equal(1, Dot(b.UpX, b.UpY, b.UpZ, b.UpX, b.UpY, b.UpZ), 9);
        Assert.Equal(1, Dot(b.ToX, b.ToY, b.ToZ, b.ToX, b.ToY, b.ToZ), 9);
        Assert.Equal(0, Dot(b.RightX, b.RightY, b.RightZ, b.UpX, b.UpY, b.UpZ), 9);
        Assert.Equal(0, Dot(b.RightX, b.RightY, b.RightZ, b.ToX, b.ToY, b.ToZ), 9);
        Assert.Equal(0, Dot(b.UpX, b.UpY, b.UpZ, b.ToX, b.ToY, b.ToZ), 9);
    }

    [Fact]
    public void FromTheFront_XIsRight_ZIsUp_AndTheViewerIsInFrontOfTheModel()
    {
        var (right, up, to) = MeshView.Front.Basis.Project(1, 0, 0);
        Assert.Equal((1, 0, 0), (Math.Round(right, 9), Math.Round(up, 9), Math.Round(to, 9)));

        (right, up, to) = MeshView.Front.Basis.Project(0, 0, 1);
        Assert.Equal((0, 1, 0), (Math.Round(right, 9), Math.Round(up, 9), Math.Round(to, 9)));

        (_, _, to) = MeshView.Front.Basis.Project(0, -1, 0); // a point nearer the viewer than the middle: at the front of the model, where y is negative
        Assert.Equal(1, to, 9);
    }

    [Fact]
    public void FromAbove_YIsUpInThePicture()
    {
        var (right, up, _) = MeshView.Top.Basis.Project(0, 1, 0);

        Assert.Equal(0, right, 9);
        Assert.Equal(1, up, 9);
    }
}
