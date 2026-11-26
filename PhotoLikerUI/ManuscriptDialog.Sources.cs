using PhotoLiker.GenerateVideo;
using PhotoLiker.Manuscripts;

namespace PhotoLikerUI
{
    /// <summary>
    /// The "Photos from" box of the Scan manuscripts window, as the Make a book window has it: the pages come from the photos that are loaded (Current, the one the window starts with), from the liked
    /// photos, or from a group of them. Each place has a manuscript of its own, kept in its folder (<see cref="ManuscriptFile"/>): picking another place saves the one that is open and opens that of
    /// the other, with its pages, its texts and its settings. The photos of a folder are listed (and counted, "Liked (123)") in the background, so the window opens at once.
    /// </summary>
    internal sealed partial class ManuscriptDialog
    {
        private readonly string? _likedFolder;
        private readonly IReadOnlyCollection<string> _extensions;
        private readonly string _currentFolder;
        private PageSource _openSource = null!;
        private bool _refreshingSourceItem;
        private bool _switchingSource;
        private int _sourceGeneration;

        // The box, over the list of pages
        internal readonly Label SourceLabel = new();
        internal readonly ComboBox SourceCombo = new();
        private readonly Panel _sourceRow = new();

        /// <summary>The place the pages are from that is open (for the tests).</summary>
        internal string OpenSourceName => _openSource.Name;

        /// <summary>The folder whose manuscript is open: the folder of the loaded photos, or the liked folder, or a group.</summary>
        internal string ManuscriptFolder => _folder;

        /// <summary>True while another place is being opened (its photos listed, its manuscript read).</summary>
        internal bool IsSwitchingSource => _switchingSource;

        // ── Building ────────────────────────────────────────────────────────

        private void BuildSourceRow()
        {
            SourceLabel.Name = "photosFromLabel";
            SourceLabel.Text = BookStrings.LabelPhotosFrom;
            SourceLabel.Dock = DockStyle.Top;
            SourceLabel.AutoSize = true;
            SourceLabel.Padding = new Padding(0, 0, 0, 2);
            SourceCombo.Name = "photosFromCombo";
            SourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            SourceCombo.DropDownWidth = 420; // a group folder can carry the search it was made for in its name
            SourceCombo.Dock = DockStyle.Top;
            _toolTip.SetToolTip(SourceCombo, ManuscriptStrings.HintPhotosFrom);
            _toolTip.SetToolTip(SourceLabel, ManuscriptStrings.HintPhotosFrom);

            _sourceRow.Name = "photosFromRow";
            _sourceRow.Dock = DockStyle.Top;
            _sourceRow.AutoSize = true;
            _sourceRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _sourceRow.Padding = new Padding(6, 0, 6, 6);
            _sourceRow.Controls.Add(SourceCombo); // docked at the top, the one added last is the first: the label is over the box
            _sourceRow.Controls.Add(SourceLabel);
        }

        /// <summary>Puts the places in the box: the photos that are loaded (picked), and the liked photos when there is a liked folder. The groups come when they are looked up.</summary>
        private void FillSources(IReadOnlyList<string> loadedPhotos)
        {
            var current = new PageSource(BookStrings.SourceCurrent, null, _currentFolder) { Photos = [.. loadedPhotos.Where(IsPagePhoto)] };
            SourceCombo.Items.Add(current);
            if (!string.IsNullOrWhiteSpace(_likedFolder))
                SourceCombo.Items.Add(new PageSource(BookStrings.SourceLiked, _likedFolder, _likedFolder));

            SourceCombo.SelectedItem = current;
            _openSource = current;
        }

        /// <summary>
        /// Looks up the group folders of the liked folder, offers each as a place, and counts the photos of the liked folder and of the groups, one folder after the other (a slow disk is not asked
        /// for everything at once). A folder that cannot be read keeps a "?" for its count; picking it tries again and says why it failed.
        /// </summary>
        private async Task CountSourcesAsync()
        {
            if (string.IsNullOrWhiteSpace(_likedFolder))
                return;

            var liked = SourceCombo.Items.Cast<PageSource>().First(source => source.Folder is not null);
            var likedCount = CountQuietlyAsync(liked);

            IReadOnlyList<GroupFolder> groups = [];
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

            var sources = groups.Select(group => new PageSource(Path.GetFileName(group.Path), group.Path, group.Path)).ToList();
            foreach (var source in sources)
                SourceCombo.Items.Add(source);

            await likedCount;
            foreach (var source in sources)
            {
                if (IsDisposed)
                    return;

                await CountQuietlyAsync(source);
            }
        }

