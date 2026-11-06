using PhotoLikerUI;

namespace PhotoLiker.Tests;

public class FrameCompositorTests
{
    private const uint Black = 0xFF000000;

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static int R(uint p) => (int)(p >> 16 & 0xFF);
    private static int G(uint p) => (int)(p >> 8 & 0xFF);
    private static int B(uint p) => (int)(p & 0xFF);

    private static uint[] Solid(int width, int height, uint color) => Enumerable.Repeat(color, width * height).ToArray();

    private static uint[] Noise(int width, int height, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, width * height).Select(_ => Rgb(random.Next(256), random.Next(256), random.Next(256))).ToArray();
    }

    private static CompositeLayer Layer(uint[] pixels, int width, int height, double centerX, double centerY, double scale = 1, int weight = 256) =>
        new(pixels, width, height, centerX, centerY, scale, weight);

    private static uint[] Compose(int width, int height, params CompositeLayer[] layers)
    {
        var screen = Solid(width, height, 0x12345678); // whatever is there must be overwritten
        FrameCompositor.Compose(screen, width, height, layers);
        return screen;
    }

    // ── a picture that is only moved ────────────────────────────────────────

    [Fact]
    public void APicture_IsCopiedAsItIs_WhereItsMiddleSays_BlackAround()
    {
        var picture = Enumerable.Range(0, 8).Select(i => Rgb(10 + i, 20 + i, 30 + i)).ToArray(); // 4 x 2

        var screen = Compose(8, 4, Layer(picture, 4, 2, 4, 2));

        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 8; x++)
        {
            var inside = x is >= 2 and < 6 && y is >= 1 and < 3;
            Assert.Equal(inside ? picture[(y - 1) * 4 + (x - 2)] : Black, screen[y * 8 + x]);
        }
    }

    [Fact]
    public void WithNoPicture_TheScreenIsBlack() =>
        Assert.All(Compose(5, 3), pixel => Assert.Equal(Black, pixel));

    [Fact]
    public void ThePixelsOfAPicture_AreMadeOpaque_TheyAreOverBlack()
    {
        var picture = new[] { 0x00FF8040u }; // alpha 0, red green blue as they are

        Assert.Equal(0xFFFF8040u, Compose(1, 1, Layer(picture, 1, 1, 0.5, 0.5))[0]);
        Assert.Equal(0xFFFF8040u, Compose(2, 2, Layer(Solid(2, 2, 0x00FF8040u), 2, 2, 1, 1))[3]);
    }

    [Fact]
    public void WhatFallsOutsideTheScreen_IsLeftOut()
    {
        var picture = Noise(6, 6, 1);

        // Hangs over the left and the top by 2: the screen shows the rest, from the third pixel of the picture
        var screen = Compose(5, 5, Layer(picture, 6, 6, 1, 1));

        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
        {
            var expected = x < 4 && y < 4 ? picture[(y + 2) * 6 + (x + 2)] : Black;
            Assert.Equal(expected, screen[y * 5 + x]);
        }
    }

    [Theory]
    [InlineData(-100, 2)]
    [InlineData(100, 2)]
    [InlineData(4, -100)]
    [InlineData(4, 100)]
    public void APictureWhollyOutside_ShowsNothing(double x, double y) =>
        Assert.All(Compose(8, 4, Layer(Solid(4, 2, Rgb(255, 255, 255)), 4, 2, x, y)), pixel => Assert.Equal(Black, pixel));

    [Fact]
    public void AMiddleBetweenPixels_GoesToTheNearestWholePixel()
    {
        var picture = Solid(2, 2, Rgb(200, 0, 0));

        var screen = Compose(6, 6, Layer(picture, 2, 2, 3.4, 3.4)); // 2.4 .. 4.4 -> left edge at 2

        Assert.Equal(Rgb(200, 0, 0), screen[2 * 6 + 2]);
        Assert.Equal(Rgb(200, 0, 0), screen[3 * 6 + 3]);
        Assert.Equal(Black, screen[1 * 6 + 1]);
        Assert.Equal(Black, screen[4 * 6 + 4]);
    }

    // ── how much of it shows ────────────────────────────────────────────────

    [Fact]
    public void AHalfShowingPicture_IsHalfAsBright_AndTheBlackAroundItStaysBlack()
    {
        var screen = Compose(6, 6, Layer(Solid(2, 2, Rgb(200, 100, 50)), 2, 2, 3, 3, weight: 128));

        var inside = screen[3 * 6 + 3];
        Assert.InRange(R(inside), 99, 100);
        Assert.InRange(G(inside), 49, 50);
        Assert.InRange(B(inside), 24, 25);
        Assert.Equal(0xFFu, inside >> 24);
        Assert.Equal(Black, screen[0]);
    }

    [Fact]
    public void ADissolve_IsTheMixOfBoth_AtEveryWeight()
    {
        var from = Solid(4, 4, Rgb(200, 100, 0));
        var to = Solid(4, 4, Rgb(0, 100, 240));

        for (var weight = 0; weight <= 256; weight += 32)
        {
            var screen = Compose(4, 4, Layer(from, 4, 4, 2, 2, weight: 256 - weight), Layer(to, 4, 4, 2, 2, weight: weight));

            var pixel = screen[5];
            Assert.InRange(R(pixel), 200.0 * (256 - weight) / 256 - 1.5, 200.0 * (256 - weight) / 256 + 0.5);
            Assert.InRange(G(pixel), 99, 100);
            Assert.InRange(B(pixel), 240.0 * weight / 256 - 1.5, 240.0 * weight / 256 + 0.5);
            Assert.Equal(0xFFu, pixel >> 24);
        }
    }

    [Fact]
    public void ADissolve_FullAtTheEnds()
    {
        var from = Noise(4, 4, 2);
        var to = Noise(4, 4, 3);

        Assert.Equal(from, Compose(4, 4, Layer(from, 4, 4, 2, 2, weight: 256), Layer(to, 4, 4, 2, 2, weight: 0)));
        Assert.Equal(to, Compose(4, 4, Layer(from, 4, 4, 2, 2, weight: 0), Layer(to, 4, 4, 2, 2, weight: 256)));
    }

    [Fact]
    public void ADissolveOfSmallPictures_FadesTheBlackAroundThemToo()
    {
        var white = Solid(2, 2, Rgb(255, 255, 255));
        var green = Solid(6, 6, Rgb(0, 200, 0));

        // The small white one is going, the large green one is coming, half and half: where only green is, it is half as bright
        var screen = Compose(6, 6, Layer(white, 2, 2, 3, 3, weight: 128), Layer(green, 6, 6, 3, 3, weight: 128));

        Assert.InRange(R(screen[0]), 0, 0);
        Assert.InRange(G(screen[0]), 99, 100);
        Assert.InRange(R(screen[3 * 6 + 3]), 127, 128);
        Assert.InRange(G(screen[3 * 6 + 3]), 99 + 127, 100 + 128);
    }

    [Fact]
    public void ASlide_ShowsTheEndOfOneAndTheStartOfTheOther_WithNothingBetween()
    {
        var red = Solid(8, 4, Rgb(255, 0, 0));
        var blue = Solid(8, 4, Rgb(0, 0, 255));

        // Moved 3 to the left, the other one screen after it
        var screen = Compose(8, 4, Layer(red, 8, 4, 4 - 3, 2), Layer(blue, 8, 4, 4 + 8 - 3, 2));

        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 8; x++)
            Assert.Equal(x < 5 ? Rgb(255, 0, 0) : Rgb(0, 0, 255), screen[y * 8 + x]);
    }

    // ── a picture that is magnified ─────────────────────────────────────────

    [Fact]
    public void AMagnifiedPicture_CoversTheScreenPixelsItsMiddleIsOver()
    {
        var screen = Compose(8, 8, Layer(Solid(2, 2, Rgb(0, 255, 0)), 2, 2, 4, 4, scale: 2)); // 4 x 4 pixels, from 2 to 6

        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            Assert.Equal(x is >= 2 and < 6 && y is >= 2 and < 6 ? Rgb(0, 255, 0) : Black, screen[y * 8 + x]);
    }

    [Fact]
    public void AMagnifiedSolidPicture_StaysTheSameColour_AtTheEdgesToo()
    {
        var screen = Compose(30, 20, Layer(Solid(9, 7, Rgb(11, 222, 133)), 9, 7, 15.3, 9.8, scale: 2.37));

        Assert.Contains(Rgb(11, 222, 133), screen);
        Assert.All(screen, pixel => Assert.True(pixel == Black || pixel == Rgb(11, 222, 133), $"{pixel:X8}"));
    }

    [Theory]
    [InlineData(1.7, 256)]
    [InlineData(0.6, 256)]
    [InlineData(1.13, 100)]
    [InlineData(3.0, 256)]
    public void AMagnifiedPicture_IsTheBilinearMixOfItsPixels(double scale, int weight)
    {
        const int sourceW = 17, sourceH = 13, width = 40, height = 30;
        const double cx = 20.3, cy = 15.1;
        var picture = Noise(sourceW, sourceH, 99);

        var screen = Compose(width, height, Layer(picture, sourceW, sourceH, cx, cy, scale, weight));

        var left = cx - sourceW * scale / 2;
        var top = cy - sourceH * scale / 2;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var covered = x + 0.5 >= left && x + 0.5 < left + sourceW * scale && y + 0.5 >= top && y + 0.5 < top + sourceH * scale;
            var actual = screen[y * width + x];
            if (!covered)
            {
                Assert.Equal(Black, actual);
                continue;
            }

            var sx = Math.Clamp((x + 0.5 - left) / scale - 0.5, 0, sourceW - 1);
            var sy = Math.Clamp((y + 0.5 - top) / scale - 0.5, 0, sourceH - 1);
            foreach (var (channel, name) in new (Func<uint, int>, string)[] { (R, "red"), (G, "green"), (B, "blue") })
            {
                var expected = Bilinear(picture, sourceW, sourceH, sx, sy, channel) * weight / 256.0;
                Assert.True(Math.Abs(channel(actual) - expected) <= 3, $"{name} at {x},{y}: {channel(actual)} instead of {expected:F1}");
            }
        }
    }

    private static double Bilinear(uint[] pixels, int width, int height, double x, double y, Func<uint, int> channel)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var top = channel(pixels[y0 * width + x0]) * (1 - fx) + channel(pixels[y0 * width + x1]) * fx;
        var bottom = channel(pixels[y1 * width + x0]) * (1 - fx) + channel(pixels[y1 * width + x1]) * fx;
        return top * (1 - fy) + bottom * fy;
    }

    [Fact]
    public void MagnificationOfExactlyOne_IsACopy()
    {
        var picture = Noise(7, 5, 5);

        Assert.Equal(Compose(11, 9, Layer(picture, 7, 5, 5.5, 4.5)), Compose(11, 9, Layer(picture, 7, 5, 5.5, 4.5, scale: 1 + 1e-12)));
    }

    // ── what is left out, what is refused ───────────────────────────────────

    [Fact]
    public void ALayerWithNoWeight_NoSize_OrNoNumbers_IsLeftOut()
    {
        var picture = Solid(4, 4, Rgb(255, 255, 255));
        var layers = new[]
        {
            Layer(picture, 4, 4, 2, 2, weight: 0),
            Layer(picture, 4, 4, 2, 2, scale: 0),
            Layer(picture, 4, 4, 2, 2, scale: double.NaN),
            Layer(picture, 4, 4, double.NaN, 2),
            Layer(picture, 0, 4, 2, 2),
            Layer(picture, 4, 8, 2, 2), // says it has more pixels than it has
        };

        foreach (var layer in layers)
            Assert.All(Compose(4, 4, layer), pixel => Assert.Equal(Black, pixel));
    }

    [Fact]
    public void AWeightOutOfRange_IsKeptInRange()
    {
        var picture = Solid(2, 2, Rgb(200, 200, 200));

        Assert.Equal(Rgb(200, 200, 200), Compose(2, 2, Layer(picture, 2, 2, 1, 1, weight: 5000))[0]);
        Assert.Equal(Black, Compose(2, 2, Layer(picture, 2, 2, 1, 1, weight: -5))[0]);
    }

    [Fact]
    public void AScreenTooSmallForItsSize_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameCompositor.Compose(new uint[10], 4, 4, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameCompositor.Compose(new uint[16], 0, 4, []));
    }

    [Fact]
    public void ARowsThatDoNotFillTheWorkInChunks_AreAllDone()
    {
        // 37 rows: not a multiple of the rows a thread takes at a time
        var picture = Noise(29, 37, 11);

        Assert.Equal(picture, Compose(29, 37, Layer(picture, 29, 37, 14.5, 18.5)));
    }

    [Fact]
    public void ABigScreen_GivesTheSameAsTheSmallOnesDo()
    {
        // Enough rows for all the cores; a dissolve and a magnified picture at once
        const int width = 640, height = 360;
        var from = Noise(500, 360, 21);
        var to = Noise(320, 240, 22);

        var screen = Compose(width, height,
            Layer(from, 500, 360, 300, 180, weight: 90),
            Layer(to, 320, 240, 320, 180, scale: 1.5, weight: 166));

        // The same, a row at a time, on a screen one row high shifted up: every row must be what the whole gave
        for (var y = 0; y < height; y += 37)
        {
            var row = new uint[width];
            FrameCompositor.Compose(row, width, 1, [
                Layer(from, 500, 360, 300, 180 - y, weight: 90),
                Layer(to, 320, 240, 320, 180 - y, scale: 1.5, weight: 166)]);

            Assert.Equal(screen.AsSpan(y * width, width).ToArray(), row);
        }
    }
}
