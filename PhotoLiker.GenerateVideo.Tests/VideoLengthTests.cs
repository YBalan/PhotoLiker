using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoLiker.GenerateVideo.Tests;

/// <summary>
/// The length of a video as the Generate Video window says it will be (the estimate of <see cref="VideoGenerationService.EstimateLength"/>, and the length the sequence says it expects, which is what the
/// progress bar goes by) against the video that ffmpeg really makes, by both ways of making it: the photos as files, and the frames drawn one by one (the depth parallax). Real ffmpeg, so these run
/// where it is (the one that comes with the program, in the repository).
/// </summary>
public class VideoLengthTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLVideoLength_" + Guid.NewGuid().ToString("N"));

    public VideoLengthTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string? Ffmpeg { get; } = FindFfmpeg();

    private static string? FindFfmpeg()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PhotoLiker.sln")))
            directory = directory.Parent;

        var bundled = directory is null ? null : Path.Combine(directory.FullName, "PhotoLikerUI", "ffmpeg", "ffmpeg.exe");
        return bundled is not null && File.Exists(bundled) ? bundled : null;
    }

    private static async Task<string> Run(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(Ffmpeg!, arguments) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        var text = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return text;
    }

    /// <summary>How long the video is (what the container says) and how many frames it has (what decoding all of it gives).</summary>
    private static async Task<(double Seconds, int Frames)> Probe(string video)
    {
        var info = await Run($"-hide_banner -i \"{video}\"");
        var match = Regex.Match(info, @"Duration: (\d+):(\d+):(\d+\.\d+)");
        var seconds = int.Parse(match.Groups[1].Value) * 3600 + int.Parse(match.Groups[2].Value) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        var decoded = await Run($"-hide_banner -i \"{video}\" -map 0:v:0 -f null -");
        var frames = Regex.Matches(decoded, @"frame=\s*(\d+)").Select(m => int.Parse(m.Groups[1].Value)).LastOrDefault();
        return (seconds, frames);
    }

    /// <summary>A window's settings for one video.</summary>
    public sealed record Scenario(
        string Name,
        int Photos,
        double Start,
        double End,
        bool Random = false,
        double? CoverStart = null,
        double? CoverEnd = null,
        bool RepeatPhotos = false,
        bool RepeatAudio = true,
        double? AudioSeconds = null,
        double? BeatBpm = null)
    {
        public override string ToString() => Name;

        /// <summary>
        /// The photos repeat for an audio that is longer than they are and nothing is shown at the end: the video of files ends where the last photo that is shown whole ends, up to one photo
        /// short of the audio (9.04 s for an audio of 10 s and photos of 1 s). This is how it was before the frames were drawn here and it is not fixed: ffmpeg makes a video of stills at the rate
        /// that the stills come, its last frame is that of the last photo, and the end that <c>-shortest</c> and <c>-t</c> find is the end of that frame. Making the video at a constant 25 a second
        /// (<c>fps=25</c>) makes these exact and every other video up to a second too long, so it is not a way out.
        /// </summary>
        public bool FilesEndEarly => RepeatPhotos && AudioSeconds is not null && CoverEnd is null;
    }

    public static IEnumerable<object[]> Scenarios() => new[]
    {
        new Scenario("five photos of half a second", 5, 0.5, 0.5),
        new Scenario("times in steps, 0.3 to 1.2", 7, 0.3, 1.2),
        new Scenario("random times", 9, 0.2, 1.4, Random: true),
        new Scenario("cover at the start", 4, 0.5, 0.5, CoverStart: 2),
        new Scenario("cover at the start and the end", 4, 0.7, 0.7, CoverStart: 1.5, CoverEnd: 1.2),
        new Scenario("cover at the end only", 4, 0.7, 0.7, CoverEnd: 1.2),
        new Scenario("one photo", 1, 1.3, 1.3),
        new Scenario("many short photos", 30, 0.04, 0.2),
        new Scenario("photos of exactly one frame", 12, 0.04, 0.04), // the window does not take less than 0.04 s for a photo
        new Scenario("audio longer than the photos: cut", 4, 0.5, 0.5, AudioSeconds: 9),
        new Scenario("audio shorter: played over and over", 6, 1.0, 1.0, AudioSeconds: 2.5),
        new Scenario("audio shorter: once, then silence", 6, 1.0, 1.0, RepeatAudio: false, AudioSeconds: 2.5),
        new Scenario("photos repeat for a longer audio", 3, 1.0, 1.0, RepeatPhotos: true, AudioSeconds: 10),
        new Scenario("photos repeat for the audio, cover at the start", 3, 1.1, 1.1, RepeatPhotos: true, CoverStart: 2, AudioSeconds: 9),
        new Scenario("photos repeat for the audio, cover at the end", 3, 1.1, 1.1, RepeatPhotos: true, CoverEnd: 2, AudioSeconds: 9),
        new Scenario("photos repeat for the audio, covers at both ends", 3, 0.9, 0.9, RepeatPhotos: true, CoverStart: 1.5, CoverEnd: 1.5, AudioSeconds: 9),
        new Scenario("photos repeat with random times", 4, 0.3, 1.5, Random: true, RepeatPhotos: true, AudioSeconds: 11),
        new Scenario("cut on the beat", 8, 0.4, 0.9, BeatBpm: 120),
        new Scenario("cut on the beat with covers", 8, 0.4, 0.9, CoverStart: 1.2, CoverEnd: 1.0, BeatBpm: 100, AudioSeconds: 14),
    }.Select(s => new object[] { s });

    private async Task<string> Picture(string name)
    {
        var path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
            await Run($"-y -hide_banner -loglevel error -f lavfi -i color=c=0x3070d0:s=64x48 -frames:v 1 \"{path}\"");
        return path;
    }

    private async Task<string> Tone(double seconds)
    {
        var path = Path.Combine(_folder, $"tone{seconds.ToString(CultureInfo.InvariantCulture)}.wav");
        if (!File.Exists(path))
            await Run($"-y -hide_banner -loglevel error -f lavfi -i sine=frequency=440:duration={seconds.ToString(CultureInfo.InvariantCulture)} \"{path}\"");
        return path;
    }

    private static RhythmAnalysis Beats(double bpm, double seconds)
    {
        var period = 60 / bpm;
        var beats = new List<double>();
        for (var time = 0.0; time < seconds; time += period)
            beats.Add(time);
        return new RhythmAnalysis(0.01, [], [.. beats], bpm, seconds);
    }

    /// <summary>What the window does with the settings: the estimate it shows, the sequence it makes, and the audio.</summary>
    private async Task<(double Estimate, VideoSequence Sequence, string? Audio, string[] Files, string? Cover)> Plan(Scenario s)
    {
        var photos = new List<string>();
        for (var i = 0; i < s.Photos; i++)
            photos.Add(await Picture("photo" + (i % 3)));
        var cover = s.CoverStart is null && s.CoverEnd is null ? null : await Picture("cover");
        var audio = s.AudioSeconds is { } length ? await Tone(length) : null;
        var rhythm = s.BeatBpm is { } bpm ? Beats(bpm, s.AudioSeconds ?? 30) : null;
        var samples = s.Random ? VideoGenerationService.DrawRandomSamples(s.Photos, new Random(5)) : null;

        var estimate = VideoGenerationService.EstimateLength(s.Photos, s.Start, s.End, samples, s.CoverStart, s.RepeatPhotos, s.AudioSeconds, s.CoverEnd, rhythm);
        var sequence = VideoGenerationService.BuildSequence(photos, cover, s.CoverStart, s.Start, s.End, samples, s.RepeatPhotos, s.RepeatAudio, s.AudioSeconds, s.CoverEnd, rhythm);
        return (estimate, sequence, audio, [.. photos], cover);
    }

    private sealed class SolidPicture : IVideoPicture
    {
        public void Render(double progress, uint[] frame) => Array.Fill(frame, 0xFF3070D0u);

        public void Dispose()
        {
        }
    }

    private sealed class SolidSource : IVideoPictureSource
    {
        public IVideoPicture Open(string file, int width, int height, double seconds, bool still) => new SolidPicture();
    }

    // ── the two ways of making the video ────────────────────────────────────

    private async Task<(string Video, List<int> Progress)> MakeFromFiles(VideoSequence sequence, string? audio)
    {
        var video = Path.Combine(_folder, "files.mp4");
        var progress = new List<int>();
        await VideoGenerationService.GenerateAsync(sequence, video, 64, 48, "libx264", 30, audio, _ => { }, p => { lock (progress) progress.Add(p); }, Ffmpeg);
        return (video, progress);
    }

    private async Task<(string Video, List<int> Progress)> MakeFromFrames(VideoSequence sequence, string? audio)
    {
        var video = Path.Combine(_folder, "frames.mp4");
        var progress = new List<int>();
        await VideoFrames.GenerateAsync(sequence, video, 64, 48, "libx264", 30, audio, new SolidSource(), new HashSet<string>(), _ => { }, p => { lock (progress) progress.Add(p); }, ffmpegPath: Ffmpeg);
        return (video, progress);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task TheLengthTheWindowSays_IsTheLengthOfTheVideoThatIsMade_ByBothWays(Scenario scenario)
    {
        if (Ffmpeg is null) return;
        var (estimate, sequence, audio, _, _) = await Plan(scenario);

        // What the window shows and what the run goes by are the same number
        Assert.Equal(estimate, sequence.ExpectedSeconds, 1e-9);

        var (files, filesProgress) = await MakeFromFiles(sequence, audio);
        var (frames, framesProgress) = await MakeFromFrames(sequence, audio);
        var fromFiles = await Probe(files);
        var fromFrames = await Probe(frames);

        Console.WriteLine($"{scenario.Name,-52} says {estimate,7:0.000} s | files {fromFiles.Seconds,7:0.00} s | frames {fromFrames.Seconds,7:0.00} s {fromFrames.Frames,5} fr (x25: {fromFrames.Frames / 25.0,7:0.000}) | progress end {filesProgress.LastOrDefault()}/{framesProgress.LastOrDefault()}");

        // The video of files is as long as the container says (ffmpeg makes it of the stills with the time of each, not at a rate of frames: it has a frame for a photo, however long it is shown);
        // the video of frames is as long as its frames at 25 a second, and the container says the same
        if (scenario.FilesEndEarly)
            Assert.InRange(fromFiles.Seconds, estimate - Math.Max(scenario.Start, scenario.End) - 0.1, estimate + 0.12); // known: up to one photo short of the audio
        else
            Assert.Equal(estimate, fromFiles.Seconds, 0.12);

        // The video of frames is exact, in every one of them
        Assert.Equal(estimate, fromFrames.Frames / 25.0, 0.04);
        Assert.Equal(estimate, fromFrames.Seconds, 0.06);

        // The bar of the run goes by the same length: it gets to the end and never goes back
        Assert.Equal(100, filesProgress.Max());
        Assert.Equal(100, framesProgress.Max());
        Assert.Equal(filesProgress.OrderBy(p => p), filesProgress);
        Assert.Equal(framesProgress.OrderBy(p => p), framesProgress);
    }
}
