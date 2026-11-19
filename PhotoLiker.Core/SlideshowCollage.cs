namespace PhotoLiker.Core
{
    /// <summary>How many photos of one event the slideshow puts on the screen at once, side by side (a collage).</summary>
    public enum SlideshowCollage
    {
        /// <summary>One photo at a time.</summary>
        None,

        /// <summary>Two photos.</summary>
        Two,

        /// <summary>Three photos: one large and two smaller.</summary>
        Three,

        /// <summary>Four photos in a grid.</summary>
        Four,

        /// <summary>Three, two and four in turn.</summary>
        Mixed,

        /// <summary>One to four photos on every page, by chance: the same pages all through the show.</summary>
        Random,
    }

    public sealed class SlideshowCollageConverter() : TolerantEnumConverter<SlideshowCollage>(SlideshowCollage.None);
}
