using System.ComponentModel;
using System.Globalization;
using PhotoLiker.Core;
using PhotoLiker.Video;
using PhotoLiker.VideoToGif;

namespace PhotoLikerUI
{
    /// <summary>The part of the Video to GIF window that works: reads the video, shows the frames the range begins and ends with, tells what the GIF will be, and makes it.</summary>
    internal sealed partial class VideoToGifDialog
    {
        /// <summary>The longer side of the pictures of the first and the last frame.</summary>
        private const int PreviewLongSide = 640;

        private readonly CancellationTokenSource _lifetime = new();
        private string? _video;
        private GifClip? _clip;
        private int _frames;
        private CancellationTokenSource? _making;
        private int _firstGeneration, _lastGeneration;
        private bool _firstDirty, _lastDirty;
        private MemoryStream? _gifStream;

        /// <summary>Asks which video to use (the Open window of Windows); a test answers it. Gets the title, the kinds of file and the folder to begin in; null when nothing was picked.</summary>
        internal delegate string? VideoPrompt(string title, string filter, string folder);

        /// <summary>Asks where to save (the Save window of Windows); a test answers it. Gets the title, the kind of file, the folder and the name it begins with; null when nothing was picked.</summary>
        internal delegate string? SavePathPrompt(string title, string filter, string folder, string fileName);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal VideoPrompt? AskVideoPath { get; set; }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal SavePathPrompt? AskSavePath { get; set; }

        /// <summary>The ffmpeg to make the GIF with; null for the one that comes with the program.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal string? FfmpegPath { get; set; }

        /// <summary>The video the window works on, once one was read.</summary>
        internal string? VideoPath => _video;

        /// <summary>What is known of that video.</summary>
        internal GifClip? Clip => _clip;

        /// <summary>True while a video is being read.</summary>
        internal bool IsReading { get; private set; }

        /// <summary>True while the GIF is being made.</summary>
        internal bool IsMaking => _making is not null;

        /// <summary>What the last GIF that was made holds, read back from the file.</summary>
        internal GifInfo? Made { get; private set; }

        internal string StatusText => _statusLabel.Text;

        internal string SummaryText => _summaryLabel.Text;

        internal string InfoText => _infoLabel.Text;

        internal bool ProgressVisible => _progress.Visible;

        internal ProgressBarStyle ProgressStyle => _progress.Style;

        /// <summary>The first frame of the part that is used, counted from 1.</summary>
        internal int FirstFrame => (int)FirstNumeric.Value;

        /// <summary>The last frame of the part that is used, counted from 1.</summary>
        internal int LastFrame => (int)LastNumeric.Value;

        /// <summary>The part of the video that is used.</summary>
        internal GifRange Range => _clip is not null && _frames > 0 ? GifRange.FromFrames(_clip, FirstFrame - 1, LastFrame - 1) : GifRange.All;

        /// <summary>What the GIF will be with the values that are in the window now; null with no video.</summary>
        internal GifPlan? Plan => _clip is null ? null : GifPlan.Make(_clip, Range, ReadSettings());

        private void SetStatus(string text, bool failure = false)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = failure ? Color.FromArgb(220, 80, 70) : _dark ? ThemeManager.DarkFore : ThemeManager.LightFore;
        }

        // ── Reading the video ───────────────────────────────────────────────

        private async Task OpenAsync()
        {
            if (_firstVideo is not null)
                await OpenVideoAsync(_firstVideo);
            else
                await BrowseAsync();
        }

        /// <summary>Asks for a video and reads it. Nothing changes when none is picked.</summary>
        internal async Task BrowseAsync()
        {
            if (IsMaking)
                return;

            var picked = AskVideo(_video is null ? string.Empty : Path.GetDirectoryName(_video) ?? string.Empty);
            if (picked is null)
            {
                if (_video is null)
                    SetStatus(VideoToGifStrings.StatusNoVideoPicked);
                return;
            }

            await OpenVideoAsync(picked);
        }

        private static string KindsOfVideo => $"{VideoToGifStrings.FilterVideos}|{string.Join(';', MediaKinds.DefaultVideoExtensions.Select(extension => "*" + extension))}|{VideoToGifStrings.FilterAllFiles}|*.*";

