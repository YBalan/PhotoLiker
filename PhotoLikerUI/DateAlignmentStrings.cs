namespace PhotoLikerUI
{
    internal static class DateAlignmentStrings
    {
        // Toolbar button
        public const string ButtonText                   = "Align dates";
        public const string ButtonTooltip                = "Make the dates of the photo files (created, modified) the date the photo was taken, as the camera wrote it into the photo. Shows what would change before anything is touched.";
        public const string StatusIndexing               = "Stop indexing before aligning dates.";
        public const string StatusDone                   = "Dates aligned: {0:N0} photos changed.";
        public const string StatusUndone                 = "Dates put back: {0:N0} photos.";

        // Dialog
        public const string Title                        = "Align file dates with the date taken";
        public const string Intro                        =
            "Sets the dates of the photo files to the date taken - the date the camera wrote into the photo (EXIF). A copied file gets the day of the copy, " +
            "so without this the file dates say nothing about when a photo was shot. The pictures are not changed. Nothing happens until you press the button at the bottom; " +
            "every run is logged in the photo folder and can be undone.";

        public const string OptionCreated                = "File created date";
        public const string OptionModified               = "File modified date";
        public const string OptionExif                   = "Dates inside the photo too (Date Time Digitized and Date Last Saved follow Date Taken (Original))";

        public const string FilterChange                 = "Will change ({0:N0})";
        public const string FilterAligned                = "Already match ({0:N0})";
        public const string FilterNoDate                 = "No usable EXIF date ({0:N0})";
        public const string FilterUnreadable             = "Unreadable ({0:N0})";
        public const string FilterAll                    = "All ({0:N0})";

        public const string ColumnPhoto                  = "Photo";
        public const string ColumnTaken                  = "Date taken (EXIF)";
        public const string ColumnCreated                = "File created";
        public const string ColumnModified               = "File modified";
        public const string ColumnAction                 = "What happens";

        public const string ActionAligned                = "already the date taken";
        public const string ActionNoDate                 = "no believable EXIF date: left as it is";
        public const string ActionLeftOut                = "left out";
        public const string ActionChangeFormat           = "set: {0}";
        public const string ActionCreated                = "created";
        public const string ActionModified               = "modified";
        public const string ActionExifOriginal           = "Date Taken (Original)";
        public const string ActionExifDigitized          = "Date Time Digitized";
        public const string ActionExifModified           = "Date Last Saved";
        public const string ActionFailedFormat           = "failed: {0}";
        public const string TakenNotBelievableFormat     = "{0} (not believable)";
        public const string TakenNone                    = "none";

        public const string LeaveOutMenu                 = "Leave out of this run";
        public const string IncludeMenu                  = "Include again";

        public const string Listing                      = "Looking for photos...";
        public const string ScanProgressFormat           = "Reading dates: {0:N0} / {1:N0}   {2:0}/s   {3} left";
        public const string ScanProgressNoEstimateFormat = "Reading dates: {0:N0} / {1:N0}";
        public const string ScanDoneFormat               = "{0:N0} photos looked at in {1}.";
        public const string ScanStopped                  = "Stopped. Nothing was changed. Press 'Look again' to start over.";
        public const string ScanError                    = "Could not look at the photos: {0}";
        public const string NoPhotos                     = "There are no photos in the folder.";

        public const string SummaryFormat                = "{0:N0} of {1:N0} photos will change{2}.";
        public const string SummaryLeftOutPart           = ", {0:N0} left out";
        public const string SummaryNothing               = "Nothing to change: all {0:N0} photos have the dates asked for.";
        public const string SummaryNothingChecked        = "Tick at least one kind of date.";

        public const string ApplyButtonFormat            = "Change {0:N0} photos";
        public const string ApplyButtonNothing           = "Nothing to change";
        public const string LookAgainButton              = "Look again";
        public const string StopButton                   = "Stop";
        public const string CloseButton                  = "Close";
        public const string UndoButton                   = "Undo last run...";
        public const string UndoTooltip                  = "Put the dates back as they were before the last run (from the log in the photo folder).";

        public const string ApplyProgressFormat          = "Changing dates: {0:N0} / {1:N0}";
        public const string UndoProgressFormat           = "Putting dates back: {0:N0} photos so far";
        public const string IndexNotUpdatedFormat        = " The search index could not be told about it ({0}), so 'Update index' will analyze the changed photos again.";
        public const string ApplyDoneFormat              = "Done: {0:N0} photos changed{1}. The log is {2}.";
        public const string ApplyStoppedFormat           = "Stopped: {0:N0} photos changed{1}. The log is {2}.";
        public const string CopiesPart                   = ", and the same dates on {0:N0} copies in the liked and group folders";
        public const string FailedPart                   = "; {0:N0} could not be changed (they are marked in the list)";
        public const string NotKeptPart                  = "; for {0:N0} the drive did not keep a date that was written (a network drive may not store the created date)";
        public const string ApplyError                   = "The dates could not be changed: {0}";

        public const string UndoQuestionFormat           = "Put the dates back as they were before the run of {0:g}?\n\n{1}\n\nA photo that was changed after that run is left as it is.";
        public const string UndoDoneFormat               = "Put back: {0:N0} photos{1}{2}.";
        public const string UndoSkippedPart              = "; {0:N0} left alone because they were changed since";
        public const string UndoNoLog                    = "There is no run to undo in this folder.";
        public const string UndoError                    = "The dates could not be put back: {0}";

        // Giving a date to photos without a usable EXIF date
        public const string AssignButton                 = "Use a date...";
        public const string AssignTooltip                = "Give the photos that have no usable EXIF date a date taken: the one they have, the one in their folder name, the date of their file, or one you type. They are then changed like the others. (Enter)";
        public const string AssignMenu                   = "Use a date for these photos...";
        public const string ForgetMenu                   = "Forget the date given";
        public const string GivenNote                    = " (given)";
        public const string ActionGivenNote              = " - date given";
        public const string StatusNothingToGive          = "No photo here waits for a date: each has a usable EXIF date, was given one, or could not be read.";
        public const string StatusGiven                  = "Gave {0} a date taken{1}.";
        public const string StatusGivenNonePart          = "; {0} have none from that source and stay as they are";
        public const string StatusForgot                 = "Forgot the date given to {0}.";

        public const string AssignTitle                  = "Use a date for photos without a usable EXIF date";
        public const string AssignIntro                  =
            "These photos have no EXIF date that can be believed: none at all, or one that looks like a camera that lost its clock. Say where their date taken comes from. " +
            "They are then changed like the others: the file dates are set to it, and the dates inside the photo too if that is ticked.";
        public const string AssignScopeSelected          = "The {0:N0} selected";
        public const string AssignScopeAll               = "All {0:N0} photos without a usable date";
        public const string AssignSourceExif             = "The date the photo has: I know it is right";
        public const string AssignSourceFolder           = "The date in the name of its folder (the time of day stays, or is noon)";
        public const string AssignSourceFile             = "The date of the file";
        public const string AssignSourceTyped            = "This date:";
        public const string AssignCountFormat            = "{0:N0} of {1:N0}, for example {2}";
        public const string AssignCountNone              = "none of {0:N0} has one";
        public const string AssignFileWarning            = "A photo that was copied has the day of the copy as its file date, which says nothing about when it was shot.";
        public const string AssignNoPlaceFormat          = "{0:N0} of them have no place for a date inside the photo: only their file dates can be set.";
        public const string AssignOk                     = "Use this date";
        public const string AssignCancel                 = "Cancel";
        public const string PickerFormat                 = "dd MMM yyyy HH:mm:ss";
    }
}
