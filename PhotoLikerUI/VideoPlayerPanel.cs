using System.Globalization;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// Plays a video in the viewer: the picture (VLC's engine, which plays whatever a camera or a phone writes), and under it a bar with play and pause, where
    /// the video is, a slider to go anywhere in it, a box to type a time to jump to, and the volume. The keys (play, step, jump, volume) are the main window's
    /// (<see cref="MainForm"/> handles them whichever control has the focus); the engine takes neither keys nor mouse. It starts the engine on first use, in
    /// the background (the first start after an install can take ten seconds while the virus scanner looks at its files), and says in the bar that it does.
    /// </summary>
    internal sealed class VideoPlayerPanel : Panel
    {
        private const int BarHeight = 38;

        private static readonly object EngineGate = new();
        private static Task<LibVLC>? _engine;
        private static int _volume = 80; // kept for the next video: a person who turned it down wants it down
        private static bool _muted;

        private readonly VideoView _view = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
        private readonly Panel _bar = new() { Dock = DockStyle.Bottom, Height = BarHeight };
        private readonly Label _caption = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true, Visible = false };
        private readonly Button _play = new() { Text = "▶", FlatStyle = FlatStyle.Flat, TabStop = false, Size = new Size(34, 28) };
        private readonly Label _time = new() { Text = "0:00 / 0:00", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Width = 124 }; // room for 1:02:03 / 1:30:00
        private readonly SeekBar _seek = new();
        private readonly Label _goLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Width = 52 };
        private readonly TextBox _go = new() { Width = 64, BorderStyle = BorderStyle.FixedSingle };
        private readonly Button _mute = new() { Text = "\U0001F50A", FlatStyle = FlatStyle.Flat, TabStop = false, Size = new Size(34, 28) };
        private readonly SeekBar _volumeBar = new() { Width = 84, Maximum = 100 };
        private readonly ToolTip _tips = new() { ShowAlways = true };
        private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };

        private MediaPlayer? _player;
        private int _generation;
        private bool _stopping;
        private bool _failedShown;

        public VideoPlayerPanel()
        {
            _play.FlatAppearance.BorderSize = 0;
            _mute.FlatAppearance.BorderSize = 0;
            _play.Font = _mute.Font = new Font("Segoe UI Symbol", 11f);

            _bar.Controls.AddRange([_play, _time, _seek, _goLabel, _go, _mute, _volumeBar]);
            _caption.Font = new Font(Font.FontFamily, 11f);
            Controls.Add(_view);
            Controls.Add(_caption); // above the bar: what is said just now
            Controls.Add(_bar);

            _volumeBar.Value = _volume;
            _seek.HoverText = at => SaidAt?.Invoke(at) is { Length: > 0 } said ? VideoTime.Format(at) + " - " + said : VideoTime.Format(at);
            _volumeBar.HoverText = at => $"{at} %";

            _play.Click += (_, _) => { TogglePause(); Used?.Invoke(); };
            _mute.Click += (_, _) => { ToggleMute(); Used?.Invoke(); };
            _seek.Seek += at => SeekTo(at);
            _seek.Used += () => Used?.Invoke();
            _volumeBar.Seek += at => SetVolume((int)at);
            _volumeBar.Used += () => Used?.Invoke();
            // A click on the picture pauses and plays (the engine takes no mouse: the click reaches the controls that hold its window)
            _view.MouseDown += PictureClicked;
            foreach (Control inner in _view.Controls)
                inner.MouseDown += PictureClicked;
            _go.KeyDown += GoKeyDown;
            _go.KeyPress += (_, e) => { if (e.KeyChar is '\r' or (char)27) e.Handled = true; }; // no beep
            _bar.Resize += (_, _) => LayoutBar();
            _tick.Tick += (_, _) => Tick();

            ApplyTexts();
            LayoutBar();
        }

        /// <summary>The person used the bar: the owner gives the keyboard back to the viewer.</summary>
        public event Action? Used;

        /// <summary>The video cannot be played. <c>EngineProblem</c>: the player itself could not start (nothing can be played).</summary>
        public event Action<string, bool>? Failed;

        /// <summary>The text typed into the go-to box is not a time.</summary>
        public event Action<string>? NotATime;

        /// <summary>The video is at this place (milliseconds); raised a few times a second while it plays or is paused.</summary>
        public event Action<long>? TimeChanged;

        /// <summary>What is said in the video just now, in a line under the picture; none hides the line.</summary>
        public void SetCaption(string? text)
        {
            text ??= string.Empty;
            if (_caption.Text != text)
                _caption.Text = text;
            if (_caption.Visible != (text.Length > 0))
                _caption.Visible = text.Length > 0;
        }

        /// <summary>Marks places on the slider (the moments a search found), or none; <paramref name="spoken"/> are the places among them that were found in what is said (teal ticks).</summary>
        public void SetMarks(IReadOnlyList<long>? places, IReadOnlyList<long>? spoken = null)
        {
            _seek.Marks = places ?? [];
            _seek.SpokenMarks = spoken ?? []; // the ones among them that were found in what is said, in another colour
        }

        /// <summary>The words that are said around a place in the video (milliseconds), to show with its time over the slider; none when nothing is known.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Func<long, string?>? SaidAt { get; set; }

        /// <summary>The video that is shown (or being started); null when none.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? CurrentFile { get; private set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsPlaying => _player is { State: VLCState.Playing };

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public long TimeMs => _player is { } p && !_stopping ? Math.Max(0, p.Time) : 0;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public long LengthMs => _player is { } p && !_stopping ? Math.Max(0, p.Length) : 0;

        /// <summary>The texts are in the language that is on; called again when it changes.</summary>
        public void ApplyTexts()
        {
            _tips.SetToolTip(_play, VideoPlayerStrings.PlayTooltip);
            _tips.SetToolTip(_go, VideoPlayerStrings.GoToTooltip);
            _tips.SetToolTip(_mute, VideoPlayerStrings.MuteTooltip);
            _tips.SetToolTip(_volumeBar, VideoPlayerStrings.VolumeTooltip);
            _goLabel.Text = VideoPlayerStrings.GoToLabel;
            _goLabel.Width = Math.Max(40, TextRenderer.MeasureText(_goLabel.Text, _goLabel.Font).Width + 10); // "Перейти до" is longer than "Go to"
            LayoutBar();
        }

        /// <summary>Whether the engine is there to be started: it comes with the program or has been downloaded (<see cref="VlcEngine"/>).</summary>
        public static bool EngineInstalled => VlcEngine.Find() is not null;

        /// <summary>Starts the engine without playing anything, so that the first video does not wait for it (it takes half a second, much more the first time after an install). Does nothing while the engine is not there.</summary>
        public static void WarmUp()
        {
            if (EngineInstalled)
                _ = StartEngine().ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>The engine, started if it was not (the slideshow plays its videos with the same one). Fails if the engine is not there.</summary>
        internal static Task<LibVLC> EngineAsync() => StartEngine();

        private static Task<LibVLC> StartEngine()
        {
            lock (EngineGate)
            {
                // A start that failed (the engine was not there yet) is not kept: the next one tries again, with the engine that has been downloaded since
                if (_engine is { IsFaulted: true })
                    _engine = null;

                return _engine ??= Task.Run(() =>
                {
                    var folder = VlcEngine.Find() ?? throw new FileNotFoundException(VideoPlayerStrings.EngineNotInstalled);
                    Core.Initialize(folder);
                    return new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--no-sub-autodetect-file", "--quiet");
                });
            }
        }

        // ── playing ────────────────────────────────────────────────────────

        /// <summary>Plays the video from <paramref name="startMs"/> (a video that is playing is replaced).</summary>
        public async Task PlayAsync(string path, long startMs = 0)
        {
            var generation = ++_generation;
            CurrentFile = path;
            _failedShown = false;
            _time.Text = VideoPlayerStrings.StartingEngine;

            LibVLC engine;
            try
            {
                engine = await StartEngine();
            }
            catch (Exception ex)
            {
                if (generation == _generation && !IsDisposed)
                    Failed?.Invoke(string.Format(VideoPlayerStrings.EngineFailedFormat, ex.Message.Split('\n')[0].Trim()), true); // the first line: the rest tells a developer to install a package
                return;
            }

            if (generation != _generation || IsDisposed)
                return; // another video was asked for, or the window closed, while the engine started

            try
            {
                var player = EnsurePlayer(engine);
                using var media = new Media(engine, path, FromType.FromPath);
                if (startMs > 0)
                    media.AddOption(":start-time=" + (startMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));

                _stopping = false;
                _seek.Maximum = 0;
                _seek.Value = 0;
                if (!player.Play(media))
                    throw new InvalidOperationException(path);

                player.Volume = _volume;
                player.Mute = _muted;
                _tick.Start();
                UpdateButtons();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(string.Format(VideoPlayerStrings.PlayFailedFormat, ex.Message), false);
            }
        }

        private MediaPlayer EnsurePlayer(LibVLC engine)
        {
            if (_player is not null)
                return _player;

            var player = new MediaPlayer(engine) { EnableKeyInput = false, EnableMouseInput = false };
            _view.MediaPlayer = player;
            _player = player;
            return player;
        }

        /// <summary>Stops the video and lets go of the file (a file that is being played cannot be moved or deleted). The panel stays; the owner hides it.</summary>
        public async Task StopAsync()
        {
            _generation++;
            CurrentFile = null;
            _tick.Stop();
            _seek.Value = 0;
            _seek.Maximum = 0;
            _time.Text = "0:00 / 0:00";

            if (_player is { } player)
            {
                _stopping = true;
                await Task.Run(() => player.Stop()); // not on the interface thread: stopping waits for the engine's threads
                _stopping = false;
            }
            UpdateButtons();
        }

        public void TogglePause()
        {
            if (CurrentFile is not { } file || _player is not { } player || _stopping)
                return;

            if (player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
                _ = PlayAsync(file, 0);
            else
                player.Pause(); // toggles
            UpdateButtons();
        }

        public void SeekBy(long deltaMs)
        {
            if (_player is null || _stopping)
                return;

            SeekTo(TimeMs + deltaMs);
        }

        /// <summary>Goes to a place in the video (before the start or after the end is the nearest of them). A video that has ended starts again from there.</summary>
        public void SeekTo(long ms)
        {
            if (CurrentFile is not { } file || _player is not { } player || _stopping)
                return;

            var length = Math.Max(0, player.Length);
            var target = Math.Max(0, length > 0 ? Math.Min(ms, Math.Max(0, length - 200)) : ms);
            if (player.State is VLCState.Ended or VLCState.Stopped or VLCState.Error)
                _ = PlayAsync(file, target);
            else
                player.Time = target;

            _seek.Value = target;
            UpdateTime(target, length);
        }

        public void ChangeVolume(int delta)
        {
            _muted = false;
            SetVolume(_volume + delta);
        }

        private void SetVolume(int value)
        {
            _volume = Math.Clamp(value, 0, 100);
            _muted = false;
            _volumeBar.Value = _volume;
            if (_player is { } player && !_stopping)
            {
                player.Volume = _volume;
                player.Mute = false;
            }
            UpdateButtons();
        }

        public void ToggleMute()
        {
            _muted = !_muted;
            if (_player is { } player && !_stopping)
                player.Mute = _muted;
            UpdateButtons();
        }

        // ── the bar ────────────────────────────────────────────────────────

        private void Tick()
        {
            if (_player is not { } player || _stopping)
                return;

            var state = player.State;
            var length = Math.Max(0, player.Length);
            var time = Math.Max(0, player.Time);
            if (state == VLCState.Ended)
                time = length;

            if (length != _seek.Maximum)
                _seek.Maximum = length;
            if (state is VLCState.Playing or VLCState.Paused or VLCState.Ended)
            {
                _seek.Value = time;
                UpdateTime(time, length);
                TimeChanged?.Invoke(time);
            }

            // Volume set before the sound output exists is lost: say it again until it is there
            if (state == VLCState.Playing && player.Volume != _volume)
                player.Volume = _volume;

            if (state == VLCState.Error && !_failedShown)
            {
                _failedShown = true;
                Failed?.Invoke(string.Format(VideoPlayerStrings.PlayFailedFormat, CurrentFile), false);
            }
            UpdateButtons();
        }

        private void UpdateTime(long time, long length) =>
            _time.Text = length > 0 ? $"{VideoTime.Format(time)} / {VideoTime.Format(length)}" : VideoTime.Format(time);

        private void UpdateButtons()
        {
            var playing = _player is { State: VLCState.Playing } && !_stopping;
            _play.Text = playing ? "⏸" : "▶";
            _mute.Text = _muted || _volume == 0 ? "\U0001F507" : "\U0001F50A";
        }

        private void PictureClicked(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                TogglePause();
            Used?.Invoke();
        }

        private void GoKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _go.Clear();
                Used?.Invoke();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                if (VideoTime.TryParse(_go.Text, out var time))
                {
                    SeekTo((long)time.TotalMilliseconds);
                    _go.Clear();
                    Used?.Invoke();
                }
                else if (_go.Text.Length > 0)
                {
                    NotATime?.Invoke(string.Format(VideoPlayerStrings.GoToNotATimeFormat, _go.Text));
                }
            }
        }

        private void LayoutBar()
        {
            var y = (_bar.Height - _play.Height) / 2;
            var x = 6;
            _play.Location = new Point(x, y);
            x += _play.Width + 4;
            _time.SetBounds(x, 0, _time.Width, _bar.Height);
            x += _time.Width + 4;

            // From the right: volume, mute, the go-to box with its label; the slider takes what is left
            var right = _bar.Width - 8;
            _volumeBar.SetBounds(right - _volumeBar.Width, (_bar.Height - _volumeBar.Height) / 2, _volumeBar.Width, _volumeBar.Height);
            right -= _volumeBar.Width + 2;
            _mute.Location = new Point(right - _mute.Width, y);
            right -= _mute.Width + 10;
            _go.Location = new Point(right - _go.Width, (_bar.Height - _go.Height) / 2);
            right -= _go.Width + 4;
            _goLabel.SetBounds(right - _goLabel.Width, 0, _goLabel.Width, _bar.Height);
            right -= _goLabel.Width + 6;
            _seek.SetBounds(x, (_bar.Height - _seek.Height) / 2, Math.Max(40, right - x), _seek.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _generation++;
                _tick.Dispose();
                _tips.Dispose();
                var player = _player;
                _player = null;
                if (player is not null)
                {
                    _view.MediaPlayer = null;
                    // Stopping waits for the engine's threads: not here, and the player is let go of when they are done
                    _ = Task.Run(() =>
                    {
                        try { player.Stop(); player.Dispose(); } catch { /* the program is closing */ }
                    });
                }
            }
            base.Dispose(disposing);
        }
    }
}
