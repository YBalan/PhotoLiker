using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    /// <summary>What each row of the settings grid means: shown in the help pane under the grid when the row is selected.</summary>
    internal static class SettingsStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(SettingsStrings), key);

        // 1. Progress
        public static string TotalFiles => T();
        public static string CurrentIndex => T();

        // 2. Current File
        public static string CurrentFolder => T();
        public static string CurrentFilePath => T();
        public static string CurrentFileName => T();
        public static string LikedFile => T();
        public static string SavedInFolders => T();

        // 3. Settings
        public static string LikedFolder => T();
        public static string GoThroughSubFolders => T();
        public static string Extensions => T();
        public static string SortOrder => T();
        public static string CacheSize => T();
        public static string ThumbnailSize => T();
        public static string LanguageDescription => T();
    }
}
