using PhotoLiker.Effects.Compositing;
using PhotoLiker.Effects.Motion;

namespace PhotoLiker.Effects.Tests.Compositing;

/// <summary>The pictures that are moved by their depth (the Parallax effect): <see cref="DepthShift"/> on a <see cref="CompositeLayer"/>.</summary>
public class FrameCompositorDepthTests
{
    private const uint Black = 0xFF000000;

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static int R(uint p) => (int)(p >> 16 & 0xFF);
    private static int G(uint p) => (int)(p >> 8 & 0xFF);
    private static int B(uint p) => (int)(p & 0xFF);

    /// <summary>A picture whose red is its column (times 4) and green is its row (times 4), so every pixel says where it is from.</summary>
    private static uint[] Ramp(int width, int height) =>
        Enumerable.Range(0, width * height).Select(i => Rgb(i % width * 4, i / width * 4, 100)).ToArray();

    private static byte[] Depth(int width, int height, Func<int, int, byte> at) =>
        Enumerable.Range(0, width * height).Select(i => at(i % width, i / width)).ToArray();

    private static CompositeLayer Deep(
        uint[] pixels, int width, int height, double centerX, double centerY, byte[] depth, int depthWidth, int depthHeight,
        double shiftX, double shiftY, double focus, double zoom = 1, int weight = 256) =>
        new(pixels, width, height, centerX, centerY, zoom, weight, new DepthShift(depth, depthWidth, depthHeight, shiftX, shiftY, focus));

    private static uint[] Compose(int width, int height, params CompositeLayer[] layers)
    {
        var screen = Enumerable.Repeat(0x12345678u, width * height).ToArray(); // whatever is there must be overwritten
        FrameCompositor.Compose(screen, width, height, layers);
        return screen;
    }

    [Fact]
    public void AnythingAtTheFocus_StaysWhereItIs_WhateverTheShift()
    {
        const int w = 20, h = 12;
        var picture = Ramp(w, h);
        var depth = Depth(5, 3, (_, _) => 255); // all of it at depth 1, which is the focus: nothing moves

        var screen = Compose(w, h, Deep(picture, w, h, w / 2.0, h / 2.0, depth, 5, 3, shiftX: 40, shiftY: -30, focus: 1));

        Assert.Equal(picture, screen);
    }

    [Fact]
    public void AllOfItAtOneDepth_MovesTogether_ByThatDepthMinusTheFocusTimesTheShift()
    {
        const int w = 30, h = 8;
        var picture = Ramp(w, h);
        var depth = Depth(6, 2, (_, _) => 255); // depth 1, focus 0: moved by the whole shift

        var screen = Compose(w, h, Deep(picture, w, h, w / 2.0, h / 2.0, depth, 6, 2, shiftX: 3, shiftY: 1, focus: 0));

        // A point of the screen shows what is 3 to the right and 1 below it in the picture (whole pixels: nothing is smoothed between them)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 3; x++)
            Assert.Equal(picture[(y + 1) * w + x + 3], screen[y * w + x]);
    }

    [Fact]
    public void WhatIsMovedPastTheEdge_TakesTheEdge_AndNothingIsOutOfRange()
    {
        const int w = 16, h = 10;
        var picture = Ramp(w, h);
        var depth = Depth(4, 3, (_, _) => 255);

        var screen = Compose(w, h, Deep(picture, w, h, w / 2.0, h / 2.0, depth, 4, 3, shiftX: 1000, shiftY: -1000, focus: 0));

        // 1000 to the right and 1000 up is the top right corner of the picture, for every point
        Assert.All(screen, pixel => Assert.Equal(picture[w - 1], pixel));
    }

    [Fact]
    public void NearAndFarThings_MoveOppositeWays_ByTheirDistanceFromTheFocus()
    {
        const int w = 40, h = 6;
        var picture = Ramp(w, h);
        var depth = Depth(40, 6, (x, _) => x < 20 ? (byte)255 : (byte)0); // the left half near, the right half far (the focus is the middle: 0.5)

        var screen = Compose(w, h, Deep(picture, w, h, w / 2.0, h / 2.0, depth, 40, 6, shiftX: 8, shiftY: 0, focus: 0.5));

        // A near point is moved by +0.5 * 8 = +4 (it shows what is 4 to its right), a far one by -4; far from the edge between them, where the depth is a slope
        for (var y = 0; y < h; y++)
        {
            Assert.InRange(R(screen[y * w + 7]), R(picture[y * w + 7 + 4]) - 3, R(picture[y * w + 7 + 4]) + 3);
            Assert.InRange(R(screen[y * w + 33]), R(picture[y * w + 33 - 4]) - 3, R(picture[y * w + 33 - 4]) + 3);
        }
    }

