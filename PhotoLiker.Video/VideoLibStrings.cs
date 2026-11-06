using System.Runtime.CompilerServices;

namespace PhotoLiker.Video
{
    /// <summary>Messages of the video library that reach the person, and what the rows of a video in the metadata grid mean.</summary>
    internal static class VideoLibStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(VideoLibStrings), key);

        public static string FfmpegNotFound => T();
        public static string UnreadableFormat => T();
        public static string DateFromVideo => T();
        public static string DateFromFile => T();
        public static string DateNone => T();
        public static string NoAudio => T();

        public static string DescDuration => T();
        public static string DescFrameSize => T();
        public static string DescFrameRate => T();
        public static string DescVideoCodec => T();
        public static string DescAudio => T();
        public static string DescBitrate => T();
        public static string DescRecorded => T();
        public static string DescDateFrom => T();
        public static string DescLocation => T();
        public static string DescEncoder => T();
        public static string DescFileSize => T();
        public static string DescFileDates => T();
    }
}
