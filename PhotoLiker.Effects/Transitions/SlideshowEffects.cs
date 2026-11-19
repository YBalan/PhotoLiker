using PhotoLiker.Core;

namespace PhotoLiker.Effects.Transitions
{
    /// <summary>The rules of the effects of the slideshow that do not depend on a window: which of them is used, how long a change takes, how the pictures are placed at each moment.</summary>
    public static class SlideshowEffects
    {
        /// <summary>A change never takes longer than this ...</summary>
        public const double MaxTransitionSeconds = 0.8;

        /// <summary>... and never more of the time a photo stays than this share, so a short timeout still leaves the photo on the screen for most of it.</summary>
        public const double TransitionShareOfTimeout = 0.4;

        /// <summary>The size the photo of <see cref="SlideshowTransition.Zoom"/> starts at, as a share of its size.</summary>
        public const double ZoomStartScale = 0.8;

        /// <summary>The size the last photo of <see cref="SlideshowTransition.CrossZoom"/> grows to, and the one the next photo starts at, as shares of their sizes.</summary>
        public const double CrossZoomOutgoingEndScale = 1.5, CrossZoomIncomingStartScale = 0.7;

        /// <summary>What <see cref="SlideshowEffect.Random"/> chooses from: every effect that needs no model (not <see cref="SlideshowEffect.Parallax"/>, <see cref="SlideshowEffect.Portrait"/> and <see cref="SlideshowEffect.DollyZoom"/>).</summary>
        private static readonly SlideshowEffect[] RandomChoices =
        [
            SlideshowEffect.Fade, SlideshowEffect.Slide, SlideshowEffect.Zoom, SlideshowEffect.KenBurns, SlideshowEffect.Tilt,
            SlideshowEffect.PushUp, SlideshowEffect.PushDown, SlideshowEffect.CrossZoom, SlideshowEffect.Wipe, SlideshowEffect.Iris,
            SlideshowEffect.Pixelate, SlideshowEffect.Ripple, SlideshowEffect.Cube, SlideshowEffect.BlurDissolve,
            SlideshowEffect.DipToBlack, SlideshowEffect.DipToWhite,
        ];

        /// <summary>
        /// The effect to use for one change: <see cref="SlideshowEffect.Random"/> becomes one of the others, but not <paramref name="previous"/>, so it
        /// is never the same twice in a row; any other effect is itself. <paramref name="pick"/> gives a number from 0 up to but not including its argument.
        /// </summary>
        public static SlideshowEffect Resolve(SlideshowEffect effect, SlideshowEffect previous, Func<int, int> pick)
        {
            if (effect != SlideshowEffect.Random)
                return effect;

            var choices = RandomChoices.Where(choice => choice != previous).ToArray();
            return choices[Math.Clamp(pick(choices.Length), 0, choices.Length - 1)];
        }

        /// <summary>The effect after (<paramref name="direction"/> 1) or before (-1) <paramref name="effect"/>, in the order of the drop-down of the Effect row, going round at the ends: what Down and Up do in the slideshow.</summary>
        public static SlideshowEffect Next(SlideshowEffect effect, int direction)
        {
            var all = Enum.GetValues<SlideshowEffect>();
            var index = Math.Max(0, Array.IndexOf(all, effect));
            return all[Slideshow.Wrap(index + Math.Sign(direction), all.Length)];
        }

