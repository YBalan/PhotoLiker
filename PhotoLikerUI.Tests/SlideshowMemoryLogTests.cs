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
    public async Task ALineThatIsBeingMadeWhenTheShowEnds_IsNotWrittenAfterTheEnd_NorWhileTheFileIsRead()
    {
        // A line is made on a thread of the pool, which takes a moment (the facts of the show are asked there). Here the show ends while one is on its way: it used to be written beside the
        // closing line, or after it, and a file that was being appended to could not be read ("used by another process": the release of 4.6.14 failed on it, on a slow machine)
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var log = new SlideshowMemoryLog(_folder, "show", () =>
        {
            if (Interlocked.Increment(ref calls) > 1) // (the first is the line of the start, made by whoever starts the log)
            {
                entered.Set();
                release.Wait(10_000);
            }

            return string.Empty;
        }, intervalMs: 1);

        Assert.True(entered.Wait(10_000), "a line is on its way");
        log.Dispose(); // the show ends meanwhile
        release.Set(); // and the line is made
        await Task.Delay(300); // time to write it, if it were going to be written

        var lines = File.ReadAllLines(LogFile);
        Assert.Contains("slideshow ends", lines[^1]);
        Assert.Single(lines, line => line.Contains("slideshow ends"));
    }

    [Fact]
    public void ALogThatIsDisposedTwice_EndsOnce()
    {
        var log = new SlideshowMemoryLog(_folder, "show", () => string.Empty, intervalMs: 600_000);

        log.Dispose();
        log.Dispose();

        Assert.Single(File.ReadAllLines(LogFile), line => line.Contains("slideshow ends"));
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
