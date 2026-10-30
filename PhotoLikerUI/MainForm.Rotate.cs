using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Rotate button at the right end of the status bar: turns the photo that is shown a quarter clockwise and saves that in its
    /// file. A JPEG is turned by its EXIF orientation (see <see cref="JpegOrientation"/>), so nothing is decoded or written again.
    /// </summary>
    public partial class MainForm
    {
        private ToolStripButton _rotateButton = null!;

        private void InitializeRotate()
        {
            _rotateButton = new ToolStripButton(MainFormStrings.RotateButtonText)
            {
                Name = "rotateToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.RotateClockwise(),
                ToolTipText = MainFormStrings.RotateButtonTooltip,
            };
            _rotateButton.Click += (_, _) => RotateCurrentPhoto();

            // Behind the label that takes the space between, so it sits in the right corner
            statusStrip1.Items.Add(_rotateButton);
        }

        private void RotateCurrentPhoto()
        {
            if (pictureBox1.Tag is not string file)
            {
                SetStatus(MainFormStrings.StatusRotateNoPhoto);
                return;
            }

            _ = RotateAsync(file);
            FocusViewer(); // the button took the focus; the arrows and Space belong to the photo
        }

        /// <summary>Same rules as <see cref="ToggleLikeAsync"/>: the file work runs in the background, and a second press while one is running is ignored.</summary>
        private Task RotateAsync(string currentFile) => RunFileOperationAsync(currentFile, RotatePhotoAsync);

        private async Task RotatePhotoAsync(PhotoFile photo)
        {
            var file = photo.OriginalFilePath;
            var name = Path.GetFileName(file);
            if (!JpegOrientation.IsJpeg(file))
            {
                SetStatus(string.Format(MainFormStrings.StatusRotateOnlyJpeg, name));
                return;
            }

            var files = _currentConfig.Files;
            var searchIndex = _folderSearch?.Index;
            var previousLength = photo.Length;
            var lastWriteUtc = photo.LastWriteTimeUtc;

            // The copies in the liked folders carry the id of the photo, which follows from its size
            var copies = new List<string>();
            if (!string.IsNullOrEmpty(photo.LikedFilePath))
                copies.Add(photo.LikedFilePath);
            if (_groups is { } groups)
                copies.AddRange(groups.CopiesOf(photo));

            SetStatus(string.Format(MainFormStrings.StatusRotating, name));
            TurnResult turned;
            List<(string Old, string New)> renamed;
            int notRenamed;
            try
            {
                // On a network share this takes a moment
                (turned, renamed, notRenamed) = await Task.Run(() => TurnPhoto(file, previousLength, lastWriteUtc, copies, searchIndex));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetStatus(string.Format(MainFormStrings.StatusRotateError, name, ex.Message));
                return;
            }

            var message = string.Format(MainFormStrings.StatusPhotoRotated, name)
                + (turned.Rewritten ? MainFormStrings.StatusRotateAdded : string.Empty)
                + (turned.DateKept ? string.Empty : MainFormStrings.StatusRotateDateNotKept)
                + (notRenamed > 0 ? string.Format(MainFormStrings.StatusRotateCopiesNotRenamed, notRenamed) : string.Empty);

            // Another folder was opened while the photo was turned: the list is gone, the file is turned and that is all
            if (!ReferenceEquals(files, _currentConfig.Files))
            {
                SetStatus(message);
                return;
            }

            photo.Length = turned.Length;
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
            _listing?.Resize(file, turned.Length);

            // What is cached of the photo shows it as it was: read it again, from the file, so what is on screen is what was saved
            _photoCache.Reload(file);
            if (string.Equals(pictureBox1.Tag as string, file, StringComparison.Ordinal))
            {
                _currentConfig.LikedFile = photo.LikedFilePath;
                ShowLoading();
                UpdateFaces(); // the boxes of the faces were turned in the index
                RefreshLikeState();
            }

            SetStatus(message);
        }

        /// <summary>The file work of a turn: the photo, the copies that go by its size, and the search index. Safe to call from any thread.</summary>
        private static (TurnResult Result, List<(string Old, string New)> Renamed, int NotRenamed) TurnPhoto(
            string file, long previousLength, DateTime lastWriteUtc, IReadOnlyList<string> copies, PhotoIndex? searchIndex)
        {
            var turned = JpegOrientation.TurnClockwise(file);

            var renamed = new List<(string Old, string New)>();
            var notRenamed = 0;
            if (turned.Length != previousLength)
            {
                // A photo that got its orientation added is a few bytes larger: it has another id, and so do its copies
                var now = new PhotoFile(file, string.Empty) { Length = turned.Length, LastWriteTimeUtc = lastWriteUtc };
                foreach (var copy in copies)
                {
                    try
                    {
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
                searchIndex?.Turn(file, 1, previousLength, turned.Length);
            }
            catch
            {
                // The faces of this photo are drawn in the old place until the next "Update index"; the photo itself is turned
            }

            return (turned, renamed, notRenamed);
        }
    }
}
