using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The two Rotate buttons at the right end of the status bar, and Ctrl + / Ctrl -: turn the photo that is shown a quarter clockwise
    /// or counterclockwise and save that in its file. A JPEG is turned by its EXIF orientation (see <see cref="JpegOrientation"/>), so
    /// nothing is decoded or written again.
    /// </summary>
    public partial class MainForm
    {
        private const int Clockwise = 1;
        private const int CounterClockwise = -1;

        private ToolStripButton _rotateButton = null!;
        private ToolStripButton _rotateBackButton = null!;

        private void InitializeRotate()
        {
            _rotateBackButton = NewRotateButton(
                "rotateCounterClockwiseToolStripButton", MainFormStrings.RotateBackButtonText, MainFormStrings.RotateBackButtonKeys, MainFormStrings.RotateBackButtonTooltip,
                AppIcons.RotateCounterClockwise(), CounterClockwise);
            _rotateButton = NewRotateButton(
                "rotateToolStripButton", MainFormStrings.RotateButtonText, MainFormStrings.RotateButtonKeys, MainFormStrings.RotateButtonTooltip,
                AppIcons.RotateClockwise(), Clockwise);

            // Behind the label that takes the space between, so they sit in the right corner: counterclockwise first, clockwise last
            statusStrip1.Items.Add(_rotateBackButton);
            statusStrip1.Items.Add(_rotateButton);

            statusStrip1.ShowItemToolTips = true; // a status bar shows none by default
        }

        /// <summary>Writes the texts of the two buttons again in the language that is on.</summary>
        private void ApplyRotateLanguage()
        {
            _rotateBackButton.AccessibleName = MainFormStrings.RotateBackButtonText;
            _rotateBackButton.Text = MainFormStrings.RotateBackButtonKeys;
            _rotateBackButton.ToolTipText = MainFormStrings.RotateBackButtonTooltip;
            _rotateButton.AccessibleName = MainFormStrings.RotateButtonText;
            _rotateButton.Text = MainFormStrings.RotateButtonKeys;
            _rotateButton.ToolTipText = MainFormStrings.RotateButtonTooltip;
        }

        private ToolStripButton NewRotateButton(string name, string accessibleName, string keys, string tooltip, Image image, int quarterTurns)
        {
            var button = new ToolStripButton(keys)
            {
                Name = name,
                AccessibleName = accessibleName, // the text is the short key
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Image = image,
                ToolTipText = tooltip,
            };
            button.Click += (_, _) => RotateCurrentPhoto(quarterTurns);
            return button;
        }

        /// <summary>Ctrl + (also Ctrl and the plus of the numpad) turns clockwise, Ctrl - counterclockwise; Shift, which some keyboards need for the plus, is allowed.</summary>
        private static bool TryGetRotateKey(Keys keyData, out int quarterTurns)
        {
            quarterTurns = 0;
            if ((keyData & Keys.Control) == 0 || (keyData & Keys.Alt) != 0)
                return false;

            quarterTurns = (keyData & Keys.KeyCode) switch
            {
                Keys.Oemplus or Keys.Add => Clockwise,
                Keys.OemMinus or Keys.Subtract => CounterClockwise,
                _ => 0,
            };
            return quarterTurns != 0;
        }

        private void RotateCurrentPhoto(int quarterTurns)
        {
            if (pictureBox1.Tag is not string file)
            {
                SetStatus(MainFormStrings.StatusRotateNoPhoto);
                return;
            }

            _ = RotateAsync(file, quarterTurns);
            FocusViewer(); // the button took the focus; the arrows and Space belong to the photo
        }

        /// <summary>Same rules as <see cref="ToggleLikeAsync"/>: the file work runs in the background, and a second press while one is running is ignored.</summary>
        private Task RotateAsync(string currentFile, int quarterTurns) =>
            RunFileOperationAsync(currentFile, photo => RotatePhotoAsync(photo, quarterTurns));

        private async Task RotatePhotoAsync(PhotoFile photo, int quarterTurns)
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
            var before = Snapshot(photo);
            var copies = CopiesOfPhoto(photo);

            SetStatus(string.Format(MainFormStrings.StatusRotating, name));
            ExifSaveResult turned;
            DateAlignment.SavedDates? dates;
            List<(string Old, string New)> renamed;
            int notRenamed;
            try
            {
                // On a network share this takes a moment
                (turned, dates, renamed, notRenamed) = await Task.Run(() => TurnPhoto(before, quarterTurns, copies, searchIndex));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetStatus(string.Format(MainFormStrings.StatusRotateError, name, ex.Message));
                return;
            }

            var message = string.Format(quarterTurns > 0 ? MainFormStrings.StatusPhotoRotated : MainFormStrings.StatusPhotoRotatedBack, name)
                + (turned.Rewritten ? MainFormStrings.StatusRotateAdded : string.Empty)
                + (dates is null ? string.Empty : string.Format(dates.FromFile ? MainFormStrings.StatusRotateTakenFromFile : MainFormStrings.StatusRotateDatesTaken, dates.Taken))
                + (turned.DateKept ? string.Empty : MainFormStrings.StatusRotateDateNotKept)
                + (notRenamed > 0 ? string.Format(MainFormStrings.StatusRotateCopiesNotRenamed, notRenamed) : string.Empty);

            FollowSavedPhotoOnScreen(photo, turned, renamed, files);
            SetStatus(message);
        }

        /// <summary>
        /// The file work of a turn: the photo, the copies that go by its size and date, and the search index. Safe to call from any thread.
        /// The Date Taken (Original) of the photo and the dates of its file become one moment: the date taken the photo has, else the date of
        /// its file, which is then written into the photo as its date taken (see <see cref="DateAlignment.DatesForSaving(string, DateTime)"/>).
        /// A photo that has no date at all to go by keeps the dates it has.
        /// </summary>
        private static (ExifSaveResult Result, DateAlignment.SavedDates? Dates, List<(string Old, string New)> Renamed, int NotRenamed) TurnPhoto(
            PhotoFile before, int quarterTurns, IReadOnlyList<string> copies, PhotoIndex? searchIndex)
        {
            var dates = DateAlignment.DatesForSaving(before.OriginalFilePath, DateTime.Now);
            ExifSaveResult turned;
            try
            {
                turned = JpegOrientation.TurnClockwise(
                    before.OriginalFilePath, quarterTurns, dates?.FileUtc, dates is { WriteTaken: true } ? dates.Taken : null);
            }
            catch (InvalidDataException) when (dates is { WriteTaken: true })
            {
                // The Exif block has no room for the date (or is damaged where the date goes) and the file was not touched: a turn
                // alone may still fit, and is worth more than the date
                dates = null;
                turned = JpegOrientation.TurnClockwise(before.OriginalFilePath, quarterTurns);
            }

            var (renamed, notRenamed) = FollowSavedPhotoOnDisk(before, turned, quarterTurns, copies, searchIndex);
            return (turned, dates, renamed, notRenamed);
        }
    }
}
