using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class SlideshowStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(SlideshowStrings), key);

        // On the screen of the slideshow
        public static string Loading => T();
        public static string NothingToShow => T();
        public static string EffectFormat => T();
        public static string Paused => T();
        public static string FacesOn => T();
        public static string FacesOff => T();
        public static string NamesOn => T();
        public static string NamesOff => T();
        public static string NoPeopleToShow => T();

        // Status bar of the main window
        public static string StatusNoPhotos => T();
        public static string StatusEndedFormat => T();
    }
}
