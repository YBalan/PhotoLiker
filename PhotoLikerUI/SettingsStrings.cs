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

        // 3. Common Settings
        public static string LanguageDescription => T();
        public static string LikedFolder => T();
        public static string CacheSize => T();
        public static string ThumbnailSize => T();

        // 4. Photo Settings
        public static string GoThroughSubFolders => T();
        public static string Extensions => T();
        public static string SortOrder => T();

        // 5. Video Settings
        public static string SupportVideo => T();
        public static string VideoExtensions => T();
        public static string VideoSpeech => T();
        public static string SpeechModel => T();
        public static string SpeechLanguage => T();

        // 6. Slideshow
        public static string SlideshowMode => T();
        public static string SlideshowMonitors => T();
        public static string SlideshowTimeout => T();
        public static string SlideshowWatermark => T();
        public static string SlideshowEffect => T();
    }
}
