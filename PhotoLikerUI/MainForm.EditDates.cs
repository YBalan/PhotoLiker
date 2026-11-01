using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The date rows of the metadata grid can be edited (typed, or picked with the "..." button): the photo is saved again with the new
    /// date, see <see cref="DateRowEdit"/> for what each row changes. The same follows as for a turn (<see cref="FollowSavedPhotoOnDisk"/>).
    /// </summary>
    public partial class MainForm
    {
        /// <summary>Whether the window is dark, for the dialogs the grid opens.</summary>
        internal bool IsDarkTheme => _currentConfig.IsDarkTheme;

        private void PhotoDateEdited(int tagId, DateTime date)
        {
            if (pictureBox1.Tag is not string file)
                return;

            if (_likeInProgress)
            {
                SetStatus(MainFormStrings.StatusDateBusy);
                return;
            }

            _ = RunFileOperationAsync(file, photo => EditPhotoDateAsync(photo, tagId, date));
            FocusViewer();
        }

        private async Task EditPhotoDateAsync(PhotoFile photo, int tagId, DateTime date)
        {
            if (DateRowEdit.For(tagId, date) is not { } edit)
                return;

            var file = photo.OriginalFilePath;
            var name = Path.GetFileName(file);
            var files = _currentConfig.Files;
            var searchIndex = _folderSearch?.Index;
            var before = Snapshot(photo);
            var copies = CopiesOfPhoto(photo);

            SetStatus(string.Format(MainFormStrings.StatusRotating, name));
            ExifSaveResult saved;
            List<(string Old, string New)> renamed;
            int notRenamed;
            try
            {
                // On a network share this takes a moment
                (saved, renamed, notRenamed) = await Task.Run(() =>
                {
                    var result = JpegExif.Save(file, edit.Change, edit.FileDatesUtc);
                    var (moved, failed) = FollowSavedPhotoOnDisk(before, result, quarterTurns: 0, copies, searchIndex);
                    return (result, moved, failed);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetStatus(string.Format(MainFormStrings.StatusDateError, name, ex.Message));
                return;
            }

            var rowName = ExifTagMap.Tags[tagId];
            var message = string.Format(
                    MainFormStrings.StatusDateSetFormat, name, rowName, date,
                    tagId == ExifTagMap.FileDateTagId || edit.FileDatesUtc is null ? string.Empty : MainFormStrings.StatusDateAndFileSet)
                + (saved.DateKept ? string.Empty : MainFormStrings.StatusRotateDateNotKept)
                + (notRenamed > 0 ? string.Format(MainFormStrings.StatusRotateCopiesNotRenamed, notRenamed) : string.Empty);

            FollowSavedPhotoOnScreen(photo, saved, renamed, files);
            SetStatus(message);
        }
    }
}
