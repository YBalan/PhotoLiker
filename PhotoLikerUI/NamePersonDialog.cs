namespace PhotoLikerUI
{
    /// <summary>
    /// Asks for the name of a person, and optionally the group the person belongs to; suggests the names and the groups that are in use already.
    /// What is typed is checked by a function before the window closes, so a name that can not be used leaves the window open with the reason.
    /// </summary>
    internal sealed class NamePersonDialog : Form
    {
        private readonly TextBox _name = new();
        private readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDown };
        private readonly Func<string, string, string?> _check;

        /// <param name="groups">The groups there are; a name typed that is not one makes a new group.</param>
        /// <param name="initialGroup">The group to start with (the one used last), or empty for none.</param>
        /// <param name="check">Gets the name and the group (as typed, may be empty); returns null when both can be used, else the reason.</param>
        public NamePersonDialog(IEnumerable<string> knownNames, IEnumerable<string> groups, string initialGroup, Func<string, string, string?> check)
        {
            _check = check;
            Text = SearchStrings.NamePersonTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(340, 156);

            var prompt = new Label { Text = SearchStrings.NamePersonPrompt, AutoSize = true, Location = new Point(12, 12) };

            _name.Location = new Point(12, 34);
            _name.Width = 316;
            _name.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _name.AutoCompleteSource = AutoCompleteSource.CustomSource;
            var names = new AutoCompleteStringCollection();
            names.AddRange([.. knownNames]);
            _name.AutoCompleteCustomSource = names;

            var groupPrompt = new Label { Text = SearchStrings.NamePersonGroupPrompt, AutoSize = true, Location = new Point(12, 66) };
            _group.Location = new Point(12, 88);
            _group.Width = 316;
            _group.MaxLength = 60;
            _group.Items.AddRange([.. groups]);
            _group.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _group.AutoCompleteSource = AutoCompleteSource.ListItems;
            _group.Text = initialGroup;

            var ok = new Button { Text = SearchStrings.Ok, Location = new Point(172, 120), Size = new Size(75, 26) };
            var cancel = new Button { Text = SearchStrings.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(253, 120), Size = new Size(75, 26) };
            ok.Click += (_, _) => Accept();

            Controls.AddRange([prompt, _name, groupPrompt, _group, ok, cancel]);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>The name typed, without the spaces around it.</summary>
        public string PersonName => _name.Text.Trim();

        /// <summary>The group typed or chosen, without the spaces around it; empty for none.</summary>
        public string GroupName => _group.Text.Trim();

        private void Accept()
        {
            if (PersonName.Length == 0)
            {
                DialogResult = DialogResult.OK; // the caller treats an empty name as nothing to do
                return;
            }

            if (_check(PersonName, GroupName) is { } reason)
            {
                MessageBox.Show(this, reason, PeopleStrings.NameProblemTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
