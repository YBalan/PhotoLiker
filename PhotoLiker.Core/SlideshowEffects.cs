namespace PhotoLiker.Core
{
    /// <summary>How the slideshow changes from one photo to the next, and whether a photo moves while it is on the screen.</summary>
    public enum SlideshowEffect
    {
        /// <summary>The next photo replaces the last at once.</summary>
        None,

        /// <summary>The last photo dissolves into the next.</summary>
        Fade,

        /// <summary>The next photo pushes the last one out to the left.</summary>
        Slide,

        /// <summary>The next photo grows from a smaller size while it fades in over the last.</summary>
        Zoom,

        /// <summary>Every photo drifts and zooms slowly all the time it is on the screen (the Ken Burns effect), and dissolves into the next.</summary>
        KenBurns,

        /// <summary>Another one of <see cref="Fade"/>, <see cref="Slide"/>, <see cref="Zoom"/> and <see cref="KenBurns"/> for every change, never the same twice in a row.</summary>
        Random,
    }

    /// <summary>How one photo takes the place of the last one; what <see cref="SlideshowEffects.TransitionOf"/> makes of an effect.</summary>
    public enum SlideshowTransition
    {
        None,
        Fade,
        Slide,
        Zoom,
    }

    /// <summary>
    /// How one picture is drawn at one moment of an effect: how much of it shows (<see cref="Alpha"/>, 1 = all), how much it is magnified, and where it is
    /// moved to from the middle of the screen. What the offsets are fractions of depends on who made the layer: of the screen for
    /// <see cref="SlideshowEffects.Transition"/>, of the picture for <see cref="KenBurnsPath.At"/>.
    /// </summary>
    public readonly record struct SlideshowLayer(double Alpha, double Scale, double OffsetX, double OffsetY)
    {
        /// <summary>The picture as it is: all of it, the size it was made, in the middle.</summary>
        public static readonly SlideshowLayer Plain = new(1, 1, 0, 0);

        /// <summary>Whether this is <see cref="Plain"/>, give or take what the eye can not tell.</summary>
        public bool IsPlain =>
            Alpha >= 1 - 1e-9 && Math.Abs(Scale - 1) < 1e-9 && Math.Abs(OffsetX) < 1e-9 && Math.Abs(OffsetY) < 1e-9;
    }

    /// <summary>
    /// The slow movement of one photo for <see cref="SlideshowEffect.KenBurns"/>: it is magnified from <see cref="FromScale"/> to <see cref="ToScale"/>
    /// (one of them is 1: it either zooms in or out) around a point of the picture that stays where it is, <see cref="AnchorX"/> and <see cref="AnchorY"/>,
    /// from -1 (the left / top edge) through 0 (the middle) to 1 (the right / bottom edge). So the picture drifts as it grows, and it never shrinks
    /// below the size it was made, so the part of the screen it covered at the start is covered all the time.
    /// </summary>
    public readonly record struct KenBurnsPath(double FromScale, double ToScale, double AnchorX, double AnchorY)
    {
        /// <summary>The farthest the picture grows or shrinks: 1.08 to 1.16 times its size.</summary>
        public const double MinZoom = 0.08;
        public const double MaxZoom = 0.16;

        /// <summary>Where the picture is <paramref name="progress"/> of the way through (0 at the start, 1 at the end; outside it is the nearest of them), offsets as fractions of the picture.</summary>
        public SlideshowLayer At(double progress)
        {
            var p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
            var scale = FromScale + (ToScale - FromScale) * p;

            // The picture magnified about the anchor point has its middle moved by (1 - scale) times the distance from the anchor to the middle
            var shift = (1 - scale) / 2;
            return new SlideshowLayer(1, scale, shift * AnchorX, shift * AnchorY);
        }

        /// <summary>A path chosen at random: zooming in or out, by how much, and around which point. <paramref name="next"/> gives numbers from 0 up to but not including 1.</summary>
        public static KenBurnsPath Random(Func<double> next)
        {
            var zoom = 1 + MinZoom + (MaxZoom - MinZoom) * next();
            var zoomsIn = next() < 0.5;
            return new KenBurnsPath(zoomsIn ? 1 : zoom, zoomsIn ? zoom : 1, next() * 2 - 1, next() * 2 - 1);
        }
    }

    /// <summary>The rules of the effects of the slideshow that do not depend on a window: which of them is used, how long a change takes, how the pictures are placed at each moment.</summary>
    public static class SlideshowEffects
    {
        /// <summary>A change never takes longer than this ...</summary>
        public const double MaxTransitionSeconds = 0.8;

        /// <summary>... and never more of the time a photo stays than this share, so a short timeout still leaves the photo on the screen for most of it.</summary>
        public const double TransitionShareOfTimeout = 0.4;

        /// <summary>The size the photo of <see cref="SlideshowTransition.Zoom"/> starts at, as a share of its size.</summary>
        public const double ZoomStartScale = 0.8;

        private static readonly SlideshowEffect[] RandomChoices =
            [SlideshowEffect.Fade, SlideshowEffect.Slide, SlideshowEffect.Zoom, SlideshowEffect.KenBurns];

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
            SlideshowEffect.Fade or SlideshowEffect.KenBurns => SlideshowTransition.Fade,
            SlideshowEffect.Slide => SlideshowTransition.Slide,
            SlideshowEffect.Zoom => SlideshowTransition.Zoom,
            _ => SlideshowTransition.None,
        };

        /// <summary>Whether a photo moves while it is on the screen.</summary>
        public static bool HasMotion(SlideshowEffect effect) => effect == SlideshowEffect.KenBurns;

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
                SlideshowTransition.Fade => (new SlideshowLayer(1 - e, 1, 0, 0), new SlideshowLayer(e, 1, 0, 0)),
                SlideshowTransition.Slide => (new SlideshowLayer(1, 1, -e, 0), new SlideshowLayer(1, 1, 1 - e, 0)),
                SlideshowTransition.Zoom => (new SlideshowLayer(1 - e, 1, 0, 0), new SlideshowLayer(e, ZoomStartScale + (1 - ZoomStartScale) * e, 0, 0)),
                _ => (new SlideshowLayer(0, 1, 0, 0), SlideshowLayer.Plain),
            };
        }
    }

    public sealed class SlideshowEffectConverter() : TolerantEnumConverter<SlideshowEffect>(SlideshowEffect.Fade);
}
