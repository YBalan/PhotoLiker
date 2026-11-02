using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class DateAlignmentStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(DateAlignmentStrings), key);

        // Toolbar button
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string StatusIndexing => T();
        public static string StatusDone => T();
        public static string StatusUndone => T();

        // Dialog
        public static string Title => T();
        public static string Intro => T();

        public static string OptionCreated => T();
        public static string OptionModified => T();
        public static string OptionExif => T();

        public static string FilterChange => T();
        public static string FilterAligned => T();
        public static string FilterNoDate => T();
        public static string FilterUnreadable => T();
        public static string FilterAll => T();

        public static string ColumnPhoto => T();
        public static string ColumnTaken => T();
        public static string ColumnCreated => T();
        public static string ColumnModified => T();
        public static string ColumnAction => T();

        public static string ActionAligned => T();
        public static string ActionNoDate => T();
        public static string ActionLeftOut => T();
        public static string ActionChangeFormat => T();
        public static string ActionCreated => T();
        public static string ActionModified => T();
        public static string ActionExifOriginal => T();
        public static string ActionExifDigitized => T();
        public static string ActionExifModified => T();
        public static string ActionFailedFormat => T();
        public static string TakenNotBelievableFormat => T();
        public static string TakenNone => T();

        public static string LeaveOutMenu => T();
        public static string IncludeMenu => T();

        public static string Listing => T();
        public static string ScanProgressFormat => T();
        public static string ScanProgressNoEstimateFormat => T();
        public static string ScanDoneFormat => T();
        public static string ScanStopped => T();
        public static string ScanError => T();
        public static string NoPhotos => T();

        public static string SummaryFormat => T();
        public static string SummaryLeftOutPart => T();
        public static string SummaryNothing => T();
        public static string SummaryNothingChecked => T();

        public static string ApplyButtonFormat => T();
        public static string ApplyButtonNothing => T();
        public static string LookAgainButton => T();
        public static string StopButton => T();
        public static string CloseButton => T();
        public static string UndoButton => T();
        public static string UndoTooltip => T();

        public static string ApplyProgressFormat => T();
        public static string UndoProgressFormat => T();
        public static string IndexNotUpdatedFormat => T();
        public static string ApplyDoneFormat => T();
        public static string ApplyStoppedFormat => T();
        public static string CopiesPart => T();
        public static string RenamedPart => T();
        public static string FailedPart => T();
        public static string NotKeptPart => T();
        public static string ApplyError => T();

        public static string UndoQuestionFormat => T();
        public static string UndoDoneFormat => T();
        public static string UndoSkippedPart => T();
        public static string UndoNoLog => T();
        public static string UndoError => T();

        // Giving a date to photos without a usable EXIF date
        public static string AssignButton => T();
        public static string AssignTooltip => T();
        public static string AssignMenu => T();
        public static string ForgetMenu => T();
        public static string GivenNote => T();
        public static string ActionGivenNote => T();
        public static string StatusNothingToGive => T();
        public static string StatusGiven => T();
        public static string StatusGivenNonePart => T();
        public static string StatusForgot => T();
        public static string Photo => T(); // forms for Plural.Count: "photo|photos"

        public static string AssignTitle => T();
        public static string AssignIntro => T();
        public static string AssignWriteTaken => T();
        public static string AssignScopeSelected => T();
        public static string AssignScopeAll => T();
        public static string AssignSourceExif => T();
        public static string AssignSourceFolder => T();
        public static string AssignSourceFile => T();
        public static string AssignSourceTyped => T();
        public static string AssignCountFormat => T();
        public static string AssignCountNone => T();
        public static string AssignFileWarning => T();
        public static string AssignNoPlaceFormat => T();
        public static string AssignOk => T();
        public static string AssignCancel => T();
        public const string PickerFormat                 = "dd MMM yyyy HH:mm:ss";
    }
}
