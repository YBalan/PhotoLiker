namespace PhotoLiker.TraceBitmap.Tests;

public class BinarizerTests
{
    [Fact]
    public void Luminance_WeighsGreenMost_AndKeepsWhiteAndBlack()
    {
        var image = new PixelImage(4, 1, [
            0, 0, 0, // black
            255, 255, 255, // white
            0, 0, 255, // pure red (B, G, R)
            0, 255, 0, // pure green
        ]);

        var luminance = Binarizer.Luminance(image);

        Assert.Equal(0, luminance[0]);
        Assert.Equal(255, luminance[1]);
        Assert.InRange(luminance[2], 75, 77); // 0.299 * 255
        Assert.InRange(luminance[3], 149, 151); // 0.587 * 255
    }

    [Fact]
    public void TheCutoff_DecidesWhichPixelsAreBlack()
    {
        byte[] luminance = [0, 100, 114, 115, 200, 255];

        var low = Binarizer.BrightnessCutoff(luminance, 6, 1, 0.45); // 114.75
        var none = Binarizer.BrightnessCutoff(luminance, 6, 1, 0.0);
        var all = Binarizer.BrightnessCutoff(luminance, 6, 1, 1.0);

        Assert.Equal([1, 1, 1, 0, 0, 0], low.Pixels);
        Assert.Equal(0, none.BlackCount);
        Assert.Equal(5, all.BlackCount); // 255 is not darker than the whole range
    }

    [Fact]
    public void BrightnessLevels_CutAtEvenlySpacedThresholds_WithPaperAboveThem()
    {
        // Three scans cut at 1/4, 2/4 and 3/4
        byte[] luminance = [0, 60, 63, 64, 127, 128, 190, 192, 255];

        var levels = Binarizer.BrightnessLevels(luminance, 3);

        Assert.Equal([0, 0, 0, 1, 1, 2, 2, 3, 3], levels);
        Assert.Equal(0.25, Binarizer.BrightnessThreshold(0, 3));
        Assert.Equal(0.75, Binarizer.BrightnessThreshold(2, 3));
    }

    [Fact]
    public void GrayLevels_AreEqualBands_AndTheGrayOfABandIsItsMiddle()
    {
        byte[] luminance = [0, 63, 64, 127, 128, 191, 192, 255];

        var levels = Binarizer.GrayLevels(luminance, 4);

        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 3], levels);
        Assert.Equal(new TraceColor(32, 32, 32), Binarizer.GrayOfBand(0, 4));
        Assert.Equal(new TraceColor(223, 223, 223), Binarizer.GrayOfBand(3, 4));
    }

    [Fact]
    public void TheGrayOfABrightnessStep_IsTheMiddleOfItsBand_DarkestFirst()
    {
        var grays = Enumerable.Range(0, 4).Select(step => Binarizer.GrayOfBrightnessStep(step, 4).R).ToList();

        Assert.Equal(grays.OrderBy(g => g), grays);
        Assert.Equal(26, grays[0]); // half of 0.2 of 255
        Assert.Equal(new TraceColor(26, 26, 26), Binarizer.GrayOfBrightnessStep(0, 4));
    }

    [Fact]
    public void Blurred_KeepsAFlatPictureFlat_AndSpreadsASingleDot()
    {
        var flat = new PixelImage(5, 5, Enumerable.Repeat((byte)90, 75).ToArray());
        var dot = new byte[5 * 5 * 3];
        for (var c = 0; c < 3; c++)
            dot[(2 * 5 + 2) * 3 + c] = 255;

        var blurredFlat = Binarizer.Blurred(flat);
        var blurredDot = Binarizer.Blurred(new PixelImage(5, 5, dot));

        Assert.All(blurredFlat.Bgr, value => Assert.Equal(90, value));
        Assert.True(blurredDot.Bgr[(2 * 5 + 2) * 3] < 255);
        Assert.True(blurredDot.Bgr[(2 * 5 + 1) * 3] > 0);
        Assert.True(blurredDot.Bgr[(1 * 5 + 1) * 3] > 0);
    }

    [Fact]
    public void Boundaries_AreTheEdgesBetweenDifferentPaletteNumbers()
    {
        byte[] indices = [0, 0, 1, 1, 0, 0, 1, 1];

        var boundaries = Binarizer.Boundaries(indices, 4, 2);

        Assert.Equal([0, 1, 0, 0, 0, 1, 0, 0], boundaries.Pixels);
    }

    // ── Edges ──────────────────────────────────────────────────────────

    [Fact]
    public void AStepInBrightness_GivesAThinLineAtTheStep_AndNothingElse()
    {
        var luminance = new byte[40 * 20];
        for (var y = 0; y < 20; y++)
            for (var x = 20; x < 40; x++)
                luminance[y * 40 + x] = 200;

        var edges = Binarizer.Edges(luminance, 40, 20, 0.65);

        for (var y = 3; y < 17; y++)
        {
            var edgeAt = Enumerable.Range(0, 40).Where(x => edges[x, y]).ToList();
            Assert.InRange(edgeAt.Count, 1, 2); // a thin line
            Assert.InRange(edgeAt[0], 18, 21); // at the step
        }
    }

    [Fact]
    public void APictureWithNoChange_HasNoEdges() =>
        Assert.Equal(0, Binarizer.Edges(Enumerable.Repeat((byte)128, 30 * 30).ToArray(), 30, 30, 0.5).BlackCount);

    [Fact]
    public void AWeakEdge_IsOnlyKept_WhenItIsJoinedToAStrongOne()
    {
        // A strong step on the left half that fades into a weak one on the right half
        var luminance = new byte[60 * 30];
        for (var y = 0; y < 30; y++)
        {
            for (var x = 30; x < 60; x++)
            {
                var strength = y < 15 ? 200 : 20;
                luminance[y * 60 + x] = (byte)strength;
            }
        }

        var strict = Binarizer.Edges(luminance, 60, 30, 0.95);
        var joined = Binarizer.Edges(luminance, 60, 30, 0.95, lowThreshold: 0.02);

        Assert.True(joined.BlackCount >= strict.BlackCount);
        Assert.True(strict.BlackCount > 0);
    }

    [Fact]
    public void ALowerThreshold_FindsMoreEdges()
    {
        var random = new Random(3);
        var luminance = Enumerable.Range(0, 80 * 80).Select(i => (byte)(i % 80 < 40 ? 60 + random.Next(0, 30) : 150 + random.Next(0, 30))).ToArray();

        var high = Binarizer.Edges(luminance, 80, 80, 0.9).BlackCount;
        var low = Binarizer.Edges(luminance, 80, 80, 0.2).BlackCount;

        Assert.True(low >= high);
    }

    [Fact]
    public void ATinyPicture_HasNoEdges_AndDoesNotThrow() =>
        Assert.Equal(0, Binarizer.Edges([0, 255], 2, 1, 0.5).BlackCount);
}

