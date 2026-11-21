using System.Text.RegularExpressions;

namespace PhotoLikerUI
{
    /// <summary>The photos of the book: where they come from, the book that was kept with their folder, the settings, and what is written for the photo that is picked.</summary>
    internal sealed partial class BookDialog
    {
        /// <summary>The names the dialog makes itself (photobook_20261121_093015.pdf): they are not put back from the kept book, a new one is made each time.</summary>
        private static readonly Regex AutomaticFileName = new(@"^photobook_\d{8}_\d{6}\.(pdf|epub)$", RegexOptions.IgnoreCase);

        private string _automaticOutputFolder;
        private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 15000 };

        private int _generation;
        private string? _loadedFolder;
        private bool _dirty;
        private bool _applying;
        private bool _loadingEditors;
        private bool _refreshingSourceItem;
        private int _previewGeneration;

        // The photo whose texts are in the two editors now (null: none)
        private BookEntry? _shownEntry;

        private Source SelectedSource => (Source)_sourceCombo.SelectedItem!;

        private PhotoSortOrder SelectedSortOrder => ((Choice<PhotoSortOrder>)_sortCombo.SelectedItem!).Value;

        private BookFormat SelectedFormat => ((Choice<BookFormat>)_formatCombo.SelectedItem!).Value;

        private static string Extension(BookFormat format) => format == BookFormat.Epub ? ".epub" : ".pdf";

        private static string NewAutomaticFileName(BookFormat format) => $"photobook_{DateTime.Now:yyyyMMdd_HHmmss}{Extension(format)}";

        /// <summary>The photos of the source that is picked are in the view with the order that was kept for them; the book is all there is to see, edit and make.</summary>
        internal IReadOnlyList<BookEntry> Entries => _view.Entries;

        /// <summary>The view of the thumbnails (for the tests, which move photos in it as a person does).</summary>
        internal ThumbnailReorderView View => _view;

        /// <summary>True while the photos of a source are being listed or its book read.</summary>
        internal bool IsLoading { get; private set; }

        // ── For the tests of the window, which use it as a person does ──────

        internal RichDescriptionEditor AboveEditor => _above;

        internal RichDescriptionEditor BelowEditor => _below;

        internal Button MakeButton => _makeButton;

        internal ToolStripButton RotateLeftButton => _rotateLeftButton;

        internal ToolStripButton RotateRightButton => _rotateRightButton;

        internal Button OpenButton => _openButton;

        internal Button CloseButton => _closeButton;

        internal string StatusText => _statusLabel.Text;

        internal string CountsText => _countsLabel.Text;

        internal ComboBox SourceCombo => _sourceCombo;

        internal ComboBox SortCombo => _sortCombo;

        internal Button SortButton => _sortButton;

        internal ComboBox FormatCombo => _formatCombo;

        internal TextBox TitleBox => _titleBox;

        internal TextBox AuthorBox => _authorBox;

        internal TextBox OutputFolderBox => _outputFolderBox;

        internal TextBox FileNameBox => _fileNameBox;

        internal ComboBox PageCombo => _pageCombo;

        internal ComboBox OrientationCombo => _orientationCombo;

        internal ComboBox QualityCombo => _qualityCombo;

        internal IReadOnlyList<Control> PdfOnlyFields => _pdfOnly;

        internal ProgressBar Progress => _progress;

        internal ToolStrip ViewTools => _viewTools;

        internal TrackBar Zoom => _zoom;

        internal string PickedText => _pickedLabel.Text;

        internal Image? PreviewImage => _preview.Image;

