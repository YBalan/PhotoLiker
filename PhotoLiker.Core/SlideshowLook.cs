namespace PhotoLiker.Core
{
    /// <summary>How the colours of the photos of the slideshow are graded, as a film or a filter would do it. It is done once to every photo, when its frame is made.</summary>
    public enum SlideshowLook
    {
        /// <summary>The photo as it is.</summary>
        None,

        /// <summary>An old colour print: the blacks lifted and the whites lowered, the colours paler and warm, the corners darker, a little grain.</summary>
        Vintage,

        /// <summary>Colour film: a gentle contrast with cool shadows and warm lights, paler colours, grain, and the corners a little darker.</summary>
        Film,

        /// <summary>One brown tone, from the lightness of the photo.</summary>
        Sepia,

        /// <summary>Black and white, with a little contrast and grain.</summary>
        BlackAndWhite,

        /// <summary>The look of a film: teal in the shadows, orange in the lights, more contrast.</summary>
        Cinema,

        /// <summary>The colours as they are, with the corners darkened, as a lens or a film that is not quite even does it.</summary>
        Vignette,

        /// <summary>The colours as they are, with the grain of a fast film over them.</summary>
        Grain,

        /// <summary>A photo from a drawer: brown, faded, with dark corners, grain, and a few scratches and specks of dust.</summary>
        OldPhoto,

        /// <summary>
        /// The grade follows the date the photo was taken: cool and pale in winter (December to February), fresh in spring, warm and bright in summer, golden in autumn (the seasons of the northern hemisphere). A photo
        /// with no date is left as it is.
        /// </summary>
        Season,

        /// <summary>The grade of a 3D lookup table in a <c>.cube</c> file (the file of the setting <see cref="Settings.SlideshowLookFile"/>), as colour grading programs write them.</summary>
        Lut,
    }

    public sealed class SlideshowLookConverter() : TolerantEnumConverter<SlideshowLook>(SlideshowLook.None);
}
