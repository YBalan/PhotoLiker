namespace PhotoLikerUI
{
    /// <summary>
    /// A new version, installed from the About box: the box downloads the setup, the window closes (the setup cannot replace a program that
    /// is running) and starts it as the last thing it does.
    /// </summary>
    public partial class MainForm
    {
        private string? _setupAfterClose;

        /// <summary>
        /// Closes the window and, once it is closed, starts the setup. If the window does not close (an index is being built and the
        /// person answers No to stopping it) nothing is started and the program goes on as it was.
        /// </summary>
        private void CloseAndRunSetup(string setup)
        {
            _setupAfterClose = setup;
            Close();
            _setupAfterClose = null; // already used if the window closed; dropped if it did not
        }

        /// <summary>Called by <c>OnFormClosed</c> when everything is saved, as the last thing the window does.</summary>
        private void RunSetupAfterClose()
        {
            if (_setupAfterClose is not { } setup)
                return;

            _setupAfterClose = null;
            var error = UpdateLauncher.RunSetup(setup);
            if (error is not null) // the window is gone, so the message has no owner
                MessageBox.Show(string.Format(AboutStrings.UpdateStartFailedFormat, setup, error), MainFormStrings.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
