using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the video player under the viewer, and what the main window says about videos. A name ending in Format is a format string.</summary>
    internal static class VideoPlayerStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(VideoPlayerStrings), key);

        public static string PlayTooltip => T();
        public static string GoToLabel => T();
        public static string GoToTooltip => T();
        public static string GoToNotATimeFormat => T();
        public static string MuteTooltip => T();
        public static string VolumeTooltip => T();
        public static string StartingEngine => T();
        public static string EngineFailedFormat => T();
        public static string PlayFailedFormat => T();
        public static string StatusVideoShownFormat => T();
        public static string StatusVideoCopiedFormat => T();
        public static string StatusVideoOpenedElsewhereFormat => T();
        public static string StatusVideoFileMissingFormat => T();
    }
}
