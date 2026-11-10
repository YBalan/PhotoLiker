using PhotoLiker.Effects.Depth;
using System.Drawing.Imaging;
using PhotoLiker.Core;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Parallax effect in a real slideshow: the frames get their depth, the photo moves inside the place it has, the keys offer the effect only when there is a depth model, and
/// an effect chosen during the show works out the depth of the photos that are ready already. The depth is made up here (a near left half and a far right half): what the model says is the
/// business of the tests of the search library.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowParallaxTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly Rectangle PhotoRectangle = new(32, 0, 576, 360); // a 320 x 200 photo is made as large as the screen lets it be: 576 x 360, in the middle

    private readonly TempFolder _folder = new();
    private int _calls;
    private (int Width, int Height) _lastSize;

    public void Dispose() => _folder.Dispose();

    private List<PhotoFile> MakePhotos(int count)
    {
        var photos = new List<PhotoFile>();
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            for (var y = 0; y < 200; y++)
            for (var x = 0; x < 320; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(40 + x * 200 / 320, 60 + (y * 3 + i * 40) % 180, 200 - x * 150 / 320 + (x / 8 % 2) * 30));

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    /// <summary>A depth that is not the real thing: the left half near, the right half far.</summary>
    private SlideshowDepthOptions MadeUpDepth() => new((pixels, width, height) =>
    {
        Interlocked.Increment(ref _calls);
        _lastSize = (width, height);
        return new DepthMap(16, 10, Enumerable.Range(0, 160).Select(i => i % 16 < 8 ? (byte)255 : (byte)0).ToArray());
    });

    private SlideshowSession Session(List<PhotoFile> photos, SlideshowEffect effect, SlideshowDepthOptions? depth, double timeout = 2) =>
        new(photos, 0, SlideshowMode.OneByOne, timeout, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: null, depth);

    /// <summary>Runs a show on a real window until the body is done with it, then ends it with Escape.</summary>
    private static void Show(SlideshowSession session, Func<SlideshowScreen, Task> body) =>
        Ui.Run(() => new Form(), async host =>
        {
            host.BeginInvoke(() => session.Run(host)); // modal: it comes back when the show ends
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

    private static Bitmap Picture(SlideshowScreen screen)
    {
        var bitmap = new Bitmap(screen.ClientSize.Width, screen.ClientSize.Height);
        screen.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static bool Different(Bitmap a, Bitmap b, Rectangle where)
    {
        for (var y = where.Top; y < where.Bottom; y += 2)
        for (var x = where.Left; x < where.Right; x += 2)
        {
            if (a.GetPixel(x, y) != b.GetPixel(x, y))
                return true;
        }
        return false;
    }

    private static bool AllBlack(Bitmap bitmap, Rectangle inside, Rectangle outsideOf)
    {
        for (var y = inside.Top; y < inside.Bottom; y++)
        for (var x = inside.Left; x < inside.Right; x++)
        {
            if (outsideOf.Contains(x, y))
                continue;

            var pixel = bitmap.GetPixel(x, y);
            if (pixel.R > 2 || pixel.G > 2 || pixel.B > 2)
                return false;
        }
        return true;
    }

    [Fact]
    public void WithTheEffectOn_ThePhotoIsGivenItsDepth_AtTheSizeOfItsFrame()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Parallax, MadeUpDepth());

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.NotNull(frame.Depth); // before it was shown: the frame is not published without it
            Assert.Equal(16, frame.Depth!.Width);
            Assert.Equal((576, 360), _lastSize);
            Assert.True(_calls >= 1);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePhoto_MovesInsideItsPlace_WhatIsAroundItStaysBlack()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Parallax, MadeUpDepth(), timeout: 3);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo is moving");

            using var before = Picture(screen);
            await Task.Delay(700);
            using var after = Picture(screen);

            Assert.True(Different(before, after, PhotoRectangle), "what the photo shows moved");
            Assert.True(AllBlack(before, new Rectangle(Point.Empty, ScreenBounds.Size), PhotoRectangle), "nothing outside the photo, before");
            Assert.True(AllBlack(after, new Rectangle(Point.Empty, ScreenBounds.Size), PhotoRectangle), "nothing outside the photo, after: it did not grow into the bars as a Ken Burns photo does");
        });
    }

    [Fact]
    public void WithoutADepthModel_TheEffectMovesLikeKenBurns_AndThePhotoHasNoDepth()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Parallax, depth: null, timeout: 3);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.Null(frame.Depth);
            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo moves all the same");
            Assert.Equal(SlideshowEffect.Parallax, session.Effect); // the setting is not changed by the fallback
        });
    }

    [Fact]
    public void ADepthThatCouldNotBeWorkedOut_LeavesThePhotoMovingLikeKenBurns()
    {
        var photos = MakePhotos(3);
        var broken = new SlideshowDepthOptions((_, _, _) => throw new InvalidOperationException("the model failed"));
        using var session = Session(photos, SlideshowEffect.Parallax, broken, timeout: 3);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.Null(frame.Depth);
            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000));
        });
    }

    [Fact]
    public void TheKeys_OfferParallax_OnlyWhenThereIsADepthModel()
    {
        var photos = MakePhotos(2);

        using (var without = Session(photos, SlideshowEffect.KenBurns, depth: null, timeout: 30))
        {
            Show(without, _ =>
            {
                without.HandleKey(Keys.Down);
                Assert.Equal(SlideshowEffect.Random, without.Effect); // Parallax is skipped
                without.HandleKey(Keys.Up);
                Assert.Equal(SlideshowEffect.KenBurns, without.Effect);
                return Task.CompletedTask;
            });
        }

        using var with = Session(photos, SlideshowEffect.KenBurns, MadeUpDepth(), timeout: 30);
        Show(with, _ =>
        {
            with.HandleKey(Keys.Down);
            Assert.Equal(SlideshowEffect.Parallax, with.Effect);
            with.HandleKey(Keys.Down);
            Assert.Equal(SlideshowEffect.Random, with.Effect);
            with.HandleKey(Keys.Up);
            Assert.Equal(SlideshowEffect.Parallax, with.Effect);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void ChoosingParallaxDuringTheShow_WorksOutTheDepthOfThePhotosThatAreReady_AndTheShownOneMoves()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Fade, MadeUpDepth(), timeout: 30);

        Show(session, async screen =>
        {
            Assert.Equal(0, _calls); // Fade does not need it
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.Null(frame.Depth);

            for (var i = 0; i < 4; i++)
                session.HandleKey(Keys.Down); // Slide, Zoom, KenBurns, Parallax
            Assert.Equal(SlideshowEffect.Parallax, session.Effect);

            Assert.True(await Ui.Until(() => frame.Depth is not null, 15000), "the photo that is shown has its depth now");
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var next) && next.Depth is not null, 15000), "so have the ones that are ready");
            Assert.True(screen.IsAnimating, "the photo moves: by its depth from the next change on, now as Ken Burns");
        });
    }

    [Fact]
    public void AFrameThatWasBeingMadeWhenParallaxWasChosen_GetsItsDepthWhenItIsPublished()
    {
        var photos = MakePhotos(3);

        Ui.Run(() => new Form(), async host =>
        {
            using var frames = new SlideshowFrames(ScreenBounds.Size, 1L << 28, 3, 3, facesOf: null, depthOf: MadeUpDepth().Estimate);
            frames.SetWindow(photos, pinned: photos[0].OriginalFilePath);

            // The pool makes the frames, looks at "is the effect on?" (no), and waits to hand them over to this thread, which is kept busy (it does not yield): nothing is in the list of frames yet
            Thread.Sleep(1500);
            Assert.False(frames.TryGet(photos[0].OriginalFilePath, out _), "nothing is published yet");

            frames.SetDepthWanted(true); // the effect is chosen: the frames that are here (none) are worked on

            Assert.True(await Ui.Until(() => photos.All(photo => frames.TryGet(photo.OriginalFilePath, out _)), 10000), "the frames are published");
            foreach (var photo in photos)
            {
                Assert.True(frames.TryGet(photo.OriginalFilePath, out var frame));
                Assert.True(await Ui.Until(() => frame.Depth is not null, 10000), $"{Path.GetFileName(photo.OriginalFilePath)} has its depth, though the effect was chosen after it was made");
            }
        });
    }

    [Fact]
    public void DepthIsWorkedOutOnce_ForAFrame_WhoeverAsks()
    {
        var photos = MakePhotos(1);
        using var session = Session(photos, SlideshowEffect.Parallax, MadeUpDepth(), timeout: 30);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var before = _calls;

            screen.Frames.SetDepthWanted(true);
            frame.EnsureDepth(MadeUpDepth().Estimate);
            await Task.Delay(300);

            Assert.Equal(before, _calls);
        });
    }
}
