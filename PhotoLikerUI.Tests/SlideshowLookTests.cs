using PhotoLiker.Core;
using PhotoLiker.Effects.Looks;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The colour grade (Look) in a real slideshow: frames are graded as they are made, the L key chooses the next look and the frames that are ready are made again with it, a look that is the photo as it
/// is leaves the pixels alone, and the Lut look needs a table (without one it grades nothing and L leaves it out).
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowLookTests : IDisposable
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
                g.Clear(Color.FromArgb(120 + i * 10, 140, 160));

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            photos.Add(new PhotoFile(path, string.Empty));
        }
        return photos;
    }

    private static CubeLut Inverting() => CubeLut.Parse(new StringReader("LUT_1D_SIZE 2\n1 1 1\n0 0 0"));

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowStyleOptions? look) =>
        new(photos, 0, SlideshowMode.OneByOne, 30, SlideshowWatermark.None, [ScreenBounds], 0, SlideshowEffect.None, video: null, people: null, depth: null, look);

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

    private static (int R, int G, int B) Middle(SlideshowFrame frame)
    {
        var pixel = frame.Pixels[frame.Height / 2 * frame.Width + frame.Width / 2];
        return ((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF));
    }

    [Fact]
    public void FramesAreGradedAsTheyAreMade_AndAPhotoWithNoLookIsLeftAlone()
    {
        var photos = MakePhotos(3);

        (int R, int G, int B) plain = default;
        using (var none = Session(photos, look: null))
        {
            Show(none, screen =>
            {
                Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
                Assert.Equal(SlideshowLook.None, frame.AppliedLook);
                plain = Middle(frame);
                return Task.CompletedTask;
            });
        }

        using var sepia = Session(photos, new SlideshowStyleOptions(SlideshowLook.Sepia, Lut: null));
        Show(sepia, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.Equal(SlideshowLook.Sepia, frame.AppliedLook);
            var (r, g, b) = Middle(frame);
            Assert.True(r > g && g > b, $"brown: {r},{g},{b}");
            Assert.NotEqual(plain, (r, g, b));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheLKey_ChoosesTheNextLook_AndMakesTheReadyFramesAgainWithIt_AndGoesRound()
    {
        var photos = MakePhotos(4);
        using var session = Session(photos, look: null);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");
            Assert.Equal(SlideshowLook.None, session.Look);

            session.HandleKey(Keys.L);
            Assert.Equal(SlideshowLook.Vintage, session.Look);
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.AppliedLook == SlideshowLook.Vintage, 10000), "the frame that was ready was made again, graded");

            // Without a table: Vintage, Film, Sepia, BlackAndWhite, Cinema, and round to None (Lut is left out)
            var seen = new List<SlideshowLook>();
            for (var press = 0; press < 5; press++)
            {
                session.HandleKey(Keys.L);
                seen.Add(session.Look);
            }
            Assert.Equal([SlideshowLook.Film, SlideshowLook.Sepia, SlideshowLook.BlackAndWhite, SlideshowLook.Cinema, SlideshowLook.None], seen);
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.AppliedLook == SlideshowLook.None, 10000), "and graded no more when the look is None");
        });
    }

    [Fact]
    public void LooksChosenInQuickSuccession_LeaveNoFrameOfAnOldLook_EvenOneThatWasBeingMadeWhenTheKeyWasPressed()
    {
        var photos = MakePhotos(8);
        using var session = Session(photos, look: null);

        Show(session, async screen =>
        {
            // Eleven presses with the frames of the next photos being made all the while: None, Vintage, Film, Sepia, BlackAndWhite, Cinema, round, ..., 11 on is Cinema
            for (var press = 0; press < 11; press++)
            {
                session.HandleKey(Keys.L);
                await Task.Delay(12);
            }
            Assert.Equal(SlideshowLook.Cinema, session.Look);

            // Every frame that is ready (the shown one is the one that was on the screen before: it keeps its look) is graded the way the show is now, and the photos next to it do get made
            var shown = photos[0].OriginalFilePath;
            bool Settled() =>
                photos.Skip(1).Take(3).All(photo => screen.Frames.TryGet(photo.OriginalFilePath, out _)) &&
                photos.Skip(1).Select(photo => screen.Frames.TryGet(photo.OriginalFilePath, out var frame) ? frame : null).Where(frame => frame is not null).All(frame => frame!.AppliedLook == SlideshowLook.Cinema);

            Assert.True(await Ui.Until(Settled, 20000), "no frame was left graded the old way");
            Assert.Equal(shown, session.ShownPaths[0]);
        });
    }

    [Fact]
    public void WithATable_TheLutLookIsOneOfTheLooksOfTheKey_AndGradesByTheTable()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, new SlideshowStyleOptions(SlideshowLook.Cinema, Inverting()));

        Show(session, async screen =>
        {
            session.HandleKey(Keys.L);
            Assert.Equal(SlideshowLook.Lut, session.Look); // after Cinema

            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.AppliedLook == SlideshowLook.Lut, 10000), "graded by the table");
            screen.Frames.TryGet(photos[1].OriginalFilePath, out var frame);
            var (r, g, b) = Middle(frame!);
            Assert.InRange(r, 255 - 130 - 3, 255 - 120 + 3 + 20); // the photo is about (120..130, 140, 160): inverted
            Assert.InRange(g, 255 - 140 - 3, 255 - 140 + 3);
            Assert.InRange(b, 255 - 160 - 3, 255 - 160 + 3);

            session.HandleKey(Keys.L);
            Assert.Equal(SlideshowLook.None, session.Look); // round
        });
    }

    [Fact]
    public void ALutLookWithNoTable_GradesNothing_AndKeepsTheLookTheShowBeganWith()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, new SlideshowStyleOptions(SlideshowLook.Lut, Lut: null));

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.Equal(SlideshowLook.None, frame.AppliedLook);
            Assert.Equal(SlideshowLook.Lut, session.Look); // what the setting says is not changed by a missing table
            return Task.CompletedTask;
        });
    }
}
