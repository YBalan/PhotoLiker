using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    /// <summary>Alt + a group key: saving all the photos of the search results into the group.</summary>
    internal static class GroupCopyStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(GroupCopyStrings), key);

        public static string ConfirmTitle => T();
        public static string ConfirmFormat => T();
        public static string NewFolderNote => T();
        public static string AlreadyThereFormat => T();
        public static string NothingToCopyFormat => T();

        public static string ProgressTitleFormat => T();
        public static string ProgressFormat => T();
        public static string ProgressStarting => T();
        public static string CancelButton => T();
        public static string Stopping => T();

        public static string StatusNoResults => T();
        public static string StatusBusy => T();
        public static string StatusDoneFormat => T();
        public static string StatusDoneNothing => T();
        public static string NoteSkippedFormat => T();
        public static string NoteCancelled => T();
        public static string NoteFailedFormat => T();
        public static string StatusError => T();

        public static string FailuresTitle => T();
        public static string FailuresFormat => T();
        public static string GaveUpFormat => T();
        public static string FailedFormat => T();
        public static string FailureLineFormat => T();
        public static string MoreFailuresFormat => T();

        public static string Photo => T();

        /// <summary>Megabytes, as asked for; gigabytes too once they are that many.</summary>
        public static string FormatSize(long bytes)
        {
            var megabytes = bytes / 1048576.0;
            return megabytes >= 1024 ? $"{megabytes:N1} MB ({megabytes / 1024:N1} GB)" : $"{megabytes:N1} MB";
        }
    }
}
