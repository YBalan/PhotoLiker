namespace PhotoLiker.Core
{
    /// <summary>
    /// Where the user manual is: one page per language, <c>Manual\manual.en.html</c> and <c>Manual\manual.ua.html</c> in the folder of the
    /// program (the installer puts them there, with the pictures they show in <c>Manual\images</c>).
    /// </summary>
    public static class UserManual
    {
        public const string English = "en";
        public const string Ukrainian = "ua"; // the suffix of the file; the page itself says lang="uk", the code of the language

        public const string FolderName = "Manual";

        /// <summary>The path of the page in that language, or null if the program folder does not have it.</summary>
        public static string? Find(string programFolder, string language)
        {
            var path = Path.Combine(programFolder, FolderName, $"manual.{language}.html");
            return File.Exists(path) ? path : null;
        }
    }
}
