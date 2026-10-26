namespace PhotoLikerUI
{
    /// <summary>Asks for the name of a person; suggests the names that are in use already.</summary>
    internal sealed class NamePersonDialog : Form
    {
        private readonly TextBox _name = new();

        public NamePersonDialog(IEnumerable<string> knownNames)
        {
            Text = SearchStrings.NamePersonTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(340, 96);

            var prompt = new Label { Text = SearchStrings.NamePersonPrompt, AutoSize = true, Location = new Point(12, 12) };

            _name.Location = new Point(12, 34);
            _name.Width = 316;
            _name.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _name.AutoCompleteSource = AutoCompleteSource.CustomSource;
            var names = new AutoCompleteStringCollection();
            names.AddRange([.. knownNames]);
            _name.AutoCompleteCustomSource = names;

            var ok = new Button { Text = SearchStrings.Ok, DialogResult = DialogResult.OK, Location = new Point(172, 62), Size = new Size(75, 26) };
            var cancel = new Button { Text = SearchStrings.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(253, 62), Size = new Size(75, 26) };

            Controls.AddRange([prompt, _name, ok, cancel]);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>The name typed, without the spaces around it.</summary>
        public string PersonName => _name.Text.Trim();
    }
}
