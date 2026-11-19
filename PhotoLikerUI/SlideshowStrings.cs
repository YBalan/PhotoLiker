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
        public static string CaptionsOn => T();
        public static string CaptionsOff => T();
        public static string NoCaptionsToShow => T();
        public static string LookFormat => T();
        public static string BorderFormat => T();
        public static string CollageFormat => T();
        public static string CollageNotPossible => T();
        public static string LookFileDialogTitle => T();
        public static string LookFileFilter => T();
        public static string StatusLookFileFailedFormat => T();

        // The indexing that runs behind the show (see SlideshowIndexing)
        public static string IndexingPreparing => T();
        public static string IndexingCountFormat => T();
        public static string IndexingPercentFormat => T();
        public static string IndexingDownloadFormat => T();
        public static string IndexingLeftFormat => T();
        public static string IndexingOver => T();

        // Status bar of the main window
        public static string StatusNoPhotos => T();
        public static string StatusEndedFormat => T();
    }
}
