using PhotoLiker.Core;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The keys that change how the slideshow looks (Down and Up, F, N, C, L, B, 1 2 3 4 and M) do not pause it, while the others (Right and Left, a key with no meaning) do; and the collage keys change the pages
/// of the show while it runs, from the page the person is looking at, with the frames of the old pages on the screen until the new ones are ready.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowKeysTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly Color[] Colours = [Color.FromArgb(220, 30, 30), Color.FromArgb(30, 200, 30), Color.FromArgb(30, 30, 220), Color.FromArgb(230, 210, 30), Color.FromArgb(235, 235, 235), Color.FromArgb(30, 200, 200), Color.FromArgb(160, 60, 200), Color.FromArgb(240, 140, 20)];

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private List<PhotoFile> MakePhotos(int count)
    {
        var photos = new List<PhotoFile>();
        for (var i = 0; i < count; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            using (var g = Graphics.FromImage(bitmap))
                g.Clear(Colours[i % Colours.Length]);

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowCollage collage = SlideshowCollage.None, SlideshowMode mode = SlideshowMode.OneByOne) =>
        new(photos, 0, mode, 30, SlideshowWatermark.None, [ScreenBounds], 0, SlideshowEffect.Fade, video: null, people: null, depth: null,
            new SlideshowStyleOptions(SlideshowLook.None, Lut: null, SlideshowBorder.None, collage));

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

    // ── which keys pause ────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.Down, true)]
    [InlineData(Keys.Up, true)]
    [InlineData(Keys.F, true)]
    [InlineData(Keys.N, true)]
    [InlineData(Keys.C, true)]
    [InlineData(Keys.L, true)]
    [InlineData(Keys.B, true)]
    [InlineData(Keys.M, true)]
    [InlineData(Keys.D1, true)]
    [InlineData(Keys.D2, true)]
    [InlineData(Keys.D3, true)]
    [InlineData(Keys.D4, true)]
    [InlineData(Keys.NumPad1, true)]
    [InlineData(Keys.NumPad2, true)]
    [InlineData(Keys.NumPad3, true)]
    [InlineData(Keys.NumPad4, true)]
    [InlineData(Keys.L | Keys.Shift, true)]       // a Caps Lock, or a finger that slipped
    [InlineData(Keys.M | Keys.Shift, true)]
    [InlineData(Keys.Right, false)]
    [InlineData(Keys.Left, false)]
    [InlineData(Keys.A, false)]
    [InlineData(Keys.D5, false)]
    [InlineData(Keys.Enter, false)]
    [InlineData(Keys.Down | Keys.Shift, false)]
    [InlineData(Keys.F | Keys.Control, false)]
    [InlineData(Keys.D2 | Keys.Shift, false)]     // that is an @, not a 2
    [InlineData(Keys.B | Keys.Alt, false)]
    public void OnlyTheKeysThatChangeTheLook_KeepTheShowGoing(Keys key, bool keepsGoing) =>
        Assert.Equal(keepsGoing, SlideshowSession.KeepsTheShowGoing(key));

    [Fact]
    public void ARunningShow_GoesOn_WhenALookKeyIsPressed_AndStaysPausedWhenItWasPaused()
    {
        var photos = MakePhotos(4);
        using var session = Session(photos);

        Show(session, async screen =>
        {
            Assert.False(screen.IsPaused);
            foreach (var key in new[] { Keys.Down, Keys.Up, Keys.F, Keys.N, Keys.C, Keys.L, Keys.B, Keys.D2, Keys.M, Keys.NumPad3 })
            {
                session.HandleKey(key);
                Assert.False(screen.IsPaused, $"{key} did not pause the show");
            }

            session.HandleKey(Keys.Right); // browsing does
            Assert.True(await Ui.Until(() => screen.IsPaused, 2000), "Right pauses the show");

            foreach (var key in new[] { Keys.Down, Keys.F, Keys.L, Keys.B, Keys.D4, Keys.M })
            {
                session.HandleKey(key);
                Assert.True(screen.IsPaused, $"{key} left the paused show paused");
            }

            session.HandleKey(Keys.A); // a key with no meaning pauses a show that runs: here it is paused already
            session.HandleKey(Keys.Space);
            Assert.False(screen.IsPaused, "Space continues it");
            session.HandleKey(Keys.A);
            Assert.True(screen.IsPaused, "a key with no meaning pauses it");
        });
    }

    // ── the collage keys ────────────────────────────────────────────────────

    [Fact]
    public void TheCollageKeys_ChangeThePagesWhileTheShowRuns_FromThePageThatIsShown()
    {
        var photos = MakePhotos(6);
        using var session = Session(photos);

        Show(session, async screen =>
        {
            Assert.Equal(SlideshowCollage.None, session.Collage);
            Assert.Equal(photos[0].OriginalFilePath, screen.CurrentPath);

            session.HandleKey(Keys.D2);
            Assert.Equal(SlideshowCollage.Two, session.Collage);
            Assert.True(screen.CurrentPath is { } before && screen.Frames.TryGet(before, out _), "the photo that is on the screen is kept until the page that takes its place is ready");
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[0].OriginalFilePath, 2), 15000), "the first page of two is on the screen");
            Assert.Equal(photos[0].OriginalFilePath, session.ShownPaths[0]);

            session.HandleKey(Keys.Right);
            Assert.True(await Ui.Until(() => session.ShownPaths[0] == photos[2].OriginalFilePath, 15000), "the next page begins with the third photo");
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[2].OriginalFilePath, 2), 15000));

            // Three: the page that holds the third photo is the first of three
            session.HandleKey(Keys.D3);
            Assert.Equal(SlideshowCollage.Three, session.Collage);
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[0].OriginalFilePath, 3), 15000), "the page of three that holds it");

            // The same key again takes the collage away, and the photo the page began with is shown
            session.HandleKey(Keys.D3);
            Assert.Equal(SlideshowCollage.None, session.Collage);
            Assert.True(await Ui.Until(() => screen.CurrentPath == photos[0].OriginalFilePath, 15000), "a photo again");

            // M: three, two, four in turn; 4: a grid; 1: no collage at once
            session.HandleKey(Keys.M);
            Assert.Equal(SlideshowCollage.Mixed, session.Collage);
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[0].OriginalFilePath, 3), 15000));
            session.HandleKey(Keys.D4);
            Assert.Equal(SlideshowCollage.Four, session.Collage);
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[0].OriginalFilePath, 4), 15000));
            session.HandleKey(Keys.D1);
            Assert.Equal(SlideshowCollage.None, session.Collage);
            Assert.True(await Ui.Until(() => screen.CurrentPath == photos[0].OriginalFilePath, 15000));
        });
    }

    [Fact]
    public void AShowThatCanNotHaveACollage_SaysSo_AndIsLeftAsItIs()
    {
        var photos = MakePhotos(1);
        using var session = Session(photos);

        Show(session, screen =>
        {
            session.HandleKey(Keys.D2);
            Assert.Equal(SlideshowCollage.None, session.Collage);
            Assert.Equal(photos[0].OriginalFilePath, screen.CurrentPath);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheCollageKeys_WorkInTheRandomMode_AndInAShowThatBeganWithACollage()
    {
        var photos = MakePhotos(8);
        using var session = Session(photos, SlideshowCollage.Two, SlideshowMode.Random);

        Show(session, async screen =>
        {
            Assert.StartsWith(photos[0].OriginalFilePath, screen.CurrentPath);
            Assert.EndsWith("|page2", screen.CurrentPath);

            session.HandleKey(Keys.D4);
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[0].OriginalFilePath, 4), 15000), "the page of four that holds the photo that was shown");

            session.HandleKey(Keys.D1);
            Assert.True(await Ui.Until(() => screen.CurrentPath == photos[0].OriginalFilePath, 15000), "no collage: the photo");
            Assert.Equal(photos[0].OriginalFilePath, session.ShownPaths[0]);
        });
    }

    [Fact]
    public void CollagesChosenInQuickSuccession_NeverBreakTheShow_AndEndOnTheOneThatWasChosenLast()
    {
        var photos = MakePhotos(8);
        using var session = Session(photos);

        Show(session, async screen =>
        {
            foreach (var key in new[] { Keys.D2, Keys.D3, Keys.D4, Keys.M, Keys.D1, Keys.D4, Keys.D2, Keys.D3, Keys.M, Keys.D4 })
            {
                session.HandleKey(key);
                await Task.Delay(15);

                // Whatever the pages are now, the frame that is on the screen is still kept (it is being painted): a frame that is let go of under the window would be painted disposed
                Assert.True(screen.CurrentPath is { } shown && screen.Frames.TryGet(shown, out _), $"the frame on the screen is kept after {key}");
            }

            Assert.Equal(SlideshowCollage.Four, session.Collage);
            Assert.True(await Ui.Until(() => screen.CurrentPath == FrameKeys.PageKey(photos[0].OriginalFilePath, 4), 20000), "the page of four is on the screen, not one of the pages that came and went");
            Assert.Equal(photos[0].OriginalFilePath, session.ShownPaths[0]);
        });
    }
}
