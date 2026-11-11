namespace PhotoLikerUI.Tests;

/// <summary>The memory log of the slideshow: what a line says, that it keeps coming, and that the file stays small.</summary>
public sealed class SlideshowMemoryLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "plmemlog-" + Guid.NewGuid().ToString("N"));
    private string LogFile => Path.Combine(_folder, SlideshowMemoryLog.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheLogBeginsWithTheShow_ThenALineOfFacts_AndEndsWithTheEnd()
    {
        var calls = 0;
        using (new SlideshowMemoryLog(_folder, "5 photos, effect Parallax", () => "changes=" + ++calls, intervalMs: 600_000))
        {
        }

        var lines = File.ReadAllLines(LogFile);
        Assert.Equal(3, lines.Length);
        Assert.Contains("slideshow begins: 5 photos, effect Parallax", lines[0]);
        Assert.Contains("private=", lines[1]);
        Assert.Contains("workingset=", lines[1]);
        Assert.Contains("managed=", lines[1]);
        Assert.Contains("handles=", lines[1]);
        Assert.Contains("gdi=", lines[1]);
        Assert.Contains("ffmpeg=", lines[1]);
        Assert.EndsWith("changes=1", lines[1]); // what the show says is on the line
        Assert.Contains("slideshow ends", lines[2]);
    }

    [Fact]
    public async Task WhileTheShowRuns_ALineComesEveryInterval()
    {
        using (new SlideshowMemoryLog(_folder, "show", () => string.Empty, intervalMs: 50))
            await Task.Delay(700);

        var memoryLines = File.ReadAllLines(LogFile).Count(line => line.Contains("private="));
        Assert.True(memoryLines >= 4, $"{memoryLines} lines");
    }

    [Fact]
    public void AFileThatHasGrownTooBig_BecomesTheOldOne_AndANewOneIsBegun()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(LogFile, new string('x', (int)SlideshowMemoryLog.MaxBytes + 10));

        using (new SlideshowMemoryLog(_folder, "show", () => string.Empty, intervalMs: 600_000))
        {
        }

        Assert.True(File.Exists(Path.Combine(_folder, "slideshow-memory.old.log")));
        Assert.True(new FileInfo(LogFile).Length < 5_000, "the new file is small");
        Assert.DoesNotContain("xxxx", File.ReadAllText(LogFile));
    }

    [Fact]
    public void AFactsFunctionThatFails_DoesNotStopTheLine()
    {
        using (new SlideshowMemoryLog(_folder, "show", () => throw new InvalidOperationException("no"), intervalMs: 600_000))
        {
        }

        Assert.Contains(File.ReadAllLines(LogFile), line => line.Contains("private="));
    }

    [Fact]
    public void ALogThatCanNotBeWritten_IsNoReasonToFail()
    {
        Directory.CreateDirectory(_folder);
        Directory.CreateDirectory(LogFile); // a folder where the file should be: every write fails

        using var log = new SlideshowMemoryLog(_folder, "show", () => string.Empty, intervalMs: 600_000);
        Assert.Contains("private=", log.Line());
    }
}
