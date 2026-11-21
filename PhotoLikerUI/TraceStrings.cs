using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the Trace Bitmap dialog and its toolbar button. A name ending in Format is a format string; a name that begins with Hint is the tip of the control it is named after.</summary>
    internal static class TraceStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(TraceStrings), key);

        // The button and the window
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string DialogTitle => T();
        public static string StatusOpening => T();
        public static string StatusNoPhoto => T();
        public static string StatusIsVideo => T();
        public static string StatusSavedFormat => T();
        public static string StatusError => T();

        // The scans (the names are those of the Trace Bitmap window of Inkscape)
        public static string GroupSingle => T();
        public static string GroupMultiple => T();
        public static string ModeBrightnessCutoff => T();
        public static string ModeEdgeDetection => T();
        public static string ModeColorQuantization => T();
        public static string ModeBrightnessSteps => T();
        public static string ModeColors => T();
        public static string ModeGrays => T();
        public static string InvertImage => T();
        public static string Smooth => T();
        public static string StackScans => T();
        public static string RemoveBackground => T();
        public static string HintBrightnessCutoff => T();
        public static string HintEdgeDetection => T();
        public static string HintColorQuantization => T();
        public static string HintBrightnessSteps => T();
        public static string HintColors => T();
        public static string HintGrays => T();
        public static string HintInvertImage => T();
        public static string HintSmooth => T();
        public static string HintStackScans => T();
        public static string HintRemoveBackground => T();

        // The options
        public static string GroupOptions => T();
        public static string SuppressSpeckles => T();
        public static string SmoothCorners => T();
        public static string OptimizePaths => T();
        public static string HintSuppressSpeckles => T();
        public static string HintSmoothCorners => T();
        public static string HintOptimizePaths => T();

        // The drawing
        public static string GroupDrawing => T();
        public static string LabelDetail => T();
        public static string HintDetail => T();
        public static string LabelWidth => T();
        public static string LabelHeight => T();
        public static string HintKeepProportions => T();
        public static string HintDoNotKeepProportions => T();
        public static string LabelFormat => T();
        public static string FormatSvg => T();
        public static string FormatDxf => T();
        public static string LabelAccuracy => T();
        public static string HintAccuracy => T();

        // The preview and the buttons
        public static string ShowPhoto => T();
        public static string ShowTrace => T();
        public static string PreviewHint => T();
        public static string LivePreview => T();
        public static string HintLivePreview => T();
        public static string Update => T();
        public static string Save => T();
        public static string Close => T();

        // What it says while it works
        public static string StatusReading => T();
        public static string StatusTracing => T();
        public static string StatusTracedFormat => T();
        public static string StatusNothingTraced => T();
        public static string StatusStale => T();
        public static string StatusPhotoFailedFormat => T();
        public static string StatusTraceFailedFormat => T();
        public static string StatusSaveFailedFormat => T();
        public static string StatusDrawingFormat => T();
        public static string SaveTitle => T();
        public static string FilterSvg => T();
        public static string FilterDxf => T();
    }
}
