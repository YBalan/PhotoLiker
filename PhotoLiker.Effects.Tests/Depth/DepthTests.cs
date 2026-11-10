using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Tests.Depth;

/// <summary>The depth of a picture: what is done to the numbers of the model (no model is needed for most of it), and the model itself where its file is.</summary>
public class DepthTests
{
    // ── the size the model is given ─────────────────────────────────────────

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(4000, 3000)]
    [InlineData(512, 512)]
    [InlineData(100, 60)]       // smaller than the model works at: made larger
    [InlineData(10000, 100)]
    [InlineData(1, 1)]
    public void TheModelIsGivenSidesThatAreMultiplesOfFourteen_TheLongerOneAbout518(int width, int height)
    {
        var (w, h) = DepthEstimator.InputSizeFor(width, height);

        Assert.True(w >= 14 && h >= 14);
        Assert.Equal(0, w % 14);
        Assert.Equal(0, h % 14);
        if (Math.Max(width, height) >= 100 && Math.Max(width, height) <= 4 * Math.Min(width, height))
        {
            Assert.InRange(Math.Max(w, h), 504, 532);
            Assert.InRange((double)w / h, (double)width / height * 0.93, (double)width / height * 1.07 + 0.05); // the shape is kept, give or take a multiple of 14
        }
    }

    // ── the numbers of the model become bytes ───────────────────────────────

    [Fact]
    public void AllTheRange_IsUsed_NearestIs255_FarthestIs0()
    {
        var raw = Enumerable.Range(0, 1000).Select(i => i / 999f * 7.3f - 2f).ToArray(); // any scale, any offset

        var bytes = DepthEstimator.Stretch(raw);

        Assert.Equal(0, bytes[0]);
        Assert.Equal(255, bytes[^1]);
        for (var i = 1; i < bytes.Length; i++)
            Assert.True(bytes[i] >= bytes[i - 1], $"at {i}"); // order is kept
    }

    [Fact]
    public void AFewPointsFarOutside_DoNotSqueezeTheRest()
    {
        // 1000 points between 0 and 1, and two that are 1000 times farther: with the whole range used by those, the rest would be one grey
        var raw = Enumerable.Range(0, 1000).Select(i => i / 999f).Concat([1000f, -1000f]).ToArray();

        var bytes = DepthEstimator.Stretch(raw);

        var spread = bytes.Take(1000).Max() - bytes.Take(1000).Min();
        Assert.True(spread > 200, $"{spread}");
        Assert.Equal(255, bytes[1000]);
        Assert.Equal(0, bytes[1001]);
    }

    [Fact]
    public void AFlatPicture_IsAllAtTheMiddle()
    {
        Assert.All(DepthEstimator.Stretch(Enumerable.Repeat(3.5f, 50).ToArray()), b => Assert.Equal(128, b));
        Assert.All(DepthEstimator.Stretch(new float[50]), b => Assert.Equal(128, b));
    }

    [Fact]
    public void ANumberThatIsNone_IsTheMiddle_AndTheOthersAreStillStretched()
    {
        var raw = new[] { 0f, float.NaN, 10f, 5f };

        var bytes = DepthEstimator.Stretch(raw);

        Assert.Equal(0, bytes[0]);
        Assert.Equal(128, bytes[1]);
        Assert.Equal(255, bytes[2]);
        Assert.InRange(bytes[3], 100, 160);
    }

    [Fact]
    public void NoNumbersAtAll_GiveNoBytes() =>
        Assert.Empty(DepthEstimator.Stretch([]));

    // ── what the model is given ─────────────────────────────────────────────

    [Fact]
    public void AWhitePicture_IsGivenAsTheModelNormalisesWhite()
    {
        var white = Enumerable.Repeat((byte)255, 40 * 30 * 3).ToArray();

        var input = DepthEstimator.Prepare(white, 3, 40, 30, 28, 14);

        var plane = 28 * 14;
        Assert.Equal(3 * plane, input.Length);
        Assert.All(input.Take(plane), v => Assert.Equal((1 - 0.485f) / 0.229f, v, 0.001f));            // red
        Assert.All(input.Skip(plane).Take(plane), v => Assert.Equal((1 - 0.456f) / 0.224f, v, 0.001f)); // green
        Assert.All(input.Skip(2 * plane), v => Assert.Equal((1 - 0.406f) / 0.225f, v, 0.001f));          // blue
    }

    [Fact]
    public void TheColoursAreGivenInTheOrderRedGreenBlue_WhateverTheOrderOfThePixels()
    {
        // one pixel of blue, green, red as the pixels are kept (B first), with and without an alpha byte
        foreach (var bytesPerPixel in new[] { 3, 4 })
        {
            var pixels = new byte[bytesPerPixel];
            pixels[0] = 255; // blue only

            var input = DepthEstimator.Prepare(pixels, bytesPerPixel, 1, 1, 1, 1);

            Assert.True(input[0] < 0 && input[1] < 0, "no red, no green");
            Assert.True(input[2] > 2, "blue");
        }
    }

    [Fact]
    public void ALargePicture_IsAveragedNotSampled()
    {
        // 100 x 2, black and white columns; shrunk to 10 x 1 every point covers 5 black and 5 white
        var pixels = new byte[100 * 2 * 3];
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 100; x++)
            Array.Fill(pixels, (byte)(x % 2 == 0 ? 0 : 254), (y * 100 + x) * 3, 3);

        var input = DepthEstimator.Prepare(pixels, 3, 100, 2, 10, 1);

        Assert.All(input.Take(10), v => Assert.Equal((127f / 255f - 0.485f) / 0.229f, v, 0.01f));
    }

    // ── the map ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheDepthBetweenThePoints_IsSmooth_AndAtTheEdgesTheNearestPoint()
    {
        var map = new DepthMap(2, 1, [0, 255]);

        Assert.Equal(0f, map.At(0, 0.5));
        Assert.Equal(0f, map.At(0.25, 0.5), 0.001f);  // the middle of the first point
        Assert.Equal(0.5f, map.At(0.5, 0.5), 0.001f); // between
        Assert.Equal(1f, map.At(0.75, 0.5), 0.001f);
        Assert.Equal(1f, map.At(1, 0.5));
        Assert.Equal(0f, map.At(-5, 3));              // outside: the nearest edge
        Assert.Equal(1f, map.At(9, -2));
    }

    [Fact]
    public void BlurringByNothing_IsTheMapItself()
    {
        var map = new DepthMap(3, 2, [1, 2, 3, 4, 5, 6]);

        Assert.Same(map, map.Blurred(0));
        Assert.Same(map, map.Blurred(-3));
    }

    [Fact]
    public void BlurringAFlatMap_ChangesNothing()
    {
        var map = new DepthMap(20, 12, Enumerable.Repeat((byte)77, 240).ToArray());

        Assert.All(map.Blurred(3).Values, v => Assert.Equal(77, v));
    }

    [Fact]
    public void BlurringACliff_MakesItASlope_AndKeepsTheSides()
    {
        // 40 points across, 0 on the left half and 255 on the right
        var values = new byte[40 * 3];
        for (var y = 0; y < 3; y++)
        for (var x = 20; x < 40; x++)
            values[y * 40 + x] = 255;
        var map = new DepthMap(40, 3, values);

        var blurred = map.Blurred(2);

        Assert.Equal(0, blurred.Values[1 * 40 + 5]);     // far from the cliff: as it was
        Assert.Equal(255, blurred.Values[1 * 40 + 34]);
        for (var x = 18; x <= 21; x++)
            Assert.InRange(blurred.Values[1 * 40 + x], 1, 254); // at the cliff: between
        for (var x = 1; x < 40; x++)
            Assert.True(blurred.Values[1 * 40 + x] >= blurred.Values[1 * 40 + x - 1], $"the slope only goes up, at {x}");
        Assert.Equal(map.Width, blurred.Width);
        Assert.Equal(map.Height, blurred.Height);
    }

    [Fact]
    public void BlurringAMapSmallerThanTheBlur_StillWorks()
    {
        var map = new DepthMap(2, 2, [0, 100, 200, 255]);

        var blurred = map.Blurred(5);

        Assert.Equal(4, blurred.Values.Length);
        Assert.All(blurred.Values, v => Assert.InRange(v, 0, 255));
    }

    [Fact]
    public void TheDepthThatTheEstimatorGives_HasItsEdgesSoftened() =>
        Assert.Equal(2, DepthEstimator.EdgeSoftening);

    // ── the model (where its file is) ───────────────────────────────────────

    private static bool ModelPresent => ToolModels.Depth.IsPresentIn(RealModels.Folder);

    /// <summary>A picture of a near thing in front of a far wall: the middle is a bright, textured ball on a dim, smooth background. Any depth model should put the ball nearer.</summary>
    private static DecodedPhoto BallInFrontOfAWall(int width, int height)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = (x - width / 2.0) / (width * 0.22);
            var dy = (y - height / 2.0) / (height * 0.32);
            var inside = dx * dx + dy * dy < 1;
            var shade = inside ? (byte)(150 + 80 * Math.Sqrt(Math.Max(0, 1 - dx * dx - dy * dy)) + 15 * Math.Sin(x / 3.0)) : (byte)(60 + y * 30 / height);
            var i = (y * width + x) * 3;
            bgr[i] = (byte)(inside ? shade * 0.6 : shade);
            bgr[i + 1] = (byte)(inside ? shade * 0.8 : shade * 0.9);
            bgr[i + 2] = inside ? shade : (byte)(shade * 0.8);
        }
        return new DecodedPhoto(width, height, bgr);
    }

    [Fact]
    public void TheModel_GivesAMapOfTheRightShape_OfTheWholeRange()
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);

        var photo = BallInFrontOfAWall(640, 400);
        var map = hub.Depth.Estimate(photo);

        var (w, h) = DepthEstimator.InputSizeFor(640, 400);
        Assert.Equal(w, map.Width);
        Assert.Equal(h, map.Height);
        Assert.Equal(w * h, map.Values.Length);
        Assert.True(map.Values.Max() - map.Values.Min() > 150, "the whole range is used");
    }

    [Fact]
    public void TheModel_PutsTheBallNearerThanTheWall()
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);

        var map = hub.Depth.Estimate(BallInFrontOfAWall(640, 400));

        var ball = map.At(0.5, 0.5);
        var wall = (map.At(0.05, 0.1) + map.At(0.95, 0.1) + map.At(0.05, 0.9) + map.At(0.95, 0.9)) / 4;
        Assert.True(ball > wall + 0.15f, $"ball {ball:0.00}, wall {wall:0.00}");
    }

    [Fact]
    public void ThePixelsOfAScreen_GiveTheSameDepthAsThePhotoTheyAreMadeFrom()
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);
        var photo = BallInFrontOfAWall(320, 200);
        var pixels = new uint[photo.Width * photo.Height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = 0xFF000000u | (uint)(photo.Bgr[i * 3 + 2] << 16) | (uint)(photo.Bgr[i * 3 + 1] << 8) | photo.Bgr[i * 3];

        var fromPhoto = hub.Depth.Estimate(photo);
        var fromPixels = hub.Depth.Estimate(pixels, photo.Width, photo.Height);

        Assert.Equal(fromPhoto.Values, fromPixels.Values);
    }

    [Fact]
    public async Task SeveralThreadsAtOnce_AllGetTheirAnswer()
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);
        var photo = BallInFrontOfAWall(256, 160);

        var maps = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => hub.Depth.Estimate(photo))));

        Assert.All(maps, map => Assert.Equal(maps[0].Values, map.Values)); // the same picture, the same depth, on every thread
    }

    [Fact]
    public void TheDepthModelAskedForWithoutItsFile_IsAClearError_NotACrashOfTheNativeCode()
    {
        using var folder = new TempFolder();
        using var hub = new EffectModels(folder.Path);

        Assert.False(hub.DepthDownloaded);
        Assert.Throws<FileNotFoundException>(() => _ = hub.Depth);
        Assert.Throws<FileNotFoundException>(() => hub.CreateColorizer());
        Assert.False(hub.DepthDownloaded); // and nothing was made
    }
}
