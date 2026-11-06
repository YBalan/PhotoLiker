using System.Diagnostics;

namespace PhotoLikerUI
{
    /// <summary>
    /// The slideshow: the photos of the list one after the other on the whole of one screen, or of every screen at once (see <see cref="SlideshowMonitors"/>), each
    /// in a window of its own (<see cref="SlideshowScreen"/>). Alt+Enter and Esc go back to the main window, and with it every window of the slideshow.
    ///
    /// With several screens every screen shows a photo of its own and they change together. A new set of photos is shown once all of them are ready, so the
    /// screens never change one after the other, and never show the same photo at one time. In the random mode every screen takes the next photo of the same random
    /// order (<see cref="SlideshowShuffle"/>). In the one-by-one mode the screens, left to right, show photos that follow each other, as the pages of a book
    /// laid out in a row, and the next set begins where this one ends. With one screen both are what they always were.
    ///
    /// Right and Left show the photo next to the one on a screen, whatever the mode is (in the random mode only the timer picks at random): on every screen at
    /// once, so the row of photos moves a place, and the time starts over. Two things make them quick. The photos are ready before they are asked for
    /// (<see cref="SlideshowFrames"/>): the few next to the shown one of each screen, more of them in the direction the person is going, and the next random ones,
    /// which are decided ahead. And a key that comes while the photos it wants are still being made is kept (up to <see cref="MaxQueuedSteps"/>) and shown when they are
    /// there, the nearest set that is ready first, rather than skipped, so holding a key scrolls as fast as the photos can be made and never freezes.
    ///
    /// The effect (<see cref="SlideshowEffect"/>) is chosen here and carried out by the screens: the timer's change of photos is the one that dissolves, slides
    /// or zooms (the same way on all the screens, at the same moment); a key puts the photo there at once, because whoever presses it wants to get on, but
    /// the photo moves if the effect is Ken Burns. A random effect is drawn anew for every change. Down and Up choose the next and the previous effect while the
    /// show runs: the name is written in a corner of every screen for a moment, and the next change is made by it (a Ken Burns photo starts to move at once).
    ///
    /// Videos (when <see cref="SlideshowVideoOptions"/> are given) are not part of the photos: the photos go on exactly as above, and a video is played in between, on
    /// the screen of the main window only (<see cref="_primary"/>); the other screens keep their photos until it is over, and the show goes on with the next set. In the
    /// one-by-one mode a video is played where it lies among the photos (after the photo it follows in the list, when the screen of the window passes it); in the random mode
    /// videos are drawn in as often as they are among the items (each once before any comes again). A list of videos only is played one after the other, on that screen alone.
    /// What of a video is played (the whole, or the places a search found) and the sound (muted, or the others turned down) is in <see cref="SlideshowVideoOptions"/>.
    /// </summary>
    internal sealed class SlideshowSession : IDisposable
    {
        private const long MaxFrameBytes = 512L * 1024 * 1024; // of screen-sized frames kept ready, all the screens together
        private const int MinFrames = 3;                       // kept on every screen whatever they weigh (an 8K screen has 130 MB frames)
        private const int MaxParallelLoads = 4;                // photos made at a time, all the screens together (not fewer than 2 for a screen)
        private const int PrefetchAhead = 6;                   // photos made ready in the direction the person goes ...
        private const int PrefetchBehind = 2;                  // ... and on the other side
        private const int PrefetchRandom = 3;                  // the next sets of photos of the random order
        private const int MaxQueuedSteps = 3;                  // keys kept while their photos are being made

        private readonly PhotoFile[] _photos;                  // the photos only: the videos are played in between (see below)
        private readonly SlideshowScreen[] _screens;           // left to right
        private readonly int _primary;                         // the place of the screen the main window is on: it has the photo that was shown there
        private readonly int[] _first;                         // the photos to begin with, one for each screen
        private readonly SlideshowShuffle? _shuffle;           // in the random mode
        private readonly System.Windows.Forms.Timer _timer = new();
        private SlideshowEffect _effect;
        private readonly double _timeoutSeconds;

