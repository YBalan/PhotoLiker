using System.Runtime.CompilerServices;

namespace PhotoLiker.PhotoBook
{
    /// <summary>Texts of the book library that reach the person: what the file says about itself (the contents of an EPUB, the name of a book without a title) and why a book could not be made.</summary>
    internal static class PhotoBookStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(PhotoBookStrings), key);

        public static string NoFont => T();
        public static string UntitledBook => T();
        public static string PhotoNumberFormat => T();
        public static string Contents => T();
    }
}
