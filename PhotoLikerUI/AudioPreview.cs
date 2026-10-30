namespace PhotoLikerUI
{
    using System.Media;

    internal enum AudioPreviewState
    {
        Idle,

        /// <summary>The audio file is being turned into something that can be played.</summary>
        Preparing,
        Playing,
    }

    /// <summary>
    /// Plays the audio file chosen for a video, once, to listen to it. ffmpeg turns the file into a WAV first (<see cref="AudioDecoder"/>),
    /// so whatever ffmpeg can read can be played, which is the same that goes into the video. The WAV is kept for the next listen to
    /// the same file, and removed with this object. Used from the UI thread; <see cref="Changed"/> is raised there too.
    /// </summary>
    internal sealed class AudioPreview : IDisposable
    {
        // Playing has no end event, so the end is when the length of the WAV is over, and a little more for a start that is not at once
        private const int EndMarginMilliseconds = 400;

        private readonly System.Windows.Forms.Timer _endTimer = new();

        private string? _folder;
        private string? _wavPath;
        private string? _wavOf;
        private SoundPlayer? _player;
        private CancellationTokenSource? _preparing;
        private int _generation;

        public AudioPreview() => _endTimer.Tick += (_, _) => Stop();

        public AudioPreviewState State { get; private set; }

        public event EventHandler? Changed;

        /// <summary>
        /// Prepares the file and plays it. Does nothing while something is prepared or played already. Stopping meanwhile ends it quietly.
        /// </summary>
        /// <exception cref="InvalidOperationException">ffmpeg is not there, or cannot read the file as audio.</exception>
        /// <exception cref="IOException">The file cannot be read.</exception>
        public async Task PlayAsync(string audioPath)
        {
            if (State != AudioPreviewState.Idle)
                return;

            var generation = ++_generation;
            using var preparing = _preparing = new CancellationTokenSource();
            SetState(AudioPreviewState.Preparing);
            try
            {
                var (wavPath, seconds) = await PrepareAsync(audioPath, preparing.Token);

                // Stopped, or closed, while it was being prepared
                if (generation != _generation)
                    return;

                var player = new SoundPlayer(wavPath);
                await Task.Run(player.Load);
                if (generation != _generation)
                {
                    player.Dispose();
                    return;
                }

                _player = player;
                player.Play();
                _endTimer.Interval = (int)Math.Ceiling(seconds * 1000) + EndMarginMilliseconds;
                _endTimer.Start();
                SetState(AudioPreviewState.Playing);
            }
            catch (OperationCanceledException) when (generation != _generation)
            {
            }
            catch
            {
                if (generation == _generation)
                    SetState(AudioPreviewState.Idle);

                throw;
            }
            finally
            {
                if (ReferenceEquals(_preparing, preparing))
                    _preparing = null;
            }
        }

        public void Stop()
        {
            _generation++;
            _preparing?.Cancel();
            _endTimer.Stop();
            if (_player is { } player)
            {
                player.Stop();
                player.Dispose();
                _player = null;
            }

            SetState(AudioPreviewState.Idle);
        }

        public void Dispose()
        {
            Stop();
            _endTimer.Dispose();
            RemoveFolder();
        }

        /// <summary>The WAV of the file (made, if the file is not the one of the last listen) and how long it is.</summary>
        private async Task<(string WavPath, double Seconds)> PrepareAsync(string audioPath, CancellationToken cancellationToken)
        {
            var info = new FileInfo(audioPath);
            if (!info.Exists)
                throw new FileNotFoundException("The audio file is not there.", audioPath);

            // A file that was changed since the last listen is not the one that was made a WAV of
            var identity = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            if (_wavPath is null || _wavOf != identity || !File.Exists(_wavPath))
            {
                _folder ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "photoliker_audio_" + Guid.NewGuid().ToString("N"))).FullName;
                var wavPath = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    await AudioDecoder.ToWavAsync(audioPath, wavPath, cancellationToken);
                }
                catch
                {
                    TryDelete(wavPath);
                    throw;
                }

                TryDelete(_wavPath);
                _wavPath = wavPath;
                _wavOf = identity;
            }

            return (_wavPath, new FileInfo(_wavPath).Length / (double)AudioDecoder.WavBytesPerSecond);
        }

        private void SetState(AudioPreviewState state)
        {
            if (State == state)
                return;

            State = state;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveFolder()
        {
            if (_folder is null)
                return;

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in the temp folder
            }

            _folder = null;
            _wavPath = null;
        }

        private static void TryDelete(string? path)
        {
            if (path is null)
                return;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
