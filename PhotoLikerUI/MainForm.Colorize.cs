using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>The "Colorize" button of the toolbar: opens the window that gives colours to black and white photos (see <see cref="ColorizeDialog"/>), as copies next to the photos.</summary>
    public partial class MainForm
    {
        private ToolStripButton _colorizeButton = null!;

        private void InitializeColorize()
        {
            _colorizeButton = new ToolStripButton(ColorizeStrings.ButtonText)
            {
                Name = "colorizeToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Colorize(),
                ToolTipText = ColorizeStrings.ButtonTooltip,
            };
            _colorizeButton.Click += (_, _) => ColorizeButtonClicked();

            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(_findDuplicatesButton) + 1, _colorizeButton);
        }

        private void ApplyColorizeLanguage()
        {
            _colorizeButton.Text = ColorizeStrings.ButtonText;
            _colorizeButton.ToolTipText = ColorizeStrings.ButtonTooltip;
            _bar?.ApplyTexts();
        }

        /// <summary>The window for many photos: this photo, or the black and white photos of the list (the bar over a photo that is shown works on that one photo; its "Many photos" button opens this).</summary>
        private void Colorize()
        {
            var photos = _currentConfig.Files.Where(file => !file.IsVideo).Select(file => file.OriginalFilePath).ToList();
            if (photos.Count == 0)
            {
                SetStatus(ColorizeStrings.StatusNoPhotos);
                return;
            }
            if (_indexCts is not null)
            {
                // The index lists the photos it was started with; copies that join the folder meanwhile would be indexed as if they had been there
                SetStatus(ColorizeStrings.StatusIndexing);
                return;
            }

            var current = pictureBox1.Tag is string shown && !MediaKinds.IsVideo(shown) ? shown : null;
            using var dialog = new ColorizeDialog(
                photos, current, _effects, () => EnsureToolModelAsync(ToolModels.Colorizer, ToolModelStrings.ColorizeFeature, askAgain: true), _currentConfig.IsDarkTheme,
                _bar?.Tuning ?? new ColorTuning(_globalConfig.ColorizeStrength / 100f, _globalConfig.ColorizeWarmth / (float)ColorizeBar.MaxWarmth));
            dialog.ShowDialog(this);

            if (dialog.Saved == 0)
                return;

            // The copies are new files of the folder: list it again, as after the other tools that change it
            SetStatus(string.Format(ColorizeStrings.StatusDoneFormat, dialog.Saved));
            ClearDisplay();
            LoadFolder(useSavedList: false);
        }
    }
}
