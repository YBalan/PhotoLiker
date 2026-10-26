using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class VideoGenerationServiceTests
{
    // ── SortSourceFiles ─────────────────────────────────────────────────────

    [Fact]
    public void SortSourceFiles_KeepsTheOrder()
    {
        Assert.Equal(["a", "b", "c"], VideoGenerationService.SortSourceFiles(["a", "b", "c"], reverseSourceFiles: false));
    }

    [Fact]
    public void SortSourceFiles_CanReverseTheOrder()
    {
        Assert.Equal(["c", "b", "a"], VideoGenerationService.SortSourceFiles(["a", "b", "c"], reverseSourceFiles: true));
    }

    [Fact]
    public void SortSourceFiles_ReturnsACopy()
    {
        List<string> source = ["a", "b"];

        var sorted = VideoGenerationService.SortSourceFiles(source, reverseSourceFiles: true);

        Assert.Equal(["a", "b"], source);
        Assert.NotSame(source, sorted);
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
