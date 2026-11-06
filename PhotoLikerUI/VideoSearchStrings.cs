using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of searching the videos of a folder: indexing them, the results, the strip of moments. A name ending in Format is a format string.</summary>
    internal static class VideoSearchStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(VideoSearchStrings), key);

        public static string IndexedVideosFormat => T();
        public static string IndexVideosReadingFormat => T();
        public static string IndexVideosProgressFormat => T();
        public static string IndexVideosProgressNoEstimateFormat => T();
        public static string IndexVideosTooltipFormat => T();
        public static string StatusVideosDoneFormat => T();
        public static string StatusVideosUpToDateFormat => T();
        public static string StatusVideosStoppedFormat => T();
        public static string StatusVideosFailedPartFormat => T();
        public static string ResultsTitleVideosFormat => T();
        public static string ResultsTitleWeakVideosFormat => T();
        public static string StatusResultsShownVideosFormat => T();
        public static string IndexedVideosSpeechFormat => T();
        public static string IndexSpeechProgressFormat => T();
        public static string IndexSpeechProgressNoEstimateFormat => T();
        public static string IndexSpeechTooltipFormat => T();
        public static string StatusSpeechDoneFormat => T();
        public static string StatusSpeechUpToDateFormat => T();
        public static string StatusSpeechStoppedFormat => T();
        public static string StatusSpeechDeclined => T();
        public static string StatusSpeechSilentPartFormat => T();
        public static string SpeechDownloadQuestionFormat => T();
        public static string SpeechDownloadTitle => T();
    }
}
