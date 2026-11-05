using System.Runtime.CompilerServices;

namespace PhotoLiker.GenerateVideo
{
    /// <summary>Messages of the video library that reach the person: why a video or its audio could not be made, and how a short length is written.</summary>
    internal static class GenerateVideoStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(GenerateVideoStrings), key);

        public static string FfmpegFailedFormat => T();
        public static string FfmpegNoOutputFormat => T();
        public static string FfmpegNotFound => T();
        public static string TooManyRepeatsFormat => T();
        public static string AudioUnreadable => T();
        public static string DurationSecondsFormat => T();
    }
}
