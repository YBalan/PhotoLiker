namespace PhotoLiker.Effects.Transitions
{
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
}