        private void WireEvents()
        {
            _sourceCombo.SelectedIndexChanged += (_, _) =>
            {
                if (_refreshingSourceItem)
                    return;

                SaveBook(); // the book that is in the view goes to its folder before another is shown
                FollowOutputFolder();
                _ = LoadSourceAsync();
            };

            _sortButton.Click += (_, _) => _view.SortAll(SelectedSortOrder);
            _formatCombo.SelectedIndexChanged += (_, _) => UpdateFormatControls();
            _browseButton.Click += (_, _) => BrowseOutputFolder();

            foreach (var combo in new[] { _formatCombo, _pageCombo, _orientationCombo, _fontCombo, _qualityCombo })
                combo.SelectedIndexChanged += (_, _) => MarkDirty();
            foreach (var box in new[] { _titleBox, _authorBox, _outputFolderBox, _fileNameBox })
                box.TextChanged += (_, _) => MarkDirty();
            _marginNumeric.ValueChanged += (_, _) => MarkDirty();
            _pageNumbersCheck.CheckedChanged += (_, _) => MarkDirty();

            _view.SelectionChanged += (_, _) =>
            {
                ShowPicked();
                UpdateCommands();
            };
            _view.OrderChanged += (_, _) =>
            {
                MarkDirty();
                UpdateCounts();
                UpdateCommands();
                RefreshPicked(); // the number of the photo in the list, or how it is turned, changed
            };
            _zoom.ValueChanged += (_, _) =>
            {
                _view.ThumbnailSize = _zoom.Value;
                MarkDirty();
            };

            _rotateLeftButton.Click += (_, _) => _view.RotateSelected(-1);
            _rotateRightButton.Click += (_, _) => _view.RotateSelected(1);
            _earlierButton.Click += (_, _) => _view.MoveSelectedBy(-1);
            _laterButton.Click += (_, _) => _view.MoveSelectedBy(1);
            _toStartButton.Click += (_, _) => _view.MoveSelectedToStart();
            _toEndButton.Click += (_, _) => _view.MoveSelectedToEnd();
            _toPlaceButton.Click += (_, _) => AskPlace();
            _leaveOutButton.Click += (_, _) => _view.ToggleLeftOut();
            _reverseButton.Click += (_, _) => _view.ReverseAll();
            _undoButton.Click += (_, _) => _view.Undo();
            _redoButton.Click += (_, _) => _view.Redo();
            _pickAllButton.Click += (_, _) => _view.Select([.. Enumerable.Range(0, _view.Entries.Count)]);

            _above.TextChanged += (_, _) => EditorChanged(above: true);
            _below.TextChanged += (_, _) => EditorChanged(above: false);

            _saveTimer.Tick += (_, _) => SaveBook();
            FormClosing += OnClosing;
            _makeButton.Click += async (_, _) => await MakeAsync();
            _openButton.Click += (_, _) => OpenMadeBook();
            _closeButton.Click += (_, _) => CloseOrCancel();
        }

        private void MarkDirty()
        {
            if (_applying || IsDisposed)
                return;

            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start(); // kept with the folder a moment after the last change, so that a crash does not take the work away
        }

        // ── Where the photos come from ──────────────────────────────────────

        /// <summary>
        /// Shows the book of the source that is picked: waits for its folder to be listed (the dialog is open meanwhile), reads the book that was kept with the folder, puts the settings in and the
        /// photos in the order that was kept (new photos at the end, in the sort order). Only the latest source asked for is shown.
        /// </summary>
        private async Task LoadSourceAsync()
        {
            var source = SelectedSource;
            var generation = ++_generation;
            IsLoading = true;
            _loadedFolder = null;
            _view.SetEntries([]);
            _countLabel.Text = source.Photos is null ? BookStrings.Counting : string.Empty;
            UpdateCounts();
            UpdateCommands();

            try
            {
                if (source.Photos is null)
                    await ListAsync(source);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && generation == _generation)
                {
                    _countLabel.Text = string.Format(BookStrings.CannotListFormat, ex.Message);
                    IsLoading = false;
                }
                return;
            }

            var folder = source.OutputFolder;
            var saved = await Task.Run(() => BookSettingsFile.Load(folder));

            if (IsDisposed || generation != _generation)
                return;

