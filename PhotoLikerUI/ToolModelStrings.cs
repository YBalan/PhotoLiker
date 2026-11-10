using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts about the models of the features that edit or move a photo (the Parallax effect, Colorize): asking to download one, and how the download goes.</summary>
    internal static class ToolModelStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ToolModelStrings), key);

        // What the question is about
        public static string ParallaxFeature => T();
        public static string ColorizeFeature => T();

        // The question
        public static string DownloadTitle => T();
        public static string DownloadQuestionFormat => T();

        // The status bar
        public static string StatusDownloadingFormat => T();
        public static string StatusReadyFormat => T();
        public static string StatusFailedFormat => T();
        public static string StatusDeclinedFormat => T();
        public static string StatusParallaxWithoutModel => T();
    }
}
