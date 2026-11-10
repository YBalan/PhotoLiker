using System.Diagnostics;
using System.Drawing.Imaging;
using PhotoLiker.GenerateVideo;
using PhotoLiker.Effects.Models;

namespace PhotoLikerUI.Tests;

/// <summary>
/// A video whose photos move by their depth, made for real: the photos are read, the depth model works out their depth, the frames are drawn and given to the ffmpeg that comes with the
/// program, and the video is looked at. Runs where the depth model is (see <see cref="ModelsFolder"/>) and ffmpeg is.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ParallaxVideoTests : IDisposable
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

    private static async Task<byte[]> FrameAt(string video, double seconds)
    {
        using var process = Process.Start(new ProcessStartInfo(
            TestFfmpeg.Path!, $"-hide_banner -loglevel error -ss {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} -i \"{video}\" -frames:v 1 -f rawvideo -pix_fmt rgb24 -")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        using var bytes = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(bytes);
        await process.WaitForExitAsync();
        Assert.Equal(VideoWidth * VideoHeight * 3, bytes.Length);
        return bytes.ToArray();
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

    [Fact]
    public void ThePhotosMove_InsideTheirPlace_AndTheVideoHasTheLengthOfItsSequence()
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
            await VideoFrames.GenerateAsync(
                sequence, output, VideoWidth, VideoHeight, "libx264", 20, null, new ParallaxVideoPictures(hub, line => log.Add(line)), new HashSet<string>(),
                line => { }, _ => { }, ffmpegPath: TestFfmpeg.Path);

            Assert.DoesNotContain(log, line => line.Contains("Could not")); // the depth of both photos was worked out

            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 2.85);
            var next = await FrameAt(output, 3.5);

            // The first photo moves while it is shown, and what is round it stays black
            Assert.True(Differences(start, end, (x, y) => InsidePhoto(x, y)) > 50, "the photo moved");
            Assert.Equal(0, Differences(start, end, (x, y) => !InsidePhoto(x, y) && x < 24)); // the bar on the left: still black ...
            Assert.Equal(0, Differences(start, end, (x, y) => !InsidePhoto(x, y) && x >= 616)); // ... and on the right

            // The second photo is another picture
            Assert.True(Differences(end, next, (x, y) => InsidePhoto(x, y)) > 50, "the next photo is shown from 3 s on");

            var info = new FileInfo(output);
            Assert.True(info.Length > 1000);
        }, seconds: 300);
    }

    [Fact]
    public void ACover_StaysStill()
    {
        if (!Available) return;
        using var hub = new EffectModels(ModelsFolder);
        var cover = MakePhoto("cover.jpg", 0);
        var photo = MakePhoto("p.jpg", 30);
        var output = Path.Combine(_folder.Path, "cover.mp4");
        var sequence = new VideoSequence([cover, photo], [2.0, 2.0], 4.04, AudioPlayback.Loop);

        Ui.Run(() => new Form(), async _ =>
        {
            await VideoFrames.GenerateAsync(
                sequence, output, VideoWidth, VideoHeight, "libx264", 20, null, new ParallaxVideoPictures(hub, _ => { }), new HashSet<string> { cover },
                _ => { }, _ => { }, ffmpegPath: TestFfmpeg.Path);

            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 1.85);
            Assert.True(Differences(start, end, (_, _) => true) < 20, "the cover does not move (apart from the noise of the encoder)");

            var moving = await FrameAt(output, 2.1);
            var later = await FrameAt(output, 3.85);
            Assert.True(Differences(moving, later, (x, y) => InsidePhoto(x, y)) > 50, "the photo after it does");
        }, seconds: 300);
    }

    [Fact]
    public void WithoutTheModelsFile_APhotoIsShownStill_AndAVideoIsStillMade()
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
            await VideoFrames.GenerateAsync(
                sequence, output, VideoWidth, VideoHeight, "libx264", 20, null, new ParallaxVideoPictures(hub, line => log.Add(line)), new HashSet<string>(),
                _ => { }, _ => { }, ffmpegPath: TestFfmpeg.Path);

            Assert.Contains(log, line => line.Contains("Could not work out the depth") && line.Contains("p.jpg")); // the person is told, with the photo
            Assert.True(new FileInfo(output).Length > 500);
            var start = await FrameAt(output, 0.1);
            var end = await FrameAt(output, 0.9);
            Assert.True(Differences(start, end, (_, _) => true) < 20, "a photo without a depth does not move");
        }, seconds: 300);
    }
}
