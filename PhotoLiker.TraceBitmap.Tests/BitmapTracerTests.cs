namespace PhotoLiker.TraceBitmap.Tests;

public class BitmapTracerTests
{
    private static TraceSettings Plain(TraceMode mode = TraceMode.BrightnessCutoff) => new() { Mode = mode };

    [Fact]
    public void ADisc_IsOneBlackLayer_WithOneOutline_OfFewCurves()
    {
        var trace = BitmapTracer.Trace(Shapes.Disc(100, 40), Plain());

        var layer = Assert.Single(trace.Layers);
        Assert.Equal(TraceColor.Black, layer.Color);
        var path = Assert.Single(layer.Paths);
        Assert.InRange(path.NodeCount, 4, 24); // a circle is a handful of curves, not a hundred steps
        Assert.All(path.Segments, segment => Assert.True(segment.IsCurve));
        Assert.Equal(100, trace.Width);
        Assert.Equal(100, trace.Height);
    }

    [Fact]
    public void TheTraceOfADisc_FillsBackTheSamePixels()
    {
        var image = Shapes.Disc(100, 40);

        var trace = BitmapTracer.Trace(image, Plain());

        Assert.True(Shapes.Overlap(Shapes.BlackOf(image), Shapes.Fill(trace, 0)) > 0.985);
    }

    [Fact]
    public void ARing_HasAHole_AndIsFilledBackAsARing()
    {
        var image = Shapes.Ring(120, 50, 25);

        var trace = BitmapTracer.Trace(image, Plain());

        Assert.Equal(2, trace.Layers[0].Paths.Count);
        var filled = Shapes.Fill(trace, 0);
        Assert.False(filled[60, 60]); // the hole
        Assert.True(filled[60, 25]); // the ring
        Assert.True(Shapes.Overlap(Shapes.BlackOf(image), filled) > 0.98);
    }

    [Fact]
    public void ARectangle_KeepsItsFourSharpCorners_AsFourLines()
    {
        var trace = BitmapTracer.Trace(Shapes.Rectangle(80, 60, 10, 10, 70, 50), Plain());

        var path = Assert.Single(trace.Layers[0].Paths);
        Assert.Equal(4, path.NodeCount);
        Assert.All(path.Segments, segment => Assert.False(segment.IsCurve));
        Assert.Equal(new TracePoint(10, 10), path.Start);
        Assert.Contains(path.Segments, s => s.End == new TracePoint(70, 10));
        Assert.Contains(path.Segments, s => s.End == new TracePoint(70, 50));
        Assert.Contains(path.Segments, s => s.End == new TracePoint(10, 50));
    }

    [Fact]
    public void ARoundedRectangle_KeepsLongSidesStraight_AndRoundsTheCorners()
    {
        var image = Shapes.Draw(160, 100, Shapes.White, (x, y) =>
        {
            // A rectangle of 140 x 80 with corners of a radius of 20
            var cx = Math.Clamp(x, 30, 130);
            var cy = Math.Clamp(y, 30, 70);
            return Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) < 20 ? Shapes.Black : null;
        });

        var trace = BitmapTracer.Trace(image, Plain());

