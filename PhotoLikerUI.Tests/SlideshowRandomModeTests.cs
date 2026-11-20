using PhotoLiker.Core;
using PhotoLiker.Effects.Looks;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Random value of Look, Border and Collage in a real slideshow: every photo gets a look of its own, and a card or not, and the pages hold one to four photos, all by chance (see
/// <see cref="RandomStyle"/>) that goes by the file and by a number drawn when the show is made, so a photo has what it had when it comes again. L, B and R reach them while the show runs, and
/// a setting that is not Random is what it says.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowRandomModeTests : IDisposable
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
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Color.FromArgb(40 + i * 7 % 200, 100, 180 - i * 5 % 120));

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static SlideshowSession Session(
        List<PhotoFile> photos, SlideshowLook look = SlideshowLook.None, SlideshowBorder border = SlideshowBorder.None, SlideshowCollage collage = SlideshowCollage.None,
        SlideshowEffect effect = SlideshowEffect.None, Func<string, DateTime?>? takenOf = null) =>
        new(photos, 0, SlideshowMode.OneByOne, 30, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null, people: null, depth: null,
            new SlideshowStyleOptions(look, Lut: null, border, collage, takenOf, Gpu: false));

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

    /// <summary>Every photo is taken a day after the one before: no two are of one event, so a collage still puts every photo on a page of its own (and the frames are those of the photos).</summary>
    private static Func<string, DateTime?> ADayApart(List<PhotoFile> photos) =>
        path => new DateTime(2020, 1, 1).AddDays(photos.FindIndex(photo => photo.OriginalFilePath == path));

    // ── the frames ──────────────────────────────────────────────────────────

    [Fact]
    public void WithARandomStyle_EveryFrameGetsItsOwnLookAndCard_ByItsFile_AndTheSameOnesWhenMadeAgain()
    {
        var photos = MakePhotos(30);

        Ui.Run(() => new Form(), async host =>
        {
            using var frames = new SlideshowFrames(ScreenBounds.Size, 1L << 28, 3, 4);
            frames.SetRandomStyle(look: true, border: true, seed: 42);
            frames.SetWindow(photos, pinned: null);
            Assert.True(await Ui.Until(() => photos.All(photo => frames.TryGet(photo.OriginalFilePath, out _)), 60000), "the frames are made");

            var looks = new HashSet<SlideshowLook>();
            var borders = new HashSet<SlideshowBorder>();
            var cards = 0;
            foreach (var photo in photos)
            {
                Assert.True(frames.TryGet(photo.OriginalFilePath, out var frame));
                Assert.Equal(RandomStyle.Look(FrameKeys.Of(photo), 42, hasTable: false), frame.AppliedLook);
                Assert.Equal(RandomStyle.Border(FrameKeys.Of(photo), 42), frame.Border); // which border, not only whether there is one
                Assert.Equal(RandomStyle.Card(FrameKeys.Of(photo), 42), frame.HasBorder);
                looks.Add(frame.AppliedLook);
                if (frame.HasBorder)
                {
                    cards++;
                    borders.Add(frame.Border);
                }
            }
            Assert.True(looks.Count >= 4, $"looks of many kinds: {string.Join(", ", looks)}");
            Assert.InRange(cards, 1, 29);
            Assert.True(borders.Count >= 2, $"borders of more than one kind: {string.Join(", ", borders)}");

            // Not left to chance any more: the frames are made again as they are set
            frames.SetRandomStyle(look: false, border: false, seed: 42);
            Assert.True(await Ui.Until(() => photos.All(photo => frames.TryGet(photo.OriginalFilePath, out var frame) && frame.AppliedLook == SlideshowLook.None && !frame.HasBorder), 60000),
                "made again with no look and no card");

            // What is set is what is used for the one that is not left to chance
            frames.SetLook(SlideshowLook.Sepia, null);
            frames.SetRandomStyle(look: false, border: true, seed: 7);
            Assert.True(await Ui.Until(() => photos.All(photo => frames.TryGet(photo.OriginalFilePath, out var frame) && frame.AppliedLook == SlideshowLook.Sepia), 60000), "all sepia");
            foreach (var photo in photos)
            {
                frames.TryGet(photo.OriginalFilePath, out var frame);
                Assert.Equal(RandomStyle.Border(FrameKeys.Of(photo), 7), frame!.Border);
            }
        });
    }

    // ── a show whose settings are Random ────────────────────────────────────

    [Fact]
    public void ARandomLook_GivesEveryPhotoItsOwnLook_WithTheSeedOfTheShow_AndTheSettingStaysRandom()
    {
        var photos = MakePhotos(12);
        using var session = Session(photos, look: SlideshowLook.Random);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => photos.Take(6).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000), "the photos next to the shown one are ready");

            var chosen = 0;
            foreach (var photo in photos.Take(6))
            {
                screen.Frames.TryGet(photo.OriginalFilePath, out var frame);
                var look = RandomStyle.Look(FrameKeys.Of(photo), session.StyleSeed, hasTable: false);
                Assert.Equal(look, frame!.AppliedLook);
                Assert.False(frame.HasBorder, "only the look is left to chance");
                if (look != SlideshowLook.None)
                    chosen++;
            }
            Assert.True(chosen >= 1, "not every photo is left as it is");
            Assert.Equal(SlideshowLook.Random, session.Look);
        });
    }

    [Fact]
    public void ARandomBorder_PutsSomePhotosOnACard_ByChance_AndTheLookIsLeftAlone()
    {
        var photos = MakePhotos(12);
        using var session = Session(photos, border: SlideshowBorder.Random, look: SlideshowLook.Sepia);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => photos.Take(6).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000));

            foreach (var photo in photos.Take(6))
            {
                screen.Frames.TryGet(photo.OriginalFilePath, out var frame);
                Assert.Equal(RandomStyle.Border(FrameKeys.Of(photo), session.StyleSeed), frame!.Border);
                Assert.Equal(SlideshowLook.Sepia, frame.AppliedLook); // what is set is what it says
            }
            Assert.Equal(SlideshowBorder.Random, session.Border);
        });
    }

    [Fact]
    public void ARandomCollage_HasPagesOfOneToFourPhotos_ByChance_AndTheSamePagesAllThroughTheShow()
    {
        var photos = MakePhotos(24);
        using var session = Session(photos, collage: SlideshowCollage.Random); // no dates: all of one event

        Show(session, async screen =>
        {
            var pages = RandomStyle.Pages(new DateTime?[photos.Count], session.StyleSeed);
            Assert.True(pages.Count >= 6 && pages.Select(page => page.Length).Distinct().Count() >= 2, "the pages are of different sizes");
            Assert.Equal(SlideshowCollage.Random, session.Collage);

            Assert.Equal(photos[0].OriginalFilePath, session.ShownPaths[0]);
            for (var page = 1; page < 7; page++)
            {
                session.HandleKey(Keys.Right);
                var first = photos[pages[page][0]].OriginalFilePath;
                Assert.True(await Ui.Until(() => session.ShownPaths[0] == first, 20000), $"page {page} begins with photo {pages[page][0]}");
            }
            for (var page = 5; page >= 1; page--)
            {
                session.HandleKey(Keys.Left); // back through the same pages
                var first = photos[pages[page][0]].OriginalFilePath;
                Assert.True(await Ui.Until(() => session.ShownPaths[0] == first, 20000), $"back to page {page}");
            }
        });
    }

    [Fact]
    public void ARandomCollage_StillEndsThePageWhereAnEventEnds()
    {
        var photos = MakePhotos(8);
        using var session = Session(photos, collage: SlideshowCollage.Random, takenOf: ADayApart(photos)); // a day apart: every page is one photo

        Show(session, async screen =>
        {
            for (var next = 1; next < 4; next++)
            {
                session.HandleKey(Keys.Right);
                var path = photos[next].OriginalFilePath;
                Assert.True(await Ui.Until(() => session.ShownPaths[0] == path && screen.Frames.TryGet(path, out _), 20000), $"page {next} is photo {next} alone");
            }
        });
    }

    // ── the keys ────────────────────────────────────────────────────────────

    [Fact]
    public void TheRKey_ChoosesTheRandomCollage_AndTheSameKeyAgainTakesItAway_AndItDoesNotPauseTheShow()
    {
        var photos = MakePhotos(24);
        using var session = Session(photos);

        Show(session, async screen =>
        {
            Assert.Equal(SlideshowCollage.None, session.Collage);
            Assert.True(SlideshowSession.KeepsTheShowGoing(Keys.R));
            Assert.True(SlideshowSession.KeepsTheShowGoing(Keys.R | Keys.Shift));
            Assert.False(SlideshowSession.KeepsTheShowGoing(Keys.R | Keys.Control));

            session.HandleKey(Keys.R);
            Assert.Equal(SlideshowCollage.Random, session.Collage);
            Assert.Contains("Random", screen.Notice);
            Assert.False(session.IsPaused, "the key does not pause the show");

            var pages = RandomStyle.Pages(new DateTime?[photos.Count], session.StyleSeed);
            var target = Enumerable.Range(1, pages.Count - 1).First(page => pages[page].Length >= 2);
            for (var page = 1; page <= target; page++)
            {
                session.HandleKey(Keys.Right);
                var first = photos[pages[page][0]].OriginalFilePath;
                Assert.True(await Ui.Until(() => session.ShownPaths[0] == first, 20000), $"page {page}");
            }

            session.HandleKey(Keys.R);
            Assert.Equal(SlideshowCollage.None, session.Collage);
            var second = photos[pages[target][1]].OriginalFilePath;
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(second, out _), 30000), "a page is a photo again");
        });
    }

    [Fact]
    public void TheLAndBKeys_ReachTheRandomLookAndTheRandomBorder_AndLeaveThemAgain()
    {
        var photos = MakePhotos(8);
        using var session = Session(photos);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => photos.Take(4).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000));

            for (var press = 0; press < 9; press++)
                session.HandleKey(Keys.L); // Vintage ... Season (without a table: the table is left out), then
            Assert.Equal(SlideshowLook.Season, session.Look);
            session.HandleKey(Keys.L);
            Assert.Equal(SlideshowLook.Random, session.Look);
            Assert.Contains("Random", screen.Notice);
            Assert.True(await Ui.Until(() => photos.Skip(1).Take(3).All(photo =>
                screen.Frames.TryGet(photo.OriginalFilePath, out var frame) && frame.AppliedLook == RandomStyle.Look(FrameKeys.Of(photo), session.StyleSeed, false)), 30000), "the frames are made with looks by chance");

            session.HandleKey(Keys.L);
            Assert.Equal(SlideshowLook.None, session.Look);
            Assert.True(await Ui.Until(() => photos.Skip(1).Take(3).All(photo =>
                screen.Frames.TryGet(photo.OriginalFilePath, out var frame) && frame.AppliedLook == SlideshowLook.None), 30000), "and as they are when the look is None");

            for (var press = 0; press < Borders.Styles.Count + 1; press++)
                session.HandleKey(Keys.B); // the Polaroid card, the frame, the film, the stamp, the glow, the shadow, and then
            Assert.Equal(SlideshowBorder.Random, session.Border);
            Assert.Contains("Random", screen.Notice);
            Assert.True(await Ui.Until(() => photos.Skip(1).Take(3).All(photo =>
                screen.Frames.TryGet(photo.OriginalFilePath, out var frame) && frame.Border == RandomStyle.Border(FrameKeys.Of(photo), session.StyleSeed)), 30000), "borders by chance");

            session.HandleKey(Keys.B);
            Assert.Equal(SlideshowBorder.None, session.Border);
        });
    }

    [Fact]
    public void TheRandomValues_AreTheSettingsWhateverTheEffectIs_AndTheEffectNeverMakesThemRandom()
    {
        var photos = MakePhotos(8);

        foreach (var effect in new[] { SlideshowEffect.None, SlideshowEffect.Random })
        {
            using var session = Session(photos, look: SlideshowLook.Random, effect: effect);
            Show(session, async screen =>
            {
                Assert.True(await Ui.Until(() => photos.Take(4).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)), 20000));
                foreach (var photo in photos.Take(4))
                {
                    screen.Frames.TryGet(photo.OriginalFilePath, out var frame);
                    Assert.Equal(RandomStyle.Look(FrameKeys.Of(photo), session.StyleSeed, hasTable: false), frame!.AppliedLook);
                }
            });
        }
    }
}