        /// <summary>How a photo takes the place of the last one with this effect (<see cref="SlideshowEffect.Random"/> must have been resolved).</summary>
        public static SlideshowTransition TransitionOf(SlideshowEffect effect) => effect switch
        {
            SlideshowEffect.Fade or SlideshowEffect.KenBurns or SlideshowEffect.Parallax or SlideshowEffect.Portrait or SlideshowEffect.Tilt or SlideshowEffect.DollyZoom => SlideshowTransition.Fade,
            SlideshowEffect.Slide => SlideshowTransition.Slide,
            SlideshowEffect.Zoom => SlideshowTransition.Zoom,
            SlideshowEffect.PushUp => SlideshowTransition.PushUp,
            SlideshowEffect.PushDown => SlideshowTransition.PushDown,
            SlideshowEffect.CrossZoom => SlideshowTransition.CrossZoom,
            SlideshowEffect.Wipe => SlideshowTransition.Wipe,
            SlideshowEffect.Iris => SlideshowTransition.Iris,
            SlideshowEffect.Pixelate => SlideshowTransition.Pixelate,
            SlideshowEffect.Ripple => SlideshowTransition.Ripple,
            SlideshowEffect.Cube => SlideshowTransition.Cube,
            SlideshowEffect.BlurDissolve => SlideshowTransition.BlurDissolve,
            SlideshowEffect.DipToBlack => SlideshowTransition.DipBlack,
            SlideshowEffect.DipToWhite => SlideshowTransition.DipWhite,
            _ => SlideshowTransition.None,
        };

        /// <summary>Whether the change is worked out pixel by pixel from the two pictures (<see cref="TransitionMixer"/>) and not by laying them over each other (<see cref="Transition"/>).</summary>
        public static bool UsesMixer(SlideshowTransition transition) =>
            transition is SlideshowTransition.Wipe or SlideshowTransition.Iris or SlideshowTransition.Pixelate or SlideshowTransition.Ripple
                or SlideshowTransition.Cube or SlideshowTransition.BlurDissolve or SlideshowTransition.DipBlack or SlideshowTransition.DipWhite;

        /// <summary>Whether the two pictures of the change lie side by side and never over each other (a slide, a push): each of them shows wholly, where the pictures of a dissolve share what shows.</summary>
        public static bool IsSliding(SlideshowTransition transition) =>
            transition is SlideshowTransition.Slide or SlideshowTransition.PushUp or SlideshowTransition.PushDown;

        /// <summary>Whether a photo moves while it is on the screen.</summary>
        public static bool HasMotion(SlideshowEffect effect) =>
            effect is SlideshowEffect.KenBurns or SlideshowEffect.Parallax or SlideshowEffect.Portrait or SlideshowEffect.Tilt or SlideshowEffect.DollyZoom;

        /// <summary>Whether a photo turns while it is on the screen (<see cref="SlideshowEffect.Tilt"/>).</summary>
        public static bool Tilts(SlideshowEffect effect) => effect == SlideshowEffect.Tilt;

        /// <summary>
        /// Whether the effect may need to know where the faces of a photo are, to keep them in view while the photo drifts (<see cref="KenBurnsPath.Aimed"/>): the Ken Burns drift (and the tilt, which is one), a random effect (which may
        /// choose it), and Parallax and the dolly zoom (a photo whose depth is not ready drifts like a Ken Burns one). The frames then have their faces worked out as they are made, not when they are first shown.
        /// </summary>
        public static bool AimsAtFaces(SlideshowEffect effect) =>
            effect is SlideshowEffect.KenBurns or SlideshowEffect.Tilt or SlideshowEffect.Parallax or SlideshowEffect.DollyZoom or SlideshowEffect.Portrait or SlideshowEffect.Random;

        /// <summary>Whether the effect needs to know how near each part of a photo is (<see cref="SlideshowEffect.Parallax"/>, <see cref="SlideshowEffect.DollyZoom"/> and <see cref="SlideshowEffect.Portrait"/>): the depth model has to be there.</summary>
        public static bool UsesDepth(SlideshowEffect effect) => MovesByDepth(effect) || BlursByDepth(effect);

        /// <summary>Whether the parts of a photo move against each other by their depth while it is shown (<see cref="SlideshowEffect.Parallax"/>, <see cref="SlideshowEffect.DollyZoom"/>).</summary>
        public static bool MovesByDepth(SlideshowEffect effect) => effect is SlideshowEffect.Parallax or SlideshowEffect.DollyZoom;

