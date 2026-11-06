using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>The "Align dates" button of the toolbar: opens the dialog that makes the dates of the files the date taken.</summary>
    public partial class MainForm
    {
        private ToolStripButton _alignDatesButton = null!;

        private void InitializeDateAlignment()
        {
            _alignDatesButton = new ToolStripButton(DateAlignmentStrings.ButtonText)
            {
                Name = "alignDatesToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.AlignDates(),
                ToolTipText = DateAlignmentStrings.ButtonTooltip,
            };
            _alignDatesButton.Click += (_, _) => AlignDates();

            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(generateVideoToolStripButton) + 1, _alignDatesButton);
        }

        private async void AlignDates()
        {
            var folder = _currentConfig.CurrentFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                SetStatus(SearchStrings.StatusNoFolder);
                return;
            }
            if (_indexCts is not null)
            {
                // The index remembers the date of change of every photo; a run in the middle of indexing would leave it with stale ones
                SetStatus(DateAlignmentStrings.StatusIndexing);
                return;
            }

            // A video that plays holds its file open: Windows would not let its dates be set
            if (_player is { Visible: true })
            {
                HidePlayer();
                await _player.StopAsync();
            }

            _currentConfig.ApplyDefaultLikedFolder();
            var index = _folderSearch is { } search && SameFolder(search.Folder, folder) ? search.Index : null;

            // With videos listed the dialog looks at them too (by the date recorded in them); without, as before, at the photos
            var videos = _currentConfig.SupportVideo ? VideoLibrary.Current : null;
            using var dialog = new DateAlignmentDialog(
                folder, _currentConfig.GoThroughSubFolders, videos is null ? _currentConfig.Extensions : _currentConfig.ListedExtensions, _currentConfig.LikedFolder, index, videos,
                _currentConfig.IsDarkTheme);
            dialog.ShowDialog(this);

            if (!dialog.Changed)
                return;

            // The list holds the old dates (and is sorted by them), and the photo shown has them in its File Date row: list the folder again
            ClearDisplay();
            LoadFolder(useSavedList: false);
        }
    }
}
