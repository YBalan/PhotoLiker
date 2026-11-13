using System.Diagnostics.CodeAnalysis;

namespace PhotoLiker.Maui.Services;

/// <summary>One cached photo: the bytes of its file and, when a resizer is given, the smaller bytes of its thumbnail.</summary>
public sealed class PhotoCacheEntry
{
    internal PhotoCacheEntry(byte[] full) => Full = full;

    /// <summary>The bytes of the file, for the main viewer.</summary>
    public byte[] Full { get; }

    /// <summary>The downsized bytes for the thumbnail strip; null until made, and when the photo is already small or the resize failed.</summary>
    public byte[]? Thumb { get; internal set; }

    /// <summary>The <see cref="PhotoCacheService.ThumbSize"/> the thumbnail was made (or attempted) for; 0 when not yet.</summary>
    public int ThumbSize { get; internal set; }

    /// <summary>What the thumbnail strip shows: the downsized bytes, or the file itself until (or instead of) them.</summary>
    public byte[] ThumbOrFull => Thumb ?? Full;

    internal long Bytes => Full.LongLength + (Thumb?.LongLength ?? 0);
}

/// <summary>
/// Keeps the bytes of the photos around the photo being viewed, like the WinForms app's PhotoCache: a window of wanted
/// photos is loaded in the background, nearest first, at most two at a time and within a memory budget; photos that leave
/// the window are dropped. Thumbnails are downsized by a resizer the platform provides (null falls back to the full bytes).
///
/// Unlike the WinForms cache this one is thread-safe: events may be raised on pool threads, so UI subscribers marshal
/// to their main thread themselves.
/// </summary>
public sealed class PhotoCacheService : IDisposable
{
    private const int MaxParallelLoads = 2;

    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _reader;
    private readonly Func<byte[], int, CancellationToken, Task<byte[]?>>? _thumbResizer;

    private readonly Dictionary<string, PhotoCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private readonly List<(string Path, bool ThumbOnly)> _queue = [];
    private IReadOnlyList<string> _window = [];
    private HashSet<string> _wanted = new(StringComparer.Ordinal);
    private CancellationTokenSource _cts = new();
    private int _activePumps;
    private long _bytes;
    private int _thumbSize;

    public PhotoCacheService(
        long maxBytes,
        Func<string, CancellationToken, Task<byte[]?>>? reader = null,
        Func<byte[], int, CancellationToken, Task<byte[]?>>? thumbResizer = null)
    {
        _maxBytes = maxBytes;
        _reader = reader ?? ReadFileAsync;
        _thumbResizer = thumbResizer;
    }

    /// <summary>A photo finished loading and is now in the cache (or, for the photo asked of <see cref="GetOrLoadAsync"/> outside the window, at least read).</summary>
    public event Action<string, PhotoCacheEntry>? Loaded;

    /// <summary>The downsized thumbnail of a cached photo is ready.</summary>
    public event Action<string, PhotoCacheEntry>? ThumbReady;

    /// <summary>A photo of the window could not be read; it is not tried again until it is the shown photo once more.</summary>
    public event Action<string>? LoadFailed;

    /// <summary>The pixel size thumbnails are downsized to.</summary>
    public int ThumbSize
    {
        get { lock (_gate) return _thumbSize; }
    }

    /// <summary>How many bytes the cache holds right now.</summary>
    public long CachedBytes
    {
        get { lock (_gate) return _bytes; }
    }

    public bool TryGet(string path, [NotNullWhen(true)] out PhotoCacheEntry? entry)
    {
        lock (_gate)
            return _entries.TryGetValue(path, out entry);
    }

    /// <summary>Changes the thumbnail size: the thumbnails are dropped and rebuilt on the next <see cref="SetWindow"/>.</summary>
    public void SetThumbSize(int size)
    {
        lock (_gate)
        {
            if (size == _thumbSize)
                return;

            _thumbSize = size;
            foreach (var entry in _entries.Values)
            {
                _bytes -= entry.Thumb?.LongLength ?? 0;
                entry.Thumb = null;
                entry.ThumbSize = 0;
            }
        }
    }

    /// <summary>
    /// Sets which photos should be cached, nearest first (the first one is the photo being viewed). Photos outside the
    /// window are dropped, missing ones (and missing thumbnails) are loaded in that order in the background.
    /// </summary>
    public void SetWindow(IReadOnlyList<string> nearestFirst)
    {
        lock (_gate)
        {
            _window = [.. nearestFirst];
            _wanted = new HashSet<string>(nearestFirst, StringComparer.Ordinal);

            if (nearestFirst.Count > 0)
                _failed.Remove(nearestFirst[0]); // showing a photo again is a retry

            foreach (var path in _entries.Keys.Where(p => !_wanted.Contains(p)).ToList())
                EvictLocked(path);

            _queue.Clear();
            foreach (var path in nearestFirst)
            {
                if (_loading.Contains(path) || _failed.Contains(path))
                    continue;

                if (!_entries.TryGetValue(path, out var entry))
                    _queue.Add((path, ThumbOnly: false));
                else if (_thumbResizer is not null && entry.ThumbSize != _thumbSize)
                    _queue.Add((path, ThumbOnly: true));
            }

            StartPumpsLocked(_cts.Token);
        }
    }

