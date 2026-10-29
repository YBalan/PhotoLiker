using PhotoLiker.Core;

namespace PhotoLikerUI
{
    /// <summary>
    /// Copies the photos of a <see cref="GroupAddPlan"/> into their group while it is open, with a progress bar. It is
    /// shown modally, which is what blocks the rest of the program; the only thing to press is Cancel, which stops after the
    /// photo that is being copied. It starts when it is shown and closes by itself when the work is over.
    /// </summary>
    internal sealed class GroupCopyDialog : Form
    {
        private readonly GroupLibrary _groups;
        private readonly GroupAddPlan _plan;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Continuous };
        private readonly Label _counts = new() { AutoSize = false, Text = GroupCopyStrings.ProgressStarting };
        private readonly Label _file = new() { AutoSize = false, AutoEllipsis = true };
        private readonly Button _cancel = new() { Text = GroupCopyStrings.CancelButton };
        private bool _running;

        public GroupCopyDialog(GroupLibrary groups, GroupAddPlan plan, string groupName)
        {
            _groups = groups;
            _plan = plan;

            Text = string.Format(GroupCopyStrings.ProgressTitleFormat, groupName);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false; // the way out is Cancel, which stops cleanly
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 118);

            _counts.SetBounds(12, 12, 436, 20);
            _bar.SetBounds(12, 36, 436, 22);
            _file.SetBounds(12, 64, 436, 20);
            _cancel.SetBounds(373, 86, 75, 26);
            _cancel.Click += (_, _) => Cancel();

            Controls.AddRange([_counts, _bar, _file, _cancel]);
            CancelButton = _cancel;
        }

        /// <summary>What was done; null if the work failed altogether (see <see cref="Error"/>).</summary>
        public GroupAddResult? Result { get; private set; }

        public Exception? Error { get; private set; }

        private void Cancel()
        {
            _cancel.Enabled = false;
            _file.Text = GroupCopyStrings.Stopping;
            _cancellation.Cancel();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _running = true;
            try
            {
                var progress = new Progress<GroupAddProgress>(ShowProgress);
                Result = await Task.Run(() => _groups.AddAll(_plan, progress, _cancellation.Token));
            }
            catch (Exception ex)
            {
                Error = ex;
            }

            _running = false;
            DialogResult = DialogResult.OK;
        }

        private void ShowProgress(GroupAddProgress progress)
        {
            if (IsDisposed || !_running)
                return;

            _bar.Value = progress.BytesTotal > 0
                ? (int)Math.Clamp(progress.BytesDone * _bar.Maximum / progress.BytesTotal, 0, _bar.Maximum)
                : (int)Math.Clamp((long)progress.Done * _bar.Maximum / Math.Max(1, progress.Total), 0, _bar.Maximum);
            _counts.Text = string.Format(
                GroupCopyStrings.ProgressFormat, progress.Done, progress.Total,
                GroupCopyStrings.FormatSize(progress.BytesDone), GroupCopyStrings.FormatSize(progress.BytesTotal));
            if (_cancel.Enabled)
                _file.Text = progress.FileName;
        }

        // Alt+F4 and the like: stop the work, and close when it has stopped
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running)
            {
                Cancel();
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _cancellation.Dispose();
            base.Dispose(disposing);
        }
    }
}