        private readonly int[] _shown;                         // the place in the list of the photo on each screen, -1 before the first
        private int _direction = 1;                            // the way the person goes, 1 right and -1 left
        private int _steps;                                    // Right (+1) and Left (-1) keys not shown yet, up to MaxQueuedSteps either way
        private int[]? _jump;                                  // the photos the timer or the start asks for, while they are being made
        private bool _jumpByTimer;                             // _jump is the timer's, so its photos come with the effect (not the start's)
        private SlideshowEffect _lastEffect;                   // the effect of the last change, so a random one is not the same twice
        private bool _scrolling;                               // a key was used since the timer last picked random photos
        private bool _closing;
        private bool _cursorHidden;

        // Videos
        private readonly SlideshowVideoOptions? _videoOptions;
        private readonly VideoSlot[] _videos;                  // in the order of the list
        private readonly List<int>[] _videosAfter;             // for each photo: the videos that follow it in the list (those before the first photo follow the last one, as the list goes round)
        private readonly SlideshowShuffle? _videoShuffle;      // the random order of the videos
        private readonly int _startVideo;                      // the video the show begins with (the one the window shows), -1 if none
        private readonly Queue<int> _videoQueue = new();       // videos to play now, one after the other
        private bool _interlude;                               // videos are playing: what is wanted next (_jump) waits for them
        private int _videoNow = -1;                            // the video that is on the screen of the window, -1 if none
        private int _videoCursor;                              // the video played last (for a show of videos only)
        private int _quickFinishes;                            // videos in a row that ended at once: a show of videos only gives up when none of them can be played
        private long _videoStartedAt;
        private IDisposable? _ducking;                         // what puts the other programs' sound back

        /// <summary>A video, and the place it has among the photos: it follows the photo <paramref name="AfterPhoto"/> of the list of photos (-1: it comes before the first).</summary>
        private readonly record struct VideoSlot(PhotoFile File, int AfterPhoto);

