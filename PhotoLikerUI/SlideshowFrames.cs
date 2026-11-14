using PhotoLiker.Search;
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

        private readonly Lazy<IReadOnlyList<FaceLabel>> _faces;
        private readonly Lazy<string?> _names;
        private readonly object _depthGate = new();
        private DepthMap? _depth;
        private bool _depthTried;

        /// <summary>Takes <paramref name="image"/>: its pixels are copied and it is disposed.</summary>
        /// <param name="facesOf">Says who is in the photo (by its file); asked once, the first time <see cref="Faces"/> or <see cref="Names"/> is wanted. Null: nobody is.</param>
        public SlideshowFrame(Bitmap image, DateTime? taken, string path, Func<string, IReadOnlyList<FaceLabel>>? facesOf = null)
        {
            _canvas = PixelCanvas.Take(image);
            Taken = taken;
            Path = path;
            Bytes = (long)_canvas.Width * _canvas.Height * 4;

            // Working out who is who takes a few milliseconds (more with many named faces): whoever wants it first pays, a thread of the pool that warms it ahead or, if it is wanted before,
            // the window. A failure is no reason to lose the photo: it is shown with nobody in it.
            _faces = new Lazy<IReadOnlyList<FaceLabel>>(() =>
            {
                try
                {
                    return facesOf?.Invoke(path) ?? [];
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return [];
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication);
            _names = new Lazy<string?>(() => FaceNames.Caption(_faces.Value), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Bitmap Image => _canvas.Image;

        /// <summary>The same pixels as an array, for <see cref="FrameCompositor"/>.</summary>
        public uint[] Pixels => _canvas.Pixels;

        public int Width => _canvas.Width;
        public int Height => _canvas.Height;
        public DateTime? Taken { get; }

        /// <summary>The faces of the photo, each with the name it is known by, if any; none when the folder has no index or the photo is not in it. Works them out at the first call (see the constructor).</summary>
        public IReadOnlyList<FaceLabel> Faces => _faces.Value;

        /// <summary>
        /// Where the faces are in the frame, as fractions of it, for a drift that keeps them in view (see <see cref="KenBurnsPath.Aimed"/>); every face the index has, named or not. A frame with a card
        /// (<see cref="AddCard"/>) has the photo on a part of it (<see cref="Inner"/>), and the faces are where they are on the card.
        /// </summary>
        public IReadOnlyList<PhotoRegion> FaceRegions =>
            [.. Faces.Select(label => new PhotoRegion(
                Inner.X + label.Face.X * Inner.Width, Inner.Y + label.Face.Y * Inner.Height, label.Face.Width * Inner.Width, label.Face.Height * Inner.Height))];

        /// <summary>Where the photo is in the frame, as fractions of it: all of it, or the part of it that is not the card (<see cref="AddCard"/>).</summary>
        public RectangleF Inner { get; private set; } = new(0, 0, 1, 1);

        /// <summary>Whether the photo is on a card (see <see cref="AddCard"/>).</summary>
        public bool HasCard { get; private set; }

        /// <summary>
        /// Puts the photo on a Polaroid card, in the pixels of the frame (see <see cref="PolaroidCard"/>), and writes the date it was taken in the margin below it, by hand. Done once, last of all (the depth,
        /// the portrait blur and the grade are those of the photo, not of the card), and the pixels are changed, so it is for a frame that is not on a screen yet.
        /// </summary>
        public void AddCard()
        {
            if (HasCard)
                return;

            try
            {
                PolaroidCard.Apply(_canvas.Pixels, Width, Height);
                var (x, y, width, height) = PolaroidCard.Inner;
                Inner = new RectangleF(x, y, width, height);
                HasCard = true;
                WriteDate();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // the photo is shown as it is
            }
        }

        private static readonly string[] HandwritingFonts = ["Ink Free", "Segoe Script", "Segoe Print", "Bradley Hand ITC", "Comic Sans MS"];

        /// <summary>The first of the handwriting fonts this computer has; a plain italic one if it has none.</summary>
        private static FontFamily HandwritingFamily()
        {
            var installed = FontFamily.Families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = HandwritingFonts.FirstOrDefault(installed.Contains);
            return name is null ? FontFamily.GenericSansSerif : new FontFamily(name);
        }

        private void WriteDate()
        {
            if (Taken is not { } date)
                return;

            var (x, y, width, height) = PolaroidCard.DateArea;
            var area = new RectangleF(x * Width, y * Height, width * Width, height * Height);
            using var family = HandwritingFamily();
            using var g = Graphics.FromImage(_canvas.Image);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(255, 56, 52, 60));
            using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            // As large as the margin lets it be, but not wider than it is
            var text = date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            var size = Math.Max(8f, area.Height * 0.5f);
            using (var probe = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var wide = g.MeasureString(text, probe, PointF.Empty, format).Width;
                if (wide > area.Width * 0.9f)
                    size = Math.Max(8f, size * area.Width * 0.9f / wide);
            }

            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString(text, font, brush, area, format);
        }

        /// <summary>The names of the people in the photo as one line for the corner (see <see cref="FaceNames.Caption"/>); null when there is nobody to write.</summary>
        public string? Names => _names.Value;

        /// <summary>Works out who is in the photo now, if that has not been done: for a thread of the pool, ahead of the first time the window asks.</summary>
        public void WarmPeople() => _ = _names.Value;

        /// <summary>
        /// How near each part of the photo is, for the Parallax effect; null until it has been worked out (<see cref="EnsureDepth"/>), and for ever when that was not possible. Never waits: the window
        /// reads it while it paints.
        /// </summary>
        public DepthMap? Depth => Volatile.Read(ref _depth);

        /// <summary>
        /// Works out the depth of the photo, once (a second call, or one that comes while the first is working, finds it done or waits for it): half a second of a processor, so only a thread of the pool
        /// may call it. A photo that the model fails on keeps no depth, and is not tried again.
        /// </summary>
        public void EnsureDepth(Func<uint[], int, int, DepthMap?>? estimate)
        {
            if (estimate is null)
                return;

            lock (_depthGate)
            {
                if (_depthTried)
                    return;

                _depthTried = true;
                try
                {
                    Volatile.Write(ref _depth, estimate(Pixels, Width, Height));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // the photo moves like any other (Ken Burns) without it
                }
            }
        }

        /// <summary>The look the pixels were graded with (see <see cref="ApplyLook"/>); none for the photo as it is.</summary>
        public SlideshowLook AppliedLook { get; private set; }

        /// <summary>
        /// Grades the colours of the photo in its own pixels (see <see cref="LookGrade"/>), once: a frame that has been graded is left as it is (it keeps no original to grade again). The pixels are changed, so it
        /// is for a frame that is not on a screen yet. The grain is the same every time the photo is made.
        /// </summary>
        public void ApplyLook(SlideshowLook look, CubeLut? lut)
        {
            if (look == SlideshowLook.None || AppliedLook != SlideshowLook.None)
                return;

            try
            {
                LookGrade.Apply(_canvas.Pixels, Width, Height, look, lut, Path.GetHashCode());
                AppliedLook = look;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // the photo is shown as it is
            }
        }

        /// <summary>Whether the pixels are those of the portrait photo (see <see cref="ApplyPortrait"/>): the background is blurred and the pixels of the original are gone.</summary>
        public bool IsPortrait { get; private set; }

        /// <summary>
        /// Takes the photo in portrait mode, in its own pixels: what is at the depth of the people (the largest face; else the middle of the photo) stays as it is, the nearer and the farther are blurred (see
        /// <see cref="DepthOfField"/>). Needs the depth (<see cref="EnsureDepth"/> first: a frame that has none is left as it is) and the faces, and the pixels are changed, so it is for a frame that is not on a
        /// screen yet: the one that makes it, before it is published. Done once.
        /// </summary>
        public void ApplyPortrait()
        {
            if (IsPortrait || Depth is not { } depth)
                return;

            try
            {
                var look = FocusLook.Portrait(DepthOfField.AutoFocus(depth, FaceRegions));
                var portrait = DepthOfField.Render(_canvas.Pixels, Width, Height, depth, look);
                Array.Copy(portrait, _canvas.Pixels, Width * Height);
                IsPortrait = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // the photo is shown as it is
            }
        }

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
        private readonly Func<string, IReadOnlyList<FaceLabel>>? _facesOf; // who is in a photo (the frames ask it when they are shown); null when there is no index to ask
        private volatile bool _warmPeople;   // the people are shown (the boxes or the names are on): they are worked out along with a frame, not when it is first painted
        private readonly Func<uint[], int, int, DepthMap?>? _depthOf; // works out the depth of a frame (the Parallax effect); null when the depth model is not here
        private volatile bool _warmDepth;    // the Parallax effect is on: the depth is worked out along with a frame, before it is published
        private volatile bool _warmPortrait; // the Portrait effect is on: a frame is taken in portrait mode (its pixels are changed) before it is published
        private volatile LookSetting? _look; // the colours of a frame are graded this way before it is published; null for the photo as it is
        private volatile bool _card;         // a frame is put on a Polaroid card before it is published (the last thing that is done to it)
        private int _variant;                // counts the times the way frames are made was changed (portrait, grade, card); UI thread only
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

        /// <param name="facesOf">Says who is in a photo; called on the threads that make the frames and, when a frame has not been asked yet, on the window's thread, so it must be safe to call from any thread. Null: nobody is.</param>
        /// <param name="depthOf">Works out how near each part of a picture is, from its pixels and size (called on the threads that make the frames, so it must be safe to call from any thread). Null: there is no depth, and no photo has any.</param>
        public SlideshowFrames(
            Size screen, long maxBytes, int minFrames, int maxParallelLoads, Func<string, IReadOnlyList<FaceLabel>>? facesOf = null,
            Func<uint[], int, int, DepthMap?>? depthOf = null)
        {
            _depthOf = depthOf;
            _screen = screen;
            _maxBytes = maxBytes;
            _minFrames = minFrames;
            _maxParallelLoads = maxParallelLoads;
            _facesOf = facesOf;
        }

        /// <summary>
        /// Says whether the people are shown. When they are, every frame that is made has them worked out right away (on its thread of the pool), and turning them on works out those of the frames
        /// that are here already (in the background, two at a time), so that showing a photo with its people takes no more time than showing it without.
        /// </summary>
        public void SetPeopleShown(bool shown)
        {
            _warmPeople = shown;
            if (!shown || _facesOf is null || _frames.Count == 0)
                return;

            var here = _frames.Values.ToList();
            _ = Task.Run(() => Parallel.ForEach(here, new ParallelOptions { MaxDegreeOfParallelism = 2 }, frame => frame.WarmPeople()));
        }

        /// <summary>
        /// Says whether the Parallax effect is on. When it is, every frame that is made has its depth worked out before it is published (on its thread of the pool, so a photo is shown with its depth as
        /// soon as it is shown at all), and turning it on works out the depth of the frames that are here already (in the background, two at a time).
        /// </summary>
        public void SetDepthWanted(bool wanted)
        {
            _warmDepth = wanted && _depthOf is not null;
            if (!_warmDepth || _frames.Count == 0)
                return;

            var here = _frames.Values.ToList();
            var estimate = _depthOf;
            _ = Task.Run(() => Parallel.ForEach(here, new ParallelOptions { MaxDegreeOfParallelism = 2 }, frame => frame.EnsureDepth(estimate)));
        }

        /// <summary>
        /// Says whether the Portrait effect is on. The frames are made in portrait mode while it is (and as they are, when it is not), and the frames that are here are not the right ones when that changes: the
        /// photo of a portrait frame has no original to go back to. So they are let go of, except the two that are on the screen, and made again, the nearest first. A frame that was being made when this was
        /// said is made again when it is done.
        /// </summary>
        public void SetPortraitWanted(bool wanted)
        {
            wanted = wanted && _depthOf is not null;
            if (_warmPortrait == wanted)
                return;

            _warmPortrait = wanted;
            Remake();
        }

        /// <summary>
        /// Says how the photos are graded (see <see cref="SlideshowLook"/>; <paramref name="lut"/> is the table of the <see cref="SlideshowLook.Lut"/> look). Frames are made with the grade from now on, and the
        /// frames that are here are made again (a graded frame has no original), except the ones on the screen. Said again with what it is already, it does nothing.
        /// </summary>
        public void SetLook(SlideshowLook look, CubeLut? lut)
        {
            var next = look == SlideshowLook.None || (look == SlideshowLook.Lut && lut is null) ? null : new LookSetting(look, lut);
            if (_look?.Look == next?.Look && ReferenceEquals(_look?.Lut, next?.Lut))
                return;

            _look = next;
            Remake();
        }

        private sealed record LookSetting(SlideshowLook Look, CubeLut? Lut);

        /// <summary>
        /// Says what the photos are put in (see <see cref="SlideshowBorder"/>). Frames are made with the card from now on (or without), and the frames that are here are made again (a photo on a card has no
        /// original), except the ones on the screen. Said again with what it is already, it does nothing.
        /// </summary>
        public void SetBorder(SlideshowBorder border)
        {
            var card = border == SlideshowBorder.Polaroid;
            if (_card == card)
                return;

            _card = card;
            Remake();
        }

        /// <summary>Lets go of the frames that are here, except the ones on the screen, and makes them again (the nearest first) the way that is wanted now.</summary>
        private void Remake()
        {
            _variant++; // a frame that is being made now comes out for the old state: the pump sees that, and makes it again
            foreach (var path in _frames.Keys.Where(path => !IsPinned(path)).ToList())
                Evict(path);
            SetWindow(_window, _pinned, _alsoPinned);
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
            _wanted = new HashSet<string>(mostNeededFirst.Select(FrameKeys.Of), StringComparer.Ordinal);

            foreach (var path in _frames.Keys.Where(p => !_wanted.Contains(p) && !IsPinned(p)).ToList())
                Evict(path);

            _queue.Clear();
            foreach (var photo in mostNeededFirst)
            {
                var path = FrameKeys.Of(photo);
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
                    var path = FrameKeys.Of(photo);
                    _loading.Add(path);
                    SlideshowFrame? frame = null;
                    Exception? error = null;
                    var variant = _variant; // the way frames are made as this one begins: if it has changed when it is done, this one is made the old way

                    try
                    {
                        // Only the file/GDI+ work runs on the pool; the result is private until it is published below.
                        frame = await Task.Run(() =>
                        {
                            // A page of the collage is made of its photos (which are read here one after the other, each as large as its cell lets it be); a photo, of itself
                            var made = photo is CollagePage page
                                ? CollageFrames.Make(page, _screen, (member, cell) => ImageHelper.LoadSlide(member, cell))
                                : ImageHelper.LoadSlide(photo, _screen, _facesOf);
                            if (_warmPeople)
                                made.WarmPeople();
                            if (_warmDepth)
                                made.EnsureDepth(_depthOf);
                            if (_warmPortrait)
                                made.ApplyPortrait();
                            if (_look is { } grade)
                                made.ApplyLook(grade.Look, grade.Lut);
                            if (_card)
                                made.AddCard();
                            return made;
                        });
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

                    if (error is null && frame is not null && variant != _variant)
                    {
                        // The portrait blur, the grade or the card was chosen or taken away while this frame was being made: it is not the frame that is wanted now, and a frame that is dressed
                        // keeps no original to dress again. It is let go of and made again, first of all (the photo is wanted: it is in the window still, and whoever waits for it waits for this one)
                        frame.Dispose();
                        if (_wanted.Contains(path) && !_frames.ContainsKey(path) && !_failed.Contains(path))
                            _queue.Insert(0, photo);
                        continue;
                    }

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

                var path = FrameKeys.Of(photo);
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

            // The Parallax effect was chosen while this frame was being made: the pool thread that made it looked at _warmDepth before that, and SetDepthWanted worked on the frames that were here
            // then, not on this one (it was on its way). A frame that has its depth, or has been tried, costs nothing here.
            if (_warmDepth && frame.Depth is null && _depthOf is { } estimate)
                _ = Task.Run(() => frame.EnsureDepth(estimate));

            Loaded?.Invoke(path, frame);
            EnforceBudget();
        }

        /// <summary>Drops the frames farthest from what is needed next until they fit the memory budget (never the shown one, never fewer than the few kept).</summary>
        private void EnforceBudget()
        {
            for (var i = _window.Count - 1; i > 0 && _bytes > _maxBytes && _frames.Count > _minFrames; i--)
            {
                var path = FrameKeys.Of(_window[i]);
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
