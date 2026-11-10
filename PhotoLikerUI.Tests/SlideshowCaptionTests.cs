using PhotoLiker.Core;
using PhotoLiker.Video;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The captions of the slideshow (the row ShowCaptions) in a real show: a real six second video played by the real engine, with the words it is said to have (made up here: what the
/// speech indexer reads is the business of the tests of the video library). The video is played without its sound, which is how a show with captions is mostly watched.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class SlideshowCaptionTests : IDisposable
{
    private static readonly Rectangle ScreenBounds = new(0, 0, 640, 360);

    private static readonly SpeechSegment[] Said =
    [
        new(1_000, 2_500, "first line"),
        new(4_500, 5_500, "  second\n line "),
    ];

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static bool CanPlay => TestFfmpeg.Path is not null && VlcEngine.Find() is not null;

    /// <summary>A show of one video of six seconds (it plays again and again until the show is ended), with these facts; <paramref name="body"/> looks at the screen while it plays.</summary>
    private void Play(IReadOnlyList<SpeechSegment>? speech, bool captions, Func<SlideshowScreen, Task> body)
    {
        using var _ = TestFfmpeg.Use();
        var path = TestFfmpeg.MakeMp4(_folder.Path, "clip.mp4", "2003-01-31T15:06:00Z", seconds: 6);
        var options = new SlideshowVideoOptions(
            Mute: true, SlideshowVideoPart.FullVideo, LowerOtherAudio: 0,
            FactsOf: _ => new SlideshowVideoFacts(Found: null, LengthMs: 6_000, Taken: null, speech), ShowCaptions: captions);
        using var session = new SlideshowSession([new PhotoFile(path, string.Empty)], 0, SlideshowMode.OneByOne, 30, SlideshowWatermark.None, [ScreenBounds], 0, SlideshowEffect.None, options);

        Ui.Run(() => new Form(), async host =>
        {
            host.BeginInvoke(() => session.Run(host)); // modal: it comes back when the show ends
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

    /// <summary>The caption that is on the screen, if one is (the date, the pause mark and the like are not there: the show has no watermark and is not paused).</summary>
    private static Label? Caption(SlideshowScreen screen) => Ui.Controls<Label>(screen).FirstOrDefault(label => label.Visible && label.Text.Length > 0);

    private static async Task<bool> NoCaptionFor(SlideshowScreen screen, int milliseconds)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end)
        {
            if (Caption(screen) is not null)
                return false;
            await Task.Delay(50);
        }
        return true;
    }

    [Fact]
    public void WithCaptionsOn_WhatIsSaid_IsWrittenOverTheVideo_WhileItIsSaid_AndNotWhenNothingIs()
    {
        if (!CanPlay) return;

        Play(Said, captions: true, async screen =>
        {
            Assert.True(await Ui.Until(() => Caption(screen)?.Text == "first line", 40000), "the first words are written"); // the first start of the engine can take ten seconds

            var label = Caption(screen)!;
            Assert.True(new Rectangle(Point.Empty, screen.ClientSize).Contains(label.Bounds), "the caption is inside the screen");
            Assert.True(label.Top > screen.ClientSize.Height / 2, "at the bottom half of it");
            Assert.InRange(label.Left + label.Width / 2, screen.ClientSize.Width / 2 - 2, screen.ClientSize.Width / 2 + 2); // centred

            Assert.True(await Ui.Until(() => Caption(screen) is null, 5000), "when the words are over the caption goes");
            Assert.True(await Ui.Until(() => Caption(screen)?.Text == "second line", 5000), "the next words come, on one line, tidied");
        });
    }

    [Fact]
    public void WithCaptionsOff_NothingIsWritten_ThoughTheSpeechIsThere()
    {
        if (!CanPlay) return;

        Play(Said, captions: false, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.IsPlayingVideo, 20000));
            Assert.True(await NoCaptionFor(screen, 4500), "the first words were due after a second, and are not written");
        });
    }

    [Fact]
    public void AVideoWhoseSpeechWasNotRead_HasNoCaptions_EvenWithTheRowOn()
    {
        if (!CanPlay) return;

        Play(speech: null, captions: true, async screen =>
        {
            Assert.True(await Ui.Until(() => screen.IsPlayingVideo, 20000));
            Assert.True(await NoCaptionFor(screen, 3000));
        });
    }
}
