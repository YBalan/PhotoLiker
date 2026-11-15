using PhotoLiker.Effects.Looks;

namespace PhotoLikerUI
{
    /// <summary>
    /// How the photos of the slideshow are dressed: graded (<see cref="SlideshowLook"/>: the look the show begins with, and the table of the <see cref="SlideshowLook.Lut"/> look when the person gave a
    /// <c>.cube</c> file that could be read, null when there is none: the L key then leaves that look out), put in a card (<see cref="SlideshowBorder"/>: the B key turns it on and off), and put
    /// together into pages of several photos of one event (<see cref="SlideshowCollage"/>, with <paramref name="TakenOf"/> to say when a photo was taken: null for a photo it knows nothing of).
    /// </summary>
    internal sealed record SlideshowStyleOptions(
        SlideshowLook Look, CubeLut? Lut, SlideshowBorder Border = SlideshowBorder.None, SlideshowCollage Collage = SlideshowCollage.None, Func<string, DateTime?>? TakenOf = null);
}