        private string? AskVideo(string folder)
        {
            if (AskVideoPath is { } prompt)
                return prompt(VideoToGifStrings.PickTitle, KindsOfVideo, folder);

            using var dialog = new OpenFileDialog
            {
                Title = VideoToGifStrings.PickTitle,
                Filter = KindsOfVideo,
                InitialDirectory = Directory.Exists(folder) ? folder : string.Empty,
                CheckFileExists = true,
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        /// <summary>
        /// Reads the video and makes it the one the window works on: its range is all of it, its first and last frame are shown. A video that cannot be read says why, and the one that was
        /// there stays.
        /// </summary>
        internal async Task OpenVideoAsync(string path)
        {
            IsReading = true;
            UpdateEnabled();
            SetStatus(VideoToGifStrings.StatusReading);
            try
            {
                var info = await VideoProbe.ReadAsync(path, _lifetime.Token);
                if (IsDisposed)
                    return;

                _video = path;
                VideoBox.Text = path;
                _infoLabel.Text = InfoOf(info);
                ReleaseGif();
                SetClip(new GifClip(info.DurationSeconds, info.FramesPerSecond, info.DisplayWidth, info.DisplayHeight));
                SetStatus(string.Empty);
                await ShowFramesAsync();
            }
            catch (OperationCanceledException)
            {
                // The window was closed
            }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or TimeoutException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(VideoToGifStrings.StatusVideoFailedFormat, ex.Message), failure: true);
            }
            finally
            {
                IsReading = false;
                if (!IsDisposed)
                    UpdateEnabled();
            }
        }

        private static string InfoOf(VideoInfo info)
        {
            var length = info.DurationSeconds > 0 ? Clock(info.DurationSeconds) : VideoToGifStrings.UnknownLength;
            return info.FramesPerSecond > 0
                ? string.Format(CultureInfo.CurrentCulture, VideoToGifStrings.InfoFormat, length, info.FramesPerSecond.ToString("0.##", CultureInfo.CurrentCulture), info.DisplayWidth, info.DisplayHeight)
                : string.Format(CultureInfo.CurrentCulture, VideoToGifStrings.InfoNoRateFormat, length, info.DisplayWidth, info.DisplayHeight);
        }

