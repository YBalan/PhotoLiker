namespace PhotoLikerUI
{
    /// <summary>Alt + a group key: saving all the photos of the search results into the group.</summary>
    internal static class GroupCopyStrings
    {
        public const string ConfirmTitle                 = "Save the results in a group";
        public const string ConfirmFormat                =
            "Copy {0} to the group folder '{1}'?{4}\n\nThe copies take {2} of disk space in the Liked folder.{3}\n\n" +
            "The program is blocked while it copies. You can cancel; the copies made by then stay.";
        public const string NewFolderNote                = "\n\nThat folder does not exist yet: it is made, named after the search.";
        public const string AlreadyThereFormat           = "\n\n{0} of the {1} results are in that folder already and are left as they are.";
        public const string NothingToCopyFormat          = "All {0} are in the group folder '{1}' already: nothing to copy.";

        public const string ProgressTitleFormat          = "Copying to '{0}'";
        public const string ProgressFormat               = "{0:N0} of {1:N0} photos, {2} of {3}";
        public const string ProgressStarting             = "Starting...";
        public const string CancelButton                 = "Cancel";
        public const string Stopping                     = "Stopping after this photo...";

        public const string StatusNoResults              = "Alt+{0} saves all the photos of the search results in a group: search first, then press it.";
        public const string StatusBusy                   = "Wait until the photo that is being copied is done.";
        public const string StatusDoneFormat             = "Saved {0} ({1}) in '{2}'.{3}";
        public const string StatusDoneNothing            = "Nothing was copied to '{0}'.{1}";
        public const string NoteSkippedFormat            = " {0:N0} were there already.";
        public const string NoteCancelled                = " Stopped before the end; the copies made stay.";
        public const string NoteFailedFormat             = " {0:N0} could not be copied.";
        public const string StatusError                  = "Could not save the results in the group: {0}";

        public const string FailuresTitle                = "Some photos could not be copied";
        public const string FailuresFormat               = "{0}\n\n{1}{2}";
        public const string GaveUpFormat                 = "Stopped after {0} photos in a row could not be copied (is the disk full, or the drive gone?).";
        public const string FailedFormat                 = "{0:N0} photos could not be copied.";
        public const string FailureLineFormat            = "{0}: {1}";
        public const string MoreFailuresFormat           = "\n... and {0:N0} more.";

        public const string One                          = "photo";
        public const string Many                         = "photos";

        /// <summary>Megabytes, as asked for; gigabytes too once they are that many.</summary>
        public static string FormatSize(long bytes)
        {
            var megabytes = bytes / 1048576.0;
            return megabytes >= 1024 ? $"{megabytes:N1} MB ({megabytes / 1024:N1} GB)" : $"{megabytes:N1} MB";
        }
    }
}
