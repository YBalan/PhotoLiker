using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class VideoGenerationServiceTests
{
    // ── SortSourceFiles ─────────────────────────────────────────────────────

    private static readonly DateTime Day1 = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PhotoFile Photo(string path, int createdDay = 0, int writtenDay = 0) => new(path, string.Empty)
    {
        CreationTimeUtc = createdDay == 0 ? default : Day1.AddDays(createdDay),
        LastWriteTimeUtc = writtenDay == 0 ? default : Day1.AddDays(writtenDay),
    };

    private static string[] Paths(IEnumerable<PhotoFile> photos) => [.. photos.Select(p => p.OriginalFilePath)];

    [Fact]
    public void SortSourceFiles_OldestFirst_GoesByTheCreatedDate()
    {
        PhotoFile[] photos = [Photo("c", createdDay: 3), Photo("a", createdDay: 1), Photo("b", createdDay: 2)];

        Assert.Equal(["a", "b", "c"], Paths(VideoGenerationService.SortSourceFiles(photos, PhotoSortOrder.OldestFirst)));
    }

    [Fact]
    public void SortSourceFiles_NewestFirst_IsTheOldestFirstBackwards()
    {
        // Two photos of one moment keep to the path, so the one order is exactly the other reversed
        PhotoFile[] photos = [Photo("b", createdDay: 1), Photo("c", createdDay: 3), Photo("a", createdDay: 1)];

        var oldest = Paths(VideoGenerationService.SortSourceFiles(photos, PhotoSortOrder.OldestFirst));
        var newest = Paths(VideoGenerationService.SortSourceFiles(photos, PhotoSortOrder.NewestFirst));

        Assert.Equal(["a", "b", "c"], oldest);
        Assert.Equal([.. oldest.Reverse()], newest);
    }

    [Fact]
    public void SortSourceFiles_ByName_IgnoresTheDates_AndCountsNumbersAsNumbers()
    {
        // The newest photo has the smallest number, the oldest the biggest
        PhotoFile[] photos = [Photo(@"C:\p\IMG_10.jpg", createdDay: 1), Photo(@"C:\p\IMG_2.jpg", createdDay: 3), Photo(@"C:\p\IMG_1.jpg", createdDay: 4)];

        Assert.Equal([@"C:\p\IMG_1.jpg", @"C:\p\IMG_2.jpg", @"C:\p\IMG_10.jpg"], Paths(VideoGenerationService.SortSourceFiles(photos, PhotoSortOrder.ByName)));
    }

    [Fact]
    public void SortSourceFiles_Random_KeepsEveryPhoto_InANewOrderEachTime_AndLeavesTheSourceAlone()
    {
        var source = Enumerable.Range(0, 12).Select(i => Photo($"p{i:00}", createdDay: i + 1)).ToList();

        var orders = Enumerable.Range(0, 6).Select(_ => Paths(VideoGenerationService.SortSourceFiles(source, PhotoSortOrder.Random))).ToList();

        Assert.All(orders, order => Assert.Equal(Paths(source), order.Order(StringComparer.Ordinal)));
        Assert.True(orders.Select(order => string.Join(",", order)).Distinct().Count() > 1);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => $"p{i:00}"), Paths(source)); // the list that was given is not shuffled
    }

    [Fact]
    public void SortSourceFiles_None_KeepsTheOrderGiven()
    {
        PhotoFile[] photos = [Photo("c", createdDay: 3), Photo("a", createdDay: 1), Photo("b", createdDay: 2)];

        Assert.Equal(["c", "a", "b"], Paths(VideoGenerationService.SortSourceFiles(photos, PhotoSortOrder.None)));
    }

    [Fact]
    public void SortSourceFiles_WithoutACreatedDate_GoesByTheLastWriteDate()
    {
        // Search results do not know when the file was created
        PhotoFile[] photos = [Photo("c", writtenDay: 3), Photo("a", writtenDay: 1), Photo("b", writtenDay: 2)];

        Assert.Equal(["a", "b", "c"], Paths(VideoGenerationService.SortSourceFiles(photos, PhotoSortOrder.OldestFirst)));
    }

    [Fact]
    public void SortSourceFiles_ReturnsACopy()
    {
        List<PhotoFile> source = [Photo("b", createdDay: 2), Photo("a", createdDay: 1)];

        var sorted = VideoGenerationService.SortSourceFiles(source, PhotoSortOrder.OldestFirst);

        Assert.Equal(["b", "a"], Paths(source));
        Assert.NotSame(source, sorted);
    }

    // ── BuildFfmpegArguments ────────────────────────────────────────────────

    [Fact]
    public void BuildFfmpegArguments_KeepsFfmpegFromTurningThePhotos_AsAnInputOption()
    {
        var arguments = VideoGenerationService.BuildFfmpegArguments(@"C:\t\list.txt", @"C:\v\out.mp4", 1920, 1080, "libx264", 23);

        // An input option has to come before the -i it is for
        var option = arguments.IndexOf("-noautorotate", StringComparison.Ordinal);
        Assert.True(option >= 0);
        Assert.True(option < arguments.IndexOf(" -i ", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFfmpegArguments_FitsEveryPhotoIntoTheFrame()
    {
        var arguments = VideoGenerationService.BuildFfmpegArguments(@"C:\t\list.txt", @"C:\v\out.mp4", 1280, 720, "libx265", 28);

        Assert.Contains("-f concat -safe 0", arguments);
        Assert.Contains("scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2", arguments);
        Assert.Contains("-c:v libx265 -crf 28", arguments);
        Assert.EndsWith("\"C:\\v\\out.mp4\"", arguments);
    }

    [Fact]
    public void BuildFfmpegArguments_WithoutAudio_HasNoAudioInput()
    {
        foreach (var none in new string?[] { null, "", "   " })
        {
            var arguments = VideoGenerationService.BuildFfmpegArguments(@"C:\t\list.txt", @"C:\v\out.mp4", 1920, 1080, "libx264", 23, none);

            Assert.DoesNotContain("-stream_loop", arguments);
            Assert.DoesNotContain("-shortest", arguments);
            Assert.DoesNotContain("-map", arguments);
            Assert.DoesNotContain("-c:a", arguments);
            Assert.Equal(1, arguments.Split(" -i ").Length - 1);
        }
    }

    [Fact]
    public void BuildFfmpegArguments_WithAudio_RepeatsItAndEndsWithThePhotos()
    {
        var arguments = VideoGenerationService.BuildFfmpegArguments(@"C:\t\list.txt", @"C:\v\out.mp4", 1920, 1080, "libx264", 23, @"C:\music\my song.mp3");

        // The photos come first, then the audio, played over and over (an input option, so before the -i it is for)
        var photos = arguments.IndexOf("-i \"C:\\t\\list.txt\"", StringComparison.Ordinal);
        var loop = arguments.IndexOf("-stream_loop -1 -i \"C:\\music\\my song.mp3\"", StringComparison.Ordinal);
        Assert.True(photos >= 0);
        Assert.True(loop > photos);

        // The video is as long as the photos, so a longer audio is cut; the picture of the photos, the sound of the audio file
        Assert.Contains("-shortest", arguments);
        Assert.Contains("-map 0:v:0 -map 1:a:0", arguments);
        Assert.Contains("-c:a aac", arguments);
        Assert.True(arguments.IndexOf("-shortest", StringComparison.Ordinal) > loop);
        Assert.EndsWith("\"C:\\v\\out.mp4\"", arguments);
    }

    // ── AudioDecoder ────────────────────────────────────────────────────────

    [Fact]
    public void AudioDecoder_MakesAWavOfTheAudioOnly_ThatIsNotTooLong()
    {
        var arguments = AudioDecoder.BuildArguments(@"C:\music\song.mp3", @"C:\t\preview.wav");

        Assert.Contains("-i \"C:\\music\\song.mp3\"", arguments);
        Assert.Contains("-vn", arguments); // not the cover picture
        Assert.Contains($"-t {AudioDecoder.MaxPreviewSeconds}", arguments);
        Assert.Contains("-ac 2 -ar 44100", arguments);
        Assert.Contains("-c:a pcm_s16le", arguments);
        Assert.EndsWith("\"C:\\t\\preview.wav\"", arguments);
        Assert.Equal(44100 * 2 * 2, AudioDecoder.WavBytesPerSecond);
    }

    // ── BuildFrameDurations ─────────────────────────────────────────────────

    [Fact]
    public void BuildFrameDurations_NoFiles_NoDurations()
    {
        Assert.Empty(VideoGenerationService.BuildFrameDurations(0, 1, 3));
    }

    [Fact]
    public void BuildFrameDurations_OneFile_ShowsForTheStartTime()
    {
        Assert.Equal([2.0], VideoGenerationService.BuildFrameDurations(1, 2, 5));
    }

    [Fact]
    public void BuildFrameDurations_SeveralFiles_GoLinearlyFromStartToEnd()
    {
        Assert.Equal([1.0, 2.0, 3.0], VideoGenerationService.BuildFrameDurations(3, 1, 3));
    }

    [Fact]
    public void BuildFrameDurations_CanGetShorter()
    {
        Assert.Equal([3.0, 2.0, 1.0], VideoGenerationService.BuildFrameDurations(3, 3, 1));
    }

    // ── BuildFfmpegConcatFile ───────────────────────────────────────────────

    [Fact]
    public void BuildFfmpegConcatFile_NamesEveryFileWithItsDuration_AndRepeatsTheLast()
    {
        var text = VideoGenerationService.BuildFfmpegConcatFile(["a.jpg", "b.jpg"], [1.5, 2.5]);

        // ffmpeg's concat demuxer ignores the duration of the last file unless the file is listed once more
        Assert.Equal(
            ["file 'a.jpg'", "duration 1.5", "file 'b.jpg'", "duration 2.5", "file 'b.jpg'"],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void BuildFfmpegConcatFile_EscapesSingleQuotesInPaths()
    {
        var text = VideoGenerationService.BuildFfmpegConcatFile([@"C:\it's\a.jpg"], [1]);

        Assert.Contains(@"file 'C:\it'\''s\a.jpg'", text);
    }

    [Fact]
    public void BuildFfmpegConcatFile_WritesDurationsWithADotWhateverTheCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("uk-UA");

            var text = VideoGenerationService.BuildFfmpegConcatFile(["a.jpg"], [1.5]);

            Assert.Contains("duration 1.5", text);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BuildFfmpegConcatFile_NoFiles_IsEmpty()
    {
        Assert.Equal(string.Empty, VideoGenerationService.BuildFfmpegConcatFile([], []));
    }

    // ── TryParseFfmpegProgressPercent ───────────────────────────────────────

    [Fact]
    public void Progress_OutTime_IsThePercentOfTheTotalDuration()
    {
        // 2.5 s of 10 s; ffmpeg reports out_time_ms in microseconds
        var parsed = VideoGenerationService.TryParseFfmpegProgressPercent("out_time_ms=2500000", 10_000, out var percent);

        Assert.True(parsed);
        Assert.Equal(25, percent);
    }

    [Fact]
    public void Progress_CannotGoOver100()
    {
        VideoGenerationService.TryParseFfmpegProgressPercent("out_time_ms=99000000", 10_000, out var percent);

        Assert.Equal(100, percent);
    }

    [Fact]
    public void Progress_End_Is100()
    {
        var parsed = VideoGenerationService.TryParseFfmpegProgressPercent("progress=end", 0, out var percent);

        Assert.True(parsed);
        Assert.Equal(100, percent);
    }

    [Theory]
    [InlineData("frame=12")]
    [InlineData("out_time_ms=not-a-number")]
    [InlineData("progress=continue")]
    public void Progress_OtherLines_AreNotProgress(string line)
    {
        Assert.False(VideoGenerationService.TryParseFfmpegProgressPercent(line, 10_000, out _));
    }

    [Fact]
    public void Progress_WithoutADuration_CannotBeComputed()
    {
        Assert.False(VideoGenerationService.TryParseFfmpegProgressPercent("out_time_ms=1000", 0, out _));
    }
}