        /// <param name="items">The photos, and the videos too when <paramref name="video"/> is given (without it they are left out), in the order of the list.</param>
        /// <param name="start">The place in <paramref name="items"/> of the photo (or video) that is shown in the main window.</param>
        /// <param name="screens">The bounds of the screens, left to right.</param>
        /// <param name="primary">Which of them the main window is on. With more screens than photos, those farthest from it are left out. The videos play on this one only.</param>
        /// <param name="video">How the videos are played; null leaves them out of the show.</param>
        public SlideshowSession(
            IReadOnlyList<PhotoFile> items, int start, SlideshowMode mode, double timeoutSeconds, SlideshowWatermark watermark,
            IReadOnlyList<Rectangle> screens, int primary, SlideshowEffect effect = SlideshowEffect.None, SlideshowVideoOptions? video = null)
        {
            ArgumentOutOfRangeException.ThrowIfZero(items.Count);
            ArgumentOutOfRangeException.ThrowIfZero(screens.Count);

            // The photos and the videos apart; a video knows the photo it follows, and the show begins with the photo that follows the item that is shown
            var photos = new List<PhotoFile>();
            var videos = new List<VideoSlot>();
            start = Math.Clamp(start, 0, items.Count - 1);
            var startPhoto = -1;
            _startVideo = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].IsVideo)
                {
                    if (video is not null)
                        videos.Add(new VideoSlot(items[i], photos.Count - 1));
                    if (i == start)
                    {
                        startPhoto = photos.Count; // the photo that comes after it
                        _startVideo = video is not null ? videos.Count - 1 : -1;
                    }
                }
                else
                {
                    if (i == start)
                        startPhoto = photos.Count;
                    photos.Add(items[i]);
                }
            }
            ArgumentOutOfRangeException.ThrowIfZero(photos.Count + videos.Count);

            _photos = [.. photos];
            _videos = [.. videos];
            _videoOptions = _videos.Length > 0 ? video : null;
            startPhoto = _photos.Length == 0 ? 0 : startPhoto < 0 || startPhoto >= _photos.Length ? 0 : startPhoto;
            primary = Math.Clamp(primary, 0, screens.Count - 1);

            _videosAfter = new List<int>[Math.Max(1, _photos.Length)];
            for (var i = 0; i < _videosAfter.Length; i++)
                _videosAfter[i] = [];
            if (_photos.Length > 0)
            {
                for (var v = 0; v < _videos.Length; v++)
                {
                    if (_videos[v].AfterPhoto >= 0)
                        _videosAfter[_videos[v].AfterPhoto].Add(v);
                }
                for (var v = 0; v < _videos.Length; v++)
                {
                    if (_videos[v].AfterPhoto < 0)
                        _videosAfter[^1].Add(v); // the list goes round: what is before the first photo is after the last
                }
            }

            var chosen = Slideshow.ChooseScreens(screens.Count, primary, Math.Max(1, _photos.Length)); // videos alone: the screen of the window only
            _primary = Array.IndexOf(chosen, primary);
            _shown = new int[chosen.Length];
            Array.Fill(_shown, -1);

            _shuffle = mode == SlideshowMode.Random && _photos.Length > 0 ? new SlideshowShuffle(_photos.Length, shown: startPhoto) : null;
            _videoShuffle = mode == SlideshowMode.Random && _videos.Length > 1 ? new SlideshowShuffle(_videos.Length, shown: Math.Max(-1, _startVideo)) : null;
            _videoCursor = Math.Max(0, _startVideo);
            _first = _photos.Length > 0 ? FirstPhotos(startPhoto, chosen.Length) : [];

            var frameBytes = MaxFrameBytes / chosen.Length;
            var parallelLoads = Math.Max(2, MaxParallelLoads / chosen.Length);
            _screens = [.. chosen.Select(i => new SlideshowScreen(this, screens[i], watermark, frameBytes, MinFrames, parallelLoads))];
            foreach (var screen in _screens)
            {
                screen.Frames.Loaded += (_, _) => Advance();
                screen.Frames.LoadFailed += (_, _) => Advance();
                screen.VideoFinished += OnVideoFinished;
            }

            _effect = effect;
            _timeoutSeconds = Slideshow.TimeoutOf(timeoutSeconds);
            _timer.Interval = (int)Math.Round(_timeoutSeconds * 1000);
            _timer.Tick += (_, _) => AdvanceByTimer();
        }

        public int ScreenCount => _screens.Length;

        /// <summary>The effect the show has now: the one it began with, or the one Down and Up chose last.</summary>
        public SlideshowEffect Effect => _effect;

        /// <summary>The file of the photo (or video) that was on the screen of the main window last; null if none was shown.</summary>
        public string? LastShownPath =>
            _videoNow >= 0 ? _videos[_videoNow].File.OriginalFilePath
            : _photos.Length == 0 ? _videos[_videoCursor].File.OriginalFilePath
            : PathOf(_shown[_primary]);

        /// <summary>The file of the photo on each screen, left to right; null for one that has none yet.</summary>
        internal IReadOnlyList<string?> ShownPaths => [.. _shown.Select(PathOf)];

        internal IReadOnlyList<SlideshowScreen> Screens => _screens;

        private string? PathOf(int index) => index >= 0 ? _photos[index].OriginalFilePath : null;

        /// <summary>Shows the windows and waits until they are closed: the one on the screen of the main window is the dialog, the others are shown first, owned by the main window.</summary>
        public void Run(Form owner)
        {
            var primary = _screens[_primary];
            primary.Shown += (_, _) => Begin();

            Cursor.Hide(); // nothing to point at; Esc goes back
            _cursorHidden = true;
            try
            {
                foreach (var screen in _screens.Where(screen => screen != primary))
                    screen.Show(owner);

                primary.ShowDialog(owner);
            }
            finally
            {
                ShowCursor();
            }
        }

        private void ShowCursor()
        {
            if (_cursorHidden)
                Cursor.Show(); // Hide and Show count: every Hide needs its Show
            _cursorHidden = false;
        }

        /// <summary>Starts with the photo that was shown in the window (and the others that go with it).</summary>
        internal void Begin()
        {
            if (_photos.Length == 0)
            {
                PlayVideos([_videoCursor]); // videos only
                return;
            }

            _jump = _first;
            Prefetch();
            if (_startVideo >= 0)
            {
                PlayVideos([_startVideo]); // the video that was shown in the window comes first; the photos that wait begin when it is over
                return;
            }
            Advance();
        }

        /// <summary>The photos the screens begin with: the shown photo on the screen of the window, and on the others what goes with it by the mode.</summary>
        private int[] FirstPhotos(int start, int screens)
        {
            var first = new int[screens];
            if (_shuffle is null)
            {
                // The row of photos is laid out so that the shown one stays where the window is
                return Slideshow.Strip(Slideshow.Wrap(start - _primary, _photos.Length), screens, _photos.Length);
            }

            first[_primary] = start;
            for (var screen = 0; screen < screens; screen++)
            {
                if (screen == _primary)
                    continue;

                var placed = screen;
                first[screen] = _shuffle.Take(place => place == start || Array.IndexOf(first, place, 0, placed) >= 0);
            }
            return first;
        }

        // ── keys ───────────────────────────────────────────────────────────

        internal bool HandleKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                case Keys.Alt | Keys.Enter: // the key that started it: the same key goes back, as full screen does everywhere
                    Close();
                    return true;
                case Keys.Right:
                    Step(1);
                    return true;
                case Keys.Left:
                    Step(-1);
                    return true;
                case Keys.Down: // down the drop-down of the Effect row
                    ChangeEffect(1);
                    return true;
                case Keys.Up:
                    ChangeEffect(-1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Goes back to the main window: every window of the slideshow is closed.</summary>
        internal void Close()
        {
            if (_closing)
                return;

            CloseAllBut(null);
        }

        /// <summary>A window was closed (Alt+F4 on one of them): the others go with it.</summary>
        internal void ScreenClosed(SlideshowScreen closed)
        {
            if (!_closing)
                CloseAllBut(closed);
        }

        private void CloseAllBut(SlideshowScreen? closed)
        {
            _closing = true;
            _timer.Stop();
            ReleaseDucking();

            // The dialog last: when it closes the main window gets back control
            foreach (var screen in _screens.Where(screen => screen != closed && screen != _screens[_primary]))
                CloseScreen(screen);
            if (_screens[_primary] != closed)
                CloseScreen(_screens[_primary]);
        }

        private static void CloseScreen(SlideshowScreen screen)
        {
            if (!screen.IsDisposed)
                screen.Close();
        }

        /// <summary>Right / Left: the photo next to the shown one on every screen, in either mode.</summary>
        internal void Step(int direction)
        {
            if (_photos.Length == 0)
            {
                StepVideos(direction); // videos only: the next or the previous one
                return;
            }

            if (_interlude)
            {
                // The person takes over from the video: it stops, and the photos that were waiting for it are shown (that is the step: the video is skipped)
                StopInterlude();
                if (_jump is not null)
                {
                    Advance();
                    return;
                }
            }

            if (_photos.Length < 2 || _shown.Any(place => place < 0))
                return;

            _direction = direction;
            _scrolling = true;
            _jump = null; // the person takes over from the timer
            _jumpByTimer = false;
            _steps = Math.Clamp(_steps + direction, -MaxQueuedSteps, MaxQueuedSteps);
            _timer.Stop(); // the photos that come start it again
            Prefetch();    // the way may have changed
            Advance();

            if (_steps == 0 && _jump is null)
                RestartTimer(); // Right and Left cancelled each other: nothing new comes to start it
        }

        /// <summary>Down / Up: the next / previous effect, from now on; the person is told which, on every screen.</summary>
        internal void ChangeEffect(int direction)
        {
            _effect = SlideshowEffects.Next(_effect, direction);

            var text = string.Format(SlideshowStrings.EffectFormat, _effect);
            foreach (var screen in _screens)
                screen.ShowNotice(text);

            if (_effect == SlideshowEffect.KenBurns)
            {
                // The photo that is on the screen has none yet: it begins to move now, not at the next change
                var plan = PlanEffect(byTimer: false);
                foreach (var screen in _screens)
                    screen.StartMotion(plan);
            }
        }

        // ── what comes next ────────────────────────────────────────────────

        private void AdvanceByTimer()
        {
            _timer.Stop(); // the photos that come start it again
            _steps = 0;
            _jumpByTimer = true;

            if (_shuffle is not null)
            {
                _scrolling = false;
                _jump = NextRandomPhotos();
            }
            else
            {
                _direction = 1;
                _jump = NextRow();
            }

            Prefetch();
            if (VideosBefore(_jump) is { Count: > 0 } videos && _videoOptions is not null)
            {
                PlayVideos(videos); // the photos that were asked for wait until they are over
                return;
            }
            Advance();
        }

        /// <summary>The next photos of the random order, one for each screen, in the order of the screens. No photo twice at one time, and none that the screen has now.</summary>
        private int[] NextRandomPhotos()
        {
            var next = new int[_screens.Length];
            for (var screen = 0; screen < next.Length; screen++)
            {
                var placed = screen;
                next[screen] = _shuffle!.Take(place => place == _shown[placed] || Array.IndexOf(next, place, 0, placed) >= 0);
            }
            return next;
        }

        /// <summary>The row of photos that follows the one on the screens: it begins with the photo after the last screen's.</summary>
        private int[] NextRow() =>
            Slideshow.Strip(Slideshow.Move(_shown[^1], _photos.Length, 1, IsUnreadable), _screens.Length, _photos.Length, IsUnreadable);

        /// <summary>
        /// Shows what is wanted (the photos the timer asked for, or the Right and Left keys that came) as far as it is ready. It is called again whenever
        /// a frame is made or fails, so what was not ready comes onto the screens the moment it is.
        /// </summary>
        private void Advance()
        {
            if (_closing || _interlude)
                return; // while a video plays what is wanted waits

            for (var attempts = 0; _jump is { } jump; attempts++)
            {
                if (TryGetFrames(jump, out var frames))
                {
                    var byTimer = _jumpByTimer;
                    _jump = null;
                    _jumpByTimer = false;
                    ShowFrames(jump, frames, byTimer);
                    return;
                }

                if (!jump.Any(IsUnreadable))
                    return; // still being made

                if (attempts >= _photos.Length)
                {
                    GiveUp(); // all of them were tried
                    return;
                }

                _jump = Replacement(jump);
                Prefetch(); // the new ones first
            }

            while (_steps != 0 && TryStep())
            {
            }
        }

        /// <summary>The photos asked for with a photo in them that can not be read: others in its place, the rest as they are.</summary>
        private int[] Replacement(int[] jump)
        {
            if (_shuffle is null)
                return Slideshow.Strip(jump[0], _screens.Length, _photos.Length, IsUnreadable);

            var next = (int[])jump.Clone();
            for (var screen = 0; screen < next.Length; screen++)
            {
                if (!IsUnreadable(next[screen]))
                    continue;

                var placed = screen;
                next[screen] = _shuffle.Take(place => place == _shown[placed] || Array.IndexOf(next, place) >= 0);
            }
            return next;
        }

        /// <summary>
        /// Shows the photos that are as many steps away as the keys that came, or the nearest ones before them that are ready: the person asked for them
        /// in this order, so none of the ones in between is lost, and none is waited for.
        /// </summary>
        private bool TryStep()
        {
            var direction = Math.Sign(_steps);
            for (var steps = Math.Abs(_steps); steps > 0; steps--)
            {
                var photos = PhotosAfter(direction * steps);
                if (photos.SequenceEqual(_shown) || !TryGetFrames(photos, out var frames))
                    continue;

                _steps -= direction * steps;
                ShowFrames(photos, frames);
                return true;
            }
            return false;
        }

        /// <summary>The photos that are that many places on from the shown ones: the row moves, or in the random mode every screen goes to its neighbour.</summary>
        private int[] PhotosAfter(int steps) => _shuffle is null
            ? Slideshow.Strip(Slideshow.Move(_shown[0], _photos.Length, steps, IsUnreadable), _screens.Length, _photos.Length, IsUnreadable)
            : [.. _shown.Select(place => Slideshow.Move(place, _photos.Length, steps, IsUnreadable))];

        private bool TryGetFrames(int[] photos, out SlideshowFrame[] frames)
        {
            frames = new SlideshowFrame[photos.Length];
            for (var screen = 0; screen < photos.Length; screen++)
            {
                if (!_screens[screen].Frames.TryGet(_photos[photos[screen]].OriginalFilePath, out var frame))
                    return false;
                frames[screen] = frame;
            }
            return true;
        }

        /// <summary>A photo that one screen could not read is not tried by the others: it is the same file.</summary>
        private bool IsUnreadable(int index) => _screens.Any(screen => screen.Frames.HasFailed(_photos[index].OriginalFilePath));

        /// <summary>No photos can be had: say so if the screens are empty, otherwise stay on the ones that are there.</summary>
        private void GiveUp()
        {
            _jump = null;
            if (_screens.All(screen => !screen.HasPhoto))
            {
                foreach (var screen in _screens)
                    screen.ShowMessage(SlideshowStrings.NothingToShow);
            }
            else
            {
                RestartTimer();
            }
        }

        /// <param name="byTimer">The change is the timer's: the photos take each other's place by the effect. Else (a key, the start) they are just there.</param>
        private void ShowFrames(int[] photos, SlideshowFrame[] frames, bool byTimer = false)
        {
            var plan = PlanEffect(byTimer);
            for (var screen = 0; screen < photos.Length; screen++)
            {
                _shown[screen] = photos[screen];
                _screens[screen].ShowFrame(frames[screen], plan);
            }

            Prefetch(); // the photos that were shown before can go now
            RestartTimer();
        }

        /// <summary>What the screens do with the photos of this change: one plan for all of them, with one clock, so that they change in step.</summary>
        private SlideshowShowPlan PlanEffect(bool byTimer)
        {
            if (_effect == SlideshowEffect.None)
                return default;

            var effect = SlideshowEffects.Resolve(_effect, _lastEffect, Random.Shared.Next);
            _lastEffect = effect;

            var transition = byTimer ? SlideshowEffects.TransitionOf(effect) : SlideshowTransition.None;
            var seconds = SlideshowEffects.TransitionSeconds(effect, _timeoutSeconds);
            return new SlideshowShowPlan(
                transition, transition == SlideshowTransition.None ? 0 : seconds,
                SlideshowEffects.HasMotion(effect), SlideshowEffects.MotionSeconds(_timeoutSeconds, seconds),
                Stopwatch.GetTimestamp());
        }

        private void RestartTimer()
        {
            if (_photos.Length < 2)
                return; // one photo stays

            _timer.Stop();
            _timer.Start();
        }

        /// <summary>Tells the frames of every screen which photos to have ready, the one that is wanted first at the front.</summary>
        private void Prefetch()
        {
            var count = _photos.Length;
            var screens = _screens.Length;

            // In the random mode the neighbours are there for the keys only, and every new photo has new neighbours (on every screen): until a key is
            // used there are just the two next to the photo, or the one after it when there are several screens, then they follow the way the person goes
            var full = _shuffle is null || _scrolling;
            var ahead = full ? PrefetchAhead : 1;
            var behind = full ? PrefetchBehind : screens > 1 ? 0 : 1;

            // The next sets of the random order, which the screens take in turns: the screen at place s gets every screens'th of them from s
            var upcoming = _shuffle?.Upcoming(Math.Min(PrefetchRandom, count) * screens);

            for (var screen = 0; screen < screens; screen++)
            {
                var center = _shown[screen] >= 0 ? _shown[screen] : _jump?[screen] ?? _first[screen];
                var order = new List<int>();
                var seen = new HashSet<int>();

                void Add(int index)
                {
                    if (seen.Add(index))
                        order.Add(index);
                }

                if (_jump is { } jump)
                    Add(jump[screen]);
                Add(center);

                var neighbours = new List<int>();
                for (var distance = 1; distance <= ahead; distance++)
                {
                    neighbours.Add(Slideshow.Wrap(center + _direction * distance, count));
                    if (distance <= behind)
                        neighbours.Add(Slideshow.Wrap(center - _direction * distance, count));
                }

                // What the timer will ask for: in the random mode the planned photos; with several screens in the one-by-one mode, a row on
                var coming = new List<int>();
                if (upcoming is not null)
                {
                    for (var round = 0; round < PrefetchRandom && screen + round * screens < upcoming.Count; round++)
                        coming.Add(upcoming[screen + round * screens]);
                }
                else if (screens > 1)
                {
                    for (var round = 1; round <= PrefetchRandom; round++)
                        coming.Add(Slideshow.Wrap(center + round * screens, count));
                }

                // Taken in turns: a key may come any moment, and so may the timer
                for (var i = 0; i < Math.Max(neighbours.Count, coming.Count); i++)
                {
                    if (i < neighbours.Count)
                        Add(neighbours[i]);
                    if (i < coming.Count)
                        Add(coming[i]);
                }

                // The photo that is leaving while an effect lasts is still being painted: it stays as well
                _screens[screen].Frames.SetWindow([.. order.Select(index => _photos[index])], PathOf(_shown[screen]), _screens[screen].OutgoingPath);
            }
        }

        // ── videos ─────────────────────────────────────────────────────────

        /// <summary>
        /// The videos to play before the photos that are wanted (<paramref name="jump"/>) are shown. One-by-one: those that lie between the photo on the screen of the window and
        /// the one it goes to (in the list). Random: one now and then, so that they come as often as they are among the items, each before any comes again.
        /// </summary>
        private List<int> VideosBefore(int[] jump)
        {
            var found = new List<int>();
            if (_videoOptions is null || _videos.Length == 0 || _photos.Length == 0)
                return found;

            if (_shuffle is not null)
            {
                var share = _videos.Length / (_videos.Length + _photos.Length / (double)_screens.Length);
                if (Random.Shared.NextDouble() < share)
                    found.Add(_videoShuffle?.Take(avoid: _videoCursor) ?? 0);
                return found;
            }

            var from = _shown[_primary];
            var to = jump[_primary];
            if (from < 0 || to < 0)
                return found;

            if (_photos.Length == 1)
            {
                found.AddRange(Enumerable.Range(0, _videos.Length)); // the one photo is all there is to pass: every video once
                return found;
            }

            var place = from;
            for (var passed = 0; place != to && passed < _photos.Length; passed++)
            {
                found.AddRange(_videosAfter[place]); // the videos that follow each photo the screen goes past
                place = (place + 1) % _photos.Length;
            }
            return found;
        }

        /// <summary>Plays the videos <paramref name="order"/> on the screen of the window, one after the other.</summary>
        private void PlayVideos(IEnumerable<int> order)
        {
            if (_videoOptions is null)
                return;

            _videoQueue.Clear();
            foreach (var index in order)
                _videoQueue.Enqueue(index);
            _interlude = true;
            _timer.Stop();
            PlayNextVideo();
        }

        private void PlayNextVideo()
        {
            if (_closing)
                return;
            if (_videoQueue.Count == 0)
            {
                EndInterlude();
                return;
            }

            var options = _videoOptions!;
            var index = _videoQueue.Dequeue();
            _videoNow = _videoCursor = index;

            var file = _videos[index].File.OriginalFilePath;
            var facts = options.FactsOf(file);
            var segments = SlideshowVideo.Segments(options.Part, facts.Found, facts.LengthMs);

            // The others' sound is turned down for as long as videos play with theirs (and put back when they are over)
            if (_ducking is null && !options.Mute && SlideshowVideo.LowerOtherAudioOf(options.LowerOtherAudio) > 0)
            {
                var level = SlideshowVideo.LevelOfOthers(options.LowerOtherAudio);
                _ducking = options.Duck is { } duck ? duck(level) : AudioDucking.Lower(level);
            }

            _videoStartedAt = Stopwatch.GetTimestamp();
            _screens[_primary].ShowVideo(file, segments, options.Mute, facts.Taken);
        }

        private void OnVideoFinished(SlideshowScreen screen)
        {
            if (_closing || !_interlude || !ReferenceEquals(screen, _screens[_primary]))
                return;

            // A video that was over at once could not be played; when that is all that happens (a show of videos only) it ends
            var seconds = (Stopwatch.GetTimestamp() - _videoStartedAt) / (double)Stopwatch.Frequency;
            _quickFinishes = seconds < 1 ? _quickFinishes + 1 : 0;
            _videoNow = -1;

            if (_photos.Length == 0 && _quickFinishes > _videos.Length)
            {
                _interlude = false;
                ReleaseDucking();
                screen.ShowMessage(SlideshowStrings.NothingToShow);
                return;
            }
            PlayNextVideo();
        }

        /// <summary>The videos that were to be played are over: the photos that waited are shown, or in a show of videos only the next video is played.</summary>
        private void EndInterlude()
        {
            ReleaseDucking();
            _videoNow = -1;
            _interlude = false;
            if (_closing)
                return;

            if (_photos.Length == 0)
            {
                PlayVideos([NextVideoOf(_videoCursor, 1)]);
                return;
            }
            Advance();
        }

        /// <summary>A key (or the end of the show) takes over from the videos: what plays stops, and none of the queue is played.</summary>
        private void StopInterlude()
        {
            _videoQueue.Clear();
            _screens[_primary].StopVideo();
            ReleaseDucking();
            _videoNow = -1;
            _interlude = false;
        }

        /// <summary>A show of videos only: Right and Left play the next and the previous video.</summary>
        private void StepVideos(int direction)
        {
            if (_videos.Length == 0)
                return;

            var target = _videoNow >= 0 || _interlude ? NextVideoOf(_videoCursor, direction) : _videoCursor;
            StopInterlude();
            PlayVideos([target]);
        }

        /// <summary>The video after (or before) <paramref name="from"/>: the next of the list, or a random one in the random mode.</summary>
        private int NextVideoOf(int from, int direction) =>
            _videoShuffle is not null && direction > 0 ? _videoShuffle.Take(avoid: from) : Slideshow.Wrap(from + direction, _videos.Length);

        private void ReleaseDucking()
        {
            _ducking?.Dispose();
            _ducking = null;
        }

        public void Dispose()
        {
            _closing = true;
            ReleaseDucking();
            ShowCursor();
            _timer.Dispose();
            foreach (var screen in _screens)
                screen.Dispose(); // the frames on the screens go with them
        }
    }
}
