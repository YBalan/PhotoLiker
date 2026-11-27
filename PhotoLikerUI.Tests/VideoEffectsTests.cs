using PhotoLiker.Core;
using PhotoLiker.Effects.Transitions;

namespace PhotoLikerUI.Tests;

/// <summary>The effects of a video (see <see cref="VideoEffects"/>): what the box offers, which effect every photo of the video has, and how long the change that brings a photo in takes. No window, no model, no ffmpeg.</summary>
public sealed class VideoEffectsTests
{
    private static readonly SlideshowEffect[] NeedingTheModel = [SlideshowEffect.Parallax, SlideshowEffect.Portrait, SlideshowEffect.DollyZoom];

    // ── what the box offers ─────────────────────────────────────────────────

    [Fact]
    public void TheChoices_AreNone_Random_AndThenEveryOtherEffectOnce_InTheOrderOfTheSlideshow()
    {
        var choices = VideoEffects.Choices;

        Assert.Equal(SlideshowEffect.None, choices[0]);
        Assert.Equal(SlideshowEffect.Random, choices[1]);
        Assert.Equal(Enum.GetValues<SlideshowEffect>().Length, choices.Count);
        Assert.Equal(choices.Count, choices.Distinct().Count());
        Assert.Equal(Enum.GetValues<SlideshowEffect>().Where(effect => effect is not (SlideshowEffect.None or SlideshowEffect.Random)), choices.Skip(2));
    }

    [Fact]
    public void OnlyTheEffectsThatWorkOutTheDepth_NeedTheModel()
    {
        foreach (var effect in Enum.GetValues<SlideshowEffect>())
            Assert.Equal(NeedingTheModel.Contains(effect), VideoEffects.NeedsDepth(effect));
    }

    // ── the effect of every photo ───────────────────────────────────────────

    [Theory]
    [InlineData(SlideshowEffect.Fade)]
    [InlineData(SlideshowEffect.KenBurns)]
    [InlineData(SlideshowEffect.Parallax)]
    [InlineData(SlideshowEffect.Wipe)]
    [InlineData(SlideshowEffect.None)]
    public void AChosenEffect_IsTheEffectOfEveryPhoto(SlideshowEffect effect) =>
        Assert.Equal(Enumerable.Repeat(effect, 7), VideoEffects.Plan(effect, 7));

    [Fact]
    public void ARandomEffect_IsADifferentOneForEveryPhoto_NeverTheSameTwiceInARow_NeverNoneRandomOrOneThatNeedsTheModel()
    {
        var plan = VideoEffects.Plan(SlideshowEffect.Random, 500);

        Assert.Equal(500, plan.Length);
        Assert.All(plan, effect =>
        {
            Assert.NotEqual(SlideshowEffect.None, effect);
            Assert.NotEqual(SlideshowEffect.Random, effect);
            Assert.DoesNotContain(effect, NeedingTheModel); // a random video must not ask for a download of 99 MB
        });
        for (var i = 1; i < plan.Length; i++)
            Assert.NotEqual(plan[i - 1], plan[i]);
        Assert.True(plan.Distinct().Count() > 8, "many of them are used"); // 500 draws from 16 effects
    }

