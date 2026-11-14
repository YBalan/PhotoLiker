using PhotoLiker.Effects.Motion;

namespace PhotoLiker.Effects.Tests.Motion;

public class KenBurnsAimedTests
{
    private static readonly double[] Steps = [0, 0.1, 0.25, 0.5, 0.75, 0.9, 1];

    /// <summary>Where a point of the photo (a fraction of it, 0 to 1) is at a moment of the path, as a fraction of the photo at its start size and place.</summary>
    private static (double X, double Y) Place(KenBurnsPath path, double progress, double x, double y)
    {
        var layer = path.At(progress);
        return (layer.OffsetX + layer.Scale * (x - 0.5) + 0.5, layer.OffsetY + layer.Scale * (y - 0.5) + 0.5);
    }

    private static Func<double> Sequence(params double[] numbers)
    {
        var i = 0;
        return () => numbers[i++ % numbers.Length];
    }

    /// <summary>Whether the head round a face (see <see cref="PhotoRegion.WithHead"/>) is inside the photo at every moment of the path.</summary>
    private static bool KeepsInView(KenBurnsPath path, params PhotoRegion[] faces)
    {
        foreach (var face in faces.Select(face => face.WithHead()))
        foreach (var progress in Steps)
        {
            var (left, top) = Place(path, progress, face.X, face.Y);
            var (right, bottom) = Place(path, progress, face.Right, face.Bottom);
            if (left < -1e-9 || top < -1e-9 || right > 1 + 1e-9 || bottom > 1 + 1e-9)
                return false;
        }
        return true;
    }

    [Fact]
    public void WithNoFaces_ItIsTheRandomPath_AskingForTheSameNumbers()
    {
        foreach (var faces in new IReadOnlyList<PhotoRegion>[] { [], [new PhotoRegion(0.4, 0.4, 0, 0.1)], [new PhotoRegion(double.NaN, 0.4, 0.1, 0.1)] })
            Assert.Equal(KenBurnsPath.Random(Sequence(0.3, 0.2, 0.9, 0.1)), KenBurnsPath.Aimed(faces, Sequence(0.3, 0.2, 0.9, 0.1)));
    }

    [Fact]
    public void AFaceInTheMiddle_IsZoomedAtItsMiddle_AsMuchAsRandomWould()
    {
        var face = new PhotoRegion(0.45, 0.35, 0.1, 0.15);

        var path = KenBurnsPath.Aimed([face], Sequence(0.5, 0.2));

        Assert.Equal(1, path.FromScale);                 // zooming in (0.2 < 0.5)
        Assert.Equal(1 + KenBurnsPath.MinZoom + (KenBurnsPath.MaxZoom - KenBurnsPath.MinZoom) * 0.5, path.ToScale, 9);
        Assert.Equal(0, path.AnchorX, 9);                // the face is in the middle across
        Assert.Equal(-0.1875, path.AnchorY, 9);          // and above the middle down: the middle of the face with its head (more of it is above the box than below), not of the box
    }

    [Fact]
    public void TheFaceStaysWhereItIs_WhileItIsZoomedInto_AndZoomingOutEndsAsMade()
    {
        var face = new PhotoRegion(0.2, 0.3, 0.1, 0.1);
        var ahead = KenBurnsPath.Aimed([face], Sequence(0.9, 0.1));
        var back = KenBurnsPath.Aimed([face], Sequence(0.9, 0.9));

        Assert.True(ahead.At(0).IsPlain);
        Assert.True(back.At(1).IsPlain);
        Assert.True(back.ToScale == 1 && back.FromScale > 1);
        Assert.True(ahead.ToScale > 1);
    }

    [Fact]
    public void AGroupAtTheEdge_IsKeptInViewByAnAnchorAtTheEdge_NotCutByTheZoom()
    {
        var face = new PhotoRegion(0.0, 0.4, 0.12, 0.2); // against the left edge

        var path = KenBurnsPath.Aimed([face], Sequence(1, 0.1)); // the largest zoom, in

        Assert.True(path.ToScale > 1 + KenBurnsPath.MinUsefulZoom);
        Assert.True(path.AnchorX <= -0.5, $"the anchor {path.AnchorX} should be at the left edge, where the face is");
        Assert.True(KeepsInView(path, face));
    }

