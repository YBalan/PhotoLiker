namespace PhotoLiker.Effects.Focus
{
    /// <summary>
    /// How a photo is refocused (see <see cref="DepthOfField"/>): where the focus is, how much what is out of focus is blurred, how deep the zone is that stays sharp, and how much haze lies over what is far.
    /// Every number is 0 to 1 (a number outside is brought to it; one that is none takes the usual value).
    /// </summary>
    /// <param name="Focus">How near the sharp things are, the depth of the map (<see cref="PhotoLiker.Effects.Depth.DepthMap"/>): 0 the farthest thing, 1 the nearest.</param>
    /// <param name="Blur">How strong the blur is where it is strongest: 0 none, 1 a disc of <see cref="DepthOfField.MaxBlurShare"/> of the long side of the photo as its radius.</param>
    /// <param name="Range">How deep the zone round the focus is that stays sharp: 0 a thin slice, 1 most of the photo.</param>
    /// <param name="Haze">How much the far parts are lost in mist: 0 none, 1 the farthest parts all mist.</param>
    public readonly record struct FocusLook(double Focus, double Blur, double Range, double Haze)
    {
        public const double UsualBlur = 0.5;
        public const double UsualRange = 0.3;
        public const double UsualHaze = 0;

        /// <summary>The look of a portrait mode photo with the focus at <paramref name="focus"/>: the usual blur, a zone of the usual depth, no haze.</summary>
        public static FocusLook Portrait(double focus) => new(focus, UsualBlur, UsualRange, UsualHaze);

        /// <summary>The look with every number brought into 0 to 1 (and the focus put in the middle when it is none).</summary>
        public FocusLook Clamped() => new(Unit(Focus, 0.5), Unit(Blur, UsualBlur), Unit(Range, UsualRange), Unit(Haze, UsualHaze));

        private static double Unit(double value, double ifNone) => double.IsNaN(value) ? ifNone : Math.Clamp(value, 0, 1);
    }
}
