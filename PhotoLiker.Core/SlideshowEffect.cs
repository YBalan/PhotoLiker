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

        /// <summary>
        /// Every photo is a little scene in depth: a model works out how near each part of it is, and while the photo is on the screen the "camera" moves
        /// sideways, so the near parts (a face, a flower) move against the far ones (the hills, the wall) as if the photo were real space. Dissolves into the next.
        /// Needs the depth model (a download of 99 MB, asked for the first time) and some work for every photo, so <see cref="Random"/> does not pick it.
        /// </summary>
        Parallax,

        /// <summary>
        /// Every photo is taken in portrait mode: a model works out how near each part of it is, and what is at the depth of the people (of the largest face; else of the middle of the photo) stays sharp while
        /// what is nearer or farther is blurred, as a phone does it, and the photo drifts slowly with its faces in view as in <see cref="KenBurns"/>. Dissolves into the next. Needs the depth model, like
        /// <see cref="Parallax"/>, and <see cref="Random"/> does not pick it either.
        /// </summary>
        Portrait,

        /// <summary>Like <see cref="KenBurns"/> (the drift, the zoom, the faces kept in view), and the photo also turns slowly, a degree or two, while it is on the screen. Dissolves into the next.</summary>
        Tilt,

        /// <summary>
        /// The vertigo shot of the cinema: while the photo is on the screen the far parts of it grow and the near ones shrink (or the other way round) about its middle, as if the camera went in while the lens
        /// zoomed out. A model works out how near each part is, so it needs the depth model, like <see cref="Parallax"/>, and <see cref="Random"/> does not pick it either. Dissolves into the next.
        /// </summary>
        DollyZoom,

        /// <summary>The next photo pushes the last one out upwards.</summary>
        PushUp,

        /// <summary>The next photo pushes the last one out downwards.</summary>
        PushDown,

        /// <summary>Both photos fly towards the viewer: the last one grows and fades out while the next one grows from a smaller size and fades in.</summary>
        CrossZoom,

        /// <summary>The next photo is wiped on over the last one from the left, with a soft edge.</summary>
        Wipe,

        /// <summary>The next photo shows through a circle that opens from the middle of the screen until it covers it.</summary>
        Iris,

        /// <summary>The photo turns into big squares that grow, the next photo takes their place, and the squares shrink to it again.</summary>
        Pixelate,

        /// <summary>Waves run out from the middle of the screen and bend the picture while the last photo dissolves into the next.</summary>
        Ripple,

        /// <summary>The photos are two faces of a box that turns, as the cube transitions of presentation programs do.</summary>
        Cube,

        /// <summary>The last photo blurs, dissolves into the next one, which comes into focus.</summary>
        BlurDissolve,

        /// <summary>The last photo fades to black, then the next one fades in out of it.</summary>
        DipToBlack,

        /// <summary>The last photo fades to white, then the next one fades in out of it.</summary>
        DipToWhite,

        /// <summary>
        /// Another one of the effects that cost no model for every change, never the same twice in a row (not <see cref="Parallax"/>, <see cref="Portrait"/> and <see cref="DollyZoom"/>, which need the depth model
        /// and more work). It only chooses the effect: the look, the border and the collage are what they are set to, also when they are <c>None</c>.
        /// </summary>
        Random,
    }

    public sealed class SlideshowEffectConverter() : TolerantEnumConverter<SlideshowEffect>(SlideshowEffect.Fade);
}