    /// <summary>
    /// The photo to show right now: from the cache, or read immediately (joining the cache when it is in the window,
    /// returned transiently when not). Null when the file cannot be read.
    /// </summary>
    public async Task<PhotoCacheEntry?> GetOrLoadAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var cached))
                return cached;
        }

        byte[]? bytes;
        try
        {
            bytes = await _reader(path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            bytes = null;
        }

        if (bytes is null)
            return null;

        var entry = PublishFull(path, bytes);
        Loaded?.Invoke(path, entry);
        return entry;
    }

    /// <summary>Drops everything, e.g. when another folder is opened.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            _queue.Clear();
            _loading.Clear();
            _failed.Clear();
            _window = [];
            _wanted = new HashSet<string>(StringComparer.Ordinal);
            _entries.Clear();
            _bytes = 0;
        }
    }

    public void Dispose() => Clear();

    private void StartPumpsLocked(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        while (_activePumps < MaxParallelLoads && _queue.Count > 0)
        {
            _activePumps++;
            _ = Task.Run(() => PumpAsync(token), CancellationToken.None);
        }
    }

    private async Task PumpAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string path;
                bool thumbOnly;
                byte[]? full;
                int thumbSize;
                lock (_gate)
                {
                    if (!TryTakeJobLocked(out path, out thumbOnly, out full, out thumbSize))
                        return;

                    _loading.Add(path);
                }

                if (thumbOnly)
                    await LoadThumbAsync(path, full!, thumbSize, token).ConfigureAwait(false);
                else
                    await LoadFullAsync(path, token).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                _activePumps--;
                StartPumpsLocked(_cts.Token); // the queue may have grown, or the session changed, while this pump wound down
            }
        }
    }

    private async Task LoadFullAsync(string path, CancellationToken token)
    {
        byte[]? bytes;
        try
        {
            bytes = await _reader(path, token).ConfigureAwait(false);
        }
        catch
        {
            bytes = null;
        }

        if (token.IsCancellationRequested)
            return; // the session was abandoned: nobody wants the result (Clear has reset the bookkeeping)

        if (bytes is null)
        {
            lock (_gate)
            {
                _loading.Remove(path);
                _failed.Add(path);
            }

            LoadFailed?.Invoke(path);
            return;
        }

        var entry = PublishFull(path, bytes);
        Loaded?.Invoke(path, entry);
    }

    private async Task LoadThumbAsync(string path, byte[] full, int thumbSize, CancellationToken token)
    {
        byte[]? thumb;
        try
        {
            thumb = await _thumbResizer!(full, thumbSize, token).ConfigureAwait(false);
        }
        catch
        {
            thumb = null; // the full bytes serve as the thumbnail
        }

        if (token.IsCancellationRequested)
            return;

        PhotoCacheEntry? ready = null;
        lock (_gate)
        {
            _loading.Remove(path);
            if (_entries.TryGetValue(path, out var entry) && thumbSize == _thumbSize)
            {
                entry.Thumb = thumb;
                entry.ThumbSize = thumbSize;
                _bytes += thumb?.LongLength ?? 0;
                ready = entry;
            }
        }

        if (ready is not null)
            ThumbReady?.Invoke(path, ready);
    }

    /// <summary>Publishes freshly read bytes: into the cache when the photo is wanted (queueing its thumbnail), transiently when not.</summary>
    private PhotoCacheEntry PublishFull(string path, byte[] bytes)
    {
        lock (_gate)
        {
            _loading.Remove(path);
            if (_entries.TryGetValue(path, out var existing))
                return existing;

            var entry = new PhotoCacheEntry(bytes);
            if (!_wanted.Contains(path))
                return entry;

            _entries[path] = entry;
            _bytes += entry.Bytes;
            EnforceBudgetLocked();

            if (_thumbResizer is not null && _entries.ContainsKey(path))
            {
                _queue.Add((path, ThumbOnly: true));
                StartPumpsLocked(_cts.Token);
            }

            return entry;
        }
    }

    private bool TryTakeJobLocked(out string path, out bool thumbOnly, out byte[]? full, out int thumbSize)
    {
        thumbSize = _thumbSize;
        while (_queue.Count > 0)
        {
            (path, thumbOnly) = _queue[0];
            _queue.RemoveAt(0);

            if (!_wanted.Contains(path) || _loading.Contains(path))
                continue;

            if (thumbOnly)
            {
                if (_entries.TryGetValue(path, out var entry) && entry.ThumbSize != _thumbSize)
                {
                    full = entry.Full;
                    return true;
                }

                continue;
            }

            if (_entries.ContainsKey(path))
                continue;

            // Once the memory budget is used up only the photo being viewed is still loaded
            if (_bytes >= _maxBytes && (_window.Count == 0 || !string.Equals(path, _window[0], StringComparison.Ordinal)))
            {
                _queue.Clear();
                break;
            }

            full = null;
            return true;
        }

        path = string.Empty;
        thumbOnly = false;
        full = null;
        return false;
    }

    /// <summary>Drops the photos farthest from the shown one until the cache fits its memory budget (the shown photo is never dropped).</summary>
    private void EnforceBudgetLocked()
    {
        for (var i = _window.Count - 1; i > 0 && _bytes > _maxBytes; i--)
        {
            if (_entries.ContainsKey(_window[i]))
                EvictLocked(_window[i]);
        }
    }

    private void EvictLocked(string path)
    {
        if (_entries.Remove(path, out var entry))
            _bytes -= entry.Bytes;
    }

    private static async Task<byte[]?> ReadFileAsync(string path, CancellationToken token)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
