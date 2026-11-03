using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class SlideshowStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(SlideshowStrings), key);

        // On the screen of the slideshow
        public static string Loading => T();
        public static string NothingToShow => T();

        // Status bar of the main window
        public static string StatusNoPhotos => T();
        public static string StatusEndedFormat => T();
    }
}