        /// <summary>A time as "1:05.40" (minutes, seconds and hundredths), "1:02:03.00" from an hour on.</summary>
        internal static string Clock(double seconds)
        {
            var hundredths = (long)Math.Round(Math.Max(0, seconds) * 100);
            var hours = hundredths / 360000;
            var minutes = hundredths / 6000 % 60;
            var rest = hundredths % 6000;
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", hours, minutes, rest / 100, rest % 100)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", minutes, rest / 100, rest % 100);
        }

        private void SetNoVideo()
        {
            _clip = null;
            _frames = 0;
            _infoLabel.Text = VideoToGifStrings.NoVideo;
            _summaryLabel.Text = string.Empty;
            FirstTimeLabel.Text = LastTimeLabel.Text = string.Empty;
            SetStatus(string.Empty);
            UpdateEnabled();
        }

        // ── The frames ──────────────────────────────────────────────────────

        /// <summary>Makes the range all of the new video.</summary>
        private void SetClip(GifClip clip)
        {
            _clip = clip;
            _frames = clip.SourceFrames;
            _syncing = true;
            try
            {
                var last = Math.Max(1, _frames);
                foreach (var (slider, number) in new[] { (FirstSlider, FirstNumeric), (LastSlider, LastNumeric) })
                {
                    slider.Minimum = 1;
                    slider.Maximum = last;
                    slider.SmallChange = 1;
                    slider.LargeChange = Math.Max(1, last / 20);
                    number.Minimum = 1;
                    number.Maximum = last;
                }

                FirstSlider.Value = 1;
                FirstNumeric.Value = 1;
                LastSlider.Value = last;
                LastNumeric.Value = last;
            }
            finally
            {
                _syncing = false;
            }

            _firstDirty = _lastDirty = true;
            UpdateRangeTexts();
            UpdateSummary();
            UpdateEnabled();
        }

        /// <summary>Moves the first frame (counted from 1); it cannot pass the last one, which stays where it is.</summary>
        internal void SetFirst(int frame)
        {
            if (_frames <= 0)
                return;

            var value = Math.Clamp(frame, 1, LastFrame);
            PutRange(first: value, last: LastFrame);
            _firstDirty = true;
            RangeChanged();
        }

        /// <summary>Moves the last frame (counted from 1); it cannot go before the first one, which stays where it is.</summary>
        internal void SetLast(int frame)
        {
            if (_frames <= 0)
                return;

            var value = Math.Clamp(frame, FirstFrame, _frames);
            PutRange(first: FirstFrame, last: value);
            _lastDirty = true;
            RangeChanged();
        }

        /// <summary>The range is all of the video again.</summary>
        internal void SelectAllFrames()
        {
            if (_frames <= 0)
                return;

            PutRange(1, _frames);
            _firstDirty = _lastDirty = true;
            RangeChanged();
        }

        // The two sliders and the two numbers hold the same two values: one that is changed puts the others right, without being called back
        private void PutRange(int first, int last)
        {
            var was = _syncing;
            _syncing = true;
            try
            {
                FirstSlider.Value = first;
                FirstNumeric.Value = first;
                LastSlider.Value = last;
                LastNumeric.Value = last;
            }
            finally
            {
                _syncing = was;
            }
        }

        private void RangeChanged()
        {
            UpdateRangeTexts();
            UpdateSummary();
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void UpdateRangeTexts()
        {
            if (_clip is null || _frames <= 0)
            {
                FirstTimeLabel.Text = LastTimeLabel.Text = string.Empty;
                return;
            }

            var rate = _clip.SourceFramesPerSecond;
            FirstTimeLabel.Text = string.Format(VideoToGifStrings.FrameOfFormat, _frames, Clock((FirstFrame - 1) / rate));
            LastTimeLabel.Text = string.Format(VideoToGifStrings.FrameOfFormat, _frames, Clock((LastFrame - 1) / rate));
        }

        private void UpdateSummary()
        {
            if (Plan is not { } plan)
            {
                _summaryLabel.Text = string.Empty;
                return;
            }

            _summaryLabel.Text = plan.FrameCount > 0 && plan.Width > 0
                ? string.Format(VideoToGifStrings.SummaryFormat, plan.FrameCount, plan.Width, plan.Height, string.Format(CultureInfo.CurrentCulture, VideoToGifStrings.SecondsFormat, plan.PlaySeconds),
                    Math.Abs(plan.Speed - 1) < 0.005 ? string.Empty : string.Format(CultureInfo.CurrentCulture, VideoToGifStrings.SpeedNoteFormat, plan.Speed))
                : VideoToGifStrings.SummaryUnknown;
        }

        /// <summary>Shows the pictures of the first and the last frame that were changed since they were shown. Done when they are there.</summary>
        internal async Task ShowFramesAsync()
        {
            _previewTimer.Stop();
            var work = new List<Task>();
            if (_firstDirty)
            {
                _firstDirty = false;
                work.Add(ShowFrameAsync(FirstPicture, FirstFrame, first: true));
            }

            if (_lastDirty)
            {
                _lastDirty = false;
                work.Add(ShowFrameAsync(LastPicture, LastFrame, first: false));
            }

            await Task.WhenAll(work);
        }

        private async Task ShowFrameAsync(PreviewBox box, int frame, bool first)
        {
            if (_video is not { } video || _clip is not { } clip)
                return;

            var generation = first ? ++_firstGeneration : ++_lastGeneration;
            var rate = clip.SourceFramesPerSecond;
            var seconds = (frame - 1) / rate;
            if (clip.DurationSeconds > 0)
                seconds = Math.Min(seconds, Math.Max(0, clip.DurationSeconds - 0.5 / rate)); // the last frame starts a frame before the end, and a place beyond it gives the first picture instead

            try
            {
                var bytes = await VideoPoster.ExtractAsync(video, seconds, PreviewLongSide, _lifetime.Token);
                if (IsDisposed || generation != (first ? _firstGeneration : _lastGeneration))
                    return;

                using var stream = new MemoryStream(bytes);
                using var decoded = new Bitmap(stream);
                var picture = new Bitmap(decoded);
                var old = box.Image;
                box.Image = picture;
                old?.Dispose();
            }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or TimeoutException or OperationCanceledException or ArgumentException)
            {
                // The picture that was there stays; the GIF can still be made
            }
        }

        // ── What can be changed when ────────────────────────────────────────

        private void WireEvents()
        {
            BrowseButton.Click += async (_, _) => await BrowseAsync();
            AllFramesButton.Click += (_, _) => SelectAllFrames();

            FirstSlider.ValueChanged += (_, _) => { if (!_syncing && !_loading) SetFirst(FirstSlider.Value); };
            FirstNumeric.ValueChanged += (_, _) => { if (!_syncing && !_loading) SetFirst((int)FirstNumeric.Value); };
            LastSlider.ValueChanged += (_, _) => { if (!_syncing && !_loading) SetLast(LastSlider.Value); };
            LastNumeric.ValueChanged += (_, _) => { if (!_syncing && !_loading) SetLast((int)LastNumeric.Value); };
            _previewTimer.Tick += async (_, _) => await ShowFramesAsync();

            // While the GIF is to play at the speed of the video, the time each frame plays is the distance the frames are taken at
            FrameEveryNumeric.ValueChanged += (_, _) =>
            {
                FollowVideoSpeed();
                SettingChanged();
            };
            VideoSpeedCheck.CheckedChanged += (_, _) =>
            {
                FollowVideoSpeed();
                UpdateEnabled();
                SettingChanged();
            };
            foreach (var number in new[] { FramePlaysNumeric, WidthNumeric, ColorsNumeric })
                number.ValueChanged += (_, _) => SettingChanged();
            OriginalCheck.CheckedChanged += (_, _) =>
            {
                UpdateEnabled();
                SettingChanged();
            };

            MakeButton.Click += async (_, _) => await MakeAsync();
            StopButton.Click += (_, _) => _making?.Cancel();
            CloseButton.Click += (_, _) => Close();
        }

        private void FollowVideoSpeed()
        {
            if (!_loading && VideoSpeedCheck.Checked)
                FramePlaysNumeric.Value = FrameEveryNumeric.Value;
        }

        private void SettingChanged()
        {
            if (_loading)
                return;

            UpdateSummary();
        }

        private void UpdateEnabled()
        {
            var working = IsMaking || IsReading;
            var ready = _clip is not null && !working;
            var range = ready && _frames > 1;

            BrowseButton.Enabled = !IsMaking;
            MakeButton.Enabled = ready;
            StopButton.Enabled = IsMaking;
            AllFramesButton.Enabled = range;
            FirstSlider.Enabled = FirstNumeric.Enabled = LastSlider.Enabled = LastNumeric.Enabled = range;

            FrameEveryNumeric.Enabled = VideoSpeedCheck.Enabled = ColorsNumeric.Enabled = DitherCombo.Enabled = RepeatCombo.Enabled = OriginalCheck.Enabled = !IsMaking;
            FramePlaysNumeric.Enabled = !IsMaking && !VideoSpeedCheck.Checked;
            WidthNumeric.Enabled = !IsMaking && !OriginalCheck.Checked;
        }

        // ── Making the GIF ──────────────────────────────────────────────────

        private string? AskPath()
        {
            var name = Path.GetFileNameWithoutExtension(_video) + ".gif";
            var folder = Path.GetDirectoryName(_video) ?? string.Empty;
            var filter = $"{VideoToGifStrings.FilterGif}|*.gif";
            if (AskSavePath is { } prompt)
                return prompt(VideoToGifStrings.SaveTitle, filter, folder, name);

            using var dialog = new SaveFileDialog
            {
                Title = VideoToGifStrings.SaveTitle,
                Filter = filter,
                InitialDirectory = Directory.Exists(folder) ? folder : string.Empty,
                FileName = name,
                OverwritePrompt = true,
                CheckPathExists = true,
                AddExtension = true,
                DefaultExt = "gif",
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        /// <summary>Asks where to save and makes the GIF of the part that is picked. True when a GIF was written.</summary>
        internal async Task<bool> MakeAsync()
        {
            if (_video is not { } video || _clip is not { } clip || IsMaking || IsReading)
                return false;

            var chosen = AskPath();
            if (chosen is null)
                return false;

            if (Path.GetExtension(chosen).Length == 0)
                chosen += ".gif";

            var settings = ReadSettings();
            var range = Range;
            var making = _making = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _progress.Value = 0;
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;
            SetStatus(VideoToGifStrings.StatusPalette);
            UpdateEnabled();
            var progress = new Progress<GifProgress>(report =>
            {
                if (!ReferenceEquals(_making, making) || IsDisposed)
                    return; // a report that comes after the work is over

                if (report.Phase == GifPhase.Palette)
                {
                    _progress.Style = ProgressBarStyle.Marquee;
                    SetStatus(VideoToGifStrings.StatusPalette);
                }
                else
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Value = Math.Clamp((int)(report.Fraction * _progress.Maximum), 0, _progress.Maximum);
                    SetStatus(string.Format(VideoToGifStrings.StatusFramesFormat, (int)Math.Round(report.Fraction * 100)));
                }
            });

            try
            {
                var result = await GifMaker.MakeAsync(video, chosen, clip, range, settings, progress, making.Token, FfmpegPath);
                if (IsDisposed)
                    return true;

                var info = GifInfo.Read(result.Path);
                ShowGif(result.Path); // first: it lets go of the GIF that was shown before, and of what was known about it
                Made = info;
                SavedPath = result.Path;
                settings.Save(_settingsFile);
                SetStatus(string.Format(VideoToGifStrings.StatusMadeFormat, Path.GetFileName(result.Path), info.FrameCount, info.Width, info.Height, SizeText(result.Bytes)));
                return true;
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    SetStatus(VideoToGifStrings.StatusStopped);
                return false;
            }
            catch (GifException ex)
            {
                if (!IsDisposed)
                    SetStatus(FailureText(ex), failure: true);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                if (!IsDisposed)
                    SetStatus(string.Format(VideoToGifStrings.StatusFailedFormat, ex.Message), failure: true);
                return false;
            }
            finally
            {
                _making = null;
                making.Dispose();
                if (!IsDisposed)
                {
                    _progress.Visible = false;
                    UpdateEnabled();
                }
            }
        }

        private static string FailureText(GifException error) => error.Reason switch
        {
            GifFailure.FfmpegNotFound => string.Format(VideoToGifStrings.StatusFfmpegMissingFormat, error.Detail),
            GifFailure.NoOutput => VideoToGifStrings.StatusNoOutput,
            _ => string.Format(VideoToGifStrings.StatusFailedFormat, error.Detail),
        };

        /// <summary>A size of a file as "812 KB" or "2.4 MB".</summary>
        internal static string SizeText(long bytes) => bytes >= 1024 * 1024
            ? string.Format(CultureInfo.CurrentCulture, VideoToGifStrings.SizeMegabytesFormat, bytes / (1024.0 * 1024.0))
            : string.Format(CultureInfo.CurrentCulture, VideoToGifStrings.SizeKilobytesFormat, Math.Max(1, bytes / 1024.0));

        // The GIF is read into memory and shown from there: a picture that is shown from a file holds the file open
        private void ShowGif(string path)
        {
            ReleaseGif();
            _gifStream = new MemoryStream(File.ReadAllBytes(path));
            GifPicture.Image = Image.FromStream(_gifStream);
        }

        private void ReleaseGif()
        {
            Made = null;
            var old = GifPicture.Image;
            GifPicture.Image = null;
            old?.Dispose();
            _gifStream?.Dispose();
            _gifStream = null;
        }

        private void ReleasePictures()
        {
            ReleaseGif();
            foreach (var box in new[] { FirstPicture, LastPicture })
            {
                var old = box.Image;
                box.Image = null;
                old?.Dispose();
            }

            _lifetime.Cancel(); // not disposed: a read that is still running may ask for its token
        }

        // ── Closing ─────────────────────────────────────────────────────────

        private void OnClosing()
        {
            _previewTimer.Stop();
            _making?.Cancel();
            ReadSettings().Save(_settingsFile);
        }
    }
}
