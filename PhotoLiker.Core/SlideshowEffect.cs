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

        /// <summary>Another one of <see cref="Fade"/>, <see cref="Slide"/>, <see cref="Zoom"/> and <see cref="KenBurns"/> for every change, never the same twice in a row (not <see cref="Parallax"/>, which costs more).</summary>
        Random,
    }

    public sealed class SlideshowEffectConverter() : TolerantEnumConverter<SlideshowEffect>(SlideshowEffect.Fade);
}
