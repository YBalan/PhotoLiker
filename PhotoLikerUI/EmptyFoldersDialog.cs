using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// Lists the folders of the photo folder that hold no photo (see <see cref="EmptyFolders"/>) and removes the ones that stay ticked.
    /// Nothing is deleted before the button is pressed.
    /// </summary>
    internal sealed class EmptyFoldersDialog : Form
    {
        private enum Phase { Looking, Ready, Working }

        /// <summary>
        /// A list with ticks. A double click on a row of such a list flips its tick; here a double click opens the folder, so the list takes
        /// the double click itself and tells which row it was on, and the tick stays as it was.
        /// </summary>
        private sealed class TickedList : ListView
        {
            private const int WmLeftButtonDoubleClick = 0x0203;

            public event Action<ListViewItem>? RowDoubleClicked;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmLeftButtonDoubleClick)
                {
                    var position = (int)(m.LParam.ToInt64() & 0xFFFFFFFF);
                    if (GetItemAt((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF)) is { } item)
                        RowDoubleClicked?.Invoke(item);
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private readonly string _folder;
        private readonly IReadOnlyCollection<string> _leave;
        private readonly IReadOnlyCollection<string> _extensions;
        private readonly PhotoIndex? _index;
        private readonly bool _canMovePhotos;
        private readonly bool _dark;

        private readonly Label _intro = new() { Text = EmptyFoldersStrings.Intro, Dock = DockStyle.Fill, Height = 92, Padding = new Padding(0, 0, 0, 6) };
        private readonly RadioButton _showFolders = new() { AutoSize = true, Checked = true, Margin = new Padding(3, 3, 14, 3) };
        private readonly RadioButton _showPhotos = new() { AutoSize = true, Margin = new Padding(3, 3, 14, 3) };
        private readonly TickedList _list = new();                 // the empty folders
        private readonly TickedList _photos = new() { Visible = false }; // the photos that can not be read
        private readonly ContextMenuStrip _menu = new();
        private readonly ToolStripMenuItem _openItem = new(EmptyFoldersStrings.OpenMenu);
        private readonly ContextMenuStrip _photoMenu = new();
        private readonly ToolStripMenuItem _openPhotoItem = new(EmptyFoldersStrings.OpenPhotoMenu);
        private readonly ToolStripMenuItem _showPhotoItem = new(EmptyFoldersStrings.ShowPhotoMenu);
        private readonly Label _summary = new() { AutoSize = true };
        private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 14, Minimum = 0, Maximum = 100 };
        private readonly Label _status = new() { AutoSize = true };
        private readonly Button _remove = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(130, 28) };
        private readonly Button _stop = new() { Text = EmptyFoldersStrings.StopButton, AutoSize = true, MinimumSize = new Size(80, 28), Visible = false };
        private readonly Button _close = new() { Text = EmptyFoldersStrings.CloseButton, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };
        private readonly Button _lookAgain = new() { Text = EmptyFoldersStrings.LookAgainButton, AutoSize = true, MinimumSize = new Size(90, 28) };
        private readonly Button _checkAll = new() { Text = EmptyFoldersStrings.CheckAllButton, AutoSize = true, MinimumSize = new Size(80, 28) };
        private readonly Button _uncheckAll = new() { Text = EmptyFoldersStrings.UncheckAllButton, AutoSize = true, MinimumSize = new Size(80, 28) };

        private bool _updating; // the ticks are being set by the code: not by a click
        private Phase _phase = Phase.Looking;
        private CancellationTokenSource? _cts;

        /// <param name="extensions">The extensions of photos: an empty file with one of them is a photo that can not be read.</param>
        /// <param name="index">The search index of the folder, if it has one: the photos it could not decode are unreadable photos too.</param>
        /// <param name="canMovePhotos">False while the folder is being indexed: photos must not move away under the indexer.</param>
        public EmptyFoldersDialog(string folder, IReadOnlyCollection<string> leave, IReadOnlyCollection<string> extensions, PhotoIndex? index, bool canMovePhotos, bool dark)
        {
            _folder = folder;
            _leave = leave;
            _extensions = extensions;
            _index = index;
            _canMovePhotos = canMovePhotos;
            _dark = dark;

            Text = EmptyFoldersStrings.Title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 560);
            MinimumSize = new Size(640, 420);

            BuildLayout();
            Wire();

            ThemeManager.Apply(this, dark);
            foreach (Control control in new Control[] { _intro, _summary, _status, _showFolders, _showPhotos })
                control.BackColor = Color.Transparent; // the theme paints a label like a control; here it sits on a panel
            _list.BackColor = _photos.BackColor = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;

            SetPhase(Phase.Looking);
            Shown += async (_, _) => await LookAsync();
        }

        /// <summary>How many folders were removed.</summary>
        public int Removed { get; private set; }

        /// <summary>How many photos that can not be read were moved away: the photo list of the main window is out of date then.</summary>
        public int PhotosMoved { get; private set; }

        private bool ShowingPhotos => _showPhotos.Checked;

        // ── building the dialog ────────────────────────────────────────────

        private void BuildLayout()
        {
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(EmptyFoldersStrings.ColumnFolder, 560);
            _list.Columns.Add(EmptyFoldersStrings.ColumnContains, 260);

            _photos.View = View.Details;
            _photos.CheckBoxes = true;
            _photos.FullRowSelect = true;
            _photos.HideSelection = false;
            _photos.Dock = DockStyle.Fill;
            _photos.Columns.Add(EmptyFoldersStrings.ColumnPhoto, 380);
            _photos.Columns.Add(EmptyFoldersStrings.ColumnSize, 80);
            _photos.Columns.Add(EmptyFoldersStrings.ColumnReason, 360);

            // Which of the two lists is shown is chosen like the filters of the other dialogs; both sit in one place
            var choice = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            choice.Controls.AddRange([_showFolders, _showPhotos]);
            var lists = new Panel { Dock = DockStyle.Fill };
            lists.Controls.AddRange([_list, _photos]);

            var left = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            left.Controls.AddRange([_lookAgain, _checkAll, _uncheckAll]);
            var right = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            right.Controls.AddRange([_close, _remove, _stop]);
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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12, 10, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_intro, 0, 0);
            root.Controls.Add(choice, 0, 1);
            root.Controls.Add(lists, 0, 2);
            root.Controls.Add(bottom, 0, 3);
            Controls.Add(root);

            CancelButton = _close;
        }

        private void Wire()
        {
            _list.ItemChecked += (_, e) => OnChecked(e.Item!);
            _list.RowDoubleClicked += item => OpenFolder(item);
            _list.ContextMenuStrip = _menu;
            _menu.Items.Add(_openItem);
            _menu.Opening += (_, e) =>
            {
                if (_list.SelectedItems.Count == 0)
                    e.Cancel = true;
                _menu.Renderer = _dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
                _menu.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
                _menu.ForeColor = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            };
            _openItem.Click += (_, _) =>
            {
                if (_list.SelectedItems.Count > 0)
                    OpenFolder(_list.SelectedItems[0]);
            };
            _showFolders.CheckedChanged += (_, _) => ShowList();
            _showPhotos.CheckedChanged += (_, _) => ShowList();
            _photos.ItemChecked += (_, _) =>
            {
                if (!_updating)
                    UpdateSummary();
            };
            _photos.RowDoubleClicked += item => OpenPhoto(item, show: false);
            _photos.ContextMenuStrip = _photoMenu;
            _photoMenu.Items.AddRange([_openPhotoItem, _showPhotoItem]);
            _photoMenu.Opening += (_, e) =>
            {
                if (_photos.SelectedItems.Count == 0)
                    e.Cancel = true;
                _photoMenu.Renderer = _dark ? new DarkToolStripRenderer() : new ToolStripProfessionalRenderer();
                _photoMenu.BackColor = _dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBack;
                _photoMenu.ForeColor = _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
            };
            _openPhotoItem.Click += (_, _) =>
            {
                if (_photos.SelectedItems.Count > 0)
                    OpenPhoto(_photos.SelectedItems[0], show: false);
            };
            _showPhotoItem.Click += (_, _) =>
            {
                if (_photos.SelectedItems.Count > 0)
                    OpenPhoto(_photos.SelectedItems[0], show: true);
            };
            _remove.Click += (_, _) =>
            {
                if (ShowingPhotos)
                    MovePhotosAsync();
                else
                    RemoveAsync();
            };
            _lookAgain.Click += async (_, _) => await LookAsync();
            _checkAll.Click += (_, _) => SetAll(true);
            _uncheckAll.Click += (_, _) => SetAll(false);
            _stop.Click += (_, _) => StopWorking();
        }

        // ── looking ────────────────────────────────────────────────────────

        private async Task LookAsync()
        {
            if (_phase == Phase.Working)
                return;

            SetPhase(Phase.Looking);
            var cts = _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = EmptyFoldersStrings.Looking;
            try
            {
                var progress = new Progress<int>(done =>
                {
                    if (!IsDisposed && _phase == Phase.Looking)
                        _status.Text = string.Format(EmptyFoldersStrings.LookingProgressFormat, done);
                });
                var (scan, unreadable) = await Task.Run(() =>
                {
                    var walk = EmptyFolders.Scan(_folder, _leave, _extensions, progress, cts.Token);
                    return (walk, Unreadable(walk.EmptyPhotos));
                }, cts.Token);
                if (IsDisposed)
                    return;

                Fill(scan.Folders);
                FillPhotos(unreadable);
                _status.Text = string.Empty;

                // With no folder to remove but photos that can not be read, those are what to look at
                if (_list.Items.Count == 0 && _photos.Items.Count > 0)
                    _showPhotos.Checked = true;
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed)
                    return;
                Fill([]);
                FillPhotos([]);
                _status.Text = EmptyFoldersStrings.LookingStopped;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (IsDisposed)
                    return;
                Fill([]);
                FillPhotos([]);
                _status.Text = string.Format(EmptyFoldersStrings.LookingError, ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (!IsDisposed)
                SetPhase(Phase.Ready);
        }

        private void Fill(IReadOnlyList<EmptyFolder> found)
        {
            _updating = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var folder in found.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
                    _list.Items.Add(new ListViewItem([Path.GetRelativePath(_folder, folder.Path), Contains(folder)]) { Tag = folder, Checked = true });
                _list.EndUpdate();
            }
            finally
            {
                _updating = false;
            }
        }

        /// <summary>
        /// The photos that can not be read: the ones the search index could not decode (its memory, so nothing is read here) and the empty files
        /// the walk of the folders came across. A photo that is gone, or lies in a folder the app keeps (Deleted, Liked), is left out.
        /// </summary>
        private List<UnreadablePhoto> Unreadable(IReadOnlyList<string> emptyPhotos)
        {
            var found = new Dictionary<string, UnreadablePhoto>(StringComparer.OrdinalIgnoreCase);
            foreach (var failed in _index?.FailedPhotos() ?? [])
            {
                if (!InKeptFolder(failed.Path) && File.Exists(failed.Path))
                    found[failed.Path] = failed;
            }
            foreach (var path in emptyPhotos)
                found[path] = new UnreadablePhoto(path, 0, UnreadablePhotos.EmptyFileReason); // says more than the decoder's complaint about nothing

            return [.. found.Values.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)];
        }

        private bool InKeptFolder(string path) =>
            _leave.Any(kept => !string.IsNullOrWhiteSpace(kept)
                && path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(kept)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        /// <summary>Photos are listed unticked: unlike the thumbnail files of an empty folder, a photo that can not be read may still be worth something to another program.</summary>
        private void FillPhotos(IReadOnlyList<UnreadablePhoto> found)
        {
            _updating = true;
            try
            {
                _photos.BeginUpdate();
                _photos.Items.Clear();
                foreach (var photo in found)
                {
                    _photos.Items.Add(new ListViewItem(
                    [
                        Path.GetRelativePath(_folder, photo.Path),
                        string.Format(EmptyFoldersStrings.SizeFormat, Math.Max(photo.Length / 1024, photo.Length > 0 ? 1 : 0)),
                        photo.Reason,
                    ]) { Tag = photo, Checked = false });
                }
                _photos.EndUpdate();
            }
            finally
            {
                _updating = false;
            }
        }

        private void ShowList()
        {
            _list.Visible = !ShowingPhotos;
            _photos.Visible = ShowingPhotos;
            UpdateSummary();
        }

        private static string Contains(EmptyFolder folder)
        {
            var parts = new List<string>(folder.Junk);
            if (folder.Subfolders > 0)
                parts.Add(string.Format(EmptyFoldersStrings.ContainsSubfoldersFormat, EmptyFoldersStrings.Count(folder.Subfolders, EmptyFoldersStrings.EmptyFolderOne, EmptyFoldersStrings.EmptyFolderMany)));
            return parts.Count == 0 ? EmptyFoldersStrings.ContainsEmpty : string.Join(EmptyFoldersStrings.ContainsSeparator, parts);
        }

        // ── opening a folder ───────────────────────────────────────────────

        /// <summary>The folder of the row in File Explorer: to see what is in it before it is removed.</summary>
        private void OpenFolder(ListViewItem item)
        {
            var path = ((EmptyFolder)item.Tag!).Path;
            if (ShellOpen.Folder(path) is { } error)
                _status.Text = ShellOpen.Report(path, error);
        }

        /// <summary>The photo of the row, in the program Windows has for it, or its folder in File Explorer: to look at it before it is moved.</summary>
        private void OpenPhoto(ListViewItem item, bool show)
        {
            var path = ((UnreadablePhoto)item.Tag!).Path;
            var error = show ? ShellOpen.ShowInFolder(path) : ShellOpen.Photo(path);
            if (error is not null)
                _status.Text = ShellOpen.Report(path, error);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _menu.Dispose();
                _photoMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ── the ticks ──────────────────────────────────────────────────────

        private static bool IsBelow(string path, string parent) =>
            path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>A folder can not go without the folders in it, nor stay while the folders above it go: the ticks follow that.</summary>
        private void OnChecked(ListViewItem item)
        {
            if (_updating)
                return;

            _updating = true;
            try
            {
                var path = ((EmptyFolder)item.Tag!).Path;
                foreach (ListViewItem other in _list.Items)
                {
                    var otherPath = ((EmptyFolder)other.Tag!).Path;
                    if (item.Checked && IsBelow(otherPath, path))
                        other.Checked = true;
                    else if (!item.Checked && IsBelow(path, otherPath))
                        other.Checked = false;
                }
            }
            finally
            {
                _updating = false;
            }
            UpdateSummary();
        }

        private void SetAll(bool on)
        {
            _updating = true;
            try
            {
                foreach (ListViewItem item in (ShowingPhotos ? _photos : _list).Items)
                    item.Checked = on;
            }
            finally
            {
                _updating = false;
            }
            UpdateSummary();
        }

        // An item that is still being added when the list makes its window (a list that was not shown yet) is not there to ask yet
        private List<ListViewItem> Ticked() => [.. _list.Items.Cast<ListViewItem?>().Where(i => i is { Checked: true }).Select(i => i!)];

        private List<ListViewItem> TickedPhotos() => [.. _photos.Items.Cast<ListViewItem?>().Where(i => i is { Checked: true }).Select(i => i!)];

        private void UpdateSummary()
        {
            _showFolders.Text = string.Format(EmptyFoldersStrings.FilterFolders, _list.Items.Count);
            _showPhotos.Text = string.Format(EmptyFoldersStrings.FilterPhotos, _photos.Items.Count);

            if (ShowingPhotos)
            {
                UpdatePhotosSummary();
                return;
            }

            var total = _list.Items.Count;
            var junk = _list.Items.Cast<ListViewItem?>().Sum(i => i?.Tag is EmptyFolder folder ? folder.Junk.Count : 0);
            var ticked = Ticked().Count;

            _summary.Text = total == 0
                ? (_phase == Phase.Looking ? string.Empty : EmptyFoldersStrings.FoundNone)
                : string.Format(
                    EmptyFoldersStrings.FoundFormat, EmptyFoldersStrings.Count(total, EmptyFoldersStrings.EmptyFolderOne, EmptyFoldersStrings.EmptyFolderMany),
                    junk > 0 ? string.Format(EmptyFoldersStrings.FoundJunkPart, EmptyFoldersStrings.Count(junk, EmptyFoldersStrings.ThumbnailFileOne, EmptyFoldersStrings.ThumbnailFileMany)) : string.Empty);
            _remove.Text = ticked > 0
                ? string.Format(EmptyFoldersStrings.RemoveButtonFormat, EmptyFoldersStrings.Count(ticked, EmptyFoldersStrings.FolderOne, EmptyFoldersStrings.FolderMany))
                : EmptyFoldersStrings.RemoveButtonNothing;
            _remove.Enabled = _phase == Phase.Ready && ticked > 0;
            _checkAll.Enabled = _uncheckAll.Enabled = _phase == Phase.Ready && total > 0;
        }

        private void UpdatePhotosSummary()
        {
            var total = _photos.Items.Count;
            var ticked = TickedPhotos().Count;

            _summary.Text = total == 0
                ? (_phase == Phase.Looking ? string.Empty : EmptyFoldersStrings.PhotosFoundNone + (_index is null ? EmptyFoldersStrings.PhotosNoIndexNote : string.Empty))
                : string.Format(
                    EmptyFoldersStrings.PhotosFoundFormat, EmptyFoldersStrings.Count(total, EmptyFoldersStrings.PhotoOne, EmptyFoldersStrings.PhotoMany),
                    _canMovePhotos ? string.Empty : EmptyFoldersStrings.PhotosIndexingNote);
            _remove.Text = ticked > 0
                ? string.Format(EmptyFoldersStrings.MovePhotosButtonFormat, EmptyFoldersStrings.Count(ticked, EmptyFoldersStrings.PhotoOne, EmptyFoldersStrings.PhotoMany))
                : EmptyFoldersStrings.MovePhotosNothing;
            _remove.Enabled = _phase == Phase.Ready && ticked > 0 && _canMovePhotos;
            _checkAll.Enabled = _uncheckAll.Enabled = _phase == Phase.Ready && total > 0;
        }

        // ── moving photos that can not be read ─────────────────────────────

        private async void MovePhotosAsync()
        {
            var ticked = TickedPhotos();
            if (ticked.Count == 0 || _phase != Phase.Ready || !_canMovePhotos)
                return;

            var paths = ticked.Select(i => ((UnreadablePhoto)i.Tag!).Path).ToList();
            SetPhase(Phase.Working);
            var cts = _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Maximum = paths.Count;
            _progress.Value = 0;
            _status.Text = string.Format(EmptyFoldersStrings.MovingPhotosFormat, 0, paths.Count);
            var progress = new Progress<int>(done =>
            {
                if (IsDisposed || _phase != Phase.Working)
                    return;
                _progress.Value = Math.Clamp(done, 0, _progress.Maximum);
                _status.Text = string.Format(EmptyFoldersStrings.MovingPhotosFormat, done, paths.Count);
            });

            try
            {
                var result = await Task.Run(() => UnreadablePhotos.MoveAway(_folder, paths, progress, cts.Token));
                var indexNote = await ForgetAsync(result.Moved);
                if (IsDisposed)
                    return;

                PhotosMoved += result.Moved.Count;
                ShowMoved(result, indexNote);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (!IsDisposed)
                SetPhase(Phase.Ready);
        }

        /// <summary>The moved photos are no photos of the folder any more: the search index forgets them, so that its counts (and its list of unreadable photos) are right.</summary>
        private async Task<string> ForgetAsync(IReadOnlyList<string> moved)
        {
            if (_index is null || moved.Count == 0)
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
                return string.Format(EmptyFoldersStrings.IndexNotUpdatedFormat, ex.Message);
            }
        }

        /// <summary>Takes the photos that are gone out of the list, and marks the ones that could not be moved.</summary>
        private void ShowMoved(UnreadableMoveResult result, string indexNote)
        {
            var gone = result.Moved.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var failed = result.Failures.ToDictionary(f => f.Path, f => f.Message, StringComparer.OrdinalIgnoreCase);

            _updating = true;
            try
            {
                _photos.BeginUpdate();
                foreach (var item in _photos.Items.Cast<ListViewItem>().ToList())
                {
                    var path = ((UnreadablePhoto)item.Tag!).Path;
                    if (gone.Contains(path))
                    {
                        _photos.Items.Remove(item);
                    }
                    else if (failed.TryGetValue(path, out var message))
                    {
                        item.SubItems[2].Text = string.Format(EmptyFoldersStrings.FailedFormat, message);
                        item.ForeColor = SystemColors.GrayText;
                        item.Checked = false;
                    }
                }
                _photos.EndUpdate();
            }
            finally
            {
                _updating = false;
            }

            var failures = result.Failures.Count > 0
                ? string.Format(EmptyFoldersStrings.FailedPart, EmptyFoldersStrings.Count(result.Failures.Count, EmptyFoldersStrings.PhotoOne, EmptyFoldersStrings.PhotoMany))
                : string.Empty;
            _status.Text = string.Format(
                result.Cancelled ? EmptyFoldersStrings.MovedPhotosStoppedFormat : EmptyFoldersStrings.MovedPhotosFormat,
                EmptyFoldersStrings.Count(result.Moved.Count, EmptyFoldersStrings.PhotoOne, EmptyFoldersStrings.PhotoMany), failures) + indexNote;
        }

        // ── removing ───────────────────────────────────────────────────────

        private async void RemoveAsync()
        {
            var ticked = Ticked();
            if (ticked.Count == 0 || _phase != Phase.Ready)
                return;

            // The folders in a folder go before it
            var order = ticked
                .Select(i => ((EmptyFolder)i.Tag!).Path)
                .OrderByDescending(p => p.Count(c => c == Path.DirectorySeparatorChar))
                .ToList();

            SetPhase(Phase.Working);
            var cts = _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Maximum = order.Count;
            _progress.Value = 0;
            _status.Text = string.Format(EmptyFoldersStrings.RemovingFormat, 0, order.Count);
            var progress = new Progress<int>(done =>
            {
                if (IsDisposed || _phase != Phase.Working)
                    return;
                _progress.Value = Math.Clamp(done, 0, _progress.Maximum);
                _status.Text = string.Format(EmptyFoldersStrings.RemovingFormat, done, order.Count);
            });

            try
            {
                var result = await Task.Run(() => EmptyFolders.Remove(order, progress, cts.Token));
                if (IsDisposed)
                    return;

                Removed += result.Removed.Count;
                Show(result);
            }
            finally
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
                cts.Dispose();
            }

            if (!IsDisposed)
                SetPhase(Phase.Ready);
        }

        /// <summary>Takes the folders that are gone out of the list, and marks the ones that could not be removed.</summary>
        private void Show(EmptyFoldersResult result)
        {
            var gone = result.Removed.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var failed = result.Failures.ToDictionary(f => f.Path, f => f.Message, StringComparer.OrdinalIgnoreCase);

            _updating = true;
            try
            {
                _list.BeginUpdate();
                foreach (var item in _list.Items.Cast<ListViewItem>().ToList())
                {
                    var path = ((EmptyFolder)item.Tag!).Path;
                    if (gone.Contains(path))
                    {
                        _list.Items.Remove(item);
                    }
                    else if (failed.TryGetValue(path, out var message))
                    {
                        item.SubItems[1].Text = string.Format(EmptyFoldersStrings.FailedFormat, message);
                        item.ForeColor = SystemColors.GrayText;
                        item.Checked = false;
                    }
                }
                _list.EndUpdate();
            }
            finally
            {
                _updating = false;
            }

            var failures = result.Failures.Count > 0
                ? string.Format(EmptyFoldersStrings.FailedPart, EmptyFoldersStrings.Count(result.Failures.Count, EmptyFoldersStrings.FolderOne, EmptyFoldersStrings.FolderMany))
                : string.Empty;
            _status.Text = string.Format(result.Cancelled ? EmptyFoldersStrings.RemovedStoppedFormat : EmptyFoldersStrings.RemovedFormat, 
                EmptyFoldersStrings.Count(result.Removed.Count, EmptyFoldersStrings.FolderOne, EmptyFoldersStrings.FolderMany),
                EmptyFoldersStrings.Count(result.JunkFiles, EmptyFoldersStrings.ThumbnailFileOne, EmptyFoldersStrings.ThumbnailFileMany), failures);
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

            _lookAgain.Enabled = ready;
            _stop.Visible = !ready;
            _stop.Enabled = true;
            _close.Enabled = phase != Phase.Working;
            _list.Enabled = _photos.Enabled = ready || phase == Phase.Looking;
            _showFolders.Enabled = _showPhotos.Enabled = phase != Phase.Working;
            _progress.Visible = !ready;
            UpdateSummary();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_phase == Phase.Working)
            {
                // Removing is stopped between two folders, not abandoned in the middle of one
                e.Cancel = true;
                StopWorking();
                return;
            }

            _cts?.Cancel();
            base.OnFormClosing(e);
        }
    }
}
