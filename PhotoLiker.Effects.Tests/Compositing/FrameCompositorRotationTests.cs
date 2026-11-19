using PhotoLiker.Effects.Compositing;
using PhotoLiker.Effects.Motion;
using PhotoLiker.Effects.Transitions;

namespace PhotoLiker.Effects.Tests.Compositing;

/// <summary>The pictures that are turned about their middle (the Tilt effect): <see cref="CompositeLayer.Rotation"/>, and the radial part of <see cref="DepthShift"/> (the dolly zoom).</summary>
public class FrameCompositorRotationTests
{
    private const uint Black = 0xFF000000, Red = 0xFFFF0000, Blue = 0xFF0000FF, White = 0xFFFFFFFF;

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static int R(uint p) => (int)(p >> 16 & 0xFF);
    private static int B(uint p) => (int)(p & 0xFF);

    private static uint[] Compose(int width, int height, params CompositeLayer[] layers)
    {
        var screen = Enumerable.Repeat(0x12345678u, width * height).ToArray(); // whatever is there must be overwritten
        FrameCompositor.Compose(screen, width, height, layers);
        return screen;
    }

    private static uint[] Ramp(int width, int height) =>
        Enumerable.Range(0, width * height).Select(i => Rgb(i % width * 4, i / width * 4, 100)).ToArray();

    /// <summary>40 by 20: red on the left half, blue on the right.</summary>
    private static uint[] TwoHalves() =>
        Enumerable.Range(0, 40 * 20).Select(i => i % 40 < 20 ? Red : Blue).ToArray();

    [Fact]
    public void ARotationOfNothing_IsThePictureAsItIs()
    {
        var picture = Ramp(20, 10);

        Assert.Equal(Compose(30, 20, new CompositeLayer(picture, 20, 10, 15, 10, 1, 256)),
                     Compose(30, 20, new CompositeLayer(picture, 20, 10, 15, 10, 1, 256, null, 0)));
        Assert.Equal(Compose(30, 20, new CompositeLayer(picture, 20, 10, 15, 10, 1, 256)),
                     Compose(30, 20, new CompositeLayer(picture, 20, 10, 15, 10, 1, 256, null, 1e-12)));
        Assert.Equal(Compose(30, 20, new CompositeLayer(picture, 20, 10, 15, 10, 1, 256)),
                     Compose(30, 20, new CompositeLayer(picture, 20, 10, 15, 10, 1, 256, null, double.NaN))); // no number: not turned
    }

    [Fact]
    public void AQuarterTurnClockwise_PutsTheLeftOfThePictureAtTheTop()
    {
        // 40 by 20, turned about the middle of a 40 by 40 screen: it is then 20 wide and 40 high, the left half (red) above the right half (blue)
        var screen = Compose(40, 40, new CompositeLayer(TwoHalves(), 40, 20, 20, 20, 1, 256, null, Math.PI / 2));

        Assert.Equal(Red, screen[5 * 40 + 20]);
        Assert.Equal(Blue, screen[35 * 40 + 20]);
        Assert.Equal(Black, screen[20 * 40 + 2]);   // beside it is black
        Assert.Equal(Black, screen[20 * 40 + 37]);
    }

    [Fact]
    public void AQuarterTurnTheOtherWay_PutsTheLeftOfThePictureAtTheBottom()
    {
        var screen = Compose(40, 40, new CompositeLayer(TwoHalves(), 40, 20, 20, 20, 1, 256, null, -Math.PI / 2));

        Assert.Equal(Blue, screen[5 * 40 + 20]);
        Assert.Equal(Red, screen[35 * 40 + 20]);
    }

    [Fact]
    public void AHalfTurn_IsThePictureUpsideDown()
    {
        var screen = Compose(40, 20, new CompositeLayer(TwoHalves(), 40, 20, 20, 10, 1, 256, null, Math.PI));

        Assert.Equal(Blue, screen[10 * 40 + 5]);   // blue is on the left now
        Assert.Equal(Red, screen[10 * 40 + 35]);
    }

    [Fact]
    public void ASmallTurn_HasASmoothEdge_NotAStaircase()
    {
        var white = Enumerable.Repeat(White, 60 * 40).ToArray();
        var screen = Compose(100, 80, new CompositeLayer(white, 60, 40, 50, 40, 1, 256, null, 0.08));

        Assert.Equal(White, screen[40 * 100 + 50]);  // the middle is all there
        Assert.Equal(Black, screen[0]);               // the corner of the screen is not
        // Along the edge there are pixels that are partly there: neither black nor white
        Assert.Contains(screen, pixel => R(pixel) is > 20 and < 235);
        Assert.All(screen, pixel => Assert.Equal(0xFFu, pixel >> 24));
    }

