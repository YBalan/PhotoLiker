namespace PhotoLiker.Effects.Transitions
{
    /// <summary>
    /// How one photo takes the place of the last one; what <see cref="SlideshowEffects.TransitionOf"/> makes of an effect. The first group is done by laying the two pictures over each other
    /// (<see cref="SlideshowEffects.Transition"/> says how each of them lies), the second by working out every pixel of the change from the two pictures as they are at that moment
    /// (<see cref="TransitionMixer"/>).
    /// </summary>
    public enum SlideshowTransition
    {
        None,

        // Pictures laid over each other
        Fade,
        Slide,
        Zoom,
        PushUp,
        PushDown,
        CrossZoom,

        // Pixel by pixel, from the two pictures
        Wipe,
        Iris,
        Pixelate,
        Ripple,
        Cube,
        BlurDissolve,
        DipBlack,
        DipWhite,
    }
}
