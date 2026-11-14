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
    /// F turns the boxes with the names on the faces of the people on and off, and N the line of their names in the corner (see <see cref="SlideshowPeopleOptions"/>): each on its own,
    /// on every screen, for the photo that is shown and the ones after it, and what the show ends with is what <see cref="ShowFaces"/> and <see cref="ShowNames"/> say.
    ///
    /// Any key but Esc (and Alt+Enter, which go back) pauses the show, and Space, which pauses a running one too, continues it. While it is paused nothing changes by
    /// itself: the timer is off, a video is held, and "Paused" is written in the top right corner of every screen; the keys still work (Right and Left browse, Down and Up choose
    /// the effect), the show stays paused after them, and Space gives the shown photo its full time again. A modifier alone (Shift, Ctrl, Alt) and Alt+F4 are no keys to pause by.
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

        private PhotoFile[] _photos;                           // what the show goes through, one step each: the photos, or the pages of the collage that are made of them (the videos are played in between, see below)
        private readonly SlideshowScreen[] _screens;           // left to right
        private readonly int _primary;                         // the place of the screen the main window is on: it has the photo that was shown there
        private int[] _first;                                  // the photos to begin with, one for each screen
        private SlideshowShuffle? _shuffle;                    // in the random mode (made again when the collage is changed)
        private readonly System.Windows.Forms.Timer _timer = new();
        private SlideshowEffect _effect;
        private SlideshowLook _look;                           // how the colours of the photos are graded now: the one the show began with, or the one L chose last
        private readonly CubeLut? _lut;                        // the table of the Lut look, when there is one
        private readonly PhotoFile[] _singles;                 // the photos one by one, in the order of the list: what the pages of the collage are made of
        private readonly int[] _videoAfterSingle;              // for each video, the photo (of _singles) it follows, -1 when it comes before the first
        private readonly Func<string, DateTime?>? _takenOf;    // when a photo was taken, for the events of the collage
        private DateTime?[]? _taken;                           // when each of _singles was taken: asked once, when a collage is first wanted
        private readonly SlideshowMode _mode;
        private SlideshowCollage _collage;                     // how many photos of one event are on a screen at once now: as the show began, or as the keys left it
        private IReadOnlyList<int[]> _pageGroups = [];         // for each of _photos, the photos of _singles it is made of
        private SlideshowBorder _border;                       // what the photos are put in now: as the show began, or as the B key left it
        private readonly double _timeoutSeconds;

        private readonly int[] _shown;                         // the place in the list of the photo on each screen, -1 before the first
        private int _direction = 1;                            // the way the person goes, 1 right and -1 left
        private int _steps;                                    // Right (+1) and Left (-1) keys not shown yet, up to MaxQueuedSteps either way
        private int[]? _jump;                                  // the photos the timer or the start asks for, while they are being made
        private bool _jumpByTimer;                             // _jump is the timer's, so its photos come with the effect (not the start's)
        private SlideshowEffect _lastEffect;                   // the effect of the last change, so a random one is not the same twice
        private bool _scrolling;                               // a key was used since the timer last picked random photos
        private bool _closing;
        private bool _paused;                                  // a key was pressed: nothing changes by itself until Space
        private SlideshowIndexing? _indexing;                  // what the indexing behind the show says now; null when none runs
        private long _playPauseAt;                            // when the last Play/Pause button was taken (0: none yet), so that one press that arrives twice is not taken twice
        private readonly bool _hasPeople;                      // there is something to ask who is in a photo (the folder has an index)
        private readonly SlideshowDepthOptions? _depth;        // how to work out how near the parts of a photo are (the Parallax effect); null when the depth model is not on this computer
        private bool _showFaces;                               // the boxes with the names on the faces are on (F)
        private bool _showNames;                               // the line of names in the corner is on (N)
        private bool _showCaptions;                            // the words said in a video are written over it (C)
        private bool _cursorHidden;

        // Videos
        private readonly SlideshowVideoOptions? _videoOptions;
        private VideoSlot[] _videos;                           // in the order of the list
        private List<int>[] _videosAfter;                      // for each photo: the videos that follow it in the list (those before the first photo follow the last one, as the list goes round)
        private readonly SlideshowShuffle? _videoShuffle;      // the random order of the videos
        private readonly int _startVideo;                      // the video the show begins with (the one the window shows), -1 if none
        private readonly Queue<int> _videoQueue = new();       // videos to play now, one after the other
        private bool _interlude;                               // videos are playing: what is wanted next (_jump) waits for them
        private int _videoNow = -1;                            // the video that is on the screen of the window, -1 if none
        private int _videoCursor;                              // the video played last (for a show of videos only)
        private int _quickFinishes;                            // videos in a row that ended at once: a show of videos only gives up when none of them can be played
        private long _videoStartedAt;
        private IDisposable? _ducking;                         // what puts the other programs' sound back
        private int _changes;                                  // sets of photos shown so far, and videos played, for the memory log (read on another thread)
        private int _videosPlayed;

        /// <summary>How the show is set up, and what it has done so far, as a few words for <see cref="SlideshowMemoryLog"/>. Safe to call from any thread.</summary>
        internal string Facts() => $"changes={Volatile.Read(ref _changes)} videos={Volatile.Read(ref _videosPlayed)}";

        /// <summary>The settings of the show as a few words, for <see cref="SlideshowMemoryLog"/>.</summary>
        internal string Describe() =>
            $"{_photos.Length} photos, {_videos.Length} videos, {_screens.Length} screen(s), mode {(_shuffle is null ? "OneByOne" : "Random")}, timeout {_timeoutSeconds} s, effect {_effect}, " +
            $"depth {(_depth is null ? "no" : "yes")}, video {(_videoOptions is null ? "off" : _videoOptions.Mute ? "muted" : "with sound")}, captions {(_showCaptions ? "on" : "off")}, names {(_showNames ? "on" : "off")}, faces {(_showFaces ? "on" : "off")}";

        /// <summary>A video, and the place it has among the photos: it follows the photo <paramref name="AfterPhoto"/> of the list of photos (-1: it comes before the first).</summary>
        private readonly record struct VideoSlot(PhotoFile File, int AfterPhoto);

        /// <param name="items">The photos, and the videos too when <paramref name="video"/> is given (without it they are left out), in the order of the list.</param>
        /// <param name="start">The place in <paramref name="items"/> of the photo (or video) that is shown in the main window.</param>
        /// <param name="screens">The bounds of the screens, left to right.</param>
        /// <param name="primary">Which of them the main window is on. With more screens than photos, those farthest from it are left out. The videos play on this one only.</param>
        /// <param name="video">How the videos are played; null leaves them out of the show.</param>
        /// <param name="people">Who is in a photo, and whether the boxes with the names on the faces (F) and the line of names (N) are on when the show begins; null when there is nobody to ask (the folder has no index), and then the two keys say so.</param>
        /// <param name="depth">How to work out the depth of a photo, for <see cref="SlideshowEffect.Parallax"/>; null when the depth model is not here, and then that effect is a Ken Burns one and the Down and Up keys skip it.</param>
        /// <param name="look">How the colours of the photos are graded, and the table of the Lut look (see <see cref="SlideshowLook"/>); null for the photos as they are, and no table.</param>
        public SlideshowSession(
            IReadOnlyList<PhotoFile> items, int start, SlideshowMode mode, double timeoutSeconds, SlideshowWatermark watermark,
            IReadOnlyList<Rectangle> screens, int primary, SlideshowEffect effect = SlideshowEffect.None, SlideshowVideoOptions? video = null,
            SlideshowPeopleOptions? people = null, SlideshowDepthOptions? depth = null, SlideshowStyleOptions? look = null)
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

            // The collage: photos of one event that follow each other in the list are shown together, one page at a time; what is counted from here on (the order, the random order, the photos a key moves
            // by, where a video comes) is pages. A video follows the page of the photo it followed, and the show begins with the page of the photo that was shown. The photos themselves are kept (they are
            // what pages are made of), so that the collage can be changed while the show runs (see ChangeCollage).
            _singles = [.. photos];
            _videoAfterSingle = [.. videos.Select(slot => slot.AfterPhoto)];
            _takenOf = look?.TakenOf;
            _mode = mode;
            _collage = look?.Collage ?? SlideshowCollage.None;
            var (pages, pageOf, groups) = Paginate(_collage);
            _photos = pages;
            _pageGroups = groups;
            _videos = [.. videos.Select(slot => slot with { AfterPhoto = slot.AfterPhoto >= 0 ? pageOf[slot.AfterPhoto] : -1 })];
            if (startPhoto >= 0 && startPhoto < photos.Count)
                startPhoto = pageOf[startPhoto];

            _videoOptions = _videos.Length > 0 ? video : null;
            startPhoto = _photos.Length == 0 ? 0 : startPhoto < 0 || startPhoto >= _photos.Length ? 0 : startPhoto;
            primary = Math.Clamp(primary, 0, screens.Count - 1);

            _videosAfter = VideosAfterEachPhoto();

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
            _screens = [.. chosen.Select(i => new SlideshowScreen(this, screens[i], watermark, frameBytes, MinFrames, parallelLoads, people?.FacesOf, depth?.Estimate))];
            _depth = depth;
            _hasPeople = people is not null;
            _showFaces = people?.ShowFaces ?? false;
            _showNames = people?.ShowNames ?? false;
            _showCaptions = _videoOptions?.ShowCaptions ?? false;
            foreach (var screen in _screens)
            {
                screen.Frames.Loaded += (_, _) => Advance();
                screen.Frames.LoadFailed += (_, _) => Advance();
                screen.VideoFinished += OnVideoFinished;
                screen.SetPeopleShown(_showFaces, _showNames);
                screen.SetCaptionsShown(_showCaptions);
            }

            _effect = effect;
            _look = look?.Look ?? SlideshowLook.None;
            _lut = look?.Lut;
            _border = look?.Border ?? SlideshowBorder.None;
            foreach (var screen in _screens)
            {
                screen.Frames.SetLook(_look, _lut);
                screen.Frames.SetBorder(_border);
            }
            WantDepthIfNeeded();
            WantPortraitIfNeeded();
            WantFacesIfNeeded();
            _timeoutSeconds = Slideshow.TimeoutOf(timeoutSeconds);
            _timer.Interval = (int)Math.Round(_timeoutSeconds * 1000);
            _timer.Tick += (_, _) => AdvanceByTimer();
        }

        public int ScreenCount => _screens.Length;

        /// <summary>The effect the show has now: the one it began with, or the one Down and Up chose last.</summary>
        public SlideshowEffect Effect => _effect;

        /// <summary>The look the photos are graded with now: the one the show began with, or the one the L key chose last.</summary>
        public SlideshowLook Look => _look;

        /// <summary>What the photos are put in now: as the show began, or as the B key left it.</summary>
        public SlideshowBorder Border => _border;

        /// <summary>Whether the boxes with the names on the faces are on now: as the show began, or as the F key left them.</summary>
        public bool ShowFaces => _showFaces;

        /// <summary>Whether the line of names in the corner is on now: as the show began, or as the N key left it.</summary>
        public bool ShowNames => _showNames;

        /// <summary>Whether the words said in a video are written over it now: as the show began, or as the C key left it. Null when the show has no videos (there is nothing to caption, and the setting is not touched).</summary>
        public bool? ShowCaptions => _videoOptions is null ? null : _showCaptions;

        /// <summary>The file of the photo (or video) that was on the screen of the main window last; null if none was shown.</summary>
        public string? LastShownPath =>
            _videoNow >= 0 ? _videos[_videoNow].File.OriginalFilePath
            : _photos.Length == 0 ? _videos[_videoCursor].File.OriginalFilePath
            : PathOf(_shown[_primary]);

        /// <summary>The file of the photo on each screen, left to right; null for one that has none yet.</summary>
        internal IReadOnlyList<string?> ShownPaths => [.. _shown.Select(PathOf)];

        internal IReadOnlyList<SlideshowScreen> Screens => _screens;

        private string? PathOf(int index) => index >= 0 ? _photos[index].OriginalFilePath : null;

        /// <summary>The key the frame of a photo or a page is kept under (see <see cref="FrameKeys"/>); null for none.</summary>
        private string? KeyOf(int index) => index >= 0 ? FrameKeys.Of(_photos[index]) : null;

        /// <summary>Shows the windows and waits until they are closed: the one on the screen of the main window is the dialog, the others are shown first, owned by the main window.</summary>
        public void Run(Form owner)
        {
            var primary = _screens[_primary];
            primary.Shown += (_, _) => Begin();

            Cursor.Hide(); // nothing to point at; Esc goes back
            _cursorHidden = true;

            // A show that is left to run is looked at without a hand on the keyboard or the mouse: the screen saver, the display going off and the sleep of the computer would all come
            // while it runs (a paused show is looked at as well). Asked for as long as the windows are there, on this thread, which is the one that gives it back
            using var awake = KeepAwake.Begin();
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

        /// <summary>
        /// What the indexing that runs behind the show says, or null when none runs (any more): it is written at the top of every screen, so that it is known that it runs and how far it
        /// is. When it ends while the show runs, the screens say so for a moment.
        /// </summary>
        internal void ShowIndexing(SlideshowIndexing? state)
        {
            var wasRunning = _indexing is not null;
            _indexing = state;
            if (_closing)
                return;

            foreach (var screen in _screens)
            {
                screen.SetIndexing(state);
                if (state is null && wasRunning)
                    screen.ShowNotice(SlideshowStrings.IndexingOver);
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

        /// <param name="repeat">The key is held down and this is one more of its repeats (the space key then is not pressed again).</param>
        internal bool HandleKey(Keys keyData, bool repeat = false)
        {
            switch (keyData)
            {
                case Keys.Escape:
                case Keys.Alt | Keys.Enter: // the key that started it: the same key goes back, as full screen does everywhere
                    Close();
                    return true;
            }

            // A modifier alone is only the beginning of a key (Alt of Alt+Enter), and Alt+F4 closes the window as Windows does it: none of them is a key to pause by
            var code = keyData & Keys.KeyCode;
            if (IsModifier(code) || keyData == (Keys.Alt | Keys.F4))
                return false;

            // Volume keys are the sound's, Play goes on, Pause stops (a keyboard, or a remote control that sends keys)
            var media = MediaKeys.FromKey((int)keyData);
            if (media != MediaCommand.None)
                return HandleMedia(media, repeat);

            // Space continues a show that was paused, and (being a key) pauses one that runs; a held key does not flicker between the two
            if (code == Keys.Space && (keyData & Keys.Alt) == 0)
            {
                if (!repeat)
                {
                    if (_paused)
                        Resume();
                    else
                        Pause();
                }
                return true;
            }

            // Any other key: the show stands still, and then does what the key says (Right and Left browse, with the show paused). The keys that only change how the show looks leave it as it is: it goes on, or
            // stays paused if it was
            if (!KeepsTheShowGoing(keyData))
                Pause();

            switch (keyData)
            {
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
                case Keys.F:
                case Keys.F | Keys.Shift: // Caps Lock, or a finger that slipped
                    if (!repeat)
                        TogglePeople(faces: true);
                    return true;
                case Keys.N:
                case Keys.N | Keys.Shift:
                    if (!repeat)
                        TogglePeople(faces: false);
                    return true;
                case Keys.C:
                case Keys.C | Keys.Shift:
                    if (!repeat)
                        ToggleCaptions();
                    return true;
                case Keys.L:
                case Keys.L | Keys.Shift:
                    if (!repeat)
                        ChangeLook();
                    return true;
                case Keys.B:
                case Keys.B | Keys.Shift:
                    if (!repeat)
                        ToggleBorder();
                    return true;
                case Keys.D1:
                case Keys.NumPad1:
                    if (!repeat)
                        ChangeCollage(SlideshowCollage.None, toggle: false);
                    return true;
                case Keys.D2:
                case Keys.NumPad2:
                    if (!repeat)
                        ChangeCollage(SlideshowCollage.Two);
                    return true;
                case Keys.D3:
                case Keys.NumPad3:
                    if (!repeat)
                        ChangeCollage(SlideshowCollage.Three);
                    return true;
                case Keys.D4:
                case Keys.NumPad4:
                    if (!repeat)
                        ChangeCollage(SlideshowCollage.Four);
                    return true;
                case Keys.M:
                case Keys.M | Keys.Shift:
                    if (!repeat)
                        ChangeCollage(SlideshowCollage.Mixed);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The keys that change how the show looks and do not stop it: Down and Up (the effect), F and N (the faces and the names), C (the captions), L (the look), B (the card), and 1, 2, 3, 4 (also on the number pad)
        /// and M (the collage). The letters may come with Shift (a Caps Lock, or a finger that slipped), the others come alone. Every other key pauses the show.
        /// </summary>
        internal static bool KeepsTheShowGoing(Keys keyData)
        {
            var modifiers = keyData & Keys.Modifiers;
            return (keyData & Keys.KeyCode) switch
            {
                Keys.Down or Keys.Up or Keys.D1 or Keys.D2 or Keys.D3 or Keys.D4 or Keys.NumPad1 or Keys.NumPad2 or Keys.NumPad3 or Keys.NumPad4 => modifiers == Keys.None,
                Keys.F or Keys.N or Keys.C or Keys.L or Keys.B or Keys.M => modifiers is Keys.None or Keys.Shift,
                _ => false,
            };
        }

        /// <summary>
        /// An application command of the system (WM_APPCOMMAND), which is how a remote control such as that of Windows Media Center usually asks for Play, Pause and the volume.
        /// True if the show took it (then nobody else gets it); false if it is none of the show's business, or the system's, like the volume, and goes on its way.
        /// </summary>
        internal bool HandleAppCommand(int appCommand)
        {
            var media = MediaKeys.FromAppCommand(appCommand);
            return media != MediaCommand.None && HandleMedia(media, repeat: false);
        }

        /// <summary>
        /// Play goes on with a paused show (and leaves a running one as it is), Pause stops it, and the one button for both does what Space does. The volume is the system's: the show does
        /// not pause by it and does not take it, so the sound still changes. One press of the one button can come to the window twice (as a key and as an application command), so a second
        /// one within a fraction of a second is the same press.
        /// </summary>
        private bool HandleMedia(MediaCommand media, bool repeat)
        {
            switch (media)
            {
                case MediaCommand.Volume:
                    return false;
                case MediaCommand.Play:
                    Resume();
                    return true;
                case MediaCommand.Pause:
                    Pause();
                    return true;
                case MediaCommand.PlayPause:
                    var now = Stopwatch.GetTimestamp();
                    var same = _playPauseAt != 0 && Stopwatch.GetElapsedTime(_playPauseAt, now) < SamePress;
                    _playPauseAt = now;
                    if (!repeat && !same)
                    {
                        if (_paused)
                            Resume();
                        else
                            Pause();
                    }
                    return true;
                default:
                    return false;
            }
        }

        private static readonly TimeSpan SamePress = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// F / N: the boxes with the names on the faces, or the line of names in the corner, are turned on if they were off and off if they were on, on every screen, and the person is told
        /// which and how they are now. The photo that is shown gets them (or loses them) at once. Without anything to ask who is in a photo (the folder has no index) there is nothing to turn
        /// on, and the person is told so.
        /// </summary>
        private void TogglePeople(bool faces)
        {
            if (!_hasPeople)
            {
                foreach (var screen in _screens)
                    screen.ShowNotice(SlideshowStrings.NoPeopleToShow);
                return;
            }

            if (faces)
                _showFaces = !_showFaces;
            else
                _showNames = !_showNames;

            var text = faces
                ? _showFaces ? SlideshowStrings.FacesOn : SlideshowStrings.FacesOff
                : _showNames ? SlideshowStrings.NamesOn : SlideshowStrings.NamesOff;
            foreach (var screen in _screens)
            {
                screen.SetPeopleShown(_showFaces, _showNames);
                screen.ShowNotice(text);
            }
        }

        /// <summary>
        /// C: the words said in a video are written over it if they were not, and not if they were, on every screen (the video that plays gets or loses its caption at once), and the person is
        /// told which. A show without videos has nothing to caption, and the person is told so.
        /// </summary>
        private void ToggleCaptions()
        {
            if (_videoOptions is null)
            {
                foreach (var screen in _screens)
                    screen.ShowNotice(SlideshowStrings.NoCaptionsToShow);
                return;
            }

            _showCaptions = !_showCaptions;
            var text = _showCaptions ? SlideshowStrings.CaptionsOn : SlideshowStrings.CaptionsOff;
            foreach (var screen in _screens)
            {
                screen.SetCaptionsShown(_showCaptions);
                screen.ShowNotice(text);
            }
        }

        private static bool IsModifier(Keys code) => code is Keys.None or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.ControlKey or Keys.LControlKey
            or Keys.RControlKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin or Keys.CapsLock or Keys.NumLock or Keys.Scroll;

        /// <summary>Whether the show is paused: nothing changes by itself (the timer is off, a video is held), and the keys still work.</summary>
        internal bool IsPaused => _paused;

        /// <summary>
        /// Stops the show where it is: the timer is off, so no photo takes the place of the shown one, and a video that plays is held. A change that the timer had asked for and that is
        /// still waiting for its photos is called off, so the person is not given a photo they did not look at; one that waits for a video is not (the photos come after the
        /// video, and with them the timer, so that is not stuck).
        /// </summary>
        private void Pause()
        {
            if (_paused || _closing)
                return;

            _paused = true;
            _timer.Stop();
            if (_jumpByTimer && !_interlude)
            {
                _jump = null;
                _jumpByTimer = false;
            }

            foreach (var screen in _screens)
                screen.SetPaused(true);
        }

        /// <summary>Goes on: the shown photo has its full time again from now, and a video that was held plays on.</summary>
        private void Resume()
        {
            if (!_paused)
                return;

            _paused = false;
            foreach (var screen in _screens)
                screen.SetPaused(false);

            // A video goes on and the photos that wait for it come after it; photos that are being made start the timer when they are shown
            if (!_interlude && _jump is null && _steps == 0)
                RestartTimer();
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
            var next = SlideshowEffects.Next(_effect, direction);
            while (SlideshowEffects.UsesDepth(next) && _depth is null && next != _effect)
                next = SlideshowEffects.Next(next, direction); // there is no depth model: those are not offered
            _effect = next;
            WantDepthIfNeeded();
            WantPortraitIfNeeded();
            WantFacesIfNeeded();

            var text = string.Format(SlideshowStrings.EffectFormat, _effect);
            foreach (var screen in _screens)
                screen.ShowNotice(text);

            if (SlideshowEffects.HasMotion(_effect))
            {
                // The photo that is on the screen has none yet: it begins to move now, not at the next change (by its depth if it has it, else as Ken Burns)
                var plan = PlanEffect(byTimer: false);
                foreach (var screen in _screens)
                    screen.StartMotion(plan);
            }
        }

        /// <summary>The effect is one that moves photos by their depth: the frames work the depth out as they are made, and for the ones that are here already.</summary>
        private void WantDepthIfNeeded()
        {
            if (_depth is null || !SlideshowEffects.UsesDepth(_effect))
                return;

            foreach (var screen in _screens)
                screen.Frames.SetDepthWanted(true);
        }

        /// <summary>
        /// L: the next look (see <see cref="LookGrade.Next"/>), from now on; the person is told which, on every screen. The photos that are made already are made again with it (not the ones on the screen:
        /// they change to it with the next change of photo).
        /// </summary>
        internal void ChangeLook()
        {
            _look = LookGrade.Next(_look, _lut is not null);
            foreach (var screen in _screens)
            {
                screen.Frames.SetLook(_look, _lut);
                screen.ShowNotice(string.Format(SlideshowStrings.LookFormat, _look));
            }
        }

        /// <summary>B: the card of <see cref="SlideshowBorder.Polaroid"/> is put round the photos if it was not, and taken away if it was, from now on; the person is told which, on every screen.</summary>
        internal void ToggleBorder()
        {
            _border = _border == SlideshowBorder.None ? SlideshowBorder.Polaroid : SlideshowBorder.None;
            foreach (var screen in _screens)
            {
                screen.Frames.SetBorder(_border);
                screen.ShowNotice(string.Format(SlideshowStrings.BorderFormat, _border));
            }
        }

        /// <summary>
        /// Divides the photos into the pages of <paramref name="collage"/> (see <see cref="CollagePlan"/>): the pages the show goes through (a page of one photo is that photo), which page each photo is on,
        /// and the photos of each page. With no collage, or fewer than two photos, every photo is a page.
        /// </summary>
        private (PhotoFile[] Pages, int[] PageOf, IReadOnlyList<int[]> Groups) Paginate(SlideshowCollage collage)
        {
            if (collage == SlideshowCollage.None || _singles.Length < 2)
            {
                var own = new int[_singles.Length];
                var alone = new int[_singles.Length][];
                for (var i = 0; i < own.Length; i++)
                {
                    own[i] = i;
                    alone[i] = [i];
                }
                return ([.. _singles], own, alone);
            }

            _taken ??= [.. _singles.Select(photo => _takenOf?.Invoke(photo.OriginalFilePath))];
            var groups = CollagePlan.Pages(_taken, collage);
            var pageOf = new int[_singles.Length];
            var pages = new PhotoFile[groups.Count];
            for (var page = 0; page < groups.Count; page++)
            {
                foreach (var index in groups[page])
                    pageOf[index] = page;
                pages[page] = groups[page].Length == 1 ? _singles[groups[page][0]] : new CollagePage([.. groups[page].Select(index => _singles[index])]);
            }
            return (pages, pageOf, groups);
        }

        /// <summary>For each page: the videos that follow it in the list (those before the first photo follow the last page, as the list goes round).</summary>
        private List<int>[] VideosAfterEachPhoto()
        {
            var after = new List<int>[Math.Max(1, _photos.Length)];
            for (var i = 0; i < after.Length; i++)
                after[i] = [];
            if (_photos.Length == 0)
                return after;

            for (var v = 0; v < _videos.Length; v++)
            {
                if (_videos[v].AfterPhoto >= 0)
                    after[_videos[v].AfterPhoto].Add(v);
            }
            for (var v = 0; v < _videos.Length; v++)
            {
                if (_videos[v].AfterPhoto < 0)
                    after[^1].Add(v); // the list goes round: what is before the first photo is after the last
            }
            return after;
        }

        /// <summary>How many photos of one event are on a screen at once now: as the show began, or as the 1, 2, 3, 4 and M keys left it.</summary>
        public SlideshowCollage Collage => _collage;

        /// <summary>
        /// 2, 3, 4 and M: the collage is changed from now on (two, three, four, or three, two and four in turn), and the key of the collage that is on takes it away; 1 takes it away at once. The photos are divided
        /// into pages again and the show goes on from the page that holds the photo the person is looking at, the other screens following it as at the start; the new pages come with the effect, as soon as they
        /// are ready (the frames of the old ones stay until then). The person is told which collage it is. A show that can not have it (fewer than two photos, or fewer pages than screens) says so and is left as it is.
        /// </summary>
        /// <param name="toggle">The key of the collage that is on takes it away (false: the collage asked for is the one that comes, also if it is on).</param>
        internal void ChangeCollage(SlideshowCollage requested, bool toggle = true)
        {
            var target = toggle && requested == _collage ? SlideshowCollage.None : requested;
            var (pages, pageOf, groups) = Paginate(target);
            if (_photos.Length == 0 || _singles.Length < 2 || pages.Length < _screens.Length)
            {
                foreach (var screen in _screens)
                    screen.ShowNotice(SlideshowStrings.CollageNotPossible);
                return;
            }

            // What the screens show now, in the numbers of the new pages (taken before the old pages are let go of): the page that holds the first photo of what is shown
            var onScreen = _shown[_primary] >= 0 ? _shown[_primary] : _first[_primary];
            var anchor = pageOf[_pageGroups[onScreen][0]];
            var shown = _shown.Select(place => place >= 0 ? pageOf[_pageGroups[place][0]] : -1).ToArray();

            _collage = target;
            _photos = pages;
            _pageGroups = groups;
            for (var v = 0; v < _videos.Length; v++)
                _videos[v] = _videos[v] with { AfterPhoto = _videoAfterSingle[v] >= 0 ? pageOf[_videoAfterSingle[v]] : -1 };
            _videosAfter = VideosAfterEachPhoto();
            _shuffle = _mode == SlideshowMode.Random ? new SlideshowShuffle(_photos.Length, shown: anchor) : null;
            _first = FirstPhotos(anchor, _screens.Length);
            Array.Copy(shown, _shown, shown.Length);

            // What is wanted now: the pages that stand where the shown photos are, by the effect (the change is the timer's kind: it dissolves). The timer starts again when they are there.
            _timer.Stop();
            _steps = 0;
            _scrolling = false;
            _direction = 1;
            _jump = (int[])_first.Clone();
            _jumpByTimer = true;

            var text = string.Format(SlideshowStrings.CollageFormat, _collage);
            foreach (var screen in _screens)
                screen.ShowNotice(text);

            Prefetch();
            Advance();
        }

        /// <summary>
        /// The effect is the portrait one (see <see cref="SlideshowEffect.Portrait"/>), or it is not any more: the frames are made with the blur or without it, and the frames that are here are made again the other
        /// way (a photo that was blurred keeps no original). Said every time the effect may have changed, it does something only when it did.
        /// </summary>
        private void WantPortraitIfNeeded()
        {
            var wanted = _depth is not null && SlideshowEffects.BlursByDepth(_effect);
            foreach (var screen in _screens)
                screen.Frames.SetPortraitWanted(wanted);
        }

        /// <summary>The effect drifts the photos with their faces in view: the frames work the faces out as they are made (the effect can be changed while the show runs, so it is said every time).</summary>
        private void WantFacesIfNeeded()
        {
            var wanted = _hasPeople && SlideshowEffects.AimsAtFaces(_effect);
            foreach (var screen in _screens)
                screen.SetFacesWantedForMotion(wanted);
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
                if (!_screens[screen].Frames.TryGet(FrameKeys.Of(_photos[photos[screen]]), out var frame))
                    return false;
                frames[screen] = frame;
            }
            return true;
        }

        /// <summary>A photo that one screen could not read is not tried by the others: it is the same file.</summary>
        private bool IsUnreadable(int index) => _screens.Any(screen => screen.Frames.HasFailed(FrameKeys.Of(_photos[index])));

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
            Interlocked.Increment(ref _changes);

            Prefetch(); // the photos that were shown before can go now
            RestartTimer();
        }

        /// <summary>What the screens do with the photos of this change: one plan for all of them, with one clock, so that they change in step.</summary>
        private SlideshowShowPlan PlanEffect(bool byTimer)
        {
            if (_effect == SlideshowEffect.None)
                return default;

            var effect = SlideshowEffects.Resolve(_effect, _lastEffect, Random.Shared.Next);
            if (SlideshowEffects.UsesDepth(effect) && _depth is null)
                effect = SlideshowEffect.KenBurns; // no depth model: the nearest thing there is
            _lastEffect = effect;

            var transition = byTimer ? SlideshowEffects.TransitionOf(effect) : SlideshowTransition.None;
            var seconds = SlideshowEffects.TransitionSeconds(effect, _timeoutSeconds);
            return new SlideshowShowPlan(
                transition, transition == SlideshowTransition.None ? 0 : seconds,
                SlideshowEffects.HasMotion(effect), SlideshowEffects.MotionSeconds(_timeoutSeconds, seconds),
                Stopwatch.GetTimestamp(), SlideshowEffects.MovesByDepth(effect));
        }

        private void RestartTimer()
        {
            if (_photos.Length < 2 || _paused)
                return; // one photo stays; and so does every photo while the show is paused (Resume starts the timer)

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
                // (what the screen has on it is kept by what it says it has: after the collage was changed the numbers of the show are those of the new pages, and the screen still shows an old one)
                _screens[screen].Frames.SetWindow([.. order.Select(index => _photos[index])], _screens[screen].CurrentPath ?? KeyOf(_shown[screen]), _screens[screen].OutgoingPath);
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
            _screens[_primary].ShowVideo(file, segments, options.Mute, facts.Taken, facts.Speech); // the screen writes the words when captions are on (C turns them on and off while the video plays)
        }

        private void OnVideoFinished(SlideshowScreen screen)
        {
            if (_closing || !_interlude || !ReferenceEquals(screen, _screens[_primary]))
                return;

            // A video that was over at once could not be played; when that is all that happens (a show of videos only) it ends
            var seconds = (Stopwatch.GetTimestamp() - _videoStartedAt) / (double)Stopwatch.Frequency;
            Interlocked.Increment(ref _videosPlayed);
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
