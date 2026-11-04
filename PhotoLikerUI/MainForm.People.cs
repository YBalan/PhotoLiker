using PhotoLiker.Search;

namespace PhotoLikerUI
{
    /// <summary>The people and their groups: the window that sorts the people into groups, and the groups in the menu of a face.</summary>
    public partial class MainForm
    {
        /// <summary>The group the last new person was put into: the next new person starts with it, because faces of one photo are usually named one after the other.</summary>
        private string _lastGroup = string.Empty;

        private void OpenPeopleDialog()
        {
            if (_folderSearch is not { } search)
                return;

            using (var dialog = new PeopleDialog(search.People, _currentConfig.IsDarkTheme))
                dialog.ShowDialog(this);

            UpdateAutoComplete(); // the groups made there can be searched by their names
            UpdateFaces(); // and somebody may have been renamed
            FocusViewer();
        }

        /// <summary>
        /// The people in the menu of a face. With groups the menu shows a group by name with its people in a sub-menu, and then the people who are in no
        /// group, so that it stays short however many people there are; the person the face looks like is also at the top, because it may be in a sub-menu.
        /// </summary>
        private void AddPeopleToFaceMenu(ContextMenuStrip menu, FaceLabel label, FolderIndex search)
        {
            var people = search.People;
            var spelling = people.Names.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);

            ToolStripMenuItem PersonItem(string name)
            {
                var item = new ToolStripMenuItem(name) { Checked = label.Confirmed && string.Equals(name, label.Name, StringComparison.CurrentCultureIgnoreCase) };
                item.Click += (_, _) => NameFace(label.Face, name);
                return item;
            }

            var groups = people.Groups.Names;
            if (groups.Count > 0 && label.Name is { } likely && !label.Confirmed)
            {
                var first = PersonItem(likely);
                first.Font = new Font(menu.Font, FontStyle.Bold);
                menu.Items.Add(first);
                menu.Items.Add(new ToolStripSeparator());
            }

            var inGroup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var members = people.Groups.MembersOf(group).Where(spelling.ContainsKey).Select(member => spelling[member]).ToList();
                if (members.Count == 0)
                    continue;

                var parent = new ToolStripMenuItem(group)
                {
                    Checked = label.Confirmed && label.Name is { } named && members.Contains(named, StringComparer.CurrentCultureIgnoreCase),
                };
                parent.DropDownItems.AddRange([.. members.Select(PersonItem)]);
                ApplyMenuTheme(parent.DropDown);
                menu.Items.Add(parent);
                inGroup.UnionWith(members);
            }

            var others = people.Names.Where(name => !inGroup.Contains(name)).ToList();
            if (inGroup.Count > 0 && others.Count > 0)
                menu.Items.Add(new ToolStripSeparator());
            foreach (var name in others)
                menu.Items.Add(PersonItem(name));
        }

        /// <summary>"Rename ..." in the menu of a face: the person gets another name, everywhere.</summary>
        private void RenamePersonFromMenu(string person)
        {
            if (_folderSearch is not { } search)
                return;
            if (PersonRenamePrompt.Ask(this, search.People, person, _currentConfig.IsDarkTheme) is not { } renamed)
                return;

            SetStatus(search.People.SharedError is { } shared
                ? string.Format(SearchStrings.StatusSharedPeopleError, shared)
                : string.Format(SearchStrings.StatusPersonRenamedFormat, person, renamed));
            UpdateAutoComplete();
            UpdateFaces();
        }

        /// <summary>A person who was just named, put into the group typed in the window (made if it is new). Says what happened in the status bar.</summary>
        private void PutPersonInGroup(FolderIndex search, string person, string group)
        {
            _lastGroup = group;
            if (group.Length == 0)
                return;

            try
            {
                search.People.SetGroupMembers(group, [person], member: true);
                _lastGroup = search.People.Groups.TryFind(group, out var actual) ? actual : group;
                SetStatus(search.People.Groups.FolderCopyError is { } copyError
                    ? string.Format(SearchStrings.StatusGroupsFolderCopyError, copyError)
                    : string.Format(SearchStrings.StatusFaceNamedInGroupFormat, person, _lastGroup));
                UpdateAutoComplete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(SearchStrings.StatusGroupNotSavedFormat, person, group, ex.Message));
            }
            catch (ArgumentException ex)
            {
                SetStatus(ex.Message);
            }
        }
    }
}
