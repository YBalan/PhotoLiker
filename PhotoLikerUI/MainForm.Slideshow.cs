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
        private const int SlideshowIndexingMs = 250;  // how often the show is told how far the indexing is (it reports for every photo)

        private SlideshowSession? _slideshow;         // the show that is on the screen, if one is; what happens behind it (the indexing) is told to it
        private SlideshowIndexing? _indexingForSlideshow; // what the indexing that runs says, as the show writes it; null when none runs
        private long _slideshowToldAt;                // when the show was told last
        private IndexPhase? _slideshowToldPhase;      // the phase it was told of

        /// <summary>
        /// The indexing has a new report: it is kept for a show that begins later and told to the one that runs (at most four times a second, or at once when the phase changes,
        /// since what the line says changes with it).
        /// </summary>
        private void TellSlideshowAboutIndexing(IndexProgress progress)
        {
            _indexingForSlideshow = SlideshowIndexing.Of(progress);
            if (_slideshow is not { } show)
                return;

            var now = Environment.TickCount64;
            if (_slideshowToldPhase == progress.Phase && now - _slideshowToldAt < SlideshowIndexingMs)
                return;

            _slideshowToldAt = now;
            _slideshowToldPhase = progress.Phase;
            show.ShowIndexing(_indexingForSlideshow);
        }

        /// <summary>The indexing is over (done, stopped or failed): the show that runs takes the bar away and says so for a moment.</summary>
        private void TellSlideshowIndexingIsOver()
        {
            _indexingForSlideshow = null;
            _slideshowToldPhase = null;
            _slideshow?.ShowIndexing(null);
        }

        private bool _slideshowStarting;              // a show is being started, or is on the screen: the key that starts one is not asked twice

        /// <summary>
        /// Alt+Enter. The start is not at once (the video that plays lets go of its file, the engine of the videos and the depth model of the Parallax effect are looked at first), and the key comes
        /// again in that time: it repeats while it is held, and a person presses it twice. A second show on top of the first would have to be ended one Esc at a time and would work the photos
        /// twice, so while one is on its way, or on the screen, the key is not taken as a request for another.
        /// </summary>
        private async void StartSlideshow()
        {
            if (_slideshowStarting)
                return;

            _slideshowStarting = true;
            try
            {
                await RunSlideshowAsync();
            }
            finally
            {
                _slideshowStarting = false;
            }
        }

        private async Task RunSlideshowAsync()
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

            // The Parallax effect needs the depth model (it is asked for once; without it the show moves its photos like Ken Burns does)
            if (SlideshowEffects.UsesDepth(_currentConfig.SlideshowEffect) && !await EnsureToolModelAsync(ToolModels.Depth, ToolModelStrings.ParallaxFeature, askAgain: false))
                SetStatus(ToolModelStrings.StatusParallaxWithoutModel);

            // The depth model is given to the show when its file is the one that was tested, also when the show starts with another effect (Down can change to Parallax while it runs): the file is looked
            // at once a run (see IsTestedFileAsync), so this costs nothing after the first show
            var depthTested = _effects.DepthDownloaded && await IsTestedFileAsync(ToolModels.Depth);

            if (CreateSlideshow(useVideo, depthTested) is not { } slideshow)
            {
                SetStatus(SlideshowStrings.StatusNoPhotos);
                return;
            }

            using (slideshow)
            {
                _slideshow = slideshow;
                slideshow.ShowIndexing(_indexingForSlideshow); // an indexing that was running when the show began is on the screen from the start
                try
                {
                    slideshow.Run(this);
                }
                finally
                {
                    _slideshow = null;
                }

                KeepTheEffectThatWasChosen(slideshow.Effect);
                KeepThePeopleThatWereChosen(slideshow.ShowFaces, slideshow.ShowNames);
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
        private SlideshowSession? CreateSlideshow(bool useVideo, bool depthTested)
        {
            var files = (useVideo ? _currentConfig.Files : _currentConfig.Files.Where(f => !f.IsVideo)).ToList();
            if (files.Count == 0)
                return null;

            var start = pictureBox1.Tag is string shown ? Math.Max(0, files.IndexOf(shown)) : 0;
            var (screens, primary) = SlideshowScreens();
            return new SlideshowSession(
                files, start, _currentConfig.SlideshowMode, _currentConfig.SlideshowTimeout, _currentConfig.SlideshowWatermark, screens, primary,
                _currentConfig.SlideshowEffect, useVideo ? SlideshowVideoSettings() : null, SlideshowPeopleOptionsOfTheFolder(),
                depthTested ? SlideshowDepthOptions.Of(_effects) : null);
        }

        /// <summary>
        /// The people of the photos of the show: the faces the search found in a photo, each with the name it is known by, as the main window names them (the folder's index is in memory,
        /// so asking is quick, and it is done along with the making of the frame, not when the photo is painted), and the two rows <b>PeopleFaces</b> and <b>PeopleNames</b> as the
        /// starting state of the F and N keys. Null when there is no index to ask (a photo that is not in the index has no faces).
        /// </summary>
        private SlideshowPeopleOptions? SlideshowPeopleOptionsOfTheFolder() =>
            _folderSearch is { } search
                ? new SlideshowPeopleOptions(search.DescribeFaces, _currentConfig.SlideshowPeopleFaces, _currentConfig.SlideshowPeopleNames)
                : null;

        /// <summary>F and N turn the boxes and the names on and off while the slideshow runs; how they were left when it ended is the setting from then on (like the effect).</summary>
        private void KeepThePeopleThatWereChosen(bool faces, bool names)
        {
            if (_currentConfig.SlideshowPeopleFaces == faces && _currentConfig.SlideshowPeopleNames == names)
                return;

            _currentConfig.SlideshowPeopleFaces = faces;
            _currentConfig.SlideshowPeopleNames = names;
            settingsPropertyGrid.Refresh();
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
