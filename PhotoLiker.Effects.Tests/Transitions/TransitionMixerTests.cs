using PhotoLiker.Effects.Transitions;

namespace PhotoLiker.Effects.Tests.Transitions;

/// <summary>The changes that are worked out pixel by pixel from the two pictures (<see cref="TransitionMixer"/>).</summary>
public class TransitionMixerTests
{
    private const int W = 64, H = 36;
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF, Black = 0xFF000000, White = 0xFFFFFFFF;

    private static readonly SlideshowTransition[] Kinds =
    [
        SlideshowTransition.Wipe, SlideshowTransition.Iris, SlideshowTransition.Pixelate, SlideshowTransition.Ripple,
        SlideshowTransition.Cube, SlideshowTransition.BlurDissolve, SlideshowTransition.DipBlack, SlideshowTransition.DipWhite,
    ];

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static int R(uint p) => (int)(p >> 16 & 0xFF);
    private static int G(uint p) => (int)(p >> 8 & 0xFF);
    private static int B(uint p) => (int)(p & 0xFF);

    private static uint[] Solid(uint colour, int width = W, int height = H) => Enumerable.Repeat(colour, width * height).ToArray();

    /// <summary>A picture that says where every pixel is: red is the column (times 4), green the row (times 4).</summary>
    private static uint[] Ramp(int width = W, int height = H, int blue = 90) =>
        Enumerable.Range(0, width * height).Select(i => Rgb(i % width * 4 % 256, i / width * 4 % 256, blue)).ToArray();

    private static uint[] Mixed(uint[] from, uint[] to, SlideshowTransition kind, double progress, int width = W, int height = H)
    {
        var destination = Enumerable.Repeat(0x12345678u, width * height).ToArray(); // whatever is there must be overwritten
        TransitionMixer.Mix(destination, from, to, width, height, kind, progress);
        return destination;
    }

    // ── it begins on the first photo and ends on the second ─────────────────

    [Fact]
    public void Everyone_BeginsOnTheFirstPicture_AndEndsOnTheSecond_ExactlyWhateverThePictures()
    {
        var from = Ramp(blue: 20);
        var to = Ramp(blue: 220);
        foreach (var kind in Kinds)
        {
            Assert.Equal(from, Mixed(from, to, kind, 0));
            Assert.Equal(to, Mixed(from, to, kind, 1));
            Assert.Equal(from, Mixed(from, to, kind, -4));          // out of range is the nearest end
            Assert.Equal(to, Mixed(from, to, kind, 7));
            Assert.Equal(from, Mixed(from, to, kind, double.NaN));  // no number is the start
        }
    }

    [Fact]
    public void WhateverTheProgress_TheResultIsOpaque_AndNeverReadsOutsideThePictures()
    {
        var from = Ramp(blue: 20);
        var to = Ramp(blue: 220);
        foreach (var kind in Kinds)
        for (var step = 1; step < 20; step++)
            Assert.All(Mixed(from, to, kind, step / 20.0), pixel => Assert.Equal(0xFFu, pixel >> 24));
    }

    [Fact]
    public void ATransitionThatIsNotOneOfThese_IsADissolve()
    {
        var middle = Mixed(Solid(Black), Solid(White), SlideshowTransition.Fade, 0.5);

        Assert.All(middle, pixel => Assert.InRange(R(pixel), 120, 136)); // the middle of the way is the middle of the colours (the progress is eased: 0.5 stays 0.5)
        Assert.All(Mixed(Solid(Black), Solid(White), SlideshowTransition.None, 0.5), pixel => Assert.InRange(R(pixel), 120, 136));
    }

    [Fact]
    public void ABadBuffer_IsRefused()
    {
        var picture = Solid(Red);
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionMixer.Mix(new uint[5], picture, picture, W, H, SlideshowTransition.Wipe, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionMixer.Mix(Solid(Black), new uint[5], picture, W, H, SlideshowTransition.Wipe, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionMixer.Mix(Solid(Black), picture, new uint[5], W, H, SlideshowTransition.Wipe, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionMixer.Mix(Solid(Black), picture, picture, 0, H, SlideshowTransition.Wipe, 0.5));
        Assert.Throws<ArgumentNullException>(() => TransitionMixer.Mix(null!, picture, picture, W, H, SlideshowTransition.Wipe, 0.5));
    }

    // ── what each of them looks like ────────────────────────────────────────

    [Fact]
    public void TheWipe_ComesInFromTheLeft_SoTheLeftIsTheNextPhotoFirst()
    {
        var early = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Wipe, 0.3);
        var late = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Wipe, 0.7);

        Assert.Equal(Blue, early[H / 2 * W + 2]);        // the left already shows the next photo ...
        Assert.Equal(Red, early[H / 2 * W + W - 3]);     // ... and the right the last one
        Assert.Equal(Blue, late[H / 2 * W + W / 2]);
        Assert.Equal(Red, Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Wipe, 0.05)[H / 2 * W + W / 2]);

