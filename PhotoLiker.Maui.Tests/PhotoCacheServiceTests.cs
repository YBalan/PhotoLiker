using System.Collections.Concurrent;
using PhotoLiker.Maui.Services;

namespace PhotoLiker.Maui.Tests;

public class PhotoCacheServiceTests
{
    private const int PhotoBytes = 8;

    /// <summary>A reader of invented photos that never touches the disk; a photo can be held back until the test releases it.</summary>
    private sealed class FakeReader
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new(StringComparer.Ordinal);
        public readonly ConcurrentBag<string> Reads = [];
        public readonly HashSet<string> Missing = [];

        public void Hold(string path) => _held[path] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string path) => _held[path].SetResult();

        public async Task<byte[]?> ReadAsync(string path, CancellationToken token)
        {
            Reads.Add(path);
            if (_held.TryGetValue(path, out var gate))
                await gate.Task;
            return Missing.Contains(path) ? null : BytesOf(path);
        }
    }

    private static byte[] BytesOf(string path) => [.. System.Text.Encoding.UTF8.GetBytes(path.PadRight(PhotoBytes, '.'))[..PhotoBytes]];

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 5_000)
    {
        var stopAt = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < stopAt)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task SetWindow_LoadsEveryPhotoOfTheWindow_InTheBackground()
    {
        var reader = new FakeReader();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);
        var loaded = new ConcurrentBag<string>();
        cache.Loaded += (path, _) => loaded.Add(path);

        cache.SetWindow(["a", "b", "c"]);

        Assert.True(await Until(() => cache.TryGet("a", out _) && cache.TryGet("b", out _) && cache.TryGet("c", out _)), "all three photos should be cached");
        Assert.True(await Until(() => loaded.Count >= 3), "Loaded should be raised for each");
        Assert.True(cache.TryGet("a", out var entry));
        Assert.Equal(BytesOf("a"), entry!.Full);
    }

    [Fact]
    public async Task SetWindow_DropsThePhotosThatLeftTheWindow()
    {
        var reader = new FakeReader();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);

        cache.SetWindow(["a", "b"]);
        Assert.True(await Until(() => cache.TryGet("a", out _) && cache.TryGet("b", out _)));

        cache.SetWindow(["b", "c"]);

        Assert.True(await Until(() => cache.TryGet("c", out _)));
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _), "a photo still in the window is kept, not loaded again");
    }

    [Fact]
    public async Task Budget_OnceUsedUp_OnlyTheShownPhotoIsStillLoaded()
    {
        var reader = new FakeReader();
        // The budget fits exactly one photo
        using var cache = new PhotoCacheService(maxBytes: PhotoBytes, reader.ReadAsync);

        cache.SetWindow(["a", "b", "c"]);

        Assert.True(await Until(() => cache.TryGet("a", out _) && !cache.TryGet("b", out _) && !cache.TryGet("c", out _) && cache.CachedBytes == PhotoBytes),
            "only the shown photo fits the budget");
    }

    [Fact]
    public async Task Thumbnails_AreResized_AndAnnounced()
    {
        var reader = new FakeReader();
        byte[] marker = [1, 2, 3];
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync, (_, _, _) => Task.FromResult<byte[]?>(marker));
        cache.SetThumbSize(144);
        var thumbs = new ConcurrentBag<string>();
        cache.ThumbReady += (path, _) => thumbs.Add(path);

        cache.SetWindow(["a"]);

        Assert.True(await Until(() => cache.TryGet("a", out var entry) && entry.Thumb is not null));
        Assert.True(cache.TryGet("a", out var cached));
        Assert.Equal(marker, cached!.Thumb);
        Assert.Equal(marker, cached.ThumbOrFull);
        Assert.Equal(144, cached.ThumbSize);
        Assert.Contains("a", thumbs);
    }

    [Fact]
    public async Task Thumbnails_FallBackToTheFullBytes_WhenTheResizeFails()
    {
        var reader = new FakeReader();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync, (_, _, _) => Task.FromResult<byte[]?>(null));
        cache.SetThumbSize(144);

        cache.SetWindow(["a"]);

        // The failed resize is still recorded (ThumbSize), so it is not tried again and again
        Assert.True(await Until(() => cache.TryGet("a", out var entry) && entry.ThumbSize == 144));
        Assert.True(cache.TryGet("a", out var cached));
        Assert.Null(cached!.Thumb);
        Assert.Equal(BytesOf("a"), cached.ThumbOrFull);
    }

    [Fact]
    public async Task SetThumbSize_DropsTheThumbnails_AndTheNextWindowRebuildsThem()
    {
        var reader = new FakeReader();
        var sizes = new ConcurrentBag<int>();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync, (_, size, _) =>
        {
            sizes.Add(size);
            return Task.FromResult<byte[]?>([9, 9]);
        });
        cache.SetThumbSize(100);
        cache.SetWindow(["a"]);
        Assert.True(await Until(() => cache.TryGet("a", out var entry) && entry.ThumbSize == 100));

        cache.SetThumbSize(200);
        Assert.True(cache.TryGet("a", out var dropped));
        Assert.Null(dropped!.Thumb);

        cache.SetWindow(["a"]);
        Assert.True(await Until(() => cache.TryGet("a", out var entry) && entry.ThumbSize == 200));
        Assert.Contains(200, sizes);
    }

    [Fact]
    public async Task GetOrLoadAsync_ReadsAPhotoOutsideTheWindow_WithoutCachingIt()
    {
        var reader = new FakeReader();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);
        cache.SetWindow(["a"]);

        var entry = await cache.GetOrLoadAsync("elsewhere", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal(BytesOf("elsewhere"), entry.Full);
        Assert.False(cache.TryGet("elsewhere", out _), "a photo outside the window is handed out but not kept");
    }

    [Fact]
    public async Task GetOrLoadAsync_OfACachedPhoto_DoesNotReadAgain()
    {
        var reader = new FakeReader();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);
        cache.SetWindow(["a"]);
        Assert.True(await Until(() => cache.TryGet("a", out _)));
        var readsBefore = reader.Reads.Count;

        var entry = await cache.GetOrLoadAsync("a", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal(readsBefore, reader.Reads.Count);
    }

    [Fact]
    public async Task APhotoThatCannotBeRead_Fails_AndIsRetried_WhenItIsShownAgain()
    {
        var reader = new FakeReader();
        reader.Missing.Add("broken");
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);
        var failed = new ConcurrentBag<string>();
        cache.LoadFailed += failed.Add;

        cache.SetWindow(["a", "broken"]);
        Assert.True(await Until(() => failed.Contains("broken")));

        // Still broken: re-announcing the same window does not hammer the file
        cache.SetWindow(["a", "broken"]);
        Assert.True(await Until(() => cache.TryGet("a", out _)));
        var readsWhileBroken = reader.Reads.Count(p => p == "broken");

        // The file is back, and the photo becomes the shown one: that is a retry
        reader.Missing.Remove("broken");
        cache.SetWindow(["broken", "a"]);

        Assert.True(await Until(() => cache.TryGet("broken", out _)));
        Assert.Equal(readsWhileBroken + 1, reader.Reads.Count(p => p == "broken"));
    }

    [Fact]
    public async Task Clear_DropsEverything()
    {
        var reader = new FakeReader();
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);
        cache.SetWindow(["a", "b"]);
        Assert.True(await Until(() => cache.TryGet("a", out _) && cache.TryGet("b", out _)));

        cache.Clear();

        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.Equal(0, cache.CachedBytes);
    }

    [Fact]
    public async Task Clear_AbandonsALoadStillRunning_AndANewWindowStartsFresh()
    {
        var reader = new FakeReader();
        reader.Hold("slow");
        using var cache = new PhotoCacheService(maxBytes: 1_000, reader.ReadAsync);
        cache.SetWindow(["slow"]);
        Assert.True(await Until(() => reader.Reads.Contains("slow")));

        cache.Clear();
        reader.Release("slow");

        cache.SetWindow(["a"]);
        Assert.True(await Until(() => cache.TryGet("a", out _)));
        Assert.False(cache.TryGet("slow", out _), "the abandoned load of the old session is thrown away");
    }
}
