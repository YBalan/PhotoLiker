using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoLiker.Core;
using PhotoLiker.GenerateVideo;
using PhotoLiker.Effects.Models;

namespace PhotoLikerUI.Tests;

/// <summary>
/// Videos whose photos have an effect of the slideshow, made for real: the photos are read, the frames are drawn (the photos moved by their effect, the changes between them made), given to the ffmpeg that
/// comes with the program, and the video is looked at. The effects that need no model (the changes, Ken Burns, Tilt) run wherever ffmpeg is; the ones that need the depth model run where it is too
/// (see <see cref="ModelsFolder"/>), and the tests that need it do nothing where it is not.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class EffectVideoTests : IDisposable
{
    private const int VideoWidth = 640;
    private const int VideoHeight = 360;

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static string ModelsFolder =>
        Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");

    private static bool Available => ToolModels.Depth.IsPresentIn(ModelsFolder) && TestFfmpeg.Path is not null;

    /// <summary>A bright, textured ball in front of a dim wall: any depth model puts the ball nearer, so it moves against the wall.</summary>
    private string MakePhoto(string name, int hue)
    {
        using var bitmap = new Bitmap(640, 400);
        for (var y = 0; y < 400; y++)
        for (var x = 0; x < 640; x++)
        {
            var dx = (x - 320.0) / 140;
            var dy = (y - 200.0) / 120;
            var inside = dx * dx + dy * dy < 1;
            var shade = inside ? (int)(150 + 80 * Math.Sqrt(Math.Max(0, 1 - dx * dx - dy * dy)) + 15 * Math.Sin(x / 3.0)) : 60 + y * 30 / 400;
            bitmap.SetPixel(x, y, inside ? Color.FromArgb(shade * 3 / 5 + hue, shade * 4 / 5, shade) : Color.FromArgb(shade, shade * 9 / 10, shade * 8 / 10));
        }

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    /// <summary>A photo of one colour, the shape of the ball photos: a change between two of them shows in every frame.</summary>
    private string MakeSolid(string name, Color colour)
    {
        using var bitmap = new Bitmap(640, 400);
        using (var g = Graphics.FromImage(bitmap))
            g.Clear(colour);

        var path = _folder.File(name);
        bitmap.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static async Task<byte[]> FrameAt(string video, double seconds)
    {
        using var process = Process.Start(new ProcessStartInfo(
            TestFfmpeg.Path!, $"-hide_banner -loglevel error -ss {seconds.ToString(CultureInfo.InvariantCulture)} -i \"{video}\" -frames:v 1 -f rawvideo -pix_fmt rgb24 -")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        using var bytes = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(bytes);
        await process.WaitForExitAsync();
        Assert.Equal(VideoWidth * VideoHeight * 3, bytes.Length);
        return bytes.ToArray();
    }

    private static (int R, int G, int B) PixelAt(byte[] frame, int x, int y)
    {
        var i = (y * VideoWidth + x) * 3;
        return (frame[i], frame[i + 1], frame[i + 2]);
    }

    private static async Task<double> DurationOf(string video)
    {
        using var process = Process.Start(new ProcessStartInfo(TestFfmpeg.Path!, $"-hide_banner -i \"{video}\"") { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        var text = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var match = Regex.Match(text, @"Duration: (\d+):(\d+):(\d+\.\d+)");
        return int.Parse(match.Groups[1].Value) * 3600 + int.Parse(match.Groups[2].Value) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    private static int Differences(byte[] a, byte[] b, Func<int, int, bool> where)
    {
        var count = 0;
        for (var y = 0; y < VideoHeight; y += 2)
        for (var x = 0; x < VideoWidth; x += 2)
        {
            if (!where(x, y))
                continue;

            var i = (y * VideoWidth + x) * 3;
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24)
                count++;
        }
        return count;
    }

    private static bool InsidePhoto(int x, int y) => x is >= 40 and < 600; // a 640 x 400 photo is 576 x 360 in a 640 x 360 video: bars of 32 pixels, and a little for the edge of the picture

    /// <summary>Makes a video of these photos with these effects and the changes that go with them, as the app does.</summary>
    private static Task Make(EffectModels hub, VideoSequence sequence, SlideshowEffect[] effects, string output, Action<string> log, IReadOnlySet<string>? still = null) =>
        VideoFrames.GenerateAsync(
            sequence, output, VideoWidth, VideoHeight, "libx264", 20, null, new EffectVideoPictures(hub, effects, log), still ?? new HashSet<string>(),
            _ => { }, _ => { }, ffmpegPath: TestFfmpeg.Path, changes: new EffectVideoChanges(effects, VideoWidth, VideoHeight));

    // ── the changes: no model needed ────────────────────────────────────────

    public static IEnumerable<object[]> Changes() =>
        new[]
        {
            SlideshowEffect.Fade, SlideshowEffect.Slide, SlideshowEffect.Zoom, SlideshowEffect.PushUp, SlideshowEffect.PushDown, SlideshowEffect.CrossZoom, SlideshowEffect.Wipe,
            SlideshowEffect.Iris, SlideshowEffect.Pixelate, SlideshowEffect.Ripple, SlideshowEffect.Cube, SlideshowEffect.BlurDissolve, SlideshowEffect.DipToBlack, SlideshowEffect.DipToWhite,
        }.Select(effect => new object[] { effect });

    [Theory]
    [MemberData(nameof(Changes))]
    public void EveryChange_GoesFromTheFirstPhotoToTheSecond_InsideTheTimeOfTheFirst_AndTheVideoHasTheLengthOfItsSequence(SlideshowEffect effect)
    {
        if (TestFfmpeg.Path is null) return;
        using var empty = new TempFolder();
        using var hub = new EffectModels(empty.Path); // no model: none of these needs one
        var first = MakeSolid("a.jpg", Color.FromArgb(220, 40, 40));
        var second = MakeSolid("b.jpg", Color.FromArgb(40, 60, 220));
        var output = Path.Combine(_folder.Path, $"{effect}.mp4");
        var sequence = new VideoSequence([first, second], [1.5, 1.5], 3.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, [effect, effect], output, _ => { });

            Assert.Equal(3.04, await DurationOf(output), 0.15); // the change takes the end of the first photo's time, so the video is no longer than without it

            // The first photo is shown on its own to begin with, the second on its own from the time that is its own
            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 2.9);
            var red = PixelAt(start, 320, 180);
            var blue = PixelAt(end, 320, 180);
            Assert.True(red.R > 150 && red.B < 100, $"the first photo: {red}");
            Assert.True(blue.B > 150 && blue.R < 100, $"the second photo: {blue}");

            // In the last 0.6 s of the first photo's time both of them are in the picture: it is neither of them
            var middle = await FrameAt(output, 1.2);
            Assert.True(Differences(start, middle, (_, _) => true) > 100, "the change has begun");
            Assert.True(Differences(end, middle, (_, _) => true) > 100, "and is not done");
        }, seconds: 180);
    }

    [Fact]
    public void AWipe_ShowsTheSecondPhotoOnTheLeftAndTheFirstOnTheRight_InTheMiddleOfTheChange()
    {
        if (TestFfmpeg.Path is null) return;
        using var empty = new TempFolder();
        using var hub = new EffectModels(empty.Path);
        var first = MakeSolid("a.jpg", Color.FromArgb(220, 40, 40));
        var second = MakeSolid("b.jpg", Color.FromArgb(40, 60, 220));
        var output = Path.Combine(_folder.Path, "wipe.mp4");
        var sequence = new VideoSequence([first, second], [3.0, 3.0], 6.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, [SlideshowEffect.Wipe, SlideshowEffect.Wipe], output, _ => { });

            // 0.8 s of change at the end of the first photo's 3 s: from 2.2 s to 3.0 s, in the middle at 2.6 s
            var middle = await FrameAt(output, 2.6);
            var left = PixelAt(middle, 100, 180);
            var right = PixelAt(middle, 540, 180);
            Assert.True(left.B > 150 && left.R < 100, $"the second photo is coming in from the left: {left}");
            Assert.True(right.R > 150 && right.B < 100, $"the first is still on the right: {right}");

            var before = await FrameAt(output, 2.0);
            Assert.True(PixelAt(before, 100, 180).R > 150, "nothing has begun before the change");
        }, seconds: 180);
    }

    [Fact]
    public void ARandomEffect_MakesAVideoToo_WithAnotherChangeForEveryPhoto_AndNoModel()
    {
        if (TestFfmpeg.Path is null) return;
        using var empty = new TempFolder();
        using var hub = new EffectModels(empty.Path);
        var photos = new[]
        {
            MakeSolid("a.jpg", Color.FromArgb(220, 40, 40)), MakeSolid("b.jpg", Color.FromArgb(40, 60, 220)),
            MakeSolid("c.jpg", Color.FromArgb(40, 200, 60)), MakeSolid("d.jpg", Color.FromArgb(220, 200, 40)),
        };
        var effects = VideoEffects.Plan(SlideshowEffect.Random, photos.Length);
        var output = Path.Combine(_folder.Path, "random.mp4");
        var log = new List<string>();
        var sequence = new VideoSequence(photos, [1.0, 1.0, 1.0, 1.0], 4.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, effects, output, log.Add);

            Assert.Equal(4.04, await DurationOf(output), 0.15);
            Assert.DoesNotContain(log, line => line.Contains("Could not")); // nothing that needs the depth was chosen
            // Every photo is on its own after the change that brings it in and before the one that takes it off (the last 0.4 s of its second)
            foreach (var (seconds, colour) in new[] { (0.3, 0), (1.3, 1), (2.3, 2), (3.9, 3) })
            {
                var frame = PixelAt(await FrameAt(output, seconds), 320, 180);
                Assert.True(colour switch
                {
                    0 => frame.R > 150 && frame.B < 100,
                    1 => frame.B > 150 && frame.R < 100,
                    2 => frame.G > 130 && frame.R < 100 && frame.B < 120,
                    _ => frame.R > 150 && frame.G > 130 && frame.B < 120,
                }, $"the photo number {colour} at {seconds} s: {frame}");
            }
        }, seconds: 180);
    }

    // ── the movements: no model needed ──────────────────────────────────────

    [Theory]
    [InlineData(SlideshowEffect.KenBurns)]
    [InlineData(SlideshowEffect.Tilt)]
    public void ThePhotosOfKenBurnsAndTilt_Move_WithoutAModel_AndAFadeBringsInTheNext(SlideshowEffect effect)
    {
        if (TestFfmpeg.Path is null) return;
        using var empty = new TempFolder();
        using var hub = new EffectModels(empty.Path);
        var first = MakePhoto("a.jpg", 0);
        var second = MakePhoto("b.jpg", 30);
        var output = Path.Combine(_folder.Path, $"{effect}.mp4");
        var sequence = new VideoSequence([first, second], [3.0, 3.0], 6.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, [effect, effect], output, _ => { });

            Assert.Equal(6.04, await DurationOf(output), 0.15);
            var start = await FrameAt(output, 0.1);
            var late = await FrameAt(output, 2.1); // before the change that takes the end of the photo's time begins
            Assert.True(Differences(start, late, (_, _) => true) > 50, "the photo drifts and zooms while it is shown");
        }, seconds: 180);
    }

    // ── the movements that need the depth model ─────────────────────────────

    [Fact]
    public void TheParallaxPhotos_MoveInsideTheirPlace_AndTheVideoHasTheLengthOfItsSequence()
    {
        if (!Available) return;
        using var hub = new EffectModels(ModelsFolder);
        var first = MakePhoto("a.jpg", 0);
        var second = MakePhoto("b.jpg", 30);
        var log = new List<string>();
        var output = Path.Combine(Environment.GetEnvironmentVariable("PL_PARALLAX_OUT") is { Length: > 0 } keep ? keep : _folder.Path, "parallax.mp4");
        var sequence = new VideoSequence([first, second], [3.0, 3.0], 6.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, [SlideshowEffect.Parallax, SlideshowEffect.Parallax], output, log.Add);

            Assert.DoesNotContain(log, line => line.Contains("Could not")); // the depth of both photos was worked out

            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 2.15); // the change that takes the end of the first photo's time (0.8 s) has not begun
            var next = await FrameAt(output, 3.5);

            // The first photo moves while it is shown, and what is round it stays black
            Assert.True(Differences(start, end, (x, y) => InsidePhoto(x, y)) > 50, "the photo moved");
            Assert.Equal(0, Differences(start, end, (x, y) => !InsidePhoto(x, y) && x < 24)); // the bar on the left: still black ...
            Assert.Equal(0, Differences(start, end, (x, y) => !InsidePhoto(x, y) && x >= 616)); // ... and on the right

            // The second photo is another picture
            Assert.True(Differences(end, next, (x, y) => InsidePhoto(x, y)) > 50, "the next photo is shown from 3 s on");

            Assert.Equal(6.04, await DurationOf(output), 0.15);
            Assert.True(new FileInfo(output).Length > 1000);
        }, seconds: 300);
    }

    [Fact]
    public void ADollyZoomAndAPortrait_AreMadeToo_WhereTheModelIs()
    {
        if (!Available) return;
        using var hub = new EffectModels(ModelsFolder);
        var photo = MakePhoto("p.jpg", 0);

        Ui.Run(() => new Form(), async _ =>
        {
            foreach (var effect in new[] { SlideshowEffect.DollyZoom, SlideshowEffect.Portrait })
            {
                var log = new List<string>();
                var output = Path.Combine(_folder.Path, $"{effect}.mp4");
                await Make(hub, new VideoSequence([photo], [2.0], 2.04, AudioPlayback.Loop), [effect], output, log.Add);

                Assert.DoesNotContain(log, line => line.Contains("Could not"));
                var start = await FrameAt(output, 0.1);
                var end = await FrameAt(output, 1.9);
                Assert.True(Differences(start, end, (x, y) => InsidePhoto(x, y)) > 50, $"{effect}: the photo moved");
            }
        }, seconds: 300);
    }

    [Fact]
    public void ACover_StaysStill_WhileThePhotoAfterItMoves()
    {
        if (!Available) return;
        using var hub = new EffectModels(ModelsFolder);
        var cover = MakePhoto("cover.jpg", 0);
        var photo = MakePhoto("p.jpg", 30);
        var output = Path.Combine(_folder.Path, "cover.mp4");
        var sequence = new VideoSequence([cover, photo], [2.0, 2.0], 4.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, [SlideshowEffect.Parallax, SlideshowEffect.Parallax], output, _ => { }, new HashSet<string> { cover });

            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 1.1); // before the photo after it begins to come in (from 1.2 s: 0.8 s at the end of the cover's time)
            Assert.True(Differences(start, end, (_, _) => true) < 20, "the cover does not move (apart from the noise of the encoder)");

            var moving = await FrameAt(output, 2.1);
            var later = await FrameAt(output, 3.85);
            Assert.True(Differences(moving, later, (x, y) => InsidePhoto(x, y)) > 50, "the photo after it does");
        }, seconds: 300);
    }

    [Fact]
    public void WithoutTheModelsFile_APhotoOfADepthEffectIsShownStill_AndAVideoIsStillMade()
    {
        if (TestFfmpeg.Path is null) return;
        using var empty = new TempFolder();
        using var hub = new EffectModels(empty.Path); // no depth model here
        var photo = MakePhoto("p.jpg", 0);
        var log = new List<string>();
        var output = Path.Combine(_folder.Path, "still.mp4");
        var sequence = new VideoSequence([photo], [1.0], 1.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await Make(hub, sequence, [SlideshowEffect.Parallax], output, log.Add);

            Assert.Contains(log, line => line.Contains("Could not work out the depth") && line.Contains("p.jpg")); // the person is told, with the photo
            Assert.True(new FileInfo(output).Length > 500);
            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 0.9);
            Assert.True(Differences(start, end, (_, _) => true) < 20, "a photo without a depth does not move");
        }, seconds: 300);
    }
}
