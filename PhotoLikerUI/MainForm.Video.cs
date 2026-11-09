using System.Collections.Specialized;
using System.Drawing.Drawing2D;
using System.Net.Http;
using PhotoLiker.Search;
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
        private MomentStrip? _strip;
        private int _stripGeneration;
        private long _lastMomentMs;
        private IReadOnlyList<SpeechSegment> _speech = []; // what is said in the shown video, if it was read (see SpeechIndexingJob)

        // What the search that is shown found in each video of it, by path: where in the video, and which stretches (empty when no results are shown)
        private Dictionary<string, SearchHit> _resultHits = new(StringComparer.Ordinal);

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

        // ── the engine that plays videos ───────────────────────────────────

        // A release does not carry the engine (VlcEngine): it is downloaded the first time videos are turned on or played, after asking
        private Task<bool>? _engineDownload;      // a download that is going on: whoever needs the engine waits for it
        private bool _engineDeclined;             // the answer was no: videos open in the program Windows has for them, until SupportVideo is turned on again
        private CancellationTokenSource? _engineStop;
        private long _engineShownMegabytes = -1;
        private bool _engineFinished;             // reports are posted to the window's thread: the last of them can arrive after "ready" and must not replace it

        /// <summary>Asked before the engine is downloaded: what it is, how big and from where. A test answers it.</summary>
        protected virtual bool ConfirmEngineDownload(ComponentSource source) =>
            MessageBox.Show(this, string.Format(VideoPlayerStrings.EngineDownloadQuestionFormat, Megabytes(source.Bytes), source.Host),
                VideoPlayerStrings.EngineDownloadTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

        /// <summary>Opens a file in the program Windows has for it. A test answers it (it would start a player on the desktop).</summary>
        protected virtual string? OpenInDefaultProgram(string file) => ShellOpen.Photo(file);

        /// <summary>True when the engine is there to play videos with, after asking whether to download it, and downloading it, if it is not.</summary>
        private async Task<bool> EnsureVideoEngineAsync()
        {
            if (VideoPlayerPanel.EngineInstalled)
                return true;
            if (_engineDownload is { } running)
                return await running;
            if (_engineDeclined)
                return false;

            _engineDownload = DownloadEngineAsync();
            try
            {
                return await _engineDownload;
            }
            finally
            {
                _engineDownload = null;
            }
        }

        /// <summary>
        /// Videos were just turned on: if the engine that plays them has to be downloaded, the folder is listed again once it is there (like F5), so that the list and the viewer
        /// are made with the player in place. Nothing is listed again when the engine was there already, when the answer was no or the download failed, or when the person has gone
        /// to another folder or turned videos off meanwhile.
        /// </summary>
        private async Task RelistAfterEngineAsync()
        {
            var folder = _currentConfig.CurrentFolder;
            var wasMissing = !VideoPlayerPanel.EngineInstalled;
            if (!await EnsureVideoEngineAsync() || !wasMissing)
                return;

            if (IsDisposed || !_currentConfig.SupportVideo || string.IsNullOrWhiteSpace(folder) || !SameFolder(folder, _currentConfig.CurrentFolder))
                return;

            ClearDisplay();
            LoadFolder(useSavedList: false);
        }

        private async Task<bool> DownloadEngineAsync()
        {
            _engineStop ??= new CancellationTokenSource();
            var token = _engineStop.Token;
            try
            {
                SetStatus(VideoPlayerStrings.StatusEngineLooking);
                var source = await VlcEngine.ResolveAsync(token);
                if (source is null)
                {
                    SetStatus(VideoPlayerStrings.StatusEngineOffline);
                    return false;
                }

                if (!ConfirmEngineDownload(source))
                {
                    _engineDeclined = true;
                    SetStatus(VideoPlayerStrings.StatusEngineDeclined);
                    return false;
                }

                _engineShownMegabytes = -1;
                _engineFinished = false;
                await VlcEngine.DownloadAsync(new Progress<ComponentProgress>(ShowEngineProgress), token, source);
                _engineFinished = true;
                SetStatus(VideoPlayerStrings.StatusEngineReady);
                VideoPlayerPanel.WarmUp();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false; // the window is closing
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(VideoPlayerStrings.StatusEngineFailedFormat, ex.Message));
                return false;
            }
        }

        private void ShowEngineProgress(ComponentProgress progress)
        {
            if (_engineFinished)
                return;

            if (progress.Stage == ComponentStage.Unpacking)
            {
                SetStatus(string.Format(VideoPlayerStrings.StatusEngineUnpackingFormat, progress.Done, progress.Total));
                return;
            }

            var megabytes = Megabytes(progress.Done);
            if (megabytes != _engineShownMegabytes)
            {
                _engineShownMegabytes = megabytes;
                SetStatus(string.Format(VideoPlayerStrings.StatusEngineDownloadingFormat, megabytes, Megabytes(progress.Total)));
            }
        }

        private static long Megabytes(long bytes) => (bytes + (1 << 19)) >> 20; // 82,765,861 bytes are 79 MB (of 1,048,576), as Windows says

        /// <summary>The engine cannot be had (not downloaded, or it cannot start): the video is still worth seeing, in whatever Windows plays it with.</summary>
        private void OpenVideoElsewhere(string file, string message, string openedFormat)
        {
            HidePlayer();
            var problem = OpenInDefaultProgram(file);
            SetStatus(problem is null ? string.Format(openedFormat, Path.GetFileName(file)) : message + " " + problem);
        }

        private VideoPlayerPanel EnsurePlayer()
        {
            if (_player is { } existing)
                return existing;

            var player = new VideoPlayerPanel { Dock = DockStyle.Fill, Visible = false };
            player.Used += FocusViewer;
            player.Failed += OnPlayerFailed;
            player.NotATime += SetStatus;
            player.SaidAt = SaidNear;
            player.TimeChanged += ms =>
            {
                _lastMomentMs = ms;
                _strip?.SetCurrent(ms);
                ShowCaption(ms);
            };
            scrollPanel.Controls.Add(player);
            player.BringToFront(); // in front of the picture, and the status bar (docked below) keeps its place
            _player = player;
            ThemeManager.Apply(this, _currentConfig.IsDarkTheme);
            return player;
        }

        private readonly ToolTip _heardTip = new() { ShowAlways = true };

        /// <summary>The hit of a video of the shown results that was found in what is said (not only in the pictures); null for anything else.</summary>
        private SearchHit? HeardHitOf(string path) =>
            _resultsActive && _resultHits.TryGetValue(path, out var hit) && hit.Spoken is { Count: > 0 } ? hit : null;

        /// <summary>The text over the thumbnail of a video that was found in what is said (what is said there), none for any other.</summary>
        private string? HeardTooltipOf(string path) =>
            HeardHitOf(path) is { } hit ? string.Format(VideoSearchStrings.ThumbHeardTooltipFormat, hit.Said is { } said ? OneLine(said) ?? string.Empty : string.Empty) : null;

        /// <summary>The place in the shown video that a search found, if search results are shown and found one in it.</summary>
        private SearchHit? HitOfShownVideo() =>
            _resultsActive && pictureBox1.Tag is string file && _resultHits.TryGetValue(file, out var hit) ? hit : null;

        /// <summary>
        /// Plays the video that is shown, from <paramref name="startMs"/>; without a time, from the place a search found in it (the best fitting moment),
        /// else from the start.
        /// </summary>
        private async void PlayCurrentVideo(long? startMs = null)
        {
            if (pictureBox1.Tag is not string file || !MediaKinds.IsVideo(file))
                return;

            var hit = HitOfShownVideo();
            var from = startMs ?? hit?.TimeMs ?? 0;

            if (!File.Exists(file))
            {
                SetStatus(string.Format(VideoPlayerStrings.StatusVideoFileMissingFormat, Path.GetFileName(file)));
                return;
            }

            // The engine may have to be downloaded first; the person may have gone to another item by the time it is there
            if (!await EnsureVideoEngineAsync())
            {
                if (pictureBox1.Tag is string stillShown && stillShown == file)
                    OpenVideoElsewhere(file, VideoPlayerStrings.EngineNotInstalled, VideoPlayerStrings.StatusVideoOpenedNoEngineFormat);
                return;
            }
            if (pictureBox1.Tag is not string current || current != file)
                return;

            var player = EnsurePlayer();
            player.Visible = true;
            player.BringToFront();
            pictureBox1.Invalidate();
            // What was found in what is said comes first: the rest of the line (the keys) is long and may be cut
            SetStatus((hit is { Spoken.Count: > 0, Said: { Length: > 0 } said } ? string.Format(VideoSearchStrings.StatusVideoHeardFormat, OneLine(said)) : string.Empty)
                + string.Format(VideoPlayerStrings.StatusVideoShownFormat, Path.GetFileName(file)));
            player.SetMarks(hit?.Moments, hit?.Spoken);
            _lastMomentMs = from;
            await player.PlayAsync(file, from);
            FocusViewer();
        }

        /// <summary>The player goes away and the poster shows again; the file is let go of in the background.</summary>
        private void HidePlayer()
        {
            if (_player is not { } player)
                return;

            player.Visible = false;
            _ = player.StopAsync();
            _strip?.SetCurrent(-1);
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

            OpenVideoElsewhere(file, message, VideoPlayerStrings.StatusVideoOpenedElsewhereFormat);
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
                case Keys.Control | Keys.S:
                    SaveVideoFrame();
                    return true;
                case Keys.OemPeriod:
                    GoToMoment(forward: true);
                    return true;
                case Keys.Oemcomma:
                    GoToMoment(forward: false);
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

        /// <summary>
        /// Saves the picture the video shows now (the first one, if it is not playing) as a photo in the Liked folder: full size, named after the video and the place, with the
        /// date it was recorded at that place as its date taken. It is a file like any other copy there; the photos of the folder are not changed.
        /// </summary>
        private async void SaveVideoFrame()
        {
            if (pictureBox1.Tag is not string file || !MediaKinds.IsVideo(file))
                return;

            var folder = _currentConfig.LikedFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                SetStatus(VideoPlayerStrings.StatusFrameNoFolder);
                return;
            }

            var timeMs = PlayerShown ? _player!.TimeMs : _lastMomentMs;
            try
            {
                var catalog = VideoLibrary.Current;
                var recorded = catalog is null ? null : await Task.Run(async () => (await catalog.GetAsync(file)).Date.Taken);
                var saved = await VideoFrameSaver.SaveAsync(file, timeMs, folder, recorded);
                SetStatus(string.Format(VideoPlayerStrings.StatusFrameSavedFormat, Path.GetFileName(file), VideoTime.Format(timeMs), Path.GetFileName(saved)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(VideoPlayerStrings.StatusFrameErrorFormat, ex.Message));
            }
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

        // ── the moments of a video ─────────────────────────────────────────

        private MomentStrip EnsureStrip()
        {
            if (_strip is { } existing)
                return existing;

            var strip = new MomentStrip { Dock = DockStyle.Bottom, Visible = false };
            strip.MomentClicked += OnMomentClicked;
            strip.SaidIn = SaidIn;
            strip.Used += FocusViewer;
            scrollPanel.Controls.Add(strip);
            // Docked above the status bar and below the picture: the status bar (the last control) is laid out first, then this, then what fills the rest
            scrollPanel.Controls.SetChildIndex(strip, scrollPanel.Controls.GetChildIndex(statusStrip1));
            _strip = strip;
            ThemeManager.Apply(this, _currentConfig.IsDarkTheme);
            return strip;
        }

        /// <summary>
        /// Shows the moments of the video that is shown, as small pictures under the viewer, with the places a search found marked; hides the strip for a photo and for a
        /// video that has no moments (it has not been analyzed). The pictures are read from the catalog and decoded in the background.
        /// </summary>
        private async void UpdateMomentStrip(string? file)
        {
            var generation = ++_stripGeneration;
            var catalog = VideoLibrary.Current;
            _speech = [];
            _player?.SetCaption(null);
            if (file is null || !MediaKinds.IsVideo(file) || !_currentConfig.SupportVideo || catalog is null)
            {
                if (_strip is { } hidden)
                {
                    hidden.Clear();
                    hidden.Visible = false;
                }
                return;
            }

            _ = LoadSpeechAsync(file, catalog, generation);

            var preferred = SearchModel.Id;
            List<(long TimeMs, Bitmap Picture)> moments;
            try
            {
                moments = await Task.Run(() => ReadMomentPictures(catalog, file, preferred));
            }
            catch (Exception)
            {
                return; // the catalog was closed (another folder), or the file is gone: no strip
            }

            if (generation != _stripGeneration || IsDisposed)
            {
                foreach (var (_, picture) in moments)
                    picture.Dispose();
                return;
            }

            var strip = EnsureStrip();
            if (moments.Count == 0)
            {
                strip.Clear();
                strip.Visible = false;
                return;
            }

            var hit = HitOfShownVideo();
            strip.SetMoments(moments, hit?.Moments, hit?.Spoken);
            strip.Visible = true;
            strip.ScrollToFirstHit();
            if (_player is { Visible: true } player)
                strip.SetCurrent(player.TimeMs);
        }

        /// <summary>Reads what is said in the video that is shown (if the speech of the folder was read), for the line under the player.</summary>
        private async Task LoadSpeechAsync(string file, VideoCatalog catalog, int generation)
        {
            IReadOnlyList<SpeechSegment> speech;
            try
            {
                speech = await Task.Run(() => catalog.SpeechOf(file));
            }
            catch (Exception)
            {
                return; // the catalog was closed (another folder)
            }

            if (generation == _stripGeneration && !IsDisposed)
                _speech = speech;
        }

        // The words said in a stretch of the shown video (milliseconds), as a line that fits over a picture; null when the speech was not read or nothing is said there
        private string? SaidIn(long fromMs, long toMs) =>
            _speech.Count == 0 ? null : OneLine(string.Join(' ', _speech.Where(s => s.EndMs > fromMs && s.StartMs < toMs).Select(s => s.Text.Trim())));

        // The words said a little before and after a place (the slider's hover text)
        private string? SaidNear(long ms) => SaidIn(ms - 1500, ms + 1500);

        private static string? OneLine(string text)
        {
            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length == 0 ? null : text.Length <= 140 ? text : text[..139] + "…";
        }

        // The words that are said at this place in the video, a little before and after their segment: the model puts a segment where the voice starts and stops
        private void ShowCaption(long timeMs)
        {
            if (_player is not { } player)
                return;

            var line = _speech.Count == 0 ? null : _speech.FirstOrDefault(s => timeMs >= s.StartMs - 300 && timeMs <= s.EndMs + 1200);
            player.SetCaption(line?.Text);
        }

        // The thumbnails of the model that is chosen; a video that was analyzed with another model only still shows its moments
        private static List<(long TimeMs, Bitmap Picture)> ReadMomentPictures(PhotoLiker.Video.VideoCatalog catalog, string file, string preferredModelId)
        {
            var thumbnails = catalog.Thumbnails(file, preferredModelId);
            for (var i = 0; thumbnails.Count == 0 && i < PhotoLiker.Search.ClipModels.All.Count; i++)
                thumbnails = catalog.Thumbnails(file, PhotoLiker.Search.ClipModels.All[i].Id);

            var pictures = new List<(long, Bitmap)>(thumbnails.Count);
            try
            {
                foreach (var thumbnail in thumbnails)
                {
                    using var stream = new MemoryStream(thumbnail.Jpeg, writable: false);
                    using var decoded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                    pictures.Add((thumbnail.TimeMs, new Bitmap(decoded)));
                }
                return pictures;
            }
            catch
            {
                foreach (var (_, picture) in pictures)
                    picture.Dispose();
                throw;
            }
        }

        private void OnMomentClicked(long timeMs)
        {
            if (_player is { Visible: true } player && string.Equals(player.CurrentFile, pictureBox1.Tag as string, StringComparison.Ordinal))
            {
                player.SeekTo(timeMs);
                FocusViewer();
            }
            else
            {
                PlayCurrentVideo(timeMs);
            }
        }

        /// <summary>
        /// Goes to the next (or previous) moment of the shown video: of those a search found if it found some, else of all the moments the video has. Plays the video from there
        /// if it is not playing.
        /// </summary>
        private void GoToMoment(bool forward)
        {
            if (!ShowingVideo)
                return;

            IReadOnlyList<long> places = HitOfShownVideo()?.Moments is { Count: > 0 } found ? found : _strip?.Times ?? [];
            if (places.Count == 0)
                return;

            var now = _player is { Visible: true } player ? player.TimeMs : _lastMomentMs;
            long? target = forward
                ? places.Where(p => p > now + 500).Cast<long?>().FirstOrDefault()
                : places.Where(p => p < now - 1500).Cast<long?>().LastOrDefault();
            if (target is not { } at)
                return;

            OnMomentClicked(at);
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

        /// <summary>A small speech bubble in the other corner of the thumbnail of a video that a search found in what is said.</summary>
        private static void DrawThumbSpeechMark(Graphics g, Size thumb)
        {
            var side = Math.Clamp(thumb.Width / 4, 18, 30);
            SpeechMark.Draw(g, new Rectangle(thumb.Width - side - 4, 4, side, side));
        }
    }
}