            var entries = BookOrder.Arrange(source.Photos!, saved?.ToEntries(), SelectedSortOrder);
            _applying = true;
            try
            {
                if (saved is not null)
                    ApplySettings(saved);
            }
            finally
            {
                _applying = false;
            }

            _loadedFolder = folder;
            _view.SetEntries(entries);
            if (entries.Count > 0)
                _view.Select(0);

            _dirty = false;
            _countLabel.Text = entries.Count > 0 ? string.Empty : source.EmptyText;
            IsLoading = false;
            UpdateCounts();
            UpdateCommands();
        }

        /// <summary>
        /// Looks up the group folders that are in the liked folder, offers each as a source, and counts the photos of the liked folder and of the groups, one folder after the other (a slow disk
        /// is not asked for everything at once). Done with the window open. A folder that cannot be read keeps a "?" for its count; picking it tries again and says why it failed.
        /// </summary>
        private async Task CountFoldersAsync()
        {
            IReadOnlyList<GroupFolder> groups = [];
            var likedCount = CountQuietlyAsync(SelectedSource);
            try
            {
                groups = await Task.Run(() => PhotoGroups.FindFolders(_likedFolder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // no group folders to offer
            }

            if (IsDisposed)
                return;

            var sources = groups.Select(group => new Source(Path.GetFileName(group.Path), group.Path, group.Path, BookStrings.EmptyFolder)).ToList();
            foreach (var source in sources)
                _sourceCombo.Items.Add(source);

            await likedCount;
            foreach (var source in sources)
            {
                if (IsDisposed)
                    return;

                await CountQuietlyAsync(source);
            }
        }

        private async Task CountQuietlyAsync(Source source)
        {
            try
            {
                await ListAsync(source);
            }
            catch (Exception)
            {
                // shown as "?" in the combo, and as the reason when the source is picked
            }
        }

        /// <summary>Lists the photos of a source folder on another thread, once; the count in the name of the source in the combo is brought up to date.</summary>
        private Task<List<PhotoFile>> ListAsync(Source source) => source.Listing ??= ListCoreAsync(source);

        private async Task<List<PhotoFile>> ListCoreAsync(Source source)
        {
            try
            {
                source.Photos = await Task.Run(() => VideoSources.Copies(source.Folder ?? throw new InvalidOperationException("The loaded photos are not listed from a folder."), _extensions));
                source.Failed = false;
            }
            catch
            {
                source.Failed = true;
                source.Listing = null;
                throw;
            }
            finally
            {
                if (!IsDisposed)
                    RefreshSourceItem(source);
            }

            return source.Photos;
        }

        /// <summary>Shows the name of the source with its new count in the combo, without that being a pick.</summary>
        private void RefreshSourceItem(Source source)
        {
            var index = _sourceCombo.Items.IndexOf(source);
            if (index < 0)
                return;

            _refreshingSourceItem = true;
            try
            {
                _sourceCombo.Items[index] = source;
            }
            finally
            {
                _refreshingSourceItem = false;
            }
        }

        /// <summary>The book goes to the folder its photos are in, unless the folder was changed by hand (typed, or browsed to).</summary>
        private void FollowOutputFolder()
        {
            var folder = SelectedSource.OutputFolder;
            if (string.Equals(_outputFolderBox.Text.Trim(), _automaticOutputFolder.Trim(), StringComparison.OrdinalIgnoreCase))
                _outputFolderBox.Text = folder;

            _automaticOutputFolder = folder;
        }

        private void BrowseOutputFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = BookStrings.SelectOutputFolder, SelectedPath = _outputFolderBox.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _outputFolderBox.Text = dialog.SelectedPath;
        }

        // ── What is kept with the folder ────────────────────────────────────

        private BookSettings CurrentSettings() => new()
        {
            Format = SelectedFormat.ToString(),
            Title = _titleBox.Text.Trim(),
            Author = _authorBox.Text.Trim(),
            PageFormat = ((Choice<PageFormat>)_pageCombo.SelectedItem!).Value.ToString(),
            Orientation = ((Choice<PageOrientation>)_orientationCombo.SelectedItem!).Value.ToString(),
            MarginMillimetres = (double)_marginNumeric.Value,
            FontFamily = _fontCombo.SelectedItem?.ToString(),
            PageNumbers = _pageNumbersCheck.Checked,
            Quality = ((Choice<PictureQuality>)_qualityCombo.SelectedItem!).Value.ToString(),
            OutputFolder = _outputFolderBox.Text.Trim(),
            OutputFileName = _fileNameBox.Text.Trim(),
            ThumbnailSize = _view.ThumbnailSize,
        };

        /// <summary>Puts the values the kept book has in the dialog (a value it does not have, or one that makes no sense, is left as it is).</summary>
        private void ApplySettings(BookSettings settings)
        {
            Pick(_formatCombo, settings.GetFormat());
            if (settings.Title is not null)
                _titleBox.Text = settings.Title;
            if (settings.Author is not null)
                _authorBox.Text = settings.Author;
            Pick(_pageCombo, settings.GetPageFormat());
            Pick(_orientationCombo, settings.GetOrientation());
            if (settings.MarginMillimetres is { } margin && !double.IsNaN(margin) && !double.IsInfinity(margin))
                _marginNumeric.Value = (decimal)Math.Clamp(margin, (double)_marginNumeric.Minimum, (double)_marginNumeric.Maximum);
            if (settings.FontFamily is { } font && _fontCombo.Items.Contains(font))
                _fontCombo.SelectedItem = font;
            if (settings.PageNumbers is { } numbers)
                _pageNumbersCheck.Checked = numbers;
            Pick(_qualityCombo, settings.GetQuality());

            if (!string.IsNullOrWhiteSpace(settings.OutputFolder))
                _outputFolderBox.Text = settings.OutputFolder;
            if (!string.IsNullOrWhiteSpace(settings.OutputFileName) && !AutomaticFileName.IsMatch(settings.OutputFileName))
                _fileNameBox.Text = settings.OutputFileName;
            if (settings.ThumbnailSize is { } size)
                _zoom.Value = Math.Clamp(size, _zoom.Minimum, _zoom.Maximum);

            UpdateFormatControls();
        }

        private static void Pick<T>(ComboBox combo, T? value) where T : struct
        {
            if (value is not { } wanted)
                return;

            var index = combo.Items.Cast<Choice<T>>().ToList().FindIndex(choice => EqualityComparer<T>.Default.Equals(choice.Value, wanted));
            if (index >= 0)
                combo.SelectedIndex = index;
        }

        /// <summary>Keeps the book with its folder, if anything was changed. The folder that is gone is not brought back; a book that could not be kept is said so in the status line.</summary>
        internal void SaveBook()
        {
            _saveTimer.Stop();
            if (!_dirty || _loadedFolder is null || _view.Entries.Count == 0)
                return;

            if (BookSettingsFile.TrySave(_loadedFolder, CurrentSettings(), _view.Entries, out var error))
                _dirty = false;
            else
                _statusLabel.Text = string.Format(BookStrings.StatusSaveFailedFormat, error);
        }

        // ── The format ──────────────────────────────────────────────────────

        /// <summary>The paper, the font and the page numbers are for a PDF only (an EPUB is set to the screen of its reader); the extension of the name follows the format.</summary>
        private void UpdateFormatControls()
        {
            var pdf = SelectedFormat == BookFormat.Pdf;
            foreach (var field in _pdfOnly)
                field.Enabled = pdf && !_making;

            var name = _fileNameBox.Text.Trim();
            var extension = Extension(SelectedFormat);
            if (AutomaticFileName.IsMatch(name))
                _fileNameBox.Text = NewAutomaticFileName(SelectedFormat);
            else if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
                _fileNameBox.Text = Path.ChangeExtension(name, extension);
        }

        // ── The photos and their order ──────────────────────────────────────

        private void UpdateCounts()
        {
            var entries = _view.Entries;
            var leftOut = entries.Count(entry => entry.LeftOut);
            _countsLabel.Text = entries.Count == 0 ? string.Empty : string.Format(BookStrings.CountsFormat, entries.Count - leftOut, leftOut);
        }

        /// <summary>The buttons that change the order work when something is picked; the others when there is something to change.</summary>
        private void UpdateCommands()
        {
            var picked = _view.SelectedIndices;
            var some = picked.Count > 0 && !_making;
            _earlierButton.Enabled = _laterButton.Enabled = _toStartButton.Enabled = _toEndButton.Enabled = _toPlaceButton.Enabled = some;
            _rotateLeftButton.Enabled = _rotateRightButton.Enabled = some;
            _leaveOutButton.Enabled = some;
            _leaveOutButton.Text = some && picked.All(index => _view.Entries[index].LeftOut) ? BookStrings.PutBack : BookStrings.LeaveOut;
            var several = _view.Entries.Count > 1 && !_making;
            _reverseButton.Enabled = several;
            _sortButton.Enabled = several;
            _pickAllButton.Enabled = _view.Entries.Count > 0 && !_making;
            _undoButton.Enabled = _view.CanUndo && !_making;
            _redoButton.Enabled = _view.CanRedo && !_making;
            _makeButton.Enabled = !_making && _view.Entries.Any(entry => !entry.LeftOut);
        }

        private void AskPlace()
        {
            var picked = _view.SelectedIndices;
            if (picked.Count == 0)
                return;

            using var dialog = new PlacePromptDialog(BookStrings.DialogTitle, string.Format(BookStrings.MoveToPlacePromptFormat, _view.Entries.Count), picked[0] + 1, _view.Entries.Count, _dark);
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _view.MoveSelectedToPlace(dialog.Place);
        }

        // ── What is written for the picked photo ────────────────────────────

        /// <summary>Shows the photo that is picked and what is written for it; with none picked, or several, there is nothing to write to.</summary>
        private void ShowPicked()
        {
            var picked = _view.SelectedIndices;
            _loadingEditors = true;
            try
            {
                if (picked.Count == 1)
                {
                    var index = picked[0];
                    var entry = _view.Entries[index];
                    _above.SetEditable(true);
                    _below.SetEditable(true);
                    _above.Rtf = entry.AboveRtf ?? string.Empty;
                    _below.Rtf = entry.BelowRtf ?? string.Empty;
                    _shownEntry = entry;
                    _pickedLabel.Text = string.Format(BookStrings.PickedFormat, index + 1, _view.Entries.Count, Path.GetFileName(entry.Path));
                    _ = ShowPreviewAsync(entry.Path, entry.Rotation);
                }
                else
                {
                    _above.Rtf = string.Empty;
                    _below.Rtf = string.Empty;
                    _above.SetEditable(false);
                    _below.SetEditable(false);
                    _shownEntry = null;
                    _pickedLabel.Text = picked.Count == 0 ? BookStrings.NothingPicked : string.Format(BookStrings.SeveralPickedFormat, picked.Count);
                    _previewGeneration++;
                    SetPreview(null);
                }
            }
            finally
            {
                _loadingEditors = false;
            }
        }

        /// <summary>
        /// The order or the turn of the photos changed. When the photo that is picked is still the one whose texts are in the editors, only its number and its picture are brought up to date (the
        /// editors are left alone, with the place of the cursor in them); otherwise what is picked is shown again.
        /// </summary>
        private void RefreshPicked()
        {
            if (_view.SelectedIndices is not [var index] || !ReferenceEquals(_view.Entries[index], _shownEntry))
            {
                ShowPicked();
                return;
            }

            var entry = _view.Entries[index];
            _pickedLabel.Text = string.Format(BookStrings.PickedFormat, index + 1, _view.Entries.Count, Path.GetFileName(entry.Path));
            _ = ShowPreviewAsync(entry.Path, entry.Rotation);
        }

        /// <summary>What was typed (or its look changed) goes to the photo that is picked, and its marks in the list are drawn again.</summary>
        private void EditorChanged(bool above)
        {
            if (_loadingEditors || _view.SelectedIndices is not [var index])
                return;

            var entry = _view.Entries[index];
            var editor = above ? _above : _below;
            var rtf = editor.IsEmpty ? null : editor.Rtf;
            if (above)
                entry.AboveRtf = rtf;
            else
                entry.BelowRtf = rtf;

            _view.RefreshEntry(index);
            MarkDirty();
        }

        private async Task ShowPreviewAsync(string path, int rotation)
        {
            var generation = ++_previewGeneration;
            Bitmap? bitmap = null;
            try
            {
                bitmap = await Task.Run(() =>
                {
                    var picture = ThumbnailCache.Decode(path, 640);
                    picture.RotateFlip(BookRotation.Normalize(rotation) switch
                    {
                        90 => RotateFlipType.Rotate90FlipNone,
                        180 => RotateFlipType.Rotate180FlipNone,
                        270 => RotateFlipType.Rotate270FlipNone,
                        _ => RotateFlipType.RotateNoneFlipNone,
                    }); // as the photo is turned in the book
                    return picture;
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or System.Runtime.InteropServices.ExternalException)
            {
                // no preview: the thumbnail says the photo cannot be read
            }

            if (IsDisposed || generation != _previewGeneration)
            {
                bitmap?.Dispose();
                return;
            }

            SetPreview(bitmap);
        }

        private void SetPreview(Bitmap? bitmap)
        {
            var old = _preview.Image;
            _preview.Image = bitmap;
            old?.Dispose();
        }

        // ── A source of photos ──────────────────────────────────────────────

        /// <summary>Where the photos of the book come from: the loaded ones (no folder), or the copies in a folder. Shown in the combo as its name and the number of its photos, "Liked (123)".</summary>
        private sealed class Source(string name, string? folder, string outputFolder, string emptyText)
        {
            public string? Folder { get; } = folder;

            /// <summary>Where the book is kept and made while this is the source: the folder the photos are in.</summary>
            public string OutputFolder { get; } = outputFolder;

            public string EmptyText { get; } = emptyText;

            /// <summary>The photos, once they are known (the loaded ones from the start, a folder when it is listed).</summary>
            public List<PhotoFile>? Photos { get; set; }

            /// <summary>The folder is being listed or is listed; null if that was not started, or failed.</summary>
            public Task<List<PhotoFile>>? Listing { get; set; }

            public bool Failed { get; set; }

            public override string ToString() => $"{name} ({(Photos is { } photos ? photos.Count.ToString("N0") : Failed ? "?" : "...")})";
        }
    }

    /// <summary>Asks for a number, the place a photo is moved to (a small window with a number box).</summary>
    internal sealed class PlacePromptDialog : Form
    {
        private readonly NumericUpDown _number = new();

        public PlacePromptDialog(string title, string prompt, int initial, int maximum, bool dark)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = DialogIcons.Book;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 120);

            var label = new Label { Text = prompt, Location = new Point(12, 12), Size = new Size(356, 40) };
            _number.Location = new Point(12, 56);
            _number.Width = 100;
            _number.Minimum = 1;
            _number.Maximum = Math.Max(1, maximum);
            _number.Value = Math.Clamp(initial, 1, Math.Max(1, maximum));
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(212, 84), Size = new Size(75, 26) };
            var cancel = new Button { Text = BookStrings.CancelButton, DialogResult = DialogResult.Cancel, Location = new Point(293, 84), Size = new Size(75, 26) };
            Controls.AddRange([label, _number, ok, cancel]);
            AcceptButton = ok;
            CancelButton = cancel;
            ThemeManager.Apply(this, dark);
            label.BackColor = Color.Transparent;
            _number.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            Shown += (_, _) => _number.Select(0, _number.Text.Length);
        }

        /// <summary>The number that was asked for (from 1).</summary>
        public int Place => (int)_number.Value;
    }
}
