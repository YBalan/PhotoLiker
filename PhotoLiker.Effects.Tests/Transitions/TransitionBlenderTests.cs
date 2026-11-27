using PhotoLiker.Effects.Transitions;

namespace PhotoLiker.Effects.Tests.Transitions;

/// <summary>A change of photo between two whole frames (what a video that is drawn frame by frame needs): every change of the slideshow, made of the two frames and nothing else.</summary>
public class TransitionBlenderTests
{
    private const int W = 64, H = 36;
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    /// <summary>Every change there is, but none (which is no change: the picture that comes is there at once).</summary>
    public static IEnumerable<object[]> Kinds() =>
        Enum.GetValues<SlideshowTransition>().Where(kind => kind != SlideshowTransition.None).Select(kind => new object[] { kind });

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
    private static int R(uint p) => (int)(p >> 16 & 0xFF);
    private static int B(uint p) => (int)(p & 0xFF);

    private static uint[] Solid(uint colour) => Enumerable.Repeat(colour, W * H).ToArray();

    /// <summary>A picture that says where every pixel is: red is the column, green the row, blue is given.</summary>
    private static uint[] Ramp(int blue) => Enumerable.Range(0, W * H).Select(i => Rgb(i % W * 4 % 256, i / W * 4 % 256, blue)).ToArray();

    private static uint[] Blended(uint[] from, uint[] to, SlideshowTransition kind, double progress)
    {
        var destination = Enumerable.Repeat(0x12345678u, W * H).ToArray(); // whatever is there must be overwritten
        TransitionBlender.Blend(destination, from, to, W, H, kind, progress);
        return destination;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryChange_BeginsOnTheFirstPicture_AndEndsOnTheSecond_ExactlyWhateverThePictures(SlideshowTransition kind)
    {
        var from = Ramp(blue: 20);
        var to = Ramp(blue: 220);

        Assert.Equal(from, Blended(from, to, kind, 0));
        Assert.Equal(to, Blended(from, to, kind, 1));
        Assert.Equal(from, Blended(from, to, kind, -3));         // out of range is the nearest end
        Assert.Equal(to, Blended(from, to, kind, 9));
        Assert.Equal(from, Blended(from, to, kind, double.NaN)); // no number is the start
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryChange_IsNeitherPictureInTheMiddleOfTheWay_AndIsAlwaysOpaque(SlideshowTransition kind)
    {
        var from = Solid(Red);
        var to = Solid(Blue);

        for (var step = 1; step < 10; step++)
            Assert.All(Blended(from, to, kind, step / 10.0), pixel => Assert.Equal(0xFFu, pixel >> 24));

        var middle = Blended(from, to, kind, 0.5);
        Assert.NotEqual(from, middle);
        Assert.NotEqual(to, middle);
    }

    [Fact]
    public void ADissolve_IsTheMiddleOfTheColours_AtTheMiddleOfTheWay_AndItGoesFromOneToTheOther()
    {
        var middle = Blended(Solid(Red), Solid(Blue), SlideshowTransition.Fade, 0.5);
        Assert.All(middle, pixel =>
        {
            Assert.InRange(R(pixel), 120, 136);
            Assert.InRange(B(pixel), 120, 136);
        });

        var early = Blended(Solid(Red), Solid(Blue), SlideshowTransition.Fade, 0.2);
        var late = Blended(Solid(Red), Solid(Blue), SlideshowTransition.Fade, 0.8);
        Assert.True(R(early[0]) > R(middle[0]) && R(middle[0]) > R(late[0]), "the red goes");
        Assert.True(B(early[0]) < B(middle[0]) && B(middle[0]) < B(late[0]), "the blue comes");
    }

    [Fact]
    public void ASlide_HasTheTwoPicturesSideBySide_NeitherOverTheOther()
    {
        var middle = Blended(Solid(Red), Solid(Blue), SlideshowTransition.Slide, 0.5);

        // Half of the first picture has gone out on the left, and the second has come in as far: each of them shows as it is, whole colours
        Assert.Equal(Red, middle[10]);
        Assert.Equal(Blue, middle[W - 10]);
        Assert.Equal(Red, middle[(H - 1) * W + 10]);
    }

    [Fact]
    public void APush_GoesUpAndDown()
    {
        var up = Blended(Solid(Red), Solid(Blue), SlideshowTransition.PushUp, 0.5);
        Assert.Equal(Red, up[3 * W]);                  // the top is the picture that goes
        Assert.Equal(Blue, up[(H - 3) * W]);           // the bottom is the one that comes

        var down = Blended(Solid(Red), Solid(Blue), SlideshowTransition.PushDown, 0.5);
        Assert.Equal(Blue, down[3 * W]);
        Assert.Equal(Red, down[(H - 3) * W]);
    }

    [Fact]
    public void ADipToBlack_GoesDarkBeforeTheNextPictureComes_AndADipToWhiteGoesLight()
    {
        var darkening = Blended(Solid(Red), Solid(Blue), SlideshowTransition.DipBlack, 0.3);
        Assert.InRange(R(darkening[0]), 1, 254);
        Assert.Equal(0, B(darkening[0]));              // it is still the first picture, darker

        var darkest = Blended(Solid(Red), Solid(Blue), SlideshowTransition.DipBlack, 0.5);
        Assert.All(darkest, pixel => Assert.Equal(0xFF000000u, pixel));

        var lightening = Blended(Solid(Red), Solid(Blue), SlideshowTransition.DipWhite, 0.5);
        Assert.All(lightening, pixel => Assert.Equal(0xFFFFFFFFu, pixel));
    }

    [Fact]
    public void WithNoChange_ThePictureThatComesIsThereAtOnce()
    {
        var from = Solid(Red);
        var to = Solid(Blue);

        Assert.Equal(to, Blended(from, to, SlideshowTransition.None, 0.3));
    }

    [Fact]
    public void TheFrameOfTheChange_CanNotBeOneOfThePictures()
    {
        var from = Solid(Red);
        var to = Solid(Blue);

        Assert.Throws<ArgumentException>(() => TransitionBlender.Blend(from, from, to, W, H, SlideshowTransition.Fade, 0.5));
        Assert.Throws<ArgumentException>(() => TransitionBlender.Blend(to, from, to, W, H, SlideshowTransition.Wipe, 0.5));
    }

    [Fact]
    public void ASizeOfNothing_IsRefused()
    {
        var frame = Solid(Red);

        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionBlender.Blend(new uint[W * H], frame, frame, 0, H, SlideshowTransition.Fade, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionBlender.Blend(new uint[W * H], frame, frame, W, 0, SlideshowTransition.Fade, 0.5));
    }
}
