namespace PhotoLiker.TraceBitmap.Tests;

public class ContourTracerTests
{
    private static Bilevel From(params string[] rows)
    {
        var bitmap = new Bilevel(rows[0].Length, rows.Length);
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                bitmap[x, y] = rows[y][x] == '#';
        return bitmap;
    }

    [Fact]
    public void ABlankPicture_HasNoOutline() =>
        Assert.Empty(ContourTracer.Trace(From("....", "....")));

    [Fact]
    public void OnePixel_IsASquareOfFourCorners_GoingClockwise()
    {
        var loop = Assert.Single(ContourTracer.Trace(From("...", ".#.", "...")));

        Assert.Equal(4, loop.Count);
        Assert.Equal(4, loop.Length);
        Assert.Equal(2, loop.Area2); // one pixel, twice
        Assert.False(loop.IsHole);
        Assert.Equal(new[] { (1, 1), (2, 1), (2, 2), (1, 2) }.OrderBy(p => p).ToArray(), loop.X.Zip(loop.Y).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void ARectangle_KeepsOnlyItsFourCorners_NotEveryStep()
    {
        var loop = Assert.Single(ContourTracer.Trace(From("......", ".####.", ".####.", ".####.", "......")));

        Assert.Equal(4, loop.Count);
        Assert.Equal(14, loop.Length); // 4 + 3 + 4 + 3 edges
        Assert.Equal(2 * 12, loop.Area2);
    }

    [Fact]
    public void ARingOfPixels_HasAnOuterOutlineAndAHoleGoingTheOtherWay()
    {
        var loops = ContourTracer.Trace(From(".....", ".###.", ".#.#.", ".###.", "....."));

        Assert.Equal(2, loops.Count);
        var outer = Assert.Single(loops, l => !l.IsHole);
        var hole = Assert.Single(loops, l => l.IsHole);
        Assert.Equal(2 * 9, outer.Area2);
        Assert.Equal(-2 * 1, hole.Area2);
    }

    [Fact]
    public void ThePixelsOfAShape_AreTheOuterAreasMinusTheHoles()
    {
        var bitmap = From("..........", ".########.", ".#......#.", ".#.####.#.", ".#.#..#.#.", ".#.####.#.", ".#......#.", ".########.", "..........");

        var loops = ContourTracer.Trace(bitmap);

        Assert.Equal(bitmap.BlackCount, loops.Sum(l => l.Area2) / 2);
    }

    [Fact]
    public void TwoPixelsThatTouchOnlyAtACorner_AreOneShape_WithOneOutline()
    {
        // A chessboard corner: the outline goes on to the other pixel, so black that touches at a corner is joined
        var loops = ContourTracer.Trace(From("....", ".#..", "..#.", "...."));

        var loop = Assert.Single(loops);
        Assert.Equal(8, loop.Length);
        Assert.Equal(2 * 2, loop.Area2);
    }

    [Fact]
    public void AChessboard_AlwaysGivesOutlinesWhoseAreasAddUpToTheBlackPixels()
    {
        var bitmap = new Bilevel(9, 7);
        for (var y = 0; y < 7; y++)
            for (var x = 0; x < 9; x++)
                bitmap[x, y] = (x + y) % 2 == 0;

        var loops = ContourTracer.Trace(bitmap);

        Assert.Equal(bitmap.BlackCount, loops.Sum(l => l.Area2) / 2);
        Assert.Equal(bitmap.BlackCount * 4 - InnerEdges(bitmap), loops.Sum(l => l.Length)); // every edge between black and white is walked once
    }

    private static int InnerEdges(Bilevel bitmap)
    {
        // The edges between two black pixels are not on any outline
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap[x, y])
                    count += (bitmap[x + 1, y] ? 2 : 0) + (bitmap[x, y + 1] ? 2 : 0);
        return count;
    }

    [Fact]
    public void ShapesOnTheEdgeOfThePicture_AreClosedToo()
    {
        var loop = Assert.Single(ContourTracer.Trace(From("##", "##")));

        Assert.Equal(4, loop.Count);
        Assert.Equal(2 * 4, loop.Area2);
    }

    [Fact]
    public void ACancelledTrace_Stops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => ContourTracer.Trace(From("#"), cancelled.Token));
    }
}

public class BilevelTests
{
    private static Bilevel From(params string[] rows)
    {
        var bitmap = new Bilevel(rows[0].Length, rows.Length);
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                bitmap[x, y] = rows[y][x] == '#';
        return bitmap;
    }

    private static string[] Rows(Bilevel bitmap) =>
        [.. Enumerable.Range(0, bitmap.Height).Select(y => new string([.. Enumerable.Range(0, bitmap.Width).Select(x => bitmap[x, y] ? '#' : '.')]))];

    [Fact]
    public void APixelOutsideThePicture_IsWhite()
    {
        var bitmap = From("#");

        Assert.False(bitmap[-1, 0]);
        Assert.False(bitmap[0, 1]);
        Assert.False(bitmap[1, 0]);
        Assert.True(bitmap[0, 0]);
    }

    [Fact]
    public void Speckles_OfTheSizeOrLess_AreTakenAway_AndBiggerShapesStay()
    {
        var bitmap = From("#.......", "........", "...##...", "........", "......##", "......##");

        bitmap.SuppressSpeckles(2);

        Assert.Equal(["........", "........", "........", "........", "......##", "......##"], Rows(bitmap));
    }

    [Fact]
    public void ASizeOfZero_TakesNothingAway()
    {
        var bitmap = From("#...", "....");

        bitmap.SuppressSpeckles(0);

        Assert.Equal(["#...", "...."], Rows(bitmap));
    }

    [Fact]
    public void ASmallHoleInsideBlack_IsFilled_ButNotWhiteAtTheEdgeOfThePicture()
    {
        var bitmap = From("#####", "##.##", "#####", "#...#", "#####");

        bitmap.SuppressSpeckles(1);

        // The single white pixel in the middle of the top block is a hole and goes; the three white pixels are bigger than the size and stay
        Assert.Equal(["#####", "#####", "#####", "#...#", "#####"], Rows(bitmap));
    }

    [Fact]
    public void WhiteThatReachesTheEdge_IsNotAHole()
    {
        var bitmap = From("#.#", "###");

        bitmap.SuppressSpeckles(5);

        Assert.Equal(["...", "..."], Rows(bitmap)); // the black shape is 5 pixels, a speck of that size, and goes; the white at the top is the outside, not a hole
    }

    [Fact]
    public void WhiteThatTouchesTheEdgeOfThePicture_StaysWhite_WhenBlackIsBig()
    {
        var bitmap = From("##.##", "#####", "#####");

        bitmap.SuppressSpeckles(1);

        Assert.Equal(["##.##", "#####", "#####"], Rows(bitmap));
    }

    [Fact]
    public void BlackThatTouchesAtACorner_IsOneShape_WhenSpecklesAreCounted()
    {
        var bitmap = From("#...", ".#..", "....");

        bitmap.SuppressSpeckles(1); // two pixels joined at a corner are 2 pixels, not two shapes of 1

        Assert.Equal(["#...", ".#..", "...."], Rows(bitmap));
    }

    [Fact]
    public void Inverted_SwapsBlackAndWhite() =>
        Assert.Equal(["..#", "##."], Rows(From("##.", "..#").Inverted()));
}
