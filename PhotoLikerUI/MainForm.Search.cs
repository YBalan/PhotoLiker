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
        private string _resultsRequest = string.Empty;   // what was searched for; names a group folder that Alt + a group key makes
        private string _returnFile = string.Empty;

        // The last search, kept so that the precision slider can run it again with other cutoffs. Cleared when the results are closed or another folder is shown.
        private Func<SearchTuning, SearchResult>? _repeatSearch;
        private bool _syncingPrecision;
        private readonly System.Windows.Forms.Timer _precisionTimer = new() { Interval = MainFormConstants.PrecisionSettleMs };
        private readonly ToolTip _precisionTip = new();

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
        private TrackBar _precisionBar = null!;
        private ToolStripControlHost _precisionHost = null!;
        private ToolStripComboBox _modelBox = null!;
        private bool _syncingModel;
        private ToolStripLabel _precisionWideLabel = null!;
        private ToolStripLabel _precisionPreciseLabel = null!;
        private ToolStripLabel _modelLabel = null!;
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

            // Wide to precise: how much the searches let through (the cutoffs are in SearchTuning), kept in the folder's settings file
            _precisionBar = new TrackBar
            {
                Minimum = 0,
                Maximum = Settings.MaxSearchPrecision,
                Value = Settings.DefaultSearchPrecision,
                SmallChange = 5,
                LargeChange = 10,
                TickStyle = TickStyle.None,
                TabStop = false, // the arrow keys are the viewer's
                AutoSize = false,
                Size = new Size(MainFormConstants.PrecisionSliderWidth, 22),
            };
            _precisionBar.ValueChanged += PrecisionBar_ValueChanged;
            _precisionBar.MouseUp += (_, _) => FocusViewer();
            _precisionTimer.Tick += PrecisionTimer_Tick;
            _precisionHost = new ToolStripControlHost(_precisionBar)
            {
                Name = "precisionToolStripSlider",
                AutoSize = false,
                Size = new Size(MainFormConstants.PrecisionSliderWidth + 6, 24),
            };
            UpdatePrecisionTooltip();

            // The model that matches words with photos, kept in the folder's settings file (Settings.SearchModel)
            _modelBox = new ToolStripComboBox
            {
                Name = "searchModelToolStripComboBox",
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
            };
            FillModelBox();
            _modelBox.SelectedIndexChanged += ModelBox_SelectedIndexChanged;
            _modelBox.DropDownClosed += (_, _) => FocusViewer(); // the arrow keys are the viewer's, not the combo box's

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

            _precisionWideLabel = new ToolStripLabel(SearchStrings.PrecisionWide) { Name = "precisionWideToolStripLabel" };
            _precisionPreciseLabel = new ToolStripLabel(SearchStrings.PrecisionPrecise) { Name = "precisionPreciseToolStripLabel" };
            _modelLabel = new ToolStripLabel(SearchStrings.ModelLabel) { Name = "searchModelToolStripLabel" };

            var strip = new ToolStrip { Name = "searchToolStrip", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            strip.Items.AddRange([
                _transferButton,
                new ToolStripSeparator(),
                _indexButton, _indexProgress, _indexLabel,
                new ToolStripSeparator(),
                _searchBox, _searchButton, _closeResultsButton, _similarButton, _facesButton,
                new ToolStripSeparator(),
                _precisionWideLabel,
                _precisionHost,
                _precisionPreciseLabel,
                new ToolStripSeparator(),
                _modelLabel,
                _modelBox,
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

        /// <summary>The model lines of the combo box, as wide as the longest (name, vendor, languages), so that the box shows the chosen line whole; the list opens as wide.</summary>
        private void FillModelBox()
        {
            var selected = Math.Max(0, _modelBox.SelectedIndex);
            _syncingModel = true;
            try
            {
                _modelBox.Items.Clear();
                foreach (var model in ClipModels.All)
                    _modelBox.Items.Add(new ModelChoice(model));

                _modelBox.Width = _modelBox.Items.Cast<object>().Max(item => TextRenderer.MeasureText(item.ToString(), _modelBox.Font).Width)
                    + MainFormConstants.ModelBoxPadding;
                _modelBox.ComboBox.DropDownWidth = _modelBox.Width;
                _modelBox.SelectedIndex = selected;
            }
            finally
            {
                _syncingModel = false;
            }
        }

        /// <summary>Writes the texts of the strip again in the language that is on (labels, tooltips, the lines of the model box).</summary>
        private void ApplySearchLanguage()
        {
            _searchBox.ToolTipText = SearchStrings.SearchTooltip;
            _searchBox.TextBox.PlaceholderText = SearchStrings.SearchPlaceholder;
            _searchButton.Text = SearchStrings.SearchButton;
            _searchButton.ToolTipText = SearchStrings.SearchTooltip;
            _closeResultsButton.Text = SearchStrings.CloseResultsButton;
            _closeResultsButton.ToolTipText = SearchStrings.CloseResultsTooltip;
            _similarButton.Text = SearchStrings.SimilarButton;
            _similarButton.ToolTipText = SearchStrings.SimilarTooltip;
            _facesButton.Text = SearchStrings.FacesButton;
            _facesButton.ToolTipText = SearchStrings.FacesTooltip;
            _exportItem.Text = SearchStrings.ExportIndexMenu;
            _importItem.Text = SearchStrings.ImportIndexMenu;
            _transferButton.Text = SearchStrings.IndexFileButton;
            _transferButton.ToolTipText = SearchStrings.IndexFileTooltip;
            _precisionWideLabel.Text = SearchStrings.PrecisionWide;
            _precisionPreciseLabel.Text = SearchStrings.PrecisionPrecise;
            _modelLabel.Text = SearchStrings.ModelLabel;
            UpdatePrecisionTooltip();
            FillModelBox();
            UpdateSearchControls(); // the index button, the label of the index, the tooltip of the model
        }

        /// <summary>Enables and labels the controls for what is possible right now.</summary>
        private void UpdateSearchControls()
        {
            var folderKnown = !string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder);
            var indexing = _indexCts is not null;
            var index = _folderSearch?.Index;
            var photoCount = index?.PhotoCount ?? 0;
            var model = SearchModel;
            var photosOfModel = index?.CountWithModel(model.Id) ?? 0;

            _indexButton.Enabled = folderKnown;
            _indexButton.Text = indexing ? SearchStrings.StopIndexButton : photoCount > 0 ? SearchStrings.UpdateIndexButton : SearchStrings.IndexButton;
            _indexButton.ToolTipText = indexing ? SearchStrings.StopIndexTooltip : SearchStrings.IndexTooltip;
            _indexProgress.Visible = indexing;

            if (!indexing)
            {
                var failed = index?.FailedCount ?? 0;
                var partly = photoCount > 0 && photosOfModel < photoCount; // some photos have not been through the chosen model yet
                _indexLabel.Text = photoCount == 0 ? SearchStrings.NotIndexed
                    : partly ? string.Format(SearchStrings.IndexedForModelFormat, photosOfModel, photoCount, model.Title)
                    : failed > 0 ? string.Format(SearchStrings.IndexedWithFailuresFormat, photoCount, index!.FaceCount, failed)
                    : string.Format(SearchStrings.IndexedFormat, photoCount, index!.FaceCount);
                _indexLabel.ToolTipText = partly
                    ? string.Format(SearchStrings.IndexedForModelTooltipFormat, photosOfModel, photoCount, model.Title, index!.FaceCount)
                    : _indexLabel.Text;
            }

            // Searching works as soon as the first photos are in the index, also while the rest is still being done
            var searchable = photoCount > 0;
            _searchBox.Enabled = searchable;
            _searchButton.Enabled = searchable;
            _facesButton.Enabled = searchable;
            _precisionBar.Enabled = searchable;
            ShowSearchPrecision();
            ShowSearchModel();
            _modelBox.Enabled = folderKnown && !indexing; // not while photos are being analyzed: they would end up with two models
            _closeResultsButton.Visible = _resultsActive;
            _transferButton.Enabled = folderKnown;
            _exportItem.Enabled = searchable && !_transferBusy;
            _importItem.Enabled = folderKnown && !indexing && !_transferBusy;
            UpdateSimilarEnabled();
        }

        private void UpdateSimilarEnabled() =>
            _similarButton.Enabled = pictureBox1.Tag is string file && _folderSearch?.CanSearchSimilar(file) == true;

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
            _resultsRequest = string.Empty;
            _repeatSearch = null;
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
            else if (opened?.People.SharedError is { } sharedError)
                SetStatus(string.Format(SearchStrings.StatusSharedPeopleError, sharedError));
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

            var model = SearchModel;
            if (_models.MissingMegabytes(model) is > 0 and var missing &&
                MessageBox.Show(this, SearchStrings.DownloadQuestionFor(model, !_models.ClipDownloaded(model), !_models.FacesDownloaded, missing),
                    SearchStrings.DownloadTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            var cancellation = _indexCts = new CancellationTokenSource();
            _indexLabel.Text = SearchStrings.IndexScanning;
            UpdateSearchControls();

            FolderIndex? index = null;
            try
            {
                index = _folderSearch is { } shown && SameFolder(shown.Folder, folder) ? shown : await Task.Run(() => FolderIndex.Open(folder));
                index.ClipModel = model;
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
            time.TotalHours >= 1 ? string.Format(SearchStrings.DurationHoursFormat, (int)time.TotalHours, time.Minutes)
            : time.TotalMinutes >= 1 ? string.Format(SearchStrings.DurationMinutesFormat, (int)Math.Ceiling(time.TotalMinutes))
            : string.Format(SearchStrings.DurationSecondsFormat, (int)time.TotalSeconds);

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
            if (search.NeedsTextModel(text))
            {
                var model = SearchModel;
                if (search.Index.CountWithModel(model.Id) == 0)
                {
                    SetStatus(string.Format(SearchStrings.StatusNothingForModelFormat, model.Title));
                    return;
                }
                if (!_models.ClipDownloaded(model))
                {
                    SetStatus(string.Format(SearchStrings.StatusModelMissingFormat, model.Title, SearchStrings.Size(model.Megabytes)));
                    return;
                }
            }

            _repeatSearch = tuning => search.Search(_models, text, tuning);
            await RepeatSearchAsync();
        }

        private void RunSimilarSearch()
        {
            if (_folderSearch is not { } search || pictureBox1.Tag is not string file)
                return;

            if (!search.CanSearchSimilar(file))
            {
                SetStatus(search.Index.FindFullPath(file)?.HasVector is not true
                    ? SearchStrings.StatusSimilarNotIndexed
                    : string.Format(SearchStrings.StatusSimilarOtherModelFormat, SearchModel.Title));
                return;
            }

            _repeatSearch = tuning => search.SearchSimilar(file, tuning);
            _ = RepeatSearchAsync();
        }

        /// <summary>Runs the last search (<see cref="_repeatSearch"/>) with the cutoffs of the precision slider and shows what it found.</summary>
        private async Task RepeatSearchAsync()
        {
            if (_repeatSearch is not { } repeat || _folderSearch is not { } search)
                return;

            var tuning = SearchTuning.For(_currentConfig.SearchPrecision);
            var generation = ++_searchGeneration;
            SetStatus(SearchStrings.StatusSearching);
            SearchResult result;
            try
            {
                result = await Task.Run(() => repeat(tuning));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(SearchStrings.StatusSearchError, ex.Message));
                return;
            }

            if (generation == _searchGeneration && ReferenceEquals(search, _folderSearch))
                await ShowResultsAsync(result);
        }

        // ── the search model ───────────────────────────────────────────────

        /// <summary>The model that matches words with photos in the shown folder: the one its settings file names, the default if it names none we know.</summary>
        private ClipModelInfo SearchModel => ClipModels.Find(_currentConfig.SearchModel);

        private sealed record ModelChoice(ClipModelInfo Model)
        {
            public override string ToString() => SearchStrings.ModelName(Model);
        }

        /// <summary>Makes the combo box, and the index of the shown folder, follow the settings of the shown folder (each folder keeps its own model).</summary>
        private void ShowSearchModel()
        {
            var model = SearchModel;
            if (_folderSearch is { } search && !ReferenceEquals(search.ClipModel, model))
                search.ClipModel = model;

            var position = ClipModels.All.ToList().FindIndex(m => m.Id == model.Id);
            if (_modelBox.SelectedIndex != position)
            {
                _syncingModel = true;
                try
                {
                    _modelBox.SelectedIndex = position;
                }
                finally
                {
                    _syncingModel = false;
                }
            }
            _modelBox.ToolTipText = SearchStrings.ModelBoxTooltip + "\n\n" + SearchStrings.ModelDescription(model) + "\n\n"
                + string.Format(_models.ClipDownloaded(model) ? SearchStrings.ModelDownloadedFormat : SearchStrings.ModelToDownloadFormat, SearchStrings.Size(model.Megabytes));
        }

        private void ModelBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_syncingModel || _modelBox.SelectedItem is not ModelChoice { Model: var model } || model.Id == SearchModel.Id)
                return;

            _currentConfig.SearchModel = model.Id;
            if (!string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder))
                SaveSettingsToJson();

            if (_folderSearch is { } search)
                search.ClipModel = model;

            // What is shown was found with the other model, and the last search would be repeated with this one
            _repeatSearch = null;
            _searchGeneration++; // a search that is still running was made with the other model: its result is not shown
            _precisionTimer.Stop();
            CloseSearchResults();
            _models.ReleaseTextEncodersExcept(model); // the text half of a model is up to 1.1 GB in memory
            UpdateSearchControls();

            var total = _folderSearch?.Index.PhotoCount ?? 0;
            var done = _folderSearch?.Index.CountWithModel(model.Id) ?? 0;
            var download = _models.ClipDownloaded(model) ? string.Empty : string.Format(SearchStrings.StatusModelDownloadNoteFormat, SearchStrings.Size(model.Megabytes));
            SetStatus(total == 0 ? string.Format(SearchStrings.StatusModelEmptyFormat, model.Title, download)
                : done == total ? string.Format(SearchStrings.StatusModelReadyFormat, model.Title, total)
                : done == 0 ? string.Format(SearchStrings.StatusModelNoneFormat, model.Title, total, download)
                : string.Format(SearchStrings.StatusModelPartFormat, model.Title, done, total, download));
        }

        // ── the precision slider ───────────────────────────────────────────

        private void PrecisionBar_ValueChanged(object? sender, EventArgs e)
        {
            if (_syncingPrecision)
                return;

            _currentConfig.SearchPrecision = _precisionBar.Value;
            UpdatePrecisionTooltip();

            // Dragging passes through many values: the search is repeated once the slider rests
            _precisionTimer.Stop();
            _precisionTimer.Start();
        }

        private async void PrecisionTimer_Tick(object? sender, EventArgs e)
        {
            _precisionTimer.Stop();
            if (!string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder))
                SaveSettingsToJson();

            await RepeatSearchAsync();
        }

        /// <summary>Puts the slider where the settings of the shown folder have it (each folder keeps its own value).</summary>
        private void ShowSearchPrecision()
        {
            var precision = Math.Clamp(_currentConfig.SearchPrecision, 0, Settings.MaxSearchPrecision);
            if (_precisionBar.Value == precision)
                return;

            _syncingPrecision = true;
            try
            {
                _precisionBar.Value = precision;
            }
            finally
            {
                _syncingPrecision = false;
            }
            UpdatePrecisionTooltip();
        }

        private void UpdatePrecisionTooltip()
        {
            var tuning = SearchTuning.For(_precisionBar.Value);
            _precisionTip.SetToolTip(_precisionBar, string.Format(
                SearchStrings.PrecisionTooltipFormat, _precisionBar.Value, tuning.MinTextScore, tuning.TextScoreBand, tuning.MinSimilarScore, tuning.MinFaceScore));
        }

        /// <summary>Shows the photos that were found in place of the folder, so that they can be browsed, liked and grouped like any photos.</summary>
        private async Task ShowResultsAsync(SearchResult result)
        {
            // In the order of the SortOrder setting, like the folder (None: best match first)
            result = result.OrderedBy(_currentConfig.SortOrder);

            if (result.Hits.Count == 0)
            {
                SetStatus(string.Format(SearchStrings.StatusNothingFound, result.Description) + DateNotes(result) + ModelNotes(result));
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
            _resultsRequest = result.Description;
            var generation = _folderGeneration;

            var likedFolder = _currentConfig.LikedFolder;
            var (liked, groups) = await Task.Run(() => (LikedLibrary.Scan(likedFolder), GroupLibrary.Scan(likedFolder)));
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
            SetStatus(string.Format(SearchStrings.StatusResultsShown, result.Description, files.Count, result.SearchedPhotos) + DateNotes(result) + ModelNotes(result));
            LoadFirstPhoto(files[0].OriginalFilePath);
            settingsPropertyGrid.Refresh();

            // The search box keeps the keys for its own typing: with the focus left in it the arrows, Space and the group keys would not reach the photos
            FocusViewer();
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

        /// <summary>That a text search left out photos because they are analyzed for another search model than the one chosen now.</summary>
        private static string ModelNotes(SearchResult result) =>
            result.OtherModelPhotos > 0 ? string.Format(SearchStrings.StatusOtherModelNoteFormat, result.OtherModelPhotos) : string.Empty;

        /// <summary>Leaves the results and shows the folder again, at the photo that was shown before.</summary>
        private void CloseSearchResults()
        {
            if (!_resultsActive)
                return;

            _resultsActive = false;
            _resultsTitle = string.Empty;
            _resultsRequest = string.Empty;
            _repeatSearch = null;
            _precisionTimer.Stop();
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
                SetStatus(search.People.SharedError is { } notShared
                    ? string.Format(SearchStrings.StatusFaceNamedNotShared, name, notShared)
                    : string.Format(SearchStrings.StatusFaceNamed, name));
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

            _repeatSearch = tuning => label.Confirmed && label.Name is not null
                ? search.SearchPerson(label.Name, tuning)
                : search.SearchPerson(label.Face, label.Name, tuning);
            _ = RepeatSearchAsync();
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
            _precisionTimer.Dispose();
            _precisionTip.Dispose();
            _folderSearch?.Dispose();
            if (!ReferenceEquals(_indexingSearch, _folderSearch))
                _indexingSearch?.Dispose();
            _models.Dispose();
        }
    }
}
