using System.Diagnostics;
using System.Globalization;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Finds photos that are the same file in different folders and moves the copies that are not in the best folder to Deleted\Duplicated.
    /// What would move is listed first, group by group, with the reason for every choice; a person can pick another copy to stay, or leave
    /// a group alone. Nothing is moved before the button is pressed.
    /// </summary>
    internal sealed class DuplicatesDialog : Form
    {
        private enum Phase { Scanning, Ready, Working }

        private sealed class BufferedListView : ListView
        {
            public BufferedListView() => DoubleBuffered = true;
        }

        // What a group is: moved as the rules (or a person) say, waiting for a person to pick the copy that stays, or left alone by a person
        private const int WillMove = 0, NeedsChoice = 1, LeftAlone = 2, AllGroups = 3;

        private readonly string _folder;
        private readonly IReadOnlyCollection<string> _extensions;
        private readonly string _likedFolder;
        private readonly PhotoIndex? _index;
        private readonly bool _dark;

        private readonly Label _intro = new() { Text = DuplicatesStrings.Intro, Dock = DockStyle.Fill, Height = 62, Padding = new Padding(0, 0, 0, 6) };
        private readonly CheckBox _whole = new() { Text = DuplicatesStrings.OptionWhole, AutoSize = true };
        private readonly CheckBox _names = new() { Text = DuplicatesStrings.OptionNames, AutoSize = true };
        private readonly Label _keepLabel = new() { Text = DuplicatesStrings.KeepLabel, AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly TextBox _keepBox = new() { Dock = DockStyle.Fill };
        private readonly Label _moveLabel = new() { Text = DuplicatesStrings.MoveLabel, AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly TextBox _moveBox = new() { Dock = DockStyle.Fill };
        private readonly Label _hint = new() { Text = DuplicatesStrings.PatternHint, AutoSize = true };
        private readonly List<(RadioButton Button, int Filter, string Format)> _filters = [];
        private readonly BufferedListView _list = new();
        private readonly Label _summary = new() { AutoSize = true };
        private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 14, Minimum = 0, Maximum = 100 };
        private readonly Label _status = new() { AutoSize = true };
        private readonly Button _move = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(150, 28) };
        private readonly Button _stop = new() { Text = DuplicatesStrings.StopButton, AutoSize = true, MinimumSize = new Size(80, 28), Visible = false };
        private readonly Button _close = new() { Text = DuplicatesStrings.CloseButton, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };
        private readonly Button _lookAgain = new() { Text = DuplicatesStrings.LookAgainButton, AutoSize = true, MinimumSize = new Size(90, 28) };
        private readonly Button _undo = new() { Text = DuplicatesStrings.UndoButton, AutoSize = true, MinimumSize = new Size(110, 28) };
        private readonly Button _keepThis = new() { Text = DuplicatesStrings.KeepThisButton, AutoSize = true, MinimumSize = new Size(100, 28) };
        private readonly Button _leaveAlone = new() { Text = DuplicatesStrings.LeaveAloneButton, AutoSize = true, MinimumSize = new Size(120, 28) };
        private readonly ToolTip _tips = new();
        private readonly ContextMenuStrip _menu = new();
        private readonly ToolStripMenuItem _keepItem = new(DuplicatesStrings.KeepMenu);
        private readonly ToolStripMenuItem _leaveItem = new(DuplicatesStrings.LeaveAloneMenu);
        private readonly ToolStripMenuItem _openItem = new(ShellOpen.OpenMenu);
        private readonly ToolStripMenuItem _showItem = new(ShellOpen.ShowMenu);
        private readonly System.Windows.Forms.Timer _typing = new() { Interval = 300 };

        // What the scan found, and what the rules and the person made of it
        private DuplicateGroup[] _groups = [];
        private DuplicatePlan[] _plans = [];
        private readonly Dictionary<string, string> _choice = new(StringComparer.OrdinalIgnoreCase);   // group (by its first path) -> the copy a person picked to stay
        private readonly HashSet<string> _left = new(StringComparer.OrdinalIgnoreCase);               // groups a person left alone
        private readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);  // copies that could not be moved
        private (int Plan, int Copy)[] _view = [];
        private readonly int[] _counts = new int[3];
        private int _filter = WillMove;
        private string _note = string.Empty;                                                           // said after the next look

        private Phase _phase = Phase.Scanning;
        private CancellationTokenSource? _cts;

        public DuplicatesDialog(string folder, IReadOnlyCollection<string> extensions, string likedFolder, PhotoIndex? index, bool dark)
        {
            _folder = folder;
            _extensions = extensions;
            _likedFolder = likedFolder;
            _index = index;
            _dark = dark;

            Text = DuplicatesStrings.Title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(820, 520);

            BuildLayout();
            Wire();
            ApplyTheme();

            SetPhase(Phase.Scanning);
            Shown += async (_, _) => await ScanAsync();
        }

        /// <summary>Whether any photo was moved (or put back): the list of the main window is out of date then.</summary>
        public bool Changed { get; private set; }

        // ── building the dialog ────────────────────────────────────────────

        private void BuildLayout()
        {
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 4) };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.Controls.Add(_whole, 0, 0);
            options.SetColumnSpan(_whole, 2);
            options.Controls.Add(_names, 0, 1);
            options.SetColumnSpan(_names, 2);
            options.Controls.Add(_keepLabel, 0, 2);
            options.Controls.Add(_keepBox, 1, 2);
            options.Controls.Add(_moveLabel, 0, 3);
            options.Controls.Add(_moveBox, 1, 3);
            options.Controls.Add(_hint, 1, 4);

            var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            AddFilter(filters, DuplicatesStrings.FilterMove, WillMove);
            AddFilter(filters, DuplicatesStrings.FilterChoose, NeedsChoice);
            AddFilter(filters, DuplicatesStrings.FilterLeft, LeftAlone);
            AddFilter(filters, DuplicatesStrings.FilterAll, AllGroups);

            _list.View = View.Details;
            _list.VirtualMode = true;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(DuplicatesStrings.ColumnGroup, 60);
            _list.Columns.Add(DuplicatesStrings.ColumnAction, 190);
            _list.Columns.Add(DuplicatesStrings.ColumnPhoto, 400);
            _list.Columns.Add(DuplicatesStrings.ColumnSize, 70);
            _list.Columns.Add(DuplicatesStrings.ColumnWhy, 330);
            _list.ContextMenuStrip = _menu;
            _menu.Items.AddRange([_keepItem, _leaveItem, new ToolStripSeparator(), _openItem, _showItem]);

            _move.Text = DuplicatesStrings.MoveButtonNothing;
            _tips.SetToolTip(_keepThis, DuplicatesStrings.KeepThisTooltip);
            _tips.SetToolTip(_leaveAlone, DuplicatesStrings.LeaveAloneTooltip);
            _tips.SetToolTip(_undo, DuplicatesStrings.UndoTooltip);

            var left = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            left.Controls.AddRange([_keepThis, _leaveAlone, _lookAgain, _undo]);
            var right = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            right.Controls.AddRange([_close, _move, _stop]);
            var buttons = new Panel { Dock = DockStyle.Fill, Height = 36 };
            buttons.Controls.AddRange([left, right]);

            _summary.Margin = new Padding(0, 6, 0, 4);
            _status.Margin = new Padding(0, 4, 0, 6);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.Controls.Add(_summary, 0, 0);
            bottom.Controls.Add(_progress, 0, 1);
            bottom.Controls.Add(_status, 0, 2);
            bottom.Controls.Add(buttons, 0, 3);

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
        }

        private void AddFilter(FlowLayoutPanel panel, string format, int filter)
        {
            var button = new RadioButton { AutoSize = true, Margin = new Padding(3, 3, 14, 3), Checked = filter == _filter };
            button.CheckedChanged += (_, _) =>
            {
                if (!button.Checked)
                    return;
                _filter = filter;
                RebuildView();
            };
            _filters.Add((button, filter, format));
            panel.Controls.Add(button);
        }

        private void Wire()
        {
            _list.RetrieveVirtualItem += (_, e) => e.Item = BuildItem(e.ItemIndex);
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    KeepThis();
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    LeaveAloneToggle();
                }
            };
            _menu.Opening += (_, e) =>
            {
                if (_list.SelectedIndices.Count == 0)
                    e.Cancel = true;
                _keepItem.Enabled = _leaveItem.Enabled = _phase == Phase.Ready;
                _openItem.Enabled = _showItem.Enabled = true; // opening a photo changes nothing, so it works at any time
                _menu.Renderer = _dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
                _menu.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
                _menu.ForeColor = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            };
            _list.MouseDoubleClick += (_, e) =>
            {
                if (_list.HitTest(e.Location).Item is { } item && item.Index >= 0 && item.Index < _view.Length)
                    OpenCopy(item.Index, show: false);
            };
            _openItem.Click += (_, _) => OpenSelectedCopy(show: false);
            _showItem.Click += (_, _) => OpenSelectedCopy(show: true);
            _keepItem.Click += (_, _) => KeepThis();
            _leaveItem.Click += (_, _) => LeaveAloneToggle();
            _keepThis.Click += (_, _) => KeepThis();
            _leaveAlone.Click += (_, _) => LeaveAloneToggle();

            // The rules are worked out again a moment after the last key, not at every one
            _typing.Tick += (_, _) =>
            {
                _typing.Stop();
                if (_phase == Phase.Ready)
                    Replan();
            };
            _keepBox.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };
            _moveBox.TextChanged += (_, _) => { _typing.Stop(); _typing.Start(); };

            _move.Click += (_, _) => MoveAsync();
            _undo.Click += (_, _) => UndoAsync();
            _lookAgain.Click += async (_, _) => await ScanAsync();
            _stop.Click += (_, _) => StopWorking();
        }

        private void ApplyTheme()
        {
            ThemeManager.Apply(this, _dark);
            foreach (Control control in new Control[] { _intro, _whole, _names, _keepLabel, _moveLabel, _hint, _summary, _status }.Concat(_filters.Select(f => (Control)f.Button)))
                control.BackColor = Color.Transparent; // the theme paints a label like a control; here it sits on a panel
            _list.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _typing.Dispose();
                _tips.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ── looking ────────────────────────────────────────────────────────

        private async Task ScanAsync()
        {
            if (_phase == Phase.Working)
                return;

            SetPhase(Phase.Scanning);
            var cts = _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = DuplicatesStrings.Listing;
            try
            {
                var listed = await Task.Run(() => PhotoScanner.Scan(_folder, recursive: true, _extensions, _likedFolder), cts.Token);
                if (IsDisposed)
                    return;

                // What was liked or put in a group was copied on purpose
                var files = listed.Where(f => !DuplicateRules.IsDeliberateCopy(_folder, f.OriginalFilePath)).ToList();
                var clock = Stopwatch.StartNew();
                var whole = _whole.Checked;
                var sameName = _names.Checked;
                var progress = new Progress<DuplicateProgress>(p => ShowComparing(p, clock));
                var result = await Task.Run(() => DuplicateFinder.Find(files, whole, progress, cts.Token, sameName), cts.Token);
                if (IsDisposed)
                    return;

                _groups = [.. result.Groups];
                _failed.Clear();
                _progress.Visible = false;

                var text = _groups.Length == 0
                    ? DuplicatesStrings.NoDuplicates
                    : string.Format(DuplicatesStrings.ScanDoneFormat,
                        Plural.Count(files.Count, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany), MainForm.FormatDuration(clock.Elapsed),
                        Plural.Count(result.Candidates, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany),
                        Plural.Count(_groups.Length, DuplicatesStrings.GroupOne, DuplicatesStrings.GroupMany));
                if (listed.Count > files.Count)
                    text += string.Format(DuplicatesStrings.ScanLeftOutPart, Plural.Count(listed.Count - files.Count, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany));
                if (_groups.Count(g => g.MetadataDiffers) is > 0 and var differing)
                    text += string.Format(DuplicatesStrings.ScanMetadataPart, Plural.Count(differing, DuplicatesStrings.GroupOne, DuplicatesStrings.GroupMany));
                if (result.Unreadable > 0)
                    text += string.Format(DuplicatesStrings.ScanUnreadablePart, Plural.Count(result.Unreadable, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany));
                _status.Text = _note + text;
                _note = string.Empty;

                SetPhase(Phase.Ready);
                Replan(chooseFilter: true);
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed)
                    return;
                Stopped(DuplicatesStrings.ScanStopped);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (IsDisposed)
                    return;
                Stopped(string.Format(DuplicatesStrings.ScanError, ex.Message));
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }
        }

        private void Stopped(string message)
        {
            _groups = [];
            _progress.Visible = false;
            _status.Text = message;
            SetPhase(Phase.Ready);
            Replan();
        }

        private void ShowComparing(DuplicateProgress progress, Stopwatch clock)
        {
            if (IsDisposed || _phase != Phase.Scanning)
                return;

            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Maximum = Math.Max(progress.Total, 1);
            _progress.Value = Math.Clamp(progress.Done, 0, _progress.Maximum);

            var seconds = clock.Elapsed.TotalSeconds;
            var rate = seconds > 0 ? progress.Done / seconds : 0;
            _status.Text = progress.Done >= 20 && rate > 0
                ? string.Format(DuplicatesStrings.ComparingFormat, progress.Done, progress.Total, progress.BytesRead / 1048576.0, progress.BytesRead / 1048576.0 / seconds,
                    MainForm.FormatDuration(TimeSpan.FromSeconds((progress.Total - progress.Done) / rate)))
                : string.Format(DuplicatesStrings.ComparingNoEstimateFormat, progress.Done, progress.Total);
        }

        // ── the groups ─────────────────────────────────────────────────────

        private static string IdOf(DuplicateGroup group) => group.Paths[0];

        private int CategoryOf(int plan) => _left.Contains(IdOf(_plans[plan].Group)) ? LeftAlone : _plans[plan].Decided ? WillMove : NeedsChoice;

        /// <summary>Works out again which copy stays in every group: from the rules (with the folders asked for) and what a person picked.</summary>
        private void Replan(bool chooseFilter = false)
        {
            var options = new DuplicateOptions(DuplicateOptions.Split(_keepBox.Text), DuplicateOptions.Split(_moveBox.Text));
            var plans = DuplicateRules.Plan(_groups, _folder, options).ToArray();
            for (var i = 0; i < plans.Length; i++)
            {
                if (_choice.TryGetValue(IdOf(_groups[i]), out var picked))
                    plans[i] = plans[i].KeepingCopy(picked);
            }
            _plans = plans;

            Array.Clear(_counts);
            for (var i = 0; i < _plans.Length; i++)
                _counts[CategoryOf(i)]++;

            foreach (var (button, filter, format) in _filters)
                button.Text = string.Format(format, filter == AllGroups ? _plans.Length : _counts[filter]);

            // After a look the list starts with what will move, else with what needs a choice, else with everything
            if (chooseFilter)
            {
                var wanted = _counts[WillMove] > 0 ? WillMove : _counts[NeedsChoice] > 0 ? NeedsChoice : AllGroups;
                _filters.First(f => f.Filter == wanted).Button.Checked = true;
            }
            RebuildView();
        }

        private void RebuildView()
        {
            var rows = new List<(int Plan, int Copy)>();
            for (var p = 0; p < _plans.Length; p++)
            {
                if (_filter != AllGroups && CategoryOf(p) != _filter)
                    continue;

                var plan = _plans[p];
                // The copy that stays first, the others by path
                foreach (var c in Enumerable.Range(0, plan.Copies.Count)
                             .OrderBy(c => plan.Keeper == c ? 0 : 1)
                             .ThenBy(c => plan.Copies[c].Path, StringComparer.OrdinalIgnoreCase))
                    rows.Add((p, c));
            }
            _view = [.. rows];

            _list.VirtualListSize = 0; // setting the same size again would not redraw
            _list.VirtualListSize = _view.Length;
            _list.Invalidate();
            UpdateSummary();
        }

        /// <summary>The copies that move, with their size in bytes.</summary>
        private (int Files, long Bytes) ToMove()
        {
            var files = 0;
            long bytes = 0;
            for (var p = 0; p < _plans.Length; p++)
            {
                if (CategoryOf(p) != WillMove)
                    continue;
                var count = _plans[p].ToMove.Count();
                files += count;
                bytes += _plans[p].ToMove.Sum(c => c.Length);
            }
            return (files, bytes);
        }

        private void UpdateSummary()
        {
            var (files, bytes) = ToMove();

            _summary.Text = _plans.Length == 0
                ? string.Empty
                : string.Format(DuplicatesStrings.SummaryFormat,
                    Plural.Count(_plans.Length, DuplicatesStrings.GroupOne, DuplicatesStrings.GroupMany),
                    Plural.Count(files, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany),
                    bytes / 1073741824.0,
                    _counts[NeedsChoice] > 0 ? string.Format(DuplicatesStrings.SummaryChoosePart, Plural.Count(_counts[NeedsChoice], DuplicatesStrings.GroupOne, DuplicatesStrings.GroupMany)) : string.Empty,
                    _counts[LeftAlone] > 0 ? string.Format(DuplicatesStrings.SummaryLeftPart, Plural.Count(_counts[LeftAlone], DuplicatesStrings.GroupOne, DuplicatesStrings.GroupMany)) : string.Empty);

            _move.Text = files > 0
                ? string.Format(DuplicatesStrings.MoveButtonFormat, Plural.Count(files, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany))
                : DuplicatesStrings.MoveButtonNothing;
            _move.Enabled = _phase == Phase.Ready && files > 0;
        }

        private List<(int Plan, int Copy)> Selected()
        {
            var rows = new List<(int, int)>(_list.SelectedIndices.Count);
            foreach (int row in _list.SelectedIndices)
            {
                if (row < _view.Length)
                    rows.Add(_view[row]);
            }
            return rows;
        }

        /// <summary>The selected photo stays, and the other copies of its group move: a person's choice, before the rules.</summary>
        private void KeepThis()
        {
            if (_phase != Phase.Ready)
                return;

            foreach (var (plan, copy) in Selected())
            {
                var id = IdOf(_plans[plan].Group);
                _choice[id] = _plans[plan].Copies[copy].Path;
                _left.Remove(id);
            }
            Replan();
        }

        private void LeaveAloneToggle()
        {
            if (_phase != Phase.Ready)
                return;

            var ids = Selected().Select(s => IdOf(_plans[s.Plan].Group)).Distinct().ToList();
            if (ids.Count == 0)
                return;

            var leave = ids.Any(id => !_left.Contains(id));
            foreach (var id in ids)
            {
                if (leave)
                    _left.Add(id);
                else
                    _left.Remove(id);
            }
            Replan();
        }

        // ── opening a photo ────────────────────────────────────────────────

        private void OpenSelectedCopy(bool show)
        {
            if (_list.SelectedIndices.Count > 0)
                OpenCopy(_list.SelectedIndices[0], show);
        }

        /// <summary>The photo of the row, in the program Windows has for it, or its folder in File Explorer: to look at a copy before deciding which one stays.</summary>
        private void OpenCopy(int row, bool show)
        {
            if (row < 0 || row >= _view.Length)
                return;

            var (p, c) = _view[row];
            var path = _plans[p].Copies[c].Path;
            var error = show ? ShellOpen.ShowInFolder(path) : ShellOpen.Photo(path);
            if (error is not null)
                _status.Text = ShellOpen.Report(path, error);
        }

        // ── the list ───────────────────────────────────────────────────────

        private ListViewItem BuildItem(int row)
        {
            var (p, c) = _view[row];
            var plan = _plans[p];
            var copy = plan.Copies[c];
            var category = CategoryOf(p);
            var id = IdOf(_plans[p].Group);

            var keeps = plan.Keeper == c;
            string action;
            if (category == LeftAlone)
                action = DuplicatesStrings.ActionLeft;
            else if (category == NeedsChoice)
                action = DuplicatesStrings.ActionChoose;
            else if (keeps)
                action = DuplicatesStrings.ActionKeep;
            else
                action = _failed.TryGetValue(copy.Path, out var failure) ? string.Format(DuplicatesStrings.ActionFailedFormat, failure) : DuplicatesStrings.ActionMove;

            var why = keeps && _choice.ContainsKey(id) && category == WillMove ? DuplicatesStrings.WhyYourChoice : copy.Reason;
            if (plan.Group.MetadataDiffers)
                why += DuplicatesStrings.WhyMetadataDiffers;
            var item = new ListViewItem(
            [
                (p + 1).ToString("N0", CultureInfo.InvariantCulture),
                action,
                Path.GetRelativePath(_folder, copy.Path),
                string.Format(CultureInfo.InvariantCulture, DuplicatesStrings.SizeFormat, copy.Length / 1048576.0),
                why,
            ]);

            // Groups alternate in shade; what stays is green, what waits for a choice is orange, what is left alone is grey
            item.BackColor = p % 2 == 0
                ? (_dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt)
                : (_dark ? Color.FromArgb(56, 57, 63) : Color.FromArgb(238, 242, 249));
            item.ForeColor = category == LeftAlone ? SystemColors.GrayText
                : category == NeedsChoice ? (_dark ? Color.FromArgb(255, 190, 90) : Color.FromArgb(180, 100, 0))
                : keeps ? (_dark ? Color.FromArgb(120, 220, 140) : Color.FromArgb(20, 120, 50))
                : (_dark ? ThemeManager.DarkFore : ThemeManager.LightFore);
            return item;
        }

        // ── moving ─────────────────────────────────────────────────────────

        private async void MoveAsync()
        {
            var todo = new List<DuplicatePlan>();
            for (var p = 0; p < _plans.Length; p++)
            {
                if (CategoryOf(p) == WillMove)
                    todo.Add(_plans[p]);
            }
            var total = todo.Sum(p => p.ToMove.Count());
            if (total == 0 || _phase != Phase.Ready)
                return;

            SetPhase(Phase.Working);
            var cts = _cts = new CancellationTokenSource();
            var log = DuplicateMover.NewLogFile(_folder, DateTime.Now);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Maximum = total;
            _progress.Value = 0;
            _status.Text = string.Format(DuplicatesStrings.MovingFormat, 0, total);
            var progress = new Progress<int>(done =>
            {
                if (IsDisposed || _phase != Phase.Working)
                    return;
                _progress.Value = Math.Clamp(done, 0, _progress.Maximum);
                _status.Text = string.Format(DuplicatesStrings.MovingFormat, done, total);
            });

            try
            {
                var report = await Task.Run(() => DuplicateMover.Move(todo, _folder, log, progress, cts.Token));
                var indexNote = await ForgetAsync(report.Moved.Select(m => m.From));
                if (IsDisposed)
                    return;

                // The groups lose the copies that are gone; a group with one photo left is no group
                var gone = report.Moved.Select(m => m.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _groups = [.. _groups
                    .Select(g =>
                    {
                        var left = Enumerable.Range(0, g.Paths.Count).Where(i => !gone.Contains(g.Paths[i])).ToList();
                        return g with { Paths = [.. left.Select(i => g.Paths[i])], Lengths = [.. left.Select(i => g.Lengths[i])] };
                    })
                    .Where(g => g.Paths.Count > 1)];
                foreach (var failure in report.Failures)
                    _failed[failure.Path] = failure.Message;

                Changed |= report.Moved.Count > 0;
                var failures = report.Failures.Count > 0 ? string.Format(DuplicatesStrings.FailedPart, Plural.Count(report.Failures.Count, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany)) : string.Empty;
                _status.Text = string.Format(
                    report.Cancelled ? DuplicatesStrings.MovedStoppedFormat : DuplicatesStrings.MovedFormat,
                    Plural.Count(report.Moved.Count, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany), report.Bytes / 1073741824.0, failures, Path.GetFileName(report.LogFile)) + indexNote;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(DuplicatesStrings.MoveError, ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (IsDisposed)
                return;
            _progress.Visible = false;
            SetPhase(Phase.Ready);
            Replan(chooseFilter: true);
        }

        /// <summary>The copies that moved are no photos of the folder any more: the search index forgets them, so that its counts and results are right.</summary>
        private async Task<string> ForgetAsync(IEnumerable<string> paths)
        {
            if (_index is null)
                return string.Empty;

            var moved = paths.ToList();
            if (moved.Count == 0)
                return string.Empty;

            try
            {
                await Task.Run(() =>
                {
                    var relative = new List<string>(moved.Count);
                    foreach (var path in moved)
                    {
                        if (_index.TryGetRelativePath(path, out var r))
                            relative.Add(r);
                    }
                    _index.Remove(relative);
                });
                return string.Empty;
            }
            catch (Exception ex)
            {
                return string.Format(DuplicatesStrings.IndexNotUpdatedFormat, ex.Message);
            }
        }

        private async void UndoAsync()
        {
            if (_phase != Phase.Ready)
                return;

            var log = DuplicateMover.LatestLog(_folder);
            if (log is null)
            {
                _status.Text = DuplicatesStrings.UndoNoLog;
                RefreshUndo();
                return;
            }

            var question = string.Format(DuplicatesStrings.UndoQuestionFormat, File.GetLastWriteTime(log), Path.GetFileName(log));
            if (MessageBox.Show(this, question, DuplicatesStrings.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SetPhase(Phase.Working);
            var cts = _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = string.Format(DuplicatesStrings.UndoProgressFormat, 0);
            var progress = new Progress<int>(done =>
            {
                if (!IsDisposed && _phase == Phase.Working)
                    _status.Text = string.Format(DuplicatesStrings.UndoProgressFormat, done);
            });

            var look = false;
            try
            {
                var report = await Task.Run(() => DuplicateMover.Undo(log, _folder, progress, cts.Token));
                if (IsDisposed)
                    return;

                Changed |= report.Moved.Count > 0;
                var skipped = report.Skipped > 0 ? string.Format(DuplicatesStrings.UndoSkippedPart, Plural.Count(report.Skipped, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany)) : string.Empty;
                if (report.Failures.Count > 0)
                    skipped += string.Format(DuplicatesStrings.FailedPart, Plural.Count(report.Failures.Count, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany));
                _note = string.Format(DuplicatesStrings.UndoDoneFormat, Plural.Count(report.Moved.Count, DuplicatesStrings.PhotoOne, DuplicatesStrings.PhotoMany), skipped)
                    + (_index is not null && report.Moved.Count > 0 ? DuplicatesStrings.UndoReindexPart : string.Empty) + " ";
                look = true; // they are duplicates again: look again
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(DuplicatesStrings.UndoError, ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (IsDisposed)
                return;
            _progress.Visible = false;
            SetPhase(Phase.Ready);
            if (look)
                await ScanAsync();
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

            _whole.Enabled = _names.Enabled = _keepBox.Enabled = _moveBox.Enabled = ready;
            _lookAgain.Enabled = _keepThis.Enabled = _leaveAlone.Enabled = ready;
            RefreshUndo();
            _stop.Visible = !ready;
            _stop.Enabled = true;
            _close.Enabled = phase != Phase.Working;
            _progress.Visible = !ready;
            UpdateSummary();
        }

        private void RefreshUndo() => _undo.Enabled = _phase == Phase.Ready && DuplicateMover.LatestLog(_folder) is not null;

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
