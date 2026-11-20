namespace PhotoLiker.Core
{
    /// <summary>What the photos of the slideshow are put in.</summary>
    public enum SlideshowBorder
    {
        /// <summary>Nothing: the photo fills the screen as far as its shape lets it.</summary>
        None,

        /// <summary>A white card like an instant print: the photo a little smaller on it, a wider margin below, and the date the photo was taken written in it by hand.</summary>
        Polaroid,

        /// <summary>A picture frame: a gilt moulding, a cream mat with a bevelled opening, and the photo behind it with a little shadow along its top and left edge.</summary>
        Frame,

        /// <summary>A length of 35 mm film: the photo between two black bands with sprocket holes, and the date it was taken stamped in orange in its corner, as a camera of the film days did.</summary>
        Film,

        /// <summary>A postage stamp: the photo on cream paper with a perforated edge, a fine line round it, and the date it was taken printed in capitals in the margin below.</summary>
        Stamp,

        /// <summary>A neon line round the photo on a dark ground, which glows in the colour the photo has most of.</summary>
        Glow,

        /// <summary>A print on a sheet of paper: the photo with rounded corners and a soft shadow that falls down and to the right.</summary>
        Shadow,

        /// <summary>Every photo has a border or not, by chance (about one photo in three has one, and which of the borders above it is is by chance too): the photo with the same file has the same in the same show.</summary>
        Random,
    }

    public sealed class SlideshowBorderConverter() : TolerantEnumConverter<SlideshowBorder>(SlideshowBorder.None);
}
