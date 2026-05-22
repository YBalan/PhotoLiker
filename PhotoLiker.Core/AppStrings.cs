namespace PhotoLiker.Core
{
    public static class AppStrings
    {
        public const string SettingsFileName             = "photo-liker-config.json";
        public const string DefaultLikedFolderName       = "Liked";
        public const string StatusFolderLoaded           = "Loaded {0} photos from '{1}'";
        public const string StatusFolderLoadError        = "Error loading folder: {0}";
        public const string StatusNoPhotosFound          = "No photos found.";
        public const string StatusImageLoadError         = "Error loading image: {0}";
        public const string StatusFileCopied             = "'{0}' copied to liked folder.";
        public const string StatusFileRemoved            = "Removed '{0}' from liked folder.";
        public const string StatusLikeError              = "Error liking photo: {0}";
        public const string StatusUnlikeError            = "Error unliking photo: {0}";
        public const string TitleFormat                  = "PhotoLiker — {0}";
        public const string LikedCheckmark               = "✓";
        public const string GpsMapLinkLabel              = "📍 Maps";
        public const string FolderBrowserSelectPhotos    = "Select a folder containing photos";
        public const string FolderBrowserSelectLiked     = "Select a folder to save liked photos";
    }
}
