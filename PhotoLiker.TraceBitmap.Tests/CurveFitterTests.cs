namespace PhotoLiker.TraceBitmap.Tests;

public class CurveFitterTests
{
    [Theory]
    [InlineData(0.0, 0.0)] // on the line
    [InlineData(0.5, 0.0)] // sticks out by less than a pixel
    [InlineData(1.0, 0.0)] // a pixel exactly
    [InlineData(4.0, 1.0)] // the corner threshold of 1 begins here: (1 - 1/4) / 0.75
    [InlineData(8.0, 1.0 / 0.75 * (1 - 1.0 / 8))]
    public void TheAlphaOfACorner_GrowsWithHowFarItSticksOutFromTheLineBetweenItsNeighbours(double distance, double expected)
    {
        var alpha = CurveFitter.CornerAlpha(new TracePoint(-10, 0), new TracePoint(0, distance), new TracePoint(10, 0));

        Assert.Equal(expected, alpha, 9);
    }

    [Fact]
    public void TheAlpha_DoesNotDependOnWhichSideTheCornerSticksOutOn_NorOnTheDirectionOfTheOutline()
    {
        var up = CurveFitter.CornerAlpha(new TracePoint(-10, 0), new TracePoint(0, 6), new TracePoint(10, 0));
        var down = CurveFitter.CornerAlpha(new TracePoint(-10, 0), new TracePoint(0, -6), new TracePoint(10, 0));
        var back = CurveFitter.CornerAlpha(new TracePoint(10, 0), new TracePoint(0, 6), new TracePoint(-10, 0));

        Assert.Equal(up, down, 9);
        Assert.Equal(up, back, 9);
    }

    [Fact]
    public void WhenTheNeighboursAreTheSamePoint_TheCornerIsASpike_AndTheAlphaIsTheLargest() =>
        Assert.Equal(4.0 / 3.0, CurveFitter.CornerAlpha(new TracePoint(3, 3), new TracePoint(9, 9), new TracePoint(3, 3)), 9);

    [Fact]
    public void ACornerIsSharp_FromTheThresholdUp_IncludingTheThreshold()
    {
        Assert.True(CurveFitter.IsSharp(1.0, 1.0));
        Assert.True(CurveFitter.IsSharp(1.2, 1.0));
        Assert.False(CurveFitter.IsSharp(0.99, 1.0));
        Assert.True(CurveFitter.IsSharp(0.0, 0.0)); // a threshold of 0 keeps every corner
        Assert.False(CurveFitter.IsSharp(1.3333, 1.3334)); // and the top of the range rounds every one
    }

    // ── The polygon ─────────────────────────────────────────────────────────

    [Fact]
    public void ANoisyStraightSide_IsReducedToItsEnds()
    {
        // A closed outline: a long side with a wobble of a third of a pixel, and a short way back
        var points = new List<TracePoint>();
        for (var x = 0; x <= 40; x++)
            points.Add(new TracePoint(x, x % 2 == 0 ? 0 : 0.3));
        points.Add(new TracePoint(40, 10));
        points.Add(new TracePoint(0, 10));

        var polygon = CurveFitter.Simplify([.. points], 1.0);

        Assert.Equal(4, polygon.Length);
        Assert.Contains(0, polygon);
        Assert.Contains(40, polygon);
    }

    [Fact]
    public void ABumpOfMoreThanTheTolerance_IsKept()
    {
        var points = new List<TracePoint>();
        for (var x = 0; x <= 20; x++)
            points.Add(new TracePoint(x, x == 10 ? 3 : 0));
        points.Add(new TracePoint(20, 20));
        points.Add(new TracePoint(0, 20));

        var polygon = CurveFitter.Simplify([.. points], 1.0);

        Assert.Contains(10, polygon);
    }

    [Fact]
    public void AnOutlineOfThreePointsOrFewer_IsReturnedAsItIs()
    {
        var triangle = new[] { new TracePoint(0, 0), new TracePoint(1, 0), new TracePoint(0, 1) };

        Assert.Equal([0, 1, 2], CurveFitter.Simplify(triangle, 1.0));
    }

    // ── Whole outlines ──────────────────────────────────────────────────────

    private static PixelLoop LoopOf(Bilevel bitmap) => Assert.Single(ContourTracer.Trace(bitmap), loop => !loop.IsHole);

    private static Bilevel Block(int width, int height, int left, int top, int right, int bottom)
    {
        var bitmap = new Bilevel(width, height);
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
                bitmap[x, y] = true;
        return bitmap;
    }

    [Fact]
    public void AShapeOfAFewPixels_KeepsItsOutline_AsLines()
    {
        var path = CurveFitter.Fit(LoopOf(Block(10, 10, 4, 4, 6, 6)), new TraceSettings());

        Assert.Equal(4, path.NodeCount);
        Assert.All(path.Segments, segment => Assert.False(segment.IsCurve));
    }

    [Fact]
    public void ThePathOfAnOutline_EndsWhereItStarted()
    {
        var path = CurveFitter.Fit(LoopOf(Block(60, 60, 10, 10, 50, 40)), new TraceSettings());

        Assert.Equal(path.Start, path.Segments[^1].End);
    }

    [Fact]
    public void ACurveThatIsStraightEnough_IsWrittenAsALine()
    {
        // A long thin bar slightly slanted: its sides are lines, not curves with the control points on them
        var bitmap = new Bilevel(200, 40);
        for (var x = 10; x < 190; x++)
            for (var y = 10 + x / 40; y < 20 + x / 40; y++)
                bitmap[x, y] = true;

        var path = CurveFitter.Fit(LoopOf(bitmap), new TraceSettings { CornerThreshold = 0.8 });

        Assert.Contains(path.Segments, segment => !segment.IsCurve);
        Assert.InRange(path.NodeCount, 4, 14);
    }
}
