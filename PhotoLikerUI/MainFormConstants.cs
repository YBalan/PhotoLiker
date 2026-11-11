using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class MainFormStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(MainFormStrings), key);

        // Settings persistence
        public static string SettingsLoaded => T();
        public static string SettingsLoadError => T();
        public static string SettingsSaved => T();
        public static string SettingsSaveError => T();
        public static string SettingsFolderLoaded => T();

        // Folder / file selection dialogs
        public static string FolderBrowserSelectVideoOutput => T();

        // Video generation
        public static string VideoDialogTitle => T();
        public static string GenerateVideoButtonText => T();
        public static string GenerateVideoTooltip => T();
        public static string StatusVideoGenerating => T();
        public static string StatusVideoGenerated => T();
        public static string StatusVideoGenerationError => T();

        // Folder loading
        public static string StatusFolderLoading => T();
        public static string StatusNoPhotosFound => T();
        public static string StatusFolderFromSavedList => T();
        public static string StatusFolderChecked => T();
        public static string StatusFolderNotReachable => T();
        public static string StatusFolderCheckError => T();
        public static string CheckNothingChanged => T();
        public static string CheckAdded => T();
        public static string CheckRemoved => T();
        public static string CheckChanged => T();
        public static string CheckIncompleteNote => T();
        public const string DebugTitlePrefix             = "[DEBUG] ";

        // About (Help button)
        public static string AboutTitle => T();

        // Image loading / cache
        public static string ImageLoading => T();
        public static string StatusCacheLoadError => T();

        // Like / unlike
        public static string StatusFileCopied => T();
        public static string StatusFileSaved => T();
        public static string StatusFileRemovedFromGroup => T();
        public static string StatusGroupError => T();
        public static string StatusLikeCancelled => T();
        public static string StatusCreateLikedFolderError => T();
        public static string StatusNoFolderSelected => T();

        // Delete (moves the photo to the Deleted folder)
        public static string StatusPhotoDeleted => T();
        public static string StatusDeleteError => T();

        // Duplicate file dialog
        public static string DuplicateDialogText => T();
        public static string DuplicateDialogTitle => T();
        public static string SaveLikedPhotoDialogTitle => T();

        // Copy to the clipboard
        public static string StatusPhotoCopied => T();
        public static string StatusCopyNoPhoto => T();
        public static string StatusCopyError => T();

        // Rotate (turns the photo a quarter clockwise or counterclockwise and saves it in its own file)
        public static string RotateButtonText => T();       // the name screen readers get; the button itself shows the keys
        public static string RotateButtonKeys => T();
        public static string RotateButtonTooltip => T();
        public static string RotateBackButtonText => T();
        public static string RotateBackButtonKeys => T();
        public static string RotateBackButtonTooltip => T();
        public static string StatusRotateNoPhoto => T();
        public static string StatusRotateOnlyJpeg => T();
        public static string StatusRotating => T();
        public static string StatusPhotoRotated => T();
        public static string StatusPhotoRotatedBack => T();
        public static string StatusRotateAdded => T();
        public static string StatusRotateDatesTaken => T();
        public static string StatusRotateTakenFromFile => T();
        public static string StatusRotateDateNotKept => T();
        public static string StatusRotateCopiesNotRenamed => T();
        public static string StatusRotateError => T();

        // Editing a date row of the metadata grid
        public static string DateEditTitle => T();
        public static string DateEditOk => T();
        public static string DateEditCancel => T();
        public static string StatusDateBusy => T();
        public static string StatusDateSetFormat => T();
        public static string StatusDateAndFileSet => T();
        public static string StatusDateError => T();
        public static string StatusDateSaving => T();
        public static string StatusVideoDateNoPlace => T();

        // Zoom / fit
        public static string StatusImageFitted => T();

        // Keyboard
        public static string StatusKeyPressed => T();
        public static string StatusMonthShown => T();
        public static string StatusNoNextMonth => T();
        public static string StatusNoPreviousMonth => T();

        // Theme toggle button labels
        public static string ThemeDarkLabel => T();
        public static string ThemeLightLabel => T();

        // Context menu registration
        public static string ContextMenuRegister => T();
        public static string ContextMenuUnregister => T();
        public static string ContextMenuRegisterTooltip => T();
        public static string ContextMenuUnregisterTooltip => T();
        public static string StatusContextMenuRegistered => T();
        public static string StatusContextMenuRemoved => T();
        public static string StatusContextMenuError => T();

        // Folder explorer buttons
        public static string StatusOpenSourceFolder => T();
        public static string StatusOpenLikedFolder => T();
        public static string StatusLikedFolderNotSet => T();
        public static string StatusSourceFolderNotSet => T();

        // Exception messages
        public const string ExCurrentFileNullOrEmpty     = "Current file cannot be null or empty.";
        public const string ExCurrentFileNotInList       = "Current file is not in the list of files.";
    }

    internal static class MainFormConstants
    {
        // Liked overlay drawn on the main PictureBox
        public const int    LikedOverlayAlpha            = 128;
        public const float  LikedCheckmarkFontSize       = 25f;
        public const float  LikedCheckmarkOffset         = 2f;

        // Folder list next to the check, e.g. "[Liked] [A - Group]"
        public const float  SavedInFontSize              = 12f;
        public const float  SavedInPadding               = 4f;
        public const int    SavedInBgAlpha               = 150;

        // Legend of the group folders in the bottom-left corner
        public const float  GroupLegendFontSize          = 10f;
        public const float  GroupLegendMargin            = 6f;
        public const float  GroupLegendColumnGap         = 14f;

        // Face boxes drawn on the photo
        public const float  FaceNameFontSize             = 10f;
        public const float  MinMarkedFaceSize            = 12f;   // a face marked by hand must be at least this many pixels wide and high

        // Search precision slider: the last search is repeated this long after the slider stopped moving
        public const int    PrecisionSettleMs            = 300;
        public const int    PrecisionSliderWidth         = 100;

        // The combo box with the search model, at the right end of the search strip
        public const int    ModelBoxPadding              = 30; // the drop-down arrow and the margins, added to the width of the longest line

        // Decoded photos kept in memory around the current one (a 24MP photo is ~96 MB decoded)
        public const long MaxCachedImageBytes            = 2L * 1024 * 1024 * 1024;

        // Window position restore
        public const int MinWindowWidth                  = 400;
        public const int MinWindowHeight                 = 300;
        public const int WindowOffScreenOffset           = 40;

        // Preview thumbnail strip
        public const int PreviewLabelHeight              = 30;
        public const int PreviewThumbMarginPx            = 4;
        public const int PreviewThumbTotalVerticalMargin = PreviewThumbMarginPx * 2;
        public const int PreviewPanelBorderWidth         = 2;

        // Liked badge drawn on thumbnail PictureBox
        public const float  LikedBadgeFontSize           = 14f;
        public const float  LikedBadgePadding            = 2f;
        public const int    LikedBadgeBrushAlpha         = 180;
        public const int    LikedBadgeBgAlpha            = 120;
        public const float  LikedBadgeBgPadding          = 1f;

        // Thumbnail strip colours
        public static readonly Color ThumbBackColor      = Color.FromArgb(50, 50, 50);
    }
}
