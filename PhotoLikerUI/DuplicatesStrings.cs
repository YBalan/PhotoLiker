using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class DuplicatesStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(DuplicatesStrings), key);

        // Toolbar button
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string StatusIndexing => T();
        public static string StatusDone => T();

        // Dialog
        public static string Title => T();
        public static string Intro => T();

        public static string OptionWhole => T();
        public static string OptionNames => T();
        public static string KeepLabel => T();
        public static string MoveLabel => T();
        public static string PatternHint => T();

        public static string FilterMove => T();
        public static string FilterChoose => T();
        public static string FilterLeft => T();
        public static string FilterAll => T();

        public static string ColumnGroup => T();
        public static string ColumnAction => T();
        public static string ColumnPhoto => T();
        public static string ColumnSize => T();
        public static string ColumnWhy => T();

        public static string ActionKeep => T();
        public static string ActionMove => T();
        public static string ActionChoose => T();
        public static string ActionLeft => T();
        public static string ActionFailedFormat => T();
        public static string WhyYourChoice => T();
        public static string SizeFormat => T();

        public static string Listing => T();
        public static string ComparingFormat => T();
        public static string ComparingNoEstimateFormat => T();
        public static string ScanDoneFormat => T();
        public static string ScanLeftOutPart => T();
        public static string ScanUnreadablePart => T();
        public static string ScanMetadataPart => T();
        public static string WhyMetadataDiffers => T();
        public static string ScanStopped => T();
        public static string ScanError => T();
        public static string NoDuplicates => T();

        public static string SummaryFormat => T();
        public static string SummaryChoosePart => T();
        public static string SummaryLeftPart => T();

        public static string MoveButtonFormat => T();
        public static string MoveButtonNothing => T();
        public static string LookAgainButton => T();
        public static string KeepThisButton => T();
        public static string KeepThisTooltip => T();
        public static string LeaveAloneButton => T();
        public static string LeaveAloneTooltip => T();
        public static string UndoButton => T();
        public static string UndoTooltip => T();
        public static string StopButton => T();
        public static string CloseButton => T();
        public static string KeepMenu => T();
        public static string LeaveAloneMenu => T();

        public static string MovingFormat => T();
        public static string MovedFormat => T();
        public static string MovedStoppedFormat => T();
        public static string FailedPart => T();
        public static string MoveError => T();
        public static string IndexNotUpdatedFormat => T();

        public static string UndoQuestionFormat => T();
        public static string UndoProgressFormat => T();
        public static string UndoDoneFormat => T();
        public static string UndoSkippedPart => T();
        public static string UndoReindexPart => T();
        public static string UndoNoLog => T();
        public static string UndoError => T();

        // Counts with the noun in the right number
        public static string Photo => T();
        public static string Group => T();
    }
}
