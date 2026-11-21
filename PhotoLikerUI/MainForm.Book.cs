namespace PhotoLikerUI
{
    /// <summary>The "Make a book" button of the toolbar: opens the window that makes a PDF or an EPUB of the loaded photos, the liked photos or a group.</summary>
    public partial class MainForm
    {
        private ToolStripButton _makeBookButton = null!;

        private void InitializeBook()
        {
            _makeBookButton = new ToolStripButton(BookStrings.ButtonText)
            {
                Name = "makeBookToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Book(),
                ToolTipText = BookStrings.ButtonTooltip,
            };
            _makeBookButton.Click += (_, _) => MakeBook();

            // The last button of the toolbar: after the Help button (the About box), with a separator between them
            toolStrip1.Items.Add(new ToolStripSeparator { Name = "makeBookToolStripSeparator" });
            toolStrip1.Items.Add(_makeBookButton);
        }

        private void ApplyBookLanguage()
        {
            _makeBookButton.Text = BookStrings.ButtonText;
            _makeBookButton.ToolTipText = BookStrings.ButtonTooltip;
        }

        /// <summary>Opens the book window; a window that is already open for the program is not opened twice.</summary>
        private void MakeBook()
        {
            _makeBookButton.Enabled = false;
            try
            {
                _currentConfig.ApplyDefaultLikedFolder();
                SetStatus(BookStrings.StatusOpening);

                using var dialog = CreateBookDialog();
                dialog.ShowDialog(this);

                if (!string.IsNullOrWhiteSpace(dialog.MadeBookPath) && File.Exists(dialog.MadeBookPath))
                    SetStatus(string.Format(BookStrings.StatusMadeFormat, dialog.MadeBookPath));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(BookStrings.StatusError, ex.Message));
                MessageBox.Show(this, ex.Message, BookStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _makeBookButton.Enabled = true;
            }
        }

        /// <summary>Makes the window with what the program is showing; a test makes it without showing it.</summary>
        internal BookDialog CreateBookDialog() => new(
            _currentConfig.CurrentFolder,
            _currentConfig.Files,
            _currentConfig.LikedFolder,
            _currentConfig.Extensions,
            _currentConfig.SortOrder,
            _currentConfig.IsDarkTheme);
    }
}
