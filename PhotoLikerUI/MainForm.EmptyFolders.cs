namespace PhotoLikerUI
{
    /// <summary>The "Remove empty folders" button of the toolbar: opens the dialog that lists them and removes the ones left ticked.</summary>
    public partial class MainForm
    {
        private ToolStripButton _removeEmptyFoldersButton = null!;

        private void InitializeEmptyFolders()
        {
            _removeEmptyFoldersButton = new ToolStripButton(EmptyFoldersStrings.ButtonText)
            {
                Name = "removeEmptyFoldersToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.RemoveEmptyFolders(),
                ToolTipText = EmptyFoldersStrings.ButtonTooltip,
            };
            _removeEmptyFoldersButton.Click += (_, _) => RemoveEmptyFolders();

            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(_alignDatesButton) + 1, _removeEmptyFoldersButton);
        }

        private void RemoveEmptyFolders()
        {
            var folder = _currentConfig.CurrentFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                SetStatus(EmptyFoldersStrings.StatusNoFolder);
                return;
            }

            _currentConfig.ApplyDefaultLikedFolder();

            // The photos the search index could not decode are unreadable too; moving photos under a running indexer would confuse it
            var index = _folderSearch is { } search && SameFolder(search.Folder, folder) ? search.Index : null;

            // The folders the app keeps for itself stay, empty or not
            using var dialog = new EmptyFoldersDialog(
                folder, [DeletedPhotos.FolderOf(folder), _currentConfig.LikedFolder], _currentConfig.Extensions, index, canMovePhotos: _indexCts is null, _currentConfig.IsDarkTheme);
            dialog.ShowDialog(this);

            if (dialog.PhotosMoved > 0)
            {
                // Photos left the folder: the list holds some that are not there any more
                ClearDisplay();
                LoadFolder();
            }
            else if (dialog.Removed > 0)
            {
                SetStatus(string.Format(EmptyFoldersStrings.StatusDone, EmptyFoldersStrings.Count(dialog.Removed, EmptyFoldersStrings.EmptyFolderOne, EmptyFoldersStrings.EmptyFolderMany)));
            }
        }
    }
}
