namespace PhotoLikerUI
{
    internal static class EmptyFoldersStrings
    {
        // Toolbar button
        public const string ButtonText                   = "Remove empty folders";
        public const string ButtonTooltip                = "Find the folders that hold no photos - empty ones, or with only a thumbnail cache (Thumbs.db) in them - and remove them. Also lists photos that can not be read, to move them out of the way. Shows the lists before anything is touched.";
        public const string StatusNoFolder               = "Open a folder of photos first.";
        public const string StatusDone                   = "Removed {0}.";

        // Dialog
        public const string Title                        = "Remove empty folders";
        public const string Intro                        =
            "These folders hold no photo and nothing else of use: they are empty, or hold only thumbnail files (Thumbs.db, desktop.ini, .DS_Store) " +
            "and other folders like that. Untick a folder to keep it. The thumbnail files go with their folder; anything else is never touched. " +
            "The Deleted and the Liked folders stay. " +
            "The other list is of photos that can not be read: the search index could not decode them (damaged, cut off), or they are empty files. " +
            "Look at them first (double click); the ones you tick are moved to Deleted\\Unreadable, not erased, and are not ticked until you tick them.";

        public const string ColumnFolder                 = "Folder";
        public const string ColumnContains               = "What is in it";
        public const string OpenMenu                     = "Open in File Explorer";
        public const string OpenPhotoMenu                = "Open";
        public const string ShowPhotoMenu                = "Show in File Explorer";

        // The list of photos that can not be read
        public const string FilterFolders                = "Empty folders ({0:N0})";
        public const string FilterPhotos                 = "Unreadable photos ({0:N0})";
        public const string ColumnPhoto                  = "Photo";
        public const string ColumnSize                   = "Size";
        public const string ColumnReason                 = "Why it can not be read";
        public const string SizeFormat                   = "{0:N0} KB";
        public const string PhotosFoundFormat            = "{0} that can not be read.{1}";
        public const string PhotosFoundNone              = "No unreadable photos found.";
        public const string PhotosNoIndexNote            = " Photos that the search index could not decode are listed here too once the folder is indexed (Index photos).";
        public const string PhotosIndexingNote           = " Stop indexing before moving photos.";
        public const string MovePhotosButtonFormat       = "Move {0} to Deleted\\Unreadable";
        public const string MovePhotosNothing            = "Tick photos to move";
        public const string MovingPhotosFormat           = "Moving: {0:N0} / {1:N0}";
        public const string MovedPhotosFormat            = "Moved {0} to Deleted\\Unreadable{1}. Press Look again to find the folders that are empty now.";
        public const string MovedPhotosStoppedFormat     = "Stopped: moved {0} to Deleted\\Unreadable{1}.";
        public const string PhotoOne                     = "photo";
        public const string PhotoMany                    = "photos";
        public const string IndexNotUpdatedFormat        = " The search index could not forget them ({0}); 'Update index' does.";

        public const string Looking                      = "Looking at the folders...";
        public const string LookingProgressFormat        = "Looking at the folders: {0:N0} so far";
        public const string LookingStopped               = "Stopped. Press 'Look again' to start over.";
        public const string LookingError                 = "Could not look at the folders: {0}";
        public const string FoundFormat                  = "{0} found{1}.";
        public const string FoundNone                    = "There are no empty folders.";
        public const string FoundJunkPart                = ", with {0} in them";

        public const string ContainsEmpty                = "nothing";
        public const string ContainsSubfoldersFormat     = "{0}";
        public const string ContainsSeparator            = ", ";

        public const string RemoveButtonFormat           = "Remove {0}";
        public const string RemoveButtonNothing          = "Nothing to remove";
        public const string LookAgainButton              = "Look again";
        public const string CheckAllButton               = "Tick all";
        public const string UncheckAllButton             = "Untick all";
        public const string StopButton                   = "Stop";
        public const string CloseButton                  = "Close";

        public const string RemovingFormat               = "Removing: {0:N0} / {1:N0}";
        public const string RemovedFormat                = "Removed {0} and {1}{2}.";
        public const string RemovedStoppedFormat         = "Stopped: removed {0} and {1}{2}.";
        public const string FailedPart                   = "; {0} could not be removed (marked in the list)";
        public const string FailedFormat                 = "not removed: {0}";

        // Counts with the noun in the right number: "1 folder", "3 folders"
        public const string EmptyFolderOne               = "empty folder";
        public const string EmptyFolderMany              = "empty folders";
        public const string FolderOne                    = "folder";
        public const string FolderMany                   = "folders";
        public const string ThumbnailFileOne             = "thumbnail file";
        public const string ThumbnailFileMany            = "thumbnail files";

        public static string Count(int number, string one, string many) => Plural.Count(number, one, many);
    }
}
