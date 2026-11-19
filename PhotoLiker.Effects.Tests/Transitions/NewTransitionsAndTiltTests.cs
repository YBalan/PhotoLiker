using PhotoLiker.Core;
using PhotoLiker.Effects.Motion;
using PhotoLiker.Effects.Transitions;

namespace PhotoLiker.Effects.Tests.Transitions;

/// <summary>The rules of the transitions that lay the pictures over each other (push up and down, cross zoom), of the ones that are worked out pixel by pixel, and of the tilt.</summary>
public class NewTransitionsAndTiltTests
{
    // ── push up and down ────────────────────────────────────────────────────

    [Fact]
    public void PushUp_TheLastPhotoGoesUp_TheNextComesFromBelow_AndNothingFades()
    {
        var (start, startIn) = SlideshowEffects.Transition(SlideshowTransition.PushUp, 0);
        var (half, halfIn) = SlideshowEffects.Transition(SlideshowTransition.PushUp, 0.5);
        var (end, endIn) = SlideshowEffects.Transition(SlideshowTransition.PushUp, 1);

        Assert.Equal(0, start.OffsetY, 9);
        Assert.Equal(1, startIn.OffsetY, 9);       // a whole screen below
        Assert.Equal(-0.5, half.OffsetY, 9);
        Assert.Equal(0.5, halfIn.OffsetY, 9);
        Assert.Equal(-1, end.OffsetY, 9);
        Assert.Equal(0, endIn.OffsetY, 9);
        Assert.All(new[] { start, startIn, half, halfIn, end, endIn }, layer =>
        {
            Assert.Equal(0, layer.OffsetX);
            Assert.Equal(1, layer.Alpha);
            Assert.Equal(1, layer.Scale);
        });
    }

    [Fact]
    public void PushDown_IsPushUpTheOtherWay()
    {
        var (up, upIn) = SlideshowEffects.Transition(SlideshowTransition.PushUp, 0.3);
        var (down, downIn) = SlideshowEffects.Transition(SlideshowTransition.PushDown, 0.3);

        Assert.Equal(-up.OffsetY, down.OffsetY, 9);
        Assert.Equal(-upIn.OffsetY, downIn.OffsetY, 9);
        Assert.Equal(0, SlideshowEffects.Transition(SlideshowTransition.PushDown, 1).Incoming.OffsetY, 9);
    }

    [Fact]
    public void ThePushesAndTheSlide_AreTheOnesWhosePicturesLieSideBySide()
    {
        Assert.All(Enum.GetValues<SlideshowTransition>(), transition =>
            Assert.Equal(transition is SlideshowTransition.Slide or SlideshowTransition.PushUp or SlideshowTransition.PushDown, SlideshowEffects.IsSliding(transition)));
    }

    // ── cross zoom ──────────────────────────────────────────────────────────

    [Fact]
    public void CrossZoom_TheLastPhotoFliesTowardsYou_AndTheNextOneComesFromFarther()
    {
        var (start, startIn) = SlideshowEffects.Transition(SlideshowTransition.CrossZoom, 0);
        var (end, endIn) = SlideshowEffects.Transition(SlideshowTransition.CrossZoom, 1);
        var (half, halfIn) = SlideshowEffects.Transition(SlideshowTransition.CrossZoom, 0.5);

        Assert.Equal(1.0, start.Scale, 9);
        Assert.Equal(1, start.Alpha, 9);
        Assert.Equal(SlideshowEffects.CrossZoomOutgoingEndScale, end.Scale, 9);
        Assert.Equal(0, end.Alpha, 9);
        Assert.Equal(SlideshowEffects.CrossZoomIncomingStartScale, startIn.Scale, 9);
        Assert.Equal(0, startIn.Alpha, 9);
        Assert.Equal(1, endIn.Scale, 9);
        Assert.Equal(1, endIn.Alpha, 9);
        Assert.Equal(1.0, half.Alpha + halfIn.Alpha, 9);   // a dissolve: the two add up to all
        Assert.True(half.Scale > 1 && halfIn.Scale < 1);
    }

    // ── the ones worked out pixel by pixel ──────────────────────────────────

    [Fact]
    public void TheMixerTransitions_AreExactlyTheOnesTheMixerDoes_AndTheirLayersAreADissolve()
    {
        var mixed = new[]
        {
            SlideshowTransition.Wipe, SlideshowTransition.Iris, SlideshowTransition.Pixelate, SlideshowTransition.Ripple,
            SlideshowTransition.Cube, SlideshowTransition.BlurDissolve, SlideshowTransition.DipBlack, SlideshowTransition.DipWhite,
        };

        foreach (var transition in Enum.GetValues<SlideshowTransition>())
            Assert.Equal(mixed.Contains(transition), SlideshowEffects.UsesMixer(transition));

        // What the boxes round the faces follow: as much of each photo as there is
        foreach (var transition in mixed)
        {
            var (leaving, coming) = SlideshowEffects.Transition(transition, 0.25);
            Assert.Equal(1, leaving.Alpha + coming.Alpha, 9);
            Assert.Equal(1, SlideshowEffects.Transition(transition, 1).Incoming.Alpha, 9);
            Assert.Equal(0, SlideshowEffects.Transition(transition, 1).Outgoing.Alpha, 9);
        }
    }

