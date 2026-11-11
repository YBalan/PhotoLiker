using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// The date rows of the metadata grid can be edited (typed, or picked with the "..." button): the photo is saved again with the new
    /// date, see <see cref="DateRowEdit"/> for what each row changes. The same follows as for a turn (<see cref="FollowSavedPhotoOnDisk"/>).
    /// The one row of a video that can be edited is "Recorded" (<see cref="EditVideoDateAsync"/>).
    /// </summary>
    public partial class MainForm
    {
        /// <summary>Whether the window is dark, for the dialogs the grid opens.</summary>
        internal bool IsDarkTheme => _currentConfig.IsDarkTheme;

        /// <summary>Ctrl+D without Alt or Shift (the plain letters are the group keys).</summary>
        private static bool IsEditDateKey(Keys keyData) => keyData == (Keys.Control | Keys.D);

        /// <summary>
        /// Ctrl+D: asks for a date for the photo or video that is shown, in the same dialog as the "..." of the metadata grid, and saves it as typing it into that row would:
        /// the Date Taken (Original) of a JPEG (the dates of the file follow it), the Recorded date of a video. Another kind of file has no date to change here, and the status says so.
        /// Not while the slideshow is on (it is a window of its own and takes the keys; this is only a second guard).
        /// </summary>
        private void EditDateOfShownFile()
        {
            if (_slideshow is not null)
                return;

            if (pictureBox1.Tag is not string file)
            {
                SetStatus(MainFormStrings.StatusDateNoPhoto);
                return;
            }

            var video = MediaKinds.IsVideo(file);
            if (!video && !JpegOrientation.IsJpeg(file))
            {
                SetStatus(string.Format(MainFormStrings.StatusDateOnlyJpegOrVideo, Path.GetFileName(file)));
                return;
            }

            if (_likeInProgress)
            {
                SetStatus(MainFormStrings.StatusDateBusy);
                return;
            }

            var tagId = video ? VideoMetadata.RecordedTagId : DateRowEdit.DateTakenTagId;
            var rowName = video ? VideoMetadata.RecordedName : ExifTagMap.Tags[tagId];

            // Where the grid row starts from: the date the photo has now; a photo with none starts from the date of its file
            var shown = _photoCache.TryGet(file, out var entry) ? entry.Metadata.FirstOrDefault(e => e.TagId == tagId)?.Value : null;
            DateTime start;
            if (!DateText.TryParseTyped(shown, out start) && !DateText.TryFindDisplayed(shown, out start))
            {
                try { start = File.GetLastWriteTime(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { start = DateTime.Now; }
            }

            using var dialog = new DateEditDialog(rowName, start, IsDarkTheme);
            if (dialog.ShowDialog(this) == DialogResult.OK)
                PhotoDateEdited(tagId, dialog.Picked);
            else
                FocusViewer();
        }

        private void PhotoDateEdited(int tagId, DateTime date)
        {
            if (pictureBox1.Tag is not string file)
                return;

            if (_likeInProgress)
            {
                SetStatus(MainFormStrings.StatusDateBusy);
                return;
            }

            _ = RunFileOperationAsync(file, photo => MediaKinds.IsVideo(file) ? EditVideoDateAsync(photo, date) : EditPhotoDateAsync(photo, tagId, date));
            FocusViewer();
        }

        /// <summary>
        /// The "Recorded" row of a video was edited: the date is written into the video where it keeps it (MP4, MOV, 3GP, AVI) and the created and modified dates
        /// of the file become the same moment, see <see cref="VideoDateSave"/>. A video that has no place for a date gets the dates of its file only, and the status says so.
        /// The catalog of the folder is told, so the video stays analyzed and is shown with its new date; the copies follow as for a photo.
        /// </summary>
        private async Task EditVideoDateAsync(PhotoFile photo, DateTime date)
        {
            var file = photo.OriginalFilePath;
            var name = Path.GetFileName(file);
            var files = _currentConfig.Files;
            var searchIndex = _folderSearch?.Index;
            var catalog = VideoLibrary.Current;
            var before = Snapshot(photo);
            var copies = CopiesOfPhoto(photo);

            // A video that plays holds its file open: it could not be written
            await ReleasePlayedVideoAsync(file);

            SetStatus(string.Format(MainFormStrings.StatusDateSaving, name));
            VideoDateSaved saved;
            List<(string Old, string New)> renamed;
            int notRenamed;
            try
            {
                // On a network share this takes a moment
                (saved, renamed, notRenamed) = await Task.Run(() =>
                {
                    var result = VideoDateSave.Save(file, date);
                    var (moved, failed) = FollowSavedPhotoOnDisk(before, OnDisk(result), quarterTurns: 0, copies, searchIndex);

                    // The catalog knows the video by its date of change: without this the video would be taken for another one and lose its moments and its speech
                    catalog?.UpdateStamps([(file, before.LastWriteTimeUtc.Ticks, result.LastWriteUtc.Ticks)]);
                    return (result, moved, failed);
                });

                // The date inside is read again from the file (only that, nothing else the catalog knows of the video is lost)
                if (catalog is not null && saved.WrittenInside)
                    await catalog.RefreshRecordedAsync(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetStatus(string.Format(MainFormStrings.StatusDateError, name, ex.Message));
                return;
            }

            var message = (saved.NoPlaceInside
                    ? string.Format(MainFormStrings.StatusVideoDateNoPlace, name, date)
                    : string.Format(MainFormStrings.StatusDateSetFormat, name, VideoMetadata.RecordedName, date, MainFormStrings.StatusDateAndFileSet))
                + (saved.DatesKept ? string.Empty : MainFormStrings.StatusRotateDateNotKept)
                + (notRenamed > 0 ? string.Format(MainFormStrings.StatusRotateCopiesNotRenamed, notRenamed) : string.Empty);

            FollowSavedPhotoOnScreen(photo, OnDisk(saved), renamed, files);
            SetStatus(message);
        }

        /// <summary>What a saved video is like on the disk, in the form the follow-up of a saved photo takes (a video has no orientation, and is never written anew).</summary>
        private static ExifSaveResult OnDisk(VideoDateSaved saved) =>
            new(Orientation: 1, saved.Length, Rewritten: false, saved.DatesKept, saved.CreatedUtc, saved.LastWriteUtc);

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