    [Fact]
    public void TheZoomIsHeldBack_WhenTheFacesLeaveNoRoomForIt()
    {
        var wide = new PhotoRegion(0.1, 0.3, 0.3, 0.2);
        var wide2 = new PhotoRegion(0.55, 0.3, 0.3, 0.2); // together with their heads they span 0.025..0.925 across: room for 1.11 at most

        var path = KenBurnsPath.Aimed([wide, wide2], Sequence(1, 0.1));

        Assert.True(path.ToScale < 1 + KenBurnsPath.MaxZoom);
        Assert.True(KeepsInView(path, wide, wide2));
    }

    [Fact]
    public void FacesThatFillThePhoto_LeaveItStandingStill()
    {
        var path = KenBurnsPath.Aimed([new PhotoRegion(0.1, 0.05, 0.8, 0.85)], Sequence(0.5, 0.5));

        Assert.Equal(new KenBurnsPath(1, 1, 0, 0), path);
        Assert.True(path.At(0.5).IsPlain);
    }

    [Fact]
    public void APasserBy_IsLeftToChance_ButARealCompanionIsKept()
    {
        var main = new PhotoRegion(0.4, 0.3, 0.2, 0.3);
        var passerBy = new PhotoRegion(0.95, 0.9, 0.03, 0.04);     // far in a corner, a fraction of the area of the main face
        var companion = new PhotoRegion(0.7, 0.35, 0.15, 0.25);    // 60% of its area

        var alone = KenBurnsPath.Aimed([main, passerBy], Sequence(1, 0.1));
        var together = KenBurnsPath.Aimed([main, passerBy, companion], Sequence(1, 0.1));

        Assert.True(KeepsInView(alone, main));
        Assert.True(KeepsInView(together, main, companion));
        Assert.Equal(1.16, alone.ToScale, 9);              // the passer-by did not hold the zoom back
        Assert.True(together.AnchorX > alone.AnchorX);     // the companion moved the point to the right
    }

    [Fact]
    public void NoFaceIsCut_WhateverTheFacesAndTheLuck_WhenThereIsRoomToKeepThemAll()
    {
        var random = new Random(77);
        var tried = 0;
        for (var i = 0; i < 4000; i++)
        {
            var faces = new List<PhotoRegion>();
            for (var f = random.Next(1, 5); f > 0; f--)
            {
                var width = 0.03 + random.NextDouble() * 0.25;
                var height = width * (0.8 + random.NextDouble() * 0.6);
                faces.Add(new PhotoRegion(random.NextDouble() * (1 - width), random.NextDouble() * (1 - height), width, height));
            }

            var path = KenBurnsPath.Aimed(faces, random.NextDouble);
            var largest = faces.Max(face => face.Area);
            var counted = faces.Where(face => face.Area >= KenBurnsPath.PasserbyShare * largest).ToArray();
            tried++;

            Assert.True(KeepsInView(path, counted), $"faces {string.Join(" ", counted)} cut by {path}");

            // And it never uncovers what the photo covered at the start
            foreach (var progress in Steps)
            {
                var layer = path.At(progress);
                Assert.True(layer.OffsetX - layer.Scale / 2 <= -0.5 + 1e-9 && layer.OffsetX + layer.Scale / 2 >= 0.5 - 1e-9);
                Assert.True(layer.OffsetY - layer.Scale / 2 <= -0.5 + 1e-9 && layer.OffsetY + layer.Scale / 2 >= 0.5 - 1e-9);
            }
        }
        Assert.Equal(4000, tried);
    }

    [Fact]
    public void ARandomPath_DoesCutFaces_WhichIsWhatThisIsFor()
    {
        // The reason for the whole thing: a face at the edge, and a random path that zooms about the other edge
        var face = new PhotoRegion(0.0, 0.4, 0.12, 0.2);
        var random = new KenBurnsPath(1, 1.16, 1, 0);

        Assert.False(KeepsInView(random, face));
    }
}