    [Fact]
    public void EveryTransitionOfEveryEffect_TakesTheSameTimeAsAFade_ButNoEffectTakesNone()
    {
        foreach (var effect in Enum.GetValues<SlideshowEffect>().Where(effect => effect is not SlideshowEffect.None and not SlideshowEffect.Random))
            Assert.Equal(0.8, SlideshowEffects.TransitionSeconds(effect, 5), 9);

        Assert.Equal(0, SlideshowEffects.TransitionSeconds(SlideshowEffect.None, 5));
    }

    // ── what is worth the graphics chip ─────────────────────────────────────

    [Fact]
    public void OnlyTheEffectsThatAreHeavyForTheProcessor_MayUseTheChip_AndRandomMayBecauseItMayChooseThem()
    {
        var heavy = new[]
        {
            SlideshowEffect.Tilt, SlideshowEffect.DollyZoom, SlideshowEffect.Parallax, SlideshowEffect.Ripple, SlideshowEffect.Cube, SlideshowEffect.BlurDissolve, SlideshowEffect.Random,
        };

        Assert.All(Enum.GetValues<SlideshowEffect>(), effect => Assert.Equal(heavy.Contains(effect), SlideshowEffects.MayUseGpu(effect)));
    }

    [Fact]
    public void TheHeavyChanges_AreRippleCubeAndBlur_AndNoOther()
    {
        Assert.All(Enum.GetValues<SlideshowTransition>(), transition =>
            Assert.Equal(transition is SlideshowTransition.Ripple or SlideshowTransition.Cube or SlideshowTransition.BlurDissolve, SlideshowEffects.IsHeavy(transition)));
    }

    // ── the tilt ────────────────────────────────────────────────────────────

    [Fact]
    public void OnlyTilt_Turns_AndEveryDriftingEffectKeepsDrifting()
    {
        Assert.All(Enum.GetValues<SlideshowEffect>(), effect => Assert.Equal(effect == SlideshowEffect.Tilt, SlideshowEffects.Tilts(effect)));
        Assert.True(SlideshowEffects.HasMotion(SlideshowEffect.Tilt));
        Assert.True(SlideshowEffects.HasMotion(SlideshowEffect.DollyZoom));
        Assert.False(SlideshowEffects.HasMotion(SlideshowEffect.Cube));
    }

    [Fact]
    public void ATiltingPath_TurnsEvenly_FromNothingToItsEnd_WhileItDrifts()
    {
        var path = new KenBurnsPath(1, 1.1, 0.2, -0.3).WithTilt(0.04);

        Assert.Equal(0, path.At(0).Rotation, 9);
        Assert.Equal(0.02, path.At(0.5).Rotation, 9);
        Assert.Equal(0.04, path.At(1).Rotation, 9);
        Assert.Equal(0.04, path.At(7).Rotation, 9);   // it never turns further than its end
        Assert.Equal(0, path.At(-2).Rotation, 9);
        Assert.Equal(1.1, path.At(1).Scale, 9);       // and the drift is the same as without the turn
        Assert.Equal(new KenBurnsPath(1, 1.1, 0.2, -0.3).At(0.4).OffsetX, path.At(0.4).OffsetX, 9);
    }

    [Fact]
    public void APathWithNoTilt_NeverTurns()
    {
        var path = new KenBurnsPath(1, 1.1, 0, 0);

        for (var step = 0; step <= 10; step++)
            Assert.Equal(0, path.At(step / 10.0).Rotation);
        Assert.True(path.At(0.5).IsPlain == false); // it drifts (the scale), it does not turn
        Assert.True(new KenBurnsPath(1, 1, 0, 0).At(0.5).IsPlain);
    }

    [Fact]
    public void ALayerThatIsTurned_IsNotPlain()
    {
        Assert.True(SlideshowLayer.Plain.IsPlain);
        Assert.False(new SlideshowLayer(1, 1, 0, 0, 0.01).IsPlain);
        Assert.True(new SlideshowLayer(1, 1, 0, 0, 1e-12).IsPlain);
    }

    [Fact]
    public void ARandomTilt_IsAFewDegrees_EitherWay()
    {
        var left = KenBurnsPath.RandomTilt(new Queue<double>([0.0, 0.0]).Dequeue);
        var right = KenBurnsPath.RandomTilt(new Queue<double>([0.999999, 0.9]).Dequeue);

        Assert.True(left < 0 && right > 0);
        Assert.Equal(KenBurnsPath.MinTiltDegrees, -left * 180 / Math.PI, 6);
        Assert.InRange(right * 180 / Math.PI, KenBurnsPath.MinTiltDegrees, KenBurnsPath.MaxTiltDegrees);
        Assert.True(right * 180 / Math.PI > 2.4);
    }

    [Fact]
    public void AnAimedPath_CanTurnToo_WithoutLosingItsAim()
    {
        var faces = new[] { new PhotoRegion(0.4, 0.4, 0.1, 0.15) };
        var aimed = KenBurnsPath.Aimed(faces, () => 0.7);
        var tilting = aimed.WithTilt(0.03);

        Assert.Equal(aimed.AnchorX, tilting.AnchorX);
        Assert.Equal(aimed.ToScale, tilting.ToScale);
        Assert.Equal(0.03, tilting.Tilt);
    }
}
