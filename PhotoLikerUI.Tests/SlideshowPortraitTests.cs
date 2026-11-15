using PhotoLiker.Core;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Transitions;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Portrait effect in a real slideshow: the frames are made in portrait mode (what is at the depth of the middle of the photo stays as it is, what is far is blurred), they are made again the other way
/// when the effect is changed while the show runs, and without a depth model the effect is not offered by the keys and the photos move like Ken Burns ones. The depth is made up here (the left three
/// quarters near, the right quarter far): what the model says is the business of the tests of the effects library.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowPortraitTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private List<PhotoFile> MakePhotos(int count)
    {
        var photos = new List<PhotoFile>();
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            for (var y = 0; y < 200; y++)
            for (var x = 0; x < 320; x++)
                bitmap.SetPixel(x, y, ((x / 4 + y / 4) % 2 == 0) ? Color.FromArgb(230, 40 + i * 30, 60) : Color.FromArgb(30, 200 - i * 30, 230));

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static SlideshowDepthOptions MadeUpDepth() => new((_, _, _) =>
        new DepthMap(16, 10, Enumerable.Range(0, 160).Select(i => i % 16 < 12 ? (byte)255 : (byte)0).ToArray()));

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowEffect effect, SlideshowDepthOptions? depth) =>
        new(photos, 0, SlideshowMode.OneByOne, 30, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: null, depth);

    private static void Show(SlideshowSession session, Func<SlideshowScreen, Task> body) =>
        Ui.Run(() => new Form(), async host =>
        {
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

    private static int DifferentPixels(uint[] a, uint[] b, int width, int height, int fromColumn, int toColumn)
    {
        var count = 0;
        for (var y = 0; y < height; y++)
        for (var x = fromColumn; x < toColumn; x++)
        {
            if ((a[y * width + x] & 0x00FFFFFF) != (b[y * width + x] & 0x00FFFFFF)) // the colours: the alpha of a frame is not looked at
                count++;
        }
        return count;
    }

    [Fact]
    public void WithTheEffectOn_ThePhotoIsBlurredWhereItIsFarFromTheFocus_AndNowhereElse()
    {
        var photos = MakePhotos(3);
        uint[]? original = null;
        int width = 0, height = 0;

        using (var plain = Session(photos, SlideshowEffect.Fade, MadeUpDepth()))
        {
            Show(plain, screen =>
            {
                Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
                Assert.False(frame.IsPortrait);
                original = frame.Pixels.ToArray();
                (width, height) = (frame.Width, frame.Height);
                return Task.CompletedTask;
            });
        }

        using var portrait = Session(photos, SlideshowEffect.Portrait, MadeUpDepth());
        Show(portrait, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.True(frame.IsPortrait, "the frame was taken in portrait mode before it was shown");
            Assert.Equal((width, height), (frame.Width, frame.Height));

            // The focus is on the middle of the photo, which is near: the left part is as it was; the far quarter on the right is blurred (the chessboard has lost its squares)
            Assert.Equal(0, DifferentPixels(original!, frame.Pixels, width, height, 0, width / 2));
            Assert.True(DifferentPixels(original!, frame.Pixels, width, height, width * 9 / 10, width) > height * width / 10 / 2, "the far side is blurred");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ChoosingPortraitDuringTheShow_MakesTheFramesAgainWithTheBlur_AndChoosingAnotherTakesItAway()
    {
        var photos = MakePhotos(4);
        using var session = Session(photos, SlideshowEffect.Fade, MadeUpDepth());

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");
            screen.Frames.TryGet(photos[1].OriginalFilePath, out var before);
            Assert.False(before!.IsPortrait);

            for (var key = 0; key < 5; key++)
                session.HandleKey(Keys.Down); // Slide, Zoom, KenBurns, Parallax, Portrait
            Assert.Equal(SlideshowEffect.Portrait, session.Effect);

            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.IsPortrait, 15000), "the photos that were ready were made again, in portrait mode");

            session.HandleKey(Keys.Down); // Random
            Assert.Equal(SlideshowEffect.Random, session.Effect);
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && !made.IsPortrait, 15000), "and made again as they are when the effect is another");
        });
    }

    [Fact]
    public void WithoutADepthModel_PortraitIsNotOffered_AndAPhotoIsNotBlurred()
    {
        var photos = MakePhotos(3);

        using (var session = Session(photos, SlideshowEffect.Portrait, depth: null))
        {
            Show(session, async screen =>
            {
                Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
                Assert.False(frame.IsPortrait);
                Assert.Null(frame.Depth);
                Assert.Equal(SlideshowEffect.Portrait, session.Effect); // the setting is not changed by the fallback
                Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo moves like a Ken Burns one all the same");
            });
        }

        using (var down = Session(photos, SlideshowEffect.KenBurns, depth: null))
        {
            Show(down, screen =>
            {
                down.HandleKey(Keys.Down);
                Assert.Equal(SlideshowEffect.Random, down.Effect); // Parallax and Portrait are skipped
                return Task.CompletedTask;
            });
        }

        using (var up = Session(photos, SlideshowEffect.Random, depth: null))
        {
            Show(up, screen =>
            {
                up.HandleKey(Keys.Up);
                Assert.Equal(SlideshowEffect.KenBurns, up.Effect);
                return Task.CompletedTask;
            });
        }
    }
}
