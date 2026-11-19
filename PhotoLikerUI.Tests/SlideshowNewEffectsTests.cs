using PhotoLiker.Core;
using PhotoLiker.Effects.Compositing;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Motion;
using PhotoLiker.Effects.Transitions;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The new effects, in a real slideshow window: every new change of photo animates from one photo to the next and ends on it at rest, the tilt turns a photo, the dolly zoom
/// moves one by its depth inside its place, and a random effect only chooses the effect (look, border and collage stay as they are set).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowNewEffectsTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly Rectangle PhotoRectangle = new(32, 0, 576, 360); // a 320 x 200 photo is made as large as the screen lets it be: 576 x 360, in the middle
    private static readonly Color[] Colours = [Color.FromArgb(220, 30, 30), Color.FromArgb(30, 200, 30), Color.FromArgb(30, 30, 220), Color.FromArgb(230, 210, 30)];

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private List<PhotoFile> MakePhotos(int count, bool plain = true)
    {
        var photos = new List<PhotoFile>();
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Colours[i % Colours.Length]);
            if (!plain)
            {
                for (var y = 0; y < 200; y++)
                for (var x = 0; x < 320; x++)
                    bitmap.SetPixel(x, y, Color.FromArgb(40 + x * 200 / 320, 60 + (y * 3 + i * 40) % 180, 200 - x * 150 / 320 + (x / 8 % 2) * 30));
            }

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static SlideshowSession Session(
        List<PhotoFile> photos, SlideshowEffect effect, double timeout = 1.5, SlideshowStyleOptions? style = null, SlideshowDepthOptions? depth = null) =>
        new(photos, 0, SlideshowMode.OneByOne, timeout, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: null, depth, style);

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

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 45 && Math.Abs(a.G - b.G) < 45 && Math.Abs(a.B - b.B) < 45;

    /// <summary>The share (0 to 1) of the pixels of the photo's place, sampled, that are about this colour.</summary>
    private static double Share(Bitmap bitmap, Color colour)
    {
        int near = 0, all = 0;
        for (var y = 2; y < PhotoRectangle.Height; y += 6)
        for (var x = PhotoRectangle.Left + 2; x < PhotoRectangle.Right; x += 6)
        {
            all++;
            if (Near(bitmap.GetPixel(x, y), colour))
                near++;
        }
        return near / (double)all;
    }

    private static double Brightness(Bitmap bitmap)
    {
        double sum = 0;
        var count = 0;
        for (var y = 2; y < PhotoRectangle.Height; y += 8)
        for (var x = PhotoRectangle.Left + 2; x < PhotoRectangle.Right; x += 8)
        {
            var pixel = bitmap.GetPixel(x, y);
            sum += (pixel.R + pixel.G + pixel.B) / 3.0;
            count++;
        }
        return sum / count;
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

    /// <summary>Pictures of the screen, as often as it can, for as long as the change lasts (at most 4 seconds), then the one of the photo at rest.</summary>
    private static async Task<(List<Bitmap> During, Bitmap Final)> WatchTheChange(SlideshowScreen screen)
    {
        Assert.True(await Ui.Until(() => screen.IsAnimating, 10000), "the timer changes the photo, and the change begins");

        var during = new List<Bitmap>();
        var watch = Stopwatch.StartNew();
        while (screen.IsAnimating && watch.Elapsed < TimeSpan.FromSeconds(4))
        {
            during.Add(Picture(screen));
            await Task.Delay(10);
        }
        Assert.False(screen.IsAnimating, "the change is over");
        return (during, Picture(screen));
    }

    // ── the new changes of photo ────────────────────────────────────────────

    [Theory]
    [InlineData(SlideshowEffect.PushUp)]
    [InlineData(SlideshowEffect.PushDown)]
    [InlineData(SlideshowEffect.CrossZoom)]
    [InlineData(SlideshowEffect.Wipe)]
    [InlineData(SlideshowEffect.Iris)]
    [InlineData(SlideshowEffect.Pixelate)]
    [InlineData(SlideshowEffect.Ripple)]
    [InlineData(SlideshowEffect.Cube)]
    [InlineData(SlideshowEffect.BlurDissolve)]
    [InlineData(SlideshowEffect.DipToBlack)]
    [InlineData(SlideshowEffect.DipToWhite)]
    public void ANewChange_AnimatesFromThePhotoToTheNext_AndEndsOnTheNextAtRest(SlideshowEffect effect)
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, effect);

        Show(session, async screen =>
        {
            using var before = Picture(screen);
            Assert.True(Share(before, Colours[0]) > 0.95, "the first photo is on the screen");

            var (during, rest) = await WatchTheChange(screen);
            try
            {
                Assert.True(during.Count >= 3, $"the change was seen in {during.Count} pictures");
                Assert.Equal(photos[1].OriginalFilePath, session.ShownPaths[0]);
                Assert.True(Share(rest, Colours[1]) > 0.95, "it ends on the next photo, at rest, with nothing of the change left");
                Assert.True(during.Any(picture => Share(picture, Colours[0]) < 0.95 && Share(picture, Colours[1]) < 0.95), "somewhere in the change the screen is neither the one photo nor the other");
            }
            finally
            {
                foreach (var picture in during)
                    picture.Dispose();
                rest.Dispose();
            }
        });
    }

    [Fact]
    public void TheDips_GoThroughTheirColour_BlackForBlackAndWhiteForWhite()
    {
        var photos = MakePhotos(3);

        using (var black = Session(photos, SlideshowEffect.DipToBlack))
        {
            Show(black, async screen =>
            {
                var (during, rest) = await WatchTheChange(screen);
                var darkest = during.Min(Brightness);
                var start = Brightness(during[0]);
                foreach (var picture in during) picture.Dispose();
                rest.Dispose();
                Assert.True(darkest < 40, $"the screen goes (nearly) black: {darkest:0}");
                Assert.True(start > darkest + 30, $"and it began lighter: {start:0}");
            });
        }

        using var white = Session(photos, SlideshowEffect.DipToWhite);
        Show(white, async screen =>
        {
            var (during, rest) = await WatchTheChange(screen);
            var lightest = during.Max(Brightness);
            foreach (var picture in during) picture.Dispose();
            rest.Dispose();
            Assert.True(lightest > 200, $"the screen goes (nearly) white: {lightest:0}");
        });
    }

    [Theory]
    [InlineData(SlideshowEffect.Wipe)]
    [InlineData(SlideshowEffect.Iris)]
    public void AWipeAndAnIris_ShowBothPhotosAtOnce_AsADissolveNeverDoes(SlideshowEffect effect)
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, effect);

        Show(session, async screen =>
        {
            var (during, final) = await WatchTheChange(screen);
            var both = during.Count(picture => Share(picture, Colours[0]) > 0.15 && Share(picture, Colours[1]) > 0.15);
            foreach (var picture in during) picture.Dispose();
            final.Dispose();
            Assert.True(both >= 2, $"the last photo and the next were both on the screen in {both} of {during.Count} pictures");
        });
    }

    [Fact]
    public void ThePlaceOfATiltingPhoto_CarriesItsTurn_AndAPhotoThatDoesNotTurnHasNone()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Tilt, timeout: 30);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var turned = SlideshowScreen.LayerOf(frame, new SlideshowLayer(1, 1.1, 0.01, 0.02, 0.035), SlideshowLayer.Plain, FrameCompositor.FullWeight, 640, 360);
            var straight = SlideshowScreen.LayerOf(frame, new SlideshowLayer(1, 1.1, 0.01, 0.02), SlideshowLayer.Plain, FrameCompositor.FullWeight, 640, 360);

            Assert.Equal(0.035, turned.Rotation);
            Assert.Equal(0, straight.Rotation);
            Assert.Equal(1.1, turned.Scale);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ThePushes_MoveThePhotosUpOrDown_SoTheEdgeBetweenThemCrossesTheScreenInThatDirection()
    {
        var photos = MakePhotos(3);

        double EdgeHeight(Bitmap picture)
        {
            // The first photo is red, the next green: where the green begins in the middle column of the screen (360 = the whole height)
            var column = PhotoRectangle.Left + PhotoRectangle.Width / 2;
            for (var y = 0; y < picture.Height; y++)
            {
                if (Near(picture.GetPixel(column, y), Colours[1]))
                    return y;
            }
            return picture.Height;
        }

        using (var up = Session(photos, SlideshowEffect.PushUp))
        {
            Show(up, async screen =>
            {
                var (during, rest) = await WatchTheChange(screen);
                var edges = during.Select(EdgeHeight).Where(edge => edge is > 5 and < 355).ToList();
                foreach (var picture in during) picture.Dispose();
                rest.Dispose();
                Assert.True(edges.Count >= 2, "the edge was seen on the screen");
                Assert.True(edges.First() > edges.Last(), $"going up: the next photo comes from the bottom edge towards the top: {string.Join(", ", edges)}");
            });
        }

        using var down = Session(photos, SlideshowEffect.PushDown);
        Show(down, async screen =>
        {
            var (during, rest) = await WatchTheChange(screen);
            // Going down the next photo is above: its bottom edge goes down; the first green pixel is the very top
            var greenRows = during.Select(picture =>
            {
                var column = PhotoRectangle.Left + PhotoRectangle.Width / 2;
                var rows = 0;
                for (var y = 0; y < picture.Height; y++)
                    if (Near(picture.GetPixel(column, y), Colours[1]))
                        rows++;
                return rows;
            }).Where(rows => rows is > 5 and < 355).ToList();
            foreach (var picture in during) picture.Dispose();
            rest.Dispose();
            Assert.True(greenRows.Count >= 2, "both photos were on the screen");
            Assert.True(greenRows.First() < greenRows.Last(), $"the next photo takes more and more of the screen: {string.Join(", ", greenRows)}");
        });
    }

    [Fact]
    public void ACubeChange_LeavesBlackAroundTheBox_AndNothingOfItWhenItIsOver()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Cube);

        Show(session, async screen =>
        {
            var (during, rest) = await WatchTheChange(screen);
            try
            {
                Assert.True(during.Any(picture => picture.GetPixel(PhotoRectangle.Left + 4, 4).GetBrightness() < 0.05f && picture.GetPixel(PhotoRectangle.Right - 5, 355).GetBrightness() < 0.05f
                    && Brightness(picture) > 20), "the box is smaller than the screen for a while: black in its corners, and still something on the screen");
                Assert.True(Near(rest.GetPixel(PhotoRectangle.Left + 4, 4), Colours[1]), "and it fills the screen again when it is over");
            }
            finally
            {
                foreach (var picture in during) picture.Dispose();
                rest.Dispose();
            }
        });
    }

    [Fact]
    public void KeysShowAPhotoAtOnce_WhateverTheChange()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Wipe, timeout: 30);

        Show(session, async screen =>
        {
            session.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => session.ShownPaths[0] == photos[1].OriginalFilePath, 10000));
            Assert.False(screen.IsAnimating, "a key is not a change by the effect");
            using var picture = Picture(screen);
            Assert.True(Share(picture, Colours[1]) > 0.95);
        });
    }

    // ── the tilt ────────────────────────────────────────────────────────────

    [Fact]
    public void ATiltingPath_IsADriftThatAlsoTurnsAFewDegrees_EitherWay_AndAPlanWithNoTiltNeverTurns()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.Tilt, timeout: 30);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var tilting = new SlideshowShowPlan(SlideshowTransition.None, 0, Motion: true, MotionSeconds: 3.4, Stopwatch.GetTimestamp(), Tilt: true);
            var still = tilting with { Tilt = false };

            var signs = new HashSet<int>();
            for (var i = 0; i < 200; i++)
            {
                var (path, parallax) = SlideshowScreen.MotionFor(frame, tilting);
                Assert.Null(parallax);
                var degrees = path!.Value.Tilt * 180 / Math.PI;
                Assert.InRange(Math.Abs(degrees), KenBurnsPath.MinTiltDegrees - 1e-9, KenBurnsPath.MaxTiltDegrees + 1e-9);
                signs.Add(Math.Sign(degrees));

                Assert.Equal(0, SlideshowScreen.MotionFor(frame, still).Path!.Value.Tilt);
            }
            Assert.Equal(2, signs.Count);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ATiltingPhoto_GoesOnMoving_OnTheScreen()
    {
        var photos = MakePhotos(3, plain: false);
        using var session = Session(photos, SlideshowEffect.Tilt, timeout: 6);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo is moving");

            using var early = Picture(screen);
            await Task.Delay(1500);
            using var late = Picture(screen);

            Assert.True(Different(early, late, PhotoRectangle), "what the photo shows moved");
        });
    }

    // ── the dolly zoom ──────────────────────────────────────────────────────

    private static SlideshowDepthOptions MadeUpDepth() => new((pixels, width, height) =>
        new DepthMap(16, 10, Enumerable.Range(0, 160).Select(i => i % 16 < 8 ? (byte)255 : (byte)0).ToArray()));

    [Fact]
    public void TheDollyZoom_MovesThePhotoByItsDepth_InsideItsPlace_AndAParallaxPlanStillGivesTheParallaxPath()
    {
        var photos = MakePhotos(3, plain: false);
        using var session = Session(photos, SlideshowEffect.DollyZoom, timeout: 6, depth: MadeUpDepth());

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.NotNull(frame.Depth);

            var dolly = new SlideshowShowPlan(SlideshowTransition.None, 0, Motion: true, MotionSeconds: 3.4, Stopwatch.GetTimestamp(), Parallax: true, Dolly: true);
            var (path, depthPath) = SlideshowScreen.MotionFor(frame, dolly);
            Assert.Null(path);
            Assert.IsType<DollyPath>(depthPath);
            Assert.IsType<ParallaxPath>(SlideshowScreen.MotionFor(frame, dolly with { Dolly = false }).Parallax);

            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "the photo is moving");
            using var before = Picture(screen);
            await Task.Delay(900);
            using var after = Picture(screen);

            Assert.True(Different(before, after, PhotoRectangle), "what the photo shows changed size");
            Assert.True(AllBlack(before, new Rectangle(Point.Empty, ScreenBounds.Size), PhotoRectangle), "nothing outside the photo, before");
            Assert.True(AllBlack(after, new Rectangle(Point.Empty, ScreenBounds.Size), PhotoRectangle), "and after: it breathes inside its place");
        });
    }

    [Fact]
    public void WithoutADepthModel_TheDollyZoomDriftsLikeKenBurns_AndTheSettingStays()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowEffect.DollyZoom, timeout: 3);

        Show(session, async screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.Null(frame.Depth);
            Assert.True(await Ui.Until(() => screen.IsAnimating, 5000), "it moves all the same");
            Assert.Equal(SlideshowEffect.DollyZoom, session.Effect);
        });
    }

    // ── a random effect only chooses the effect: look, border and collage are what they are set to ──

    private static SlideshowStyleOptions Style(SlideshowLook look = SlideshowLook.None, SlideshowBorder border = SlideshowBorder.None, SlideshowCollage collage = SlideshowCollage.None) =>
        new(look, Lut: null, border, collage);

    [Fact]
    public void WithARandomEffect_ALookABorderAndACollageAtNone_StayNone()
    {
        var photos = MakePhotos(12);
        using var session = Session(photos, SlideshowEffect.Random, timeout: 30); // no dates: all of one event, so a collage left to chance would have made pages

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => photos.Take(6).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000), "the photos next to the shown one are ready, every one with a frame of its own (no page)");

            foreach (var photo in photos.Take(6))
            {
                screen.Frames.TryGet(photo.OriginalFilePath, out var frame);
                Assert.Equal(SlideshowLook.None, frame!.AppliedLook);
                Assert.False(frame.HasCard);
            }
            Assert.Equal(SlideshowLook.None, session.Look);
            Assert.Equal(SlideshowBorder.None, session.Border);
            Assert.Equal(SlideshowCollage.None, session.Collage);

            session.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => session.ShownPaths[0] == photos[1].OriginalFilePath, 20000), "the next page is the next photo");
        });
    }

    [Fact]
    public void ChangingTheEffectToRandom_ChangesNothingOfTheLookTheBorderOrThePages_AndTheKeysNeverSayRandom()
    {
        var photos = MakePhotos(8);
        using var session = Session(photos, SlideshowEffect.DipToWhite, timeout: 30); // the effect before Random in the list

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => photos.Take(4).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000));

            session.HandleKey(Keys.Down);
            Assert.Equal(SlideshowEffect.Random, session.Effect);
            await Task.Delay(500); // the frames would be made again by now if anything were

            foreach (var photo in photos.Skip(1).Take(3))
            {
                Assert.True(screen.Frames.TryGet(photo.OriginalFilePath, out var frame), "the frame is still there, made as it was");
                Assert.Equal(SlideshowLook.None, frame.AppliedLook);
                Assert.False(frame.HasCard);
            }

            // L goes round the eleven looks (the last is the Random look) and back to None: the notice says None
            for (var press = 0; press < 11; press++)
                session.HandleKey(Keys.L);
            Assert.Equal(SlideshowLook.None, session.Look);
            Assert.Contains("None", screen.Notice);
            Assert.DoesNotContain("Random", screen.Notice);

            session.HandleKey(Keys.B); // None, Polaroid, Random: and round
            session.HandleKey(Keys.B);
            session.HandleKey(Keys.B);
            Assert.Equal(SlideshowBorder.None, session.Border);
            Assert.Contains("None", screen.Notice);
            Assert.DoesNotContain("Random", screen.Notice);

            session.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => session.ShownPaths[0] == photos[1].OriginalFilePath, 20000), "pages are photos");
        });
    }

    [Fact]
    public void ALookABorderAndACollageThatAreSet_AreWhatTheyAreSet_AlsoWithARandomEffect()
    {
        var photos = MakePhotos(8);

        using (var dressed = Session(photos, SlideshowEffect.Random, timeout: 30, style: Style(SlideshowLook.Vintage, SlideshowBorder.Polaroid)))
        {
            Show(dressed, async screen =>
            {
                Assert.True(await Ui.Until(() => photos.Take(5).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000), "the photos next to the shown one are ready");

                foreach (var photo in photos.Take(5))
                {
                    screen.Frames.TryGet(photo.OriginalFilePath, out var frame);
                    Assert.Equal(SlideshowLook.Vintage, frame!.AppliedLook);
                    Assert.True(frame.HasCard);
                }
            });
        }

        using var pages = Session(photos, SlideshowEffect.Random, timeout: 30, style: Style(collage: SlideshowCollage.Two));
        Show(pages, async screen =>
        {
            pages.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => pages.ShownPaths[0] == photos[2].OriginalFilePath, 20000), "pages of two, as set");
            pages.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => pages.ShownPaths[0] == photos[4].OriginalFilePath, 20000));
        });
    }
}
