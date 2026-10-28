using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Photo search: the button that indexes the folder in the background, the search box, the results (shown like a
    /// folder of their own) and the faces of the shown photo, which can be named and searched for.
    /// The analysis itself is in PhotoLiker.Search.
    /// </summary>
    public partial class MainForm
    {
        private readonly ModelHub _models = new();
        private FolderIndex? _folderSearch;       // the index of the folder that is shown
        private FolderIndex? _indexingSearch;     // the index of the folder that is being indexed, if any
        private CancellationTokenSource? _indexCts;
        private Task? _indexTask;
        private int _attachGeneration;
        private int _searchGeneration;

        // Results are shown in place of the folder until they are closed
        private bool _resultsActive;
        private string _resultsTitle = string.Empty;
        private string _returnFile = string.Empty;

        // Faces of the shown photo
        private IReadOnlyList<FaceLabel> _faces = [];
        private FaceLabel? _hoverFace;
        private bool _marking;
        private Point _markStart;
        private Point _markEnd;

        private ToolStripButton _indexButton = null!;
        private ToolStripProgressBar _indexProgress = null!;
        private ToolStripLabel _indexLabel = null!;
        private ToolStripTextBox _searchBox = null!;
        private ToolStripButton _searchButton = null!;
        private ToolStripButton _closeResultsButton = null!;
        private ToolStripButton _similarButton = null!;
        private ToolStripButton _facesButton = null!;
        private ToolStripDropDownButton _transferButton = null!;
        private ToolStripMenuItem _exportItem = null!;
        private ToolStripMenuItem _importItem = null!;
        private bool _transferBusy; // an index is being exported or imported

        private bool FacesMode => _facesButton.Checked && _folderSearch is not null;

        // ── the strip above the viewer ─────────────────────────────────────

        private void InitializeSearch()
        {
            _indexButton = new ToolStripButton(SearchStrings.IndexButton)
            {
                Name = "indexToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = SearchStrings.IndexTooltip,
            };
            _indexButton.Click += IndexButton_Click;

            _indexProgress = new ToolStripProgressBar { Name = "indexToolStripProgressBar", Width = 110, Visible = false };
            _indexLabel = new ToolStripLabel { Name = "indexToolStripLabel", AutoSize = false, Width = 250, TextAlign = ContentAlignment.MiddleLeft };

            _searchBox = new ToolStripTextBox { Name = "searchToolStripTextBox", AutoSize = false, Width = 220, ToolTipText = SearchStrings.SearchTooltip };
            _searchBox.TextBox.PlaceholderText = SearchStrings.SearchPlaceholder;
            _searchBox.TextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _searchBox.TextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _searchBox.KeyDown += SearchBox_KeyDown;

            _searchButton = new ToolStripButton(SearchStrings.SearchButton)
            {
                Name = "searchToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Search(),
                ToolTipText = SearchStrings.SearchTooltip,
            };
            _searchButton.Click += (_, _) => RunSearch(_searchBox.Text);

            _closeResultsButton = new ToolStripButton(SearchStrings.CloseResultsButton)
            {
                Name = "closeResultsToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = SearchStrings.CloseResultsTooltip,
                Visible = false,
            };
            _closeResultsButton.Click += (_, _) => CloseSearchResults();

            _similarButton = new ToolStripButton(SearchStrings.SimilarButton)
            {
                Name = "similarToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = SearchStrings.SimilarTooltip,
            };
            _similarButton.Click += (_, _) => RunSimilarSearch();

            _facesButton = new ToolStripButton(SearchStrings.FacesButton)
            {
                Name = "facesToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = AppIcons.FaceRecognition(),
                ToolTipText = SearchStrings.FacesTooltip,
                CheckOnClick = true,
            };
            _facesButton.CheckedChanged += (_, _) =>
            {
                _hoverFace = null;
                _marking = false;
                pictureBox1.Cursor = Cursors.Default;
                pictureBox1.Invalidate();
            };

            _exportItem = new ToolStripMenuItem(SearchStrings.ExportIndexMenu) { Name = "exportIndexMenuItem" };
            _exportItem.Click += (_, _) => ExportIndex();
            _importItem = new ToolStripMenuItem(SearchStrings.ImportIndexMenu) { Name = "importIndexMenuItem" };
            _importItem.Click += (_, _) => ImportIndex();
            _transferButton = new ToolStripDropDownButton(SearchStrings.IndexFileButton)
            {
                Name = "indexFileToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = SearchStrings.IndexFileTooltip,
            };
            _transferButton.DropDownItems.AddRange([_exportItem, _importItem]);
            _transferButton.DropDownOpening += (_, _) => ApplyMenuTheme(_transferButton.DropDown);

            var strip = new ToolStrip { Name = "searchToolStrip", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            strip.Items.AddRange([
                _transferButton,
                new ToolStripSeparator(),
                _indexButton, _indexProgress, _indexLabel,
                new ToolStripSeparator(),
                _searchBox, _searchButton, _closeResultsButton, _similarButton, _facesButton,
            ]);
            splitContainer3.Panel1.Controls.Add(strip);

            pictureBox1.MouseLeave += (_, _) =>
            {
                if (_hoverFace is not null)
                {
                    _hoverFace = null;
                    pictureBox1.Cursor = Cursors.Default;
                    pictureBox1.Invalidate();
                }
            };

            UpdateSearchControls();
        }

        /// <summary>Enables and labels the controls for what is possible right now.</summary>
        private void UpdateSearchControls()
        {
            var folderKnown = !string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder);
            var indexing = _indexCts is not null;
            var index = _folderSearch?.Index;
            var photoCount = index?.PhotoCount ?? 0;

            _indexButton.Enabled = folderKnown;
            _indexButton.Text = indexing ? SearchStrings.StopIndexButton : photoCount > 0 ? SearchStrings.UpdateIndexButton : SearchStrings.IndexButton;
            _indexButton.ToolTipText = indexing ? SearchStrings.StopIndexTooltip : SearchStrings.IndexTooltip;
            _indexProgress.Visible = indexing;

            if (!indexing)
            {
                var failed = index?.FailedCount ?? 0;
                _indexLabel.Text = photoCount == 0 ? SearchStrings.NotIndexed
                    : failed > 0 ? string.Format(SearchStrings.IndexedWithFailuresFormat, photoCount, index!.FaceCount, failed)
                    : string.Format(SearchStrings.IndexedFormat, photoCount, index!.FaceCount);
                _indexLabel.ToolTipText = _indexLabel.Text;
            }

            // Searching works as soon as the first photos are in the index, also while the rest is still being done
            var searchable = photoCount > 0;
            _searchBox.Enabled = searchable;
            _searchButton.Enabled = searchable;
            _facesButton.Enabled = searchable;
            _closeResultsButton.Visible = _resultsActive;
            _transferButton.Enabled = folderKnown;
            _exportItem.Enabled = searchable && !_transferBusy;
            _importItem.Enabled = folderKnown && !indexing && !_transferBusy;
            UpdateSimilarEnabled();
        }

        private void UpdateSimilarEnabled() =>
            _similarButton.Enabled = pictureBox1.Tag is string file && _folderSearch?.Index.FindFullPath(file)?.Clip is not null;

        private void UpdateAutoComplete()
        {
            var names = new AutoCompleteStringCollection();
            if (_folderSearch is { } search)
                names.AddRange([.. search.People.Names]);
            _searchBox.TextBox.AutoCompleteCustomSource = names;
        }

        // ── the index of the shown folder ──────────────────────────────────

        private static bool SameFolder(string a, string b) =>
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

        /// <summary>A folder is being loaded: leave the results, and open the index of the folder if it has one.</summary>
        private async void AttachFolderSearch()
        {
            _resultsActive = false;
            _resultsTitle = string.Empty;
            var folder = _currentConfig.CurrentFolder;
            var generation = ++_attachGeneration;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                ShowIndex(null);
                return;
            }
            if (_folderSearch is { } shown && SameFolder(shown.Folder, folder))
            {
                UpdateSearchControls();
                return;
            }
            if (_indexingSearch is { } running && SameFolder(running.Folder, folder))
            {
                ShowIndex(running);
                return;
            }

            ShowIndex(null);
            FolderIndex? opened = null;
            try
            {
                // Without an index file there is nothing to open (and nothing is created until the folder is indexed)
                if (File.Exists(SearchPaths.IndexFile(folder)))
                    opened = await Task.Run(() => FolderIndex.Open(folder));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(SearchStrings.StatusSearchError, ex.Message));
            }

            if (generation != _attachGeneration)
            {
                opened?.Dispose();
                return;
            }
            if (_indexingSearch is { } indexing && SameFolder(indexing.Folder, folder))
            {
                opened?.Dispose(); // indexing started meanwhile: its index is the live one
                ShowIndex(indexing);
                return;
            }

            ShowIndex(opened);
            if (opened?.People.LoadError is { } error)
                SetStatus(string.Format(SearchStrings.StatusPeopleFileError, error));
        }

        /// <summary>Makes this the index of the shown folder.</summary>
        private void ShowIndex(FolderIndex? index)
        {
            var old = _folderSearch;
            _folderSearch = index;
            if (old is not null && !ReferenceEquals(old, index) && !ReferenceEquals(old, _indexingSearch))
                old.Dispose();

            UpdateAutoComplete();
            UpdateFaces();
            UpdateSearchControls();
        }

        // ── indexing ───────────────────────────────────────────────────────

        private async void IndexButton_Click(object? sender, EventArgs e)
        {
            if (_indexCts is { } running)
            {
                running.Cancel();
                SetStatus(SearchStrings.StatusIndexStopping);
                return;
            }

            var folder = _currentConfig.CurrentFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                SetStatus(SearchStrings.StatusNoFolder);
                return;
            }

            if (!_models.IsDownloaded &&
                MessageBox.Show(this, SearchStrings.DownloadQuestion, SearchStrings.DownloadTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            var cancellation = _indexCts = new CancellationTokenSource();
            _indexLabel.Text = SearchStrings.IndexScanning;
            UpdateSearchControls();

            FolderIndex? index = null;
            try
            {
                index = _folderSearch is { } shown && SameFolder(shown.Folder, folder) ? shown : await Task.Run(() => FolderIndex.Open(folder));
                _indexingSearch = index;

                // The user may have opened another folder while the index file was loading
                if (!ReferenceEquals(_folderSearch, index) && SameFolder(_currentConfig.CurrentFolder, folder))
                    ShowIndex(index);

                var task = index.IndexAsync(_models, _currentConfig.LikedFolder, _currentConfig.Extensions, new Progress<IndexProgress>(OnIndexProgress), cancellation.Token);
                _indexTask = task;
                SetStatus(DescribeIndexSummary(await task));
            }
            catch (OperationCanceledException)
            {
                SetStatus(string.Format(SearchStrings.StatusIndexStopped, index?.Index.PhotoCount ?? 0));
            }
            catch (Exception ex)
            {
                var message = string.Format(SearchStrings.StatusIndexError, ex.Message);
                SetStatus(message);
                MessageBox.Show(this, message, SearchStrings.IndexButton, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                cancellation.Dispose();
                _indexCts = null;
                _indexTask = null;
                _indexingSearch = null;
                if (index is not null && !ReferenceEquals(_folderSearch, index))
                    index.Dispose();

                UpdateAutoComplete();
                UpdateFaces();
                UpdateSearchControls();
            }
        }

        private static string DescribeIndexSummary(IndexSummary summary)
        {
            var removed = summary.Removed > 0 ? string.Format(SearchStrings.StatusIndexRemovedPart, summary.Removed) : string.Empty;
            if (summary.Cancelled)
                return string.Format(SearchStrings.StatusIndexStopped, summary.Indexed + summary.Failed);
            if (summary.Indexed + summary.Failed == 0)
            {
                return summary.DatesRead > 0
                    ? string.Format(SearchStrings.StatusDatesRead, summary.DatesRead, FormatDuration(summary.Elapsed), removed)
                    : string.Format(SearchStrings.StatusIndexUpToDate, removed);
            }

            var failed = summary.Failed > 0 ? string.Format(SearchStrings.StatusIndexFailedPart, summary.Failed) : string.Empty;
            return string.Format(SearchStrings.StatusIndexDone, summary.Indexed, summary.Faces, FormatDuration(summary.Elapsed), failed + removed);
        }

        private void OnIndexProgress(IndexProgress progress)
        {
            if (_indexCts is null)
                return; // the last report of a run that is over

            switch (progress.Phase)
            {
                case IndexPhase.Scanning:
                    ShowMarquee(SearchStrings.IndexScanning);
                    break;

                case IndexPhase.LoadingModels:
                    ShowMarquee(SearchStrings.IndexLoadingModels);
                    break;

                case IndexPhase.DownloadingModels:
                    ShowBar(progress.Done, progress.Total);
                    _indexLabel.Text = progress.Message;
                    _indexLabel.ToolTipText = progress.Message;
                    break;

                case IndexPhase.ReadingDates:
                    ShowBar(progress.Done, progress.Total);
                    _indexLabel.Text = progress.Remaining is { } left
                        ? string.Format(SearchStrings.IndexDatesProgressFormat, progress.Done, progress.Total, progress.PhotosPerSecond, FormatDuration(left))
                        : string.Format(SearchStrings.IndexDatesProgressNoEstimateFormat, progress.Done, progress.Total);
                    _indexLabel.ToolTipText = _indexLabel.Text;
                    break;

                default:
                    ShowBar(progress.Done, progress.Total);
                    _indexLabel.Text = progress.Remaining is { } remaining
                        ? string.Format(SearchStrings.IndexProgressFormat, progress.Done, progress.Total, progress.PhotosPerSecond, FormatDuration(remaining))
                        : string.Format(SearchStrings.IndexProgressNoEstimateFormat, progress.Done, progress.Total);
                    _indexLabel.ToolTipText = string.Format(SearchStrings.IndexProgressTooltipFormat, progress.Done, progress.Total, progress.Faces);

                    if (progress.Done > 0 && !_searchBox.Enabled && ReferenceEquals(_folderSearch, _indexingSearch))
                        UpdateSearchControls();
                    break;
            }
        }

        private void ShowMarquee(string text)
        {
            _indexProgress.Style = ProgressBarStyle.Marquee;
            _indexProgress.MarqueeAnimationSpeed = 30;
            _indexLabel.Text = text;
            _indexLabel.ToolTipText = text;
        }

        private void ShowBar(int done, int total)
        {
            _indexProgress.Style = ProgressBarStyle.Continuous;
            _indexProgress.Maximum = Math.Max(1, total);
            _indexProgress.Value = Math.Clamp(done, 0, _indexProgress.Maximum);
        }

        internal static string FormatDuration(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min"
            : time.TotalMinutes >= 1 ? $"{(int)Math.Ceiling(time.TotalMinutes)} min"
            : $"{(int)time.TotalSeconds} s";

        // ── searching ──────────────────────────────────────────────────────

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;
            RunSearch(_searchBox.Text);
        }

        private async void RunSearch(string text)
        {
            var search = _folderSearch;
            text = text.Trim();
            if (search is null || text.Length == 0)
                return;

            // The name of a person is searched in the faces, and a date in the dates: neither needs the text model
            if (!_models.ClipDownloaded && search.NeedsTextModel(text))
            {
                SetStatus(SearchStrings.StatusModelMissing);
                return;
            }

            var generation = ++_searchGeneration;
            SetStatus(SearchStrings.StatusSearching);
            SearchResult result;
            try
            {
                result = await Task.Run(() => search.Search(_models, text));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(SearchStrings.StatusSearchError, ex.Message));
                return;
            }

            if (generation == _searchGeneration && ReferenceEquals(search, _folderSearch))
                await ShowResultsAsync(result);
        }

        private void RunSimilarSearch()
        {
            if (_folderSearch is not { } search || pictureBox1.Tag is not string file)
                return;

            var result = search.SearchSimilar(file);
            if (result.Hits.Count == 0)
            {
                SetStatus(SearchStrings.StatusSimilarNotIndexed);
                return;
            }
            _ = ShowResultsAsync(result);
        }

        /// <summary>Shows the photos that were found in place of the folder, so that they can be browsed, liked and grouped like any photos.</summary>
        private async Task ShowResultsAsync(SearchResult result)
        {
            if (result.Hits.Count == 0)
            {
                SetStatus(string.Format(SearchStrings.StatusNothingFound, result.Description) + DateNotes(result));
                return;
            }

            var files = result.Hits
                .Select(hit => new PhotoFile(hit.FullPath, string.Empty) { Length = hit.Length, LastWriteTimeUtc = hit.ModifiedUtc })
                .ToList();

            var unfinished = _indexCts is not null && ReferenceEquals(_folderSearch, _indexingSearch);
            var title = string.Format(result.Weak ? SearchStrings.ResultsTitleWeakFormat : SearchStrings.ResultsTitleFormat, result.Description, files.Count)
                + (unfinished ? SearchStrings.ResultsPartialNote : string.Empty);

            if (!_resultsActive)
                _returnFile = _currentConfig.CurrentFilePath;

            ClearDisplay();
            _resultsActive = true;
            _resultsTitle = title;
            var generation = _folderGeneration;

            var likedFolder = _currentConfig.LikedFolder;
            var (liked, groups) = await Task.Run(() => (LikedLibrary.Scan(likedFolder, files), GroupLibrary.Scan(likedFolder, files)));
            if (generation != _folderGeneration)
                return; // something else was opened meanwhile

            liked.Apply(files);
            _liked = liked;
            _groups = groups;
            _currentConfig.Files = files;
            _indexByPath = new Dictionary<string, int>(files.Count, StringComparer.Ordinal);
            for (var i = 0; i < files.Count; i++)
                _indexByPath[files[i].OriginalFilePath] = i;

            UpdateSearchControls();
            SetStatus(string.Format(SearchStrings.StatusResultsShown, result.Description, files.Count, result.SearchedPhotos) + DateNotes(result));
            LoadFirstPhoto(files[0].OriginalFilePath);
            settingsPropertyGrid.Refresh();
        }

        /// <summary>
        /// What to know about the dates behind a search that was limited by a date: how many photos are dated by their file
        /// instead of by the camera, and that an index made before dates were kept still has to read them.
        /// </summary>
        private string DateNotes(SearchResult result)
        {
            if (result.Dates is null)
                return string.Empty;

            var notes = result.FileDated > 0 ? string.Format(SearchStrings.StatusFileDatedNote, result.FileDated) : string.Empty;
            if (_folderSearch?.Index.DatesPending is > 0 and var pending)
                notes += string.Format(SearchStrings.StatusDatesPendingNote, pending);
            return notes;
        }

        /// <summary>Leaves the results and shows the folder again, at the photo that was shown before.</summary>
        private void CloseSearchResults()
        {
            if (!_resultsActive)
                return;

            _resultsActive = false;
            _resultsTitle = string.Empty;
            if (!string.IsNullOrEmpty(_returnFile))
                _currentConfig.CurrentFilePath = _returnFile;

            ClearDisplay();
            LoadFolder();
        }

        // ── faces ──────────────────────────────────────────────────────────

        /// <summary>Looks up the faces of the shown photo (from the index in memory, so it is instant).</summary>
        private void UpdateFaces()
        {
            _hoverFace = null;
            _faces = _folderSearch is { } search && pictureBox1.Tag is string file ? search.DescribeFaces(file) : [];
            UpdateSimilarEnabled();
            pictureBox1.Invalidate();
        }

        /// <summary>Where the photo is drawn inside the picture box.</summary>
        private RectangleF GetImageRectangle()
        {
            var image = pictureBox1.Image;
            var client = pictureBox1.ClientRectangle;
            if (image is null)
                return RectangleF.Empty;
            if (pictureBox1.SizeMode != PictureBoxSizeMode.Zoom)
                return client; // stretched over the whole box while zoomed

            var scale = Math.Min((float)client.Width / image.Width, (float)client.Height / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            return new RectangleF((client.Width - width) / 2, (client.Height - height) / 2, width, height);
        }

        private static RectangleF ToScreen(IndexedFace face, RectangleF image) =>
            new(image.X + face.X * image.Width, image.Y + face.Y * image.Height, face.Width * image.Width, face.Height * image.Height);

        private FaceLabel? HitTestFace(Point point)
        {
            var image = GetImageRectangle();
            return _faces
                .Where(label => ToScreen(label.Face, image).Contains(point))
                .OrderBy(label => label.Face.Width * label.Face.Height) // the smaller face if they overlap
                .FirstOrDefault();
        }

        private void DrawFaces(Graphics g)
        {
            if (!FacesMode || pictureBox1.Image is null)
                return;

            var image = GetImageRectangle();
            using var font = new Font(Font.FontFamily, MainFormConstants.FaceNameFontSize, FontStyle.Bold);
            using var nameBackground = new SolidBrush(Color.FromArgb(MainFormConstants.SavedInBgAlpha, Color.Black));

            foreach (var label in _faces)
            {
                var box = ToScreen(label.Face, image);
                var colour = label.Confirmed ? Color.LimeGreen : label.Name is not null ? Color.Gold : Color.White;
                using var pen = new Pen(colour, ReferenceEquals(label, _hoverFace) ? 3f : 2f)
                {
                    DashStyle = label.Name is not null && !label.Confirmed ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid,
                };
                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);

                if (label.Name is { } name)
                {
                    var text = label.Confirmed ? name : name + "?";
                    var size = g.MeasureString(text, font);
                    var top = Math.Max(0, box.Y - size.Height);
                    g.FillRectangle(nameBackground, box.X, top, size.Width, size.Height);
                    using var textBrush = new SolidBrush(colour);
                    g.DrawString(text, font, textBrush, box.X, top);
                }
            }

            if (_marking)
            {
                using var pen = new Pen(Color.White, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(pen, MarkRectangle());
            }
        }

        private Rectangle MarkRectangle() => Rectangle.FromLTRB(
            Math.Min(_markStart.X, _markEnd.X), Math.Min(_markStart.Y, _markEnd.Y),
            Math.Max(_markStart.X, _markEnd.X), Math.Max(_markStart.Y, _markEnd.Y));

        /// <summary>A click on a face opens its menu; Shift + drag marks a face the detector did not find. Returns true if the click was used.</summary>
        private bool HandleFaceMouseDown(MouseEventArgs e)
        {
            if (!FacesMode || pictureBox1.Image is null)
                return false;

            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) != 0)
            {
                _marking = true;
                _markStart = _markEnd = e.Location;
                pictureBox1.Cursor = Cursors.Cross;
                return true;
            }

            if (e.Button is MouseButtons.Left or MouseButtons.Right && HitTestFace(e.Location) is { } label)
            {
                ShowFaceMenu(label, e.Location);
                return true;
            }
            return false;
        }

        private bool HandleFaceMouseMove(MouseEventArgs e)
        {
            if (_marking)
            {
                _markEnd = e.Location;
                pictureBox1.Invalidate();
                return true;
            }

            if (FacesMode && !_panning)
            {
                var hit = HitTestFace(e.Location);
                if (!ReferenceEquals(hit, _hoverFace))
                {
                    _hoverFace = hit;
                    pictureBox1.Cursor = hit is null ? Cursors.Default : Cursors.Hand;
                    pictureBox1.Invalidate();
                }
            }
            return false;
        }

        private bool HandleFaceMouseUp(MouseEventArgs e)
        {
            if (!_marking)
                return false;

            _marking = false;
            pictureBox1.Cursor = Cursors.Default;

            var image = GetImageRectangle();
            var area = RectangleF.Intersect(MarkRectangle(), image);
            if (area.Width < MainFormConstants.MinMarkedFaceSize || area.Height < MainFormConstants.MinMarkedFaceSize)
            {
                SetStatus(SearchStrings.StatusFaceTooSmall);
                pictureBox1.Invalidate();
                return true;
            }

            _ = MarkFaceAsync(
                (area.X - image.X) / image.Width, (area.Y - image.Y) / image.Height,
                area.Width / image.Width, area.Height / image.Height);
            return true;
        }

        private async Task MarkFaceAsync(float x, float y, float width, float height)
        {
            if (_folderSearch is not { } search || pictureBox1.Tag is not string file)
                return;

            SetStatus(SearchStrings.StatusFaceMarking);
            IndexedFace face;
            try
            {
                face = await Task.Run(() => search.MarkFace(_models, file, x, y, width, height));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(SearchStrings.StatusFaceMarkError, ex.Message));
                pictureBox1.Invalidate();
                return;
            }

            if (!ReferenceEquals(search, _folderSearch) || !string.Equals(pictureBox1.Tag as string, file, StringComparison.Ordinal))
                return; // another photo is shown by now

            UpdateFaces();
            if (_faces.FirstOrDefault(label => ReferenceEquals(label.Face, face)) is { } marked)
            {
                var image = GetImageRectangle();
                var box = ToScreen(face, image);
                ShowFaceMenu(marked, new Point((int)(box.X + box.Width / 2), (int)(box.Y + box.Height / 2)));
            }
        }

        private void ShowFaceMenu(FaceLabel label, Point at)
        {
            if (_folderSearch is not { } search)
                return;

            var menu = new ContextMenuStrip();
            ApplyMenuTheme(menu);

            var heading = label.Name is null ? SearchStrings.FaceWhoIsThis
                : label.Confirmed ? label.Name
                : string.Format(SearchStrings.FaceLooksLikeFormat, label.Name);
            menu.Items.Add(new ToolStripLabel(heading) { Font = new Font(Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());

            foreach (var name in search.People.Names)
            {
                var item = new ToolStripMenuItem(name) { Checked = label.Confirmed && string.Equals(name, label.Name, StringComparison.CurrentCultureIgnoreCase) };
                item.Click += (_, _) => NameFace(label.Face, name);
                menu.Items.Add(item);
            }

            var create = new ToolStripMenuItem(SearchStrings.FaceNewPerson);
            create.Click += (_, _) => NameFaceAsNewPerson(label.Face);
            menu.Items.Add(create);
            menu.Items.Add(new ToolStripSeparator());

            var find = new ToolStripMenuItem(label.Name is null ? SearchStrings.FaceFindPhotos : string.Format(SearchStrings.FaceFindPhotosOfFormat, label.Name));
            find.Click += (_, _) => FindPhotosOf(label);
            menu.Items.Add(find);

            if (label.Confirmed)
            {
                var remove = new ToolStripMenuItem(SearchStrings.FaceRemoveName);
                remove.Click += (_, _) => RemoveFaceName(label.Face);
                menu.Items.Add(remove);
            }

            var delete = new ToolStripMenuItem(SearchStrings.FaceDelete);
            delete.Click += (_, _) => DeleteFace(label.Face);
            menu.Items.Add(delete);

            // Disposed after the click has been handled
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            menu.Show(pictureBox1, at);
        }

        private void NameFace(IndexedFace face, string name)
        {
            if (_folderSearch is not { } search)
                return;

            try
            {
                search.NameFace(face, name);
                SetStatus(string.Format(SearchStrings.StatusFaceNamed, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(SearchStrings.StatusFaceNameNotSaved, name, ex.Message));
            }

            UpdateAutoComplete();
            UpdateFaces();
        }

        private void NameFaceAsNewPerson(IndexedFace face)
        {
            if (_folderSearch is not { } search)
                return;

            using var dialog = new NamePersonDialog(search.People.Names);
            ThemeManager.Apply(dialog, _currentConfig.IsDarkTheme);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.PersonName.Length > 0)
                NameFace(face, dialog.PersonName);
        }

        private void RemoveFaceName(IndexedFace face)
        {
            try
            {
                _folderSearch?.ClearName(face);
                SetStatus(SearchStrings.StatusFaceNameRemoved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(SearchStrings.StatusFaceNameNotSaved, string.Empty, ex.Message));
            }

            UpdateAutoComplete();
            UpdateFaces();
        }

        private void DeleteFace(IndexedFace face)
        {
            try
            {
                _folderSearch?.DeleteFace(face);
                SetStatus(SearchStrings.StatusFaceDeleted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(SearchStrings.StatusFaceNameNotSaved, string.Empty, ex.Message));
            }

            UpdateAutoComplete();
            UpdateFaces();
        }

        /// <summary>A named face is searched by the name (all the examples of the person), any other by its own look.</summary>
        private void FindPhotosOf(FaceLabel label)
        {
            if (_folderSearch is not { } search)
                return;

            var result = label.Confirmed && label.Name is not null ? search.SearchPerson(label.Name) : search.SearchPerson(label.Face, label.Name);
            _ = ShowResultsAsync(result);
        }

        /// <summary>Pop-up menus are not part of the form's control tree, so the theme is applied to them when they open.</summary>
        private void ApplyMenuTheme(ToolStripDropDown menu)
        {
            var dark = _currentConfig.IsDarkTheme;
            menu.Renderer = dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
            menu.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
            menu.ForeColor = dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
        }

        // ── moving the index to another computer ───────────────────────────

        private async void ExportIndex()
        {
            if (_folderSearch is not { } search || _transferBusy)
                return;

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(search.Folder));
            using var dialog = new SaveFileDialog
            {
                Title = SearchStrings.ExportIndexTitle,
                Filter = SearchStrings.IndexFileFilter,
                DefaultExt = SearchStrings.IndexFileExtension,
                AddExtension = true,
                OverwritePrompt = true,
                FileName = (name.Length > 0 ? name : "photos") + "." + SearchStrings.IndexFileExtension,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var file = dialog.FileName;
            _transferBusy = true;
            UpdateSearchControls();
            SetStatus(SearchStrings.StatusExporting);
            try
            {
                await Task.Run(() => search.Index.ExportTo(file));
                SetStatus(string.Format(SearchStrings.StatusExported, file, search.Index.PhotoCount, search.Index.FaceCount, new FileInfo(file).Length / 1048576.0));
            }
            catch (Exception ex)
            {
                var message = string.Format(SearchStrings.StatusExportError, ex.Message);
                SetStatus(message);
                MessageBox.Show(this, message, SearchStrings.ExportIndexTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _transferBusy = false;
                UpdateSearchControls();
            }
        }

        /// <summary>Makes an exported index the index of the open folder (after asking), and opens it.</summary>
        private async void ImportIndex()
        {
            var folder = _currentConfig.CurrentFolder;
            if (string.IsNullOrWhiteSpace(folder) || _transferBusy)
                return;
            if (_indexCts is not null)
            {
                SetStatus(SearchStrings.StatusImportBusy);
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = SearchStrings.ImportIndexTitle,
                Filter = SearchStrings.IndexFileFilter,
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var file = dialog.FileName;
            _transferBusy = true;
            UpdateSearchControls();
            var closed = false;
            try
            {
                var info = await Task.Run(() => PhotoIndex.Inspect(file));

                var madeFor = info.Root is null ? string.Empty
                    : string.Format(SearchStrings.ImportMadeForFormat, info.Root, info.ExportedAt is { } at ? $" on {at:g}" : string.Empty);
                var current = _folderSearch?.Index.PhotoCount ?? 0;
                var replaces = current > 0 ? string.Format(SearchStrings.ImportReplaceNoteFormat, current) : string.Empty;
                var question = string.Format(SearchStrings.ImportQuestionFormat, folder, info.Photos, info.Faces, madeFor, replaces);
                if (MessageBox.Show(this, question, SearchStrings.ImportIndexTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                // A file that is open can not be replaced
                ShowIndex(null);
                closed = true;
                await Task.Run(() => PhotoIndex.Import(folder, file));
                SetStatus(string.Format(SearchStrings.StatusImported, info.Photos, info.Faces));
            }
            catch (Exception ex)
            {
                var message = string.Format(SearchStrings.StatusImportError, ex.Message);
                SetStatus(message);
                MessageBox.Show(this, message, SearchStrings.ImportIndexTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                // The imported index is opened; if the import failed this is the one the folder had before
                if (closed)
                    await ReopenIndexAsync(folder);

                _transferBusy = false;
                UpdateSearchControls();
            }
        }

        private async Task ReopenIndexAsync(string folder)
        {
            FolderIndex? opened = null;
            try
            {
                if (File.Exists(SearchPaths.IndexFile(folder)))
                    opened = await Task.Run(() => FolderIndex.Open(folder));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(SearchStrings.StatusSearchError, ex.Message));
            }

            // The user may have opened another folder in the meantime
            if (opened is not null && _folderSearch is null && SameFolder(_currentConfig.CurrentFolder, folder))
                ShowIndex(opened);
            else
                opened?.Dispose();
        }

        // ── closing ────────────────────────────────────────────────────────

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_indexCts is { } running)
            {
                if (MessageBox.Show(this, SearchStrings.ClosingWhileIndexing, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                running.Cancel();
                try
                {
                    _indexTask?.Wait(TimeSpan.FromSeconds(10)); // the work in progress is a few photos, finished within seconds
                }
                catch
                {
                    // stopped, which is what was asked for
                }
            }
            base.OnFormClosing(e);
        }

        private void ShutDownSearch()
        {
            _attachGeneration++;
            _searchGeneration++;
            _folderSearch?.Dispose();
            if (!ReferenceEquals(_indexingSearch, _folderSearch))
                _indexingSearch?.Dispose();
            _models.Dispose();
        }
    }
}
