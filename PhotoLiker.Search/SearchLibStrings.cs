using PhotoLiker.Core;
using System.Runtime.CompilerServices;

namespace PhotoLiker.Search
{
    /// <summary>Messages of the search library that reach the person.</summary>
    internal static class SearchLibStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(SearchLibStrings), key);

        public static string AnyYear => T();
        public static string ThisPerson => T();
        public static string PhotoNotIndexed => T();
        public static string PhotoGone => T();
        public static string PhotoChangedLookAgain => T();
        public static string NotDateLog => T();
        public static string SharedPeopleDamagedFormat => T();
        public static string DownloadingFormat => T();
        public static string DownloadingNoTotalFormat => T();
    }
}
