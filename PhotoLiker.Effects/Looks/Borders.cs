using PhotoLiker.Core;

namespace PhotoLiker.Effects.Looks
{
    /// <summary>How the date of a photo is written on a border (the writing itself is the window's; see <see cref="BorderDate"/>).</summary>
    public enum DateInk
    {
        /// <summary>By hand, dark ink, in the middle of the margin: a Polaroid.</summary>
        Handwriting,

        /// <summary>The orange digits that a camera of the film days printed in the corner of the photo (<c>'14 8 9</c>), flush right.</summary>
        FilmStamp,

        /// <summary>Printed in capitals, in a serif face, dark red-brown, in the middle of the margin: a postage stamp (<c>9 AUG 2014</c>).</summary>
        Capitals,
    }

    /// <summary>Where the date of a photo goes on a border, as shares of the picture (left, top, width, height), and how it is written there.</summary>
    public readonly record struct BorderDate(float X, float Y, float Width, float Height, DateInk Ink);

    /// <summary>What a border does to a picture: where the photo is in it (as shares of its width and height: what the faces and the drift of the photo are worked out in), and where the date goes, if the border has one.</summary>
    public readonly record struct BorderLayout(float X, float Y, float Width, float Height, BorderDate? Date);

    /// <summary>
    /// The borders of the slideshow (see <see cref="SlideshowBorder"/>), in one place: which there are (<see cref="Styles"/>), where the photo and the date are on each (<see cref="LayoutOf"/>), how a photo is put on one
    /// (<see cref="Apply"/>), and which comes after which when the B key goes through them (<see cref="Next"/>). Every border is made on the pixels of a picture that is already the size of the frame; none knows about windows.
    /// </summary>
    public static class Borders
    {
        /// <summary>The borders that are a border (not <see cref="SlideshowBorder.None"/> and not <see cref="SlideshowBorder.Random"/>), in the order the B key goes through them.</summary>
        public static IReadOnlyList<SlideshowBorder> Styles { get; } =
            [SlideshowBorder.Polaroid, SlideshowBorder.Frame, SlideshowBorder.Film, SlideshowBorder.Stamp, SlideshowBorder.Glow, SlideshowBorder.Shadow];

        /// <summary>Whether this is a border that puts the photo in something (one of <see cref="Styles"/>).</summary>
        public static bool IsStyle(SlideshowBorder border) => Styles.Contains(border);

        /// <summary>Where the photo and the date are on a border; null for <see cref="SlideshowBorder.None"/> and <see cref="SlideshowBorder.Random"/> (a random one is one of the others, photo by photo).</summary>
        public static BorderLayout? LayoutOf(SlideshowBorder border)
        {
            switch (border)
            {
                case SlideshowBorder.Polaroid:
                    var card = PolaroidCard.DateArea;
                    return Layout(PolaroidCard.Inner, new BorderDate(card.X, card.Y, card.Width, card.Height, DateInk.Handwriting));
                case SlideshowBorder.Frame:
                    return Layout(PictureFrame.Inner, null);
                case SlideshowBorder.Film:
                    var film = FilmStrip.DateArea;
                    return Layout(FilmStrip.Inner, new BorderDate(film.X, film.Y, film.Width, film.Height, DateInk.FilmStamp));
                case SlideshowBorder.Stamp:
                    var stamp = PostageStamp.DateArea;
                    return Layout(PostageStamp.Inner, new BorderDate(stamp.X, stamp.Y, stamp.Width, stamp.Height, DateInk.Capitals));
                case SlideshowBorder.Glow:
                    return Layout(GlowBorder.Inner, null);
                case SlideshowBorder.Shadow:
                    return Layout(ShadowBorder.Inner, null);
                default:
                    return null;
            }
        }

        private static BorderLayout Layout((float X, float Y, float Width, float Height) inner, BorderDate? date) => new(inner.X, inner.Y, inner.Width, inner.Height, date);

        /// <summary>Puts the picture in the border, in place (the border is the whole of the picture's size): the photo made smaller on it. Nothing happens for <see cref="SlideshowBorder.None"/> and <see cref="SlideshowBorder.Random"/>.</summary>
        public static void Apply(SlideshowBorder border, uint[] pixels, int width, int height)
        {
            switch (border)
            {
                case SlideshowBorder.Polaroid:
                    PolaroidCard.Apply(pixels, width, height);
                    break;
                case SlideshowBorder.Frame:
                    PictureFrame.Apply(pixels, width, height);
                    break;
                case SlideshowBorder.Film:
                    FilmStrip.Apply(pixels, width, height);
                    break;
                case SlideshowBorder.Stamp:
                    PostageStamp.Apply(pixels, width, height);
                    break;
                case SlideshowBorder.Glow:
                    GlowBorder.Apply(pixels, width, height);
                    break;
                case SlideshowBorder.Shadow:
                    ShadowBorder.Apply(pixels, width, height);
                    break;
            }
        }

        /// <summary>The border the B key chooses after <paramref name="current"/>: none, each of <see cref="Styles"/>, <see cref="SlideshowBorder.Random"/>, and round to none.</summary>
        public static SlideshowBorder Next(SlideshowBorder current)
        {
            SlideshowBorder[] order = [SlideshowBorder.None, .. Styles, SlideshowBorder.Random];
            return order[(Array.IndexOf(order, current) + 1) % order.Length];
        }
    }
}
