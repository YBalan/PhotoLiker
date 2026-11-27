using PhotoLiker.Effects.Models;
using PhotoLiker.Manuscripts;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Scan manuscripts button of the toolbar and Ctrl+Shift+M: opens the window that reads handwritten and printed pages into text (see <see cref="ManuscriptDialog"/>), starting with the photo that
    /// is shown (the photos that are loaded are its pages). When the window turned the photo of a page in its file, the lists, the copies of the photo and the screen follow, as they do for a photo the Draw window wrote over.
    /// </summary>
    public partial class MainForm
    {
        private ToolStripButton _manuscriptButton = null!;

        private void InitializeManuscript()
        {
            _manuscriptButton = new ToolStripButton(ManuscriptStrings.ButtonText)
            {
                Name = "manuscriptToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Manuscript(),
                ToolTipText = ManuscriptStrings.ButtonTooltip,
            };
            _manuscriptButton.Click += (_, _) => OpenManuscripts();

            // The very last button of the toolbar, after Draw
            toolStrip1.Items.Add(_manuscriptButton);
        }

        private void ApplyManuscriptLanguage()
        {
            _manuscriptButton.Text = ManuscriptStrings.ButtonText;
            _manuscriptButton.ToolTipText = ManuscriptStrings.ButtonTooltip;
        }

        /// <summary>Ctrl+Shift+M (without Alt): the plain letters are the group keys.</summary>
        private static bool IsManuscriptKey(Keys keyData) => keyData == (Keys.Control | Keys.Shift | Keys.M);

        /// <summary>Opens the manuscript window, starting with the photo that is shown (a video is not a page).</summary>
        private void OpenManuscripts()
        {
            _manuscriptButton.Enabled = false;
            try
            {
                _currentConfig.ApplyDefaultLikedFolder(); // the "Photos from" box offers the liked folder, as Make a book does
                SetStatus(ManuscriptStrings.StatusOpening);
                using var dialog = CreateManuscriptDialog();

                // How a model that this window asked for is coming (a download of 1.8 GB takes minutes) is said in the status bar of the main window, which is behind the dialog: the dialog shows it too
                ToolModelStatus += dialog.ShowModelStatus;
                try
                {
                    dialog.ShowDialog(this);
                }
                finally
                {
                    ToolModelStatus -= dialog.ShowModelStatus;
                }

                if (!string.IsNullOrWhiteSpace(dialog.MadeBookPath) && File.Exists(dialog.MadeBookPath))
                    SetStatus(string.Format(ManuscriptStrings.StatusBookMadeFormat, dialog.MadeBookPath, dialog.MadeBookPages));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(ManuscriptStrings.StatusErrorFormat, ex.Message));
                MessageBox.Show(this, ex.Message, ManuscriptStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _manuscriptButton.Enabled = true;
            }
        }

        /// <summary>Makes the window for what the program is showing; a test makes it without showing it.</summary>
        internal ManuscriptDialog CreateManuscriptDialog()
        {
            var shown = pictureBox1.Tag as string;
            string[] start = shown is not null && File.Exists(shown) && MediaKinds.IsImage(shown) ? [shown] : [];

            // The pages are the photos the program has loaded, as the book of the program starts with them (the ones that are not pictures are left out by the window)
            var loaded = _currentConfig.Files.Where(file => file.IsImage).Select(file => file.OriginalFilePath).ToList();
            return new ManuscriptDialog(
                _currentConfig.CurrentFolder, start, _effects, _currentConfig.IsDarkTheme, EnsureManuscriptModelsAsync, FollowDrawnPhoto, EnsureManuscriptCutModelAsync, loaded, EnsureManuscriptPictureModelsAsync,
                EnsureManuscriptFixModelAsync, likedFolder: _currentConfig.LikedFolder, extensions: _currentConfig.Extensions, ensureFinderAsync: EnsureManuscriptFinderAsync);
        }

        /// <summary>The model that finds the lines (3 MB), for marking them on a photo that goes to an AI service: asked for (and downloaded) the first time the lines are wanted on the photo.</summary>
        private Task<bool> EnsureManuscriptFinderAsync() => EnsureToolModelAsync(ToolModels.OcrFinder, ToolModelStrings.MarkLinesFeature, askAgain: true);

        /// <summary>The model of the AI that corrects the text: the language model, and the list of words it reads with (the person is asked once, for the first that is not there).</summary>
        private async Task<bool> EnsureManuscriptFixModelAsync()
        {
            var feature = ToolModelStrings.FixTextFeature;
            var modelMissing = !ToolModels.TextFixer.IsPresentIn(_effects.ModelsFolder);
            if (!await EnsureToolModelAsync(ToolModels.TextFixer, feature, askAgain: true))
                return false;

            return modelMissing
                ? await EnsureApprovedToolModelAsync(ToolModels.TextFixerTokenizer, feature)
                : await EnsureToolModelAsync(ToolModels.TextFixerTokenizer, feature, askAgain: true);
        }

        /// <summary>The models that find the pictures on a page: the finder of lines (which paints the text over) and the model of the Telegram stickers (which draws the contours). The person is asked once for the first that is not there.</summary>
        private async Task<bool> EnsureManuscriptPictureModelsAsync()
        {
            var feature = ToolModelStrings.FindPicturesFeature;
            var finderMissing = !ToolModels.OcrFinder.IsPresentIn(_effects.ModelsFolder);
            if (!await EnsureToolModelAsync(ToolModels.OcrFinder, feature, askAgain: true))
                return false;

            return finderMissing
                ? await EnsureApprovedToolModelAsync(ToolModels.Cutout, feature)
                : await EnsureToolModelAsync(ToolModels.Cutout, feature, askAgain: true);
        }

        /// <summary>The model of the Telegram stickers, which finds the sheet in a photo for the book: asked for (and downloaded) the first time a book cuts its pages out.</summary>
        private Task<bool> EnsureManuscriptCutModelAsync() => EnsureToolModelAsync(ToolModels.Cutout, ToolModelStrings.CutManuscriptPagesFeature, askAgain: true);

        /// <summary>
        /// The models for a kind of writing and a language: the one that finds the lines, and the one that reads them (the handwriting model of the language, or the text reader of the Draw window).
        /// The person is asked once, for the first that is not there; a yes is a yes for the other as well.
        /// </summary>
        private async Task<bool> EnsureManuscriptModelsAsync(ScanKind kind, ScanLanguage language)
        {
            var feature = ToolModelStrings.ScanManuscriptFeature;
            var finderMissing = !ToolModels.OcrFinder.IsPresentIn(_effects.ModelsFolder);
            if (!await EnsureToolModelAsync(ToolModels.OcrFinder, feature, askAgain: true))
                return false;

            var reader = kind == ScanKind.Handwriting ? ManuscriptDialog.HandwritingModel(language) : ToolModels.OcrReader;
            return finderMissing
                ? await EnsureApprovedToolModelAsync(reader, feature)
                : await EnsureToolModelAsync(reader, feature, askAgain: true);
        }
    }
}
