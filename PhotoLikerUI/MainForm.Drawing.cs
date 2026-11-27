using PhotoLiker.Core;
using PhotoLiker.Effects.Models;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Draw button of the toolbar and Ctrl+Shift+D: opens the window to draw on the photo that is shown (see <see cref="DrawingDialog"/>), and when the window wrote over the photo, the lists, the
    /// copies of the photo and the screen follow (the photo has another size now, so its copies by size and date have another name, see <see cref="FollowSavedPhotoOnDisk"/>). The Make a book window
    /// opens the same window for any of its photos (<see cref="DrawPhoto"/>).
    /// </summary>
    public partial class MainForm
    {
        private ToolStripButton _drawButton = null!;

        private void InitializeDrawing()
        {
            _drawButton = new ToolStripButton(DrawingStrings.ButtonText)
            {
                Name = "drawToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Drawing(),
                ToolTipText = DrawingStrings.ButtonTooltip,
            };
            _drawButton.Click += (_, _) => DrawCurrentPhoto();

            // The very last button of the toolbar, after Telegram sticker
            toolStrip1.Items.Add(_drawButton);
        }

        private void ApplyDrawingLanguage()
        {
            _drawButton.Text = DrawingStrings.ButtonText;
            _drawButton.ToolTipText = DrawingStrings.ButtonTooltip;
        }

        /// <summary>Ctrl+Shift+D (without Alt): the plain letters are the group keys, Ctrl+D is the date.</summary>
        private static bool IsDrawKey(Keys keyData) => keyData == (Keys.Control | Keys.Shift | Keys.D);

        /// <summary>Opens the Draw window for the photo that is shown.</summary>
        private void DrawCurrentPhoto()
        {
            if (pictureBox1.Tag is not string file || !File.Exists(file))
            {
                SetStatus(DrawingStrings.StatusNoPhoto);
                return;
            }

            DrawPhoto(file, this);
        }

        /// <summary>
        /// Opens the Draw window for a photo (a modal window of <paramref name="owner"/>), and follows up when it was written over. False when the photo was not changed (the window was not opened, was closed with
        /// no save, or only saved a copy).
        /// </summary>
        internal bool DrawPhoto(string file, IWin32Window owner)
        {
            if (MediaKinds.IsVideo(file))
            {
                SetStatus(DrawingStrings.StatusIsVideo);
                return false;
            }

            if (RefuseModel(file))
                return false;

            _drawButton.Enabled = false;
            try
            {
                SetStatus(DrawingStrings.StatusOpening);
                using var dialog = CreateDrawingDialog(file);
                dialog.ShowDialog(owner);

                if (dialog.PhotoWasChanged)
                    FollowDrawnPhoto(file, dialog.LosslessTurns);
                if (!string.IsNullOrWhiteSpace(dialog.SavedPath) && File.Exists(dialog.SavedPath))
                    SetStatus(string.Format(DrawingStrings.StatusSavedFormat, dialog.SavedPath));
                return dialog.PhotoWasChanged;
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(DrawingStrings.StatusCannotReadFormat, ex.Message));
                MessageBox.Show(owner, ex.Message, DrawingStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                _drawButton.Enabled = true;
            }
        }

        /// <summary>Makes the window for a photo; a test makes it without showing it. The models that scan text are asked for (and downloaded) when the window first needs them.</summary>
        internal DrawingDialog CreateDrawingDialog(string photoPath) => new(photoPath, _currentConfig.IsDarkTheme, _effects, EnsureScanTextModelsAsync);

        /// <summary>
        /// The two models of Scan text (the one that finds the lines, the one that reads them). The person is asked once, for the first that is not there; a yes is a yes for the other as well.
        /// </summary>
        private async Task<bool> EnsureScanTextModelsAsync()
        {
            var finderMissing = !ToolModels.OcrFinder.IsPresentIn(_effects.ModelsFolder);
            if (!await EnsureToolModelAsync(ToolModels.OcrFinder, ToolModelStrings.ScanTextFeature, askAgain: true))
                return false;

            return finderMissing
                ? await EnsureApprovedToolModelAsync(ToolModels.OcrReader, ToolModelStrings.ScanTextFeature)
                : await EnsureToolModelAsync(ToolModels.OcrReader, ToolModelStrings.ScanTextFeature, askAgain: true);
        }

        /// <summary>
        /// A photo of the list was written over by the Draw window (its dates are the ones it had, its size is another): the copies that are named by size and date get the new name, and the list and the screen
        /// show the new file. The search index is left as it is: it knows the photo by the old size, so "Update index" analyzes the changed picture again.
        /// </summary>
        private void FollowDrawnPhoto(string file, int? losslessTurns)
        {
            var files = _currentConfig.Files;
            var photo = files.FirstOrDefault(candidate => string.Equals(candidate.OriginalFilePath, file, StringComparison.OrdinalIgnoreCase));
            if (photo is null)
            {
                _photoCache.Reload(file); // a photo that is not in the list (a copy the book window shows) is only shown again if it is on the screen
                return;
            }

            try
            {
                var info = new FileInfo(file);
                var saved = new ExifSaveResult(Orientation: 1, info.Length, Rewritten: true, DateKept: true, info.CreationTimeUtc, info.LastWriteTimeUtc);
                // A photo that was only turned is still the picture the index analyzed: its faces are turned with it, as for the Rotate buttons. Any other change makes it another picture.
                var searchIndex = losslessTurns is null ? null : _folderSearch?.Index;
                var (renamed, notRenamed) = FollowSavedPhotoOnDisk(Snapshot(photo), saved, losslessTurns ?? 0, CopiesOfPhoto(photo), searchIndex);
                FollowSavedPhotoOnScreen(photo, saved, renamed, files);
                if (notRenamed > 0)
                    SetStatus(string.Format(MainFormStrings.StatusRotateCopiesNotRenamed, notRenamed));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(DrawingStrings.StatusSaveFailedFormat, ex.Message));
            }
        }
    }
}
