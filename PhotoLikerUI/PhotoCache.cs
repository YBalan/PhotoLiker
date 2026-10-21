using System.Diagnostics.CodeAnalysis;

namespace PhotoLikerUI
{
    /// <summary>
    /// Keeps decoded photos (and thumbnails) around the photo being viewed.
    ///
    /// Threading rule: every member must be called on the UI thread, and every event is raised on it. Photos are
    /// decoded on thread-pool threads into private bitmaps and only published to the cache once complete, so a
    /// cached image is never touched by two threads at once and is only ever disposed where nothing paints it.
    /// </summary>
    internal sealed class PhotoCache : IDisposable
    {
        private const int MaxParallelLoads = 2;

        private readonly record struct Job(string Path, bool ThumbOnly);

        /// <summary>State of one generation of loading; <see cref="Clear"/> abandons it and starts a new one.</summary>
        private sealed class Session
        {
            public readonly CancellationTokenSource Cts = new();
            public readonly List<Job> Queue = [];
            public readonly HashSet<string> Loading = new(StringComparer.Ordinal);
            public readonly HashSet<string> Failed = new(StringComparer.Ordinal);
            public int ActivePumps;
        }

        private readonly long _maxBytes;
        private readonly Dictionary<string, PhotoEntry> _entries = new(StringComparer.Ordinal);
        private IReadOnlyList<string> _window = [];                 // nearest-first, first item is the photo being viewed
        private HashSet<string> _wanted = new(StringComparer.Ordinal);
        private Session _session = new();
        private long _bytes;

        public PhotoCache(long maxBytes)
        {
            _maxBytes = maxBytes;
        }

        /// <summary>A photo finished loading and is now in the cache.</summary>
        public event Action<string, PhotoEntry>? Loaded;

        /// <summary>A thumbnail that had to be rebuilt (after a size change) is ready.</summary>
        public event Action<string, PhotoEntry>? ThumbReady;

        public event Action<string, Exception>? LoadFailed;

        /// <summary>Raised just before an entry is disposed so the UI can let go of its images.</summary>
        public event Action<string, PhotoEntry>? Evicting;

        public int ThumbSize { get; private set; }

        public bool TryGet(string path, [NotNullWhen(true)] out PhotoEntry? entry) =>
            _entries.TryGetValue(path, out entry);

        /// <summary>
        /// Changes the thumbnail size. The caller must have detached the old thumbnails from the UI first;
        /// they are rebuilt in the background on the next <see cref="SetWindow"/>.
        /// </summary>
        public void SetThumbSize(int size)
        {
            if (size == ThumbSize) return;
            ThumbSize = size;
            foreach (var entry in _entries.Values)
                entry.DropThumb();
        }

        /// <summary>
        /// Sets which photos should be cached, nearest first (the first one is the photo being viewed).
        /// Photos outside the window are dropped, missing ones are loaded in that order.
        /// </summary>
        public void SetWindow(IReadOnlyList<string> nearestFirst)
        {
            _window = nearestFirst;
            _wanted = new HashSet<string>(nearestFirst, StringComparer.Ordinal);

            var session = _session;
            if (nearestFirst.Count > 0)
                session.Failed.Remove(nearestFirst[0]); // opening a photo again is a retry

            foreach (var path in _entries.Keys.Where(p => !_wanted.Contains(p)).ToList())
                Evict(path);

            session.Queue.Clear();
            foreach (var path in nearestFirst)
            {
                if (session.Loading.Contains(path) || session.Failed.Contains(path))
                    continue;

                if (!_entries.TryGetValue(path, out var entry))
                    session.Queue.Add(new Job(path, ThumbOnly: false));
                else if (entry.Thumb is null)
                    session.Queue.Add(new Job(path, ThumbOnly: true));
            }

            StartPumps(session);
        }

        /// <summary>Drops everything, e.g. when another folder is opened. The caller must have detached the images.</summary>
        public void Clear()
        {
            _session.Cts.Cancel();
            _session = new Session();
            _window = [];
            _wanted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (path, entry) in _entries.ToList())
            {
                Evicting?.Invoke(path, entry);
                entry.Dispose();
            }
            _entries.Clear();
            _bytes = 0;
        }

