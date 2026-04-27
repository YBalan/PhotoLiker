namespace PhotoLikerUI
{
    internal static class MainFormStrings
    {
        // Settings persistence
        public const string SettingsFileName             = "settings.json";
        public const string SettingsLoaded               = "Settings loaded from {0}";
        public const string SettingsLoadError            = "Error loading settings: {0}";
        public const string SettingsSaved                = "Settings saved to {0}";
        public const string SettingsSaveError            = "Error saving settings: {0}";

        // Folder / file selection dialogs
        public const string FolderBrowserSelectPhotos    = "Select a folder containing photos";
        public const string FolderBrowserSelectLiked     = "Select a folder to save liked photos";

        // Folder loading
        public const string DefaultLikedFolderName       = "Liked";
        public const string StatusFolderLoaded           = "Loaded {0} photos from '{1}'";
        public const string StatusFolderLoadError        = "Error loading folder: {0}";
        public const string StatusNoPhotosFound          = "No photos found in the selected folder.";
        public const string TitleFormat                  = "PhotoLiker \u2014 {0}";

        // Image loading / cache
        public const string StatusImageLoadError         = "Error loading image: {0}";
        public const string StatusCacheLoadError         = "Error loading image {0}: {1}";

        // Like / unlike
        public const string StatusFileCopied             = "File '{0}' copied to liked folder.";
        public const string StatusFileSaved              = "File '{0}' saved to '{1}'.";
        public const string StatusFileRemoved            = "Removed '{0}' from liked folder.";
        public const string StatusLikeCancelled          = "Like cancelled.";
        public const string StatusLikeError              = "Error liking photo: {0}";
        public const string StatusUnlikeError            = "Error unliking photo: {0}";
        public const string StatusCreateLikedFolderError = "Error creating liked folder: {0}";
        public const string StatusNoFolderSelected       = "No folder selected for liked photos.";

        // Duplicate file dialog
        public const string DuplicateDialogText          = "File '{0}' already exists in the liked folder. Choose another location?";
        public const string DuplicateDialogTitle         = "File Exists";
        public const string SaveLikedPhotoDialogTitle    = "Save Liked Photo";

        // Zoom / fit
        public const string StatusImageFitted            = "Image fitted to screen.";

        // Keyboard
        public const string StatusKeyPressed             = "Key pressed: {0}";

        // Theme toggle button labels
        public const string ThemeDarkLabel               = "\U0001f319 Dark";
        public const string ThemeLightLabel              = "\u2600\ufe0f Light";

        // Context menu registration
        public const string ContextMenuRegister          = "Register Context Menu";
        public const string ContextMenuUnregister        = "Unregister Context Menu";
        public const string ContextMenuRegisterTooltip   = "Add 'Open in Photo Liker' to folder right-click menu";
        public const string ContextMenuUnregisterTooltip = "Remove 'Open in Photo Liker' from folder right-click menu";
        public const string StatusContextMenuRegistered  = "Context menu entry registered. Right-click any folder to use 'Open in Photo Liker'.";
        public const string StatusContextMenuRemoved     = "Context menu entry removed.";
        public const string StatusContextMenuError       = "Error updating context menu registration: {0}";

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
        public const string LikedCheckmark               = "\u2713";   // ✓

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
