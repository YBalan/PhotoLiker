using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Make a book dialog and its toolbar button. A name ending in Format is a format string; Photo and Page are the forms of a noun for <see cref="Plural.Count"/>.</summary>
    internal static class BookStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(BookStrings), key);

        // The button and the window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string DialogTitle => T();
        public static string StatusOpening => T();
        public static string StatusNoFolder => T();
        public static string StatusMadeFormat => T();
        public static string StatusError => T();

        // The photos
        public static string LabelPhotosFrom => T();
        public static string SourceCurrent => T();
        public static string SourceLiked => T();
        public static string NoPhotosLoaded => T();
        public static string EmptyFolder => T();
        public static string Counting => T();
        public static string CannotListFormat => T();
        public static string LabelSortOrder => T();
        public static string SortOldestFirst => T();
        public static string SortNewestFirst => T();
        public static string SortByName => T();
        public static string SortRandom => T();
        public static string SortButton => T();
        public static string SortHint => T();

        // The book
        public static string LabelFormat => T();
        public static string FormatPdf => T();
        public static string FormatEpub => T();
        public static string FormatDocx => T();
        public static string LabelTitle => T();
        public static string LabelAuthor => T();
        public static string TitleHint => T();
        public static string LabelPageSize => T();
        public static string PageA4 => T();
        public static string PageA5 => T();
        public static string PageLetter => T();
        public static string PageSquare => T();
        public static string LabelOrientation => T();
        public static string OrientationPortrait => T();
        public static string OrientationLandscape => T();
        public static string OrientationFollow => T();
        public static string LabelMargin => T();
        public static string LabelFont => T();
        public static string PageNumbers => T();
        public static string LabelQuality => T();
        public static string QualityScreen => T();
        public static string QualityGood => T();
        public static string QualityPrint => T();
        public static string QualityHint => T();
        public static string PdfOnlyHint => T();
        public static string LabelOutputFolder => T();
        public static string LabelFileName => T();
        public static string Browse => T();
        public static string SelectOutputFolder => T();

        // The thumbnails
        public static string MoveEarlier => T();
        public static string MoveLater => T();
        public static string MoveToStart => T();
        public static string MoveToEnd => T();
        public static string MoveToPlace => T();
        public static string MoveToPlacePromptFormat => T();
        public static string LeaveOut => T();
        public static string LeaveOutHint => T();
        public static string RotateLeft => T();
        public static string RotateRight => T();
        public static string RotateLeftTip => T();
        public static string RotateRightTip => T();
        public static string Draw => T();
        public static string DrawTip => T();
        public static string PutBack => T();
        public static string Reverse => T();
        public static string Undo => T();
        public static string Redo => T();
        public static string ThumbnailSize => T();
        public static string SelectAll => T();
        public static string ThumbnailsHint => T();
        public static string LeftOutLabel => T();
        public static string HasAboveTip => T();
        public static string HasBelowTip => T();
        public static string CountsFormat => T();
        public static string NoPhotosToShow => T();

        // The descriptions
        public static string LabelAbove => T();
        public static string LabelBelow => T();
        public static string NothingPicked => T();
        public static string SeveralPickedFormat => T();
        public static string PickedFormat => T();
        public static string Bold => T();
        public static string Italic => T();
        public static string Underline => T();
        public static string Strike => T();
        public static string FontSize => T();
        public static string TextColor => T();
        public static string ColorAutomatic => T();
        public static string ColorMore => T();
        public static string AlignLeft => T();
        public static string AlignCenter => T();
        public static string AlignRight => T();
        public static string AlignJustify => T();
        public static string BulletList => T();
        public static string NumberedList => T();
        public static string ShowCharacters => T();
        public static string ClearFormatting => T();
        public static string DescriptionHint => T();

        // Making the book
        public static string MakeButton => T();
        public static string OpenButton => T();
        public static string CloseButton => T();
        public static string CancelButton => T();
        public static string StatusReady => T();
        public static string StatusPreparingFormat => T();
        public static string StatusWritingPdf => T();
        public static string StatusWritingEpub => T();
        public static string StatusWritingDocx => T();
        public static string StatusDoneFormat => T();
        public static string StatusCancelled => T();
        public static string StatusUnreadableFormat => T();
        public static string StatusSaveFailedFormat => T();
        public static string NothingToMake => T();
        public static string EnterOutputFolder => T();
        public static string EnterFileName => T();
        public static string InvalidFileName => T();
        public static string ReplaceQuestionFormat => T();
        public static string FailedFormat => T();
        public static string UnreadableListFormat => T();
        public static string Photo => T();
        public static string Page => T();
    }
}