    [Fact]
    public void ARandomEffect_IsDecidedByTheNumbersItIsGiven()
    {
        var always0 = VideoEffects.Plan(SlideshowEffect.Random, 6, _ => 0);
        var again = VideoEffects.Plan(SlideshowEffect.Random, 6, _ => 0);

        Assert.Equal(always0, again);
        Assert.Equal(always0[0], always0[2]); // the first choice, then the one after it (the first is the last one), then the first again
        Assert.NotEqual(always0[0], always0[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NoPhotos_HaveNoEffects(int count) => Assert.Empty(VideoEffects.Plan(SlideshowEffect.Random, count));

    [Fact]
    public void APhotoThatIsOnItsOwn_HasAnEffectToo()
    {
        Assert.Single(VideoEffects.Plan(SlideshowEffect.Random, 1));
        Assert.Equal([SlideshowEffect.Tilt], VideoEffects.Plan(SlideshowEffect.Tilt, 1));
    }

    // ── the change that brings a photo in ───────────────────────────────────

    [Fact]
    public void TheChange_TakesAShareOfTheShorterPhoto_AndNeverMoreThanTheSlideshowAllows()
    {
        var changes = new EffectVideoChanges([SlideshowEffect.Fade, SlideshowEffect.Wipe, SlideshowEffect.Cube], 64, 36);

        Assert.Equal(0.4, changes.Seconds(1, 1.0), 1e-9);                 // 40 % of a photo of a second
        Assert.Equal(SlideshowEffects.MaxTransitionSeconds, changes.Seconds(1, 30.0), 1e-9); // never longer than 0.8 s
        Assert.Equal(0.04, changes.Seconds(2, 0.1), 1e-9);                // a short photo is on the screen for most of its time, below the 0.2 s the slideshow stops at too
    }

    [Fact]
    public void NoChange_BringsInThePhotoThatHasNone_TheFirstPhoto_OrOneThatIsNotThere()
    {
        var changes = new EffectVideoChanges([SlideshowEffect.Wipe, SlideshowEffect.None, SlideshowEffect.Slide], 64, 36);

        Assert.Equal(0, changes.Seconds(0, 2.0));   // the first photo is not brought in
        Assert.Equal(0, changes.Seconds(1, 2.0));   // None: a cut
        Assert.Equal(0, changes.Seconds(-1, 2.0));
        Assert.Equal(0, changes.Seconds(3, 2.0));   // not in the plan
        Assert.True(changes.Seconds(2, 2.0) > 0);
        Assert.Equal(0, changes.Seconds(2, -1.0));  // no time, no change
    }

    [Theory]
    [InlineData(SlideshowEffect.KenBurns)]
    [InlineData(SlideshowEffect.Tilt)]
    [InlineData(SlideshowEffect.Parallax)]
    [InlineData(SlideshowEffect.Portrait)]
    [InlineData(SlideshowEffect.DollyZoom)]
    public void ThePhotosThatMove_AreBroughtInByADissolve_AsInTheSlideshow(SlideshowEffect effect)
    {
        var changes = new EffectVideoChanges([SlideshowEffect.None, effect], 8, 4);
        var from = Enumerable.Repeat(0xFFFF0000u, 32).ToArray();
        var to = Enumerable.Repeat(0xFF0000FFu, 32).ToArray();
        var destination = new uint[32];

        Assert.True(changes.Seconds(1, 2.0) > 0);
        changes.Mix(1, 0.5, from, to, destination);

        Assert.All(destination, pixel =>
        {
            Assert.InRange(pixel >> 16 & 0xFF, 100u, 156u); // half of each
            Assert.InRange(pixel & 0xFF, 100u, 156u);
        });
    }

    [Fact]
    public void TheMix_IsTheChangeOfTheEffectOfThePhotoThatComes()
    {
        var changes = new EffectVideoChanges([SlideshowEffect.None, SlideshowEffect.DipToBlack, SlideshowEffect.Slide], 8, 4);
        var from = Enumerable.Repeat(0xFFFF0000u, 32).ToArray();
        var to = Enumerable.Repeat(0xFF0000FFu, 32).ToArray();

        var dip = new uint[32];
        changes.Mix(1, 0.5, from, to, dip);
        Assert.All(dip, pixel => Assert.Equal(0xFF000000u, pixel)); // the middle of a dip to black

        var slide = new uint[32];
        changes.Mix(2, 0.5, from, to, slide);
        Assert.Equal(0xFFFF0000u, slide[0]);                        // the photo that goes is on the left, the one that comes on the right
        Assert.Equal(0xFF0000FFu, slide[7]);
    }
}
