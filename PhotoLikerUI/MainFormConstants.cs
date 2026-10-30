namespace PhotoLikerUI
{
    internal static class MainFormStrings
    {
        // Settings persistence
        public const string SettingsLoaded               = "Settings loaded from {0}";
        public const string SettingsLoadError            = "Error loading settings: {0}";
        public const string SettingsSaved                = "Settings saved to {0}";
        public const string SettingsSaveError            = "Error saving settings: {0}";
        public const string SettingsFolderLoaded         = "Settings loaded from folder '{0}'.";

        // Folder / file selection dialogs
        public const string FolderBrowserSelectVideoOutput = "Select a folder to save generated video";

        // Video generation
        public const string VideoDialogTitle             = "Generate Video";
        public const string GenerateVideoButtonText      = "Generate Video";
        public const string GenerateVideoTooltip         = "Generate video from loaded photos using ffmpeg";
        public const string StatusVideoGenerating        = "Generating video with ffmpeg...";
        public const string StatusVideoGenerated         = "Video generated: {0}";
        public const string StatusVideoGenerationError   = "Video generation failed: {0}";

        // Folder loading
        public const string StatusFolderLoading          = "Scanning '{0}'...";
        public const string StatusNoPhotosFound          = "No photos found in the selected folder.";
        public const string StatusFolderFromSavedList    = "Loaded {0:N0} photos from the list saved for '{1}'. Checking the folder for changes...";
        public const string StatusFolderChecked          = "Loaded {0:N0} photos from '{1}'. Checked for changes in {2:0.0} s: {3}.";
        public const string StatusFolderNotReachable     = "'{0}' cannot be reached. The list saved at the last visit is shown.";
        public const string StatusFolderCheckError       = "Could not check the folder for changes: {0}";
        public const string CheckNothingChanged          = "nothing changed";
        public const string CheckAdded                   = "{0:N0} added";
        public const string CheckRemoved                 = "{0:N0} removed";
        public const string CheckChanged                 = "{0:N0} changed";
        public const string CheckIncompleteNote          = " {0:N0} folders could not be read and are looked at again next time.";
        public const string DebugTitlePrefix             = "[DEBUG] ";

        // About (Help button)
        public const string AboutTitle                   = "About PhotoLiker";
        public const string AboutMessageFormat           = "PhotoLiker\n\nVersion: {0}\nBuild date: {1:yyyy-MM-dd HH:mm:ss}";

        // Image loading / cache
        public const string ImageLoading                 = "Loading...";
        public const string StatusCacheLoadError         = "Error loading image {0}: {1}";

        // Like / unlike
        public const string StatusFileCopied             = "File '{0}' copied to liked folder.";
        public const string StatusFileSaved              = "File '{0}' saved to '{1}'.";
        public const string StatusFileRemovedFromGroup   = "Removed '{0}' from '{1}'.";
        public const string StatusGroupError             = "Error saving photo to group: {0}";
        public const string StatusLikeCancelled          = "Like cancelled.";
        public const string StatusCreateLikedFolderError = "Error creating liked folder: {0}";
        public const string StatusNoFolderSelected       = "No folder selected for liked photos.";

        // Delete (moves the photo to the Deleted folder)
        public const string StatusPhotoDeleted           = "'{0}' moved to the Deleted folder. In the list now: {1:N0}.";
        public const string StatusDeleteError            = "Could not move the photo to the Deleted folder: {0}";

        // Duplicate file dialog
        public const string DuplicateDialogText          = "File '{0}' already exists in the liked folder. Choose another location?";
        public const string DuplicateDialogTitle         = "File Exists";
        public const string SaveLikedPhotoDialogTitle    = "Save Liked Photo";

        // Copy to the clipboard
        public const string StatusPhotoCopied            = "Photo '{0}' copied to the clipboard.";
        public const string StatusCopyNoPhoto            = "No photo to copy.";
        public const string StatusCopyError              = "Error copying photo: {0}";

        // Zoom / fit
        public const string StatusImageFitted            = "Image fitted to screen.";

        // Keyboard
        public const string StatusKeyPressed             = "Key pressed: {0}";
        public const string StatusMonthShown             = "{0}: photo {1:N0} of {2:N0}.";
        public const string StatusNoNextMonth            = "This is the last month of the list.";
        public const string StatusNoPreviousMonth        = "This is the first month of the list.";

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

        // Folder explorer buttons
        public const string StatusOpenSourceFolder       = "Opened source folder in File Explorer.";
        public const string StatusOpenLikedFolder        = "Opened liked folder in File Explorer.";
        public const string StatusLikedFolderNotSet      = "Liked folder is not set.";
        public const string StatusSourceFolderNotSet     = "Source folder is not set.";

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
