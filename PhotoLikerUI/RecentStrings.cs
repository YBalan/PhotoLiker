using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the folders that were opened last: the right-click menu of the Open button, and the heading of the same list in the Jump List of the taskbar button.</summary>
    internal static class RecentStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(RecentStrings), key);

        public static string MenuNone => T();
        public static string MenuClear => T();
        public static string JumpListCategory => T();
        public static string StatusFolderGoneFormat => T();
    }
}
