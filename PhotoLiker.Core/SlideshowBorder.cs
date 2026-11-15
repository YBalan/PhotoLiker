namespace PhotoLiker.Core
{
    /// <summary>What the photos of the slideshow are put in.</summary>
    public enum SlideshowBorder
    {
        /// <summary>Nothing: the photo fills the screen as far as its shape lets it.</summary>
        None,

        /// <summary>A white card like an instant print: the photo a little smaller on it, a wider margin below, and the date the photo was taken written in it by hand.</summary>
        Polaroid,
    }

    public sealed class SlideshowBorderConverter() : TolerantEnumConverter<SlideshowBorder>(SlideshowBorder.None);
}
