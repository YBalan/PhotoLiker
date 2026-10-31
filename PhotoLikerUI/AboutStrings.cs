namespace PhotoLikerUI
{
    internal static class AboutStrings
    {
        public const string Heading              = "PhotoLiker";
        public const string VersionFormat        = "Version: {0}\nBuild date: {1:yyyy-MM-dd HH:mm:ss}";
        public const string ShortcutsHeading     = "Keyboard and mouse";

        // The second button is in Ukrainian on purpose: it is the one a Ukrainian reader looks for
        public const string ManualEnglishButton   = "User manual (English)";
        public const string ManualUkrainianButton = "Посібник користувача (українською)";
        public const string OkButton              = "OK";

        public const string ManualNotFoundFormat = "The user manual was not found. It should be in the folder:\n\n{0}\n\nReinstall PhotoLiker to get it back.";

        /// <summary>The keys and mouse moves of the main window, with what they do. Keep it in step with "Keyboard and mouse" in the README.</summary>
        public static readonly (string Input, string Action)[] Shortcuts =
        [
            ("Right / Left",        "Next / previous photo"),
            ("Home / End",          "First / last photo"),
            ("PageDown / PageUp",   "First photo of the next / previous month"),
            ("Space",               "Like the photo (copy it to Liked), or take the copy away"),
            ("Delete",              "Move the photo to the Deleted folder"),
            ("1-9, A-Z",            "Save the photo in a group, or take it out again"),
            ("Alt+1-9, Alt+A-Z",    "Search results: save all of them in the group"),
            ("0",                   "Fit the photo to the viewer"),
            ("F1",                  "This box"),
            ("F2",                  "User manual in Ukrainian"),
            ("F3",                  "User manual in English"),
            ("F5",                  "List the folder again"),
            ("Ctrl+C",              "Copy the photo to the clipboard"),
            ("Ctrl+mouse wheel",    "Zoom the photo (drag to pan); over the strip: thumbnail size"),
            ("Esc",                 "Close the search results"),
            ("Enter",               "In the search box: search"),
            ("Shift+drag",          "Mark a face the program missed (face icon on)"),
            ("Click a thumbnail",   "Go to that photo"),
        ];
    }
}
