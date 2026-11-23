using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Telegram Sticker dialog and its toolbar button. A name ending in Format is a format string; a name that begins with Hint is the tip of the control it is named after.</summary>
    internal static class StickerStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(StickerStrings), key);

        // The button and the window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string DialogTitle => T();
        public static string StatusOpening => T();
        public static string StatusNoPhoto => T();
        public static string StatusIsVideo => T();
        public static string StatusSavedFormat => T();
        public static string StatusError => T();

        // The cutout
        public static string GroupCutout => T();
        public static string LabelSubject => T();
        public static string ChoiceDetect => T();
        public static string ChoiceWholePicture => T();
        public static string HintSubject => T();
        public static string LabelSensitivity => T();
        public static string HintSensitivity => T();
        public static string LabelSmallParts => T();
        public static string HintSmallParts => T();
        public static string LabelFollowEdges => T();
        public static string HintFollowEdges => T();
        public static string LabelEdge => T();
        public static string HintEdge => T();

        // The outline and the shadow
        public static string GroupOutline => T();
        public static string OutlineCheck => T();
        public static string HintOutline => T();
        public static string LabelOutlineWidth => T();
        public static string HintOutlineWidth => T();
        public static string LabelOutlineColor => T();
        public static string HintOutlineColor => T();
        public static string ShadowCheck => T();
        public static string HintShadow => T();

        // The file
        public static string GroupFile => T();
        public static string LabelFormat => T();
        public static string FormatWebP => T();
        public static string FormatPng => T();
        public static string HintFormat => T();
        public static string LabelQuality => T();
        public static string HintQuality => T();

        // The pictures
        public static string LabelBackground => T();
        public static string BackgroundChecker => T();
        public static string BackgroundDark => T();
        public static string BackgroundLight => T();
        public static string HintBackground => T();
        public static string PaneCutline => T();
        public static string PaneSticker => T();
        public static string PaneEmptyPhoto => T();
        public static string PaneEmptySticker => T();
        public static string HowTo => T();

        // The buttons and what the window says while it works
        public static string Save => T();
        public static string Close => T();
        public static string SaveTitle => T();
        public static string FilterWebP => T();
        public static string FilterPng => T();
        public static string StatusReading => T();
        public static string StatusPhotoFailedFormat => T();
        public static string StatusFindingSubject => T();
        public static string StatusModelMissing => T();
        public static string StatusSubjectFailedFormat => T();
        public static string StatusNoSubject => T();
        public static string StatusMaking => T();
        public static string StatusFfmpegMissing => T();
        public static string StatusEncodeFailedFormat => T();
        public static string StatusSaveFailedFormat => T();
        public static string StatusSavedTooBigFormat => T();
        public static string InfoFitsFormat => T();
        public static string InfoTooBigFormat => T();
    }
}
