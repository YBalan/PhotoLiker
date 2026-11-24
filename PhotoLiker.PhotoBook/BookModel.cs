namespace PhotoLiker.PhotoBook
{
    /// <summary>The kinds of file a book can be made as.</summary>
    public enum BookFormat
    {
        Pdf,
        Epub,
    }

    /// <summary>The size of the pages of a PDF.</summary>
    public enum PageFormat
    {
        A4,
        A5,
        Letter,

        /// <summary>21 cm by 21 cm, the size of many photo books; it has no portrait and landscape.</summary>
        Square,
    }

    /// <summary>How a page of a PDF is turned.</summary>
    public enum PageOrientation
    {
        Portrait,
        Landscape,

        /// <summary>Each page as its photo is: portrait for a photo that is taller than wide, landscape for the others.</summary>
        FollowPhoto,
    }

    /// <summary>How much picture goes into the file: how many pixels the longest side of a photo has, and how hard the JPEG is squeezed.</summary>
    public enum PictureQuality
    {
        /// <summary>For reading on a screen: small files.</summary>
        Screen,

        /// <summary>The usual: sharp on a screen and in a print on paper of A4.</summary>
        Good,

        /// <summary>For a print: the pixels the photo has up to 3600 on the long side (A4 at 300 dpi), hardly squeezed.</summary>
        Print,
    }

    public static class PictureQualities
    {
        public static int MaxSide(PictureQuality quality) => quality switch
        {
            PictureQuality.Screen => 1600,
            PictureQuality.Good => 2400,
            _ => 3600,
        };

        /// <summary>The JPEG quality (1-100) the photos are written with.</summary>
        public static int JpegQuality(PictureQuality quality) => quality switch
        {
            PictureQuality.Screen => 80,
            PictureQuality.Good => 87,
            _ => 93,
        };
    }

    /// <summary>The paper: sizes in points (1/72 inch, what a PDF measures in).</summary>
    public static class PageSizes
    {
        public const double PointsPerMillimetre = 72.0 / 25.4;

        /// <summary>The width and height of a page of a format, turned as asked (<paramref name="photoIsPortrait"/> says what <see cref="PageOrientation.FollowPhoto"/> follows).</summary>
        public static (double Width, double Height) Points(PageFormat format, PageOrientation orientation, bool photoIsPortrait)
        {
            var (shortSide, longSide) = format switch
            {
                PageFormat.A4 => (210.0, 297.0),
                PageFormat.A5 => (148.0, 210.0),
                PageFormat.Letter => (215.9, 279.4),
                _ => (210.0, 210.0),
            };

            var landscape = orientation switch
            {
                PageOrientation.Landscape => true,
                PageOrientation.FollowPhoto => !photoIsPortrait,
                _ => false,
            };

            return landscape
                ? (longSide * PointsPerMillimetre, shortSide * PointsPerMillimetre)
                : (shortSide * PointsPerMillimetre, longSide * PointsPerMillimetre);
        }
    }

    /// <summary>What the book as a whole is set up as.</summary>
    /// <param name="Title">The title of the book; when it is not empty it has a page of its own at the start.</param>
    /// <param name="Author">Who made the book, under the title.</param>
    /// <param name="Language">The language of the text, as a code of two letters ("en", "ua"): an EPUB says it.</param>
    /// <param name="Format">The paper of a PDF. An EPUB has none: its readers set the text to the screen.</param>
    /// <param name="MarginMillimetres">The white round the text and the photo on a page of a PDF.</param>
    /// <param name="FontFamily">The font a PDF is set in; the EPUB leaves the font to the reader of the book.</param>
    /// <param name="PageNumbers">A number at the foot of each page of a PDF.</param>
    public sealed record BookOptions(
        string Title,
        string Author,
        string Language,
        PageFormat Format = PageFormat.A4,
        PageOrientation Orientation = PageOrientation.FollowPhoto,
        double MarginMillimetres = 15,
        string FontFamily = "Segoe UI",
        bool PageNumbers = true);

    /// <summary>
    /// A page of the book: a photo as a JPEG file (upright, at the size the book needs), and what is written above and below it. A page whose <paramref name="ImageFile"/> is empty has no photo: it is
    /// only the text (a digitized manuscript is a book of its text, with or without the photos of its pages). The text can have pictures in it (<see cref="RichPicture"/>: paragraphs of their own).
    /// </summary>
    public sealed record BookPage(string ImageFile, int ImageWidth, int ImageHeight, RichDocument Above, RichDocument Below, string Description)
    {
        /// <summary>A page that is only text, as one document (what is written below the first line is on the same page flow as what is written above).</summary>
        public static BookPage TextOnly(RichDocument text, string description) =>
            new(string.Empty, 0, 0, text, RichDocument.Empty, description);

        public bool HasImage => !string.IsNullOrEmpty(ImageFile);
    }

    /// <summary>How much a photo is turned in the book, on top of the way it is upright: only in quarters of a turn, clockwise. The photo file is not touched.</summary>
    public static class BookRotation
    {
        /// <summary>The turn as 0, 90, 180 or 270 degrees: any number is brought into that range (-90 is 270, 360 is 0) and rounded down to a quarter.</summary>
        public static int Normalize(int degrees) => ((degrees % 360) + 360) % 360 / 90 * 90;

        /// <summary>The turn after more quarter turns (negative: counterclockwise).</summary>
        public static int Add(int degrees, int quarterTurns) => Normalize(Normalize(degrees) + quarterTurns * 90);

        /// <summary>True for a turn that puts the photo on its side: the width and the height change places.</summary>
        public static bool IsSideways(int degrees) => Normalize(degrees) is 90 or 270;
    }

    /// <summary>A photo of the book as it is kept: its file, the descriptions (as the RTF of the Rich Text Box), whether it is left out, and how it is turned.</summary>
    public sealed class BookEntry
    {
        private int _rotation;

        public BookEntry(string path) => Path = path;

        /// <summary>The turn of the photo in the book, clockwise, in degrees: 0, 90, 180 or 270 (see <see cref="BookRotation"/>).</summary>
        public int Rotation
        {
            get => _rotation;
            set => _rotation = BookRotation.Normalize(value);
        }

        public string Path { get; set; }

        /// <summary>What is written above the photo, as RTF; null for nothing.</summary>
        public string? AboveRtf { get; set; }

        /// <summary>What is written below the photo, as RTF; null for nothing.</summary>
        public string? BelowRtf { get; set; }

        /// <summary>The photo is in the list but not in the book.</summary>
        public bool LeftOut { get; set; }

        public bool HasText => !string.IsNullOrWhiteSpace(AboveRtf) || !string.IsNullOrWhiteSpace(BelowRtf);
    }
}