        public void Dispose() => Clear();

        private void StartPumps(Session session)
        {
            while (session.ActivePumps < MaxParallelLoads && session.Queue.Count > 0)
            {
                session.ActivePumps++;
                _ = PumpAsync(session);
            }
        }

        private async Task PumpAsync(Session session)
        {
            var token = session.Cts.Token;
            try
            {
                while (!token.IsCancellationRequested && TryTakeJob(session, out var job))
                {
                    session.Loading.Add(job.Path);
                    var thumbSize = ThumbSize;
                    PhotoEntry? entry = null;
                    Bitmap? thumb = null;
                    Exception? error = null;

                    try
                    {
                        // Only the file/GDI+ work runs on the pool; the result is private until published below.
                        if (job.ThumbOnly)
                            thumb = await Task.Run(() => ImageHelper.LoadThumbnail(job.Path, thumbSize));
                        else
                            entry = await Task.Run(() => ImageHelper.LoadPhoto(job.Path, thumbSize));
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }

                    // Back on the UI thread. If the session was abandoned meanwhile, nobody wants the result.
                    if (token.IsCancellationRequested)
                    {
                        entry?.Dispose();
                        thumb?.Dispose();
                        return;
                    }

                    session.Loading.Remove(job.Path);

                    if (error is not null)
                    {
                        session.Failed.Add(job.Path);
                        LoadFailed?.Invoke(job.Path, error);
                    }
                    else if (entry is not null)
                        Publish(session, job.Path, entry);
                    else if (thumb is not null)
                        PublishThumb(job.Path, thumb, thumbSize);
                }
            }
            finally
            {
                session.ActivePumps--;
            }
        }

        private bool TryTakeJob(Session session, out Job job)
        {
            while (session.Queue.Count > 0)
            {
                job = session.Queue[0];
                session.Queue.RemoveAt(0);

                if (!_wanted.Contains(job.Path) || session.Loading.Contains(job.Path))
                    continue;

                if (job.ThumbOnly)
                {
                    if (_entries.TryGetValue(job.Path, out var existing) && existing.Thumb is null)
                        return true;
                    continue;
                }

                if (_entries.ContainsKey(job.Path))
                    continue;

                // Once the memory budget is used up only the photo being viewed is still loaded.
                if (_bytes >= _maxBytes && !string.Equals(job.Path, _window[0], StringComparison.Ordinal))
                {
                    session.Queue.Clear();
                    break;
                }

                return true;
            }

            job = default;
            return false;
        }

        private void Publish(Session session, string path, PhotoEntry entry)
        {
            if (!_wanted.Contains(path) || _entries.ContainsKey(path))
            {
                entry.Dispose();
                return;
            }

            // The thumbnail size changed while this photo was loading
            var needsThumb = entry.ThumbSize != ThumbSize;
            if (needsThumb)
                entry.DropThumb();

            _entries[path] = entry;
            _bytes += entry.Bytes;
            Loaded?.Invoke(path, entry);
            EnforceBudget();

            if (needsThumb && _entries.ContainsKey(path))
            {
                session.Queue.Add(new Job(path, ThumbOnly: true));
                StartPumps(session);
            }
        }

        private void PublishThumb(string path, Bitmap thumb, int size)
        {
            if (size != ThumbSize || !_entries.TryGetValue(path, out var entry) || entry.Thumb is not null)
            {
                thumb.Dispose();
                return;
            }

            entry.SetThumb(thumb, size);
            ThumbReady?.Invoke(path, entry);
        }

        /// <summary>Drops the photos farthest from the current one until the cache fits its memory budget.</summary>
        private void EnforceBudget()
        {
            for (int i = _window.Count - 1; i > 0 && _bytes > _maxBytes; i--)
            {
                if (_entries.ContainsKey(_window[i]))
                    Evict(_window[i]);
            }
        }

        private void Evict(string path)
        {
            if (!_entries.Remove(path, out var entry)) return;

            Evicting?.Invoke(path, entry);
            _bytes -= entry.Bytes;
            entry.Dispose();
        }
    }
}
