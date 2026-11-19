using PhotoLiker.Core;
using PhotoLiker.Effects.Depth;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The graphics chip in a real slideshow window (see <see cref="SlideshowGpu"/>): the heavy steps of effects are drawn by it when the computer has a usable one and the setting is on, the light ones never
/// are, the textures of the frames are made ahead and let go of with them, a step that fails turns the chip off without anyone noticing, and with the setting off the processor does everything. The
/// tests that need a chip say nothing when the computer has none (the build machine of the release has none): what they check there is the processor's way, which the other tests check.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowGpuTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly Color[] Colours = [Color.FromArgb(220, 30, 30), Color.FromArgb(30, 200, 30), Color.FromArgb(30, 30, 220)];

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
            {
                var baseColour = Colours[i % Colours.Length];
                bitmap.SetPixel(x, y, Color.FromArgb(Math.Min(255, baseColour.R + x / 8), Math.Min(255, baseColour.G + y / 8), baseColour.B));
            }

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static SlideshowDepthOptions MadeUpDepth() => new((pixels, width, height) =>
        new DepthMap(16, 10, Enumerable.Range(0, 160).Select(i => i % 16 < 8 ? (byte)255 : (byte)0).ToArray()));

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowEffect effect, bool gpu = true, double timeout = 1.5, SlideshowDepthOptions? depth = null) =>
        new(photos, 0, SlideshowMode.OneByOne, timeout, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: null, depth,
            new SlideshowStyleOptions(SlideshowLook.None, Lut: null, Gpu: gpu));

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

    private static Bitmap Picture(SlideshowScreen screen)
    {
        var bitmap = new Bitmap(screen.ClientSize.Width, screen.ClientSize.Height);
        screen.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    private static async Task WatchTheChange(SlideshowScreen screen)
    {
        Assert.True(await Ui.Until(() => screen.IsAnimating, 10000), "the timer changes the photo, and the change begins");
        var watch = Stopwatch.StartNew();
        while (screen.IsAnimating && watch.Elapsed < TimeSpan.FromSeconds(4))
        {
            using var picture = Picture(screen);
            await Task.Delay(10);
        }
        Assert.False(screen.IsAnimating, "the change is over");
    }

    // ── what the chip draws ─────────────────────────────────────────────────

    [Theory]
    [InlineData(SlideshowEffect.Ripple)]
    [InlineData(SlideshowEffect.Cube)]
    [InlineData(SlideshowEffect.BlurDissolve)]
    public void TheHeavyChanges_AreDrawnByTheChip_AndEndOnTheNextPhoto(SlideshowEffect effect)
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, effect);

        Show(session, async screen =>
        {
            if (!session.Gpu.WaitUntilStarted())
                return; // no usable chip on this computer

            await WatchTheChange(screen);

            Assert.True(session.Gpu.Steps > 0, $"the chip drew {session.Gpu.Steps} steps of the {effect} change");
            Assert.Equal(photos[1].OriginalFilePath, session.ShownPaths[0]);
            using var rest = Picture(screen);
            var middle = rest.GetPixel(320, 180);
            Assert.True(middle.G > middle.R + 60, $"it ends on the next photo (green), at rest: {middle}");
        });
    }

    [Theory]
    [InlineData(SlideshowEffect.Fade)]
    [InlineData(SlideshowEffect.Wipe)]
    [InlineData(SlideshowEffect.Iris)]
    [InlineData(SlideshowEffect.Pixelate)]
    [InlineData(SlideshowEffect.DipToBlack)]
    [InlineData(SlideshowEffect.CrossZoom)]
    public void TheLightEffects_AreLeftToTheProcessor_WhichDoesThemInTheTimeTheReadBackWouldTake(SlideshowEffect effect)
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, effect);

        Show(session, async screen =>
        {
            session.Gpu.WaitUntilStarted();
            await WatchTheChange(screen);

            Assert.Equal(0, session.Gpu.Steps);
            Assert.Equal(photos[1].OriginalFilePath, session.ShownPaths[0]);
        });
    }

    [Fact]
    public void ADriftingPhoto_IsLeftToTheProcessor_ItNeedsNoTurnAndNoDepth()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.KenBurns, timeout: 6);

        Show(session, async screen =>
        {
            session.Gpu.WaitUntilStarted();
            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo is moving");
            await Task.Delay(500);
            using var picture = Picture(screen);

            Assert.Equal(0, session.Gpu.Steps);
        });
    }

    [Fact]
    public void ATiltingPhoto_IsDrawnByTheChip_AllTheTimeItMoves()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Tilt, timeout: 6);

        Show(session, async screen =>
        {
            if (!session.Gpu.WaitUntilStarted())
                return;

            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo is moving");
            using var early = Picture(screen);
            var before = session.Gpu.Steps;
            await Task.Delay(500);
            using var late = Picture(screen);

            Assert.True(session.Gpu.Steps > before, "the chip draws the photo as it turns");
        });
    }

    [Fact]
    public void TheDepthEffects_AreDrawnByTheChip_InsideThePlaceOfThePhoto()
    {
        var photos = MakePhotos(3);

        foreach (var effect in new[] { SlideshowEffect.Parallax, SlideshowEffect.DollyZoom })
        {
            using var session = Session(photos, effect, timeout: 6, depth: MadeUpDepth());
            Show(session, async screen =>
            {
                if (!session.Gpu.WaitUntilStarted())
                    return;

                Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), $"{effect}: the photo is moving");
                using var picture = Picture(screen);
                Assert.True(session.Gpu.Steps > 0, $"{effect}: the chip drew the photo by its depth");
                // The photo is 576 wide in the middle of 640: the bars at the sides stay black, as with the processor
                for (var y = 0; y < 360; y += 20)
                {
                    Assert.True(picture.GetPixel(10, y).GetBrightness() < 0.02f, $"{effect}: left bar at {y}");
                    Assert.True(picture.GetPixel(630, y).GetBrightness() < 0.02f, $"{effect}: right bar at {y}");
                }
            });
        }
    }

    // ── when the chip is not used ───────────────────────────────────────────

    [Fact]
    public void WithTheSettingOff_TheChipIsNotStarted_AndTheProcessorDoesTheChange()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Cube, gpu: false);

        Show(session, async screen =>
        {
            Assert.False(session.Gpu.WaitUntilStarted(), "there is no chip");
            Assert.Null(session.Gpu.Renderer);

            await WatchTheChange(screen);
            Assert.Equal(0, session.Gpu.Steps);
            using var rest = Picture(screen);
            Assert.True(rest.GetPixel(320, 180).G > rest.GetPixel(320, 180).R + 60, "it ends on the next photo all the same");
        });
    }

    [Fact]
    public void AStepThatFails_TurnsTheChipOff_AndTheShowGoesOnWithTheProcessor()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Ripple, timeout: 1.5);

        Show(session, async screen =>
        {
            session.Gpu.WaitUntilStarted();
            session.Gpu.Failed(new InvalidOperationException("the device was removed")); // what a step that throws does

            Assert.Null(session.Gpu.Renderer);
            Assert.Equal("the device was removed", session.Gpu.Problem);

            await WatchTheChange(screen);
            Assert.Equal(0, session.Gpu.Steps);
            Assert.Equal(photos[1].OriginalFilePath, session.ShownPaths[0]);
            using var rest = Picture(screen);
            Assert.True(rest.GetPixel(320, 180).G > rest.GetPixel(320, 180).R + 60, "the processor drew the change, and it ends on the next photo");
        });
    }

    // ── the textures of the frames ──────────────────────────────────────────

    [Fact]
    public void TheTexturesOfTheFrames_AreMadeAheadForAnEffectThatMayUseTheChip_AndNotForOneThatMayNot()
    {
        var photos = MakePhotos(5);

        using (var heavy = Session(photos, SlideshowEffect.Cube, timeout: 30))
        {
            Show(heavy, async screen =>
            {
                if (!heavy.Gpu.WaitUntilStarted())
                    return;

                Assert.True(await Ui.Until(() => heavy.Gpu.Renderer!.TextureCount >= 3, 20000), $"textures were made while the frames were made: {heavy.Gpu.Renderer!.TextureCount}");
                var made = heavy.Gpu.Renderer!.TextureCount;
                Assert.True(made <= photos.Count);
            });
        }

        using var light = Session(photos, SlideshowEffect.Fade, timeout: 30);
        Show(light, async screen =>
        {
            if (!light.Gpu.WaitUntilStarted())
                return;

            Assert.True(await Ui.Until(() => photos.All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)) || screen.Frames.TryGet(photos[2].OriginalFilePath, out _), 20000));
            await Task.Delay(300);
            Assert.Equal(0, light.Gpu.Renderer!.TextureCount);
        });
    }

    [Fact]
    public void ChoosingAnEffectThatUsesTheChip_MakesTheTexturesOfTheFramesThatAreReady_AndAFrameThatIsLetGoOfLosesItsTexture()
    {
        var photos = MakePhotos(5);
        using var session = Session(photos, SlideshowEffect.Fade, timeout: 30);

        Show(session, async screen =>
        {
            if (!session.Gpu.WaitUntilStarted())
                return;

            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 20000), "the next photo is ready");
            Assert.Equal(0, session.Gpu.Renderer!.TextureCount);

            for (var key = 0; key < 16; key++)
                session.HandleKey(Keys.Down); // Slide, Zoom, KenBurns, Parallax, Portrait, Tilt: and on to Ripple
            Assert.True(SlideshowEffects_MayUseGpu(session.Effect), $"{session.Effect} uses the chip");

            Assert.True(await Ui.Until(() => session.Gpu.Renderer!.TextureCount >= 2, 20000), "the frames that were ready got their textures");

            // The frames are let go of when the show is over, and the textures with them: the renderer is disposed with the show
            var renderer = session.Gpu.Renderer!;
            screen.Frames.SetWindow([], pinned: null);
            Assert.True(await Ui.Until(() => renderer.TextureCount <= 1, 10000), "frames that are dropped drop their textures");
        });
    }

    private static bool SlideshowEffects_MayUseGpu(SlideshowEffect effect) => PhotoLiker.Effects.Transitions.SlideshowEffects.MayUseGpu(effect);
}
