using PhotoLiker.Core;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Looks;
using PhotoLiker.Effects.Transitions;
using PhotoLiker.Search;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The Polaroid card in a real slideshow: frames are made on a card (the photo smaller on it, the date written in the margin), the B key turns it on and off and makes the ready frames again, the
/// faces are where they are on the card (so the drift and the boxes still go by the photo), and a photo on a card is not moved by its depth.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowBorderTests : IDisposable
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
                g.Clear(Color.FromArgb(20, 90 + i * 20, 200));

            var path = _folder.File($"photo{i}.jpg");
            bitmap.Save(path, ImageFormat.Jpeg);
            // The listing of a folder gives the times of the files; the date of a photo that has no Exif is one of them
            photos.Add(new PhotoFile(path, string.Empty) { CreationTimeUtc = new DateTime(2014, 8, 9, 10, 0, 0, DateTimeKind.Utc), LastWriteTimeUtc = new DateTime(2014, 8, 9, 10, 0, 0, DateTimeKind.Utc) });
        }
        return photos;
    }

    private static IReadOnlyList<FaceLabel> FaceAt(string path)
    {
        var photo = new IndexedPhoto(Path.GetFileName(path), 1, 1);
        return [new FaceLabel(new IndexedFace(photo, 0.5f, 0.25f, 0.2f, 0.3f, 0.9f, manual: false, []), null, Confirmed: false, 0f)];
    }

    private static SlideshowSession Session(List<PhotoFile> photos, SlideshowBorder border, SlideshowEffect effect = SlideshowEffect.None, SlideshowDepthOptions? depth = null, bool people = false) =>
        new(photos, 0, SlideshowMode.OneByOne, 30, SlideshowWatermark.None, [ScreenBounds], 0, effect, video: null,
            people: people ? new SlideshowPeopleOptions(FaceAt, ShowFaces: false, ShowNames: false) : null, depth, new SlideshowStyleOptions(SlideshowLook.None, Lut: null, border));

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

    private static uint At(SlideshowFrame frame, double x, double y) => frame.Pixels[(int)(y * frame.Height) * frame.Width + (int)(x * frame.Width)] | 0xFF000000;

    [Fact]
    public void WithTheBorderOn_ThePhotoIsOnACard_WithTheDateWrittenInTheMargin()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowBorder.Polaroid);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.True(frame.HasCard);
            Assert.Equal(0.08f, frame.Inner.X, 4);
            Assert.Equal(0.04f, frame.Inner.Y, 4);
            Assert.Equal(0.84f, frame.Inner.Width, 4);
            Assert.Equal(0.84f, frame.Inner.Height, 4);

            Assert.Equal(PolaroidCard.CardColour, At(frame, 0.01, 0.01));      // the corner is the card
            var photo = At(frame, 0.5, 0.45);
            Assert.True(((photo >> 16) & 0xFF) < 60 && (photo & 0xFF) > 150, "the photo is in the middle, blue");

            // The date is written below the photo: some of the margin is not the card's colour (the file has a date: it was just made)
            var ink = 0;
            for (var y = (int)(frame.Height * 0.89); y < frame.Height - 2; y++)
            for (var x = (int)(frame.Width * 0.1); x < (int)(frame.Width * 0.9); x++)
            {
                if ((frame.Pixels[y * frame.Width + x] | 0xFF000000) != PolaroidCard.CardColour)
                    ink++;
            }
            Assert.True(frame.Taken is not null, "the photo has a date");
            Assert.True(ink > 50, $"{ink} pixels of the margin carry writing");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void WithNoBorder_TheFrameIsThePhoto()
    {
        var photos = MakePhotos(2);
        using var session = Session(photos, SlideshowBorder.None);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.False(frame.HasCard);
            Assert.Equal(new RectangleF(0, 0, 1, 1), frame.Inner);
            Assert.NotEqual(PolaroidCard.CardColour, At(frame, 0.01, 0.01));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheBKey_TurnsTheCardOnAndOff_AndMakesTheReadyFramesAgain()
    {
        var photos = MakePhotos(4);
        using var session = Session(photos, SlideshowBorder.None);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");

            session.HandleKey(Keys.B);
            Assert.Equal(SlideshowBorder.Polaroid, session.Border);
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.HasCard, 10000), "made again, on a card");

            session.HandleKey(Keys.B);
            Assert.Equal(SlideshowBorder.None, session.Border);
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && !made.HasCard, 10000), "and made again as it was");
        });
    }

    [Fact]
    public void TheFacesAreWhereTheyAreOnTheCard_NotWhereTheyAreOnThePhoto()
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, SlideshowBorder.Polaroid, SlideshowEffect.KenBurns, people: true);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var face = Assert.Single(frame.FaceRegions);

            // The face was at 0.5, 0.25 (0.2 by 0.3) of the photo: of the card that is 0.08 + 0.5 * 0.84, 0.04 + 0.25 * 0.84, ...
            Assert.Equal(0.08 + 0.5 * 0.84, face.X, 4);
            Assert.Equal(0.04 + 0.25 * 0.84, face.Y, 4);
            Assert.Equal(0.2 * 0.84, face.Width, 4);
            Assert.Equal(0.3 * 0.84, face.Height, 4);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void APhotoOnACard_IsNotMovedByItsDepth_ItDriftsLikeAKenBurnsOne()
    {
        var photos = MakePhotos(3);
        var depth = new SlideshowDepthOptions((_, _, _) => new DepthMap(8, 5, Enumerable.Repeat((byte)128, 40).ToArray()));
        using var card = Session(photos, SlideshowBorder.Polaroid, SlideshowEffect.Parallax, depth);
        var plan = new SlideshowShowPlan(SlideshowTransition.None, 0, Motion: true, MotionSeconds: 3.4, Stopwatch.GetTimestamp(), Parallax: true);

        Show(card, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[0].OriginalFilePath, out var f) && f.Depth is not null, 10000), "the frame has its depth");
            screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame);
            var (path, parallax) = SlideshowScreen.MotionFor(frame!, plan);
            Assert.Null(parallax);
            Assert.NotNull(path);
        });

        using var plain = Session(photos, SlideshowBorder.None, SlideshowEffect.Parallax, depth);
        Show(plain, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[0].OriginalFilePath, out var f) && f.Depth is not null, 10000));
            screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame);
            var (path, parallax) = SlideshowScreen.MotionFor(frame!, plan);
            Assert.NotNull(parallax); // without a card it moves by its depth, as before
            Assert.Null(path);
        });
    }
}
