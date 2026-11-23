using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using PhotoLiker.Core;
using PhotoLiker.VideoToGif;

namespace PhotoLikerUI.Tests;

/// <summary>The Video to GIF window opened for real: it reads a video, shows the first and the last frame of the part that is picked, tells what the GIF will be, and makes it (and plays it).</summary>
[Collection(WindowsCollection.Name)]
public sealed class VideoToGifDialogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public VideoToGifDialogTests() => new GlobalConfig { ColorizeBar = false }.Save();

    public void Dispose() => _folder.Dispose();

    private string SettingsFile => _folder.File("video-to-gif-settings.json");

    /// <summary>A video in which every frame is different (a test pattern with a running counter), with a sound track. Null where there is no ffmpeg.</summary>
    private string? Video(string name = "clip.mp4", int seconds = 4, int rate = 25, int width = 320, int height = 240)
    {
        if (TestFfmpeg.Path is not { } ffmpeg)
            return null;

        var file = _folder.File(name);
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate={rate}",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:v", "mpeg4", "-q:v", "3", "-pix_fmt", "yuv420p", "-c:a", "aac", file,
        })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var messages = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && File.Exists(file), "ffmpeg made the test video: " + messages);
        return file;
    }

    private VideoToGifDialog Create(string? video, bool dark = false, string? saveTo = null) =>
        new(video, dark, SettingsFile)
        {
            FfmpegPath = TestFfmpeg.Path,
            AskSavePath = saveTo is null ? null : (_, _, _, _) => saveTo,
            AskVideoPath = video is null ? (_, _, _) => null : null, // a window without a video asks for one when it opens: nobody answers
        };

    private static async Task Opened(VideoToGifDialog dialog) =>
        Assert.True(await Ui.Until(() => !dialog.IsReading && dialog.Clip is not null && dialog.FirstPicture.Image is not null && dialog.LastPicture.Image is not null, 30000),
            $"the video was read and its frames shown ({dialog.StatusText})");

    private static async Task Made(VideoToGifDialog dialog) =>
        Assert.True(await dialog.MakeAsync(), $"the GIF was made ({dialog.StatusText})");

    private static byte[] Pixels(Image picture)
    {
        using var stream = new MemoryStream();
        using var copy = new Bitmap(picture);
        copy.Save(stream, ImageFormat.Bmp);
        return stream.ToArray();
    }

    // ── Opening ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_ReadsTheVideo_AndTakesAllOfIt()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.Equal(video, dialog.VideoPath);
            Assert.Equal(video, dialog.VideoBox.Text);
            Assert.Equal(new GifClip(dialog.Clip!.DurationSeconds, 25, 320, 240), dialog.Clip);
            Assert.InRange(dialog.Clip.DurationSeconds, 3.9, 4.2);
            Assert.Contains("25 frames a second", dialog.InfoText);
            Assert.Contains("320 x 240", dialog.InfoText);

            Assert.Equal(1, dialog.FirstFrame);
            Assert.Equal(dialog.Clip.SourceFrames, dialog.LastFrame);
            Assert.Equal(GifRange.All, dialog.Range);
            Assert.True(dialog.MakeButton.Enabled);
            Assert.False(dialog.StopButton.Enabled);
            Assert.Equal(string.Empty, dialog.StatusText);
        });
    }

    [Fact]
    public void TheWindow_TellsWhatTheGifWillBe_BeforeItIsMade()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            // 4 seconds at the 10 frames a second of the defaults; the video is narrower than the 640 of the defaults, so it is not made wider
            var plan = dialog.Plan!;
            Assert.Equal(40, plan.FrameCount);
            Assert.Equal((320, 240), (plan.Width, plan.Height));
            Assert.Equal($"The GIF will have 40 frames, 320 x 240 pixels, and play for {plan.PlaySeconds:0.#} s.", dialog.SummaryText);

            dialog.FrameEveryNumeric.Value = 0.05m;
            Assert.Equal(80, dialog.Plan!.FrameCount);
            Assert.Contains("80 frames", dialog.SummaryText);

            dialog.WidthNumeric.Value = 160;
            Assert.Equal((160, 120), (dialog.Plan!.Width, dialog.Plan.Height));
            Assert.Contains("160 x 120 pixels", dialog.SummaryText);

            dialog.OriginalCheck.Checked = true;
            Assert.False(dialog.WidthNumeric.Enabled);
            Assert.Equal((320, 240), (dialog.Plan!.Width, dialog.Plan.Height));
            dialog.OriginalCheck.Checked = false;
            Assert.True(dialog.WidthNumeric.Enabled);
        });
    }

    [Fact]
    public void TheFirstAndTheLastFrame_AreShown()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.Equal(320, dialog.FirstPicture.Image!.Width);
            Assert.Equal(240, dialog.FirstPicture.Image.Height);
            Assert.NotEqual(Pixels(dialog.FirstPicture.Image), Pixels(dialog.LastPicture.Image!)); // the running counter of the test pattern: two frames of the video are not the same
            Assert.Equal("0:00.00", dialog.FirstTimeLabel.Text.Split(' ')[^1]);
            Assert.StartsWith("of 100, at 0:03.9", dialog.LastTimeLabel.Text);
        });
    }

    // ── The range ───────────────────────────────────────────────────────────

    [Fact]
    public void TheRange_IsTheFramesBetweenTheTwoSliders()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            dialog.FirstNumeric.Value = 26;
            dialog.LastSlider.Value = 75;

            Assert.Equal((26, 75), (dialog.FirstFrame, dialog.LastFrame));
            Assert.Equal(26, dialog.FirstSlider.Value); // the slider and the number of a frame are one value
            Assert.Equal(75, dialog.LastNumeric.Value);
            var range = dialog.Range;
            Assert.Equal(1.0, range.StartSeconds, 6);
            Assert.Equal(3.0, range.EndSeconds!.Value, 6);
            Assert.Equal(20, dialog.Plan!.FrameCount); // two seconds at 10 a second
            Assert.Equal("of 100, at 0:01.00", dialog.FirstTimeLabel.Text);
            Assert.Contains("20 frames", dialog.SummaryText);
        });
    }

    [Fact]
    public void TheFirstFrame_CannotPassTheLast_AndTheLastCannotGoBeforeTheFirst()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.LastNumeric.Value = 50;

            dialog.FirstNumeric.Value = 90;
            Assert.Equal((50, 50), (dialog.FirstFrame, dialog.LastFrame)); // stops at the last frame; the last frame stays where it was
            Assert.Equal(50, dialog.FirstSlider.Value);

            dialog.LastSlider.Value = 10;
            Assert.Equal((50, 50), (dialog.FirstFrame, dialog.LastFrame)); // and the other way
            Assert.Equal(50, dialog.LastNumeric.Value);
            Assert.Equal(1, dialog.Plan!.FrameCount); // one frame of the video, which is one frame of the GIF at the most
        });
    }

    [Fact]
    public void AllFrames_PutsTheRangeBack()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FirstNumeric.Value = 30;
            dialog.LastNumeric.Value = 60;
            Assert.NotEqual(GifRange.All, dialog.Range);

            dialog.AllFramesButton.PerformClick();

            Assert.Equal((1, dialog.Clip!.SourceFrames), (dialog.FirstFrame, dialog.LastFrame));
            Assert.Equal(GifRange.All, dialog.Range);
            Assert.Equal(40, dialog.Plan!.FrameCount);
        });
    }

    [Fact]
    public void AFrameThatIsMoved_IsShownAgain()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            var before = Pixels(dialog.FirstPicture.Image!);
            var last = Pixels(dialog.LastPicture.Image!);

            dialog.FirstNumeric.Value = 60;
            Assert.True(await Ui.Until(() => !Pixels(dialog.FirstPicture.Image!).SequenceEqual(before), 15000), "the first frame was shown again, for frame 60");
            Assert.Equal(last, Pixels(dialog.LastPicture.Image!)); // the last frame was not touched
        });
    }

    [Fact]
    public void ALastFrameThatIsMoved_IsShownAgain_AndTheFirstIsNotTouched()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            var first = Pixels(dialog.FirstPicture.Image!);
            var before = Pixels(dialog.LastPicture.Image!);

            dialog.LastNumeric.Value = 40;
            Assert.True(await Ui.Until(() => !Pixels(dialog.LastPicture.Image!).SequenceEqual(before), 15000), "the last frame was shown again, for frame 40");
            Assert.Equal(first, Pixels(dialog.FirstPicture.Image!));
        });
    }

    [Fact]
    public void TheRangeControls_AreOff_WithoutAVideo()
    {
        Ui.Run(() => Create(null), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Task.Delay(300);

            Assert.False(dialog.MakeButton.Enabled);
            Assert.False(dialog.FirstSlider.Enabled);
            Assert.False(dialog.LastNumeric.Enabled);
            Assert.False(dialog.AllFramesButton.Enabled);
            Assert.True(dialog.BrowseButton.Enabled);
            Assert.Equal(string.Empty, dialog.SummaryText);
            Assert.Null(dialog.Plan);
        });
    }

    // ── The video ───────────────────────────────────────────────────────────

    [Fact]
    public void WithoutAVideo_TheWindowAsksForOne_AndOpensIt()
    {
        var video = Video();
        if (video is null)
            return;

        string? folderAsked = "not asked";
        using var _ = TestFfmpeg.Use();
        Ui.Run(() =>
        {
            var dialog = Create(null);
            dialog.AskVideoPath = (title, filter, folder) =>
            {
                folderAsked = folder;
                Assert.Equal("Pick the video to make a GIF of", title);
                Assert.StartsWith("Videos|*.mp4;", filter);
                Assert.EndsWith("|All files|*.*", filter);
                return video;
            };
            return dialog;
        }, async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.Equal(string.Empty, folderAsked);
            Assert.Equal(video, dialog.VideoPath);
        });
    }

    [Fact]
    public void WhenNoVideoIsPicked_TheWindowSaysSo_AndWaits()
    {
        Ui.Run(() =>
        {
            var dialog = Create(null);
            dialog.AskVideoPath = (_, _, _) => null;
            return dialog;
        }, async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Task.Delay(300);

            Assert.Equal(VideoToGifStrings.StatusNoVideoPicked, dialog.StatusText);
            Assert.Null(dialog.VideoPath);
            Assert.Null(dialog.Clip);
            Assert.Equal(VideoToGifStrings.NoVideo, dialog.InfoText);
            Assert.False(dialog.MakeButton.Enabled);
        });
    }

    [Fact]
    public void Browse_PicksAnotherVideo_AndStartsOverWithAllOfIt()
    {
        var first = Video("first.mp4", seconds: 4);
        var second = Video("second.mp4", seconds: 2, rate: 10);
        if (first is null || second is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(first), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FirstNumeric.Value = 40;
            string? folderAsked = null;
            dialog.AskVideoPath = (_, _, folder) =>
            {
                folderAsked = folder;
                return second;
            };

            await dialog.BrowseAsync();

            Assert.Equal(_folder.Path, folderAsked); // it begins where the video was
            Assert.Equal(second, dialog.VideoPath);
            Assert.Equal(second, dialog.VideoBox.Text);
            Assert.Equal(1, dialog.FirstFrame);
            Assert.Equal(dialog.Clip!.SourceFrames, dialog.LastFrame);
            Assert.Equal(20, dialog.Clip.SourceFrames); // 2 seconds of 10 frames
            Assert.Contains("10 frames a second", dialog.InfoText);
        });
    }

    [Fact]
    public void AVideoThatCannotBeRead_SaysWhy_AndTheOneThatWasThereStays()
    {
        var good = Video();
        if (good is null)
            return;

        var broken = _folder.File("broken.mp4");
        File.WriteAllText(broken, "this is not a video");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(good), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            await dialog.OpenVideoAsync(broken);

            Assert.StartsWith("The video could not be read", dialog.StatusText);
            Assert.Equal(good, dialog.VideoPath);
            Assert.Equal(good, dialog.VideoBox.Text);
            Assert.True(dialog.MakeButton.Enabled); // the video that was there can still be made
        });
    }

    [Fact]
    public void AVideoThatCannotBeRead_AtTheStart_LeavesNothingToMake()
    {
        var broken = _folder.File("broken.mp4");
        File.WriteAllText(broken, "this is not a video");
        if (TestFfmpeg.Path is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(broken), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            Assert.True(await Ui.Until(() => dialog.StatusText.StartsWith("The video could not be read"), 30000), $"the failure was told ({dialog.StatusText})");

            Assert.Null(dialog.Clip);
            Assert.False(dialog.MakeButton.Enabled);
            dialog.AskSavePath = (_, _, _, _) => throw new InvalidOperationException("nothing to make, so nothing may be asked");
            Assert.False(await dialog.MakeAsync());
        });
    }

    // ── Making the GIF ──────────────────────────────────────────────────────

    [Fact]
    public void TheGif_IsMade_AsPlanned_AndShownPlaying()
    {
        var video = Video();
        if (video is null)
            return;

        var gif = _folder.File("result.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: gif), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            var plan = dialog.Plan!;

            await Made(dialog);

            Assert.Equal(gif, dialog.SavedPath);
            var info = GifInfo.Read(gif);
            Assert.Equal(plan.FrameCount, info.FrameCount);
            Assert.Equal((plan.Width, plan.Height), (info.Width, info.Height));
            Assert.True(info.LoopsForever);
            Assert.Equal(info, dialog.Made);

            Assert.NotNull(dialog.GifPicture.Image);
            Assert.True(ImageAnimator.CanAnimate(dialog.GifPicture.Image), "the picture of the GIF plays");
            Assert.StartsWith($"Saved result.gif: {info.FrameCount} frames, {info.Width} x {info.Height} pixels, ", dialog.StatusText);
            Assert.True(dialog.MakeButton.Enabled);
            Assert.False(dialog.StopButton.Enabled);
            Assert.False(dialog.ProgressVisible);
            Assert.False(dialog.IsMaking);

            // The window does not hold the file: it can be deleted while it is shown
            File.Delete(gif);
            Assert.False(File.Exists(gif));
        });
    }

    [Fact]
    public void TheGif_IsOnlyThePartThatWasPicked()
    {
        var video = Video();
        if (video is null)
            return;

        var gif = _folder.File("part.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: gif), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FirstNumeric.Value = 26;
            dialog.LastNumeric.Value = 75;
            dialog.FrameEveryNumeric.Value = 0.1m;
            dialog.WidthNumeric.Value = 160;
            dialog.RepeatCombo.SelectedIndex = 1; // once

            await Made(dialog);

            var info = GifInfo.Read(gif);
            Assert.Equal(20, info.FrameCount);
            Assert.Equal((160, 120), (info.Width, info.Height));
            Assert.Null(info.LoopCount); // plays once
            Assert.Equal(2.0, info.DurationSeconds, 1);
        });
    }

    [Fact]
    public void TheTimeOfAFrame_IsADecimalNumberOfSeconds_AndEachFrameOfTheGifIsShownThatLong()
    {
        var video = Video();
        if (video is null)
            return;

        var gif = _folder.File("slow.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: gif), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            // Seconds, with hundredths: from the 0.02 of fifty frames a second to a minute
            var box = dialog.FrameEveryNumeric;
            Assert.Equal((0.02m, 60m, 2), (box.Minimum, box.Maximum, box.DecimalPlaces));
            Assert.Equal(0.1m, box.Value);

            box.Value = 1.5m;
            Assert.Equal(1.5, dialog.ReadSettings().FrameSeconds);
            Assert.Equal(3, dialog.Plan!.FrameCount); // a frame at 0, 1.5 and 3 seconds of the 4
            Assert.Contains("3 frames", dialog.SummaryText);
            Assert.Contains("play for 4.5 s", dialog.SummaryText); // 3 frames of 1.5 seconds each: the last one is shown for its whole time

            await Made(dialog);

            var info = GifInfo.Read(gif);
            Assert.Equal(3, info.FrameCount);
            Assert.Equal(4.5, info.DurationSeconds, 1);
        });
    }

    // ── How fast the GIF plays ──────────────────────────────────────────────

    [Fact]
    public void TheGifPlaysAtTheSpeedOfTheVideo_UntilTheTimeEachFramePlaysIsSetApart()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            // At first: as fast as the video. The time a frame plays is the box beside it, greyed, and it follows
            Assert.True(dialog.VideoSpeedCheck.Checked);
            Assert.False(dialog.FramePlaysNumeric.Enabled);
            Assert.Null(dialog.ReadSettings().DelaySeconds);
            dialog.FrameEveryNumeric.Value = 0.5m;
            Assert.Equal(0.5m, dialog.FramePlaysNumeric.Value);
            Assert.Equal(1.0, dialog.Plan!.Speed);
            Assert.DoesNotContain("speed of the video", dialog.SummaryText);

            // Unticked: the time each frame plays is the person's. 8 frames of the 4 seconds, each shown for a tenth
            dialog.VideoSpeedCheck.Checked = false;
            Assert.True(dialog.FramePlaysNumeric.Enabled);
            Assert.Equal(0.5m, dialog.FramePlaysNumeric.Value); // where it was
            dialog.FramePlaysNumeric.Value = 0.1m;

            Assert.Equal(0.1, dialog.ReadSettings().DelaySeconds);
            Assert.Equal(0.5, dialog.ReadSettings().FrameSeconds);
            Assert.Equal(8, dialog.Plan!.FrameCount);
            Assert.Equal(5.0, dialog.Plan.Speed, 6);
            Assert.Equal("The GIF will have 8 frames, 320 x 240 pixels, and play for 0.8 s (at 5 times the speed of the video).", dialog.SummaryText);

            // Slower than the video is as possible: every frame of the video (0.04) shown for a tenth
            dialog.FrameEveryNumeric.Value = 0.04m;
            Assert.Equal(100, dialog.Plan!.FrameCount);
            Assert.Contains("play for 10 s (at 0.4 times the speed of the video)", dialog.SummaryText);

            // Ticked again: the speed of the video, and the box follows the distance again
            dialog.VideoSpeedCheck.Checked = true;
            Assert.False(dialog.FramePlaysNumeric.Enabled);
            Assert.Equal(0.04m, dialog.FramePlaysNumeric.Value);
            Assert.Null(dialog.ReadSettings().DelaySeconds);
            Assert.Contains("play for 4 s.", dialog.SummaryText);
        });
    }

    [Fact]
    public void AGifThatPlaysFaster_HasTheSameFramesShownForLess()
    {
        var video = Video();
        if (video is null)
            return;

        var gif = _folder.File("fast.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: gif), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FrameEveryNumeric.Value = 0.5m;
            dialog.VideoSpeedCheck.Checked = false;
            dialog.FramePlaysNumeric.Value = 0.1m;

            await Made(dialog);

            var info = GifInfo.Read(gif);
            Assert.Equal(8, info.FrameCount);
            Assert.Equal(0.8, info.DurationSeconds, 1); // 4 seconds of video in under a second
            Assert.Contains("play for 0.8 s", dialog.SummaryText);
        });
    }

    [Fact]
    public void ATimeEachFramePlays_IsKeptAndComesBack_AndSoIsNotHavingOne()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FrameEveryNumeric.Value = 0.5m;
            dialog.VideoSpeedCheck.Checked = false;
            dialog.FramePlaysNumeric.Value = 0.08m;
        });

        var kept = GifSettings.Load(SettingsFile);
        Assert.Equal((0.5, (double?)0.08), (kept.FrameSeconds, kept.DelaySeconds));

        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.False(dialog.VideoSpeedCheck.Checked);
            Assert.True(dialog.FramePlaysNumeric.Enabled);
            Assert.Equal((0.5m, 0.08m), (dialog.FrameEveryNumeric.Value, dialog.FramePlaysNumeric.Value));
            dialog.VideoSpeedCheck.Checked = true; // and back to the speed of the video
        });

        Assert.Null(GifSettings.Load(SettingsFile).DelaySeconds);
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.True(dialog.VideoSpeedCheck.Checked);
            Assert.False(dialog.FramePlaysNumeric.Enabled);
            Assert.Equal(0.5m, dialog.FramePlaysNumeric.Value); // it follows the distance
        });
    }

    [Fact]
    public void WhileTheGifIsMade_TheTimeEachFramePlaysIsLockedToo()
    {
        var video = Video("long2.mp4", seconds: 90);
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: _folder.File("locked.gif")), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.OriginalCheck.Checked = true;
            dialog.FrameEveryNumeric.Value = 0.04m;
            dialog.VideoSpeedCheck.Checked = false;

            var making = dialog.MakeAsync();
            Assert.True(dialog.IsMaking);
            Assert.False(dialog.FramePlaysNumeric.Enabled);
            Assert.False(dialog.VideoSpeedCheck.Enabled);
            Assert.False(dialog.FrameEveryNumeric.Enabled);

            dialog.StopButton.PerformClick();
            Assert.False(await making);
            Assert.True(dialog.FramePlaysNumeric.Enabled); // the tick is off, so the box is there to use again
            Assert.True(dialog.VideoSpeedCheck.Enabled);
        }, seconds: 180);
    }

    [Fact]
    public void ATimeOfAFrame_BeyondTheRange_IsBroughtBackToIt()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            dialog.FrameEveryNumeric.Value = dialog.FrameEveryNumeric.Maximum;
            Assert.Equal(1, dialog.Plan!.FrameCount); // one frame of a minute: the frame at the start

            Assert.Throws<ArgumentOutOfRangeException>(() => dialog.FrameEveryNumeric.Value = 61m);
            Assert.Throws<ArgumentOutOfRangeException>(() => dialog.FrameEveryNumeric.Value = 0.01m);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TheGif_IsAskedWhereToGo_WithTheNameOfTheVideo_InItsFolder()
    {
        var video = Video("holiday trip.mp4");
        if (video is null)
            return;

        (string Title, string Filter, string Folder, string Name)? asked = null;
        var gif = _folder.File("anywhere.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() =>
        {
            var dialog = Create(video);
            dialog.AskSavePath = (title, filter, folder, name) =>
            {
                asked = (title, filter, folder, name);
                return gif;
            };
            return dialog;
        }, async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            await Made(dialog);

            Assert.Equal(("Save the GIF", "GIF animation (*.gif)|*.gif", _folder.Path, "holiday trip.gif"), asked);
            Assert.True(File.Exists(gif));
        });
    }

    [Fact]
    public void ANameWithoutAnExtension_GetsDotGif()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: _folder.File("plain")), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            await Made(dialog);

            Assert.Equal(_folder.File("plain.gif"), dialog.SavedPath);
            Assert.True(File.Exists(_folder.File("plain.gif")));
            Assert.False(File.Exists(_folder.File("plain")));
        });
    }

    [Fact]
    public void WhenNoPlaceIsPicked_NothingIsMade()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() =>
        {
            var dialog = Create(video);
            dialog.AskSavePath = (_, _, _, _) => null;
            return dialog;
        }, async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.False(await dialog.MakeAsync());

            Assert.Null(dialog.SavedPath);
            Assert.False(dialog.IsMaking);
            Assert.Empty(Directory.GetFiles(_folder.Path, "*.gif"));
            Assert.True(dialog.MakeButton.Enabled);
        });
    }

    [Fact]
    public void WhileTheGifIsMade_TheSettingsAreLocked_AndStopWorks()
    {
        var video = Video("long.mp4", seconds: 90);
        if (video is null)
            return;

        var gif = _folder.File("stopped.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: gif), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.OriginalCheck.Checked = true;
            dialog.FrameEveryNumeric.Value = 0.04m;

            var making = dialog.MakeAsync();
            Assert.True(dialog.IsMaking);
            Assert.True(dialog.ProgressVisible);
            Assert.False(dialog.MakeButton.Enabled);
            Assert.False(dialog.BrowseButton.Enabled);
            Assert.False(dialog.FrameEveryNumeric.Enabled);
            Assert.False(dialog.FirstSlider.Enabled);
            Assert.False(dialog.OriginalCheck.Enabled);
            Assert.True(dialog.StopButton.Enabled);
            Assert.Equal(ProgressBarStyle.Marquee, dialog.ProgressStyle); // the palette: there is no way to tell how far it is

            Assert.True(await Ui.Until(() => System.Text.RegularExpressions.Regex.IsMatch(dialog.StatusText, "^Drawing the frames: [1-9][0-9]*%"), 60000), $"the frames were being drawn ({dialog.StatusText})");
            Assert.Equal(ProgressBarStyle.Continuous, dialog.ProgressStyle);
            dialog.StopButton.PerformClick();

            Assert.False(await making);
            Assert.Equal(VideoToGifStrings.StatusStopped, dialog.StatusText);
            Assert.False(File.Exists(gif));
            Assert.Empty(Directory.GetFiles(_folder.Path, "*.part"));
            Assert.Null(dialog.SavedPath);
            Assert.True(dialog.MakeButton.Enabled);
            Assert.True(dialog.FrameEveryNumeric.Enabled);
            Assert.False(dialog.WidthNumeric.Enabled); // original size is still on
            Assert.False(dialog.ProgressVisible);
        }, seconds: 180);
    }

    [Fact]
    public void WhenFfmpegIsGone_TheWindowSaysSo()
    {
        var video = Video();
        if (video is null)
            return;

        var gif = _folder.File("never.gif");
        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video, saveTo: gif), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FfmpegPath = Path.Combine(_folder.Path, "gone", "ffmpeg.exe");

            Assert.False(await dialog.MakeAsync());

            Assert.StartsWith("ffmpeg, which makes the GIF, was not found", dialog.StatusText);
            Assert.False(File.Exists(gif));
            Assert.True(dialog.MakeButton.Enabled);
        });
    }

    [Fact]
    public void AGifThatCannotBeWritten_IsToldAsAFailure_NotACrash()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        // The place is a file that is a folder
        var blocked = _folder.File("blocked.gif");
        Directory.CreateDirectory(blocked);
        Ui.Run(() => Create(video, saveTo: blocked), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.False(await dialog.MakeAsync());

            Assert.Contains("could not be made", dialog.StatusText);
            Assert.True(dialog.MakeButton.Enabled);
        });
    }

    // ── What is kept ────────────────────────────────────────────────────────

    [Fact]
    public void TheValues_AreKeptWhenTheWindowIsClosed_AndComeBackNextTime()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);
            dialog.FrameEveryNumeric.Value = 0.25m;
            dialog.WidthNumeric.Value = 480;
            dialog.ColorsNumeric.Value = 64;
            dialog.DitherCombo.SelectedIndex = 1;
            dialog.RepeatCombo.SelectedIndex = 1;
            dialog.FirstNumeric.Value = 30; // the range is not kept: it belongs to one video
        });

        var kept = GifSettings.Load(SettingsFile);
        Assert.Equal((0.25, 480, false, 64, GifDither.Bayer, GifLoop.Once), (kept.FrameSeconds, kept.Width, kept.OriginalSize, kept.Colors, kept.Dither, kept.Loop));

        Ui.Run(() => Create(video), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Opened(dialog);

            Assert.Equal((0.25m, 480, 64), (dialog.FrameEveryNumeric.Value, (int)dialog.WidthNumeric.Value, (int)dialog.ColorsNumeric.Value));
            Assert.Equal(GifDither.Bayer, dialog.ReadSettings().Dither);
            Assert.Equal(GifLoop.Once, dialog.ReadSettings().Loop);
            Assert.Equal(1, dialog.FirstFrame);
        });
    }

    [Fact]
    public void ASettingsFileThatIsBroken_GivesTheDefaults()
    {
        File.WriteAllText(SettingsFile, "{ this is not json");
        Ui.Run(() => Create(null), async form =>
        {
            var dialog = (VideoToGifDialog)form;
            await Task.Delay(200);

            Assert.Equal(0.1m, dialog.FrameEveryNumeric.Value);
            Assert.Equal(640, dialog.WidthNumeric.Value);
            Assert.Equal(256, dialog.ColorsNumeric.Value);
            Assert.Equal(GifDither.Sierra, dialog.ReadSettings().Dither);
            Assert.Equal(GifLoop.Forever, dialog.ReadSettings().Loop);
        });
    }

    // ── The look ────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindow_FitsASmallScreen_InBothThemes()
    {
        foreach (var dark in new[] { false, true })
        {
            Ui.Run(() => Create(null, dark), async form =>
            {
                var dialog = (VideoToGifDialog)form;
                await Task.Delay(200);

                var screen = Screen.FromControl(dialog).WorkingArea;
                Assert.True(dialog.Width <= screen.Width && dialog.Height <= screen.Height, "the window is not bigger than the screen");
                Assert.NotNull(dialog.Icon);
                Assert.Equal(dark ? ThemeManager.DarkBack : ThemeManager.LightBack, dialog.BackColor);
            });
        }
    }

    // ── The button of the main window ───────────────────────────────────────

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static ToolStrip Toolbar(Form form) =>
        Ui.Controls<ToolStrip>(form).Single(strip => strip is not StatusStrip && strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "colorizeToolStripButton"));

    [Fact]
    public void TheMainWindow_HasTheVideoToGifButton_AfterTraceBitmap_AndBeforeTheStickerAndDraw()
    {
        Ui.Run(() => new MainForm(), async form =>
        {
            var names = Toolbar(form).Items.Cast<ToolStripItem>().Select(item => item.Name).ToList();

            Assert.Equal("drawToolStripButton", names[^1]);
            Assert.Equal("stickerToolStripButton", names[^2]);
            Assert.Equal("videoToGifToolStripButton", names[^3]);
            Assert.Equal("traceBitmapToolStripButton", names[^4]);
            var button = Toolbar(form).Items["videoToGifToolStripButton"]!;
            Assert.Equal(VideoToGifStrings.ButtonText, button.Text);
            Assert.Equal(VideoToGifStrings.ButtonTooltip, button.ToolTipText);
            Assert.NotNull(button.Image);
            Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
            await Task.CompletedTask;
        }, seconds: 60);
    }

    [Fact]
    public void ForAVideo_TheButtonOpensTheWindow_ForThatVideo()
    {
        var video = Video();
        if (video is null)
            return;

        using var _ = TestFfmpeg.Use();
        Ui.Run(() => new MainForm(_folder.Path), async form =>
        {
            await Task.Delay(300);
            Field<PictureBox>(form, "pictureBox1").Tag = video;

            form.BeginInvoke(() => ((ToolStripButton)Toolbar(form).Items["videoToGifToolStripButton"]!).PerformClick());
            Assert.True(await Ui.Until(() => Ui.Find("VideoToGifDialog") is not null, 30000), "the window opened");

            var dialog = (VideoToGifDialog)Ui.Find("VideoToGifDialog")!;
            await Opened(dialog);
            Assert.Equal(video, dialog.VideoPath);
            dialog.Close();
            Assert.True(await Ui.Until(() => Ui.Find("VideoToGifDialog") is null, 10000), "the window closed");
        }, seconds: 90);
    }
}
