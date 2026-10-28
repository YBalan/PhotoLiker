using System.Diagnostics;
using System.Globalization;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Shows what "Align dates" would do - how many photos, and which - and does it when asked. The photos are looked at once
    /// (the start of each file is read); what the ticks mean for each of them is worked out again at once whenever a tick changes.
    /// </summary>
    internal sealed class DateAlignmentDialog : Form
    {
        private enum Phase { Scanning, Ready, Working }

        private sealed class BufferedListView : ListView
        {
            public BufferedListView() => DoubleBuffered = true;
        }

        private readonly string _folder;
        private readonly bool _recursive;
        private readonly IReadOnlyCollection<string> _extensions;
        private readonly string _likedFolder;
        private readonly PhotoIndex? _index;
        private readonly bool _dark;

        private readonly Label _intro = new() { Dock = DockStyle.Fill, Height = 62, Padding = new Padding(0, 0, 0, 6) };
        private readonly CheckBox _created = new() { Text = DateAlignmentStrings.OptionCreated, Checked = true, AutoSize = true };
        private readonly CheckBox _modified = new() { Text = DateAlignmentStrings.OptionModified, Checked = true, AutoSize = true };
        private readonly CheckBox _exif = new() { Text = DateAlignmentStrings.OptionExif, Checked = false, AutoSize = true };
        private readonly List<(RadioButton Button, DateAlignmentStatus? Status, string Format)> _filters = [];
        private readonly BufferedListView _list = new();
        private readonly Label _summary = new() { AutoSize = true };
        private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 14, Minimum = 0, Maximum = 100 };
        private readonly Label _status = new() { AutoSize = true };
        private readonly Button _apply = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(120, 28) };
        private readonly Button _stop = new() { Text = DateAlignmentStrings.StopButton, AutoSize = true, MinimumSize = new Size(80, 28) };
        private readonly Button _close = new() { Text = DateAlignmentStrings.CloseButton, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };
        private readonly Button _undo = new() { Text = DateAlignmentStrings.UndoButton, AutoSize = true, MinimumSize = new Size(110, 28) };
        private readonly Button _lookAgain = new() { Text = DateAlignmentStrings.LookAgainButton, AutoSize = true, MinimumSize = new Size(90, 28) };
        private readonly ToolTip _tips = new();
        private readonly ContextMenuStrip _menu = new();
        private readonly ToolStripMenuItem _leaveOut = new(DateAlignmentStrings.LeaveOutMenu);
        private readonly ToolStripMenuItem _include = new(DateAlignmentStrings.IncludeMenu);
        private readonly Button _assign = new() { Text = DateAlignmentStrings.AssignButton, AutoSize = true, MinimumSize = new Size(110, 28) };
        private readonly ToolStripMenuItem _assignItem = new(DateAlignmentStrings.AssignMenu);
        private readonly ToolStripMenuItem _forgetItem = new(DateAlignmentStrings.ForgetMenu);
        private readonly ToolStripMenuItem _openItem = new(ShellOpen.OpenMenu);
        private readonly ToolStripMenuItem _showItem = new(ShellOpen.ShowMenu);
        private readonly Dictionary<string, DateTime> _assigned = new(StringComparer.OrdinalIgnoreCase); // dates taken given to photos that have none that is believed

        // What the scan found, and what the ticks make of it
        private PhotoDateFacts[] _facts = [];
        private DateAlignmentEntry[] _entries = [];
        private bool[] _left = [];                       // photos the user took out of the run
        private int[] _view = [];                        // the rows shown: indexes into _facts
        private readonly int[] _counts = new int[4];
        private readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _positions = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PhotoFile> _photos = new(StringComparer.OrdinalIgnoreCase);
        private LikedLibrary? _liked;
        private GroupLibrary? _groups;
        private DateAlignmentStatus? _filter = DateAlignmentStatus.Change;

        private Phase _phase = Phase.Scanning;
        private CancellationTokenSource? _cts;

        public DateAlignmentDialog(string folder, bool recursive, IReadOnlyCollection<string> extensions, string likedFolder, PhotoIndex? index, bool dark)
        {
            _folder = folder;
            _recursive = recursive;
            _extensions = extensions;
            _likedFolder = likedFolder;
            _index = index;
            _dark = dark;

            Text = DateAlignmentStrings.Title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1020, 640);
            MinimumSize = new Size(760, 480);

            BuildLayout();
            Wire();
            ApplyTheme();

            SetPhase(Phase.Scanning);
            Shown += async (_, _) => await ScanAsync();
        }

        /// <summary>Whether any file was changed (or put back): the photo list of the main window is out of date then.</summary>
        public bool Changed { get; private set; }

        // ── building the dialog ────────────────────────────────────────────

        private void BuildLayout()
        {
            _intro.Text = DateAlignmentStrings.Intro;

            var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            options.Controls.AddRange([_created, _modified, _exif]);

            var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            AddFilter(filters, DateAlignmentStrings.FilterChange, DateAlignmentStatus.Change);
            AddFilter(filters, DateAlignmentStrings.FilterAligned, DateAlignmentStatus.Aligned);
            AddFilter(filters, DateAlignmentStrings.FilterNoDate, DateAlignmentStatus.NoDate);
            AddFilter(filters, DateAlignmentStrings.FilterUnreadable, DateAlignmentStatus.Unreadable);
            AddFilter(filters, DateAlignmentStrings.FilterAll, null);

            _list.View = View.Details;
            _list.VirtualMode = true;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(DateAlignmentStrings.ColumnPhoto, 330);
            _list.Columns.Add(DateAlignmentStrings.ColumnTaken, 150);
            _list.Columns.Add(DateAlignmentStrings.ColumnCreated, 135);
            _list.Columns.Add(DateAlignmentStrings.ColumnModified, 135);
            _list.Columns.Add(DateAlignmentStrings.ColumnAction, 240);
            _list.ContextMenuStrip = _menu;
            _menu.Items.AddRange([_assignItem, _forgetItem, new ToolStripSeparator(), _leaveOut, _include, new ToolStripSeparator(), _openItem, _showItem]);

            _apply.Text = DateAlignmentStrings.ApplyButtonNothing;
            _undo.Enabled = false;
            _stop.Visible = false;

            var left = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            left.Controls.AddRange([_assign, _undo, _lookAgain]);
            var right = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            right.Controls.AddRange([_close, _apply, _stop]);
            var buttons = new Panel { Dock = DockStyle.Fill, Height = 36 };
            buttons.Controls.AddRange([left, right]);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            bottom.Controls.Add(_summary, 0, 0);
            bottom.Controls.Add(_progress, 0, 1);
            bottom.Controls.Add(_status, 0, 2);
            bottom.Controls.Add(buttons, 0, 3);
            _summary.Margin = new Padding(0, 6, 0, 4);
            _status.Margin = new Padding(0, 4, 0, 6);
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 10, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_intro, 0, 0);
            root.Controls.Add(options, 0, 1);
            root.Controls.Add(filters, 0, 2);
            root.Controls.Add(_list, 0, 3);
            root.Controls.Add(bottom, 0, 4);
            Controls.Add(root);

            CancelButton = _close;
            _tips.SetToolTip(_undo, DateAlignmentStrings.UndoTooltip);
            _tips.SetToolTip(_assign, DateAlignmentStrings.AssignTooltip);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }

        private void AddFilter(FlowLayoutPanel panel, string format, DateAlignmentStatus? status)
        {
            var button = new RadioButton { AutoSize = true, Margin = new Padding(3, 3, 14, 3), Checked = status == _filter };
            button.CheckedChanged += (_, _) =>
            {
                if (!button.Checked)
                    return;
                _filter = status;
                RebuildView();
            };
            _filters.Add((button, status, format));
            panel.Controls.Add(button);
        }

        private void Wire()
        {
            _list.RetrieveVirtualItem += (_, e) => e.Item = BuildItem(e.ItemIndex);
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Space)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    var rows = SelectedPhotos();
                    LeaveOut(rows.Count > 0 && rows.Any(i => !_left[i]));
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    AssignDate();
                }
            };
            _menu.Opening += (_, e) =>
            {
                var ready = _phase == Phase.Ready;
                var selected = SelectedPhotos();
                _leaveOut.Enabled = _include.Enabled = ready && selected.Any(i => _entries[i].Status == DateAlignmentStatus.Change);
                _assignItem.Enabled = ready && selected.Any(Eligible);
                _forgetItem.Enabled = ready && selected.Any(i => _assigned.ContainsKey(_facts[i].Path));
                _openItem.Enabled = _showItem.Enabled = selected.Count > 0; // opening a photo changes nothing, so it works at any time
                if (selected.Count == 0)
                    e.Cancel = true;
                _menu.Renderer = _dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
                _menu.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
                _menu.ForeColor = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            };
            _leaveOut.Click += (_, _) => LeaveOut(true);
            _include.Click += (_, _) => LeaveOut(false);
            _list.MouseDoubleClick += (_, e) =>
            {
                if (_list.HitTest(e.Location).Item is { } item && item.Index >= 0 && item.Index < _view.Length)
                    Report(ShellOpen.Photo(_facts[_view[item.Index]].Path), _facts[_view[item.Index]].Path);
            };
            _openItem.Click += (_, _) => OpenSelected(show: false);
            _showItem.Click += (_, _) => OpenSelected(show: true);
            _assignItem.Click += (_, _) => AssignDate();
            _forgetItem.Click += (_, _) => ForgetAssigned();
            _assign.Click += (_, _) => AssignDate();

            EventHandler options = (_, _) =>
            {
                if (_phase == Phase.Ready)
                    Replan();
            };
            _created.CheckedChanged += options;
            _modified.CheckedChanged += options;
            _exif.CheckedChanged += options;

            _apply.Click += (_, _) => ApplyAsync();
            _undo.Click += (_, _) => UndoAsync();
            _lookAgain.Click += async (_, _) => await ScanAsync();
            _stop.Click += (_, _) => StopWorking();
        }

        private void ApplyTheme()
        {
            ThemeManager.Apply(this, _dark);

            // The theme paints labels and tick boxes in the colour of a control; here they sit on a panel and should show its colour
            foreach (Control control in new Control[] { _intro, _created, _modified, _exif, _summary, _status }.Concat(_filters.Select(f => (Control)f.Button)))
                control.BackColor = Color.Transparent;
            _list.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
        }

        // ── looking at the photos ──────────────────────────────────────────

        private async Task ScanAsync()
        {
            if (_phase == Phase.Working)
                return;

            SetPhase(Phase.Scanning);
            var cts = _cts = new CancellationTokenSource();
            ShowMarquee(DateAlignmentStrings.Listing);
            try
            {
                var (files, liked, groups) = await Task.Run(() =>
                {
                    var found = PhotoScanner.Scan(_folder, _recursive, _extensions, _likedFolder);
                    return (found,
                        LikedLibrary.Scan(_likedFolder, found),
                        string.IsNullOrWhiteSpace(_likedFolder) ? null : GroupLibrary.Scan(_likedFolder, found));
                }, cts.Token);
                if (IsDisposed)
                    return;

                var clock = Stopwatch.StartNew();
                var now = DateTime.Now;
                ShowBar(0, files.Count);
                var progress = new Progress<int>(done => ShowScanProgress(done, files.Count, clock));
                var facts = await Task.Run(() => DateAlignment.InspectAll(files, now, progress, cts.Token), cts.Token);
                if (IsDisposed)
                    return;

                _facts = facts;
                _left = new bool[facts.Length];
                _failed.Clear();
                _liked = liked;
                _groups = groups;
                _photos = files.ToDictionary(f => f.OriginalFilePath, StringComparer.OrdinalIgnoreCase);
                _positions = new Dictionary<string, int>(facts.Length, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < facts.Length; i++)
                    _positions[facts[i].Path] = i;

                HideBar();
                _status.Text = files.Count == 0
                    ? DateAlignmentStrings.NoPhotos
                    : string.Format(DateAlignmentStrings.ScanDoneFormat, files.Count, MainForm.FormatDuration(clock.Elapsed));
                SetPhase(Phase.Ready);
                Replan(preferChange: true);
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed)
                    return;
                _facts = [];
                HideBar();
                _status.Text = DateAlignmentStrings.ScanStopped;
                SetPhase(Phase.Ready);
                Replan();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (IsDisposed)
                    return;
                _facts = [];
                HideBar();
                _status.Text = string.Format(DateAlignmentStrings.ScanError, ex.Message);
                SetPhase(Phase.Ready);
                Replan();
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }
        }

        private void ShowScanProgress(int done, int total, Stopwatch clock)
        {
            if (IsDisposed || _phase != Phase.Scanning)
                return;

            ShowBar(done, total);
            var rate = clock.Elapsed.TotalSeconds > 0 ? done / clock.Elapsed.TotalSeconds : 0;
            _status.Text = done >= 20 && rate > 0
                ? string.Format(DateAlignmentStrings.ScanProgressFormat, done, total, rate, MainForm.FormatDuration(TimeSpan.FromSeconds((total - done) / rate)))
                : string.Format(DateAlignmentStrings.ScanProgressNoEstimateFormat, done, total);
        }

        // ── what the ticks mean ────────────────────────────────────────────

        private DateAlignmentOptions CurrentOptions() => new(_created.Checked, _modified.Checked, _exif.Checked);

        private void Replan(bool preferChange = false)
        {
            var options = CurrentOptions();
            _entries = [.. _facts.Select(f => DateAlignment.Plan(f, options, _assigned.TryGetValue(f.Path, out var given) ? given : null))];

            Array.Clear(_counts);
            foreach (var entry in _entries)
                _counts[(int)entry.Status]++;

            foreach (var (button, status, format) in _filters)
                button.Text = string.Format(format, status is { } s ? _counts[(int)s] : _entries.Length);

            // After a scan the list starts with what will change, or with everything if nothing will
            if (preferChange)
            {
                var wanted = _counts[(int)DateAlignmentStatus.Change] > 0 ? DateAlignmentStatus.Change : (DateAlignmentStatus?)null;
                _filters.First(f => f.Status == wanted).Button.Checked = true;
            }
            RebuildView();
        }

        private void RebuildView()
        {
            _view = _filter is { } status
                ? [.. Enumerable.Range(0, _entries.Length).Where(i => _entries[i].Status == status)]
                : [.. Enumerable.Range(0, _entries.Length)];

            _list.VirtualListSize = 0; // setting the same size again would not redraw
            _list.VirtualListSize = _view.Length;
            _list.Invalidate();
            UpdateSummary();
        }

        private int ToChange()
        {
            var count = 0;
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Status == DateAlignmentStatus.Change && !_left[i])
                    count++;
            }
            return count;
        }

        private void UpdateSummary()
        {
            var change = ToChange();
            var left = _counts[(int)DateAlignmentStatus.Change] - change;

            _summary.Text =
                _entries.Length == 0 ? string.Empty :
                !CurrentOptions().Any ? DateAlignmentStrings.SummaryNothingChecked :
                _counts[(int)DateAlignmentStatus.Change] > 0
                    ? string.Format(DateAlignmentStrings.SummaryFormat, change, _entries.Length, left > 0 ? string.Format(DateAlignmentStrings.SummaryLeftOutPart, left) : string.Empty)
                    : string.Format(DateAlignmentStrings.SummaryNothing, _entries.Length);

            _apply.Text = change > 0 ? string.Format(DateAlignmentStrings.ApplyButtonFormat, change) : DateAlignmentStrings.ApplyButtonNothing;
            _apply.Enabled = _phase == Phase.Ready && change > 0;
        }

        /// <summary>The photos (indexes into the facts) that are selected in the list.</summary>
        private List<int> SelectedPhotos()
        {
            var rows = new List<int>(_list.SelectedIndices.Count);
            foreach (int row in _list.SelectedIndices)
            {
                if (row < _view.Length)
                    rows.Add(_view[row]);
            }
            return rows;
        }

        private void LeaveOut(bool leave)
        {
            foreach (var i in SelectedPhotos().Where(i => _entries[i].Status == DateAlignmentStatus.Change))
                _left[i] = leave;

            _list.Invalidate();
            UpdateSummary();
        }

        // ── the list ───────────────────────────────────────────────────────

        private ListViewItem BuildItem(int row)
        {
            var i = _view[row];
            var facts = _facts[i];
            var entry = _entries[i];
            var readable = facts.Problem is null;

            var item = new ListViewItem(
            [
                Path.GetRelativePath(_folder, facts.Path),
                readable ? TakenText(entry.Facts) + (entry.Assigned ? DateAlignmentStrings.GivenNote : string.Empty) : string.Empty,
                readable ? FileTimeText(facts.CreatedUtc) : string.Empty,
                readable ? FileTimeText(facts.ModifiedUtc) : string.Empty,
                ActionText(i),
            ]);
            if (entry.Status != DateAlignmentStatus.Change || _left[i])
                item.ForeColor = SystemColors.GrayText;
            return item;
        }

        private static string FileTimeText(DateTime utc) => utc.ToLocalTime().ToString(DateText.DisplayFormat, CultureInfo.InvariantCulture);

        private static string TakenText(PhotoDateFacts facts)
        {
            if (facts.Taken is { } taken)
                return taken.ToString(DateText.DisplayFormat, CultureInfo.InvariantCulture);

            var written = facts.Exif.Dates.Original ?? facts.Exif.Dates.Digitized ?? facts.Exif.Dates.Modified;
            return written is { } date
                ? string.Format(DateAlignmentStrings.TakenNotBelievableFormat, date.ToString(DateText.DisplayFormat, CultureInfo.InvariantCulture))
                : DateAlignmentStrings.TakenNone;
        }

        private string ActionText(int i)
        {
            var entry = _entries[i];
            if (_failed.TryGetValue(entry.Path, out var failure))
                return string.Format(DateAlignmentStrings.ActionFailedFormat, failure);

            switch (entry.Status)
            {
                case DateAlignmentStatus.Unreadable:
                    return entry.Facts.Problem ?? string.Empty;
                case DateAlignmentStatus.NoDate:
                    return DateAlignmentStrings.ActionNoDate;
                case DateAlignmentStatus.Aligned:
                    return DateAlignmentStrings.ActionAligned + (entry.Assigned ? DateAlignmentStrings.GivenNote : string.Empty);
            }

            if (_left[i])
                return DateAlignmentStrings.ActionLeftOut;

            var parts = new List<string>(5);
            if (entry.SetCreated)
                parts.Add(DateAlignmentStrings.ActionCreated);
            if (entry.SetModified)
                parts.Add(DateAlignmentStrings.ActionModified);
            if (entry.SetExif.HasFlag(ExifDateFields.Original))
                parts.Add(DateAlignmentStrings.ActionExifOriginal);
            if (entry.SetExif.HasFlag(ExifDateFields.Digitized))
                parts.Add(DateAlignmentStrings.ActionExifDigitized);
            if (entry.SetExif.HasFlag(ExifDateFields.Modified))
                parts.Add(DateAlignmentStrings.ActionExifModified);
            return string.Format(DateAlignmentStrings.ActionChangeFormat, string.Join(", ", parts)) + (entry.Assigned ? DateAlignmentStrings.ActionGivenNote : string.Empty);
        }

        // ── opening a photo ────────────────────────────────────────────────

        /// <summary>The first selected photo, in the program Windows has for it, or its folder in File Explorer.</summary>
        private void OpenSelected(bool show)
        {
            if (SelectedPhotos() is not [var first, ..])
                return;

            var path = _facts[first].Path;
            Report(show ? ShellOpen.ShowInFolder(path) : ShellOpen.Photo(path), path);
        }

        private void Report(string? error, string path)
        {
            if (error is not null)
                _status.Text = ShellOpen.Report(path, error);
        }

        // ── photos without a usable date ───────────────────────────────────

        /// <summary>A photo that could be read and has no EXIF date that is believed: it can be given a date.</summary>
        private bool Eligible(int i) => _facts[i].Problem is null && _facts[i].Taken is null;

        /// <summary>Asks where the date of the photos without a usable one comes from, and gives them that date: they are then planned like the others.</summary>
        private void AssignDate()
        {
            if (_phase != Phase.Ready)
                return;

            // "All" means the photos still waiting for a date, not the ones that were given one a moment ago: the selected ones may be either
            var candidates = Enumerable.Range(0, _facts.Length).Where(Eligible).ToList();
            var waiting = candidates.Where(i => !_assigned.ContainsKey(_facts[i].Path)).ToList();
            var selected = SelectedPhotos().Where(Eligible).Distinct().ToList();
            if (waiting.Count == 0 && selected.Count == 0)
            {
                _status.Text = DateAlignmentStrings.StatusNothingToGive;
                return;
            }

            using var dialog = new AssignDateDialog([.. selected.Select(i => _facts[i])], [.. waiting.Select(i => _facts[i])], _folder, _dark);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            var now = DateTime.Now;
            var scope = dialog.Scope().Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int given = 0, none = 0;
            foreach (var i in candidates.Where(i => scope.Contains(_facts[i].Path)))
            {
                if (DateSources.Resolve(dialog.Choice, _facts[i], _folder, dialog.Typed, now) is { } date)
                {
                    _assigned[_facts[i].Path] = date;
                    given++;
                }
                else
                {
                    none++;
                }
            }

            _status.Text = string.Format(DateAlignmentStrings.StatusGiven, Plural.Count(given, "photo", "photos"), none > 0 ? string.Format(DateAlignmentStrings.StatusGivenNonePart, Plural.Count(none, "photo", "photos")) : string.Empty);
            Replan();
        }

        /// <summary>Takes the date back from the selected photos: they have none again.</summary>
        private void ForgetAssigned()
        {
            if (_phase != Phase.Ready)
                return;

            var forgotten = SelectedPhotos().Count(i => _assigned.Remove(_facts[i].Path));
            if (forgotten == 0)
                return;

            _status.Text = string.Format(DateAlignmentStrings.StatusForgot, Plural.Count(forgotten, "photo", "photos"));
            Replan();
        }

        // ── doing it ───────────────────────────────────────────────────────

        private async void ApplyAsync()
        {
            var todo = new List<DateAlignmentEntry>();
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Status == DateAlignmentStatus.Change && !_left[i])
                    todo.Add(_entries[i]);
            }
            if (todo.Count == 0 || _phase != Phase.Ready)
                return;

            SetPhase(Phase.Working);
            var cts = _cts = new CancellationTokenSource();
            var log = DateAligner.NewLogFile(_folder, DateTime.Now);
            var photos = _photos;
            var liked = _liked;
            var groups = _groups;

            // A copy is matched with its photo by size and time, so it gets the times of the photo (looked up with the times it has now)
            IReadOnlyList<string> CopiesOf(string path)
            {
                if (!photos.TryGetValue(path, out var photo))
                    return [];
                var copies = new List<string>();
                if (liked?.Find(photo) is { } likedCopy)
                    copies.Add(likedCopy);
                if (groups is not null)
                    copies.AddRange(groups.CopiesOf(photo));
                return copies;
            }

            ShowBar(0, todo.Count);
            _status.Text = string.Format(DateAlignmentStrings.ApplyProgressFormat, 0, todo.Count);
            var progress = new Progress<int>(done =>
            {
                if (IsDisposed || _phase != Phase.Working)
                    return;
                ShowBar(done, todo.Count);
                _status.Text = string.Format(DateAlignmentStrings.ApplyProgressFormat, done, todo.Count);
            });

            try
            {
                var report = await Task.Run(() => DateAligner.Apply(todo, _folder, log, CopiesOf, progress, cts.Token));
                var indexNote = await UpdateIndexAsync(report.Photos);
                if (IsDisposed)
                    return;

                var after = report.Photos.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < _facts.Length; i++)
                {
                    if (after.TryGetValue(_facts[i].Path, out var done))
                        _facts[i] = DateAlignment.After(_facts[i], done.Written, done.CreatedUtc, done.ModifiedUtc);
                }
                foreach (var failure in report.Failures)
                    _failed[failure.Path] = failure.Message;

                Changed |= report.Photos.Count > 0;
                _status.Text = DescribeRun(report) + indexNote;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(DateAlignmentStrings.ApplyError, ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (IsDisposed)
                return;
            HideBar();
            SetPhase(Phase.Ready);
            Replan();
        }

        private static string DescribeRun(DateAlignmentReport report)
        {
            var parts = string.Empty;
            if (report.Copies > 0)
                parts += string.Format(DateAlignmentStrings.CopiesPart, report.Copies);
            if (report.Failures.Count > 0)
                parts += string.Format(DateAlignmentStrings.FailedPart, report.Failures.Count);
            if (report.NotKept > 0)
                parts += string.Format(DateAlignmentStrings.NotKeptPart, report.NotKept);

            return string.Format(
                report.Cancelled ? DateAlignmentStrings.ApplyStoppedFormat : DateAlignmentStrings.ApplyDoneFormat,
                report.Photos.Count, parts, Path.GetFileName(report.LogFile));
        }

        private async void UndoAsync()
        {
            if (_phase != Phase.Ready)
                return;

            var log = DateAligner.LatestLog(_folder);
            if (log is null)
            {
                _status.Text = DateAlignmentStrings.UndoNoLog;
                RefreshUndo();
                return;
            }

            var question = string.Format(DateAlignmentStrings.UndoQuestionFormat, File.GetLastWriteTime(log), Path.GetFileName(log));
            if (MessageBox.Show(this, question, DateAlignmentStrings.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetPhase(Phase.Working);
            var cts = _cts = new CancellationTokenSource();
            ShowMarquee(string.Format(DateAlignmentStrings.UndoProgressFormat, 0));
            var progress = new Progress<int>(done =>
            {
                if (!IsDisposed && _phase == Phase.Working)
                    _status.Text = string.Format(DateAlignmentStrings.UndoProgressFormat, done);
            });

            try
            {
                var report = await Task.Run(() => DateAligner.Undo(log, progress, cts.Token));
                var indexNote = await UpdateIndexAsync(report.Photos);
                if (IsDisposed)
                    return;

                Changed |= report.Photos.Count > 0;
                var skipped = report.Skipped > 0 ? string.Format(DateAlignmentStrings.UndoSkippedPart, report.Skipped) : string.Empty;
                var copies = report.Copies > 0 ? string.Format(DateAlignmentStrings.CopiesPart, report.Copies) : string.Empty;
                _status.Text = string.Format(DateAlignmentStrings.UndoDoneFormat, report.Photos.Count, copies, skipped) + indexNote;
                if (report.Failures.Count > 0)
                    _status.Text += string.Format(DateAlignmentStrings.FailedPart, report.Failures.Count);

                await LookAtAgainAsync(report.Photos.Select(p => p.Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(DateAlignmentStrings.UndoError, ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (IsDisposed)
                return;
            HideBar();
            SetPhase(Phase.Ready);
            Replan();
        }

        /// <summary>Reads those photos again, since a run changed them behind the back of what the list knows.</summary>
        private async Task LookAtAgainAsync(IEnumerable<string> paths)
        {
            var fresh = paths.Select(p => new PhotoFile(p, string.Empty)).ToList(); // without times: they are read from the disk
            if (fresh.Count == 0)
                return;

            var now = DateTime.Now;
            var facts = await Task.Run(() => DateAlignment.InspectAll(fresh, now, null, CancellationToken.None));
            if (IsDisposed)
                return;

            foreach (var item in facts)
            {
                if (_positions.TryGetValue(item.Path, out var position))
                    _facts[position] = item;
            }
        }

        /// <summary>Tells the index of the folder that the photos are the same, so that "Update index" does not analyze them again.</summary>
        private async Task<string> UpdateIndexAsync(IReadOnlyList<AlignedFile> photos)
        {
            if (_index is null || photos.Count == 0)
                return string.Empty;

            var stamps = photos.Select(p => (p.Path, p.PreviousModifiedUtc.Ticks, p.ModifiedUtc.Ticks)).ToArray();
            try
            {
                await Task.Run(() => _index.UpdateStamps(stamps));
                return string.Empty;
            }
            catch (Exception ex)
            {
                return string.Format(DateAlignmentStrings.IndexNotUpdatedFormat, ex.Message);
            }
        }

        private void StopWorking()
        {
            _cts?.Cancel();
            _stop.Enabled = false;
        }

        // ── state ──────────────────────────────────────────────────────────

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            var ready = phase == Phase.Ready;

            _created.Enabled = _modified.Enabled = _exif.Enabled = ready;
            _lookAgain.Enabled = _assign.Enabled = ready;
            _undo.Enabled = ready && DateAligner.LatestLog(_folder) is not null;
            _stop.Visible = !ready;
            _stop.Enabled = true;
            _close.Enabled = phase != Phase.Working;
            _progress.Visible = !ready;
            UpdateSummary();
        }

        private void RefreshUndo() => _undo.Enabled = _phase == Phase.Ready && DateAligner.LatestLog(_folder) is not null;

        private void ShowMarquee(string text)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            _status.Text = text;
        }

        private void ShowBar(int done, int total)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Visible = true;
            _progress.Maximum = Math.Max(total, 1);
            _progress.Value = Math.Clamp(done, 0, _progress.Maximum);
        }

        private void HideBar() => _progress.Visible = false;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_phase == Phase.Working)
            {
                // A run is stopped between two chunks of files, not abandoned in the middle of one
                e.Cancel = true;
                StopWorking();
                return;
            }

            _cts?.Cancel();
            base.OnFormClosing(e);
        }
    }
}
