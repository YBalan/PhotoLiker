using PhotoLiker.Core;

namespace PhotoLikerUI
{
    /// <summary>The Trace Bitmap button of the toolbar: opens the window that turns the photo that is shown into a vector drawing (an SVG or a DXF file).</summary>
    public partial class MainForm
    {
        private ToolStripButton _traceBitmapButton = null!;

        private void InitializeTrace()
        {
            _traceBitmapButton = new ToolStripButton(TraceStrings.ButtonText)
            {
                Name = "traceBitmapToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.Trace(),
                ToolTipText = TraceStrings.ButtonTooltip,
            };
            _traceBitmapButton.Click += (_, _) => TraceBitmap();

            // The very last button of the toolbar, after Make a book (the other buttons are put in beside their neighbours, so this stays last)
            toolStrip1.Items.Add(_traceBitmapButton);
        }

        private void ApplyTraceLanguage()
        {
            _traceBitmapButton.Text = TraceStrings.ButtonText;
            _traceBitmapButton.ToolTipText = TraceStrings.ButtonTooltip;
        }

        /// <summary>Where the window keeps its values (the scan, the size, the format) between two uses: with the other files of the program.</summary>
        internal static string TraceSettingsFile => Path.Combine(AppPaths.DataFolder, "trace-bitmap.json");

        /// <summary>Opens the Trace Bitmap window for the photo that is shown.</summary>
        private void TraceBitmap()
        {
            if (pictureBox1.Tag is not string file || !File.Exists(file))
            {
                SetStatus(TraceStrings.StatusNoPhoto);
                return;
            }

            if (MediaKinds.IsVideo(file))
            {
                SetStatus(TraceStrings.StatusIsVideo);
                return;
            }

            if (RefuseModel(file))
                return;

            _traceBitmapButton.Enabled = false;
            try
            {
                SetStatus(TraceStrings.StatusOpening);
                using var dialog = CreateTraceDialog(file);
                dialog.ShowDialog(this);

                if (!string.IsNullOrWhiteSpace(dialog.SavedPath) && File.Exists(dialog.SavedPath))
                    SetStatus(string.Format(TraceStrings.StatusSavedFormat, dialog.SavedPath));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(TraceStrings.StatusError, ex.Message));
                MessageBox.Show(this, ex.Message, TraceStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _traceBitmapButton.Enabled = true;
            }
        }

        /// <summary>Makes the window for a photo; a test makes it without showing it.</summary>
        internal TraceBitmapDialog CreateTraceDialog(string photoPath) => new(photoPath, _currentConfig.IsDarkTheme, TraceSettingsFile);
    }
}
