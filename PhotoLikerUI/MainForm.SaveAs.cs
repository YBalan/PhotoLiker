using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The Save button of the toolbar is Save As: it asks where to save, and saves what is shown. For a photo that is a copy of the file under the name and in the place the person chooses, and when the
    /// name (or the "Save as type" list) says another format (JPEG, PNG, BMP, GIF, TIFF) the photo is converted as it is saved (<see cref="PhotoConverter"/>). A video is copied as it is. Both keep the dates
    /// of the file, which this program cares about. With the Colorize bar showing colours, or the Refocus bar working, it is the picture as it looks on the screen now that is saved, not the file, in
    /// the format the person chooses (a JPEG when nothing says otherwise). The photo that is shown is never changed.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>
        /// Asks where to save (the Save As window of Windows); null when the person cancelled. A test answers it. The first of the kinds of file in <paramref name="filter"/> is the one that is selected
        /// at first; a name typed without an extension gets the one of the kind that is selected at the end.
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
                FilterIndex = 1,
                InitialDirectory = Directory.Exists(folder) ? folder : string.Empty,
                FileName = fileName,
                OverwritePrompt = true,
                CheckPathExists = true,
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return null;

            // A name with no extension is given the one of the kind the person picked in the list (not left to the window: which kind that is decides the format of a photo)
            var chosen = dialog.FileName;
            return Path.GetExtension(chosen).Length == 0 && ExtensionOfFilter(filter, dialog.FilterIndex) is { } extension ? chosen + extension : chosen;
        }

        /// <summary>The extension the kind of file number <paramref name="index"/> (from 1) of a filter text begins with (<c>.png</c> for <c>PNG (*.png)|*.png</c>); null for "all files" and when there is none.</summary>
        internal static string? ExtensionOfFilter(string filter, int index)
        {
            var parts = filter.Split('|');
            var at = index * 2 - 1;
            if (index < 1 || at >= parts.Length)
                return null;

            var first = parts[at].Split(';')[0].Trim();
            return first.Length > 2 && first.StartsWith("*.", StringComparison.Ordinal) && first[2..].All(char.IsLetterOrDigit) ? first[1..] : null;
        }

        /// <summary>The "all files" kind of a Save As window: its text and its pattern (a kind in the filter of a window is both, joined by a bar; the text alone makes the window refuse the filter).</summary>
        internal static string AllFilesKind => $"{ToolbarStrings.SaveAsFilterAll}|*.*";

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

        /// <summary>The kinds of file of the Save As window of a picture made by a bar: every format a picture can be saved as, the JPEG first (it is what the bar's own Save a copy makes).</summary>
        internal static string EditedFilter => string.Join('|', PhotoFormats.All.Select(PhotoFormats.Filter));

        /// <summary>
        /// The kinds of file of the Save As window of a file: for a photo its own format first (what is chosen at first), then the others it can be converted to, then every file; for a video only its own
        /// kind and every file (a video is copied as it is).
        /// </summary>
        internal static string CopyFilter(string file)
        {
            var extension = Path.GetExtension(file);
            if (MediaKinds.IsVideo(file))
                return extension.Length > 1 ? $"{extension.TrimStart('.').ToUpperInvariant()} (*{extension})|*{extension}|{AllFilesKind}" : AllFilesKind;

            var own = PhotoFormats.ForPath(file);
            var kinds = new List<string>();
            if (own is null && extension.Length > 1)
                kinds.Add($"{extension.TrimStart('.').ToUpperInvariant()} (*{extension})|*{extension}"); // a format that can be read and copied, but not written (WebP, HEIC)
            if (own is { } format)
                kinds.Add(PhotoFormats.Filter(format));
            kinds.AddRange(PhotoFormats.All.Where(other => other != own).Select(PhotoFormats.Filter));
            kinds.Add(AllFilesKind);
            return string.Join('|', kinds);
        }

        /// <summary>A picture made from the photo (colorized or refocused): a JPEG unless the person chooses another format; the name that is suggested is the one the bar's own Save a copy would use.</summary>
        private async Task SaveEditedAsAsync(string file, string suffix, Func<string, string> save)
        {
            var suggested = Path.GetFileName(ColorizedCopy.NextFreePath(file, suffix));
            var chosen = AskSaveAsPath(string.Format(ToolbarStrings.SaveAsTitleFormat, Path.GetFileName(file)), EditedFilter, Path.GetDirectoryName(file) ?? string.Empty, suggested);
            if (chosen is null)
                return;

            var path = PhotoFormats.WithExtension(chosen, PhotoFormat.Jpeg); // a name that says no format is a JPEG, so the name says what the file is
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(ToolbarStrings.StatusSaveAsSameFile); // the picture would be written over the photo itself, which this program never does
                return;
            }

            if (!string.Equals(path, chosen, StringComparison.Ordinal) && File.Exists(path))
            {
                SetStatus(string.Format(ToolbarStrings.StatusSaveAsExistsFormat, Path.GetFileName(path))); // the window did not ask about this name: it is not written over
                return;
            }

            SetStatus(string.Format(RefocusStrings.StatusSavingFormat, Path.GetFileName(path)));
            if (PhotoFormats.ForPath(path) == PhotoFormat.Jpeg)
                await Task.Run(() => save(path));
            else
                await Task.Run(() => SaveEditedConverted(save, path));
            await SavedAsync(path);
        }

        /// <summary>The picture is made as the JPEG that the bars write (with the Exif and the dates of the photo), and that is converted to the format of <paramref name="path"/>.</summary>
        private static void SaveEditedConverted(Func<string, string> save, string path)
        {
            var temporary = Path.Combine(Path.GetTempPath(), $"photoliker-{Guid.NewGuid():N}.jpg");
            try
            {
                save(temporary);
                PhotoConverter.Convert(temporary, path);
            }
            finally
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>A copy of the file of a photo or a video under the name and in the place the person chooses, with the dates of the file; a photo whose new name says another format is converted.</summary>
        private async Task CopyFileAsAsync(string file)
        {
            var extension = Path.GetExtension(file);
            var isPhoto = !MediaKinds.IsVideo(file);
            var chosen = AskSaveAsPath(string.Format(ToolbarStrings.SaveAsTitleFormat, Path.GetFileName(file)), CopyFilter(file), Path.GetDirectoryName(file) ?? string.Empty, Path.GetFileName(file));
            if (chosen is null)
                return;

            var path = chosen;
            var convert = false;
            if (isPhoto)
            {
                // The name decides: another format is a conversion, the photo's own is a copy of the bytes, and a name that says no format gets the photo's extension (so the file is what its name says)
                var named = PhotoFormats.ForPath(chosen);
                var own = PhotoFormats.ForPath(file);
                if (named is not null)
                    convert = named != own;
                else if (!string.Equals(Path.GetExtension(chosen), extension, StringComparison.OrdinalIgnoreCase))
                    path = chosen + extension;
            }

            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(ToolbarStrings.StatusSaveAsSameFile);
                return;
            }

            if (!string.Equals(path, chosen, StringComparison.Ordinal) && File.Exists(path))
            {
                SetStatus(string.Format(ToolbarStrings.StatusSaveAsExistsFormat, Path.GetFileName(path)));
                return;
            }

            SetStatus(string.Format(RefocusStrings.StatusSavingFormat, Path.GetFileName(path)));
            if (convert)
                await Task.Run(() => PhotoConverter.Convert(file, path));
            else
                await Task.Run(() => CopyKeepingDates(file, path));
            await SavedAsync(path);
        }

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
