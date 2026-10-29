namespace PhotoLikerUI
{
    /// <summary>
    /// Alt + a group key while search results are shown: saves every photo of the results in that group, after telling how many
    /// photos and how many MB that is and being agreed to, with the program blocked and a progress bar while it copies.
    /// </summary>
    public partial class MainForm
    {
        private async Task AddResultsToGroupAsync(char key)
        {
            if (!_resultsActive || _currentConfig.Files.Count == 0)
            {
                SetStatus(string.Format(GroupCopyStrings.StatusNoResults, key));
                return;
            }
            if (_likeInProgress)
            {
                SetStatus(GroupCopyStrings.StatusBusy);
                return;
            }

            _likeInProgress = true;
            try
            {
                var likedFolder = await PrepareLikedFolderAsync();
                if (likedFolder is null)
                    return;

                var results = _currentConfig.Files;
                var photos = results.ToList();
                var request = _resultsRequest;
                var groups = await EnsureGroupLibraryAsync(likedFolder);
                var plan = await Task.Run(() => groups.PlanAdd(key, photos, request));

                // Esc, or another folder, while the group folder was being looked at: the results are gone
                if (!ReferenceEquals(results, _currentConfig.Files))
                    return;

                var groupName = Path.GetFileName(plan.Folder);
                if (plan.ToCopy.Count == 0)
                {
                    MessageBox.Show(this, string.Format(GroupCopyStrings.NothingToCopyFormat, Plural.Count(photos.Count, GroupCopyStrings.One, GroupCopyStrings.Many), groupName),
                        GroupCopyStrings.ConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var already = plan.AlreadyThere > 0 ? string.Format(GroupCopyStrings.AlreadyThereFormat, plan.AlreadyThere, photos.Count) : string.Empty;
                var question = string.Format(
                    GroupCopyStrings.ConfirmFormat,
                    Plural.Count(plan.ToCopy.Count, GroupCopyStrings.One, GroupCopyStrings.Many), groupName, GroupCopyStrings.FormatSize(plan.Bytes), already,
                    plan.FolderIsNew ? GroupCopyStrings.NewFolderNote : string.Empty);
                if (MessageBox.Show(this, question, GroupCopyStrings.ConfirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                using var dialog = new GroupCopyDialog(groups, plan, groupName);
                ThemeManager.Apply(dialog, _currentConfig.IsDarkTheme);
                dialog.ShowDialog(this);

                if (dialog.Result is not { } result)
                {
                    SetStatus(string.Format(GroupCopyStrings.StatusError, dialog.Error?.Message));
                    return;
                }

                // Another folder was opened while the copies were made (the dialog is modal, so this is only the unusual case)
                if (ReferenceEquals(results, _currentConfig.Files))
                    groups.Update(result);

                SetStatus(DescribeGroupCopy(result, Path.GetFileName(result.Folder)));
                ReportCopyFailures(result);
                RefreshLikeState();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(GroupCopyStrings.StatusError, ex.Message));
            }
            finally
            {
                _likeInProgress = false;
            }
        }

        private static string DescribeGroupCopy(GroupAddResult result, string groupName)
        {
            var notes =
                (result.Skipped > 0 ? string.Format(GroupCopyStrings.NoteSkippedFormat, result.Skipped) : string.Empty) +
                (result.Failures.Count > 0 ? string.Format(GroupCopyStrings.NoteFailedFormat, result.Failures.Count) : string.Empty) +
                (result.Cancelled ? GroupCopyStrings.NoteCancelled : string.Empty);

            return result.Copied == 0
                ? string.Format(GroupCopyStrings.StatusDoneNothing, groupName, notes)
                : string.Format(GroupCopyStrings.StatusDoneFormat, Plural.Count(result.Copied, GroupCopyStrings.One, GroupCopyStrings.Many), GroupCopyStrings.FormatSize(result.Bytes), groupName, notes);
        }

        /// <summary>The photos that could not be copied are listed (the first few), also when the run gave up because they kept failing.</summary>
        private void ReportCopyFailures(GroupAddResult result)
        {
            if (result.Failures.Count == 0)
                return;

            const int Shown = 5;
            var lines = string.Join('\n', result.Failures.Take(Shown).Select(f => string.Format(GroupCopyStrings.FailureLineFormat, f.Path, f.Message)));
            var more = result.Failures.Count > Shown ? string.Format(GroupCopyStrings.MoreFailuresFormat, result.Failures.Count - Shown) : string.Empty;
            var headline = result.GaveUp
                ? string.Format(GroupCopyStrings.GaveUpFormat, GroupLibrary.MaxFailuresInARow)
                : string.Format(GroupCopyStrings.FailedFormat, result.Failures.Count);
            MessageBox.Show(this, string.Format(GroupCopyStrings.FailuresFormat, headline, lines, more),
                GroupCopyStrings.FailuresTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
