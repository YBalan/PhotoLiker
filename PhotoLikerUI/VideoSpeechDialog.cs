using System.Text;
using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// A table of the videos of the folder with what is said in each of them (the transcripts the speech job keeps in the catalog), the language it was heard as, and where its
    /// picture changes (its moments). A double click plays a video in the program Windows has for it; the context menu shows it in File Explorer and hears it again in another
    /// language; a button hears every video again in one language; another saves the whole table as an Excel file. Selecting a video shows its moments and its words with their times.
    /// What is changed here is the catalog's speech (the same rows the search looks through); nothing is done to the video files.
    /// </summary>
    internal sealed class VideoSpeechDialog : Form
    {
        private sealed class BufferedListView : ListView
        {
            public BufferedListView() => DoubleBuffered = true;
        }

        /// <summary>What the status box filters by: all, one status, or the videos that could not be read at all.</summary>
        private enum Filter { All, Heard, NothingHeard, NotRead, NoSound, Problems }

        private sealed record StatusChoice(Filter Filter, string Text)
        {
            public override string ToString() => Text;
        }

        /// <summary>A video as the list shows it: the catalog's row, and the text of each cell made once.</summary>
        private sealed record Entry(VideoSpeechRow Row, string Path, string Length, string Status, string Language, string Moments, string Speech, string Search);

        private const int PathColumn = 0, LengthColumn = 1, StatusColumn = 2, LanguageColumn = 3, MomentsColumn = 4, SpeechColumn = 5;
        private const int SpeechPreviewLength = 400;

        private readonly string _folder;
        private readonly VideoCatalog _catalog;
        private readonly string _clipModelId;
        private readonly SpeechModelInfo _model;
        private readonly string _modelsFolder;
        private readonly string _defaultLanguage;
        private readonly Func<int> _cores;
        private readonly Func<bool> _indexing;
        private readonly bool _dark;

        private readonly Label _intro = new() { Text = VideoSpeechStrings.Intro, Dock = DockStyle.Fill, Height = 44 };
        private readonly Label _findLabel = new() { Text = VideoSpeechStrings.FindLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) };
        private readonly TextBox _findBox = new() { Width = 280, PlaceholderText = VideoSpeechStrings.FindHint };
        private readonly ComboBox _statusBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(16, 6, 0, 0) };
        private readonly BufferedListView _list = new();
        private readonly SplitContainer _split = new() { Orientation = Orientation.Horizontal, Dock = DockStyle.Fill };
        private readonly MomentStrip _strip = new() { Dock = DockStyle.Top, Visible = false };
        private readonly TextBox _words = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
        private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 14, Minimum = 0, Maximum = 100, Visible = false };
        private readonly Label _status = new() { AutoSize = true };
        private readonly Button _hearSelected = new() { Text = VideoSpeechStrings.HearSelectedButton, AutoSize = true, MinimumSize = new Size(120, 28) };
        private readonly Button _hearAll = new() { Text = VideoSpeechStrings.HearAllButton, AutoSize = true, MinimumSize = new Size(130, 28) };
        private readonly Button _export = new() { Text = VideoSpeechStrings.ExportButton, AutoSize = true, MinimumSize = new Size(140, 28) };
        private readonly Button _stop = new() { Text = VideoSpeechStrings.StopButton, AutoSize = true, MinimumSize = new Size(80, 28), Visible = false };
        private readonly Button _close = new() { Text = VideoSpeechStrings.CloseButton, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };
        private readonly ToolTip _tips = new();
        private readonly ContextMenuStrip _menu = new();
        private readonly Panel _detail = new() { Dock = DockStyle.Fill };
        private readonly ToolStripMenuItem _openItem = new(ShellOpen.OpenMenu);
        private Font? _openFont;
        private readonly ToolStripMenuItem _showItem = new(ShellOpen.ShowMenu);
        private readonly ToolStripMenuItem _hearItem = new(VideoSpeechStrings.MenuHearAgain);
        private readonly System.Windows.Forms.Timer _selecting = new() { Interval = 150 };

        private IReadOnlyList<VideoSpeechRow> _all = [];
        private Entry[] _entries = [];
        private Entry[] _view = [];
        private int _sortColumn = PathColumn;
        private bool _sortDescending;
        private bool _rebuilding;
        private int _detailGeneration;
        private IReadOnlyList<SpeechSegment> _shownSegments = [];
        private int[] _lineStarts = [];

        private bool _busy;
        private bool _closeWhenDone;
        private CancellationTokenSource? _cts;

        /// <param name="catalog">The catalog of the window: it is only read here (the job that hears videos again opens its own connection, as the indexing does).</param>
        /// <param name="clipModelId">The picture model whose moments are shown.</param>
        /// <param name="defaultLanguage">The SpeechLanguage setting: suggested when videos are heard again.</param>
        /// <param name="cores">How many cores a run may use (asked again before each video).</param>
        /// <param name="indexing">Whether Update index is running: it writes the same catalog, so videos are not heard again meanwhile.</param>
        public VideoSpeechDialog(
            string folder, VideoCatalog catalog, string clipModelId, SpeechModelInfo model, string modelsFolder, string defaultLanguage, Func<int> cores, Func<bool> indexing, bool dark)
        {
            _folder = folder;
            _catalog = catalog;
            _clipModelId = clipModelId;
            _model = model;
            _modelsFolder = modelsFolder;
            _defaultLanguage = defaultLanguage;
            _cores = cores;
            _indexing = indexing;
            _dark = dark;

            Text = VideoSpeechStrings.Title;
            StartPosition = FormStartPosition.CenterParent;
            Icon = DialogIcons.VideoSpeech;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(900, 560);

            BuildLayout();
            Wire();
            ApplyTheme();
            ShowNothing();

            Shown += async (_, _) => await LoadAsync();
        }

        /// <summary>Whether the speech of any video was changed: what the window of the program shows of it (the counts, the caption) is out of date then.</summary>
        public bool Changed { get; private set; }

        // ── building the dialog ────────────────────────────────────────────

        private void BuildLayout()
        {
            _statusBox.Items.AddRange(
            [
                new StatusChoice(Filter.All, VideoSpeechStrings.FilterAll),
                new StatusChoice(Filter.Heard, VideoSpeechStrings.StatusHeard),
                new StatusChoice(Filter.NothingHeard, VideoSpeechStrings.StatusNothingHeard),
                new StatusChoice(Filter.NotRead, VideoSpeechStrings.StatusNotRead),
                new StatusChoice(Filter.NoSound, VideoSpeechStrings.StatusNoSound),
                new StatusChoice(Filter.Problems, VideoSpeechStrings.FilterProblems),
            ]);
            _statusBox.SelectedIndex = 0;

            var find = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            find.Controls.AddRange([_findLabel, _findBox, _statusBox, _summary]);

            _list.View = View.Details;
            _list.VirtualMode = true;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(VideoSpeechStrings.ColumnVideo, 330);
            _list.Columns.Add(VideoSpeechStrings.ColumnLength, 70);
            _list.Columns.Add(VideoSpeechStrings.ColumnStatus, 120);
            _list.Columns.Add(VideoSpeechStrings.ColumnLanguage, 150);
            _list.Columns.Add(VideoSpeechStrings.ColumnMoments, 75);
            _list.Columns.Add(VideoSpeechStrings.ColumnSpeech, 400);
            _list.ContextMenuStrip = _menu;
            _menu.Items.AddRange([_openItem, _showItem, new ToolStripSeparator(), _hearItem]);
            _openItem.Font = _openFont = new Font(_menu.Font, FontStyle.Bold); // what a double click does
            ShowSortArrow();

            _detail.Controls.Add(_words);   // fills what the strip leaves
            _detail.Controls.Add(_strip);
            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(_detail);
            _split.Panel2MinSize = 150;

            _tips.SetToolTip(_hearSelected, VideoSpeechStrings.HearSelectedTooltip);
            _tips.SetToolTip(_hearAll, VideoSpeechStrings.HearAllTooltip);
            _tips.SetToolTip(_export, VideoSpeechStrings.ExportTooltip);

            var left = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            left.Controls.AddRange([_hearSelected, _hearAll, _export]);
            var right = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            right.Controls.AddRange([_close, _stop]);
            var buttons = new Panel { Dock = DockStyle.Fill, Height = 36 };
            buttons.Controls.AddRange([left, right]);

            _status.Margin = new Padding(0, 4, 0, 6);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.Controls.Add(_progress, 0, 0);
            TaskbarProgress.Watch(_progress); // shown while it works
            bottom.Controls.Add(_status, 0, 1);
            bottom.Controls.Add(buttons, 0, 2);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12, 10, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_intro, 0, 0);
            root.Controls.Add(find, 0, 1);
            root.Controls.Add(_split, 0, 2);
            root.Controls.Add(bottom, 0, 3);
            Controls.Add(root);

            CancelButton = _close;
            Load += (_, _) => _split.SplitterDistance = Math.Max(120, (int)(_split.Height * 0.62));
        }

        private void Wire()
        {
            _list.RetrieveVirtualItem += (_, e) => e.Item = BuildItem(e.ItemIndex);
            _list.ColumnClick += (_, e) => SortBy(e.Column);
            _list.SelectedIndexChanged += (_, _) =>
            {
                if (_rebuilding)
                    return;
                _selecting.Stop(); // the moments are read a moment after the last arrow key, not at every one
                _selecting.Start();
                UpdateButtons();
            };
            _selecting.Tick += (_, _) =>
            {
                _selecting.Stop();
                ShowSelection();
            };
            _list.Resize += (_, _) => FitLastColumn();
            _list.MouseDoubleClick += (_, e) =>
            {
                if (_list.HitTest(e.Location).Item is { } item && item.Index >= 0 && item.Index < _view.Length)
                    Open(_view[item.Index], show: false);
            };
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && SelectedEntries().FirstOrDefault() is { } entry)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    Open(entry, show: false);
                }
                else if (e.KeyCode == Keys.A && e.Control)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    SelectAll();
                }
            };
            _menu.Opening += (_, e) =>
            {
                if (_list.SelectedIndices.Count == 0)
                    e.Cancel = true;
                _openItem.Enabled = _showItem.Enabled = _list.SelectedIndices.Count == 1;
                _hearItem.Enabled = !_busy && SelectedEntries().Any(CanBeHeard);
                _menu.Renderer = _dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
                _menu.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
                _menu.ForeColor = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            };
            _openItem.Click += (_, _) => { if (SelectedEntries().FirstOrDefault() is { } entry) Open(entry, show: false); };
            _showItem.Click += (_, _) => { if (SelectedEntries().FirstOrDefault() is { } entry) Open(entry, show: true); };
            _hearItem.Click += async (_, _) => await HearAsync([.. SelectedEntries().Select(e => e.Row)]);
            _hearSelected.Click += async (_, _) => await HearAsync([.. SelectedEntries().Select(e => e.Row)]);
            _hearAll.Click += async (_, _) => await HearAsync(_all);
            _export.Click += async (_, _) => await ExportAsync();
            _stop.Click += (_, _) => StopWorking();
            _findBox.TextChanged += (_, _) => Refilter();
            _statusBox.SelectedIndexChanged += (_, _) => Refilter();
            _strip.MomentClicked += ShowWordsAt;
            _strip.SaidIn = SaidIn;
            FormClosing += (_, e) =>
            {
                if (!_busy)
                    return;

                // The job writes the catalog: it is stopped and let finish before the window goes
                e.Cancel = true;
                _closeWhenDone = true;
                StopWorking();
            };
        }

        private void ApplyTheme()
        {
            ThemeManager.Apply(this, _dark);
            foreach (Control control in new Control[] { _intro, _findLabel, _summary, _status })
                control.BackColor = Color.Transparent; // the theme paints a label like a control; here it sits on a panel
            _list.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            _words.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            _words.ForeColor = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _selecting.Dispose();
                _tips.Dispose();
                _menu.Dispose();
                _openFont?.Dispose();
                _strip.Clear();
            }
            base.Dispose(disposing);
        }

        // ── the table ──────────────────────────────────────────────────────

        private async Task LoadAsync()
        {
            _status.Text = VideoSpeechStrings.StatusLoading;
            try
            {
                var rows = await Task.Run(() => _catalog.SpeechTable(_model.Id));
                if (IsDisposed)
                    return;

                _all = rows;
                _entries = [.. rows.Select(MakeEntry)];
                _status.Text = string.Empty;
                Refilter();
            }
            catch (Exception ex) when (!IsDisposed)
            {
                _status.Text = string.Format(VideoSpeechStrings.StatusLoadErrorFormat, ex.Message);
            }
        }

        private Entry MakeEntry(VideoSpeechRow row)
        {
            var path = row.Video.RelativePath.Replace('/', '\\');
            var text = row.Text;
            return new Entry(
                row, path,
                row.Video.Info is { } info ? VideoTime.Format(info.Duration) : string.Empty,
                StatusText(row.Status),
                SpeechLanguages.Describe(row.Language),
                row.MomentTimesMs.Count > 0 ? row.MomentTimesMs.Count.ToString("N0") : string.Empty,
                OneLine(text, SpeechPreviewLength),
                path + " " + text);
        }

        private static string StatusText(SpeechStatus status) => status switch
        {
            SpeechStatus.Heard => VideoSpeechStrings.StatusHeard,
            SpeechStatus.NothingHeard => VideoSpeechStrings.StatusNothingHeard,
            SpeechStatus.NotRead => VideoSpeechStrings.StatusNotRead,
            SpeechStatus.NoSound => VideoSpeechStrings.StatusNoSound,
            SpeechStatus.Failed => VideoSpeechStrings.StatusFailed,
            _ => VideoSpeechStrings.StatusUnreadable,
        };

        private static string OneLine(string text, int limit)
        {
            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= limit ? text : text[..(limit - 1)] + "…";
        }

        private static bool CanBeHeard(Entry entry) => entry.Row.Video.Info is { Audio: not null };

        /// <summary>The list again, for the words typed, the status chosen and the order asked for; the videos that were selected stay selected.</summary>
        private void Refilter()
        {
            var words = _findBox.Text.Trim();
            var filter = (_statusBox.SelectedItem as StatusChoice)?.Filter ?? Filter.All;
            var selected = SelectedEntries().Select(e => e.Row.Video.Id).ToHashSet();

            IEnumerable<Entry> kept = _entries.Where(e => Matches(e, filter) && (words.Length == 0 || e.Search.Contains(words, StringComparison.OrdinalIgnoreCase)));
            kept = _sortColumn switch
            {
                LengthColumn => Order(kept, e => e.Row.Video.Info?.DurationSeconds ?? 0),
                StatusColumn => Order(kept, e => (int)e.Row.Status),
                LanguageColumn => Order(kept, e => e.Language, StringComparer.CurrentCultureIgnoreCase),
                MomentsColumn => Order(kept, e => e.Row.MomentTimesMs.Count),
                SpeechColumn => Order(kept, e => e.Speech, StringComparer.CurrentCultureIgnoreCase),
                _ => Order(kept, e => e.Path, StringComparer.OrdinalIgnoreCase),
            };

            _rebuilding = true;
            try
            {
                _view = [.. kept];
                _list.VirtualListSize = _view.Length;
                _list.SelectedIndices.Clear();
                for (var i = 0; i < _view.Length && selected.Count > 0; i++)
                {
                    if (selected.Contains(_view[i].Row.Video.Id))
                        _list.SelectedIndices.Add(i);
                }
                _list.Invalidate();
            }
            finally
            {
                _rebuilding = false;
            }

            ShowSummary();
            UpdateButtons();
            _selecting.Stop();
            ShowSelection();
        }

        private IEnumerable<Entry> Order<TKey>(IEnumerable<Entry> entries, Func<Entry, TKey> key, IComparer<TKey>? comparer = null) =>
            _sortDescending
                ? entries.OrderByDescending(key, comparer).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                : entries.OrderBy(key, comparer).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase);

        private static bool Matches(Entry entry, Filter filter) => filter switch
        {
            Filter.All => true,
            Filter.Heard => entry.Row.Status == SpeechStatus.Heard,
            Filter.NothingHeard => entry.Row.Status == SpeechStatus.NothingHeard,
            Filter.NotRead => entry.Row.Status == SpeechStatus.NotRead,
            Filter.NoSound => entry.Row.Status == SpeechStatus.NoSound,
            _ => entry.Row.Status is SpeechStatus.Failed or SpeechStatus.Unreadable,
        };

        private void SortBy(int column)
        {
            _sortDescending = column == _sortColumn && !_sortDescending;
            _sortColumn = column;
            ShowSortArrow();
            Refilter();
        }

        private void ShowSortArrow()
        {
            string[] names =
            [
                VideoSpeechStrings.ColumnVideo, VideoSpeechStrings.ColumnLength, VideoSpeechStrings.ColumnStatus,
                VideoSpeechStrings.ColumnLanguage, VideoSpeechStrings.ColumnMoments, VideoSpeechStrings.ColumnSpeech,
            ];
            for (var i = 0; i < names.Length; i++)
                _list.Columns[i].Text = i == _sortColumn ? names[i] + (_sortDescending ? "  ▼" : "  ▲") : names[i];
        }

        private void ShowSummary()
        {
            int Count(SpeechStatus status) => _entries.Count(e => e.Row.Status == status);
            var text = string.Format(
                VideoSpeechStrings.SummaryFormat, _entries.Length, Count(SpeechStatus.Heard), Count(SpeechStatus.NothingHeard), Count(SpeechStatus.NotRead), Count(SpeechStatus.NoSound),
                Count(SpeechStatus.Failed) + Count(SpeechStatus.Unreadable));
            if (_view.Length != _entries.Length)
                text += string.Format(VideoSpeechStrings.ShownPartFormat, _view.Length);
            _summary.Text = text;
        }

        private ListViewItem BuildItem(int index)
        {
            var entry = index >= 0 && index < _view.Length ? _view[index] : null;
            return entry is null
                ? new ListViewItem(string.Empty)
                : new ListViewItem([entry.Path, entry.Length, entry.Status, entry.Language, entry.Moments, entry.Speech]);
        }

        private void FitLastColumn()
        {
            var others = 0;
            for (var i = 0; i < SpeechColumn; i++)
                others += _list.Columns[i].Width;
            _list.Columns[SpeechColumn].Width = Math.Max(200, _list.ClientSize.Width - others);
        }

        private void SelectAll()
        {
            _rebuilding = true;
            try
            {
                for (var i = 0; i < _view.Length; i++)
                    _list.SelectedIndices.Add(i);
            }
            finally
            {
                _rebuilding = false;
            }
            UpdateButtons();
            ShowSelection();
        }

        private IEnumerable<Entry> SelectedEntries()
        {
            foreach (int index in _list.SelectedIndices)
            {
                if (index >= 0 && index < _view.Length)
                    yield return _view[index];
            }
        }

        private void UpdateButtons()
        {
            _hearSelected.Enabled = !_busy && SelectedEntries().Any(CanBeHeard);
            _hearAll.Enabled = !_busy && _entries.Any(CanBeHeard);
            _export.Enabled = !_busy && _entries.Length > 0;
            _stop.Visible = _busy;
        }

        // ── the selected video: its moments and its words ──────────────────

        private void ShowNothing()
        {
            _detailGeneration++;
            _strip.Clear();
            _strip.Visible = false;
            _shownSegments = [];
            _lineStarts = [];
            _words.Text = VideoSpeechStrings.DetailNothingSelected;
        }

        private void ShowSelection()
        {
            var selected = SelectedEntries().Take(2).ToList();
            if (selected.Count == 0)
            {
                ShowNothing();
                return;
            }
            if (selected.Count > 1)
            {
                ShowNothing();
                _words.Text = string.Format(VideoSpeechStrings.DetailSeveralFormat, _list.SelectedIndices.Count);
                return;
            }

            var row = selected[0].Row;
            ShowWords(row);
            _ = ShowMomentsAsync(row);
        }

        private void ShowWords(VideoSpeechRow row)
        {
            _shownSegments = row.Segments;
            var text = new StringBuilder();
            var starts = new List<int>(row.Segments.Count);
            foreach (var segment in row.Segments)
            {
                starts.Add(text.Length);
                text.Append(VideoTime.Format(segment.StartMs)).Append("   ").Append(segment.Text.Trim()).Append("\r\n");
            }
            _lineStarts = [.. starts];

            if (row.Segments.Count == 0)
            {
                text.Append(row.Status switch
                {
                    SpeechStatus.NotRead => VideoSpeechStrings.DetailNotRead,
                    SpeechStatus.NothingHeard => VideoSpeechStrings.DetailNothingHeard,
                    SpeechStatus.NoSound => VideoSpeechStrings.DetailNoSound,
                    _ => string.Format(VideoSpeechStrings.DetailProblemFormat, row.Failure ?? string.Empty),
                });
            }
            _words.Text = text.ToString();
            _words.SelectionStart = 0;
            _words.SelectionLength = 0;
        }

        /// <summary>Reads the little pictures of the moments of the video in the background, and shows them over the words.</summary>
        private async Task ShowMomentsAsync(VideoSpeechRow row)
        {
            var generation = ++_detailGeneration;
            List<(long TimeMs, Bitmap Picture)> moments;
            try
            {
                moments = await Task.Run(() => MainForm.ReadMomentPictures(_catalog, row.FullPath, _clipModelId));
            }
            catch (Exception)
            {
                moments = [];
            }

            if (generation != _detailGeneration || IsDisposed)
            {
                foreach (var (_, picture) in moments)
                    picture.Dispose();
                return;
            }

            if (moments.Count == 0)
            {
                _strip.Clear();
                _strip.Visible = false;
                if (row.Status == SpeechStatus.Heard)
                    return;

                _words.Text = (row.Segments.Count == 0 ? _words.Text + "\r\n\r\n" : _words.Text) + VideoSpeechStrings.DetailNoMoments;
                return;
            }

            // A speech bubble marks the moments where something is said (hover for the words)
            _strip.SetMoments(moments, null, row.Segments.Select(s => s.StartMs));
            _strip.Visible = true;
        }

        // The words said in a stretch of the video that is shown, for the text over a moment
        private string? SaidIn(long fromMs, long toMs)
        {
            var said = string.Join(' ', _shownSegments.Where(s => s.EndMs > fromMs && s.StartMs < toMs).Select(s => s.Text.Trim()));
            return said.Length == 0 ? null : OneLine(said, 140);
        }

        /// <summary>A moment was clicked: the words said at that place are shown selected.</summary>
        private void ShowWordsAt(long timeMs)
        {
            var index = -1;
            for (var i = 0; i < _shownSegments.Count; i++)
            {
                if (_shownSegments[i].StartMs <= timeMs + 1500)
                    index = i;
            }
            if (index < 0 || index >= _lineStarts.Length)
                return;

            var start = _lineStarts[index];
            var end = index + 1 < _lineStarts.Length ? _lineStarts[index + 1] : _words.TextLength;
            _words.Select(start, Math.Max(0, end - start));
            _words.ScrollToCaret();
        }

        // ── opening a video ────────────────────────────────────────────────

        private void Open(Entry entry, bool show)
        {
            var path = entry.Row.FullPath;
            var problem = show ? ShellOpen.ShowInFolder(path) : ShellOpen.Photo(path);
            _status.Text = ShellOpen.Report(path, problem);
        }

        // ── hearing again ──────────────────────────────────────────────────

        private async Task HearAsync(IReadOnlyList<VideoSpeechRow> rows)
        {
            if (_busy)
                return;
            if (_indexing())
            {
                _status.Text = VideoSpeechStrings.StatusIndexing;
                return;
            }

            var hearable = rows.Where(r => r.Video.Info is { Audio: not null }).ToList();
            if (hearable.Count == 0)
            {
                _status.Text = VideoSpeechStrings.StatusNoSoundToHear;
                return;
            }

            var length = TimeSpan.FromSeconds(hearable.Sum(r => r.Video.Info!.DurationSeconds));
            var heardAs = hearable.Where(r => r.Language.Length > 0).GroupBy(r => r.Language).OrderByDescending(g => g.Count()).Take(4)
                .Select(g => $"{SpeechLanguages.Describe(g.Key)} - {g.Count():N0}");
            using var chooser = new SpeechLanguageDialog(
                Plural.Count(hearable.Count, VideoSpeechStrings.Video), MainForm.FormatDuration(length), string.Join(", ", heardAs), Suggest(hearable), _dark);
            if (chooser.ShowDialog(this) != DialogResult.OK)
                return;

            await HearAsync(hearable, chooser.Language);
        }

        /// <summary>The language to have selected: the one set in the settings, or else the likely one that the videos were not heard as (this is asked when that went wrong).</summary>
        private string Suggest(IReadOnlyList<VideoSpeechRow> rows)
        {
            var common = rows.Select(r => r.Language).Where(l => l.Length > 0).GroupBy(l => l).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var candidates = new List<string>();
            if (!SpeechIndexingJob.IsAuto(_defaultLanguage) && SpeechLanguages.Find(_defaultLanguage) is { } set)
                candidates.Add(set.Code);
            candidates.AddRange(["ru", "uk", "en"]);
            return candidates.FirstOrDefault(c => !string.Equals(c, common, StringComparison.OrdinalIgnoreCase)) ?? "ru";
        }

        private async Task HearAsync(IReadOnlyList<VideoSpeechRow> rows, string language)
        {
            var cts = _cts = new CancellationTokenSource();
            var keep = SelectedEntries().Select(e => e.Row.FullPath).ToList();
            SetBusy(true);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            _status.Text = SearchStrings.IndexLoadingModels;

            try
            {
                var paths = rows.Select(r => r.FullPath).ToList();
                using var catalog = await Task.Run(() => VideoCatalog.Open(_folder));
                var summary = await SpeechIndexingJob.RedoAsync(
                    catalog, _modelsFolder, _model, language, paths, new Progress<IndexProgress>(ShowProgress), cts.Token, ConfirmDownload, cores: _cores);

                Changed |= summary.Transcribed > 0;
                if (!IsDisposed)
                    _status.Text = Describe(summary, language, rows.Count);
            }
            catch (OperationCanceledException)
            {
                // Stopped before anything was heard: the text is as it was
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(VideoSpeechStrings.StatusHearErrorFormat, ex.Message);
            }
            finally
            {
                _cts = null;
                cts.Dispose();
            }

            if (IsDisposed)
                return;

            _progress.Visible = false;
            SetBusy(false);
            if (_closeWhenDone)
            {
                Close();
                return;
            }

            var note = _status.Text;
            await LoadAsync();
            _status.Text = note;

            // What was selected stays selected, with its new text
            _rebuilding = true;
            try
            {
                _list.SelectedIndices.Clear();
                var wanted = keep.ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < _view.Length; i++)
                {
                    if (wanted.Contains(_view[i].Row.FullPath))
                        _list.SelectedIndices.Add(i);
                }
            }
            finally
            {
                _rebuilding = false;
            }
            UpdateButtons();
            ShowSelection();
        }

        private static string Describe(SpeechIndexSummary summary, string language, int asked)
        {
            if (summary.DownloadDeclined)
                return VideoSearchStrings.StatusSpeechDeclined.Trim();
            if (summary.Cancelled)
                return string.Format(VideoSpeechStrings.StatusStoppedFormat, summary.Transcribed, asked);

            var named = SpeechIndexingJob.IsAuto(language) ? SpeechLanguages.Auto : SpeechLanguages.Describe(language);
            return string.Format(
                VideoSpeechStrings.StatusDoneFormat, Plural.Count(summary.Transcribed, VideoSpeechStrings.Video), Plural.Count(summary.Segments, VideoSpeechStrings.Stretch), named,
                MainForm.FormatDuration(summary.Elapsed), summary.Failed > 0 ? string.Format(VideoSpeechStrings.StatusFailedPartFormat, summary.Failed) : string.Empty);
        }

        /// <summary>Asked by the job, on its own thread, before it downloads the speech model.</summary>
        private bool ConfirmDownload(SpeechDownload download)
        {
            if (IsDisposed)
                return false;
            if (InvokeRequired)
                return Invoke(() => ConfirmDownload(download));

            return MessageBox.Show(this, string.Format(VideoSearchStrings.SpeechDownloadQuestionFormat, download.Model.Title, SearchStrings.Size(download.Model.Megabytes)),
                VideoSearchStrings.SpeechDownloadTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }

        private void ShowProgress(IndexProgress progress)
        {
            if (IsDisposed || !_busy)
                return;

            switch (progress.Phase)
            {
                case IndexPhase.DownloadingModels:
                    ShowBar(progress.Done, progress.Total);
                    _status.Text = progress.Message;
                    break;

                case IndexPhase.Speech:
                    ShowBar(progress.Done, progress.Total);
                    _status.Text = progress.Remaining is { } left
                        ? string.Format(VideoSearchStrings.IndexSpeechProgressFormat, Math.Min(progress.Done + 1, progress.Total), progress.Total, progress.Message, progress.PhotosPerSecond, MainForm.FormatDuration(left))
                        : string.Format(VideoSearchStrings.IndexSpeechProgressNoEstimateFormat, Math.Min(progress.Done + 1, progress.Total), progress.Total, progress.Message);
                    break;

                default:
                    _progress.Style = ProgressBarStyle.Marquee;
                    _status.Text = SearchStrings.IndexLoadingModels;
                    break;
            }
        }

        private void ShowBar(int done, int total)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Maximum = Math.Max(1, total);
            _progress.Value = Math.Clamp(done, 0, _progress.Maximum);
        }

        private void StopWorking()
        {
            _cts?.Cancel();
            _status.Text = SearchStrings.StatusIndexStopping;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _findBox.Enabled = _statusBox.Enabled = !busy;
            UpdateButtons();
        }

        // ── the Excel file ─────────────────────────────────────────────────

        private async Task ExportAsync()
        {
            if (_busy || _all.Count == 0)
                return;

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_folder));
            using var save = new SaveFileDialog
            {
                Title = VideoSpeechStrings.ExportTitle,
                Filter = VideoSpeechStrings.ExportFilter,
                DefaultExt = "xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = string.Format(VideoSpeechStrings.ExportFileNameFormat, string.IsNullOrWhiteSpace(name) ? VideoSpeechStrings.ColumnVideo : name),
            };
            if (save.ShowDialog(this) != DialogResult.OK)
                return;

            // Every video of the folder, whatever the list is filtered by
            var rows = _all;
            var labels = Labels();
            var file = save.FileName;
            _export.Enabled = false;
            try
            {
                var sheets = await Task.Run(() =>
                {
                    var built = SpeechWorkbook.Build(rows, labels);
                    XlsxWriter.Write(file, built);
                    return built;
                });
                if (!IsDisposed)
                    _status.Text = string.Format(VideoSpeechStrings.StatusExportedFormat, Plural.Count(rows.Count, VideoSpeechStrings.Video), Plural.Count(sheets[1].Rows.Count, VideoSpeechStrings.Stretch), file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) // a file that Excel has open is one of them
            {
                if (!IsDisposed)
                    _status.Text = string.Format(VideoSpeechStrings.StatusExportErrorFormat, ex.Message);
            }
            finally
            {
                if (!IsDisposed)
                    UpdateButtons();
            }
        }

        private static SpeechWorkbookLabels Labels() => new(
            VideoSpeechStrings.SheetVideos, VideoSpeechStrings.SheetSpeech,
            VideoSpeechStrings.ColumnVideo, VideoSpeechStrings.ColumnLength, VideoSpeechStrings.ColumnRecorded, VideoSpeechStrings.ColumnStatus, VideoSpeechStrings.ColumnLanguage,
            VideoSpeechStrings.ColumnMoments, VideoSpeechStrings.ColumnSegments, VideoSpeechStrings.ColumnMomentTimes, VideoSpeechStrings.ColumnSpeech,
            VideoSpeechStrings.ColumnStart, VideoSpeechStrings.ColumnEnd, VideoSpeechStrings.ColumnText,
            StatusText);
    }
}
