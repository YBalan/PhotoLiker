using System.Collections.Specialized;
using System.Drawing.Drawing2D;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// Videos in the viewer. With <see cref="Settings.SupportVideo"/> on, the videos of the folder are in the list with the photos: the viewer shows the
    /// poster picture of the one that is current, with a play mark on it, and playing it (Enter, or a click) puts the player over the picture. The
    /// keys of the photos keep their meaning (the arrows go to the next item, Space likes, the letters and numbers are groups, Delete moves the file away);
    /// the player adds Enter (play and pause), Shift+arrows (5 seconds), Ctrl+arrows (30 seconds), Ctrl+Up and Ctrl+Down (volume), and Escape (back to the poster).
    /// </summary>
    public partial class MainForm
    {
        private const long ShortSeekMs = 5_000;
        private const long LongSeekMs = 30_000;
        private const int VolumeStep = 10;

        private VideoPlayerPanel? _player;

        private bool ShowingVideo => pictureBox1.Tag is string file && MediaKinds.IsVideo(file);

        private bool PlayerShown => _player is { Visible: true };

        /// <summary>Tells the rest of the program which extensions are videos in this folder (the photo cache and the thumbnails only have a path).</summary>
        private void ApplyVideoSettings() => MediaKinds.UseVideoExtensions(_currentConfig.VideoExtensions);

        /// <summary>Opens the catalog of the videos of the folder if videos are on, else closes it. Called when a folder is listed.</summary>
        private void PrepareVideoCatalog()
        {
            ApplyVideoSettings();
            var folder = _currentConfig.CurrentFolder;
            if (!_currentConfig.SupportVideo || string.IsNullOrWhiteSpace(folder))
            {
                VideoLibrary.Close();
                return;
            }

            try
            {
                VideoLibrary.Open(folder);
            }
            catch (Exception ex)
            {
                VideoLibrary.Close();
                SetStatus(string.Format(AppStrings.StatusFolderLoadError, ex.Message));
            }
        }

        private VideoPlayerPanel EnsurePlayer()
        {
            if (_player is { } existing)
                return existing;

            var player = new VideoPlayerPanel { Dock = DockStyle.Fill, Visible = false };
            player.Used += FocusViewer;
            player.Failed += OnPlayerFailed;
            player.NotATime += SetStatus;
            scrollPanel.Controls.Add(player);
            player.BringToFront(); // in front of the picture, and the status bar (docked below) keeps its place
            _player = player;
            ThemeManager.Apply(this, _currentConfig.IsDarkTheme);
            return player;
        }

        /// <summary>Plays the video that is shown, from <paramref name="startMs"/>. A search that found a moment in a video gives the time.</summary>
        private async void PlayCurrentVideo(long startMs = 0)
        {
            if (pictureBox1.Tag is not string file || !MediaKinds.IsVideo(file))
                return;

            if (!File.Exists(file))
            {
                SetStatus(string.Format(VideoPlayerStrings.StatusVideoFileMissingFormat, Path.GetFileName(file)));
                return;
            }

            var player = EnsurePlayer();
            player.Visible = true;
            player.BringToFront();
            pictureBox1.Invalidate();
            SetStatus(string.Format(VideoPlayerStrings.StatusVideoShownFormat, Path.GetFileName(file)));
            await player.PlayAsync(file, startMs);
            FocusViewer();
        }

        /// <summary>The player goes away and the poster shows again; the file is let go of in the background.</summary>
        private void HidePlayer()
        {
            if (_player is not { } player)
                return;

            player.Visible = false;
            _ = player.StopAsync();
            pictureBox1.Invalidate();
        }

        /// <summary>For what moves or deletes the file of the video that is playing: it is let go of (Windows does not move a file that is being played) before this returns.</summary>
        private async Task ReleasePlayedVideoAsync(string file)
        {
            if (_player is { } player && string.Equals(player.CurrentFile, file, StringComparison.Ordinal))
            {
                player.Visible = false;
                await player.StopAsync();
            }
        }

        private void OnPlayerFailed(string message, bool engineProblem)
        {
            SetStatus(message);
            if (!engineProblem || pictureBox1.Tag is not string file)
                return;

            // The engine cannot start (its files are missing): the video is still worth seeing, in whatever Windows plays it with
            HidePlayer();
            var problem = ShellOpen.Photo(file);
            SetStatus(problem is null ? string.Format(VideoPlayerStrings.StatusVideoOpenedElsewhereFormat, Path.GetFileName(file)) : message + " " + problem);
        }

        /// <summary>The keys of a video that is shown. Returns true if the key was one of them.</summary>
        private bool TryHandleVideoKey(Keys keyData)
        {
            var shown = PlayerShown;
            switch (keyData)
            {
                case Keys.Enter:
                    if (shown)
                        _player!.TogglePause();
                    else
                        PlayCurrentVideo();
                    return true;
                case Keys.Escape when shown:
                    HidePlayer();
                    return true;
                case Keys.Shift | Keys.Left when shown:
                    _player!.SeekBy(-ShortSeekMs);
                    return true;
                case Keys.Shift | Keys.Right when shown:
                    _player!.SeekBy(ShortSeekMs);
                    return true;
                case Keys.Control | Keys.Left when shown:
                    _player!.SeekBy(-LongSeekMs);
                    return true;
                case Keys.Control | Keys.Right when shown:
                    _player!.SeekBy(LongSeekMs);
                    return true;
                case Keys.Control | Keys.Up when shown:
                    _player!.ChangeVolume(VolumeStep);
                    return true;
                case Keys.Control | Keys.Down when shown:
                    _player!.ChangeVolume(-VolumeStep);
                    return true;
            }
            return false;
        }

        /// <summary>Puts the video file on the clipboard as a file (there is no picture to give), so that it can be pasted into a folder or a message.</summary>
        private void CopyVideoToClipboard(string file)
        {
            try
            {
                if (!File.Exists(file))
                    throw new FileNotFoundException(null, file);

                Clipboard.SetFileDropList(new StringCollection { file });
                SetStatus(string.Format(VideoPlayerStrings.StatusVideoCopiedFormat, Path.GetFileName(file)));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(MainFormStrings.StatusCopyError, ex.Message));
            }
        }

        // ── the play mark on the poster, and on the thumbnails ─────────────

        /// <summary>A round play mark in the middle of the poster, and how long the video plays in the corner.</summary>
        private void DrawVideoMark(Graphics g)
        {
            if (!ShowingVideo || PlayerShown || pictureBox1.Image is null)
                return;

            var area = pictureBox1.ClientRectangle;
            var diameter = Math.Clamp(Math.Min(area.Width, area.Height) / 6, 56, 120);
            DrawPlayMark(g, new Rectangle(area.Left + (area.Width - diameter) / 2, area.Top + (area.Height - diameter) / 2, diameter, diameter));

            if (pictureBox1.Tag is string file && _photoCache.TryGet(file, out var entry) && entry.VideoDuration is { } duration)
            {
                var text = VideoTime.Format(duration);
                using var font = new Font(Font.FontFamily, 14f, FontStyle.Bold);
                var size = TextRenderer.MeasureText(g, text, font);
                var box = new Rectangle(area.Right - size.Width - 18, area.Bottom - size.Height - 14, size.Width + 12, size.Height + 4);
                using var back = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
                g.FillRectangle(back, box);
                TextRenderer.DrawText(g, text, font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private static void DrawPlayMark(Graphics g, Rectangle circle)
        {
            var before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillEllipse(back, circle);
            using (var edge = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(2f, circle.Width / 28f)))
                g.DrawEllipse(edge, circle);

            // The triangle sits a little to the right of the middle: its weight is on the left, so the middle of it would look too far left
            var w = circle.Width * 0.34f;
            var h = circle.Height * 0.40f;
            var x = circle.Left + circle.Width * 0.5f - w * 0.35f;
            var y = circle.Top + (circle.Height - h) / 2f;
            using (var triangle = new SolidBrush(Color.White))
                g.FillPolygon(triangle, [new PointF(x, y), new PointF(x, y + h), new PointF(x + w, y + h / 2f)]);
            g.SmoothingMode = before;
        }

        /// <summary>A small play mark in the corner of the thumbnail of a video.</summary>
        private static void DrawThumbVideoMark(Graphics g, Size thumb)
        {
            var side = Math.Clamp(thumb.Width / 4, 18, 30);
            DrawPlayMark(g, new Rectangle(4, 4, side, side));
        }
    }
}
