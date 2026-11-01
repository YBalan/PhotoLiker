using System.Diagnostics;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class FileDatesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PLFileDatesTests_" + Guid.NewGuid());

    public FileDatesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void TheDates_SetThroughTheHandle_StayWhenTheFileIsClosed()
    {
        var path = Path.Combine(_folder, "a.bin");
        var created = new DateTime(2016, 5, 19, 6, 7, 8, DateTimeKind.Utc);
        var written = new DateTime(2017, 6, 20, 7, 8, 9, DateTimeKind.Utc);

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write([1, 2, 3]);
            stream.Flush(flushToDisk: true);
            FileDates.SetOnHandle(stream, created, written);
        }

        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AFileWrittenNextToAnother_TakesItsPlace_WithItsDates()
    {
        var path = Path.Combine(_folder, "photo.jpg");
        var temp = path + ".saving";
        File.WriteAllBytes(path, [1]);
        var written = new DateTime(2017, 6, 20, 7, 8, 9, DateTimeKind.Utc);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write([1, 2, 3]);
            FileDates.SetOnHandle(stream, written, written);
        }

        FileDates.MoveOver(temp, path);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.False(File.Exists(temp));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AFileThatIsReadOnly_IsNotReplaced_AndNotWaitedFor()
    {
        var path = Path.Combine(_folder, "photo.jpg");
        var temp = path + ".saving";
        File.WriteAllBytes(path, [1]);
        File.WriteAllBytes(temp, [1, 2, 3]);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            var clock = Stopwatch.StartNew();
            Assert.ThrowsAny<Exception>(() => FileDates.MoveOver(temp, path));
            Assert.True(clock.ElapsedMilliseconds < 1000, $"it waited {clock.ElapsedMilliseconds} ms");
            Assert.Equal([1], File.ReadAllBytes(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void AMoveThatCanNeverWork_GivesUpWithTheError()
    {
        var temp = Path.Combine(_folder, "missing.saving");

        Assert.ThrowsAny<IOException>(() => FileDates.MoveOver(temp, Path.Combine(_folder, "photo.jpg")));
    }
}
