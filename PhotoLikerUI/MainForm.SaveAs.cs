using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Save button of the toolbar is Save As: it asks where to save, and saves what is shown. For a photo or a video that is a copy of the file under the name and in the place the person chooses (it keeps
    /// the dates of the file, which this program cares about). With the Colorize bar showing colours, or the Refocus bar working, it is the picture as it looks on the screen now that is saved, as a JPEG
    /// (with the Exif of the photo and its dates), not the file. The photo that is shown is never changed.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>
        /// Asks where to save (the Save As window of Windows); null when the person cancelled. A test answers it.
        /// </summary>
        /// <param name="title">The title of the window.</param>
        /// <param name="filter">The kinds of file it offers, as the window's filter text says it.</param>
        /// <param name="folder">Where it begins.</param>
        /// <param name="fileName">The name it suggests.</param>
        protected virtual string? AskSaveAsPath(string title, string filter, string folder, string fileName)
        {
            using var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                InitialDirectory = Directory.Exists(folder) ? folder : string.Empty,
                FileName = fileName,
                OverwritePrompt = true,
                CheckPathExists = true,
                AddExtension = true,
                DefaultExt = Path.GetExtension(fileName).TrimStart('.'),
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        private async void SaveAsButtonClicked()
        {
            try
            {
                await SaveAsAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(ToolbarStrings.StatusSaveAsFailedFormat, ex.Message));
            }
        }

        /// <summary>Saves what is shown where the person says: the picture of the bar that is working, or else the file of the photo or video.</summary>
        internal async Task SaveAsAsync()
        {
            if (pictureBox1.Tag is not string file)
            {
                SetStatus(ToolbarStrings.StatusNothingToSave);
                return;
            }

            // The picture the Refocus bar, or the Colorize bar, is showing: what is saved is what the person sees
            if (RefocusWorking && _focusPreview is { } refocus)
            {
                var look = _focusLook;
                await SaveEditedAsAsync(file, FocusPreview.Suffix, path => refocus.SaveAs(look, path));
                return;
            }

            if (_bar is { Visible: true, ColoursShown: true, Ready: true } colourBar && _barPreview is { } colours && string.Equals(colours.Path, file, StringComparison.Ordinal))
            {
                var tuning = colourBar.Tuning;
                await SaveEditedAsAsync(file, ColorizedCopy.Suffix, path => colours.SaveAs(tuning, path));
                return;
            }

            await CopyFileAsAsync(file);
        }

        /// <summary>A picture made from the photo (colorized or refocused), saved as a JPEG: the name that is suggested is the one the bar's own Save a copy would use.</summary>
        private async Task SaveEditedAsAsync(string file, string suffix, Func<string, string> save)
        {
            var suggested = Path.GetFileName(ColorizedCopy.NextFreePath(file, suffix));
            var chosen = AskSaveAsPath(string.Format(ToolbarStrings.SaveAsTitleFormat, Path.GetFileName(file)), "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg", Path.GetDirectoryName(file) ?? string.Empty, suggested);
            if (chosen is null)
                return;

            var path = JpegName(chosen);
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(ToolbarStrings.StatusSaveAsSameFile); // the picture would be written over the photo itself, which this program never does
                return;
            }

            SetStatus(string.Format(RefocusStrings.StatusSavingFormat, Path.GetFileName(path)));
            await Task.Run(() => save(path));
            await SavedAsync(path);
        }

        /// <summary>A copy of the file of a photo or a video, under the name and in the place the person chooses, with the dates of the file.</summary>
        private async Task CopyFileAsAsync(string file)
        {
            var extension = Path.GetExtension(file);
            var filter = extension.Length > 1
                ? $"{extension.TrimStart('.').ToUpperInvariant()} (*{extension})|*{extension}|{ToolbarStrings.SaveAsFilterAll}"
                : ToolbarStrings.SaveAsFilterAll;
            var chosen = AskSaveAsPath(string.Format(ToolbarStrings.SaveAsTitleFormat, Path.GetFileName(file)), filter, Path.GetDirectoryName(file) ?? string.Empty, Path.GetFileName(file));
            if (chosen is null)
                return;

            if (string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(ToolbarStrings.StatusSaveAsSameFile);
                return;
            }

            SetStatus(string.Format(RefocusStrings.StatusSavingFormat, Path.GetFileName(chosen)));
            await Task.Run(() => CopyKeepingDates(file, chosen));
            await SavedAsync(chosen);
        }

        /// <summary>The name with a JPEG extension: a picture that is saved is a JPEG, whatever the person typed after the dot (<c>x.png</c> becomes <c>x.png.jpg</c>, not a JPEG called PNG).</summary>
        internal static string JpegName(string path) =>
            Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? path : path + ".jpg";

        /// <summary>
        /// Copies the file, over one that is there (the person was asked), and gives the copy the dates of the file: a program that sorts and searches by date, as this one does, would else find the copy
        /// at the moment it was made.
        /// </summary>
        internal static void CopyKeepingDates(string from, string to)
        {
            File.Copy(from, to, overwrite: true);
            try
            {
                File.SetCreationTimeUtc(to, File.GetCreationTimeUtc(from));
                File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the copy is there; it only keeps the date it was made
            }
        }

        /// <summary>The file was saved: said so, and if it is in the folder that is open it joins the list (the photo that is shown stays where it is).</summary>
        private async Task SavedAsync(string path)
        {
            var text = string.Format(ToolbarStrings.StatusSaveAsSavedFormat, Path.GetFileName(path));
            var folder = _currentConfig.CurrentFolder;
            var inside = !string.IsNullOrEmpty(folder) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            if (inside && _indexCts is null)
            {
                StopListingCheck();
                if (_listing is { } listing && !_resultsActive && !IsDisposed)
                    await CheckListingAsync(_folderGeneration, listing, _currentConfig.SortOrder);
            }

            if (!IsDisposed)
                SetStatus(text);
        }
    }
}
