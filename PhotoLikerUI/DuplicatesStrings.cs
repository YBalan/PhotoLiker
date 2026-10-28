namespace PhotoLikerUI
{
    /// <summary>A number with its noun in the right number: "1 folder", "3 folders".</summary>
    internal static class Plural
    {
        public static string Count(int number, string one, string many) => number == 1 ? $"1 {one}" : $"{number:N0} {many}";
    }

    internal static class DuplicatesStrings
    {
        // Toolbar button
        public const string ButtonText                   = "Find duplicates";
        public const string ButtonTooltip                = "Find photos that are the same file in different folders, keep the one in the best folder and move the other copies to Deleted\\Duplicated. Shows what would move before anything is touched.";
        public const string StatusIndexing               = "Stop indexing before moving duplicates.";
        public const string StatusDone                   = "Moved {0} to Deleted\\Duplicated.";

        // Dialog
        public const string Title                        = "Find duplicates";
        public const string Intro                        =
            "Finds photos that show exactly the same picture in different folders; neither the name nor what is written around the picture (EXIF, XMP, ratings, keywords) matters. In each group the copy in the best folder stays: " +
            "a folder named for a date, or with a name of its own, beats a camera's folder (107_2311, 100CANON), and anything inside a Deleted folder loses. " +
            "The other copies are moved to Deleted\\Duplicated with their folder path, so nothing is lost. Photos in the Liked and group folders are copies on purpose and are left out.";

        public const string OptionWhole                  = "Compare all of the picture, not three samples (much slower, no doubt left)";
        public const string OptionNames                  = "Also look at photos that only share their name with another (finds copies whose metadata has another size; reads more)";
        public const string KeepLabel                    = "Always keep copies in folders with:";
        public const string MoveLabel                    = "Always move copies out of folders with:";
        public const string PatternHint                  = "parts of the path, separated by ;  (* and ? stand for any characters)";

        public const string FilterMove                   = "Will move ({0})";
        public const string FilterChoose                 = "Need your choice ({0})";
        public const string FilterLeft                   = "Left alone ({0})";
        public const string FilterAll                    = "All groups ({0})";

        public const string ColumnGroup                  = "Group";
        public const string ColumnAction                 = "What happens";
        public const string ColumnPhoto                  = "Photo";
        public const string ColumnSize                   = "Size";
        public const string ColumnWhy                    = "Why";

        public const string ActionKeep                   = "stays";
        public const string ActionMove                   = "moves to Deleted\\Duplicated";
        public const string ActionChoose                 = "? your choice";
        public const string ActionLeft                   = "left alone";
        public const string ActionFailedFormat           = "not moved: {0}";
        public const string WhyYourChoice                = "your choice";
        public const string SizeFormat                   = "{0:0.0} MB";

        public const string Listing                      = "Looking for photos...";
        public const string ComparingFormat              = "Comparing: {0:N0} / {1:N0} files, {2:0} MB read, {3:0.0} MB/s, {4} left";
        public const string ComparingNoEstimateFormat    = "Comparing: {0:N0} / {1:N0} files";
        public const string ScanDoneFormat               = "{0} looked at in {1}: {2} compared, {3} of identical photos found.";
        public const string ScanLeftOutPart              = " {0} in Liked and group folders were left out.";
        public const string ScanUnreadablePart           = " {0} could not be read.";
        public const string ScanMetadataPart             = " {0} differ in their metadata (EXIF, XMP): the copy that stays keeps its own.";
        public const string WhyMetadataDiffers           = "; other metadata";
        public const string ScanStopped                  = "Stopped. Nothing was changed. Press 'Look again' to start over.";
        public const string ScanError                    = "Could not look at the photos: {0}";
        public const string NoDuplicates                 = "No duplicates found.";

        public const string SummaryFormat                = "{0} of identical photos: {1} will move to Deleted\\Duplicated ({2:0.0} GB){3}{4}.";
        public const string SummaryChoosePart            = "; {0} waiting for your choice";
        public const string SummaryLeftPart              = "; {0} left alone";

        public const string MoveButtonFormat             = "Move {0}";
        public const string MoveButtonNothing            = "Nothing to move";
        public const string LookAgainButton              = "Look again";
        public const string KeepThisButton               = "Keep this one";
        public const string KeepThisTooltip              = "Select a photo in a group: it stays, the other copies of the group move. (Enter)";
        public const string LeaveAloneButton             = "Leave group alone";
        public const string LeaveAloneTooltip            = "Select a photo: its group is not touched at all, or touched again if it was left alone. (Delete)";
        public const string UndoButton                   = "Undo last run...";
        public const string UndoTooltip                  = "Move the copies of the last run back to where they were (from the log in the photo folder).";
        public const string StopButton                   = "Stop";
        public const string CloseButton                  = "Close";
        public const string KeepMenu                     = "Keep this one, move the others";
        public const string LeaveAloneMenu               = "Leave this group alone / touch it again";

        public const string MovingFormat                 = "Moving: {0:N0} / {1:N0}";
        public const string MovedFormat                  = "Done: {0} moved to Deleted\\Duplicated ({1:0.0} GB){2}. The log is {3}.";
        public const string MovedStoppedFormat           = "Stopped: {0} moved to Deleted\\Duplicated ({1:0.0} GB){2}. The log is {3}.";
        public const string FailedPart                   = "; {0} could not be moved (marked in the list)";
        public const string MoveError                    = "The copies could not be moved: {0}";
        public const string IndexNotUpdatedFormat        = " The search index could not be told about it ({0}); 'Update index' forgets them.";

        public const string UndoQuestionFormat           = "Move the copies of the run of {0:g} back to where they were?\n\n{1}\n\nA copy whose old place has a photo in it by now stays in Deleted\\Duplicated.";
        public const string UndoProgressFormat           = "Putting copies back: {0:N0} so far";
        public const string UndoDoneFormat               = "Put back: {0}{1}.";
        public const string UndoSkippedPart              = "; {0} left in Deleted\\Duplicated (their place is taken, or they are gone)";
        public const string UndoReindexPart              = " 'Update index' adds them to the search index again.";
        public const string UndoNoLog                    = "There is no run to undo in this folder.";
        public const string UndoError                    = "The copies could not be put back: {0}";

        // Counts with the noun in the right number
        public const string PhotoOne                     = "photo";
        public const string PhotoMany                    = "photos";
        public const string GroupOne                     = "group";
        public const string GroupMany                    = "groups";
    }
}
