using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// The "Video speech" button of the toolbar (shown while videos are listed): opens the table of the videos of the folder with what is said in each, their moments, hearing them again in
    /// another language, and the export of the whole table to Excel (see <see cref="VideoSpeechDialog"/>).
    /// </summary>
    public partial class MainForm
    {
        private ToolStripButton _videoSpeechButton = null!;

        private void InitializeVideoSpeech()
        {
            _videoSpeechButton = new ToolStripButton(VideoSpeechStrings.ButtonText)
            {
                Name = "videoSpeechToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.VideoSpeech(),
                ToolTipText = VideoSpeechStrings.ButtonTooltip,
                Visible = false, // there are videos to show only when SupportVideo is on (see UpdateVideoSpeechButton)
            };
            _videoSpeechButton.Click += (_, _) => ShowVideoSpeech();

            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(_findDuplicatesButton) + 1, _videoSpeechButton);
        }

        private void ApplyVideoSpeechLanguage()
        {
            _videoSpeechButton.Text = VideoSpeechStrings.ButtonText;
            _videoSpeechButton.ToolTipText = VideoSpeechStrings.ButtonTooltip;
        }

        /// <summary>The button is there while videos are listed, and works while a folder is open.</summary>
        private void UpdateVideoSpeechButton()
        {
            if (_videoSpeechButton is null)
                return; // the window is still being made

            _videoSpeechButton.Visible = _currentConfig.SupportVideo;
            _videoSpeechButton.Enabled = !string.IsNullOrWhiteSpace(_currentConfig.CurrentFolder);
        }

        private void ShowVideoSpeech()
        {
            var folder = _currentConfig.CurrentFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                SetStatus(SearchStrings.StatusNoFolder);
                return;
            }
            if (!_currentConfig.SupportVideo)
            {
                SetStatus(VideoSpeechStrings.StatusVideosOff);
                return;
            }

            VideoCatalog catalog;
            try
            {
                catalog = VideoLibrary.Open(folder);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(AppStrings.StatusFolderLoadError, ex.Message));
                return;
            }

            var settings = _currentConfig;
            using var dialog = new VideoSpeechDialog(
                folder, catalog, SearchModel.Id, SpeechModel, _models.ModelsFolder, settings.SpeechLanguage ?? string.Empty,
                cores: () => settings.CpuCores, indexing: () => _indexCts is not null, _currentConfig.IsDarkTheme);
            dialog.ShowDialog(this);

            if (!dialog.Changed)
                return;

            // The speech of some videos was read again: the index label counts the videos with speech, and the caption of the video that is shown is its words
            UpdateSearchControls();
            if (pictureBox1.Tag is string shown && MediaKinds.IsVideo(shown))
                UpdateMomentStrip(shown);
        }
    }
}
