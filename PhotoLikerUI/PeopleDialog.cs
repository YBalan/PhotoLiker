using System.Globalization;
using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>
    /// The people the user has named, and the groups they are sorted into. Select one person or many (Ctrl+A, Shift+click) and tick the groups
    /// they belong to; a box filled halfway means only some of the selected people are in that group. Every change is saved at once, there is
    /// nothing to confirm and nothing to cancel: the window only shows and changes the groups, never the faces or the photos.
    /// </summary>
    internal sealed class PeopleDialog : Form
    {
        private enum ShowKind { All, NoGroup, Group }

        /// <summary>A line of the "Show" box: everybody, the people with no group, or one group.</summary>
        private sealed record ShowChoice(ShowKind Kind, string Group, string Text)
        {
            public override string ToString() => Text;
        }

        /// <summary>A line of the list of groups: the name and how many of the named people are in it.</summary>
        private sealed record GroupItem(string Name, int Count)
        {
            public override string ToString() => $"{Name} ({Count:N0})";
        }

        private sealed record Row(string Name, int Faces);

        private sealed class RowSorter(int column, bool descending) : System.Collections.IComparer
        {
            public int Compare(object? x, object? y)
            {
                var a = (ListViewItem)x!;
                var b = (ListViewItem)y!;
                var order = column == 1
                    ? ((Row)a.Tag!).Faces.CompareTo(((Row)b.Tag!).Faces)
                    : string.Compare(a.SubItems[column].Text, b.SubItems[column].Text, StringComparison.CurrentCultureIgnoreCase);
                if (order == 0)
                    order = string.Compare(((Row)a.Tag!).Name, ((Row)b.Tag!).Name, StringComparison.CurrentCultureIgnoreCase);
                return descending ? -order : order;
            }
        }

        private readonly PeopleLibrary _people;
        private readonly bool _dark;
        private string[] _names = [];                                                   // the people, in alphabetical order (they change when somebody is renamed)
        private Dictionary<string, int> _faces = new(StringComparer.OrdinalIgnoreCase); // how many faces of each are named

        private readonly Label _intro = new() { Text = PeopleStrings.Intro, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6) };
        private readonly Label _showLabel = new() { Text = PeopleStrings.ShowLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
        private readonly ComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(0, 3, 8, 3) };
        private readonly Button _rename = new() { Text = PeopleStrings.RenameGroupButton, AutoSize = true, MinimumSize = new Size(90, 26), Margin = new Padding(0, 2, 6, 2) };
        private readonly Button _delete = new() { Text = PeopleStrings.DeleteGroupButton, AutoSize = true, MinimumSize = new Size(80, 26), Margin = new Padding(0, 2, 0, 2) };
        private readonly Label _findLabel = new() { Text = PeopleStrings.FindLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
        private readonly TextBox _find = new() { PlaceholderText = PeopleStrings.FindPlaceholder, Width = 200, Margin = new Padding(0, 3, 0, 3) };
        private readonly ListView _list = new();
        private readonly Label _summary = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) };
        private readonly Button _renamePerson = new() { Text = PeopleStrings.RenamePersonButton, AutoSize = true, MinimumSize = new Size(130, 26), Margin = new Padding(8, 4, 0, 0), Enabled = false };
        private readonly Label _groupsHeading = new() { Text = PeopleStrings.GroupsHeading, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        private readonly CheckedListBox _groups = new() { CheckOnClick = true, IntegralHeight = false, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        private readonly Label _groupsHint = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        private readonly Button _newGroup = new() { Text = PeopleStrings.NewGroupButton, AutoSize = true, MinimumSize = new Size(110, 28) };
        private readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        private readonly Button _export = new() { Text = PeopleStrings.ExportButton, AutoSize = true, MinimumSize = new Size(100, 28) };
        private readonly Button _import = new() { Text = PeopleStrings.ImportButton, AutoSize = true, MinimumSize = new Size(100, 28) };
        private readonly Button _close = new() { Text = PeopleStrings.CloseButton, AutoSize = true, MinimumSize = new Size(80, 28), DialogResult = DialogResult.Cancel };
        private readonly ToolTip _tips = new();

        private bool _updating;           // the code is filling the lists: not a click
        private int _sortColumn;
        private bool _sortDescending;

        public PeopleDialog(PeopleLibrary people, bool dark)
        {
            _people = people;
            _dark = dark;
            _people.Groups.Reload(); // another copy of the app may have changed them
            LoadPeople();

            Text = PeopleStrings.Title;
            StartPosition = FormStartPosition.CenterParent;
            Icon = DialogIcons.People;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 580);
            MinimumSize = new Size(720, 440);

            BuildLayout();
            Wire();

            ThemeManager.Apply(this, dark);
            foreach (var label in new[] { _intro, _showLabel, _findLabel, _summary, _groupsHeading, _groupsHint, _status })
                label.BackColor = Color.Transparent; // the theme paints a label like a control; here it sits on a panel
            var field = dark ? ThemeManager.DarkBackAlt : ThemeManager.LightBackAlt;
            _list.BackColor = _groups.BackColor = _find.BackColor = _show.BackColor = field;

            FillShow(null);
            Fill();
            Shown += (_, _) => FitColumns();
        }

        // ── building the window ────────────────────────────────────────────

        private void BuildLayout()
        {
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add(PeopleStrings.ColumnPerson, 220);
            _list.Columns.Add(PeopleStrings.ColumnFaces, 60, HorizontalAlignment.Right);
            _list.Columns.Add(PeopleStrings.ColumnGroups, 240);

            var filter = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 2, Margin = new Padding(0, 0, 0, 6) };
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var groupButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            groupButtons.Controls.AddRange([_rename, _delete]);
            filter.Controls.Add(_showLabel, 0, 0);
            filter.Controls.Add(_show, 1, 0);
            filter.Controls.Add(groupButtons, 2, 0);
            filter.Controls.Add(_findLabel, 0, 1);
            filter.Controls.Add(_find, 1, 1);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 12, 0) };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(filter, 0, 0);
            left.Controls.Add(_list, 0, 1);
            var below = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            below.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            below.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            below.Controls.Add(_summary, 0, 0);
            below.Controls.Add(_renamePerson, 1, 0);
            left.Controls.Add(below, 0, 2);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(_groupsHeading, 0, 0);
            right.Controls.Add(_groups, 0, 1);
            right.Controls.Add(_groupsHint, 0, 2);
            right.Controls.Add(_newGroup, 0, 3);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(left, 0, 0);
            content.Controls.Add(right, 1, 0);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.Controls.Add(_status, 0, 0);
            bottom.Controls.Add(_export, 1, 0);
            bottom.Controls.Add(_import, 2, 0);
            bottom.Controls.Add(_close, 3, 0);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 10, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_intro, 0, 0);
            root.Controls.Add(content, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);

            CancelButton = _close;
            _tips.SetToolTip(_groups, PeopleStrings.GroupsTooltip);
            _tips.SetToolTip(_renamePerson, PeopleStrings.RenamePersonTooltip);
            _tips.SetToolTip(_newGroup, PeopleStrings.NewGroupTooltip);
            _tips.SetToolTip(_export, PeopleStrings.ExportTooltip);
            _tips.SetToolTip(_import, PeopleStrings.ImportTooltip);
        }

        private void Wire()
        {
            _list.SelectedIndexChanged += (_, _) =>
            {
                if (!_updating)
                    ShowSelection();
            };
            _list.ColumnClick += (_, e) =>
            {
                _sortDescending = e.Column == _sortColumn && !_sortDescending;
                _sortColumn = e.Column;
                _list.ListViewItemSorter = new RowSorter(_sortColumn, _sortDescending);
            };
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.F2)
                {
                    e.SuppressKeyPress = true;
                    RenamePerson();
                }
                if (e.Control && e.KeyCode == Keys.A)
                {
                    e.SuppressKeyPress = true;
                    _updating = true;
                    foreach (ListViewItem item in _list.Items)
                        item.Selected = true;
                    _updating = false;
                    ShowSelection();
                }
            };
            _list.Resize += (_, _) => FitColumns();
            _show.SelectedIndexChanged += (_, _) =>
            {
                if (_updating)
                    return;
                Fill();
                UpdateGroupButtons();
            };
            _find.TextChanged += (_, _) => Fill();
            _groups.ItemCheck += Groups_ItemCheck;
            _newGroup.Click += (_, _) => NewGroup();
            _renamePerson.Click += (_, _) => RenamePerson();
            _rename.Click += (_, _) => RenameGroup();
            _delete.Click += (_, _) => DeleteGroup();
            _export.Click += (_, _) => ExportPeople();
            _import.Click += (_, _) => ImportPeople();
        }

        /// <summary>The last column takes what the others leave.</summary>
        private void FitColumns()
        {
            var rest = _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[1].Width;
            if (rest > 60)
                _list.Columns[2].Width = rest;
        }

        // ── what is shown ──────────────────────────────────────────────────

        private ShowChoice CurrentShow => (ShowChoice)_show.SelectedItem!;

        /// <summary>The "Show" box: everybody, the people with no group, and a line for each group; keeps the group that was chosen if it is still there.</summary>
        private void FillShow(string? choose)
        {
            var keep = choose ?? (_show.SelectedItem is ShowChoice { Kind: ShowKind.Group } shown ? shown.Group : null);
            _updating = true;
            try
            {
                _show.Items.Clear();
                _show.Items.Add(new ShowChoice(ShowKind.All, string.Empty, PeopleStrings.ShowAll));
                _show.Items.Add(new ShowChoice(ShowKind.NoGroup, string.Empty, PeopleStrings.ShowNoGroup));
                foreach (var group in _people.Groups.Names)
                    _show.Items.Add(new ShowChoice(ShowKind.Group, group, group));

                var index = keep is null ? 0 : _show.Items.Cast<ShowChoice>().ToList().FindIndex(c => c.Kind == ShowKind.Group && string.Equals(c.Group, keep, StringComparison.OrdinalIgnoreCase));
                _show.SelectedIndex = Math.Max(0, index);
            }
            finally
            {
                _updating = false;
            }
            UpdateGroupButtons();
        }

        private void UpdateGroupButtons() => _rename.Enabled = _delete.Enabled = CurrentShow.Kind == ShowKind.Group;

        private List<string> SelectedNames() => [.. _list.SelectedItems.Cast<ListViewItem>().Select(item => ((Row)item.Tag!).Name)];

        /// <summary>The list of people for the box and the text, with the same people still selected.</summary>
        private void Fill(IEnumerable<string>? select = null)
        {
            var selected = (select ?? SelectedNames()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var choice = CurrentShow;
            var text = _find.Text.Trim();
            var compare = CultureInfo.CurrentCulture.CompareInfo;

            var items = new List<ListViewItem>();
            foreach (var name in _names)
            {
                var groups = _people.Groups.GroupsOf(name);
                if (choice.Kind == ShowKind.NoGroup && groups.Count > 0)
                    continue;
                if (choice.Kind == ShowKind.Group && !groups.Contains(choice.Group, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (text.Length > 0 && compare.IndexOf(name, text, CompareOptions.IgnoreCase) < 0)
                    continue;

                items.Add(new ListViewItem([name, _faces[name].ToString("N0"), string.Join(PeopleStrings.GroupSeparator, groups)]) { Tag = new Row(name, _faces[name]) });
            }

            _updating = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                _list.Items.AddRange([.. items]);
                _list.ListViewItemSorter = new RowSorter(_sortColumn, _sortDescending);
                foreach (ListViewItem item in _list.Items)
                    item.Selected = selected.Contains(((Row)item.Tag!).Name);
            }
            finally
            {
                _list.EndUpdate();
                _updating = false;
            }
            ShowSelection();
        }

        /// <summary>The ticks of the groups for the people that are selected, and the line under the list.</summary>
        private void ShowSelection()
        {
            var selected = SelectedNames();
            var groups = _people.Groups.Names;
            var known = _names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _renamePerson.Enabled = selected.Count == 1;

            _updating = true;
            _groups.BeginUpdate();
            try
            {
                _groups.Items.Clear();
                foreach (var group in groups)
                {
                    var inGroup = selected.Count(name => _people.Groups.IsMember(group, name));
                    var state = inGroup == 0 ? CheckState.Unchecked : inGroup == selected.Count ? CheckState.Checked : CheckState.Indeterminate;
                    _groups.Items.Add(new GroupItem(group, _people.Groups.MembersOf(group).Count(known.Contains)), state);
                }
            }
            finally
            {
                _groups.EndUpdate();
                _updating = false;
            }

            _groupsHint.Text = groups.Count == 0 ? PeopleStrings.GroupsHintNoGroups
                : selected.Count == 0 ? PeopleStrings.GroupsHintNoSelection
                : string.Empty;
            _summary.Text = _names.Length == 0
                ? PeopleStrings.NoPeopleYet
                : string.Format(PeopleStrings.SummaryFormat, _list.Items.Count, _names.Length, selected.Count,
                    _names.Count(name => _people.Groups.GroupsOf(name).Count == 0));
        }

        // ── renaming a person ──────────────────────────────────────────────

        private void LoadPeople()
        {
            _names = [.. _people.Names];
            _faces = _names.ToDictionary(name => name, name => _people.ExamplesOf(name).Count, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The one person who is selected gets another name (everywhere: other folders, groups). The list shows the new name, still selected.</summary>
        private void RenamePerson()
        {
            if (SelectedNames() is not [var person])
                return;
            if (PersonRenamePrompt.Ask(this, _people, person, _dark) is not { } renamed)
                return;

            _status.Text = string.Empty;
            LoadPeople();
            Fill([renamed]);
            if (_list.SelectedItems.Count > 0)
                _list.SelectedItems[0].EnsureVisible();
            if (_people.SharedError is { } shared)
                _status.Text = string.Format(SearchStrings.StatusSharedPeopleError, shared);
            else
                ReportFolderCopy();
        }

        // ── changing the groups ────────────────────────────────────────────

        /// <summary>A tick: the selected people are all put into the group, or all taken out (a box that is filled halfway becomes ticked).</summary>
        private void Groups_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (_updating)
                return;

            var selected = SelectedNames();
            if (selected.Count == 0)
            {
                e.NewValue = e.CurrentValue; // nobody to put into it
                return;
            }

            var member = e.CurrentValue != CheckState.Checked;
            e.NewValue = member ? CheckState.Checked : CheckState.Unchecked;
            var group = ((GroupItem)_groups.Items[e.Index]).Name;

            _status.Text = string.Empty;
            if (Try(() => _people.SetGroupMembers(group, selected, member)) is { } problem)
                _status.Text = problem;
            else
                ReportFolderCopy();

            BeginInvoke(() => Fill()); // the lists are written again once this click is over
        }

        private void NewGroup()
        {
            var selected = SelectedNames();
            using var dialog = new TextPromptDialog(PeopleStrings.NewGroupTitle, PeopleStrings.NewGroupPrompt, string.Empty, name =>
                Try(() =>
                {
                    var made = _people.CreateGroup(name);
                    if (selected.Count > 0)
                        _people.SetGroupMembers(made, selected, member: true); // the people who are selected go into the new group
                }));
            ThemeManager.Apply(dialog, _dark);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            FillShow(null);
            Fill();
            ReportFolderCopy();
        }

        private void RenameGroup()
        {
            if (CurrentShow is not { Kind: ShowKind.Group } shown)
                return;

            using var dialog = new TextPromptDialog(PeopleStrings.RenameGroupTitle, string.Format(PeopleStrings.RenameGroupPromptFormat, shown.Group), shown.Group,
                name => Try(() => _people.RenameGroup(shown.Group, name)));
            ThemeManager.Apply(dialog, _dark);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            FillShow(_people.Groups.TryFind(dialog.Value, out var renamed) ? renamed : null);
            Fill();
            ReportFolderCopy();
        }

        private void DeleteGroup()
        {
            if (CurrentShow is not { Kind: ShowKind.Group } shown)
                return;

            var answer = MessageBox.Show(this, string.Format(PeopleStrings.DeleteGroupQuestionFormat, shown.Group), PeopleStrings.DeleteGroupTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;

            _status.Text = string.Empty;
            if (Try(() => _people.Groups.Delete(shown.Group)) is { } problem)
                _status.Text = problem;
            else
                ReportFolderCopy();

            FillShow(null); // the group is gone: everybody is shown
            Fill();
        }

        // ── export and import ──────────────────────────────────────────────

        /// <summary>All the people (with the faces named) and all the groups go into one file, to take to another computer or to keep.</summary>
        private void ExportPeople()
        {
            _status.Text = string.Empty;
            if (_names.Length == 0 && _people.Groups.Count == 0)
            {
                _status.Text = PeopleStrings.NothingToExport;
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = PeopleStrings.ExportTitle,
                Filter = PeopleStrings.PeopleFileFilter,
                DefaultExt = PeopleLibrary.PeopleFileExtension,
                AddExtension = true,
                OverwritePrompt = true,
                FileName = string.Format(PeopleStrings.ExportDefaultNameFormat, DateTime.Today) + "." + PeopleLibrary.PeopleFileExtension,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            UseWaitCursor = true;
            try
            {
                var info = _people.Export(dialog.FileName);
                _status.Text = string.Format(PeopleStrings.ExportedFormat, Path.GetFileName(dialog.FileName), info.People, info.Faces, info.Groups);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Problem(PeopleStrings.ExportTitle, string.Format(PeopleStrings.ExportErrorFormat, ex.Message));
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        /// <summary>The people and groups of such a file are added to the ones here (after asking what the file holds); nothing here is replaced or removed.</summary>
        private void ImportPeople()
        {
            using var dialog = new OpenFileDialog { Title = PeopleStrings.ImportTitle, Filter = PeopleStrings.PeopleFileFilter, CheckFileExists = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            _status.Text = string.Empty;
            var tried = false;
            try
            {
                var info = PeopleLibrary.Inspect(dialog.FileName);
                var notes = info is { Folder: { } folder, ExportedAt: { } at } ? string.Format(PeopleStrings.ImportMadeFromFormat, folder, at.ToLocalTime()) : string.Empty;
                if (!info.FacesUsable)
                    notes += PeopleStrings.ImportOtherModelsNote;

                var question = string.Format(PeopleStrings.ImportQuestionFormat, info.People, info.Faces, info.Folders, info.Groups, notes);
                if (MessageBox.Show(this, question, PeopleStrings.ImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                tried = true;
                UseWaitCursor = true;
                var result = _people.Import(dialog.FileName);
                UseWaitCursor = false;
                ShowAgain();

                MessageBox.Show(this, ImportReport(result), PeopleStrings.ImportTitle, MessageBoxButtons.OK,
                    result.FacesUnusable > 0 || result.Refused.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                UseWaitCursor = false;
                if (tried)
                    ShowAgain(); // part of it may have been added before the failure
                Problem(PeopleStrings.ImportTitle, string.Format(PeopleStrings.ImportErrorFormat, ex.Message));
            }
            finally
            {
                UseWaitCursor = false;
            }

            if (_people.SharedError is { } shared)
                _status.Text = string.Format(SearchStrings.StatusSharedPeopleError, shared);
            else
                ReportFolderCopy();
        }

        /// <summary>Reads the people and the groups again, with the same people selected and the same group shown.</summary>
        private void ShowAgain()
        {
            LoadPeople();
            FillShow(null);
            Fill();
        }

        private static string ImportReport(PeopleImportResult result)
        {
            var nothingNew = result is { NewPeople: 0, FacesAdded: 0, NewGroups: 0, GroupMembersAdded: 0 };
            var text = nothingNew
                ? PeopleStrings.ImportNothingNew
                : string.Format(PeopleStrings.ImportDoneFormat, result.NewPeople, result.FacesAdded, result.NewGroups, result.GroupMembersAdded);

            if (result.FacesKnown > 0 && !nothingNew)
                text += string.Format(PeopleStrings.ImportKnownFormat, result.FacesKnown);
            if (result.FacesUnusable > 0)
                text += string.Format(PeopleStrings.ImportUnusableFormat, result.FacesUnusable);
            if (result.Refused.Count > 0)
                text += "\n\n" + string.Join("\n", result.Refused);
            return text;
        }

        /// <summary>A failure the person must not miss: in the line at the bottom and in a message.</summary>
        private void Problem(string title, string message)
        {
            _status.Text = message;
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>After a change: says so when the copy of the groups could not be kept in the photo folder (they are saved on this computer all the same).</summary>
        private void ReportFolderCopy()
        {
            if (_people.Groups.FolderCopyError is { } error)
                _status.Text = string.Format(PeopleStrings.FolderCopyErrorFormat, error);
        }

        /// <summary>Does a change; returns why it did not work, or null. A name that is refused says so itself, a file that can not be written is told in a sentence.</summary>
        private static string? Try(Action change)
        {
            try
            {
                change();
                return null;
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return string.Format(PeopleStrings.SaveErrorFormat, ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