public class ColorQuantizerTests
{
    private static PixelImage Stripes(params (byte R, byte G, byte B)[] colours)
    {
        var width = colours.Length * 10;
        return Shapes.Draw(width, 4, Shapes.White, (x, _) => colours[(int)x / 10]);
    }

    [Fact]
    public void ThreeFlatColours_AreFoundExactly_DarkestFirst()
    {
        var image = Stripes((255, 0, 0), (0, 160, 0), (0, 0, 255));

        var reduced = ColorQuantizer.Reduce(image, 3);

        Assert.Equal(3, reduced.Palette.Length);
        Assert.Equal(reduced.Palette.OrderBy(c => c.Brightness), reduced.Palette);
        Assert.Contains(new TraceColor(255, 0, 0), reduced.Palette);
        Assert.Contains(new TraceColor(0, 160, 0), reduced.Palette);
        Assert.Contains(new TraceColor(0, 0, 255), reduced.Palette);
    }

    [Fact]
    public void EveryPixel_GetsTheNumberOfItsOwnColour()
    {
        var image = Stripes((255, 0, 0), (0, 160, 0), (0, 0, 255));

        var reduced = ColorQuantizer.Reduce(image, 3);

        for (var stripe = 0; stripe < 3; stripe++)
        {
            var numbers = Enumerable.Range(stripe * 10, 10).Select(x => reduced.Indices[x]).Distinct().ToList();
            Assert.Single(numbers);
        }

        Assert.Equal(3, reduced.Indices.Distinct().Count());
    }

    [Fact]
    public void AskingForMoreColoursThanThePictureHas_GivesTheColoursItHas()
    {
        var reduced = ColorQuantizer.Reduce(Stripes((10, 10, 10), (240, 240, 240)), 16);

        Assert.Equal(2, reduced.Palette.Length);
    }

    [Fact]
    public void ManyShadesOfTwoGroups_AreReducedToTwoColours_NearTheAverageOfEachGroup()
    {
        var random = new Random(5);
        var image = Shapes.Draw(40, 10, Shapes.White, (x, _) => { var v = (byte)(x < 20 ? 30 + random.Next(0, 20) : 200 + random.Next(0, 20)); return (v, v, v); });

        var reduced = ColorQuantizer.Reduce(image, 2);

        Assert.Equal(2, reduced.Palette.Length);
        Assert.InRange(reduced.Palette[0].R, 30, 50);
        Assert.InRange(reduced.Palette[1].R, 200, 220);
    }
}

