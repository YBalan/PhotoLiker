using PhotoLiker.Core;
using PhotoLiker.Effects.Motion;
using PhotoLiker.Effects.Transitions;
using PhotoLiker.Search;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Ken Burns drift of a real slideshow keeps the faces the index found in view: the frames have the faces worked out before they are shown (on a thread of the pool, not on the window's), only the
/// effects that drift ask for them, and the path a photo is given never takes a face out of the photo. The faces are made up here (a face against the left edge of every photo).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowFaceAwareTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);

    private readonly TempFolder _folder = new();
    private readonly object _gate = new();
    private readonly List<int> _askedOnThreads = [];
    private readonly List<(string Path, int Thread)> _asked = [];
    private int _uiThread;

    public void Dispose() => _folder.Dispose();

    private List<PhotoFile> MakePhotos(int count)
    {
        var photos = new List<PhotoFile>();
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(40 + i * 30, 90, 160));

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    /// <summary>The faces of a photo as the index would give them: one against the left edge, and a passer-by in the far corner.</summary>
    private IReadOnlyList<FaceLabel> MadeUpFaces(string path)
    {
        lock (_gate)
        {
            _askedOnThreads.Add(Environment.CurrentManagedThreadId);
            _asked.Add((path, Environment.CurrentManagedThreadId));
        }

        var photo = new IndexedPhoto(Path.GetFileName(path), 1, 1);
        return
        [
            new FaceLabel(new IndexedFace(photo, 0.0f, 0.4f, 0.12f, 0.2f, 0.9f, manual: false, []), null, Confirmed: false, 0f),
            new FaceLabel(new IndexedFace(photo, 0.95f, 0.9f, 0.03f, 0.04f, 0.9f, manual: false, []), null, Confirmed: false, 0f),
        ];
    }

    private SlideshowSession Session(List<PhotoFile> photos, SlideshowEffect effect, bool showFaces = false) =>
        new(photos, 0, SlideshowMode.OneByOne, 3, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: new SlideshowPeopleOptions(MadeUpFaces, showFaces, ShowNames: false), depth: null);

    private void Show(SlideshowSession session, Func<SlideshowScreen, Task> body) =>
        Ui.Run(() => new Form(), async host =>
        {
            _uiThread = Environment.CurrentManagedThreadId;
            host.BeginInvoke(() => session.Run(host));
            Assert.True(await Ui.Until(() => session.Screens.Count == 1 && session.Screens[0].HasPhoto, 20000), "the first photo is shown");
            try
            {
                await body(session.Screens[0]);
            }
            finally
            {
                session.HandleKey(Keys.Escape);
            }
        });

    /// <summary>Where the head round the face against the edge is on the photo (fractions): from 0 across, 0.3 to 0.65 down; what the drift must keep in view.</summary>
    private static readonly PhotoRegion Head = new PhotoRegion(0.0, 0.4, 0.12, 0.2).WithHead();

    private static bool KeepsHeadInView(KenBurnsPath path)
    {
        foreach (var progress in new[] { 0, 0.25, 0.5, 0.75, 1 })
        {
            var layer = path.At(progress);
            var left = layer.OffsetX + layer.Scale * (Head.X - 0.5) + 0.5;
            var top = layer.OffsetY + layer.Scale * (Head.Y - 0.5) + 0.5;
            var right = layer.OffsetX + layer.Scale * (Head.Right - 0.5) + 0.5;
            var bottom = layer.OffsetY + layer.Scale * (Head.Bottom - 0.5) + 0.5;
            if (left < -1e-9 || top < -1e-9 || right > 1 + 1e-9 || bottom > 1 + 1e-9)
                return false;
        }
        return true;
    }

    [Fact]
    public void KenBurnsPhotos_DriveWithTheirFacesInView_EveryTime()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.KenBurns);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var plan = new SlideshowShowPlan(SlideshowTransition.None, 0, Motion: true, MotionSeconds: 3.4, Stopwatch.GetTimestamp());

            // A random path about a random point would take the face out of the photo about half the time; this one never does
            for (var i = 0; i < 300; i++)
            {
                var (path, parallax) = SlideshowScreen.MotionFor(frame, plan);
                Assert.Null(parallax);
                Assert.NotNull(path);
                Assert.True(KeepsHeadInView(path!.Value), $"the face was cut by {path}");
            }
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AFrameHasItsFacesWorkedOutBeforeItIsShown_OnAThreadOfThePool()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.KenBurns);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");
            int[] threads;
            lock (_gate)
                threads = [.. _askedOnThreads];

            Assert.True(threads.Length >= 2, "the faces of the shown photo and of the next one were asked for");
            Assert.DoesNotContain(_uiThread, threads);
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(SlideshowEffect.None)]
    [InlineData(SlideshowEffect.Fade)]
    [InlineData(SlideshowEffect.Slide)]
    [InlineData(SlideshowEffect.Zoom)]
    public void AnEffectThatDoesNotDrift_DoesNotAskForTheFaces(SlideshowEffect effect)
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, effect);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");
            lock (_gate)
                Assert.Empty(_askedOnThreads);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ChoosingKenBurnsDuringTheShow_WorksTheFacesOutOfThePhotosThatAreReady()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Fade);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");
            lock (_gate)
                Assert.Empty(_askedOnThreads);

            session.HandleKey(Keys.Down); // Fade, Slide, Zoom, KenBurns: three keys
            session.HandleKey(Keys.Down);
            session.HandleKey(Keys.Down);
            Assert.Equal(SlideshowEffect.KenBurns, session.Effect);

            // The photo that is shown starts to move at once, so the window may be the first to ask for its faces (a few milliseconds, once); the ones that are only ready are worked out in the background
            Assert.True(await Ui.Until(() => { lock (_gate) return _asked.Any(a => a.Path == photos[1].OriginalFilePath); }, 10000), "the photo that was ready was asked for");
            lock (_gate)
                Assert.DoesNotContain(_asked, a => a.Path != photos[0].OriginalFilePath && a.Thread == _uiThread);
        });
    }

    [Fact]
    public void ThePeopleThatAreShown_AreAskedForWhateverTheEffect()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Fade, showFaces: true);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => { lock (_gate) return _askedOnThreads.Count >= 1; }, 10000), "the faces are asked for, as before");
            await Task.CompletedTask;
        });
    }
}
