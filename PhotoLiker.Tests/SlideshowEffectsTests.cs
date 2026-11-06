using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class SlideshowEffectsTests
{
    private static readonly SlideshowEffect[] Concrete =
        [SlideshowEffect.Fade, SlideshowEffect.Slide, SlideshowEffect.Zoom, SlideshowEffect.KenBurns];

    // ── which effect ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SlideshowEffect.None)]
    [InlineData(SlideshowEffect.Fade)]
    [InlineData(SlideshowEffect.Slide)]
    [InlineData(SlideshowEffect.Zoom)]
    [InlineData(SlideshowEffect.KenBurns)]
    public void AnEffectThatIsNotRandom_IsItself(SlideshowEffect effect) =>
        Assert.Equal(effect, SlideshowEffects.Resolve(effect, SlideshowEffect.Fade, _ => 0));

    [Fact]
    public void Random_IsOneOfTheFour_NeverTheSameTwiceInARow()
    {
        foreach (var previous in Enum.GetValues<SlideshowEffect>())
        {
            var seen = new HashSet<SlideshowEffect>();
            for (var pick = 0; pick < 4; pick++)
                seen.Add(SlideshowEffects.Resolve(SlideshowEffect.Random, previous, count => Math.Min(pick, count - 1)));

            Assert.DoesNotContain(previous, seen);
            Assert.DoesNotContain(SlideshowEffect.None, seen);
            Assert.DoesNotContain(SlideshowEffect.Random, seen);
            Assert.All(seen, effect => Assert.Contains(effect, Concrete));
            Assert.Equal(previous is SlideshowEffect.None or SlideshowEffect.Random ? 4 : 3, seen.Count); // every other one can come
        }
    }

    [Fact]
    public void Random_WithANumberOutOfRange_StillGivesAnEffect()
    {
        Assert.Contains(SlideshowEffects.Resolve(SlideshowEffect.Random, SlideshowEffect.None, _ => 99), Concrete);
        Assert.Contains(SlideshowEffects.Resolve(SlideshowEffect.Random, SlideshowEffect.None, _ => -5), Concrete);
    }

    [Fact]
    public void TheNextEffect_GoesDownTheDropDown_AndRoundAtTheEnd()
    {
        var order = new[] { SlideshowEffect.None, SlideshowEffect.Fade, SlideshowEffect.Slide, SlideshowEffect.Zoom, SlideshowEffect.KenBurns, SlideshowEffect.Random };

        for (var i = 0; i < order.Length; i++)
        {
            Assert.Equal(order[(i + 1) % order.Length], SlideshowEffects.Next(order[i], 1));
            Assert.Equal(order[(i + order.Length - 1) % order.Length], SlideshowEffects.Next(order[i], -1));
        }
    }

    [Fact]
    public void TheNextEffect_GoingAroundOnceBringsYouBack()
    {
        var effect = SlideshowEffect.Zoom;
        for (var i = 0; i < Enum.GetValues<SlideshowEffect>().Length; i++)
            effect = SlideshowEffects.Next(effect, 1);

        Assert.Equal(SlideshowEffect.Zoom, effect);
    }

    [Theory]
    [InlineData(SlideshowEffect.None, SlideshowTransition.None, false)]
    [InlineData(SlideshowEffect.Fade, SlideshowTransition.Fade, false)]
    [InlineData(SlideshowEffect.Slide, SlideshowTransition.Slide, false)]
    [InlineData(SlideshowEffect.Zoom, SlideshowTransition.Zoom, false)]
    [InlineData(SlideshowEffect.KenBurns, SlideshowTransition.Fade, true)]
    public void EachEffect_HasItsTransition_AndOnlyKenBurnsMovesThePhoto(SlideshowEffect effect, SlideshowTransition transition, bool motion)
    {
        Assert.Equal(transition, SlideshowEffects.TransitionOf(effect));
        Assert.Equal(motion, SlideshowEffects.HasMotion(effect));
    }

    // ── how long ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2, 0.8)]      // the longest it takes
    [InlineData(10, 0.8)]
    [InlineData(1, 0.4)]      // 40% of the time the photo stays
    [InlineData(0.5, 0.2)]
    [InlineData(0.2, 0.08)]
    [InlineData(0.01, 0.08)]  // a timeout is never less than 0.2
    [InlineData(double.NaN, 0.8)] // no number: the default timeout, 2
    public void AChange_TakesLittleOfTheTimeThePhotoStays(double timeout, double expected) =>
        Assert.Equal(expected, SlideshowEffects.TransitionSeconds(SlideshowEffect.Fade, timeout), 9);

    [Fact]
    public void NoEffect_TakesNoTime() =>
        Assert.Equal(0, SlideshowEffects.TransitionSeconds(SlideshowEffect.None, 5));

    [Fact]
    public void ThePhotoMoves_UntilItIsGone() =>
        Assert.Equal(2.8, SlideshowEffects.MotionSeconds(2, 0.8), 9);

    // ── the movement of a change ────────────────────────────────────────────

    [Fact]
    public void Ease_StartsAndEndsSlowly_AndStaysInside()
    {
        Assert.Equal(0, SlideshowEffects.Ease(0));
        Assert.Equal(1, SlideshowEffects.Ease(1));
        Assert.Equal(0.5, SlideshowEffects.Ease(0.5), 9);
        Assert.Equal(0, SlideshowEffects.Ease(-3));
        Assert.Equal(1, SlideshowEffects.Ease(7));
        Assert.Equal(0, SlideshowEffects.Ease(double.NaN));
        Assert.True(SlideshowEffects.Ease(0.1) < 0.1);  // slower than even at the start
        Assert.True(SlideshowEffects.Ease(0.9) > 0.9);  // and at the end

        var last = 0.0;
        for (var t = 0.0; t <= 1; t += 0.01)
        {
            var e = SlideshowEffects.Ease(t);
            Assert.True(e >= last);
            last = e;
        }
    }

    [Fact]
    public void Fade_GoesFromOnePhotoToTheOther_TheSharesAlwaysAddUpToAll()
    {
        var (start, _) = SlideshowEffects.Transition(SlideshowTransition.Fade, 0);
        var (_, end) = SlideshowEffects.Transition(SlideshowTransition.Fade, 1);
        Assert.Equal(1, start.Alpha);
        Assert.Equal(1, end.Alpha);

        for (var t = 0.0; t <= 1; t += 0.05)
        {
            var (leaving, coming) = SlideshowEffects.Transition(SlideshowTransition.Fade, t);
            Assert.Equal(1, leaving.Alpha + coming.Alpha, 9);
            Assert.True(leaving.IsPlain || leaving.Alpha < 1);
            Assert.Equal(1, leaving.Scale);
            Assert.Equal(1, coming.Scale);
        }

        var (_, atStart) = SlideshowEffects.Transition(SlideshowTransition.Fade, 0);
        var (gone, _) = SlideshowEffects.Transition(SlideshowTransition.Fade, 1);
        Assert.Equal(0, atStart.Alpha);
        Assert.Equal(0, gone.Alpha);
    }

    [Fact]
    public void Slide_PushesThePhotoOut_NothingBetweenAndNothingOverlapping()
    {
        for (var t = 0.0; t <= 1; t += 0.05)
        {
            var (leaving, coming) = SlideshowEffects.Transition(SlideshowTransition.Slide, t);
            Assert.Equal(1, leaving.Alpha);
            Assert.Equal(1, coming.Alpha);
            Assert.Equal(1, coming.OffsetX - leaving.OffsetX, 9); // exactly one screen between them
            Assert.True(leaving.OffsetX <= 0);                    // out to the left
        }

        var (first, _) = SlideshowEffects.Transition(SlideshowTransition.Slide, 0);
        var (_, last) = SlideshowEffects.Transition(SlideshowTransition.Slide, 1);
        Assert.Equal(0, first.OffsetX);
        Assert.Equal(0, last.OffsetX);
    }

    [Fact]
    public void Zoom_GrowsTheNewPhotoFromASmallerSize_WhileItFadesIn()
    {
        var (_, start) = SlideshowEffects.Transition(SlideshowTransition.Zoom, 0);
        var (_, end) = SlideshowEffects.Transition(SlideshowTransition.Zoom, 1);
        var (_, middle) = SlideshowEffects.Transition(SlideshowTransition.Zoom, 0.5);

        Assert.Equal(SlideshowEffects.ZoomStartScale, start.Scale, 9);
        Assert.Equal(1, end.Scale, 9);
        Assert.InRange(middle.Scale, SlideshowEffects.ZoomStartScale, 1);
        Assert.Equal(0.5, middle.Alpha, 9);

        var (leaving, coming) = SlideshowEffects.Transition(SlideshowTransition.Zoom, 0.3);
        Assert.Equal(1, leaving.Alpha + coming.Alpha, 9);
    }

    // ── the movement of one photo ───────────────────────────────────────────

    [Fact]
    public void KenBurns_ZoomingIn_StartsAsMadeAndEndsLarger()
    {
        var path = new KenBurnsPath(1, 1.2, 0, 0);

        Assert.True(path.At(0).IsPlain);
        Assert.Equal(1.2, path.At(1).Scale, 9);
        Assert.Equal(1.1, path.At(0.5).Scale, 9);
        Assert.Equal(1.2, path.At(5).Scale, 9);  // it stays at the end
        Assert.Equal(1, path.At(-5).Scale, 9);
        Assert.Equal(1, path.At(double.NaN).Scale, 9);
    }

    [Fact]
    public void KenBurns_ZoomingOut_EndsAsMade()
    {
        var path = new KenBurnsPath(1.2, 1, 0.3, -0.7);

        Assert.Equal(1.2, path.At(0).Scale, 9);
        Assert.True(path.At(1).IsPlain);
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.0, -1.0)]
    public void KenBurns_TheAnchorEdge_StaysWhereItIs(double anchorX, double anchorY)
    {
        var path = new KenBurnsPath(1, 1.15, anchorX, anchorY);

        for (var p = 0.0; p <= 1; p += 0.1)
        {
            var layer = path.At(p);
            if (anchorX != 0)
            {
                // The edge of the picture on the anchor's side, as a fraction of the picture from its original middle: 0.5 either way
                var edge = layer.OffsetX + Math.Sign(anchorX) * layer.Scale / 2;
                Assert.Equal(Math.Sign(anchorX) * 0.5, edge, 9);
            }
            if (anchorY != 0)
            {
                var edge = layer.OffsetY + Math.Sign(anchorY) * layer.Scale / 2;
                Assert.Equal(Math.Sign(anchorY) * 0.5, edge, 9);
            }
        }
    }

    [Fact]
    public void KenBurns_NeverUncoversWhatThePhotoCoveredAtTheStart()
    {
        foreach (var anchorX in new[] { -1.0, -0.4, 0, 0.6, 1 })
        foreach (var anchorY in new[] { -1.0, 0.2, 1 })
        foreach (var (from, to) in new[] { (1.0, 1.16), (1.16, 1.0) })
        {
            var path = new KenBurnsPath(from, to, anchorX, anchorY);
            for (var p = 0.0; p <= 1; p += 0.05)
            {
                var layer = path.At(p);
                Assert.True(layer.OffsetX - layer.Scale / 2 <= -0.5 + 1e-9);
                Assert.True(layer.OffsetX + layer.Scale / 2 >= 0.5 - 1e-9);
                Assert.True(layer.OffsetY - layer.Scale / 2 <= -0.5 + 1e-9);
                Assert.True(layer.OffsetY + layer.Scale / 2 >= 0.5 - 1e-9);
            }
        }
    }

    [Fact]
    public void ARandomPath_ZoomsInOrOut_ByALittle_AroundAPointOfThePicture()
    {
        var random = new Random(1234);
        var zoomedIn = 0;
        var zoomedOut = 0;

        for (var i = 0; i < 500; i++)
        {
            var path = KenBurnsPath.Random(random.NextDouble);

            Assert.True(path.FromScale == 1 || path.ToScale == 1);
            var zoom = Math.Max(path.FromScale, path.ToScale) - 1;
            Assert.InRange(zoom, KenBurnsPath.MinZoom - 1e-9, KenBurnsPath.MaxZoom + 1e-9);
            Assert.InRange(path.AnchorX, -1, 1);
            Assert.InRange(path.AnchorY, -1, 1);

            if (path.ToScale > path.FromScale) zoomedIn++; else zoomedOut++;
        }

        Assert.InRange(zoomedIn, 150, 350); // about half each way
        Assert.InRange(zoomedOut, 150, 350);
    }

    [Fact]
    public void ARandomPath_FromTheSameNumbers_IsTheSame()
    {
        static KenBurnsPath Make() { var random = new Random(7); return KenBurnsPath.Random(random.NextDouble); }

        Assert.Equal(Make(), Make());
        Assert.Equal(new KenBurnsPath(1, 1 + KenBurnsPath.MinZoom, -1, -1), KenBurnsPath.Random(() => 0));
    }
}
