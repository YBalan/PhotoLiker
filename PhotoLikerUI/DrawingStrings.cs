using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Draw window and its toolbar button. A name ending in Format is a format string; a name that begins with Hint is the tip of the control it is named after.</summary>
    internal static class DrawingStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(DrawingStrings), key);

        // The button and the window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string DialogTitle => T();
        public static string StatusOpening => T();
        public static string StatusNoPhoto => T();
        public static string StatusIsVideo => T();
        public static string StatusSavedFormat => T();
        public static string StatusError => T();
        public static string StatusCannotReadFormat => T();
        public static string StatusReady => T();
        public static string InfoFormat => T();

        // The tools
        public static string ToolPen => T();
        public static string ToolHighlighter => T();
        public static string ToolLine => T();
        public static string ToolText => T();
        public static string ToolCrop => T();
        public static string HintPen => T();
        public static string HintHighlighter => T();
        public static string HintLine => T();
        public static string HintText => T();
        public static string HintCrop => T();

        // The colours and the width
        public static string ColorTipFormat => T();
        public static string ColorRed => T();
        public static string ColorOrange => T();
        public static string ColorYellow => T();
        public static string ColorGreen => T();
        public static string ColorBlue => T();
        public static string ColorPurple => T();
        public static string ColorBlack => T();
        public static string ColorWhite => T();
        public static string MoreColors => T();
        public static string MoreColorsTip => T();
        public static string LabelWidth => T();
        public static string LabelSize => T();
        public static string HintWidth => T();
        public static string HintSize => T();

        // Undo, crop, text
        public static string Undo => T();
        public static string Redo => T();
        public static string UndoTip => T();
        public static string RedoTip => T();
        public static string RotateLeft => T();
        public static string RotateRight => T();
        public static string RotateLeftTip => T();
        public static string RotateRightTip => T();
        public static string ApplyCrop => T();
        public static string ApplyCropTip => T();
        public static string CancelCrop => T();
        public static string CancelCropTip => T();
        public static string StatusCropFormat => T();
        public static string StatusCroppedFormat => T();
        public static string StatusTextHow => T();

        // Scan text
        public static string ScanText => T();
        public static string ScanTextTip => T();
        public static string ScanTitle => T();
        public static string ScanCopy => T();
        public static string ScanCopyTip => T();
        public static string ScanHide => T();
        public static string StatusScanCopied => T();
        public static string StatusScanBusy => T();
        public static string StatusScanFoundFormat => T();
        public static string StatusScanNothing => T();
        public static string StatusScanFailedFormat => T();
        public static string StatusScanDeclined => T();
        public static string StatusScanStopped => T();

        // Saving
        public static string Save => T();
        public static string SaveTip => T();
        public static string SaveAs => T();
        public static string SaveAsTip => T();
        public static string Close => T();
        public static string SaveAsTitle => T();
        public static string StatusNothingToSave => T();
        public static string StatusSaving => T();
        public static string StatusSaveOnlyAsFormat => T();
        public static string StatusSavedOverFormat => T();
        public static string StatusSaveFailedFormat => T();
        public static string ConfirmSaveFormat => T();
    }
}
