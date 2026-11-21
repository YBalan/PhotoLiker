using PhotoLiker.Core;

namespace PhotoLikerUI
{
    /// <summary>The Video to GIF button of the toolbar: opens the window that makes an animated GIF of a part of a video.</summary>
    public partial class MainForm
    {
        private ToolStripButton _videoToGifButton = null!;

        private void InitializeVideoToGif()
        {
            _videoToGifButton = new ToolStripButton(VideoToGifStrings.ButtonText)
            {
                Name = "videoToGifToolStripButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, // the text is still the accessible name
                Image = AppIcons.VideoToGif(),
                ToolTipText = VideoToGifStrings.ButtonTooltip,
            };
            _videoToGifButton.Click += (_, _) => VideoToGif();

            // The very last button of the toolbar, after Trace bitmap (the other buttons are put in beside their neighbours, so this stays last)
            toolStrip1.Items.Add(_videoToGifButton);
        }

        private void ApplyVideoToGifLanguage()
        {
            _videoToGifButton.Text = VideoToGifStrings.ButtonText;
            _videoToGifButton.ToolTipText = VideoToGifStrings.ButtonTooltip;
        }

        /// <summary>Where the window keeps its values (the frame rate, the width, the colours) between two uses: with the other files of the program.</summary>
        internal static string VideoToGifSettingsFile => Path.Combine(AppPaths.DataFolder, "video-to-gif.json");

        /// <summary>
        /// Opens the Video to GIF window: for the video that is shown, or, when a photo is shown (or nothing), asking for a video first.
        /// </summary>
        private void VideoToGif()
        {
            var shown = pictureBox1.Tag as string;
            var video = shown is not null && File.Exists(shown) && MediaKinds.IsVideo(shown) ? shown : null;

            _videoToGifButton.Enabled = false;
            try
            {
                SetStatus(VideoToGifStrings.StatusOpening);
                using var dialog = CreateVideoToGifDialog(video);
                dialog.ShowDialog(this);

                if (!string.IsNullOrWhiteSpace(dialog.SavedPath) && File.Exists(dialog.SavedPath))
                    SetStatus(string.Format(VideoToGifStrings.StatusSavedFormat, dialog.SavedPath));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(VideoToGifStrings.StatusError, ex.Message));
                MessageBox.Show(this, ex.Message, VideoToGifStrings.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _videoToGifButton.Enabled = true;
            }
        }

        /// <summary>Makes the window for a video (or for none, which asks for one when it opens); a test makes it without showing it.</summary>
        internal VideoToGifDialog CreateVideoToGifDialog(string? videoPath) => new(videoPath, _currentConfig.IsDarkTheme, VideoToGifSettingsFile);
    }
}
