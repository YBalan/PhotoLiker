using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The date written in the corner of a video in the slideshow (the row Watermark, as it is for a photo), with a real video and the real engine behind the window.
/// What is looked at is the label that holds the date, which the screen sets when the video is handed to it: nothing here waits for the video to play, and nothing reads the pixels
/// of the screen (a runner with no display makes them something else), so these tests do not depend on how fast the engine starts or on the machine that runs them.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowVideoDateTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);
    private static readonly DateTime Recorded = new(2003, 1, 31, 15, 6, 0);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static bool CanPlay => TestFfmpeg.Path is not null && VlcEngine.Find() is not null;

    /// <summary>
    /// A show of one video, with this date known for it (the library's), and these dates of its file (UTC, as the folder listing has them; the time the file has when none is given);
    /// <paramref name="body"/> looks at the screen it is on.
    /// </summary>
    private void Play(SlideshowWatermark watermark, DateTime? taken, Func<SlideshowScreen, Task> body, DateTime? fileDatesUtc = null)
    {
        using var ffmpeg = TestFfmpeg.Use();
        var path = TestFfmpeg.MakeMp4(_folder.Path, "clip.mp4", "2003-01-31T15:06:00Z", seconds: 6);
        var listed = new PhotoFile(path, string.Empty);
        var listedAt = fileDatesUtc ?? File.GetLastWriteTimeUtc(path);
        listed.CreationTimeUtc = listedAt;
        listed.LastWriteTimeUtc = listedAt;
        var options = new SlideshowVideoOptions(
            Mute: true, SlideshowVideoPart.FullVideo, LowerOtherAudio: 0,
            FactsOf: _ => new SlideshowVideoFacts(Found: null, LengthMs: 6_000, Taken: taken));
        using var session = new SlideshowSession([listed], 0, SlideshowMode.OneByOne, 30, watermark, [ScreenBounds], 0, SlideshowEffect.None, options);

        Ui.Run(() => new Form(), async host =>
        {
            host.BeginInvoke(() => session.Run(host));
            Assert.True(await Ui.Until(() => session.Screens.Count == 1 && session.Screens[0].HasPhoto, 30000), "the video is on the screen");
            try
            {
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

    private static string Labels(Control screen) => string.Join(" | ", Ui.Controls<Label>(screen).Select(l => $"[{l.Text}] visible={l.Visible}"));

    [Fact]
    public void WithTheDateTimeWatermark_TheDateOfTheVideo_IsWrittenInTheBottomRightCorner()
    {
        if (!CanPlay) return;

        var text = DateText.Display(Recorded);
        Play(SlideshowWatermark.DateTime, Recorded, async screen =>
        {
            Assert.True(await Ui.Until(() => DateLabel(screen, text) is not null, 10000), "the date is written: " + Labels(screen));

            var label = DateLabel(screen, text)!;
            Assert.True(new Rectangle(Point.Empty, screen.ClientSize).Contains(label.Bounds), "inside the screen");
            Assert.True(label.Left > screen.ClientSize.Width / 2 && label.Top > screen.ClientSize.Height / 2, "in the bottom right quarter");
        });
    }

    [Fact]
    public void WithTheYearWatermark_OnlyTheYearIsWritten()
    {
        if (!CanPlay) return;

        Play(SlideshowWatermark.Year, Recorded, async screen =>
        {
            Assert.True(await Ui.Until(() => DateLabel(screen, "2003") is not null, 10000), Labels(screen));
            Assert.Null(DateLabel(screen, DateText.Display(Recorded)));
        });
    }

    [Fact]
    public void WithNoWatermark_NothingIsWritten()
    {
        if (!CanPlay) return;

        Play(SlideshowWatermark.None, Recorded, async screen =>
        {
            await Task.Delay(500);
            Assert.Null(DateLabel(screen, DateText.Display(Recorded)));
            Assert.Null(DateLabel(screen, "2003"));
        });
    }

    private static readonly DateTime FileDateUtc = new(2012, 7, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AVideoThatTheLibraryHasNoDateFor_IsDatedByTheDatesOfItsFile_LikeAPhotoWithNoExifDate()
    {
        if (!CanPlay) return;

        var expected = DateText.Display(FileDateUtc.ToLocalTime()); // the date of a file is the time on the clock of this computer
        Play(SlideshowWatermark.DateTime, taken: null, async screen =>
            Assert.True(await Ui.Until(() => DateLabel(screen, expected) is not null, 10000), "the date of the file is written: " + Labels(screen)), FileDateUtc);
    }

    [Fact]
    public void TheDateTheLibraryKnows_WinsOverTheDatesOfTheFile()
    {
        if (!CanPlay) return;

        Play(SlideshowWatermark.DateTime, Recorded, async screen =>
        {
            Assert.True(await Ui.Until(() => DateLabel(screen, DateText.Display(Recorded)) is not null, 10000), Labels(screen));
            Assert.Null(DateLabel(screen, DateText.Display(FileDateUtc.ToLocalTime())));
        }, FileDateUtc);
    }

    [Fact]
    public void AVideoWhoseFileHasNoBelievableDate_AndNoOtherDate_HasNothingWritten()
    {
        if (!CanPlay) return;

        // A card whose clock was reset gives 1987: that is no date of a video (the rule of the photos)
        Play(SlideshowWatermark.DateTime, taken: null, async screen =>
        {
            await Task.Delay(500);
            Assert.DoesNotContain(Ui.Controls<Label>(screen), label => label.Visible && DateText.TryParse(label.Text, out _));
        }, new DateTime(1987, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// The show as Alt+Enter makes it, from the real window, for a video: <paramref name="beforeTheShow"/> can change what the library knows of it first. The date of the video is
    /// looked for in the corner of the screen as soon as the show has the video on it.
    /// </summary>
    private void ShowOfTheMainWindow(Action<VideoCatalog, string> beforeTheShow, Action<string> assertDate, string recordedUtc = "2003-01-31T15:06:00Z", DateTime? fileDatesUtc = null, bool unreadable = false)
    {
        using var ffmpeg = TestFfmpeg.Use();
        string path;
        if (unreadable)
        {
            path = _folder.File("broken.mp4"); // bytes that are no video: ffmpeg cannot read it, so the library has no date for it
            File.WriteAllBytes(path, new byte[2048]);
        }
        else
        {
            path = TestFfmpeg.MakeMp4(_folder.Path, "only.mp4", recordedUtc, seconds: 6);
        }

        if (fileDatesUtc is { } fileDates)
        {
            File.SetCreationTimeUtc(path, fileDates);
            File.SetLastWriteTimeUtc(path, fileDates);
        }
        new Settings
        {
            CurrentFolder = _folder.Path, SupportVideo = true, SlideshowUseVideo = true, SlideshowMuteVideo = true, SlideshowVideoPart = SlideshowVideoPart.FullVideo,
            SlideshowWatermark = SlideshowWatermark.DateTime, SlideshowEffect = SlideshowEffect.None, SlideshowMode = SlideshowMode.OneByOne, SlideshowLowerOtherAudio = 0,
        }.Write();

        var start = typeof(MainForm).GetMethod("StartSlideshow", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            Assert.True(await Ui.Until(() => FileCount(form) == 1, 30000), "the folder is listed");
            if (!unreadable)
                Assert.True(await Ui.Until(() => VideoLibrary.Current?.Count == 1, 30000), "the window has read the video"); // it reads the one it shows
            beforeTheShow(VideoLibrary.Current!, path);

            form.BeginInvoke(new Action(() => start.Invoke(form, null))); // Alt+Enter
            Assert.True(await Ui.Until(() => Screens().Count == 1 && Screens()[0].HasPhoto, 60000), "the video is on the screen of the show");

            var screen = Screens()[0];
            var written = new List<string>();
            Assert.True(await Ui.Until(() =>
            {
                written = [.. Ui.Controls<Label>(screen).Where(l => l.Visible && DateText.TryParse(l.Text, out _)).Select(l => l.Text)];
                return written.Count > 0;
            }, 20000), "a date is written over the video: " + Labels(screen));

            assertDate(written[0]);
            screen.Close();
        }, seconds: 150);
    }

    private static List<SlideshowScreen> Screens() => [.. Application.OpenForms.Cast<Form>().OfType<SlideshowScreen>()];

    private static int FileCount(Form form)
    {
        var config = typeof(MainForm).GetField("_currentConfig", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        return ((System.Collections.ICollection)config.GetType().GetProperty("Files")!.GetValue(config)!).Count;
    }

    [Fact]
    public void InTheShowOfTheMainWindow_AVideoTheLibraryKnows_HasItsDate()
    {
        if (!CanPlay) return;

        ShowOfTheMainWindow((_, _) => { }, date => Assert.Contains("2003", date)); // the zone moves a time of day, not the year of the 31st of January at three in the afternoon
    }

    /// <summary>A video the library has not read when the show begins (a file that came a moment ago, a copy in the Liked folder) is read when its turn comes, and gets its date.</summary>
    [Fact]
    public void InTheShowOfTheMainWindow_AVideoTheLibraryHadNotReadWhenTheShowBegan_GetsItsDateWhenItsTurnComes()
    {
        if (!CanPlay) return;

        ShowOfTheMainWindow((catalog, path) =>
        {
            catalog.Forget(path); // the library does not know the video now, as if it had just arrived
            Assert.Equal(0, catalog.Count);
        }, date => Assert.Contains("2003", date));
    }

    /// <summary>A video with no usable date recorded in it (a camera that lost its clock writes the first of January, midnight) is dated by its file, as a photo is.</summary>
    [Fact]
    public void InTheShowOfTheMainWindow_AVideoWithNoUsableRecordedDate_IsDatedByItsFile()
    {
        if (!CanPlay) return;

        ShowOfTheMainWindow((_, _) => { }, date => Assert.Contains("2012", date), recordedUtc: "2001-01-01T00:00:00Z", fileDatesUtc: FileDateUtc);
    }

    /// <summary>A video that cannot be read at all has no record in the library, so the dates of its file are all there is to write.</summary>
    [Fact]
    public void InTheShowOfTheMainWindow_AVideoThatCannotBeRead_IsDatedByItsFile()
    {
        if (!CanPlay) return;

        ShowOfTheMainWindow((_, _) => { }, date => Assert.Contains("2012", date), fileDatesUtc: FileDateUtc, unreadable: true);
    }
}
