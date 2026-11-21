using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Video to GIF dialog and its toolbar button. A name ending in Format is a format string; a name that begins with Hint is the tip of the control it is named after.</summary>
    internal static class VideoToGifStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(VideoToGifStrings), key);

        // The button and the window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string DialogTitle => T();
        public static string StatusOpening => T();
        public static string StatusNoVideoPicked => T();
        public static string StatusSavedFormat => T();
        public static string StatusError => T();
        public static string PickTitle => T();
        public static string FilterVideos => T();
        public static string FilterAllFiles => T();

        // The video
        public static string LabelVideo => T();
        public static string Browse => T();
        public static string HintBrowse => T();
        public static string NoVideo => T();
        public static string InfoFormat => T();
        public static string InfoNoRateFormat => T();
        public static string UnknownLength => T();

        // The frames
        public static string GroupRange => T();
        public static string LabelFirst => T();
        public static string LabelLast => T();
        public static string AllFrames => T();
        public static string HintFirst => T();
        public static string HintLast => T();
        public static string HintAllFrames => T();
        public static string FrameOfFormat => T();
        public static string PreviewNone => T();

        // The GIF
        public static string GroupGif => T();
        public static string LabelFrameEvery => T();
        public static string LabelFramePlays => T();
        public static string VideoSpeed => T();
        public static string LabelWidth => T();
        public static string OriginalSize => T();
        public static string LabelColors => T();
        public static string LabelDither => T();
        public static string LabelRepeat => T();
        public static string DitherNone => T();
        public static string DitherBayer => T();
        public static string DitherSierra => T();
        public static string RepeatForever => T();
        public static string RepeatOnce => T();
        public static string HintFrameEvery => T();
        public static string HintFramePlays => T();
        public static string HintVideoSpeed => T();
        public static string SpeedNoteFormat => T();
        public static string HintWidth => T();
        public static string HintOriginalSize => T();
        public static string HintColors => T();
        public static string HintDither => T();
        public static string HintRepeat => T();
        public static string SummaryFormat => T();
        public static string SummaryUnknown => T();
        public static string SecondsFormat => T();
        public static string ResultHeading => T();
        public static string ResultPlaceholder => T();

        // The work
        public static string Make => T();
        public static string Stop => T();
        public static string Close => T();
        public static string HintMake => T();
        public static string SaveTitle => T();
        public static string FilterGif => T();
        public static string StatusReading => T();
        public static string StatusVideoFailedFormat => T();
        public static string StatusPalette => T();
        public static string StatusFramesFormat => T();
        public static string StatusStopped => T();
        public static string StatusFailedFormat => T();
        public static string StatusFfmpegMissingFormat => T();
        public static string StatusNoOutput => T();
        public static string StatusMadeFormat => T();
        public static string SizeKilobytesFormat => T();
        public static string SizeMegabytesFormat => T();
    }
}
