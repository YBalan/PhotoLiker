using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The date written in the corner of a video that plays in the slideshow (the row Watermark, as it is for a photo): a real six second video played by the real engine, with the date
/// the library knows for it. The date is a label above the player, which is a window of its own, so what is looked at is the screen itself and not only the label.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowVideoDateTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly DateTime Recorded = new(2003, 1, 31, 15, 6, 0);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static bool CanPlay => TestFfmpeg.Path is not null && VlcEngine.Find() is not null;

    private void Play(SlideshowWatermark watermark, DateTime? taken, Func<SlideshowScreen, Task> body)
    {
        using var _ = TestFfmpeg.Use();
        var path = TestFfmpeg.MakeMp4(_folder.Path, "clip.mp4", "2003-01-31T15:06:00Z", seconds: 6);
        var options = new SlideshowVideoOptions(
            Mute: true, SlideshowVideoPart.FullVideo, LowerOtherAudio: 0,
            FactsOf: _ => new SlideshowVideoFacts(Found: null, LengthMs: 6_000, Taken: taken));
        using var session = new SlideshowSession([new PhotoFile(path, string.Empty)], 0, SlideshowMode.OneByOne, 30, watermark, [ScreenBounds], 0, SlideshowEffect.None, options);

        Ui.Run(() => new Form(), async host =>
        {
            host.BeginInvoke(() => session.Run(host));
            Assert.True(await Ui.Until(() => session.Screens.Count == 1 && session.Screens[0].HasPhoto, 30000), "the video is on the screen");
            try
            {
                Assert.True(await Ui.Until(() => session.Screens[0].IsPlayingVideo, 40000));
                await body(session.Screens[0]);
            }
            finally
            {
                session.HandleKey(Keys.Escape);
            }
        }, seconds: 90);
    }

    private static Label? DateLabel(SlideshowScreen screen, string text) =>
        Ui.Controls<Label>(screen).FirstOrDefault(label => label.Visible && label.Text == text);

    /// <summary>What the screen really shows where the label is (copied from the screen, which has the player's picture in it too): the share of the pixels that are light.</summary>
    private static double LightShare(SlideshowScreen screen, Label label)
    {
        var area = screen.RectangleToScreen(label.Bounds);
        using var bitmap = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(area.Location, Point.Empty, area.Size);

        var light = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y) is { R: > 160, G: > 160, B: > 160 })
                light++;
        return light / (double)(bitmap.Width * bitmap.Height);
    }

    [Fact]
    public void WithTheDateTimeWatermark_TheDateOfTheVideo_IsWrittenInTheBottomRightCorner_OverThePlayer()
    {
        if (!CanPlay) return;

        var text = PhotoLiker.Core.DateText.Display(Recorded);
        Play(SlideshowWatermark.DateTime, Recorded, async screen =>
        {
            Assert.True(await Ui.Until(() => DateLabel(screen, text) is not null, 5000), "the date is written: " + string.Join(" | ", Ui.Controls<Label>(screen).Select(l => $"[{l.Text}] visible={l.Visible}")));

            var label = DateLabel(screen, text)!;
            Assert.True(new Rectangle(Point.Empty, screen.ClientSize).Contains(label.Bounds), "inside the screen");
            Assert.True(label.Left > screen.ClientSize.Width / 2 && label.Top > screen.ClientSize.Height / 2, "in the bottom right quarter");

            await Task.Delay(1500); // the picture is up by now
            var share = LightShare(screen, label);
            Assert.InRange(share, 0.02, 0.6); // white letters on a black box: some light pixels, most dark
        });
    }

    private static List<SlideshowScreen> Screens() => [.. Application.OpenForms.Cast<Form>().OfType<SlideshowScreen>()];

    /// <summary>
    /// The show as Alt+Enter makes it, from the real window: a folder of two videos recorded in different years, the window showing the first (so the library has read that one and not
    /// the other). Each video that plays in the show has its own date written in the corner.
    /// </summary>
    [Fact]
    public void InTheShowOfTheMainWindow_EveryVideoGetsItsOwnDate_AlsoOneTheWindowNeverShowed()
    {
        if (!CanPlay) return;
        using var ffmpeg = TestFfmpeg.Use();
        TestFfmpeg.MakeMp4(_folder.Path, "a.mp4", "2003-01-31T15:06:00Z", seconds: 6);
        TestFfmpeg.MakeMp4(_folder.Path, "b.mp4", "2005-06-15T08:30:00Z", seconds: 6);
        new Settings
        {
            CurrentFolder = _folder.Path, SupportVideo = true, SlideshowUseVideo = true, SlideshowMuteVideo = true, SlideshowVideoPart = SlideshowVideoPart.FullVideo,
            SlideshowWatermark = SlideshowWatermark.DateTime, SlideshowEffect = SlideshowEffect.None, SlideshowMode = SlideshowMode.OneByOne, SlideshowLowerOtherAudio = 0,
        }.Write();

        var start = typeof(MainForm).GetMethod("StartSlideshow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(3000); // the window lists the folder and shows the first video
            form.BeginInvoke(new Action(() => start.Invoke(form, null)));
            Assert.True(await Ui.Until(() => Screens().Count == 1 && Screens()[0].IsPlayingVideo, 60000), "a video plays in the show");

            var screen = Screens()[0];
            var seen = new HashSet<string>();
            var end = Environment.TickCount64 + 40000;
            while (Environment.TickCount64 < end && seen.Count < 2)
            {
                foreach (var label in Ui.Controls<Label>(screen).Where(l => l.Visible && DateText.TryParse(l.Text, out _)))
                    seen.Add(label.Text);
                await Task.Delay(200);
            }
            var texts = string.Join(" | ", Ui.Controls<Label>(screen).Select(l => $"[{l.Text}] visible={l.Visible}"));
            Assert.True(seen.Count >= 2, $"each video has its date written over it (seen: {string.Join(", ", seen)}): " + texts);
            screen.Close();
        }, seconds: 120);
    }

    /// <summary>The show with the settings a person has who changed only the two rows for videos: Random order, the Fade effect, only the places a search found (none here: the whole video), and photos in the list.</summary>
    [Fact]
    public void InARandomShowWithPhotosAndTheDefaultEffect_TheVideosStillGetTheirDate()
    {
        if (!CanPlay) return;
        using var ffmpeg = TestFfmpeg.Use();
        TestFfmpeg.MakeMp4(_folder.Path, "a.mp4", "2003-01-31T15:06:00Z", seconds: 6);
        TestFfmpeg.MakeMp4(_folder.Path, "b.mp4", "2005-06-15T08:30:00Z", seconds: 6);
        for (var i = 0; i < 2; i++)
        {
            using var bitmap = new Bitmap(320, 200);
            bitmap.Save(_folder.File($"c{i}.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
        }
        new Settings { CurrentFolder = _folder.Path, SupportVideo = true, SlideshowUseVideo = true, SlideshowMuteVideo = true, SlideshowTimeout = 2 }.Write();

        var start = typeof(MainForm).GetMethod("StartSlideshow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(3000);
            form.BeginInvoke(new Action(() => start.Invoke(form, null)));
            Assert.True(await Ui.Until(() => Screens().Count == 1, 60000), "the show is on the screen");

            var screen = Screens()[0];
            var withDate = 0;
            var withoutDate = new List<string>();
            var end = Environment.TickCount64 + 60000;
            while (Environment.TickCount64 < end && withDate < 2)
            {
                if (screen.IsPlayingVideo)
                {
                    await Task.Delay(1500); // the video is up
                    if (Ui.Controls<Label>(screen).FirstOrDefault(l => l.Visible && DateText.TryParse(l.Text, out _)) is { } dateLabel)
                    {
                        withDate++;
                        Assert.InRange(LightShare(screen, dateLabel), 0.02, 0.6); // it is on the screen itself, over the picture of the player
                    }
                    else if (screen.IsPlayingVideo)
                        withoutDate.Add(string.Join(" | ", Ui.Controls<Label>(screen).Select(l => $"[{l.Text}] visible={l.Visible}")));
                    await Ui.Until(() => !screen.IsPlayingVideo, 20000);
                }
                await Task.Delay(200);
            }
            Assert.True(withDate >= 1, $"a video played with its date over it ({withDate} did, without: {string.Join(" ;; ", withoutDate)})");
            Assert.Empty(withoutDate);
            screen.Close();
        }, seconds: 150);
    }

    /// <summary>A video the library has not read when the show begins (a file that came into the folder a moment ago, or a copy in the Liked folder) is read when its turn comes, and gets its date.</summary>
    [Fact]
    public void AVideoTheLibraryHadNotReadWhenTheShowBegan_GetsItsDateWhenItsTurnComes()
    {
        if (!CanPlay) return;
        using var ffmpeg = TestFfmpeg.Use();
        var only = TestFfmpeg.MakeMp4(_folder.Path, "only.mp4", "2003-01-31T15:06:00Z", seconds: 6);
        new Settings
        {
            CurrentFolder = _folder.Path, SupportVideo = true, SlideshowUseVideo = true, SlideshowMuteVideo = true, SlideshowVideoPart = SlideshowVideoPart.FullVideo,
            SlideshowWatermark = SlideshowWatermark.DateTime, SlideshowEffect = SlideshowEffect.None, SlideshowMode = SlideshowMode.OneByOne,
        }.Write();

        var start = typeof(MainForm).GetMethod("StartSlideshow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(3000);
            PhotoLikerUI.VideoLibrary.Current!.Forget(only); // the library does not know the video now, as if it had just arrived
            Assert.Equal(0, PhotoLikerUI.VideoLibrary.Current!.Count);

            form.BeginInvoke(new Action(() => start.Invoke(form, null)));
            Assert.True(await Ui.Until(() => Screens().Count == 1 && Screens()[0].IsPlayingVideo, 60000), "the video plays in the show");

            var screen = Screens()[0];
            await Task.Delay(1500);
            Assert.True(Ui.Controls<Label>(screen).Any(l => l.Visible && DateText.TryParse(l.Text, out _)),
                "the date is written over the video: " + string.Join(" | ", Ui.Controls<Label>(screen).Select(l => $"[{l.Text}] visible={l.Visible}")));
            screen.Close();
        }, seconds: 120);
    }

    [Fact]
    public void WithTheYearWatermark_OnlyTheYearIsWritten()
    {
        if (!CanPlay) return;

        Play(SlideshowWatermark.Year, Recorded, async screen =>
        {
            Assert.True(await Ui.Until(() => DateLabel(screen, "2003") is not null, 5000));
        });
    }

    [Fact]
    public void WithNoWatermark_OrWithNoDate_NothingIsWritten()
    {
        if (!CanPlay) return;

        Play(SlideshowWatermark.None, Recorded, async screen =>
        {
            await Task.Delay(1500);
            Assert.Null(DateLabel(screen, PhotoLiker.Core.DateText.Display(Recorded)));
            Assert.Null(DateLabel(screen, "2003"));
        });
    }
}
