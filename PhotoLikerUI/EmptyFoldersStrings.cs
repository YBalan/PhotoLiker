using System.Runtime.CompilerServices;
namespace PhotoLikerUI
{
    internal static class EmptyFoldersStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(EmptyFoldersStrings), key);

        // Toolbar button
        public static string ButtonText => T();
        public static string ButtonTooltip => T();
        public static string StatusNoFolder => T();
        public static string StatusDone => T();

        // Dialog
        public static string Title => T();
        public static string Intro => T();

        public static string ColumnFolder => T();
        public static string ColumnContains => T();
        public static string OpenMenu => T();
        public static string OpenPhotoMenu => T();
        public static string ShowPhotoMenu => T();

        // The list of photos that can not be read
        public static string FilterFolders => T();
        public static string FilterPhotos => T();
        public static string ColumnPhoto => T();
        public static string ColumnSize => T();
        public static string ColumnReason => T();
        public static string SizeFormat => T();
        public static string PhotosFoundFormat => T();
        public static string PhotosFoundNone => T();
        public static string PhotosNoIndexNote => T();
        public static string PhotosIndexingNote => T();
        public static string MovePhotosButtonFormat => T();
        public static string MovePhotosNothing => T();
        public static string MovingPhotosFormat => T();
        public static string MovedPhotosFormat => T();
        public static string MovedPhotosStoppedFormat => T();
        public static string Photo => T();
        public static string IndexNotUpdatedFormat => T();

        public static string Looking => T();
        public static string LookingProgressFormat => T();
        public static string LookingStopped => T();
        public static string LookingError => T();
        public static string FoundFormat => T();
        public static string FoundNone => T();
        public static string FoundJunkPart => T();

        public static string ContainsEmpty => T();
        public static string ContainsSubfoldersFormat => T();
        public static string ContainsSeparator => T();

        public static string RemoveButtonFormat => T();
        public static string RemoveButtonNothing => T();
        public static string LookAgainButton => T();
        public static string CheckAllButton => T();
        public static string UncheckAllButton => T();
        public static string StopButton => T();
        public static string CloseButton => T();

        public static string RemovingFormat => T();
        public static string RemovedFormat => T();
        public static string RemovedStoppedFormat => T();
        public static string FailedPart => T();
        public static string FailedFormat => T();

        // A noun in its forms for Plural.Count: "folder|folders"
        public static string EmptyFolder => T();
        public static string Folder => T();
        public static string ThumbnailFile => T();
    }
}
