using System.Runtime.CompilerServices;

namespace PhotoLikerUI
{
    /// <summary>Texts of the small things: the editors of the grids, the error box, the shell calls, the Language row.</summary>
    internal static class DialogStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(DialogStrings), key);

        public static string ContextMenuEntry => T();
        public static string FolderFallback => T();
        public static string ValueFallback => T();
        public static string CopyAll => T();
        public static string CrashDetailsFormat => T();
        public static string OpenErrorFormat => T();
        public static string OpenMenu => T();
        public static string ShowMenu => T();
        public static string NotThere => T();
        public static string AudioFileNotThere => T();
        public static string LanguageReportFormat => T();
        public static string LanguageMissing => T();
        public static string LanguageUnknown => T();
        public static string LanguageWrong => T();
        public static string LanguageFileProblem => T();
    }
}
