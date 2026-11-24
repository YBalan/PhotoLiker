using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// The thumbnails of the Make a book dialog: each photo is decoded once, small, on a thread of the pool (a few at a time, so that a slow disk or a router is not asked for everything at once),
    /// and only if it is still on the screen when its turn comes (a person who scrolls fast does not wait for the photos that went by). What was decoded is kept, up to a number of pictures, the
    /// oldest that are not on the screen going first. Everything but the decoding is on the thread of the window.
    /// </summary>
    internal sealed class ThumbnailCache : IDisposable
    {
        /// <summary>The longest side of a thumbnail as it is kept; the view draws it smaller or (a little) larger.</summary>
        public const int DecodeSize = 320;

        private const int Capacity = 700;
        private const int ParallelLoads = 3;

        private readonly Func<string, Bitmap?> _decode;
        private readonly SynchronizationContext _ui;
        private readonly SemaphoreSlim _gate = new(ParallelLoads);
        private readonly Dictionary<string, Bitmap> _ready = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new();
        private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
        private volatile HashSet<string> _wanted = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        /// <param name="decode">Makes the thumbnail of a photo, or null when it cannot be read. Runs on any thread; the default reads the file and decodes it small.</param>
        public ThumbnailCache(Func<string, Bitmap?>? decode = null)
        {
            _decode = decode ?? (path => Decode(path));
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        /// <summary>Raised on the thread of the window when a thumbnail (or the news that there is none) is there for a path.</summary>
        public event Action<string>? Loaded;

        /// <summary>How many thumbnails are kept.</summary>
        public int Count => _ready.Count;

        /// <summary>
        /// Says which photos are on the screen now: only those are loaded, and those are not thrown away. Call it before asking for them when the screen changes.
        /// </summary>
        public void Want(IEnumerable<string> paths) => _wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The thumbnail if it is there. Otherwise null, and its loading is started if it is on the list of <see cref="Want"/>; <paramref name="failed"/> says that the photo cannot be read, so
        /// there will be none.
        /// </summary>
        public Bitmap? TryGet(string path, out bool failed)
        {
            failed = false;
            if (_disposed)
                return null;
            if (_ready.TryGetValue(path, out var bitmap))
                return bitmap;
            if (_failed.Contains(path))
            {
                failed = true;
                return null;
            }

            // On a thread of the pool: the semaphore is free when few photos are loading, which would let the decoding run right here, on the thread that asks (the window's), once for each photo asked for
            if (_loading.Add(path))
                _ = Task.Run(() => LoadAsync(path));
            return null;
        }

        /// <summary>Forgets a photo, so that it is read again when it is next shown (the file was changed).</summary>
        public void Forget(string path)
        {
            if (_ready.Remove(path, out var bitmap))
                bitmap.Dispose();
            _failed.Remove(path);
        }

        private async Task LoadAsync(string path)
        {
            Bitmap? made = null;
            var skipped = false;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || !_wanted.Contains(path))
                {
                    skipped = true; // it went off the screen while it waited
                }
                else
                {
                    try
                    {
                        made = _decode(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or ExternalException or OutOfMemoryException)
                    {
                        made = null;
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            _ui.Post(_ =>
            {
                _loading.Remove(path);
                if (_disposed)
                {
                    made?.Dispose();
                    return;
                }
                if (skipped)
                {
                    Loaded?.Invoke(path); // asked for again by the next paint if it is on the screen by then
                    return;
                }

                if (made is null)
                {
                    _failed.Add(path);
                }
                else
                {
                    _ready[path] = made;
                    _order.Enqueue(path);
                    Trim();
                }

                Loaded?.Invoke(path);
            }, null);
        }

        /// <summary>Throws away the oldest thumbnails that are not on the screen when there are too many.</summary>
        private void Trim()
        {
            var wanted = _wanted;
            var rounds = _order.Count;
            while (_ready.Count > Capacity && rounds-- > 0)
            {
                var path = _order.Dequeue();
                if (!_ready.TryGetValue(path, out var bitmap))
                    continue; // forgotten already

                if (wanted.Contains(path))
                {
                    _order.Enqueue(path); // on the screen: it stays
                    continue;
                }

                _ready.Remove(path);
                bitmap.Dispose();
            }
        }

        /// <summary>The photo as a picture of at most <paramref name="size"/> pixels on its long side, upright: read whole and decoded at the reduced size straight away.</summary>
        internal static Bitmap Decode(string path, int size = DecodeSize)
        {
            var decoded = WicPhotoDecoder.DecodeToFit(File.ReadAllBytes(path), size, size);
            var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb);
            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, decoded.Width, decoded.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var rowBytes = decoded.Width * 4;
                    for (var y = 0; y < decoded.Height; y++)
                        Marshal.Copy(decoded.Pbgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var bitmap in _ready.Values)
                bitmap.Dispose();
            _ready.Clear();
            _order.Clear();
        }
    }
}