        var path = Assert.Single(trace.Layers[0].Paths);
        Assert.InRange(path.NodeCount, 4, 20);
        Assert.True(Shapes.Overlap(Shapes.BlackOf(image), Shapes.Fill(trace, 0)) > 0.985);
    }

    [Fact]
    public void ADiagonalBar_OfSteps_IsFittedWithFewNodes_AndTheSameArea()
    {
        var image = Shapes.Draw(120, 120, Shapes.White, (x, y) => Math.Abs(x - y) < 6 && x > 10 && x < 110 ? Shapes.Black : null);

        var trace = BitmapTracer.Trace(image, Plain());

        var path = Assert.Single(trace.Layers[0].Paths);
        Assert.InRange(path.NodeCount, 4, 12); // 200 steps become a few lines
        Assert.True(Shapes.Overlap(Shapes.BlackOf(image), Shapes.Fill(trace, 0)) > 0.95);
    }

    [Fact]
    public void ACloserCutoff_TracesLessOfAGradient()
    {
        var gradient = Shapes.Draw(100, 20, Shapes.White, (x, _) => { var v = (byte)(x * 255 / 99); return (v, v, v); });

        var dark = BitmapTracer.Trace(gradient, new TraceSettings { BrightnessCutoff = 0.25 });
        var darkAndLight = BitmapTracer.Trace(gradient, new TraceSettings { BrightnessCutoff = 0.75 });

        var small = Shapes.Fill(dark, 0).BlackCount;
        var big = Shapes.Fill(darkAndLight, 0).BlackCount;
        Assert.InRange(small, 20 * 22, 20 * 28); // a quarter of the width, give or take the rounding of the curves
        Assert.InRange(big, 20 * 72, 20 * 78);
    }

    [Fact]
    public void InvertImage_TracesTheLightInsteadOfTheDark()
    {
        var image = Shapes.Disc(100, 30);

        var trace = BitmapTracer.Trace(image, new TraceSettings { InvertImage = true });

        var filled = Shapes.Fill(trace, 0);
        Assert.False(filled[50, 50]); // the disc is not traced
        Assert.True(filled[5, 5]); // the paper is
    }

    [Fact]
    public void Specks_AreSuppressed_WhenAsked_AndKeptWhenNot()
    {
        var image = Shapes.Draw(60, 60, Shapes.White, (x, y) =>
            (x is >= 10 and < 12 && y is >= 10 and < 11) || (x is >= 30 and < 50 && y is >= 30 and < 50) ? Shapes.Black : null);

        var suppressed = BitmapTracer.Trace(image, new TraceSettings { SuppressSpeckles = true, SpeckleSize = 2 });
        var kept = BitmapTracer.Trace(image, new TraceSettings { SuppressSpeckles = false });

        Assert.Single(suppressed.Layers[0].Paths);
        Assert.Equal(2, kept.Layers[0].Paths.Count);
    }

    [Fact]
    public void WithoutSmoothCorners_EveryOutline_IsPolygonOfStraightLines()
    {
        var trace = BitmapTracer.Trace(Shapes.Disc(100, 40), new TraceSettings { SmoothCorners = false });

        var path = Assert.Single(trace.Layers[0].Paths);
        Assert.All(path.Segments, segment => Assert.False(segment.IsCurve));
        Assert.True(path.NodeCount >= 8);
    }

    [Fact]
    public void ACornerThresholdOfZero_KeepsEveryCorner()
    {
        var trace = BitmapTracer.Trace(Shapes.Disc(100, 40), new TraceSettings { CornerThreshold = 0 });

        Assert.All(trace.Layers[0].Paths[0].Segments, segment => Assert.False(segment.IsCurve));
    }

    [Fact]
    public void ALargerTolerance_GivesFewerNodes_AndNoOptimizingGivesMore()
    {
        var image = Shapes.Draw(200, 200, Shapes.White, (x, y) =>
        {
            // A wavy blob
            var angle = Math.Atan2(y - 100, x - 100);
            var radius = 60 + 12 * Math.Sin(angle * 5);
            return Math.Sqrt((x - 100) * (x - 100) + (y - 100) * (y - 100)) < radius ? Shapes.Black : null;
        });

        var loose = BitmapTracer.Trace(image, new TraceSettings { OptimizePaths = true, OptimizeTolerance = 2.0 }).NodeCount;
        var normal = BitmapTracer.Trace(image, new TraceSettings { OptimizePaths = true, OptimizeTolerance = 0.2 }).NodeCount;
        var off = BitmapTracer.Trace(image, new TraceSettings { OptimizePaths = false }).NodeCount;

        Assert.True(loose < normal, $"{loose} < {normal}");
        Assert.True(normal <= off, $"{normal} <= {off}");
    }

    [Fact]
    public void TheEdgesOfADisc_AreTraced_AsALineAroundIt()
    {
        var trace = BitmapTracer.Trace(Shapes.Disc(100, 30), Plain(TraceMode.EdgeDetection));

        var filled = Shapes.Fill(trace, 0);
        Assert.False(filled[50, 50]); // inside the line
        Assert.False(filled[5, 5]); // outside it
        Assert.True(filled.BlackCount is > 100 and < 600);
    }

    [Fact]
    public void ColorQuantization_DrawsTheLinesWhereColoursMeet()
    {
        var image = Shapes.Draw(60, 60, Shapes.White, (x, y) => x < 30 ? Shapes.Rgb(200, 30, 30) : Shapes.Rgb(30, 30, 200));

        var trace = BitmapTracer.Trace(image, new TraceSettings { Mode = TraceMode.ColorQuantization, QuantizeColors = 2, SuppressSpeckles = false });

        var filled = Shapes.Fill(trace, 0);
        Assert.True(filled[29, 30] || filled[30, 30]);
        Assert.False(filled[10, 30]);
        Assert.False(filled[50, 30]);
    }

    // ── Multiple scans ──────────────────────────────────────────────────

    private static PixelImage ThreeBands() =>
        Shapes.Draw(90, 30, Shapes.White, (x, _) => x < 30 ? Shapes.Rgb(0, 0, 0) : x < 60 ? Shapes.Rgb(128, 128, 128) : Shapes.Rgb(255, 255, 255));

    [Fact]
    public void BrightnessSteps_MakeOneLayerForEachScan_TheLightestFirst_InGrays()
    {
        var trace = BitmapTracer.Trace(ThreeBands(), new TraceSettings { Mode = TraceMode.BrightnessSteps, BrightnessSteps = 4, Smooth = false, RemoveBackground = false });

        Assert.True(trace.Layers.Count >= 2);
        Assert.All(trace.Layers, layer => Assert.True(layer.Color.R == layer.Color.G && layer.Color.G == layer.Color.B));
        var brightness = trace.Layers.Select(layer => layer.Color.Brightness).ToList();
        Assert.Equal(brightness.OrderByDescending(b => b), brightness); // from the bottom (light) up to the top (dark)
    }

    [Fact]
    public void WhenStacked_EachScanCoversEverythingDarker_SoTheTopOnesAreInsideTheBottomOnes()
    {
        var trace = BitmapTracer.Trace(ThreeBands(), new TraceSettings { Mode = TraceMode.Grays, Grays = 4, Smooth = false, RemoveBackground = false, StackScans = true });

        var sizes = Enumerable.Range(0, trace.Layers.Count).Select(i => Shapes.Fill(trace, i).BlackCount).ToList();
        Assert.Equal(sizes.OrderByDescending(s => s), sizes);
        Assert.True(sizes[0] > sizes[^1]);
    }

    [Fact]
    public void WhenNotStacked_TheLayersDoNotOverlap()
    {
        var trace = BitmapTracer.Trace(ThreeBands(), new TraceSettings { Mode = TraceMode.Grays, Grays = 4, Smooth = false, RemoveBackground = false, StackScans = false });

        var fills = Enumerable.Range(0, trace.Layers.Count).Select(i => Shapes.Fill(trace, i)).ToList();
        for (var a = 0; a < fills.Count; a++)
            for (var b = a + 1; b < fills.Count; b++)
                Assert.True(Shapes.Overlap(fills[a], fills[b]) < 0.02, $"layers {a} and {b} overlap");
    }

    [Fact]
    public void RemoveBackground_LeavesOutTheBottomLayer()
    {
        var with = BitmapTracer.Trace(ThreeBands(), new TraceSettings { Mode = TraceMode.Grays, Grays = 4, Smooth = false, RemoveBackground = false });
        var without = BitmapTracer.Trace(ThreeBands(), new TraceSettings { Mode = TraceMode.Grays, Grays = 4, Smooth = false, RemoveBackground = true });

        Assert.Equal(with.Layers.Count - 1, without.Layers.Count);
        Assert.Equal(with.Layers[1].Color, without.Layers[0].Color);
    }

    [Fact]
    public void Colors_UseTheColoursOfThePicture()
    {
        var image = Shapes.Draw(90, 30, Shapes.White, (x, _) => x < 30 ? Shapes.Rgb(255, 0, 0) : x < 60 ? Shapes.Rgb(0, 160, 0) : Shapes.Rgb(0, 0, 255));

        var trace = BitmapTracer.Trace(image, new TraceSettings { Mode = TraceMode.Colors, Colors = 3, Smooth = false, RemoveBackground = false });

        var colours = trace.Layers.Select(layer => layer.Color).ToList();
        Assert.Contains(new TraceColor(255, 0, 0), colours);
        Assert.Contains(new TraceColor(0, 160, 0), colours);
        Assert.Contains(new TraceColor(0, 0, 255), colours);
    }

    [Fact]
    public void Smooth_MakesFewerShapes_OutOfNoise()
    {
        var random = new Random(7);
        var noisy = Shapes.Draw(80, 80, Shapes.White, (x, y) => { var v = (byte)Math.Clamp(128 + random.Next(-40, 41) + (x < 40 ? -50 : 50), 0, 255); return (v, v, v); });

        var blurred = BitmapTracer.Trace(noisy, new TraceSettings { Mode = TraceMode.BrightnessSteps, BrightnessSteps = 4, Smooth = true, SuppressSpeckles = false });
        var raw = BitmapTracer.Trace(noisy, new TraceSettings { Mode = TraceMode.BrightnessSteps, BrightnessSteps = 4, Smooth = false, SuppressSpeckles = false });

        Assert.True(blurred.PathCount < raw.PathCount, $"{blurred.PathCount} < {raw.PathCount}");
    }

    [Fact]
    public void APictureOfOneColour_HasNothingToTrace_WhenTheBackgroundIsLeftOut()
    {
        var plain = Shapes.Draw(40, 40, (90, 90, 90), (_, _) => null);

        var trace = BitmapTracer.Trace(plain, new TraceSettings { Mode = TraceMode.Colors, Colors = 4, RemoveBackground = true });

        Assert.Empty(trace.Layers);
    }

    // ── Settings, progress, stopping ────────────────────────────────────

    [Fact]
    public void ValuesOutsideTheirRange_AreBroughtInside()
    {
        var settings = new TraceSettings
        {
            BrightnessCutoff = 7, EdgeThreshold = -3, QuantizeColors = 1, BrightnessSteps = 5000, Colors = 0, Grays = -2,
            SpeckleSize = -1, CornerThreshold = double.NaN, OptimizeTolerance = 99, Mode = (TraceMode)99,
        }.Normalized();

        Assert.Equal(1, settings.BrightnessCutoff);
        Assert.Equal(0, settings.EdgeThreshold);
        Assert.Equal(TraceSettings.MinScans, settings.QuantizeColors);
        Assert.Equal(TraceSettings.MaxScans, settings.BrightnessSteps);
        Assert.Equal(TraceSettings.MinScans, settings.Colors);
        Assert.Equal(TraceSettings.MinScans, settings.Grays);
        Assert.Equal(0, settings.SpeckleSize);
        Assert.Equal(1.0, settings.CornerThreshold);
        Assert.Equal(TraceSettings.MaxTolerance, settings.OptimizeTolerance);
        Assert.Equal(TraceMode.BrightnessCutoff, settings.Mode);
    }

    [Fact]
    public void Progress_IsReported_FromNothingToEverything_ForScans()
    {
        var reports = new List<double>();
        var progress = new Progress<double>(reports.Add);
        var syncing = new SyncProgress(reports);

        BitmapTracer.Trace(ThreeBands(), new TraceSettings { Mode = TraceMode.Grays, Grays = 4, Smooth = false }, syncing);

        Assert.Equal(0, reports[0]);
        Assert.Equal(1, reports[^1]);
        Assert.Equal(reports.OrderBy(r => r), reports);
        Assert.True(reports.Count >= 4);
        GC.KeepAlive(progress);
    }

    private sealed class SyncProgress(List<double> reports) : IProgress<double>
    {
        public void Report(double value) => reports.Add(value);
    }

    [Fact]
    public void ACancelledTrace_ThrowsAndMakesNothing()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => BitmapTracer.Trace(Shapes.Disc(100, 40), new TraceSettings(), null, cancel.Token));
    }

    [Fact]
    public void ThePictureOfTheWrongLength_IsRefused() =>
        Assert.Throws<ArgumentException>(() => new PixelImage(4, 4, new byte[10]));

    [Fact]
    public void TheSameSettingsOnTheSamePicture_GiveTheSameTrace()
    {
        var image = Shapes.Ring(100, 40, 20);

        var first = SvgWriter.Write(BitmapTracer.Trace(image, new TraceSettings()), 50, 50);
        var second = SvgWriter.Write(BitmapTracer.Trace(image, new TraceSettings()), 50, 50);

        Assert.Equal(first, second);
    }
}

