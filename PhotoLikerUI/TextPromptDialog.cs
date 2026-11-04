namespace PhotoLikerUI
{
    /// <summary>
    /// Asks for one line of text (the name of a group). What is typed is handed to a function before the window closes: it uses the text and says
    /// null, or says why it can not, in which case the reason is shown and the window stays open for another try.
    /// </summary>
    internal sealed class TextPromptDialog : Form
    {
        private readonly TextBox _text = new();
        private readonly Func<string, string?> _commit;

        /// <param name="commit">Gets the text without the spaces around it; returns null when it took it, else the reason it did not
        /// (an empty reason: the window stays open and says nothing, the function has asked the person already).</param>
        public TextPromptDialog(string title, string prompt, string initial, Func<string, string?> commit)
        {
            _commit = commit;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 96);

            var label = new Label { Text = prompt, AutoSize = true, Location = new Point(12, 12) };

            _text.Location = new Point(12, 34);
            _text.Width = 336;
            _text.MaxLength = 60;
            _text.Text = initial;

            var ok = new Button { Text = SearchStrings.Ok, Location = new Point(192, 62), Size = new Size(75, 26) };
            var cancel = new Button { Text = SearchStrings.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(273, 62), Size = new Size(75, 26) };
            ok.Click += (_, _) => Commit();

            Controls.AddRange([label, _text, ok, cancel]);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (_, _) => _text.SelectAll();
        }

        /// <summary>The text that was taken, without the spaces around it.</summary>
        public string Value => _text.Text.Trim();

        private void Commit()
        {
            if (_commit(Value) is { } reason)
            {
                if (reason.Length > 0)
                    MessageBox.Show(this, reason, PeopleStrings.NameProblemTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _text.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
