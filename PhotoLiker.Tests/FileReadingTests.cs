using System.Diagnostics;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class FileReadingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PLFileReadingTests_" + Guid.NewGuid());

    public FileReadingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ReadsTheWholeFile()
    {
        var path = Path.Combine(_dir, "a.bin");
        byte[] content = [1, 2, 3, 4, 5];
        File.WriteAllBytes(path, content);

        Assert.Equal(content, FileReading.ReadAllBytesShared(path));
    }

    [Fact]
    public void EmptyFile_IsEmpty()
    {
        var path = Path.Combine(_dir, "empty.bin");
        File.WriteAllBytes(path, []);

        Assert.Empty(FileReading.ReadAllBytesShared(path));
    }

    [Fact]
    public void FileThatAnotherProgramHasOpenForWriting_CanStillBeRead()
    {
        var path = Path.Combine(_dir, "busy.bin");
        File.WriteAllBytes(path, [9, 9, 9]);
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        Assert.Equal([9, 9, 9], FileReading.ReadAllBytesShared(path));
    }

    [Fact]
    public void MissingFile_FailsAtOnce_WithoutRetrying()
    {
        var watch = Stopwatch.StartNew();

        Assert.Throws<FileNotFoundException>(() => FileReading.ReadAllBytesShared(Path.Combine(_dir, "missing.bin")));

        // A retry would wait 300 ms before the second attempt
        Assert.True(watch.ElapsedMilliseconds < 250, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void MissingFolder_FailsAtOnce_WithoutRetrying()
    {
        var watch = Stopwatch.StartNew();

        Assert.Throws<DirectoryNotFoundException>(() => FileReading.ReadAllBytesShared(Path.Combine(_dir, "nope", "a.bin")));

        Assert.True(watch.ElapsedMilliseconds < 250, $"took {watch.ElapsedMilliseconds} ms");
    }
}