public class TraceDialogSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PhotoLikerTraceSettings_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private string File1 => Path.Combine(_folder, "nested", "trace.json");

    [Fact]
    public void WhatIsSaved_IsReadBackAsItWas()
    {
        var settings = new TraceDialogSettings
        {
            Trace = new TraceSettings { Mode = TraceMode.Colors, Colors = 12, BrightnessCutoff = 0.3, InvertImage = true, StackScans = false, SpeckleSize = 7, CornerThreshold = 0.6, OptimizeTolerance = 1.5 },
            Detail = 2000,
            LongestSideMillimetres = 250.5,
            Format = TraceFileFormat.Dxf,
            CurveAccuracyMillimetres = 0.1,
            KeepProportions = false,
            LivePreview = true,
        };

        Assert.True(settings.Save(File1));
        var read = TraceDialogSettings.Load(File1);

        Assert.Equal(TraceMode.Colors, read.Trace.Mode);
        Assert.Equal(12, read.Trace.Colors);
        Assert.Equal(0.3, read.Trace.BrightnessCutoff);
        Assert.True(read.Trace.InvertImage);
        Assert.False(read.Trace.StackScans);
        Assert.Equal(7, read.Trace.SpeckleSize);
        Assert.Equal(0.6, read.Trace.CornerThreshold);
        Assert.Equal(1.5, read.Trace.OptimizeTolerance);
        Assert.Equal(2000, read.Detail);
        Assert.Equal(250.5, read.LongestSideMillimetres);
        Assert.Equal(TraceFileFormat.Dxf, read.Format);
        Assert.Equal(0.1, read.CurveAccuracyMillimetres);
        Assert.False(read.KeepProportions);
        Assert.True(read.LivePreview);
    }

    [Fact]
    public void TheModeAndTheFormat_AreWrittenAsWords()
    {
        new TraceDialogSettings { Trace = new TraceSettings { Mode = TraceMode.EdgeDetection }, Format = TraceFileFormat.Dxf }.Save(File1);

        var text = File.ReadAllText(File1);

        Assert.Contains("\"EdgeDetection\"", text);
        Assert.Contains("\"Dxf\"", text);
    }

    [Fact]
    public void AMissingFile_GivesTheDefaults()
    {
        var read = TraceDialogSettings.Load(File1);

        Assert.Equal(TraceMode.BrightnessCutoff, read.Trace.Mode);
        Assert.Equal(TraceDialogSettings.DefaultDetail, read.Detail);
        Assert.Equal(TraceDialogSettings.DefaultLongestSideMillimetres, read.LongestSideMillimetres);
        Assert.True(read.KeepProportions);
        Assert.False(read.LivePreview);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"Trace\": 5}")]
    [InlineData("{\"Format\": \"Gerber\"}")]
    public void AFileThatCannotBeRead_GivesTheDefaults_AndNeverThrows(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        File.WriteAllText(File1, text);

        var read = TraceDialogSettings.Load(File1);

        Assert.Equal(TraceFileFormat.Svg, read.Format);
        Assert.Equal(TraceDialogSettings.DefaultDetail, read.Detail);
    }

    [Fact]
    public void Values_OutsideTheirRange_AreBroughtInside_WhenRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        File.WriteAllText(File1, "{\"Detail\": 99999999, \"LongestSideMillimetres\": -4, \"CurveAccuracyMillimetres\": 0, \"Trace\": {\"BrightnessCutoff\": 12, \"Colors\": 1000}}");

        var read = TraceDialogSettings.Load(File1);

        Assert.Equal(TraceDialogSettings.MaxDetail, read.Detail);
        Assert.Equal(TraceDialogSettings.MinSizeMillimetres, read.LongestSideMillimetres);
        Assert.Equal(TraceDialogSettings.MinCurveAccuracy, read.CurveAccuracyMillimetres);
        Assert.Equal(1, read.Trace.BrightnessCutoff);
        Assert.Equal(TraceSettings.MaxScans, read.Trace.Colors);
    }

    [Fact]
    public void ValuesTheFileDoesNotHave_KeepTheirDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File1)!);
        File.WriteAllText(File1, "{\"Detail\": 800}");

        var read = TraceDialogSettings.Load(File1);

        Assert.Equal(800, read.Detail);
        Assert.Equal(0.45, read.Trace.BrightnessCutoff);
        Assert.Equal(TraceDialogSettings.DefaultLongestSideMillimetres, read.LongestSideMillimetres);
    }

    [Fact]
    public void SavingWhereNothingCanBeWritten_GivesFalse_NotAnError()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "blocker"), "a file where a folder is wanted");

        Assert.False(new TraceDialogSettings().Save(Path.Combine(_folder, "blocker", "trace.json")));
    }

    [Fact]
    public void Saving_LeavesNoTemporaryFileBehind()
    {
        new TraceDialogSettings().Save(File1);

        Assert.Equal(["trace.json"], Directory.GetFiles(Path.GetDirectoryName(File1)!).Select(Path.GetFileName));
    }
}
