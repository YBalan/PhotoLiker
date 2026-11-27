namespace PhotoLikerUI
{
    /// <summary>
    /// The effects of the Effect box of the Generate Video dialog: the effects of the slideshow, applied to the photos of a video. An effect is how a photo moves while it is shown (the drift of Ken Burns, the turn of
    /// Tilt, the depth of Parallax, the blur of Portrait, the vertigo of the dolly zoom) and how it takes the place of the last (a dissolve, a slide, a wipe ...), as the slideshow has them.
    /// </summary>
    internal static class VideoEffects
    {
        /// <summary>The effects the box offers, in its order: none (the photos as they are), a random one, then every other effect in the order of the slideshow's Effect row.</summary>
        public static IReadOnlyList<SlideshowEffect> Choices { get; } =
        [
            SlideshowEffect.None,
            SlideshowEffect.Random,
            .. Enum.GetValues<SlideshowEffect>().Where(effect => effect is not (SlideshowEffect.None or SlideshowEffect.Random)),
        ];

        /// <summary>Whether the effect needs the depth model: the photos are given their depth first (<see cref="SlideshowEffects.UsesDepth"/>).</summary>
        public static bool NeedsDepth(SlideshowEffect effect) => SlideshowEffects.UsesDepth(effect);

        /// <summary>
        /// The effect of every picture of a video of <paramref name="count"/> pictures: the chosen one for each, or, for <see cref="SlideshowEffect.Random"/>, one of the slideshow's effects that need no model, a
        /// different one at every picture (never the same twice in a row, see <see cref="SlideshowEffects.Resolve"/>). The effect of a picture is how it moves while it is shown and how it is brought in:
        /// the first picture is not brought in, so only its movement counts. <paramref name="pick"/> gives a number from 0 up to but not including its argument.
        /// </summary>
        public static SlideshowEffect[] Plan(SlideshowEffect effect, int count, Func<int, int>? pick = null)
        {
            pick ??= Random.Shared.Next;
            var plan = new SlideshowEffect[Math.Max(0, count)];
            var previous = SlideshowEffect.None;
            for (var i = 0; i < plan.Length; i++)
            {
                plan[i] = SlideshowEffects.Resolve(effect, previous, pick);
                previous = plan[i];
            }
            return plan;
        }
    }

    /// <summary>
    /// How the photos of a video that is drawn frame by frame take each other's place: the change that goes with the effect of the photo that comes (see <see cref="SlideshowEffects.TransitionOf"/>), as long as the
    /// slideshow makes it (at most <see cref="SlideshowEffects.MaxTransitionSeconds"/>, and a share of the time of the shorter photo, so a short photo is on the screen for most of its time).
    /// </summary>
    internal sealed class EffectVideoChanges(IReadOnlyList<SlideshowEffect> effects, int width, int height) : IVideoChanges
    {
        public double Seconds(int index, double pictureSeconds)
        {
            if (index < 1 || index >= effects.Count || SlideshowEffects.TransitionOf(effects[index]) == SlideshowTransition.None)
                return 0;

            return Math.Min(SlideshowEffects.MaxTransitionSeconds, Math.Max(0, pictureSeconds) * SlideshowEffects.TransitionShareOfTimeout);
        }

        public void Mix(int index, double progress, uint[] outgoing, uint[] incoming, uint[] destination) =>
            TransitionBlender.Blend(destination, outgoing, incoming, width, height, SlideshowEffects.TransitionOf(effects[index]), progress);
    }
}
