using PhotoLiker.Core;
using PhotoLiker.Effects.Transitions;
using PhotoLiker.Effects.Motion;

namespace PhotoLiker.Effects.Tests.Motion;

public class ParallaxPathTests
{
    private static ParallaxPath Path(double direction = 1, double tilt = 0, double pushIn = 0.02) => new(direction, tilt, pushIn);

    [Fact]
    public void OnlyParallaxAndPortrait_NeedTheDepth_AndOnlyParallaxMovesByIt() =>
        Assert.All(Enum.GetValues<SlideshowEffect>(), effect =>
        {
            Assert.Equal(effect is SlideshowEffect.Parallax or SlideshowEffect.Portrait, SlideshowEffects.UsesDepth(effect));
            Assert.Equal(effect == SlideshowEffect.Parallax, SlideshowEffects.MovesByDepth(effect));
        });

    [Fact]
    public void TheCamera_GoesAcrossFromOneSideToTheOther_AtAnEvenSpeed()
    {
        var path = Path(direction: 1);
        var longer = 1000;

        Assert.Equal(-ParallaxPath.Amplitude * longer, path.At(0, 1000, 600).ShiftX, 9);
        Assert.Equal(0, path.At(0.5, 1000, 600).ShiftX, 9);
        Assert.Equal(ParallaxPath.Amplitude * longer, path.At(1, 1000, 600).ShiftX, 9);

        // the same distance in the same time, all along
        var quarter = path.At(0.25, 1000, 600).ShiftX - path.At(0, 1000, 600).ShiftX;
        var lastQuarter = path.At(1, 1000, 600).ShiftX - path.At(0.75, 1000, 600).ShiftX;
        Assert.Equal(quarter, lastQuarter, 9);
    }

    [Fact]
    public void ADirectionOfMinusOne_GoesTheOtherWay()
    {
        var right = Path(direction: 1).At(0.2, 800, 600);
        var left = Path(direction: -1).At(0.2, 800, 600);

        Assert.Equal(-right.ShiftX, left.ShiftX, 9);
    }

    [Fact]
    public void TheTilt_LeansTheCameraUpOrDown_WhileItGoesAcross()
    {
        var up = Path(tilt: 0.4).At(1, 1000, 600);
        var down = Path(tilt: -0.4).At(1, 1000, 600);

        Assert.Equal(0.4 * ParallaxPath.Amplitude * 1000, up.ShiftY, 9);
        Assert.Equal(-up.ShiftY, down.ShiftY, 9);
        Assert.Equal(0, Path(tilt: 0).At(1, 1000, 600).ShiftY);
    }

    [Theory]
    [InlineData(1920, 1080, 1, 0.5)]
    [InlineData(1080, 1920, -1, -0.5)]  // a portrait photo moves by its longer side, the same distance on both axes of the pixels
    [InlineData(1000, 1000, 1, 0)]
    [InlineData(4000, 300, 1, 0.3)]
    public void TheZoom_IsEnoughToKeepWhatIsBeyondTheEdgeHidden_AtTheFarthestPlaceTheCameraGoes(double width, double height, double direction, double tilt)
    {
        var path = Path(direction, tilt, pushIn: 0);

        foreach (var progress in new[] { 0.0, 0.5, 1.0 })
        {
            var view = path.At(progress, width, height);

            // The part that moves the most is the nearest or the farthest, half of the range from the focus; it is moved by half the shift, and the picture magnified
            // about its middle has (zoom - 1) / 2 of its size to spare on every side
            var mostX = Math.Max(ParallaxPath.Focus, 1 - ParallaxPath.Focus) * Math.Abs(view.ShiftX);
            var mostY = Math.Max(ParallaxPath.Focus, 1 - ParallaxPath.Focus) * Math.Abs(view.ShiftY);
            Assert.True((view.Zoom - 1) / 2 * width >= mostX - 1e-9, $"sideways at {progress}");
            Assert.True((view.Zoom - 1) / 2 * height >= mostY - 1e-9, $"up and down at {progress}");
        }
    }

    [Fact]
    public void ThePushIn_GrowsTheZoomOverTheWay()
    {
        var path = Path(pushIn: 0.03);

        Assert.Equal(0.03, path.At(1, 1600, 900).Zoom - path.At(0, 1600, 900).Zoom, 9);
        Assert.True(path.At(0.5, 1600, 900).Zoom > path.At(0, 1600, 900).Zoom);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(double.NegativeInfinity)]
    public void BeforeTheStart_TheCameraIsAtTheStart(double progress) =>
        Assert.Equal(Path().At(0, 800, 600), Path().At(progress, 800, 600));

    [Theory]
    [InlineData(7)]
    [InlineData(double.PositiveInfinity)]
    public void AfterTheEnd_TheCameraStaysAtTheEnd(double progress) =>
        Assert.Equal(Path().At(1, 800, 600), Path().At(progress, 800, 600));

    [Fact]
    public void NoNumberForTheProgress_IsTheStart() =>
        Assert.Equal(Path().At(0, 800, 600), Path().At(double.NaN, 800, 600));

    [Fact]
    public void ARandomPath_StaysInsideWhatIsAllowed()
    {
        var random = new Random(7);
        var directions = new HashSet<double>();
        for (var i = 0; i < 500; i++)
        {
            var path = ParallaxPath.Random(random.NextDouble);
            directions.Add(path.Direction);
            Assert.True(path.Direction is 1 or -1);
            Assert.InRange(path.Tilt, -0.5, 0.5);
            Assert.InRange(path.PushIn, ParallaxPath.MinPush, ParallaxPath.MaxPush);
        }

        Assert.Equal(2, directions.Count); // both ways come
    }

    [Fact]
    public void ARandomPath_FromTheExtremesOfTheNumbers_StaysInsideToo()
    {
        foreach (var number in new[] { 0.0, 0.999999 })
        {
            var path = ParallaxPath.Random(() => number);
            Assert.InRange(path.Tilt, -0.5, 0.5);
            Assert.InRange(path.PushIn, ParallaxPath.MinPush, ParallaxPath.MaxPush);
        }
    }

    [Fact]
    public void TheFocus_IsTheMiddleOfTheDepth_SoNearAndFarGoOppositeWays() =>
        Assert.Equal(0.5, ParallaxPath.Focus);
}
