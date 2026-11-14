using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the buttons that the designer of the main window makes.</summary>
    internal static class ToolbarStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ToolbarStrings), key);

        public static string Open => T();
        public static string Save => T();
        public static string SaveTooltip => T();
        public static string SaveAsTitleFormat => T();
        public static string SaveAsFilterAll => T();
        public static string StatusNothingToSave => T();
        public static string StatusSaveAsSavedFormat => T();
        public static string StatusSaveAsFailedFormat => T();
        public static string StatusSaveAsSameFile => T();
        public static string Copy => T();
        public static string Help => T();
        public static string ThemeTooltip => T();
        public static string OpenSource => T();
        public static string OpenSourceTooltip => T();
        public static string OpenLiked => T();
        public static string OpenLikedTooltip => T();
        public static string LoadLiked => T();
        public static string LoadLikedTooltip => T();
        public static string GpsTooltip => T();
    }
}
