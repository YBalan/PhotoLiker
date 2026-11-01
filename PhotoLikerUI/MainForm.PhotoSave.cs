using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// What follows when the app saves a photo's file again (it is turned, or given a date). The app knows a photo by its size and its date
    /// of change, and a copy in a Liked or group folder carries the id made from them (see <see cref="CopyName"/>): so when either changes,
    /// the copies are renamed, and the saved list of the folder, the search index and the photo shown follow.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>The copies of the photo in the Liked folder and in the group folders.</summary>
        private List<string> CopiesOfPhoto(PhotoFile photo)
        {
            var copies = new List<string>();
            if (!string.IsNullOrEmpty(photo.LikedFilePath))
                copies.Add(photo.LikedFilePath);
            if (_groups is { } groups)
                copies.AddRange(groups.CopiesOf(photo));
            return copies;
        }

        /// <summary>What the photo was like before it was saved: the size and the dates the list knew it by.</summary>
        private static PhotoFile Snapshot(PhotoFile photo) => new(photo.OriginalFilePath, string.Empty)
        {
            Length = photo.Length,
            LastWriteTimeUtc = photo.LastWriteTimeUtc,
            CreationTimeUtc = photo.CreationTimeUtc,
        };

        /// <summary>
        /// The work on the disk and in the index after a photo was saved: its copies get its dates and the name of its new id, and the
        /// index takes over the new size and date (the picture is the one that was analyzed) and turns the boxes of the faces.
        /// Safe to call from any thread.
        /// </summary>
        /// <param name="before">The photo as the list knew it.</param>
        /// <param name="quarterTurns">How far the photo was turned clockwise by the save (0 when it was not).</param>
        private static (List<(string Old, string New)> Renamed, int NotRenamed) FollowSavedPhotoOnDisk(
            PhotoFile before, ExifSaveResult saved, int quarterTurns, IReadOnlyList<string> copies, PhotoIndex? searchIndex)
        {
            var renamed = new List<(string Old, string New)>();
            var notRenamed = 0;
            var now = new PhotoFile(before.OriginalFilePath, string.Empty)
            {
                Length = saved.Length,
                LastWriteTimeUtc = saved.LastWriteUtc,
                CreationTimeUtc = saved.CreatedUtc,
            };

            if (CopyName.IdOf(now) != CopyName.IdOf(before))
            {
                // A photo that got its orientation or a date added is a few bytes larger, one that got a date has another date of change:
                // it has another id, and so do its copies. They get the dates of the photo, as the Align dates tool does, since a copy
                // that is not found by its id is found by its size and date of change.
                var modifiedChanged = !DateAlignment.SameMoment(saved.LastWriteUtc, before.LastWriteTimeUtc);
                var createdChanged = !DateAlignment.SameMoment(saved.CreatedUtc, before.CreationTimeUtc);
                foreach (var copy in copies)
                {
                    try
                    {
                        if (createdChanged)
                            File.SetCreationTimeUtc(copy, saved.CreatedUtc);
                        if (modifiedChanged)
                            File.SetLastWriteTimeUtc(copy, saved.LastWriteUtc);
                        if (LikedLibrary.RenameCopy(copy, now) is { } moved)
                            renamed.Add((copy, moved));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        notRenamed++;
                    }
                }
            }

            try
            {
                searchIndex?.Turn(before.OriginalFilePath, quarterTurns, before.Length, saved.Length);
                searchIndex?.UpdateStamps([(before.OriginalFilePath, before.LastWriteTimeUtc.Ticks, saved.LastWriteUtc.Ticks)]);
                searchIndex?.RefreshDate(before.OriginalFilePath, saved.CreatedUtc, saved.LastWriteUtc, DateTime.Now);
            }
            catch
            {
                // The faces of this photo are drawn in the old place until the next "Update index"; the photo itself is saved
            }

            return (renamed, notRenamed);
        }

        /// <summary>
        /// The lists and the screen follow a photo that was saved (UI thread). Returns false when another folder was opened meanwhile: the
        /// list is gone, the file is saved and that is all.
        /// </summary>
        private bool FollowSavedPhotoOnScreen(
            PhotoFile photo, ExifSaveResult saved, List<(string Old, string New)> renamed, IReadOnlyList<PhotoFile> filesAtStart)
        {
            if (!ReferenceEquals(filesAtStart, _currentConfig.Files))
                return false;

            // What the disk says now, which is what a listing of the folder would say
            var file = photo.OriginalFilePath;
            photo.Length = saved.Length;
            photo.LastWriteTimeUtc = saved.LastWriteUtc;
            photo.CreationTimeUtc = saved.CreatedUtc;
            foreach (var (old, moved) in renamed)
            {
                if (string.Equals(old, photo.LikedFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    photo.LikedFilePath = moved;
                    _liked?.Remove(old);
                    _liked?.Add(moved, photo);
                }
                else
                {
                    _groups?.Replace(old, moved, photo);
                }
            }
            _listing?.Update(file, saved.Length, saved.LastWriteUtc, saved.CreatedUtc);

            // What is cached of the photo shows it as it was: read it again, from the file, so what is on screen is what was saved
            _photoCache.Reload(file);
            if (string.Equals(pictureBox1.Tag as string, file, StringComparison.Ordinal))
            {
                _currentConfig.LikedFile = photo.LikedFilePath;
                ShowLoading();
                UpdateFaces(); // the boxes of the faces were turned in the index
                RefreshLikeState();
            }
            return true;
        }
    }
}
