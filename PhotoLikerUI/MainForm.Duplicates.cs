using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>The "Find duplicates" button of the toolbar: opens the dialog that finds copies of the same photo and moves the ones in the worse folders away.</summary>
    public partial class MainForm
    {
        private ToolStripButton _findDuplicatesButton = null!;

        private void InitializeDuplicates()
        {
            _findDuplicatesButton = new ToolStripButton(DuplicatesStrings.ButtonText)
            {
                Name = "findDuplicatesToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.FindDuplicates(),
                ToolTipText = DuplicatesStrings.ButtonTooltip,
            };
            _findDuplicatesButton.Click += (_, _) => FindDuplicates();

            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(_removeEmptyFoldersButton) + 1, _findDuplicatesButton);
        }

        private void FindDuplicates()
        {
            var folder = _currentConfig.CurrentFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                SetStatus(SearchStrings.StatusNoFolder);
                return;
            }
            if (_indexCts is not null)
            {
                // The indexer works on a list of the photos it made at the start; photos that move away under it would be indexed as if they were there
                SetStatus(DuplicatesStrings.StatusIndexing);
                return;
            }

            _currentConfig.ApplyDefaultLikedFolder();
            var index = _folderSearch is { } search && SameFolder(search.Folder, folder) ? search.Index : null;

            using var dialog = new DuplicatesDialog(folder, _currentConfig.Extensions, _currentConfig.LikedFolder, index, _currentConfig.IsDarkTheme);
            dialog.ShowDialog(this);

            if (!dialog.Changed)
                return;

            // Photos were moved away (or put back): list the folder again
            ClearDisplay();
            LoadFolder(useSavedList: false);
        }
    }
}
