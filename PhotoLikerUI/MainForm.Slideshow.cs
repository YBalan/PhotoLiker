using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// Alt+Enter: the photos of the list as a slideshow on the whole screen the window is on, or on every screen (see <see cref="SlideshowSession"/>), beginning with the
    /// photo that is shown. How it runs is in the rows of the "6. Slideshow" category of the settings. When it ends the window shows the photo it ended on, on
    /// the screen of the window. With <see cref="Settings.SlideshowUseVideo"/> the videos of the list are played as well (on the screen of the window only).
    /// </summary>
    public partial class MainForm
    {
        private async void StartSlideshow()
        {
            // The video that plays in the window lets go of its file, and of the engine's picture, before the show has its own
            if (_player is { Visible: true })
            {
                HidePlayer();
                await _player.StopAsync();
            }

            // Videos in the show need the engine that plays them (it may have to be downloaded, after asking): without it the show has photos only
            var useVideo = _currentConfig.SlideshowUseVideo && _currentConfig.SupportVideo && _currentConfig.Files.Any(f => f.IsVideo);
            if (useVideo && !await EnsureVideoEngineAsync())
                useVideo = false;

            // The engine takes a while to start the first time (ten seconds after an install): it starts now, not while a black screen waits for the first video
            if (useVideo)
            {
                SetStatus(VideoPlayerStrings.StartingEngine);
                try
                {
                    await VideoPlayerPanel.EngineAsync();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    SetStatus(string.Format(VideoPlayerStrings.EngineFailedFormat, ex.Message.Split('\n')[0].Trim())); // the first line: the rest tells a developer to install a package
                    useVideo = false;
                }
            }

            if (CreateSlideshow(useVideo) is not { } slideshow)
            {
                SetStatus(SlideshowStrings.StatusNoPhotos);
                return;
            }

            using (slideshow)
            {
                slideshow.Run(this);
                KeepTheEffectThatWasChosen(slideshow.Effect);
                ShowWhereSlideshowEnded(slideshow.LastShownPath);
            }
        }

        /// <summary>Down and Up change the effect while the slideshow runs; the one it ended with is the setting from then on (it is saved with the others, and the grid shows it).</summary>
        private void KeepTheEffectThatWasChosen(SlideshowEffect effect)
        {
            if (_currentConfig.SlideshowEffect == effect)
                return;

            _currentConfig.SlideshowEffect = effect;
            settingsPropertyGrid.Refresh();
        }

        /// <summary>
        /// The slideshow of the list that is open (the photos of the folder, or the search results while they are shown); null if it has nothing to show. The videos of the
        /// list are in it only if <paramref name="useVideo"/> says so, else they are left out.
        /// </summary>
        private SlideshowSession? CreateSlideshow(bool useVideo)
        {
            var files = (useVideo ? _currentConfig.Files : _currentConfig.Files.Where(f => !f.IsVideo)).ToList();
            if (files.Count == 0)
                return null;

            var start = pictureBox1.Tag is string shown ? Math.Max(0, files.IndexOf(shown)) : 0;
            var (screens, primary) = SlideshowScreens();
            return new SlideshowSession(
                files, start, _currentConfig.SlideshowMode, _currentConfig.SlideshowTimeout, _currentConfig.SlideshowWatermark, screens, primary,
                _currentConfig.SlideshowEffect, useVideo ? SlideshowVideoSettings() : null);
        }

        /// <summary>
        /// How the videos of the show are played: the settings, and for each video what is known of it: the places the search that is shown found in it (none when the list is a
        /// folder), how long it is and when it was recorded (the catalog knows).
        /// </summary>
        private SlideshowVideoOptions SlideshowVideoSettings()
        {
            var records = new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase);
            if (VideoLibrary.Current is { } catalog)
            {
                foreach (var record in catalog.Records())
                    records[catalog.FullPathOf(record)] = record;
            }

            var hits = _resultsActive ? _resultHits : new Dictionary<string, SearchHit>(StringComparer.Ordinal);
            return new SlideshowVideoOptions(
                _currentConfig.SlideshowMuteVideo, _currentConfig.SlideshowVideoPart, _currentConfig.SlideshowLowerOtherAudio,
                path =>
                {
                    hits.TryGetValue(path, out var hit);
                    records.TryGetValue(path, out var record);
                    long? length = record?.Info is { DurationSeconds: > 0 } info ? (long)(info.DurationSeconds * 1000) : null;
                    var taken = hit?.Taken ?? record?.Date.Taken;
                    // A search by date alone lists videos without having found anything inside them (its score is 0): those are played whole
                    return new SlideshowVideoFacts(hit is { Score: > 0 } ? hit.Moments : null, length, taken);
                });
        }

        /// <summary>The screens the slideshow covers, left to right, and which of them the window is on: that one only, or every screen that is connected.</summary>
        private (Rectangle[] Bounds, int Primary) SlideshowScreens()
        {
            var here = Screen.FromControl(this);
            var screens = _currentConfig.SlideshowMonitors == SlideshowMonitors.All
                ? Screen.AllScreens.OrderBy(screen => screen.Bounds.Left).ThenBy(screen => screen.Bounds.Top).ToArray()
                : [here];

            var primary = Math.Max(0, Array.FindIndex(screens, screen => screen.DeviceName == here.DeviceName));
            return ([.. screens.Select(screen => screen.Bounds)], primary);
        }

        /// <summary>The photo that was on the screen last is the one to see now; the list may have changed while the slideshow ran, so it is looked up again.</summary>
        private void ShowWhereSlideshowEnded(string? file)
        {
            var files = _currentConfig.Files;
            var index = file is null ? -1 : files.IndexOf(file);
            if (file is not null && index >= 0)
            {
                if (!string.Equals(file, pictureBox1.Tag as string, StringComparison.Ordinal))
                    LoadImage(file);
                SetStatus(string.Format(SlideshowStrings.StatusEndedFormat, Path.GetFileName(file), index + 1, files.Count));
            }

            FocusViewer(); // the keys are for the photo again
        }
    }
}