        private async Task CountQuietlyAsync(PageSource source)
        {
            try
            {
                await ListAsync(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // shown as "?" in the box, and as the reason when the place is picked
            }
        }

        /// <summary>Lists the photos of a folder on another thread, once; the count in the name of the place in the box is brought up to date.</summary>
        private Task<List<string>> ListAsync(PageSource source) => source.Listing ??= ListCoreAsync(source);

        private async Task<List<string>> ListCoreAsync(PageSource source)
        {
            try
            {
                source.Photos = await Task.Run(() => VideoSources.Copies(source.Folder ?? throw new InvalidOperationException("The loaded photos are not listed from a folder."), _extensions)
                    .Select(photo => photo.OriginalFilePath).Where(IsPagePhoto).OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase).ToList());
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

        /// <summary>Shows the name of the place with its new count in the box, without that being a pick.</summary>
        private void RefreshSourceItem(PageSource source)
        {
            var index = SourceCombo.Items.IndexOf(source);
            if (index < 0)
                return;

            _refreshingSourceItem = true;
            try
            {
                SourceCombo.Items[index] = source;
            }
            finally
            {
                _refreshingSourceItem = false;
            }
        }

        // ── Opening another place ───────────────────────────────────────────

        private async void OnSourcePicked()
        {
            if (_refreshingSourceItem || SourceCombo.SelectedItem is not PageSource source || ReferenceEquals(source, _openSource))
                return;

            if (_busy || _switchingSource)
            {
                PutBackSource(); // a scan, a turn or another opening is at work: the pick waits for the person to ask again
                return;
            }

            await OpenSourceAsync(source);
        }

        private void PutBackSource()
        {
            _refreshingSourceItem = true;
            try
            {
                SourceCombo.SelectedItem = _openSource;
            }
            finally
            {
                _refreshingSourceItem = false;
            }
        }

        /// <summary>
        /// Opens the manuscript of a place: the one that is open is saved first (to its folder); then the photos of the place are listed if that was not done, and its manuscript is read (its pages, texts and
        /// settings) with the photos that are new in it added as pages. A place that cannot be listed leaves the one that was open as it is.
        /// </summary>
        internal async Task OpenSourceAsync(PageSource source)
        {
            if (_switchingSource)
                return;

            _switchingSource = true;
            var generation = ++_sourceGeneration;
            SetBusy(true);
            try
            {
                FlushText();
                SaveWork(); // the manuscript that is open goes to its folder before another is shown
                SetStatus(BookStrings.Counting);

                if (source.Photos is null)
                    await ListAsync(source);

                if (IsDisposed || generation != _sourceGeneration)
                    return;

                ResetWork();
                _folder = source.OutputFolder;
                _openSource = source;
                LoadWork([], source.Photos ?? []);
                RefreshPageList();
                ShowPage();
                SetStatus(_pagesCapped
                    ? string.Format(ManuscriptStrings.StatusPagesCappedFormat, MaximumLoadedPages)
                    : string.Format(ManuscriptStrings.StatusSourceOpenedFormat, source.Name, _pages.Count));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (!IsDisposed)
                {
                    SetStatus(string.Format(BookStrings.CannotListFormat, ex.Message));
                    PutBackSource();
                }
            }
            finally
            {
                _switchingSource = false;
                if (!IsDisposed)
                    SetBusy(false);
            }
        }

        /// <summary>Forgets the manuscript that is open (it was saved): the pages, what was taken out, the settings, and the text on the screen, so that another can be read into the window.</summary>
        private void ResetWork()
        {
            _pages.Clear();
            _removed.Clear();
            _current = -1;
            _dirty = false;
            _touched = false;
            _pagesCapped = false;
            _workSaveFailed = false;
            _rows = null;
            _rowsFor = null;
            _work.OutputFolder = null;
            _work.OutputFileName = null;
            IncludePhotosCheck.Checked = false;
            CutPagesCheck.Checked = false;
            CutPagesCheck.Enabled = false;
            ScanPicturesCheck.Checked = true;
            UseContextCheck.Checked = false;
            AiPlacesCheck.Checked = false;
        }

        /// <summary>A place the pages can come from: the loaded photos (no folder of its own to list), or a folder of copies. Shown in the box as its name and the number of its photos, "Liked (123)".</summary>
        internal sealed class PageSource(string name, string? folder, string outputFolder)
        {
            public string Name { get; } = name;

            /// <summary>The folder to list; null for the photos that are loaded (they are known).</summary>
            public string? Folder { get; } = folder;

            /// <summary>Where the manuscript is kept while this is the place: the folder its photos are in.</summary>
            public string OutputFolder { get; } = outputFolder;

            /// <summary>The photos that can be pages, once they are known (the loaded ones from the start, a folder when it is listed).</summary>
            public List<string>? Photos { get; set; }

            /// <summary>The folder is being listed or is listed; null if that was not started, or failed.</summary>
            public Task<List<string>>? Listing { get; set; }

            public bool Failed { get; set; }

            public override string ToString() => $"{Name} ({(Photos is { } photos ? photos.Count.ToString("N0") : Failed ? "?" : "...")})";
        }
    }
}
