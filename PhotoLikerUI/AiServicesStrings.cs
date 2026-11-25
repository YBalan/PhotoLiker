using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>The texts of the AI services window (the keys of ChatGPT, Gemini and Claude). A name ending in Format is a format string; a name that begins with Hint is the tip of the control it is named after.</summary>
    internal static class AiServicesStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(AiServicesStrings), key);

        public static string Title => T();
        public static string Intro => T();
        public static string Privacy => T();
        public static string LabelKey => T();
        public static string ShowKey => T();
        public static string LabelModel => T();
        public static string CheckKey => T();
        public static string HintCheckKey => T();
        public static string GetKey => T();
        public static string HintGetKey => T();
        public static string RemoveKey => T();
        public static string HintRemoveKey => T();
        public static string Ok => T();
        public static string Cancel => T();

        // What the window says about a key
        public static string StatusChecking => T();
        public static string StatusCheckedFormat => T();
        public static string StatusCheckFailedFormat => T();
        public static string StatusNoKey => T();
        public static string StatusKeyBad => T();
        public static string StatusModelBad => T();
        public static string StatusRemoved => T();
        public static string SaveFailedFormat => T();
    }
}
