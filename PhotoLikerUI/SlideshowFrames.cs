using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// Pixels in a Format32bppPArgb bitmap that can be reached as an array as well: the array is kept where the garbage collector does not move it and the
    /// bitmap is a view of it, so GDI+ can draw the pixels (a plain copy to the screen) and <see cref="FrameCompositor"/> can work on them (the effects), with
    /// one copy of them in memory and no pointers in our code.
    /// </summary>
    internal sealed class PixelCanvas : IDisposable
    {
        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = GC.AllocateUninitializedArray<uint>(width * height, pinned: true);
            Image = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, Marshal.UnsafeAddrOfPinnedArrayElement(Pixels, 0));
        }

        public int Width { get; }
        public int Height { get; }
        public uint[] Pixels { get; }
        public Bitmap Image { get; }

        /// <summary>A canvas with the pixels of <paramref name="source"/>, which is disposed: it has been used up.</summary>
        public static PixelCanvas Take(Bitmap source)
        {
            var canvas = new PixelCanvas(source.Width, source.Height);
            try
            {
                using (var g = Graphics.FromImage(canvas.Image))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, new Rectangle(0, 0, canvas.Width, canvas.Height));
                }
                source.Dispose();
                return canvas;
            }
            catch
            {
                canvas.Dispose();
                source.Dispose();
                throw;
            }
        }

        public void Dispose() => Image.Dispose(); // the array goes when nobody has the canvas any more, the bitmap must not outlive it
    }

    /// <summary>
    /// A photo of the slideshow, already scaled to the screen it is shown on (so it is drawn as it is, with no scaling at all), and the date it was taken,
    /// for the watermark. It is a tenth of the size of the decoded photo that <see cref="PhotoCache"/> keeps: 8 MB on a full HD screen, for a 24 MP photo of 96 MB.
    /// </summary>
    internal sealed class SlideshowFrame : IDisposable
    {
        private readonly PixelCanvas _canvas;

        /// <summary>Takes <paramref name="image"/>: its pixels are copied and it is disposed.</summary>
        public SlideshowFrame(Bitmap image, DateTime? taken, string path, string? people = null)
        {
            _canvas = PixelCanvas.Take(image);
            Taken = taken;
            Path = path;
            People = string.IsNullOrWhiteSpace(people) ? null : people;
            Bytes = (long)_canvas.Width * _canvas.Height * 4;
        }

        public Bitmap Image => _canvas.Image;

        /// <summary>The same pixels as an array, for <see cref="FrameCompositor"/>.</summary>
        public uint[] Pixels => _canvas.Pixels;

        public int Width => _canvas.Width;
        public int Height => _canvas.Height;
        public DateTime? Taken { get; }

        /// <summary>The names of the people in the photo, as one line, for the corner; null when they are not asked for or there are none to write.</summary>
        public string? People { get; }

        /// <summary>The file the photo is from.</summary>
        public string Path { get; }

        public long Bytes { get; }

        public void Dispose() => _canvas.Dispose();
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
        private readonly Func<string, string?>? _peopleOf; // the line of names for a photo, worked out along with its frame; null when the names are not shown
        private readonly Dictionary<string, SlideshowFrame> _frames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
        private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _cts = new();
        private readonly List<PhotoFile> _queue = [];
        private IReadOnlyList<PhotoFile> _window = [];
        private HashSet<string> _wanted = new(StringComparer.Ordinal);
        private string? _pinned;
        private string? _alsoPinned;
        private long _bytes;
        private int _activePumps;

        /// <param name="peopleOf">Says who is in a photo (by its file), as a line of names; called on the thread that makes the frame, so it must be safe to call from any thread.</param>
        public SlideshowFrames(Size screen, long maxBytes, int minFrames, int maxParallelLoads, Func<string, string?>? peopleOf = null)
        {
            _screen = screen;
            _maxBytes = maxBytes;
            _minFrames = minFrames;
            _maxParallelLoads = maxParallelLoads;
            _peopleOf = peopleOf;
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
        /// it stays whatever happens, because it is being painted; so is <paramref name="alsoPinned"/>, the one that is leaving it while an effect lasts.
        /// </summary>
        public void SetWindow(IReadOnlyList<PhotoFile> mostNeededFirst, string? pinned, string? alsoPinned = null)
        {
            _window = mostNeededFirst;
            _pinned = pinned;
            _alsoPinned = alsoPinned;
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

        private bool IsPinned(string path) =>
            string.Equals(path, _pinned, StringComparison.Ordinal) || string.Equals(path, _alsoPinned, StringComparison.Ordinal);

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
                        frame = await Task.Run(() => ImageHelper.LoadSlide(photo, _screen, _peopleOf));
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