    [Fact]
    public void WithNothingMoved_AMagnifiedPicture_IsTheMiddleOfItAtTwiceTheSize_InTheRectItHadBefore()
    {
        const int w = 24, h = 16, screenWidth = 40, screenHeight = 30;
        var picture = Ramp(w, h);
        var depth = Depth(6, 4, (_, _) => 128);
        var centerX = screenWidth / 2.0;
        var centerY = screenHeight / 2.0;

        var deep = Compose(screenWidth, screenHeight, Deep(picture, w, h, centerX, centerY, depth, 6, 4, shiftX: 0, shiftY: 0, focus: 128 / 255.0, zoom: 2));
        var magnified = Compose(screenWidth, screenHeight, new CompositeLayer(picture, w, h, centerX, centerY, 2, 256));

        // The same pixels as the picture magnified by the compositor, but only where the picture itself was: the rest stays black
        var left = (screenWidth - w) / 2;
        var top = (screenHeight - h) / 2;
        for (var y = 0; y < screenHeight; y++)
        for (var x = 0; x < screenWidth; x++)
        {
            var inside = x >= left && x < left + w && y >= top && y < top + h;
            if (!inside)
            {
                Assert.Equal(Black, deep[y * screenWidth + x]);
                continue;
            }

            var a = deep[y * screenWidth + x];
            var b = magnified[y * screenWidth + x];
            Assert.True(Math.Abs(R(a) - R(b)) <= 2 && Math.Abs(G(a) - G(b)) <= 2 && Math.Abs(B(a) - B(b)) <= 2, $"({x},{y}): {a:X8} against {b:X8}");
        }
    }

    [Fact]
    public void ThePicture_StaysWhereItsMiddleSays_BlackAround()
    {
        const int w = 6, h = 4;
        var picture = Ramp(w, h);
        var depth = Depth(2, 2, (_, _) => 255);

        var screen = Compose(14, 10, Deep(picture, w, h, 10, 7, depth, 2, 2, shiftX: 0, shiftY: 0, focus: 1));

        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 14; x++)
        {
            var inside = x is >= 7 and < 13 && y is >= 5 and < 9;
            Assert.Equal(inside ? picture[(y - 5) * w + (x - 7)] : Black, screen[y * 14 + x]);
        }
    }

    [Fact]
    public void ALayerThatShowsHalf_IsHalfAsBright_OverBlack()
    {
        var picture = Enumerable.Repeat(Rgb(200, 100, 50), 4 * 4).ToArray();
        var depth = Depth(2, 2, (_, _) => 255);

        var screen = Compose(4, 4, Deep(picture, 4, 4, 2, 2, depth, 2, 2, shiftX: 1, shiftY: 0, focus: 0, weight: 128));

        Assert.All(screen, pixel =>
        {
            Assert.InRange(R(pixel), 99, 100);
            Assert.InRange(G(pixel), 49, 50);
            Assert.InRange(B(pixel), 24, 25);
        });
    }

    [Fact]
    public void TwoPicturesThatDissolve_AddUpToTheWholeScreen()
    {
        var one = Enumerable.Repeat(Rgb(200, 0, 0), 4 * 4).ToArray();
        var two = Enumerable.Repeat(Rgb(0, 200, 0), 4 * 4).ToArray();
        var depth = Depth(2, 2, (_, _) => 255);

        var screen = Compose(
            4, 4,
            Deep(one, 4, 4, 2, 2, depth, 2, 2, 0, 0, 1, weight: 64),
            Deep(two, 4, 4, 2, 2, depth, 2, 2, 0, 0, 1, weight: 192));

        Assert.All(screen, pixel =>
        {
            Assert.InRange(R(pixel), 49, 50);   // 200 * 64 / 256
            Assert.InRange(G(pixel), 149, 150); // 200 * 192 / 256
        });
    }

    [Fact]
    public void ADepthThatIsShorterThanItSays_LeavesThePictureAsItIs()
    {
        const int w = 8, h = 6;
        var picture = Ramp(w, h);

        var screen = Compose(w, h, Deep(picture, w, h, w / 2.0, h / 2.0, new byte[3], 4, 4, shiftX: 5, shiftY: 5, focus: 0, zoom: 1.5));

        Assert.Equal(picture, screen);
    }

    [Theory]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, double.NaN, 0)]
    [InlineData(0, 0, double.NaN)]
    public void ANumberThatIsNone_LeavesThePictureAsItIs(double shiftX, double shiftY, double focus)
    {
        const int w = 8, h = 6;
        var picture = Ramp(w, h);
        var depth = Depth(2, 2, (_, _) => 200);

        var screen = Compose(w, h, Deep(picture, w, h, w / 2.0, h / 2.0, depth, 2, 2, shiftX, shiftY, focus));

        Assert.Equal(picture, screen);
    }

    [Fact]
    public void APictureThatIsNotOnTheScreen_ShowsNothing()
    {
        var picture = Ramp(4, 4);
        var depth = Depth(2, 2, (_, _) => 255);

        var screen = Compose(6, 6, Deep(picture, 4, 4, 100, 100, depth, 2, 2, 3, 3, 0));

        Assert.All(screen, pixel => Assert.Equal(Black, pixel));
    }

    [Fact]
    public void ABigPicture_IsMovedOnAllTheRows_AsOnSeveralThreads()
    {
        // Rows are put together side by side on all the cores: every row must come out as if it were alone
        const int w = 200, h = 150;
        var picture = Ramp(w, h);
        var depth = Depth(50, 38, (x, y) => (byte)((x + y) * 3));
        var layer = Deep(picture, w, h, w / 2.0, h / 2.0, depth, 50, 38, shiftX: 9, shiftY: -4, focus: 0.4, zoom: 1.1);

        var whole = Compose(w, h, layer);
        var again = Compose(w, h, layer);

        Assert.Equal(whole, again);
        Assert.Contains(whole, pixel => pixel != Black);
    }
}
