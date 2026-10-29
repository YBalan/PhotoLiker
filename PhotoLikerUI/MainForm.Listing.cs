using System.Diagnostics;

namespace PhotoLikerUI
{
    /// <summary>
    /// The saved list of the folder (see <see cref="PhotoListing"/>): a folder opens from the list saved at the last visit, and
    /// is checked for changes while it is already on screen.
    /// </summary>
    public partial class MainForm
    {
        private PhotoListing? _listing;                 // the list of the folder that is open; photos that are deleted are taken out of it
        private CancellationTokenSource? _listingCts;   // stops the check of a folder that is being left

        private void StopListingCheck() => _listingCts?.Cancel();

        /// <summary>The saved list of the folder. The list of it that is open is written first if it changed (photos deleted), so the file is up to date.</summary>
        private static PhotoListing? LoadSavedListing(PhotoListing? open, ListingKey key)
        {
            if (open is { NeedsSave: true })
            {
                try
                {
                    open.Save();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return open; // it cannot be written, and it is the list that knows most
                }
            }
            return PhotoListing.Load(key);
        }

        /// <summary>F5: lists the folder again instead of trusting the saved list. For files that another program changed in place.</summary>
        private void ListFolderAgain()
        {
            if (string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder))
                return;

            ClearDisplay();
            LoadFolder(useSavedList: false);
        }

        /// <summary>
        /// Asks the folder what changed since the list was saved, and shows it: photos that came are in the list, photos that
        /// went are not, and the photo that was shown stays where it is. A folder nothing changed in costs a question per
        /// subfolder instead of a listing.
        /// </summary>
        private async Task CheckListingAsync(int generation, PhotoListing listing, PhotoSortOrder sortOrder)
        {
            var cts = _listingCts = new CancellationTokenSource();
            var clock = Stopwatch.StartNew();
            var folder = _currentConfig.CurrentFolder;
            try
            {
                var changes = await Task.Run(() => listing.Verify(cts.Token), cts.Token);
                if (!changes.Reachable)
                {
                    if (generation == _folderGeneration)
                        SetStatus(string.Format(MainFormStrings.StatusFolderNotReachable, folder));
                    return;
                }

                if (listing.NeedsSave)
                    await SaveListingAsync(listing);

                // Another folder, or the search results, are shown now: the saved list is up to date, that is all
                if (generation != _folderGeneration || _resultsActive)
                    return;

                if (changes.HasChanges)
                {
                    // A like or a copy to a group works on the photos of the list that is on screen: let it finish first
                    for (var waited = 0; _likeInProgress && waited < 50; waited++)
                        await Task.Delay(100, cts.Token);
                    if (_likeInProgress || generation != _folderGeneration || _resultsActive)
                        return;

                    ShowChangedList(listing.ToPhotoFiles(sortOrder));
                }

                SetStatus(DescribeCheck(changes, clock.Elapsed));
            }
            catch (OperationCanceledException)
            {
                // the folder is being left
            }
            catch (Exception ex)
            {
                if (generation == _folderGeneration)
                    SetStatus(string.Format(MainFormStrings.StatusFolderCheckError, ex.Message));
            }
            finally
            {
                if (ReferenceEquals(_listingCts, cts))
                    _listingCts = null;
                cts.Dispose();
            }
        }

        private string DescribeCheck(ListingChanges changes, TimeSpan took)
        {
            var parts = new List<string>();
            if (changes.Added > 0)
                parts.Add(string.Format(MainFormStrings.CheckAdded, changes.Added));
            if (changes.Removed > 0)
                parts.Add(string.Format(MainFormStrings.CheckRemoved, changes.Removed));
            if (changes.Changed > 0)
                parts.Add(string.Format(MainFormStrings.CheckChanged, changes.Changed));

            var text = string.Format(
                MainFormStrings.StatusFolderChecked,
                _currentConfig.TotalFiles, _currentConfig.CurrentFolder, took.TotalSeconds,
                parts.Count == 0 ? MainFormStrings.CheckNothingChanged : string.Join(", ", parts));
            return changes.Incomplete > 0 ? text + string.Format(MainFormStrings.CheckIncompleteNote, changes.Incomplete) : text;
        }

        /// <summary>Replaces the list that is shown by a newer one, keeping the photo that is shown (or the one nearest to it if it is gone).</summary>
        private void ShowChangedList(List<PhotoFile> files)
        {
            var current = _currentConfig.CurrentFilePath;
            var position = _currentConfig.CurrentIndex;

            _liked?.Apply(files);
            _currentConfig.Files = files;
            _indexByPath = new Dictionary<string, int>(files.Count, StringComparer.Ordinal);
            for (var i = 0; i < files.Count; i++)
                _indexByPath[files[i].OriginalFilePath] = i;

            if (files.Count == 0)
            {
                ShowEmptyList();
            }
            else if (!string.IsNullOrEmpty(current) && _indexByPath.TryGetValue(current, out var index))
            {
                // The same photo stays on screen; what changes is its place in the list and the neighbours around it
                _currentConfig.CurrentIndex = index;
                _currentConfig.LikedFile = files[index].LikedFilePath;
                UpdateSavedInInfo();
                settingsPropertyGrid.Refresh();
                UpdateWindowTitle(current);
                RefreshWindow(current);
            }
            else
            {
                LoadImage(files[Math.Clamp(position, 0, files.Count - 1)].OriginalFilePath);
            }

            UpdateSearchControls();
        }

        /// <summary>Writes the list to its file in the background. A list that cannot be saved is made again next time.</summary>
        private static Task SaveListingAsync(PhotoListing listing) => Task.Run(() =>
        {
            try
            {
                listing.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the next visit lists the folder again
            }
        });

        /// <summary>Photos deleted since the list was saved are taken out of it, so the next visit does not show them for a moment.</summary>
        private void SaveListingOnClose()
        {
            if (_listing is not { NeedsSave: true } listing)
                return;

            try
            {
                listing.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the next visit checks the folder anyway
            }
        }
    }
}
