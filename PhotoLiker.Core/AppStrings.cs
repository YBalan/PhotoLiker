using System.Runtime.CompilerServices;
namespace PhotoLiker.Core
{
    public static class AppStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(AppStrings), key);

        public const string SettingsFileName             = "photo-liker-config.json";
        public const string DefaultLikedFolderName       = "Liked";
        public const string DeletedFolderName            = "Deleted";
        public static string StatusFolderLoaded => T();
        public static string StatusFolderLoadError => T();
        public static string StatusNoPhotosFound => T();
        public static string StatusImageLoadError => T();
        public static string StatusFileCopied => T();
        public static string StatusFileRemoved => T();
        public static string StatusLikeError => T();
        public static string StatusUnlikeError => T();
        public const string TitleFormat                  = "PhotoLiker — {0}";
        public const string LikedCheckmark               = "✓";
        public static string GpsMapLinkLabel => T();
        public static string FolderBrowserSelectPhotos => T();
        public static string FolderBrowserSelectLiked => T();
    }
}
