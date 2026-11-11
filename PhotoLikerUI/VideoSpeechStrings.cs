using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the toolbar button and the window that shows what is said in the videos of the folder. A name ending in Format is a format string.</summary>
    internal static class VideoSpeechStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(VideoSpeechStrings), key);

        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string StatusVideosOff => T();
        public static string Title => T();
        public static string Intro => T();
        public static string SummaryFormat => T();
        public static string ShownPartFormat => T();
        public static string FindLabel => T();
        public static string FindHint => T();
        public static string FilterAll => T();
        public static string FilterProblems => T();
        public static string StatusHeard => T();
        public static string StatusNothingHeard => T();
        public static string StatusNotRead => T();
        public static string StatusNoSound => T();
        public static string StatusFailed => T();
        public static string StatusUnreadable => T();
        public static string ColumnVideo => T();
        public static string ColumnLength => T();
        public static string ColumnStatus => T();
        public static string ColumnLanguage => T();
        public static string ColumnModels => T();
        public static string ModelLabel => T();
        public static string ModelTooltip => T();
        public static string ModelChoiceFormat => T();
        public static string MenuHearWith => T();
        public static string ColumnMoments => T();
        public static string ColumnSpeech => T();
        public static string ColumnRecorded => T();
        public static string ColumnSegments => T();
        public static string ColumnMomentTimes => T();
        public static string ColumnStart => T();
        public static string ColumnEnd => T();
        public static string ColumnText => T();
        public static string SheetVideos => T();
        public static string SheetSpeech => T();
        public static string Video => T();
        public static string Stretch => T();
        public static string DetailNothingSelected => T();
        public static string DetailSeveralFormat => T();
        public static string DetailNoMoments => T();
        public static string DetailNotRead => T();
        public static string DetailNothingHeard => T();
        public static string DetailNoSound => T();
        public static string DetailProblemFormat => T();
        public static string HearSelectedButton => T();
        public static string HearSelectedTooltip => T();
        public static string HearAllButton => T();
        public static string HearAllTooltip => T();
        public static string ExportButton => T();
        public static string ExportTooltip => T();
        public static string StopButton => T();
        public static string CloseButton => T();
        public static string MenuHearAgain => T();
        public static string LanguageTitle => T();
        public static string LanguageQuestionFormat => T();
        public static string LanguageLabel => T();
        public static string LanguageAuto => T();
        public static string LanguageNowFormat => T();
        public static string LanguageHint => T();
        public static string LanguageOkButton => T();
        public static string StatusLoading => T();
        public static string StatusLoadErrorFormat => T();
        public static string StatusIndexing => T();
        public static string StatusNoSoundToHear => T();
        public static string StatusDoneFormat => T();
        public static string StatusStoppedFormat => T();
        public static string StatusFailedPartFormat => T();
        public static string ExportTitle => T();
        public static string ExportFilter => T();
        public static string ExportFileNameFormat => T();
        public static string StatusExportedFormat => T();
        public static string StatusExportErrorFormat => T();
        public static string StatusHearErrorFormat => T();
        public static string LanguageCancelButton => T();
    }
}
