using PhotoLiker.Effects.Compositing;

namespace PhotoLiker.Effects.Transitions
{
    /// <summary>
    /// A change of photo (any <see cref="SlideshowTransition"/>) between two pictures that are whole frames already: what a video drawn frame by frame needs, where the screen of the slideshow is not there to
    /// lay the pictures on. The changes that lay the pictures over each other (a dissolve, a slide, a push, a zoom) are made by <see cref="FrameCompositor"/> from the two frames as layers, just as the
    /// slideshow lays the photos on its screen; the ones that are worked out pixel by pixel (<see cref="SlideshowEffects.UsesMixer"/>) by <see cref="TransitionMixer"/>. Nothing here knows about windows.
    /// </summary>
    public static class TransitionBlender
    {
        /// <summary>
        /// Puts the change <paramref name="progress"/> of the way through (0 = <paramref name="from"/>, 1 = <paramref name="to"/>) into <paramref name="destination"/>. The three are <paramref name="width"/> by
        /// <paramref name="height"/> opaque pixels, row after row, and <paramref name="destination"/> must be none of the other two. <see cref="SlideshowTransition.None"/> shows the picture that comes at once.
        /// </summary>
        public static void Blend(uint[] destination, uint[] from, uint[] to, int width, int height, SlideshowTransition transition, double progress)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (ReferenceEquals(destination, from) || ReferenceEquals(destination, to))
                throw new ArgumentException("The picture of a change is made in a frame of its own.", nameof(destination));

            if (SlideshowEffects.UsesMixer(transition))
            {
                TransitionMixer.Mix(destination, from, to, width, height, transition, progress);
                return;
            }

            // What dissolves is the whole frame, the black round a photo too: the shares of the two add up to all. What slides lies side by side and never over the other
            var (leaving, coming) = SlideshowEffects.Transition(transition, double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1));
            var comingWeight = WeightOf(coming.Alpha);
            var leavingWeight = SlideshowEffects.IsSliding(transition) ? WeightOf(leaving.Alpha) : FrameCompositor.FullWeight - comingWeight;
            ReadOnlySpan<CompositeLayer> layers =
            [
                Layer(from, width, height, leaving, leavingWeight),
                Layer(to, width, height, coming, comingWeight),
            ];
            FrameCompositor.Compose(destination, width, height, layers);
        }

        private static int WeightOf(double alpha) => Math.Clamp((int)Math.Round(alpha * FrameCompositor.FullWeight), 0, FrameCompositor.FullWeight);

        /// <summary>Where a whole frame goes at this moment: the middle of the screen, moved by the change (a fraction of the screen, whole pixels) and magnified by it.</summary>
        private static CompositeLayer Layer(uint[] pixels, int width, int height, SlideshowLayer change, int weight) =>
            new(pixels, width, height,
                width / 2.0 + Math.Round(change.OffsetX * width),
                height / 2.0 + Math.Round(change.OffsetY * height),
                change.Scale, weight);
    }
}
