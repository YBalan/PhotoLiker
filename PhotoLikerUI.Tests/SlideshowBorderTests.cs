using PhotoLiker.Core;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Looks;
using PhotoLiker.Effects.Transitions;
using PhotoLiker.Search;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The borders in a real slideshow: frames are made in one (the Polaroid card, the picture frame, the film, the stamp, the glow, the shadow: the photo smaller in it, and the date written where the border has a place
/// for it), the B key goes through them and makes the ready frames again in each, the faces are where they are in the border (so the drift and the boxes still go by the photo), and a photo in a border is not moved
/// by its depth.
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
            Assert.True(frame.HasBorder);
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
            Assert.False(frame.HasBorder);
            Assert.Equal(new RectangleF(0, 0, 1, 1), frame.Inner);
            Assert.NotEqual(PolaroidCard.CardColour, At(frame, 0.01, 0.01));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TheBKey_GoesThroughTheBorders_AndMakesTheReadyFramesAgainInEach()
    {
        var photos = MakePhotos(4);
        using var session = Session(photos, SlideshowBorder.None);

        Show(session, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out _), 10000), "the next photo is ready");

            // None, then each of the borders in turn: the next photo is made again in it, and the corner of the screen says which
            foreach (var style in Borders.Styles)
            {
                session.HandleKey(Keys.B);
                Assert.Equal(style, session.Border);
                Assert.Contains(style.ToString(), screen.Notice);
                Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.Border == style, 10000), $"made again, in the {style} border");
            }

            session.HandleKey(Keys.B);
            Assert.Equal(SlideshowBorder.Random, session.Border); // after the last border, a border for some photos by chance
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && made.Border == RandomStyle.Border(FrameKeys.Of(photos[1]), session.StyleSeed), 10000),
                "made again, with a border or not, and which, as chance says for that photo");

            session.HandleKey(Keys.B);
            Assert.Equal(SlideshowBorder.None, session.Border);
            Assert.True(await Ui.Until(() => screen.Frames.TryGet(photos[1].OriginalFilePath, out var made) && !made.HasBorder, 10000), "and made again as it was");
        });
    }

    [Theory]
    [InlineData(SlideshowBorder.Frame)]
    [InlineData(SlideshowBorder.Film)]
    [InlineData(SlideshowBorder.Stamp)]
    [InlineData(SlideshowBorder.Glow)]
    [InlineData(SlideshowBorder.Shadow)]
    public void WithAnotherBorder_ThePhotoIsInIt_AndTheFacesAreWhereTheyAreInIt(SlideshowBorder border)
    {
        var photos = MakePhotos(3);
        using var session = Session(photos, border, SlideshowEffect.KenBurns, people: true);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var layout = Borders.LayoutOf(border)!.Value;
            Assert.True(frame.HasBorder);
            Assert.Equal(border, frame.Border);
            Assert.Equal(new RectangleF(layout.X, layout.Y, layout.Width, layout.Height), frame.Inner);

            // The photo is in the middle of the frame, and the border is what is round it
            var photo = At(frame, layout.X + layout.Width * 0.5, layout.Y + layout.Height * 0.5);
            Assert.True(((photo >> 16) & 0xFF) < 60 && (photo & 0xFF) > 150, "the photo is in the middle, blue");
            var corner = At(frame, 0.005, 0.005);
            Assert.False(((corner >> 16) & 0xFF) < 60 && (corner & 0xFF) > 150, "and not in the corner");

            // The face was at 0.5, 0.25 (0.2 by 0.3) of the photo: of the frame that is where the photo is, and in proportion to it
            var face = Assert.Single(frame.FaceRegions);
            Assert.Equal(layout.X + 0.5 * layout.Width, face.X, 4);
            Assert.Equal(layout.Y + 0.25 * layout.Height, face.Y, 4);
            Assert.Equal(0.2 * layout.Width, face.Width, 4);
            Assert.Equal(0.3 * layout.Height, face.Height, 4);
            return Task.CompletedTask;
        });
    }

    /// <summary>How many pixels of the area (shares of the frame) are of a colour that <paramref name="isInk"/> says is the writing's.</summary>
    private static int InkIn(SlideshowFrame frame, BorderDate area, Func<int, int, int, bool> isInk)
    {
        var count = 0;
        for (var y = (int)(area.Y * frame.Height); y < (int)((area.Y + area.Height) * frame.Height); y++)
        for (var x = (int)(area.X * frame.Width); x < (int)((area.X + area.Width) * frame.Width); x++)
        {
            var pixel = frame.Pixels[y * frame.Width + x];
            if (isInk((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF)))
                count++;
        }
        return count;
    }

    [Fact]
    public void OnFilm_TheDateIsStampedInOrange_OnTheCornerOfThePhoto()
    {
        var photos = MakePhotos(2);
        using var session = Session(photos, SlideshowBorder.Film);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            Assert.NotNull(frame.Taken);
            var date = Borders.LayoutOf(SlideshowBorder.Film)!.Value.Date!.Value;

            // The photo is blue (r < 60): orange digits (red high, blue low) are not its, so they are the writing
            var orange = InkIn(frame, date, (r, g, b) => r > 200 && g is > 90 and < 200 && b < 90);
            Assert.True(orange > 40, $"{orange} orange pixels in the corner of the photo");

            // Flush right: more writing in the right half of the place than in the left
            var right = InkIn(frame, date with { X = date.X + date.Width / 2, Width = date.Width / 2 }, (r, g, b) => r > 200 && g is > 90 and < 200 && b < 90);
            Assert.True(right > orange / 2, "the digits end at the right");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void OnAStamp_TheDateIsPrintedInDarkRed_InTheMarginBelowThePhoto()
    {
        var photos = MakePhotos(2);
        using var session = Session(photos, SlideshowBorder.Stamp);

        Show(session, screen =>
        {
            Assert.True(screen.Frames.TryGet(photos[0].OriginalFilePath, out var frame));
            var date = Borders.LayoutOf(SlideshowBorder.Stamp)!.Value.Date!.Value;

            // The paper is cream (all channels above 220): the printing is dark red-brown, with its anti-aliased edge
            var ink = InkIn(frame, date, (r, g, b) => r < 150 && g < 100 && b < 100 && r > g);
            Assert.True(ink > 40, $"{ink} pixels of dark red writing in the margin");
            Assert.Equal(0, InkIn(frame, date, (r, g, b) => b > 200 && r < 60)); // none of the photo is in it
            return Task.CompletedTask;
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
