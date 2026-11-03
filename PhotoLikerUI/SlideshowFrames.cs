using System.Diagnostics.CodeAnalysis;

namespace PhotoLikerUI
{
    /// <summary>
    /// A photo of the slideshow, already scaled to the screen it is shown on (so it is drawn as it is, with no scaling at all), and the date it was taken,
    /// for the watermark. It is a tenth of the size of the decoded photo that <see cref="PhotoCache"/> keeps: 8 MB on a full HD screen, for a 24 MP photo of 96 MB.
    /// </summary>
    internal sealed class SlideshowFrame(Bitmap image, DateTime? taken) : IDisposable
    {
        public Bitmap Image { get; } = image;
        public DateTime? Taken { get; } = taken;
        public long Bytes { get; } = (long)image.Width * image.Height * 4;

        public void Dispose() => Image.Dispose();
    }

    /// <summary>
    /// Keeps the frames of the slideshow that are about to be needed, so that Left, Right and the timer show a photo at once: the form says which photos
    /// it may ask for next (<see cref="SetWindow"/>, the most likely first) and they are made in the background, a few at a time, in that order.
    /// It has a cache of its own and not the one of the main window because it keeps something else: those are the photos as they are, for zooming in
    /// and for the metadata, and the ones around the shown photo only; these are ready for this screen, which is cheap enough to keep the photos
    /// that the random order will ask for as well.
    ///
    /// Threading rule, as <see cref="PhotoCache"/>: every member must be called on the UI thread, and every event is raised on it. A frame is made on a
    /// thread of the pool and only published here once it is complete.
    /// </summary>
    internal sealed class SlideshowFrames : IDisposable
    {
        private readonly Size _screen;
        private readonly long _maxBytes;
        private readonly int _minFrames; // always kept, whatever they weigh: the shown photo and the ones next to it
        private readonly int _maxParallelLoads; // the decoder keeps up when several photos are made at once (GDI+ does not), and over a network their reads overlap
        private readonly Dictionary<string, SlideshowFrame> _frames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
        private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _cts = new();
        private readonly List<PhotoFile> _queue = [];
        private IReadOnlyList<PhotoFile> _window = [];
        private HashSet<string> _wanted = new(StringComparer.Ordinal);
        private string? _pinned;
        private long _bytes;
        private int _activePumps;

        public SlideshowFrames(Size screen, long maxBytes, int minFrames, int maxParallelLoads)
        {
            _screen = screen;
            _maxBytes = maxBytes;
            _minFrames = minFrames;
            _maxParallelLoads = maxParallelLoads;
        }

        /// <summary>A frame finished loading and is now here.</summary>
        public event Action<string, SlideshowFrame>? Loaded;

        /// <summary>A photo could not be read or decoded. It is not tried again.</summary>
        public event Action<string, Exception>? LoadFailed;

        public bool TryGet(string path, [NotNullWhen(true)] out SlideshowFrame? frame) =>
            _frames.TryGetValue(path, out frame);

        public bool HasFailed(string path) => _failed.Contains(path);

        /// <summary>
        /// Says which photos to have ready, the one that is needed first at the front: what is not among them is dropped (the farthest first, if the frames
        /// weigh more than the memory allowed), what is missing is made in that order. The frame on the screen is <paramref name="pinned"/>:
        /// it stays whatever happens, because it is being painted.
        /// </summary>
        public void SetWindow(IReadOnlyList<PhotoFile> mostNeededFirst, string? pinned)
        {
            _window = mostNeededFirst;
            _pinned = pinned;
            _wanted = new HashSet<string>(mostNeededFirst.Select(photo => photo.OriginalFilePath), StringComparer.Ordinal);

            foreach (var path in _frames.Keys.Where(p => !_wanted.Contains(p) && !IsPinned(p)).ToList())
                Evict(path);

            _queue.Clear();
            foreach (var photo in mostNeededFirst)
            {
                var path = photo.OriginalFilePath;
                if (!_frames.ContainsKey(path) && !_loading.Contains(path) && !_failed.Contains(path))
                    _queue.Add(photo);
            }

            StartPumps();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _queue.Clear();
            foreach (var frame in _frames.Values)
                frame.Dispose();
            _frames.Clear();
            _bytes = 0;
        }

        private bool IsPinned(string path) => string.Equals(path, _pinned, StringComparison.Ordinal);

        private void StartPumps()
        {
            while (_activePumps < _maxParallelLoads && _queue.Count > 0 && !_cts.IsCancellationRequested)
            {
                _activePumps++;
                _ = PumpAsync();
            }
        }

        private async Task PumpAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested && TryTakeJob(out var photo))
                {
                    var path = photo.OriginalFilePath;
                    _loading.Add(path);
                    SlideshowFrame? frame = null;
                    Exception? error = null;

                    try
                    {
                        // Only the file/GDI+ work runs on the pool; the result is private until it is published below.
                        frame = await Task.Run(() => ImageHelper.LoadSlide(photo, _screen));
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }

                    // Back on the UI thread. If the slideshow is over meanwhile, nobody wants the result.
                    if (_cts.IsCancellationRequested)
                    {
                        frame?.Dispose();
                        return;
                    }

                    _loading.Remove(path);

                    if (error is not null)
                    {
                        _failed.Add(path);
                        LoadFailed?.Invoke(path, error);
                    }
                    else if (frame is not null)
                    {
                        Publish(path, frame);
                    }
                }
            }
            finally
            {
                _activePumps--;
            }
        }

        private bool TryTakeJob(out PhotoFile photo)
        {
            while (_queue.Count > 0)
            {
                photo = _queue[0];
                _queue.RemoveAt(0);

                var path = photo.OriginalFilePath;
                if (!_wanted.Contains(path) || _loading.Contains(path) || _frames.ContainsKey(path) || _failed.Contains(path))
                    continue;

                // Once the memory is used up only the few frames next to the shown one are still made
                if (_bytes >= _maxBytes && _frames.Count >= _minFrames)
                {
                    _queue.Clear();
                    break;
                }

                return true;
            }

            photo = null!;
            return false;
        }

        private void Publish(string path, SlideshowFrame frame)
        {
            if (!_wanted.Contains(path) && !IsPinned(path) || _frames.ContainsKey(path))
            {
                frame.Dispose();
                return;
            }

            _frames[path] = frame;
            _bytes += frame.Bytes;
            Loaded?.Invoke(path, frame);
            EnforceBudget();
        }

        /// <summary>Drops the frames farthest from what is needed next until they fit the memory budget (never the shown one, never fewer than the few kept).</summary>
        private void EnforceBudget()
        {
            for (var i = _window.Count - 1; i > 0 && _bytes > _maxBytes && _frames.Count > _minFrames; i--)
            {
                var path = _window[i].OriginalFilePath;
                if (!IsPinned(path))
                    Evict(path);
            }
        }

        private void Evict(string path)
        {
            if (!_frames.Remove(path, out var frame))
                return;

            _bytes -= frame.Bytes;
            frame.Dispose();
        }
    }
}
