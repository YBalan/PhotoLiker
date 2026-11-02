using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the buttons that the designer of the main window makes.</summary>
    internal static class ToolbarStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ToolbarStrings), key);

        public static string New => T();
        public static string Open => T();
        public static string Save => T();
        public static string Copy => T();
        public static string Paste => T();
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
