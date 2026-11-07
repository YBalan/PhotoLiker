using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace PhotoLikerUI
{
    /// <summary>
    /// How <see cref="SlideshowScreen.ShowFrame"/> puts a photo on a screen: with no effect at all (the default of the struct), or taking the place of the last
    /// photo by <see cref="Transition"/> for <see cref="TransitionSeconds"/>, and/or moving slowly for <see cref="MotionSeconds"/>. <see cref="StartedAt"/> is a
    /// <see cref="Stopwatch"/> time, the same for all the screens of one change so that they move together.
    /// </summary>
    internal readonly record struct SlideshowShowPlan(
        SlideshowTransition Transition, double TransitionSeconds, bool Motion, double MotionSeconds, long StartedAt);

    /// <summary>
    /// The window of the slideshow on one screen: black, with the photo on it as large as the screen lets it be and the year or the date and time it was taken
    /// in the bottom right corner (see <see cref="SlideshowWatermark"/>). It only shows what <see cref="SlideshowSession"/> gives it, and passes every key to it.
    /// It has frames of its own (<see cref="Frames"/>) because they are made for the size of this screen, and screens differ.
    ///
    /// A photo stands still, and is then drawn as it is (a copy of the pixels, as quick as it gets). While an effect lasts (see <see cref="SlideshowEffect"/>) the
    /// picture is put together by <see cref="FrameCompositor"/> at every tick of a timer, from the time that has passed rather than the ticks that came, so
    /// a computer that can not keep up shows fewer steps and not a longer effect.
    /// </summary>
    internal sealed class SlideshowScreen : Form
    {
        private const float WatermarkHeightDivisor = 40f; // the text is this much of the height of the screen
        private const float MinWatermarkFontPixels = 14f;
        private const int WatermarkBackgroundAlpha = 150;
        private const int AnimationIntervalMs = 10;       // Windows rounds it up to about 16: 60 steps a second at most
        private const int NoticeMs = 1800;                // how long the name of a new effect stays in the corner
        private const int VideoWatchMs = 100;             // how often it is looked at whether a stretch of a video is over
        private const int VideoStartTimeoutMs = 15_000;   // a video that has not begun to play by then is given up
        private const int VideoSettleMs = 400;            // after a stretch is started the player still says what it said before: not believed for this long

        private readonly SlideshowSession _session;
        private readonly SlideshowWatermark _watermark;
        private readonly Font _watermarkFont;
        private readonly System.Windows.Forms.Timer _animation = new() { Interval = AnimationIntervalMs };
        private readonly System.Windows.Forms.Timer _noticeTimer = new() { Interval = NoticeMs };
        private readonly CompositeLayer[] _layers = new CompositeLayer[2];
        private string? _notice;                           // a few words in the top left corner (the effect that was just chosen), for a moment
        private PhotoOnScreen? _current;                           // what is on the screen; its frame belongs to Frames
        private PhotoOnScreen? _outgoing;                          // while one photo takes the place of another: the one that goes; also belongs to Frames
        private SlideshowTransition _transition;
        private long _transitionStartedAt;
        private double _transitionSeconds;
        private PixelCanvas? _canvas;                      // what the effects are put together on, the size of the screen
        private string? _message = SlideshowStrings.Loading;

        // A video on this screen: a player over the whole window (see ShowVideo)
        private readonly System.Windows.Forms.Timer _videoWatch = new() { Interval = VideoWatchMs };
        private VideoView? _videoView;
        private MediaPlayer? _videoPlayer;
        private LibVLC? _videoEngine;                      // the one the player was made with (it belongs to VideoPlayerPanel and is not disposed here)
        private Label? _videoMark;                         // the date of the video, in the corner, as a photo has it
        private Label? _pausedMark;                        // "Paused" over the video (the window can not draw over a player, which is a window of its own)
        private bool _paused;                              // the show is paused: this screen says so, and holds its video
        private VideoRun? _video;                          // what is playing; null when nothing is
        private int _videoGeneration;                      // changes with every video and when one is stopped, so that what a video that has been replaced does is ignored

        /// <param name="peopleOf">Says who is in a photo as a line of names for the bottom left corner; null writes no names. Called on the threads that make the frames.</param>
        public SlideshowScreen(
            SlideshowSession session, Rectangle bounds, SlideshowWatermark watermark, long frameBytes, int minFrames, int parallelLoads,
            Func<string, string?>? peopleOf = null)
        {
            _session = session;
            _watermark = watermark;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            ForeColor = Color.Gray;
            Text = "PhotoLiker";
            DoubleBuffered = true;

            _watermarkFont = new Font(Font.FontFamily, Math.Max(MinWatermarkFontPixels, bounds.Height / WatermarkHeightDivisor), FontStyle.Bold, GraphicsUnit.Pixel);
            _animation.Tick += (_, _) => OnAnimationTick();
            _noticeTimer.Tick += (_, _) => HideNotice();
            _videoWatch.Tick += (_, _) => OnVideoWatch();
            Frames = new SlideshowFrames(bounds.Size, frameBytes, minFrames, parallelLoads, peopleOf);
        }

        /// <summary>The photos made ready for this screen.</summary>
        public SlideshowFrames Frames { get; }

        /// <summary>Whether a photo (or a video) is on this screen yet.</summary>
        public bool HasPhoto => _current is not null || _videoView is { Visible: true };

        /// <summary>The file of the photo that is leaving the screen while an effect lasts; null when none is. <see cref="Frames"/> must not let go of it before.</summary>
        public string? OutgoingPath => _outgoing?.Frame.Path;

        /// <summary>Whether an effect is under way: the photo is changing or moving.</summary>
        internal bool IsAnimating => _animation.Enabled;

        /// <summary>Puts a photo on the screen, in place of the one that is there, as <paramref name="plan"/> says.</summary>
        public void ShowFrame(SlideshowFrame frame, SlideshowShowPlan plan = default)
        {
            if (_videoView is { Visible: true })
                StopVideo(); // a photo takes the place of the video

            var previous = _current;
            _message = null;
            _outgoing = null;
            _transition = SlideshowTransition.None;

            if (previous is not null && !ReferenceEquals(previous.Frame, frame) && plan.Transition != SlideshowTransition.None && plan.TransitionSeconds > 0)
            {
                _outgoing = previous; // it goes on moving as it did while it leaves
                _transition = plan.Transition;
                _transitionStartedAt = plan.StartedAt;
                _transitionSeconds = plan.TransitionSeconds;
            }

            var path = plan.Motion && plan.MotionSeconds > 0 ? KenBurnsPath.Random(Random.Shared.NextDouble) : (KenBurnsPath?)null;
            _current = new PhotoOnScreen(frame, path, plan.StartedAt, plan.MotionSeconds);

            if (_outgoing is not null || path is not null)
                _animation.Start();
            else
                _animation.Stop();

            Invalidate();
        }

        /// <summary>
        /// Starts the slow movement of the photo that is on the screen if it has none: the effect was changed to one that moves the photos while the photo stays.
        /// A photo that is moving already goes on as it is, whatever the plan says; one that is not moving is not made to.
        /// </summary>
        public void StartMotion(SlideshowShowPlan plan)
        {
            if (_current is not { HasMotion: false } current || !plan.Motion || plan.MotionSeconds <= 0)
                return;

            _current = new PhotoOnScreen(current.Frame, KenBurnsPath.Random(Random.Shared.NextDouble), plan.StartedAt, plan.MotionSeconds);
            _animation.Start();
            Invalidate();
        }

        /// <summary>Writes a few words in the top left corner for a moment (what the Up and Down keys chose); a new one takes the place of the last.</summary>
        public void ShowNotice(string text)
        {
            _notice = text;
            _noticeTimer.Stop();
            _noticeTimer.Start();
            Invalidate();
        }

        private void HideNotice()
        {
            _noticeTimer.Stop();
            _notice = null;
            Invalidate();
        }

        /// <summary>
        /// The show was paused, or goes on: "Paused" is written in the top right corner as long as it stands, and a video that plays is held (and plays on from the place it was held at).
        /// A photo is not touched: it stays as it is, and a change that is under way finishes.
        /// </summary>
        internal void SetPaused(bool paused)
        {
            if (_paused == paused)
                return;

            _paused = paused;
            if (_video is { } run && _videoPlayer is { } player)
            {
                if (!paused)
                    run.StartedAt = Stopwatch.GetTimestamp(); // the time it stood is not time of a start that takes too long
                try
                {
                    player.SetPause(paused);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // the player is going away
                }
            }

            UpdatePausedMark();
            Invalidate();
        }

        /// <summary>Whether the show is paused as far as this screen knows.</summary>
        internal bool IsPaused => _paused;

        /// <summary>Says something in the middle of an empty screen.</summary>
        public void ShowMessage(string text)
        {
            _message = text;
            Invalidate();
        }

        // ── a video ────────────────────────────────────────────────────────

        /// <summary>Raised once for each video that was shown, when its last stretch is over, or when it could not be played; not for one that was stopped or replaced.</summary>
        internal event Action<SlideshowScreen>? VideoFinished;

        /// <summary>Whether a video is playing on this screen (it has not finished and has not been stopped).</summary>
        internal bool IsPlayingVideo => _video is not null;

        /// <summary>
        /// Plays a video over the whole screen, in place of the photo that is there (no effect: the video just begins), each of <paramref name="segments"/> after the other, then raises
        /// <see cref="VideoFinished"/>. <paramref name="taken"/> is written in the corner as a photo's is. A photo shown after it (see <see cref="ShowFrame"/>) takes the screen back.
        /// </summary>
        internal async void ShowVideo(string path, IReadOnlyList<VideoSegment> segments, bool mute, DateTime? taken)
        {
            var generation = ++_videoGeneration;
            _animation.Stop();
            _current = null;
            _outgoing = null;
            _transition = SlideshowTransition.None;
            _message = null;
            _video = new VideoRun(path, segments.Count > 0 ? segments : [new VideoSegment(0, null)], mute);

            EnsureVideoView();
            _videoView!.Visible = true;
            ShowVideoMark(taken);
            UpdatePausedMark();
            Invalidate();

            try
            {
                var engine = await VideoPlayerPanel.EngineAsync();
                if (generation != _videoGeneration || IsDisposed)
                    return; // another video, a photo, or the end of the show came while the engine started

                _videoEngine = engine;
                StartSegment(engine);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FinishVideo(generation); // the engine is not there, or the file will not play: on to the next
            }
        }

        /// <summary>Stops what plays and shows nothing of it; no <see cref="VideoFinished"/> follows.</summary>
        internal void StopVideo()
        {
            _videoGeneration++;
            _videoWatch.Stop();
            _video = null;

            if (_videoPlayer is { } player)
                _ = Task.Run(() => { try { player.Stop(); } catch (Exception ex) when (ex is not OutOfMemoryException) { /* it is going away */ } });
            if (_videoView is { } view)
                view.Visible = false;
            if (_videoMark is { } mark)
                mark.Visible = false;
            UpdatePausedMark();
        }

        private void EnsureVideoView()
        {
            if (_videoView is not null)
                return;

            _videoMark = new Label
            {
                AutoSize = true, BackColor = Color.Black, ForeColor = Color.White, Font = _watermarkFont, Visible = false, UseMnemonic = false,
            };
            _pausedMark = new Label
            {
                AutoSize = true, BackColor = Color.Black, ForeColor = Color.White, Font = _watermarkFont, Visible = false, UseMnemonic = false,
            };
            _videoView = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black, TabStop = false, Visible = false };
            Controls.Add(_videoMark);
            Controls.Add(_pausedMark);
            Controls.Add(_videoView);
            Controls.SetChildIndex(_videoMark, 0); // over the video, which is a window of its own
            Controls.SetChildIndex(_pausedMark, 0);
        }

        /// <summary>"Paused" over a video, in the top right corner, while the show is paused (a photo has it drawn on the window, see <see cref="OnPaint"/>).</summary>
        private void UpdatePausedMark()
        {
            if (_pausedMark is not { } mark)
                return;

            if (!_paused || _videoView is not { Visible: true })
            {
                mark.Visible = false;
                return;
            }

            mark.Text = SlideshowStrings.Paused;
            var size = mark.PreferredSize;
            var margin = ClientSize.Height / 40;
            mark.Location = new Point(ClientSize.Width - margin - size.Width, margin);
            mark.Visible = true;
            mark.BringToFront();
        }

        private void ShowVideoMark(DateTime? taken)
        {
            if (_videoMark is not { } mark)
                return;

            var text = Slideshow.WatermarkText(_watermark, taken);
            if (text is null)
            {
                mark.Visible = false;
                return;
            }

            mark.Text = text;
            var size = mark.PreferredSize;
            var margin = ClientSize.Height / 40;
            mark.Location = new Point(ClientSize.Width - margin - size.Width, ClientSize.Height - margin - size.Height);
            mark.Visible = true;
            mark.BringToFront();
        }

        private void StartSegment(LibVLC engine)
        {
            if (_video is not { } run)
                return;

            var segment = run.Segments[run.Index];
            if (_videoPlayer is null)
            {
                _videoPlayer = new MediaPlayer(engine) { EnableKeyInput = false, EnableMouseInput = false };
                _videoView!.MediaPlayer = _videoPlayer;
            }

            using var media = new Media(engine, run.Path, FromType.FromPath);
            if (segment.StartMs > 0)
                media.AddOption(":start-time=" + Seconds(segment.StartMs));
            if (segment.EndMs is { } end)
                media.AddOption(":stop-time=" + Seconds(end));

            _videoPlayer.Mute = run.Mute;
            run.StartedAt = Stopwatch.GetTimestamp();
            run.SawPlaying = false;
            if (!_videoPlayer.Play(media))
                throw new InvalidOperationException(run.Path);

            _videoWatch.Start();
        }

        private static string Seconds(long ms) => (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Is the stretch over? The player says it has ended or stopped, or the time has come to its end (it does not always say so at the end it was told).</summary>
        private void OnVideoWatch()
        {
            if (_video is not { } run || _videoPlayer is not { } player)
            {
                _videoWatch.Stop();
                return;
            }

            var state = player.State;

            // A paused show waits: the video is held (one that began while it is paused is held as soon as it plays), and nothing is judged over
            if (_paused)
            {
                if (state is VLCState.Playing or VLCState.Paused)
                    run.SawPlaying = true;
                if (state == VLCState.Playing)
                    player.SetPause(true);
                return;
            }

            if (state == VLCState.Playing)
                run.SawPlaying = true;

            var sinceStart = (Stopwatch.GetTimestamp() - run.StartedAt) * 1000.0 / Stopwatch.Frequency;
            var segment = run.Segments[run.Index];
            var settled = sinceStart > VideoSettleMs;
            var over = state == VLCState.Error
                || (settled && state == VLCState.Ended)
                || (settled && state == VLCState.Stopped && run.SawPlaying)
                || (run.SawPlaying && segment.EndMs is { } end && player.Time >= end)
                || (!run.SawPlaying && sinceStart > VideoStartTimeoutMs);
            if (!over)
                return;

            if (state != VLCState.Error && run.SawPlaying && run.Index + 1 < run.Segments.Count)
            {
                run.Index++;
                try
                {
                    StartSegment(_videoEngine!);
                    return;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // the next stretch cannot be played: the video is over
                }
            }

            FinishVideo(_videoGeneration);
        }

        private void FinishVideo(int generation)
        {
            if (generation != _videoGeneration || _video is null)
                return;

            _videoWatch.Stop();
            _video = null;
            if (_videoPlayer is { } player)
                _ = Task.Run(() => { try { player.Stop(); } catch (Exception ex) when (ex is not OutOfMemoryException) { /* it is going away */ } });

            VideoFinished?.Invoke(this);
        }

        /// <summary>The video that is playing: where, the stretches of it, which one is on, whether it is silent, and how its stretch is going.</summary>
        private sealed class VideoRun(string path, IReadOnlyList<VideoSegment> segments, bool mute)
        {
            public string Path { get; } = path;
            public IReadOnlyList<VideoSegment> Segments { get; } = segments;
            public bool Mute { get; } = mute;
            public int Index { get; set; }
            public long StartedAt { get; set; }
            public bool SawPlaying { get; set; }
        }

        // ── effects ────────────────────────────────────────────────────────

        private void OnAnimationTick()
        {
            var now = Stopwatch.GetTimestamp();
            if (_outgoing is not null && TransitionProgress(now) >= 1)
            {
                _outgoing = null;
                _transition = SlideshowTransition.None;
            }

            Invalidate(); // this is the last picture of the effect if it is over: the paint after this finds it so

            if (_outgoing is null && _current?.IsMoving(now) != true)
                _animation.Stop();
        }

        /// <summary>How far the change of photo has come, 0 to 1 (1 when there is none).</summary>
        private double TransitionProgress(long now) =>
            _outgoing is null ? 1 : Math.Clamp(PhotoOnScreen.SecondsBetween(_transitionStartedAt, now) / _transitionSeconds, 0, 1);

        // ── keys ───────────────────────────────────────────────────────────

        private const long KeyWasDownBit = 0x40000000; // bit 30 of the LParam of a key message: the key was already down, so this is a repeat of a held key

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            _session.HandleKey(keyData, repeat: (msg.LParam.ToInt64() & KeyWasDownBit) != 0) || base.ProcessCmdKey(ref msg, keyData);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _videoGeneration++;
            _videoWatch.Stop();
            _animation.Stop();
            _noticeTimer.Stop();
            base.OnFormClosed(e);
            _session.ScreenClosed(this); // the others go with it
        }

        // ── painting ───────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_current is { } current)
            {
                var now = Stopwatch.GetTimestamp();
                var motion = current.MotionAt(now);

                if (_outgoing is null && motion.IsPlain)
                {
                    DrawPlain(g, current.Frame);
                    DrawWatermark(g, current.Frame.Taken, 1);
                    DrawPeople(g, current.Frame, 1);
                }
                else
                {
                    DrawComposed(g, now, current, motion);
                }
            }
            else if (_message is not null)
            {
                TextRenderer.DrawText(g, _message, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }

            if (_notice is not null)
                DrawLabel(g, _notice, LabelPlace.TopLeft, 1);

            if (_paused)
                DrawLabel(g, SlideshowStrings.Paused, LabelPlace.TopRight, 1);
        }

        /// <summary>A photo standing still: the pixels of the frame, copied, with no smoothing: the frame was made for this screen.</summary>
        private void DrawPlain(Graphics g, SlideshowFrame frame)
        {
            var image = frame.Image;
            var bounds = new Rectangle((ClientSize.Width - image.Width) / 2, (ClientSize.Height - image.Height) / 2, image.Width, image.Height);

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, bounds);
            g.CompositingMode = CompositingMode.SourceOver;
        }

        /// <summary>An effect at this moment: the pictures put together on a canvas of the size of the screen, which is then copied on it.</summary>
        private void DrawComposed(Graphics g, long now, PhotoOnScreen current, SlideshowLayer currentMotion)
        {
            var width = ClientSize.Width;
            var height = ClientSize.Height;
            if (_canvas is null || _canvas.Width != width || _canvas.Height != height)
            {
                _canvas?.Dispose();
                _canvas = new PixelCanvas(width, height);
            }

            var count = 0;
            var progress = TransitionProgress(now);
            if (_outgoing is { } outgoing)
            {
                var (leaving, coming) = SlideshowEffects.Transition(_transition, progress);
                var comingWeight = WeightOf(coming.Alpha);

                // What dissolves is the whole screen, the black around a photo too: the shares of the two add up to all. What slides is not over one another
                var leavingWeight = _transition == SlideshowTransition.Slide ? WeightOf(leaving.Alpha) : FrameCompositor.FullWeight - comingWeight;
                _layers[count++] = LayerOf(outgoing.Frame, outgoing.MotionAt(now), leaving, leavingWeight, width, height);
                _layers[count++] = LayerOf(current.Frame, currentMotion, coming, comingWeight, width, height);
            }
            else
            {
                _layers[count++] = LayerOf(current.Frame, currentMotion, SlideshowLayer.Plain, FrameCompositor.FullWeight, width, height);
            }

            FrameCompositor.Compose(_canvas.Pixels, width, height, _layers.AsSpan(0, count));

            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_canvas.Image, new Rectangle(0, 0, width, height));
            g.CompositingMode = CompositingMode.SourceOver;

            DrawWatermarks(g, progress);
        }

        private static int WeightOf(double alpha) => Math.Clamp((int)Math.Round(alpha * FrameCompositor.FullWeight), 0, FrameCompositor.FullWeight);

        /// <summary>Where a picture goes at this moment: the middle of the screen, moved by its own movement (a fraction of the picture) and by the change (a fraction of the screen, whole pixels).</summary>
        private static CompositeLayer LayerOf(SlideshowFrame frame, SlideshowLayer motion, SlideshowLayer change, int weight, int width, int height) =>
            new(frame.Pixels, frame.Width, frame.Height,
                width / 2.0 + motion.OffsetX * frame.Width + Math.Round(change.OffsetX * width),
                height / 2.0 + motion.OffsetY * frame.Height + Math.Round(change.OffsetY * height),
                motion.Scale * change.Scale,
                weight);

        /// <summary>
        /// The writing of the photos: while one photo takes the place of the other the old date fades out in the first half of the change and the new one fades in in
        /// the second, so two dates are never written over each other. A date that is the same for both is not touched.
        /// </summary>
        private void DrawWatermarks(Graphics g, double progress)
        {
            if (_current is not { } current)
                return;

            if (_outgoing is not { } outgoing)
            {
                DrawWatermark(g, current.Frame.Taken, 1);
                DrawPeople(g, current.Frame, 1);
                return;
            }

            var before = Slideshow.WatermarkText(_watermark, outgoing.Frame.Taken);
            var after = Slideshow.WatermarkText(_watermark, current.Frame.Taken);
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                DrawWatermark(g, current.Frame.Taken, 1);
            }
            else
            {
                DrawWatermark(g, outgoing.Frame.Taken, Math.Max(0, 1 - 2 * progress));
                DrawWatermark(g, current.Frame.Taken, Math.Max(0, 2 * progress - 1));
            }

            // The names the same way: the same people on both photos stay as they are
            if (string.Equals(outgoing.Frame.People, current.Frame.People, StringComparison.Ordinal))
            {
                DrawPeople(g, current.Frame, 1);
            }
            else
            {
                DrawPeople(g, outgoing.Frame, Math.Max(0, 1 - 2 * progress));
                DrawPeople(g, current.Frame, Math.Max(0, 2 * progress - 1));
            }
        }

        /// <summary>Writes the year or the date and time in the bottom right corner of the screen, white on a dark band so that it reads on any photo; <paramref name="opacity"/> 1 is as it is, less lets the photo through.</summary>
        private void DrawWatermark(Graphics g, DateTime? taken, double opacity)
        {
            if (Slideshow.WatermarkText(_watermark, taken) is { } text)
                DrawLabel(g, text, LabelPlace.BottomRight, opacity);
        }

        /// <summary>
        /// Writes the names of the people in the photo in the bottom left corner, in the same band as the date has on the other side. They take the width the date leaves
        /// (a long list goes on to another line, and the band grows upwards), so the two never run into each other, even on a screen that is taller than wide.
        /// </summary>
        private void DrawPeople(Graphics g, SlideshowFrame frame, double opacity)
        {
            if (frame.People is not { } people)
                return;

            var format = StringFormat.GenericTypographic;
            var pad = _watermarkFont.Size / 3f;
            var margin = ClientSize.Height / 40f;
            var reserved = 0f;
            if (Slideshow.WatermarkText(_watermark, frame.Taken) is { } date)
                reserved = g.MeasureString(date, _watermarkFont, PointF.Empty, format).Width + pad * 2 + margin;

            var width = Math.Max(ClientSize.Width * 0.3f, ClientSize.Width - margin * 3 - reserved) - pad * 2;
            DrawLabel(g, people, LabelPlace.BottomLeft, opacity, width);
        }

        private enum LabelPlace { BottomRight, BottomLeft, TopLeft, TopRight }

        /// <summary>
        /// Text, white on a dark band: in the bottom right corner (the watermark), the bottom left one (the names), the top left one (the notice) or the top right one (paused); <paramref name="opacity"/> 1 is as it is, less
        /// lets what is behind through. Text wider than <paramref name="maxWidth"/> (when given) is wrapped.
        /// </summary>
        private void DrawLabel(Graphics g, string text, LabelPlace place, double opacity, float maxWidth = 0)
        {
            if (opacity <= 0)
                return;

            var format = StringFormat.GenericTypographic; // shared instance, must not be disposed
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            var size = maxWidth > 0 ? g.MeasureString(text, _watermarkFont, new SizeF(maxWidth, ClientSize.Height), format) : g.MeasureString(text, _watermarkFont, PointF.Empty, format);
            var pad = _watermarkFont.Size / 3f;
            var margin = ClientSize.Height / 40f;

            var box = place switch
            {
                LabelPlace.BottomRight => new RectangleF(ClientSize.Width - margin - size.Width - pad * 2, ClientSize.Height - margin - size.Height - pad * 2, size.Width + pad * 2, size.Height + pad * 2),
                LabelPlace.BottomLeft => new RectangleF(margin, ClientSize.Height - margin - size.Height - pad * 2, size.Width + pad * 2, size.Height + pad * 2),
                LabelPlace.TopRight => new RectangleF(ClientSize.Width - margin - size.Width - pad * 2, margin, size.Width + pad * 2, size.Height + pad * 2),
                _ => new RectangleF(margin, margin, size.Width + pad * 2, size.Height + pad * 2),
            };

            using var background = new SolidBrush(Color.FromArgb((int)Math.Round(WatermarkBackgroundAlpha * Math.Min(1, opacity)), Color.Black));
            using var ink = new SolidBrush(Color.FromArgb((int)Math.Round(255 * Math.Min(1, opacity)), Color.White));
            g.FillRectangle(background, box);

            if (maxWidth > 0)
                g.DrawString(text, _watermarkFont, ink, new RectangleF(box.Left + pad, box.Top + pad, maxWidth, size.Height + pad), format); // wrapped where it was measured
            else
                g.DrawString(text, _watermarkFont, ink, box.Left + pad, box.Top + pad, format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _noticeTimer.Dispose();
                _videoWatch.Dispose();
                if (_videoView is not null)
                    _videoView.MediaPlayer = null;
                if (_videoPlayer is { } player)
                {
                    try { player.Stop(); } catch (Exception ex) when (ex is not OutOfMemoryException) { /* the engine is going away */ }
                    player.Dispose();
                    _videoPlayer = null;
                }
                Frames.Dispose(); // the frames on the screen go with it
                _current = null;
                _outgoing = null;
                _canvas?.Dispose();
                _canvas = null;
                _watermarkFont.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>A photo on the screen: its frame, and how it moves while it stays (if it does) from the time it came.</summary>
        private sealed class PhotoOnScreen(SlideshowFrame frame, KenBurnsPath? path, long startedAt, double motionSeconds)
        {
            public SlideshowFrame Frame { get; } = frame;

            public static double SecondsBetween(long from, long to) => (to - from) / (double)Stopwatch.Frequency;

            private double Progress(long now) => path is null || motionSeconds <= 0 ? 1 : Math.Clamp(SecondsBetween(startedAt, now) / motionSeconds, 0, 1);

            /// <summary>Whether it moves at all (it may be done moving).</summary>
            public bool HasMotion => path is not null;

            /// <summary>Whether it is still moving; a photo that has reached the end of its movement stays as it is.</summary>
            public bool IsMoving(long now) => path is not null && Progress(now) < 1;

            /// <summary>How it is magnified and where it lies at this moment (offsets are fractions of the picture).</summary>
            public SlideshowLayer MotionAt(long now) => path is { } p ? p.At(Progress(now)) : SlideshowLayer.Plain;
        }
    }
}
