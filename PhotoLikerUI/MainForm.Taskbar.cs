namespace PhotoLikerUI
{
    /// <summary>
    /// The progress of the program on its taskbar button (see <see cref="TaskbarProgress"/>): the indexing bar of the status strip is registered here,
    /// the bars of the dialogs register themselves. The button belongs to this window; a dialog is owned by it and has none of its own.
    /// </summary>
    public partial class MainForm
    {
        private TaskbarProgress? _taskbarProgress;
        private IDisposable? _indexProgressWatch;
        private IDisposable? _transferProgressWatch;

        private void InitializeTaskbarProgress()
        {
            _indexProgressWatch = TaskbarProgress.Watch(() => TaskbarProgress.Read(_indexProgress));
            _transferProgressWatch = TaskbarProgress.Watch(() => TaskbarProgress.Read(_transferProgress)); // after the indexing: an index file that is moved while photos are indexed is what the button shows
            _taskbarProgress = new TaskbarProgress(this);
        }

        private void DisposeTaskbarProgress()
        {
            _indexProgressWatch?.Dispose();
            _transferProgressWatch?.Dispose();
            _taskbarProgress?.Dispose(); // takes the bar off the button
            _taskbarProgress = null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TaskbarProgress.ButtonCreatedMessage)
                _taskbarProgress?.ButtonCreated();

            base.WndProc(ref m);
        }
    }
}