public class CurveSafetyTests
{
    /// <summary>A picture of organic blobs and fine grain: a few waves of random size and direction, and noise on top.</summary>
    private static PixelImage Blobs(int seed, int size)
    {
        var random = new Random(seed);
        var waves = Enumerable.Range(0, 7).Select(_ => (Fx: random.NextDouble() * 0.12, Fy: random.NextDouble() * 0.12, Phase: random.NextDouble() * 6.3, Weight: 0.4 + random.NextDouble())).ToArray();
        return Shapes.Draw(size, size, Shapes.White, (x, y) =>
        {
            var value = waves.Sum(w => w.Weight * Math.Sin(x * w.Fx + y * w.Fy + w.Phase)) + (random.NextDouble() - 0.5) * 0.9;
            var v = (byte)Math.Clamp(128 + value * 40, 0, 255);
            return (v, (byte)(255 - v / 2), v);
        });
    }

    [Theory]
    [InlineData(TraceMode.BrightnessCutoff)]
    [InlineData(TraceMode.Colors)]
    [InlineData(TraceMode.Grays)]
    public void NoHandleOfACurve_FliesOutOfThePicture(TraceMode mode)
    {
        foreach (var seed in Enumerable.Range(1, 12))
        {
            var image = Blobs(seed, 220);

            var trace = BitmapTracer.Trace(image, new TraceSettings { Mode = mode, Smooth = false, SuppressSpeckles = false });

            foreach (var layer in trace.Layers)
            {
                foreach (var path in layer.Paths)
                {
                    foreach (var segment in path.Segments.Where(s => s.IsCurve))
                    {
                        foreach (var point in new[] { segment.Control1, segment.Control2 })
                        {
                            Assert.InRange(point.X, -30, image.Width + 30);
                            Assert.InRange(point.Y, -30, image.Height + 30);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ACurve_NeverHasAHandleMuchLongerThanTheOutlineItBelongsTo()
    {
        var trace = BitmapTracer.Trace(Blobs(3, 300), new TraceSettings { Smooth = false, SuppressSpeckles = false });

        foreach (var path in trace.Layers[0].Paths)
        {
            var previous = path.Start;
            var length = 0.0;
            foreach (var segment in path.Segments)
            {
                length += previous.DistanceTo(segment.End);
                previous = segment.End;
            }

            previous = path.Start;
            foreach (var segment in path.Segments.Where(s => s.IsCurve))
            {
                Assert.True(previous.DistanceTo(segment.Control1) <= length + 2, "the first handle is longer than the outline");
                Assert.True(segment.End.DistanceTo(segment.Control2) <= length + 2, "the second handle is longer than the outline");
                previous = segment.End;
            }
        }
    }
}
