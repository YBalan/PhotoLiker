using PhotoLiker.Maui.SmokeTour;

namespace PhotoLiker.Maui.Tests;

public class SmokeDemoFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PhotoLikerMauiTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp files */ }
    }

    [Fact]
    public void Create_WritesTheDemoPhotos_WithTheirMonths()
    {
        SmokeDemoFolder.Create(_folder);

        Assert.Equal(SmokeDemoFolder.FileNames.Order(), Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
        for (var i = 0; i < SmokeDemoFolder.FileNames.Length; i++)
        {
            var path = Path.Combine(_folder, SmokeDemoFolder.FileNames[i]);
            Assert.Equal(SmokeDemoFolder.FileDates[i], File.GetLastWriteTime(path));
            Assert.Equal(SmokeDemoFolder.FileDates[i], File.GetCreationTime(path));
        }
    }

    [Fact]
    public void Create_RecreatesTheFolder()
    {
        SmokeDemoFolder.Create(_folder);
        File.WriteAllText(Path.Combine(_folder, "leftover.txt"), "from the last run");

        SmokeDemoFolder.Create(_folder);

        Assert.False(File.Exists(Path.Combine(_folder, "leftover.txt")));
    }

    [Fact]
    public void WriteBmp_WritesAWellFormedBitmap()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "photo.bmp");

        SmokeDemoFolder.WriteBmp(path, width: 5, height: 3, top: (10, 20, 30), bottom: (200, 210, 220));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);
        var claimedSize = BitConverter.ToInt32(bytes, 2);
        Assert.Equal(bytes.Length, claimedSize);
        // 5 pixels of 3 bytes padded to a 16-byte stride, 3 rows, after 54 bytes of headers
        Assert.Equal(54 + 16 * 3, bytes.Length);
        Assert.Equal(5, BitConverter.ToInt32(bytes, 18));
        Assert.Equal(3, BitConverter.ToInt32(bytes, 22));
        Assert.Equal(24, BitConverter.ToInt16(bytes, 28));
        // The last stored row is the top of the picture: the top colour, as B G R
        Assert.Equal((byte)30, bytes[54 + 16 * 2]);
        Assert.Equal((byte)20, bytes[54 + 16 * 2 + 1]);
        Assert.Equal((byte)10, bytes[54 + 16 * 2 + 2]);
    }
}

public class SmokeTourOptionsTests
{
    [Fact]
    public void Parse_WithoutTheFlag_IsNull()
    {
        Assert.Null(SmokeTourOptions.Parse(["PhotoLiker.Maui.exe"]));
        Assert.Null(SmokeTourOptions.Parse([]));
    }

    [Fact]
    public void Parse_TakesTheFoldersOfTheCommandLine()
    {
        var options = SmokeTourOptions.Parse(["app.exe", "--smoke-tour", "--demo-folder", @"C:\Demo\Holiday", "--smoke-tour-out", @"C:\Logs\tour.log", "--keep"]);

        Assert.NotNull(options);
        Assert.Equal(@"C:\Demo\Holiday", options.DemoFolder);
        Assert.Equal(@"C:\Logs\tour.log", options.LogFile);
        Assert.True(options.KeepDemoFolder);
        Assert.StartsWith(Path.GetTempPath(), options.AppDataFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_IsDeterministicWithinTheProcess_SoTheRedirectAndTheTourAgree()
    {
        var first = SmokeTourOptions.Parse(["app.exe", "--smoke-tour"]);
        var second = SmokeTourOptions.Parse(["app.exe", "--smoke-tour"]);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.AppDataFolder, second.AppDataFolder);
        Assert.Equal(first.DemoFolder, second.DemoFolder);
        Assert.Equal(first.LogFile, second.LogFile);
    }
}

public class SmokeChecksTests
{
    [Fact]
    public void Render_CountsTheFailures_AndEndsWithTheSummary()
    {
        var checks = new SmokeChecks();
        checks.Step("1. A step");
        checks.That(true, "something worked");
        checks.That(false, "something broke");

        Assert.Equal(1, checks.Failures);
        var log = checks.Render();
        Assert.Contains("[ok] something worked", log);
        Assert.Contains("[FAIL] something broke", log);
        Assert.Contains("1 CHECK(S) FAILED", log);
    }

    [Fact]
    public void Render_OfACleanRun_SaysAllPassed()
    {
        var checks = new SmokeChecks();
        checks.That(true, "fine");

        Assert.Equal(0, checks.Failures);
        Assert.Contains("ALL CHECKS PASSED", checks.Render());
    }
}