        /// <summary>Whether the parts of a photo that move by their depth do it as in a vertigo shot (<see cref="SlideshowEffect.DollyZoom"/>): about the middle, the far ones against the near ones, and not sideways.</summary>
        public static bool Dollies(SlideshowEffect effect) => effect == SlideshowEffect.DollyZoom;

        /// <summary>Whether what is not at the depth of the people is blurred (<see cref="SlideshowEffect.Portrait"/>): the photo is changed when its frame is made, and it drifts like a Ken Burns photo.</summary>
        public static bool BlursByDepth(SlideshowEffect effect) => effect == SlideshowEffect.Portrait;

        /// <summary>How long a change takes, in seconds: nothing for no effect, else not more than <see cref="MaxTransitionSeconds"/> and not more than a share of the <paramref name="timeoutSeconds"/> a photo stays.</summary>
        public static double TransitionSeconds(SlideshowEffect effect, double timeoutSeconds) =>
            TransitionOf(effect) == SlideshowTransition.None
                ? 0
                : Math.Min(MaxTransitionSeconds, Slideshow.TimeoutOf(timeoutSeconds) * TransitionShareOfTimeout);

        /// <summary>How long a photo with <see cref="HasMotion"/> moves: all the time it stays and the change that takes it off the screen, so it does not stop before it is gone.</summary>
        public static double MotionSeconds(double timeoutSeconds, double transitionSeconds) =>
            Slideshow.TimeoutOf(timeoutSeconds) + transitionSeconds;

        /// <summary>Starts slowly, ends slowly: the change is felt as one movement, not a start and a stop. 0 to 1 in, 0 to 1 out.</summary>
        public static double Ease(double t)
        {
            var x = double.IsNaN(t) ? 0 : Math.Clamp(t, 0, 1);
            return x * x * (3 - 2 * x);
        }

        /// <summary>
        /// How the photo that goes (<c>Outgoing</c>) and the one that comes (<c>Incoming</c>) are drawn <paramref name="progress"/> of the way through a change
        /// (0 = only the first, 1 = only the second). Alphas are the shares of each that show, and add up to 1 where the pictures lie over each other (a picture
        /// does not cover what it is not on: black around it is part of the screen and fades too); offsets are fractions of the screen.
        /// </summary>
        public static (SlideshowLayer Outgoing, SlideshowLayer Incoming) Transition(SlideshowTransition transition, double progress)
        {
            var e = Ease(progress);
            return transition switch
            {
                // A change that is worked out pixel by pixel (see UsesMixer) is laid out like a dissolve here: what this says is how much of each photo there is (the boxes round the faces fade by it)
                SlideshowTransition.Fade => (new SlideshowLayer(1 - e, 1, 0, 0), new SlideshowLayer(e, 1, 0, 0)),
                SlideshowTransition.Slide => (new SlideshowLayer(1, 1, -e, 0), new SlideshowLayer(1, 1, 1 - e, 0)),
                SlideshowTransition.PushUp => (new SlideshowLayer(1, 1, 0, -e), new SlideshowLayer(1, 1, 0, 1 - e)),
                SlideshowTransition.PushDown => (new SlideshowLayer(1, 1, 0, e), new SlideshowLayer(1, 1, 0, e - 1)),
                SlideshowTransition.Zoom => (new SlideshowLayer(1 - e, 1, 0, 0), new SlideshowLayer(e, ZoomStartScale + (1 - ZoomStartScale) * e, 0, 0)),
                SlideshowTransition.CrossZoom => (
                    new SlideshowLayer(1 - e, 1 + (CrossZoomOutgoingEndScale - 1) * e, 0, 0),
                    new SlideshowLayer(e, CrossZoomIncomingStartScale + (1 - CrossZoomIncomingStartScale) * e, 0, 0)),
                _ when UsesMixer(transition) => (new SlideshowLayer(1 - e, 1, 0, 0), new SlideshowLayer(e, 1, 0, 0)),
                _ => (new SlideshowLayer(0, 1, 0, 0), SlideshowLayer.Plain),
            };
        }
    }
}
