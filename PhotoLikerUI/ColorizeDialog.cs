using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Colorize: gives colours to black and white photos (see <see cref="Colorizer"/>) and saves each result as a copy next to its photo (see <see cref="ColorizedCopy"/>); no photo is changed.
    /// It works either on the photo that is shown in the main window, or on the black and white photos of the list: the window looks at every photo of the list in the background
    /// (how much colour it has, see <see cref="BlackAndWhite"/>), puts the ones that have none in a list with ticks, and colorizes the ticked ones one after the other. A tick that is wrong
    /// (a photo that is toned on purpose, a photo that already has a copy) is taken off by hand.
    /// </summary>
    internal sealed class ColorizeDialog : Form
    {
        private enum Phase { Idle, Scanning, Ready, Working }

        /// <summary>The unit of looking at photos: a batch is measured on all the cores, then the list and the bar are brought up to date.</summary>
        private const int ScanBatch = 48;

        private readonly IReadOnlyList<string> _photos;
        private readonly string? _current;
        private readonly Func<Task<bool>> _ensureModelAsync;
        private readonly EffectModels _models;
        private readonly bool _dark;

        private readonly RadioButton _thisPhoto = new() { AutoSize = true, Margin = new Padding(3, 3, 18, 3) };
        private readonly RadioButton _listPhotos = new() { AutoSize = true, Margin = new Padding(3, 3, 18, 3) };
        private readonly NumericUpDown _strength = new() { Minimum = 20, Maximum = 200, Increment = 10, Value = 100, Width = 64 };
        private readonly ListView _list = new() { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill };
        private readonly Label _summary = new() { AutoSize = true };
        private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 14, Minimum = 0, Maximum = 100 };
        private readonly Label _status = new() { AutoSize = true };
        private readonly Button _colorize = new() { AutoSize = true, MinimumSize = new Size(120, 28), Text = ColorizeStrings.ColorizeButton };
        private readonly Button _stop = new() { AutoSize = true, MinimumSize = new Size(80, 28), Text = ColorizeStrings.StopButton, Visible = false };
        private readonly Button _close = new() { AutoSize = true, MinimumSize = new Size(80, 28), Text = ColorizeStrings.CloseButton, DialogResult = DialogResult.Cancel };
        private readonly Button _tickAll = new() { AutoSize = true, MinimumSize = new Size(90, 28), Text = ColorizeStrings.TickAllButton };
        private readonly Button _tickNone = new() { AutoSize = true, MinimumSize = new Size(90, 28), Text = ColorizeStrings.TickNoneButton };

        private Phase _phase = Phase.Idle;
        private CancellationTokenSource? _cts;
        private bool _updating;               // the ticks are being set by the code: not by a click
        private bool _listScanned;            // the photos of the list were looked at (they are looked at once)
        private int _saved;

        /// <param name="photos">The photos of the list of the main window (the videos are not looked at, nor the copies that were made by this window).</param>
        /// <param name="current">The photo that is shown in the main window, or null when none is.</param>
        /// <param name="ensureModelAsync">Makes sure the colorizing model is on this computer (asks, and downloads it, if it is not) and says whether it is.</param>
        public ColorizeDialog(IReadOnlyList<string> photos, string? current, EffectModels models, Func<Task<bool>> ensureModelAsync, bool dark)
        {
            _photos = [.. photos.Where(path => !MediaKinds.IsVideo(path) && !ColorizedCopy.IsCopy(path))];
            _current = current;
            _models = models;
            _ensureModelAsync = ensureModelAsync;
            _dark = dark;

            Text = ColorizeStrings.Title;
            StartPosition = FormStartPosition.CenterParent;
            Icon = DialogIcons.Colorize;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(860, 580);
            MinimumSize = new Size(640, 440);

            BuildLayout();

            _thisPhoto.Text = string.Format(ColorizeStrings.ScopeThisFormat, current is null ? string.Empty : Path.GetFileName(current));
            _listPhotos.Text = ColorizeStrings.ScopeList;
            _thisPhoto.Visible = current is not null; // with no photo shown there is only the list
            (current is not null ? _thisPhoto : _listPhotos).Checked = true;

            Wire();
            ThemeManager.Apply(this, dark);
            foreach (var control in Descendants(this).Where(control => control is Label or RadioButton))
                control.BackColor = Color.Transparent; // the theme paints a label like a control; here it sits on the window
            _list.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;

            UpdateSummary();
            SetPhase(Phase.Idle);
            Shown += async (_, _) => await ChooseScopeAsync();
        }

        /// <summary>How many copies were made: the list of the main window has to be made again if there were any.</summary>
        public int Saved => _saved;

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var inner in Descendants(child))
                    yield return inner;
            }
        }

        // ── building the window ────────────────────────────────────────────

        private void BuildLayout()
        {
            _list.Columns.Add(ColorizeStrings.ColumnPhoto, 210);
            _list.Columns.Add(ColorizeStrings.ColumnFolder, 230);
            _list.Columns.Add(ColorizeStrings.ColumnColour, 130);
            _list.Columns.Add(ColorizeStrings.ColumnResult, 220);

            var intro = new Label { Text = ColorizeStrings.Intro, Dock = DockStyle.Fill, AutoSize = false, Height = 104, Padding = new Padding(0, 0, 0, 6), BackColor = Color.Transparent };

            var scope = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) };
            scope.Controls.AddRange([_thisPhoto, _listPhotos]);

            var strengthLabel = new Label { Text = ColorizeStrings.StrengthLabel, AutoSize = true, Margin = new Padding(3, 7, 3, 0), BackColor = Color.Transparent };
            var percent = new Label { Text = "%", AutoSize = true, Margin = new Padding(0, 7, 12, 0), BackColor = Color.Transparent };
            var hint = new Label { Text = ColorizeStrings.StrengthHint, AutoSize = true, Margin = new Padding(3, 7, 3, 0), ForeColor = SystemColors.GrayText, BackColor = Color.Transparent };
            var strength = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            strength.Controls.AddRange([strengthLabel, _strength, percent, hint]);

            var left = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            left.Controls.AddRange([_tickAll, _tickNone]);
            var right = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            right.Controls.AddRange([_close, _colorize, _stop]);
            var buttons = new Panel { Dock = DockStyle.Fill, Height = 36 };
            buttons.Controls.AddRange([left, right]);

            _summary.Margin = new Padding(0, 6, 0, 4);
            _status.Margin = new Padding(0, 4, 0, 6);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.Controls.Add(_summary, 0, 0);
            bottom.Controls.Add(_progress, 0, 1);
            TaskbarProgress.Watch(_progress, () => _phase is Phase.Scanning or Phase.Working);
            bottom.Controls.Add(_status, 0, 2);
            bottom.Controls.Add(buttons, 0, 3);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 10, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(intro, 0, 0);
            root.Controls.Add(scope, 0, 1);
            root.Controls.Add(strength, 0, 2);
            root.Controls.Add(_list, 0, 3);
            root.Controls.Add(bottom, 0, 4);
            Controls.Add(root);

            CancelButton = _close;
        }

        private void Wire()
        {
            _thisPhoto.CheckedChanged += async (_, _) =>
            {
                if (_thisPhoto.Checked)
                    await ChooseScopeAsync();
            };
            _listPhotos.CheckedChanged += async (_, _) =>
            {
                if (_listPhotos.Checked)
                    await ChooseScopeAsync();
            };
            _list.ItemChecked += (_, _) =>
            {
                if (!_updating)
                    UpdateSummary();
            };
            _list.DoubleClick += (_, _) =>
            {
                if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is Row row)
                    ShellOpen.Photo(row.Copy ?? row.Path);
            };
            _tickAll.Click += (_, _) => TickAll(true);
            _tickNone.Click += (_, _) => TickAll(false);
            _colorize.Click += async (_, _) => await ColorizeAsync();
            _stop.Click += (_, _) => _cts?.Cancel();
            FormClosing += (_, e) =>
            {
                if (_phase == Phase.Working)
                {
                    e.Cancel = true; // the photo that is being done is finished first
                    _cts?.Cancel();
                }
                else
                {
                    _cts?.Cancel(); // a look at the photos that is still going on
                }
            };
        }

        /// <summary>A photo of the list: where it is, how much colour it has, and the copy it has already or got now.</summary>
        private sealed class Row(string path)
        {
            public string Path { get; } = path;
            public string? Copy { get; set; }
        }

        // ── choosing the photos ────────────────────────────────────────────

        private async Task ChooseScopeAsync()
        {
            if (_phase is Phase.Working)
                return;

            _cts?.Cancel();
            var scope = new CancellationTokenSource();
            _cts = scope;

            if (_thisPhoto.Checked && _current is not null)
            {
                _updating = true;
                _list.Items.Clear();
                _updating = false;
                var item = AddRow(_current, amount: null, tick: true);
                SetPhase(Phase.Ready);
                UpdateSummary();
                SetStatus(string.Empty);
                await MeasureOneAsync(item, scope.Token);
                return;
            }

            await ScanListAsync(scope);
        }

        /// <summary>The one photo: how much colour it has is looked at, so that the list says whether it is black and white (it is colorized whatever it is).</summary>
        private async Task MeasureOneAsync(ListViewItem item, CancellationToken token)
        {
            var row = (Row)item.Tag!;
            ColorAmount? amount = null;
            try
            {
                amount = await Task.Run(() => ColorizeJob.Measure(row.Path), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // it can not be read: it is shown so, and the run will say why
            }

            if (token.IsCancellationRequested || IsDisposed || item.ListView is null)
                return;

            item.SubItems[ColourColumn].Text = ColourText(amount);
        }

        private async Task ScanListAsync(CancellationTokenSource scope)
        {
            if (_listScanned)
            {
                // Looked at already: the same list, with the ticks as they were left
                _updating = true;
                _list.Items.Clear();
                foreach (var item in _scannedItems)
                    _list.Items.Add(item);
                _updating = false;
                SetPhase(Phase.Ready);
                UpdateSummary();
                SetStatus(_scannedItems.Count == 0 ? string.Format(ColorizeStrings.StatusNoneFound, _photos.Count) : string.Format(ColorizeStrings.StatusFoundFormat, _scannedItems.Count, _photos.Count));
                return;
            }

            _updating = true;
            _list.Items.Clear();
            _scannedItems.Clear();
            _updating = false;
            SetPhase(Phase.Scanning);
            SetProgress(0);
            UpdateSummary();

            var token = scope.Token;
            var done = 0;
            try
            {
                var threads = Math.Max(2, Environment.ProcessorCount / 3);
                for (var start = 0; start < _photos.Count; start += ScanBatch)
                {
                    token.ThrowIfCancellationRequested();
                    var batch = _photos.Skip(start).Take(ScanBatch).ToArray();
                    var measured = await Task.Run(() => MeasureBatch(batch, threads, token), token);

                    foreach (var (path, amount) in measured)
                    {
                        if (amount is { IsBlackAndWhite: true } found)
                            _scannedItems.Add(AddRow(path, found, tick: ColorizedCopy.ExistingCopy(path) is null));
                    }

                    done += batch.Length;
                    SetProgress(done * 100 / Math.Max(1, _photos.Count));
                    SetStatus(string.Format(ColorizeStrings.StatusLookingFormat, done.ToString("N0"), _photos.Count.ToString("N0")));
                    UpdateSummary();
                }

                _listScanned = true;
                SetPhase(Phase.Ready);
                SetStatus(_scannedItems.Count == 0 ? string.Format(ColorizeStrings.StatusNoneFound, _photos.Count.ToString("N0")) : string.Format(ColorizeStrings.StatusFoundFormat, _scannedItems.Count.ToString("N0"), _photos.Count.ToString("N0")));
            }
            catch (OperationCanceledException)
            {
                // the window was closed (the radio buttons do not work while the photos are looked at)
            }
        }

        private readonly List<ListViewItem> _scannedItems = [];

        /// <summary>The columns of the list after the name of the photo: where it is, how much colour it has now, and what became of it.</summary>
        private const int ColourColumn = 2;
        private const int ResultColumn = 3;

        /// <summary>The colour of every photo of a batch, side by side on a few threads; a photo that can not be read is none.</summary>
        private static (string Path, ColorAmount? Amount)[] MeasureBatch(string[] batch, int threads, CancellationToken token)
        {
            var result = new (string, ColorAmount?)[batch.Length];
            Parallel.For(0, batch.Length, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = token }, i =>
            {
                try
                {
                    result[i] = (batch[i], ColorizeJob.Measure(batch[i]));
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    result[i] = (batch[i], null);
                }
            });
            return result;
        }

        private ListViewItem AddRow(string path, ColorAmount? amount, bool tick)
        {
            var copy = ColorizedCopy.ExistingCopy(path);
            var item = new ListViewItem(Path.GetFileName(path)) { Tag = new Row(path) { Copy = copy }, Checked = tick, ToolTipText = path };
            item.SubItems.Add(Path.GetDirectoryName(path) ?? string.Empty);
            item.SubItems.Add(ColourText(amount));
            item.SubItems.Add(copy is null ? string.Empty : string.Format(ColorizeStrings.ResultHasCopyFormat, Path.GetFileName(copy)));
            _updating = true;
            _list.Items.Add(item);
            _updating = false;
            return item;
        }

        private static string ColourText(ColorAmount? amount) => amount switch
        {
            null => string.Empty,
            { MeanChroma: < ColorAmount.GreyChroma } => ColorizeStrings.ColourGrey,
            { IsBlackAndWhite: true } => ColorizeStrings.ColourToned,
            _ => ColorizeStrings.ColourColour,
        };

        // ── colorizing ─────────────────────────────────────────────────────

        private async Task ColorizeAsync()
        {
            var items = _list.Items.Cast<ListViewItem>().Where(item => item.Checked).ToList();
            if (_phase != Phase.Ready || items.Count == 0)
                return;

            SetPhase(Phase.Working);
            SetProgress(0);
            SetStatus(ColorizeStrings.StatusPreparing);

            var run = new CancellationTokenSource();
            _cts = run;
            var saved = 0;
            var failed = 0;
            var stopped = false;
            var declined = false;

            try
            {
                if (!await _ensureModelAsync())
                {
                    declined = true;
                    foreach (var item in items)
                        item.SubItems[ResultColumn].Text = ColorizeStrings.ResultNotDone;
                    return;
                }

                foreach (var item in items)
                    item.SubItems[ResultColumn].Text = string.Empty;

                Colorizer colorizer;
                try
                {
                    colorizer = await Task.Run(() => _models.CreateColorizer(Math.Max(2, Environment.ProcessorCount / 2)));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The model can not be started (a C++ runtime that is too old, a damaged file): every photo says so
                    foreach (var item in items)
                        item.SubItems[ResultColumn].Text = string.Format(ColorizeStrings.ResultFailedFormat, ex.Message);
                    failed = items.Count;
                    return;
                }

                using (colorizer)
                {
                    var strength = (float)_strength.Value / 100f;
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (run.IsCancellationRequested)
                        {
                            stopped = true;
                            break;
                        }

                        var item = items[i];
                        var path = ((Row)item.Tag!).Path;
                        SetStatus(string.Format(ColorizeStrings.StatusWorkingFormat, (i + 1).ToString("N0"), items.Count.ToString("N0")));
                        item.SubItems[ResultColumn].Text = ColorizeStrings.ResultWorking;
                        item.EnsureVisible();

                        try
                        {
                            var copy = await Task.Run(() => ColorizeJob.Colorize(colorizer, path, strength));
                            ((Row)item.Tag!).Copy = copy;
                            item.SubItems[ResultColumn].Text = string.Format(ColorizeStrings.ResultSavedFormat, Path.GetFileName(copy));
                            saved++;
                            _saved++;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            item.SubItems[ResultColumn].Text = string.Format(ColorizeStrings.ResultFailedFormat, ex.Message);
                            failed++;
                        }

                        SetProgress((i + 1) * 100 / items.Count);
                    }
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    SetPhase(Phase.Ready);
                    SetStatus(declined ? string.Format(ToolModelStrings.StatusDeclinedFormat, ToolModels.Colorizer.Title)
                        : stopped ? ColorizeStrings.StatusStopped
                        : string.Format(ColorizeStrings.StatusFinishedFormat, saved.ToString("N0"), failed.ToString("N0")));
                    if (!stopped && !declined)
                        SetProgress(100);
                }
            }
        }

        // ── state ──────────────────────────────────────────────────────────

        private void TickAll(bool ticked)
        {
            _updating = true;
            foreach (ListViewItem item in _list.Items)
                item.Checked = ticked;
            _updating = false;
            UpdateSummary();
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            var busy = phase is Phase.Scanning or Phase.Working;
            _colorize.Enabled = phase == Phase.Ready && _list.CheckedItems.Count > 0;
            _colorize.Visible = phase != Phase.Working;
            _stop.Visible = phase == Phase.Working;
            _close.Enabled = phase != Phase.Working;
            _tickAll.Enabled = _tickNone.Enabled = !busy && _list.Items.Count > 0;
            _strength.Enabled = phase is not Phase.Working;
            _thisPhoto.Enabled = !busy && _current is not null;
            _listPhotos.Enabled = !busy;
            _progress.Visible = busy || _progress.Value > 0;
        }

        private void UpdateSummary()
        {
            _summary.Text = string.Format(ColorizeStrings.SummaryTickedFormat, _list.CheckedItems.Count.ToString("N0"), _list.Items.Count.ToString("N0"));
            _colorize.Enabled = _phase == Phase.Ready && _list.CheckedItems.Count > 0;
            _tickAll.Enabled = _tickNone.Enabled = _phase is not (Phase.Scanning or Phase.Working) && _list.Items.Count > 0;
        }

        private void SetStatus(string text) => _status.Text = text;

        private void SetProgress(int percent) => _progress.Value = Math.Clamp(percent, 0, 100);

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cts is { } source)
            {
                _cts = null; // a form can be disposed twice (closed, then disposed by whoever made it)
                try { source.Cancel(); } catch (ObjectDisposedException) { /* already done with */ }
                source.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
