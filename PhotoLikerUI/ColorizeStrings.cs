using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Colorize button and window.</summary>
    internal static class ColorizeStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ColorizeStrings), key);

        // Toolbar button and the status bar of the main window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string StatusNoPhotos => T();
        public static string StatusIndexing => T();
        public static string StatusDoneFormat => T();

        // The window
        public static string Title => T();
        public static string Intro => T();
        public static string ScopeThisFormat => T();
        public static string ScopeList => T();
        public static string StrengthLabel => T();
        public static string StrengthHint => T();
        public static string ColumnPhoto => T();
        public static string ColumnFolder => T();
        public static string ColumnColour => T();
        public static string ColumnResult => T();
        public static string ColourGrey => T();
        public static string ColourToned => T();
        public static string ColourColour => T();
        public static string ColourUnreadable => T();

        // Buttons
        public static string ColorizeButton => T();
        public static string StopButton => T();
        public static string CloseButton => T();
        public static string TickAllButton => T();
        public static string TickNoneButton => T();

        // What is going on
        public static string StatusLookingFormat => T();
        public static string StatusFoundFormat => T();
        public static string StatusNoneFound => T();
        public static string StatusPreparing => T();
        public static string StatusWorkingFormat => T();
        public static string StatusFinishedFormat => T();
        public static string StatusStopped => T();
        public static string SummaryTickedFormat => T();

        // The Result column
        public static string ResultWorking => T();
        public static string ResultSavedFormat => T();
        public static string ResultFailedFormat => T();
        public static string ResultHasCopyFormat => T();
        public static string ResultNotDone => T();
    }
}