        // The same all the way down, and the edge is soft: a column with some of both
        Assert.Equal(early[2 * W + 20], early[(H - 3) * W + 20]);
        Assert.Contains(Enumerable.Range(0, W).Select(x => early[H / 2 * W + x]), pixel => R(pixel) > 20 && B(pixel) > 20);
    }

    [Fact]
    public void TheWipe_NeverGoesBackWards()
    {
        var last = -1;
        for (var step = 1; step < 20; step++)
        {
            var row = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Wipe, step / 20.0).Skip(H / 2 * W).Take(W);
            var blue = row.Sum(pixel => B(pixel));
            Assert.True(blue >= last, $"step {step}: {blue} < {last}");
            last = blue;
        }
    }

    [Fact]
    public void TheIris_OpensFromTheMiddle_TheCornersAreTheLastToChange()
    {
        var early = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Iris, 0.3);
        var late = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Iris, 0.85);

        Assert.Equal(Blue, early[H / 2 * W + W / 2]);
        Assert.Equal(Red, early[0]);
        Assert.Equal(Red, early[W - 1]);
        Assert.Equal(Red, early[(H - 1) * W]);
        Assert.Equal(Blue, late[H / 2 * W + W / 2]);
        Assert.NotEqual(Blue, late[0]); // the corner is the last of all

        // A circle: the same distance from the middle, the same colour, all round
        Assert.Equal(early[H / 2 * W + W / 2 + 10], early[H / 2 * W + W / 2 - 11]);
    }

    [Fact]
    public void ThePixelatedChange_IsMadeOfSquares_WhichAreLargestHalfWay()
    {
        var from = Ramp(W, H, 20);
        var to = Ramp(W, H, 220);
        var half = Mixed(from, to, SlideshowTransition.Pixelate, 0.5);
        var early = Mixed(from, to, SlideshowTransition.Pixelate, 0.1);

        int Runs(uint[] picture)
        {
            var runs = 0;
            for (var x = 1; x < W; x++)
                if (picture[x] != picture[x - 1])
                    runs++;
            return runs;
        }

        Assert.True(Runs(half) < Runs(early), "squares grow towards the middle of the change: fewer steps along a row");
        Assert.True(Runs(half) <= W / 4, $"half way the squares are wide: {Runs(half)} steps");

        // Whole squares: all the pixels of a block are one colour (the picture is a ramp, so a block of the original is not)
        var block = Enumerable.Range(0, W).Count(x => half[x] == half[0]);
        Assert.True(block > 4, $"the first square is {block} pixels wide");
    }

    [Fact]
    public void TheRipple_BendsThePicture_OnlyBetweenTheEnds_AndStaysInsideIt()
    {
        const int w = 256, h = 144; // large enough for the waves to be a few pixels high
        var from = Enumerable.Range(0, w * h).Select(i => Rgb(i % w, 0, 90)).ToArray(); // the red is the column: one step a pixel, no wrapping round
        var middle = Mixed(from, from, SlideshowTransition.Ripple, 0.5, w, h);

        Assert.NotEqual(from, middle); // the waves moved things
        Assert.Contains(Enumerable.Range(0, w * h), i => Math.Abs(R(middle[i]) - R(from[i])) >= 1);
        // What is shown is only what the picture has
        Assert.All(middle, pixel => Assert.InRange(R(pixel), 0, w - 1));
        Assert.All(middle, pixel => Assert.Equal(90, B(pixel)));
        Assert.InRange(R(middle[(h / 2) * w + w / 2]), R(from[(h / 2) * w + w / 2]) - 3, R(from[(h / 2) * w + w / 2]) + 3); // the middle is where the waves start: hardly anything moves there
    }

    [Fact]
    public void TheCube_IsAFaceThatTurnsAway_WithBlackAroundTheBox_AndTheNextFaceComingIn()
    {
        var early = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Cube, 0.35);
        var half = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.Cube, 0.5);

        // Half way both faces are seen, side by side, darker than they are, the box smaller than the screen: black in the corners
        Assert.Equal(Black, half[0]);
        Assert.Equal(Black, half[W - 1]);
        Assert.Equal(Black, half[(H - 1) * W]);
        Assert.Equal(Black, half[(H - 1) * W + W - 1]);

        var middleRow = Enumerable.Range(0, W).Select(x => half[H / 2 * W + x]).ToArray();
        Assert.Contains(middleRow, pixel => R(pixel) > 60 && B(pixel) < 10);  // the last photo's face (red, shaded)
        Assert.Contains(middleRow, pixel => B(pixel) > 60 && R(pixel) < 10);  // the next photo's (blue, shaded)
        Assert.True(middleRow.Select((pixel, x) => (pixel, x)).Last(p => R(p.pixel) > 60).x < middleRow.Select((pixel, x) => (pixel, x)).First(p => B(p.pixel) > 60).x + 2,
            "the red face is on the left of the blue one");

        // Earlier the face that is going is the larger one
        var redWidth = Enumerable.Range(0, W).Count(x => R(early[H / 2 * W + x]) > 60 && B(early[H / 2 * W + x]) < 10);
        var blueWidth = Enumerable.Range(0, W).Count(x => B(early[H / 2 * W + x]) > 60 && R(early[H / 2 * W + x]) < 10);
        Assert.True(redWidth > blueWidth, $"red {redWidth}, blue {blueWidth}");
    }

    [Fact]
    public void TheCube_OnlyUsesColoursOfTheTwoPictures_ShadedOrBlack()
    {
        var middle = Mixed(Ramp(W, H, 100), Ramp(W, H, 100), SlideshowTransition.Cube, 0.4);

        Assert.All(middle, pixel =>
        {
            Assert.Equal(0xFFu, pixel >> 24);
            Assert.True(B(pixel) == 0 || (B(pixel) is >= 40 and <= 100), $"blue {B(pixel)}: black or a shade of 100");
        });
    }

    [Fact]
    public void TheBlur_SmoothsBothPictures_MostHalfWay()
    {
        // A chessboard of 2 by 2 pixels: the sharpest there is
        var board = Enumerable.Range(0, W * H).Select(i => (i % W / 2 + i / W / 2) % 2 == 0 ? White : Black).ToArray();
        var half = Mixed(board, board, SlideshowTransition.BlurDissolve, 0.5);
        var early = Mixed(board, board, SlideshowTransition.BlurDissolve, 0.1);

        int Roughness(uint[] picture)
        {
            var sum = 0;
            for (var y = 0; y < H; y++)
            for (var x = 1; x < W; x++)
                sum += Math.Abs(R(picture[y * W + x]) - R(picture[y * W + x - 1]));
            return sum;
        }

        Assert.True(Roughness(half) < Roughness(board) / 4, $"half way it is blurred: {Roughness(half)} against {Roughness(board)}");
        Assert.True(Roughness(early) > Roughness(half), "and less so at the start");
    }

    [Fact]
    public void TheBlur_OfOnePlainColour_IsThatColour()
    {
        var grey = Solid(Rgb(120, 130, 140));
        Assert.All(Mixed(grey, grey, SlideshowTransition.BlurDissolve, 0.5), pixel => Assert.Equal(Rgb(120, 130, 140), pixel));
    }

    [Fact]
    public void TheDip_GoesToItsColourHalfWay_AndComesOutOfItOnTheNextPhoto()
    {
        Assert.All(Mixed(Solid(Red), Solid(Blue), SlideshowTransition.DipBlack, 0.5), pixel => Assert.Equal(Black, pixel));
        Assert.All(Mixed(Solid(Red), Solid(Blue), SlideshowTransition.DipWhite, 0.5), pixel => Assert.Equal(White, pixel));

        var before = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.DipBlack, 0.3)[0];
        var after = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.DipBlack, 0.7)[0];
        Assert.True(R(before) > 0 && B(before) == 0, "before half way it is the last photo (red) darkening");
        Assert.True(B(after) > 0 && R(after) == 0, "after half way it is the next photo (blue) brightening");

        var whiteBefore = Mixed(Solid(Red), Solid(Blue), SlideshowTransition.DipWhite, 0.3)[0];
        Assert.True(R(whiteBefore) == 255 && B(whiteBefore) > 0 && G(whiteBefore) > 0, "the red is turning white");
    }

    [Fact]
    public void TheDip_IsDarkestExactlyHalfWay()
    {
        long Sum(double progress) => Mixed(Solid(Red), Solid(Blue), SlideshowTransition.DipBlack, progress).Sum(pixel => (long)(R(pixel) + B(pixel)));

        Assert.True(Sum(0.5) < Sum(0.4));
        Assert.True(Sum(0.5) < Sum(0.6));
        Assert.True(Sum(0.4) < Sum(0.2));
        Assert.True(Sum(0.6) < Sum(0.8));
    }
}