    [Fact]
    public void ATurnedPicture_WithHalfTheWeight_IsHalfAsBright()
    {
        var white = Enumerable.Repeat(White, 60 * 40).ToArray();
        var screen = Compose(100, 80, new CompositeLayer(white, 60, 40, 50, 40, 1, 128, null, 0.5));

        Assert.InRange(R(screen[40 * 100 + 50]), 120, 136);
    }

    [Fact]
    public void ATurnedPicture_IsMagnifiedAboutItsMiddle_Too()
    {
        // A picture of 2 by 2 pixels, magnified 10 times and turned: its middle stays where it is
        var picture = new[] { Red, Red, Blue, Blue };
        var screen = Compose(40, 40, new CompositeLayer(picture, 2, 2, 20, 20, 10, 256, null, 0.3));

        Assert.NotEqual(Black, screen[20 * 40 + 20]);
        Assert.Equal(Black, screen[0]);
        Assert.Equal(Black, screen[39 * 40 + 39]);
    }

    [Fact]
    public void ATurnedPicture_OutsideTheScreen_IsLeftOut()
    {
        var screen = Compose(20, 20, new CompositeLayer(TwoHalves(), 40, 20, 500, 500, 1, 256, null, 0.4));
        Assert.All(screen, pixel => Assert.Equal(Black, pixel));
    }

    [Fact]
    public void ADissolve_OfTwoTurnedPictures_AddsUpToTheWhole()
    {
        var white = Enumerable.Repeat(White, 60 * 40).ToArray();
        var screen = Compose(60, 40,
            new CompositeLayer(white, 60, 40, 30, 20, 1.3, 100, null, 0.1),
            new CompositeLayer(white, 60, 40, 30, 20, 1.3, 156, null, -0.1));

        Assert.InRange(R(screen[20 * 60 + 30]), 250, 255);
    }

    // ── the dolly zoom: the radial part of the depth shift ─────────────────

    private static byte[] Depth(int width, int height, byte value) => Enumerable.Repeat(value, width * height).ToArray();

    private static CompositeLayer Dolly(uint[] picture, int width, int height, byte depth, double radial, double zoom = 1) =>
        new(picture, width, height, width / 2.0, height / 2.0, zoom, 256, new DepthShift(Depth(8, 4, depth), 8, 4, 0, 0, 0.5, radial));

    [Fact]
    public void WithNoRadialPart_TheDepthDoesNothing_WhenTheShiftIsNothing()
    {
        var picture = Ramp(40, 20);
        Assert.Equal(picture, Compose(40, 20, Dolly(picture, 40, 20, 0, 0)));
    }

    [Fact]
    public void AFarPart_IsMagnifiedAboutTheMiddle_WhenTheRadialPartIsPositive()
    {
        var picture = Ramp(40, 20);
        var screen = Compose(40, 20, Dolly(picture, 40, 20, 0, 0.5)); // depth 0 against a focus of 0.5: taken from 0.75 times as far from the middle

        // Right of the middle a point shows what is nearer the middle in the picture (so smaller red), left of it what is nearer the middle (so larger red)
        Assert.True(R(screen[10 * 40 + 36]) < R(picture[10 * 40 + 36]) - 8);
        Assert.True(R(screen[10 * 40 + 3]) > R(picture[10 * 40 + 3]) + 8);
        Assert.InRange(R(screen[10 * 40 + 20]), R(picture[10 * 40 + 20]) - 4, R(picture[10 * 40 + 20]) + 4); // the middle stays

        // The same up and down: the green of the ramp is the row
        Assert.True((screen[19 * 40 + 20] >> 8 & 0xFF) < (picture[19 * 40 + 20] >> 8 & 0xFF) - 5);
    }

    [Fact]
    public void ANearPart_IsShrunk_WhenTheRadialPartIsPositive_AndTheOtherWayRound_WhenItIsNegative()
    {
        var picture = Ramp(40, 20);
        var near = Compose(40, 20, Dolly(picture, 40, 20, 255, 0.5));
        var negative = Compose(40, 20, Dolly(picture, 40, 20, 0, -0.5));

        Assert.True(R(near[10 * 40 + 36]) > R(picture[10 * 40 + 36]), "a near part right of the middle shows what is farther from it");
        Assert.True(R(negative[10 * 40 + 36]) > R(picture[10 * 40 + 36]), "so does a far one when the radial part is negative");
    }

    [Fact]
    public void AnythingAtTheFocus_DoesNotChangeSize_WhateverTheRadialPart()
    {
        var picture = Ramp(40, 20);
        var screen = Compose(40, 20, Dolly(picture, 40, 20, 128, 0.9)); // depth 128 / 255 is the focus, 0.5, give or take a half step of the byte

        for (var x = 0; x < 40; x++)
            Assert.InRange(R(screen[10 * 40 + x]), R(picture[10 * 40 + x]) - 2, R(picture[10 * 40 + x]) + 2);
    }
}
