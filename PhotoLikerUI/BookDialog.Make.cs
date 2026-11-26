namespace PhotoLikerUI
{
    /// <summary>Making the book: checking what was asked for, the work in the background with its progress, and what is said at the end.</summary>
    internal sealed partial class BookDialog
    {
        private bool _making;
        private CancellationTokenSource? _cancellation;

        /// <summary>The last book made while this window was open.</summary>
        private string? _lastBook;

        private async Task MakeAsync()
        {
            if (_making)
                return;

            if (!_view.Entries.Any(entry => !entry.LeftOut))
            {
                MessageBox.Show(this, BookStrings.NothingToMake, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var format = SelectedFormat;
            var folder = _outputFolderBox.Text.Trim();
            var name = _fileNameBox.Text.Trim();
            if (folder.Length == 0)
            {
                MessageBox.Show(this, BookStrings.EnterOutputFolder, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (name.Length == 0)
            {
                MessageBox.Show(this, BookStrings.EnterFileName, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, BookStrings.InvalidFileName, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // The name ends in the extension of the format: a name with another one of the two is changed to it, a name with none gets it
            var extension = Extension(format);
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                name = HasBookExtension(name) ? Path.ChangeExtension(name, extension) : name + extension;
            _fileNameBox.Text = name;

            string path;
            try
            {
                path = Path.Combine(folder, name);
            }
            catch (ArgumentException)
            {
                MessageBox.Show(this, BookStrings.EnterOutputFolder, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (File.Exists(path) && MessageBox.Show(this, string.Format(BookStrings.ReplaceQuestionFormat, path), BookStrings.DialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SaveBook(); // the work on the book is kept before the long job, whatever becomes of it

            var settings = CurrentSettings();
            var options = new BookOptions(
                settings.Title ?? string.Empty,
                settings.Author ?? string.Empty,
                Loc.Language.Culture.TwoLetterISOLanguageName,
                ((Choice<PageFormat>)_pageCombo.SelectedItem!).Value,
                ((Choice<PageOrientation>)_orientationCombo.SelectedItem!).Value,
                (double)_marginNumeric.Value,
                _fontCombo.SelectedItem?.ToString() ?? "Segoe UI",
                _pageNumbersCheck.Checked);
            var quality = ((Choice<PictureQuality>)_qualityCombo.SelectedItem!).Value;

            _cancellation = new CancellationTokenSource();
            var cancellation = _cancellation.Token;
            SetBusy(true);
            _openButton.Visible = false;
            _progress.Value = 0;
            _progress.Visible = true;
            _statusLabel.Text = BookStrings.StatusOpening;

            try
            {
                Directory.CreateDirectory(folder);
                var photos = _view.Entries.ToList(); // the list as it is now: nothing in the window can change it until the book is made
                var result = await BookBuilder.BuildAsync(
                    photos,
                    format,
                    options,
                    quality,
                    path,
                    (done, total) => OnUi(() => _statusLabel.Text = string.Format(BookStrings.StatusPreparingFormat, done, total)),
                    () => OnUi(() => _statusLabel.Text = format switch
                    {
                        BookFormat.Pdf => BookStrings.StatusWritingPdf,
                        BookFormat.Docx => BookStrings.StatusWritingDocx,
                        _ => BookStrings.StatusWritingEpub,
                    }),
                    percent => OnUi(() => _progress.Value = Math.Clamp(percent, 0, 100)),
                    cancellation);

                MadeBookPath = result.Path;
                _lastBook = result.Path;
                _progress.Value = 100;
                _statusLabel.Text = string.Format(BookStrings.StatusDoneFormat, Path.GetFileName(result.Path), Plural.Count(result.Pages, BookStrings.Page), SizeText(result.Bytes));
                _openButton.Visible = true;

                if (result.Unreadable.Count > 0)
                {
                    _statusLabel.Text += "  " + string.Format(BookStrings.StatusUnreadableFormat, Plural.Count(result.Unreadable.Count, BookStrings.Photo));
                    var names = string.Join(Environment.NewLine, result.Unreadable.Take(10).Select(Path.GetFileName));
                    var more = result.Unreadable.Count > 10 ? Environment.NewLine + "..." : string.Empty;
                    MessageBox.Show(this, BookStrings.UnreadableListFormat + Environment.NewLine + Environment.NewLine + names + more, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = BookStrings.StatusCancelled;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = string.Format(BookStrings.FailedFormat, ex.Message);
                MessageBox.Show(this, string.Format(BookStrings.FailedFormat, ex.Message), BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cancellation?.Dispose();
                _cancellation = null;
                if (!IsDisposed)
                {
                    SetBusy(false);
                    _progress.Visible = false;
                }
            }
        }

        private static string SizeText(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

        /// <summary>Runs something on the thread of the window (the work reports from other threads); nothing is done once the window is gone.</summary>
        private void OnUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed)
                        action();
                });
            }
            catch (InvalidOperationException)
            {
                // the window closed between the check and the call
            }
        }

        /// <summary>While a book is made nothing of it can be changed; the Close button is Cancel.</summary>
        private void SetBusy(bool busy)
        {
            _making = busy;
            foreach (var input in _inputs)
                input.Enabled = !busy;

            _viewTools.Enabled = !busy;
            _view.Enabled = !busy;
            _zoom.Enabled = !busy;
            _above.Enabled = !busy;
            _below.Enabled = !busy;
            _makeButton.Enabled = !busy;
            _closeButton.Text = busy ? BookStrings.CancelButton : BookStrings.CloseButton;
            UpdateFormatControls();
            UpdateCommands();
        }

        private void CloseOrCancel()
        {
            if (_making)
                _cancellation?.Cancel();
            else
                Close();
        }

        private void OnClosing(object? sender, FormClosingEventArgs e)
        {
            if (_making)
            {
                e.Cancel = true;
                _cancellation?.Cancel(); // stops the work; the window is closed by the next Close
                return;
            }

            SaveBook();
        }

        private void OpenMadeBook()
        {
            if (_lastBook is null)
                return;

            if (ShellOpen.Document(_lastBook) is { } problem)
                _statusLabel.Text = problem;
        }
    }
}
