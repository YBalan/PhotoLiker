using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Refocus bar and its toolbar button (see <see cref="RefocusBar"/>).</summary>
    internal static class RefocusStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(RefocusStrings), key);

        // The toolbar button
        public static string ButtonText => T();
        public static string ButtonTooltip => T();

        // The bar
        public static string Title => T();
        public static string BlurLabel => T();
        public static string RangeLabel => T();
        public static string HazeLabel => T();
        public static string BlurTooltip => T();
        public static string RangeTooltip => T();
        public static string HazeTooltip => T();
        public static string SaveButton => T();
        public static string SaveTooltip => T();
        public static string BarGripTooltip => T();
        public static string BarCloseTooltip => T();

        // What it says
        public static string StatusStarting => T();
        public static string StatusReady => T();
        public static string StatusSavingFormat => T();
        public static string StatusSavedFormat => T();
        public static string StatusSaveFailedFormat => T();
        public static string StatusFailedFormat => T();
        public static string StatusNoModel => T();
        public static string StatusNoPhoto => T();
        public static string StatusIndexing => T();
    }
}
