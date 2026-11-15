using PhotoLiker.Core;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The collage show in a real slideshow: photos of one event that follow each other are put on the screen together (each in its cell, as large as the cell lets it be), a page is one step of the show,
/// an event that ends ends the page, and without the setting a photo is a photo.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowCollageTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly Color[] Colours = [Color.FromArgb(220, 30, 30), Color.FromArgb(30, 200, 30), Color.FromArgb(30, 30, 220), Color.FromArgb(230, 210, 30), Color.FromArgb(235, 235, 235), Color.FromArgb(30, 200, 200)];

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

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowCollage collage, Func<string, DateTime?>? takenOf = null) =>
        new(photos, 0, SlideshowMode.OneByOne, 30, SlideshowWatermark.None, [ScreenBounds], 0, SlideshowEffect.None, video: null, people: null, depth: null,
            new SlideshowStyleOptions(SlideshowLook.None, Lut: null, SlideshowBorder.None, collage, takenOf));

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

    private static Color At(SlideshowFrame frame, int x, int y)
    {
        var pixel = frame.Pixels[y * frame.Width + x];
        return Color.FromArgb((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF));
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 40 && Math.Abs(a.G - b.G) < 40 && Math.Abs(a.B - b.B) < 40;

    private static Task<bool> Shows(SlideshowSession session, string path) => Ui.Until(() => session.ShownPaths[0] == path, 15000);

    [Fact]
    public void TwoPhotosOfOneEvent_AreOnTheScreenTogether_EachInItsCell()
    {
        var photos = MakePhotos(4);
        using var session = Session(photos, SlideshowCollage.Two);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(FrameKeys.PageKey(photos[0].OriginalFilePath, 2), out var page));
            Assert.Equal((640, 360), (page.Width, page.Height));

            Assert.True(Near(At(page, 160, 180), Colours[0]), $"the first photo is in the left cell: {At(page, 160, 180)}");
            Assert.True(Near(At(page, 480, 180), Colours[1]), $"the second is in the right one: {At(page, 480, 180)}");
            Assert.Equal(Color.FromArgb(0, 0, 0), At(page, 320, 180)); // the gap between them is black
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void APageIsOneStepOfTheShow_AndTheShowGoesThroughAllThePages()
    {
        var photos = MakePhotos(5);
        using var session = Session(photos, SlideshowCollage.Two);

        Show(session, async screen =>
        {
            Assert.Equal(photos[0].OriginalFilePath, session.ShownPaths[0]);

            session.HandleKey(Keys.Right);
            Assert.True(await Shows(session, photos[2].OriginalFilePath), "the next page begins with the third photo");
            Assert.True(screen.Frames.TryGet(FrameKeys.PageKey(photos[2].OriginalFilePath, 2), out var second));
            Assert.True(Near(At(second, 160, 180), Colours[2]) && Near(At(second, 480, 180), Colours[3]), "the third and the fourth photo");

            session.HandleKey(Keys.Right);
            Assert.True(await Shows(session, photos[4].OriginalFilePath), "the last page is the fifth photo, alone, as it is");
            Assert.True(screen.Frames.TryGet(photos[4].OriginalFilePath, out var last));
            Assert.True(Near(At(last, 320, 180), Colours[4]), "alone it fills the screen as a photo does");

            session.HandleKey(Keys.Right);
            Assert.True(await Shows(session, photos[0].OriginalFilePath), "and round to the first page");
        });
    }

    [Fact]
    public void AnEventThatEnds_EndsThePage()
    {
        var photos = MakePhotos(5);
        var start = new DateTime(2014, 8, 9, 10, 0, 0);
        // The first three are from one morning, the last two from a day later
        var dates = new Dictionary<string, DateTime?>
        {
            [photos[0].OriginalFilePath] = start, [photos[1].OriginalFilePath] = start.AddMinutes(10), [photos[2].OriginalFilePath] = start.AddMinutes(20),
            [photos[3].OriginalFilePath] = start.AddDays(1), [photos[4].OriginalFilePath] = start.AddDays(1).AddMinutes(4),
        };
        using var session = Session(photos, SlideshowCollage.Four, path => dates[path]);

        Show(session, async screen =>
        {
            session.HandleKey(Keys.Right);
            Assert.True(await Shows(session, photos[3].OriginalFilePath), "the second page begins where the event ends, though four were asked for");

            Assert.True(screen.Frames.TryGet(FrameKeys.PageKey(photos[0].OriginalFilePath, 3), out var first));
            // Three photos: one large on the left and two small on the right
            Assert.True(Near(At(first, 190, 180), Colours[0]), "the large one");
            Assert.True(Near(At(first, 520, 90), Colours[1]) && Near(At(first, 520, 270), Colours[2]), "the two small ones");
        });
    }

    [Fact]
    public void WithNoCollage_APhotoIsAPhoto_AndNoDatesAreAskedFor()
    {
        var photos = MakePhotos(3);
        var asked = 0;
        using var session = Session(photos, SlideshowCollage.None, _ => { asked++; return null; });

        Show(session, async screen =>
        {
            session.HandleKey(Keys.Right);
            Assert.True(await Shows(session, photos[1].OriginalFilePath));
            Assert.Equal(0, asked);
        });
    }

    [Fact]
    public void APageThatCanNotBeRead_LeavesItsCellBlack_AndOneOfWhichNoneCanIsAFailure()
    {
        var photos = MakePhotos(4);
        File.WriteAllText(photos[1].OriginalFilePath, "this is not a picture"); // the second photo of the first page
        using var session = Session(photos, SlideshowCollage.Two);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(FrameKeys.PageKey(photos[0].OriginalFilePath, 2), out var page));
            Assert.True(Near(At(page, 160, 180), Colours[0]), "the photo that could be read is there");
            Assert.Equal(Color.FromArgb(0, 0, 0), At(page, 480, 180)); // and the other cell is empty
            return Task.CompletedTask;
        });
    }
}
