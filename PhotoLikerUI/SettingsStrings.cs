namespace PhotoLikerUI
{
    /// <summary>What each row of the settings grid means: shown in the help pane under the grid when the row is selected.</summary>
    internal static class SettingsStrings
    {
        // 1. Progress
        public const string TotalFiles =
            "How many photos are in the list now: the photos of the open folder (and of its subfolders, if GoThroughSubFolders is on), or the search results while they are shown.";
        public const string CurrentIndex =
            "The place of the shown photo in the list, counted from 0. Type another number to jump to that photo (a number outside the list goes to the nearest end). " +
            "Home and End jump to the first and the last photo, PageUp and PageDown to the previous and the next month.";

        // 2. Current File
        public const string CurrentFolder = "The photo folder that is open. Choose another one with the Open button of the toolbar.";
        public const string CurrentFilePath = "The full path of the shown photo.";
        public const string CurrentFileName = "The name of the shown photo, without its folder.";
        public const string LikedFile =
            "Where the copy of the shown photo is in the Liked folder; empty if it is not liked. The Space key likes a photo (copies it there) or takes the copy away again.";
        public const string SavedInFolders =
            "Every folder that holds a copy of the shown photo: the Liked folder and the group folders (keys 1-9 and A-Z). Expand the row to see one folder per line.";

        // 3. Settings
        public const string LikedFolder =
            "Where liked photos are copied to; the group folders ('1 - Group' ... 'Z - Group') are made inside it. By default the folder 'Liked' inside the photo folder. " +
            "Click the '...' button to choose another folder; a folder with any other name than 'Liked' is kept as it is, wherever it is.";
        public const string GoThroughSubFolders =
            "True: the photos of all subfolders are listed too (the Liked folder itself is left out). False: only the photos of the folder itself. Changing it lists the folder again at once.";
        public const string Extensions =
            "The file extensions that count as photos, separated by ; or , each with its dot: .jpg; .png. Case does not matter. Changing it lists the folder again at once.";
        public const string CacheSize =
            "How many photos on each side of the shown one are kept decoded in memory, so that the arrow keys show them at once. A larger number browses faster over a slow network but uses more memory.";
        public const string ThumbnailSize =
            "The size of the thumbnails in the strip at the right, in pixels. Ctrl + mouse wheel over the strip changes it.";
    }
}
