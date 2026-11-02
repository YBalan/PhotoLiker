using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class AboutStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(AboutStrings), key);

        public static string Heading => T();
        public static string VersionFormat => T();
        public static string ShortcutsHeading => T();

        // The second button is in Ukrainian on purpose: it is the one a Ukrainian reader looks for
        public static string ManualEnglishButton => T();
        public static string ManualUkrainianButton => T();
        public static string OkButton => T();

        public static string ManualNotFoundFormat => T();

        /// <summary>The keys and mouse moves of the main window, with what they do. Keep it in step with "Keyboard and mouse" in the README.</summary>
        public static (string Input, string Action)[] Shortcuts =>
            [.. ShortcutIds.Select(id => (T($"Shortcut{id}Input"), T($"Shortcut{id}Action")))];

        // The texts of a shortcut are the resources Shortcut<id>Input and Shortcut<id>Action
        private static readonly string[] ShortcutIds =
        [
            "Move", "Ends", "Month", "Like", "Delete", "Group", "GroupAll", "Fit", "About", "ManualUk", "ManualEn", "Relist", "Copy",
            "Rotate", "Zoom", "Close", "Search", "Face", "Thumbnail",
        ];
    }
}
