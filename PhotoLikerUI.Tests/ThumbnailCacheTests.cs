using System.Diagnostics;
using PhotoLikerUI;

namespace PhotoLikerUI.Tests;

/// <summary>
/// The thumbnail cache: what is asked for is read on another thread, and the thread that asks is not kept waiting. The cache is made on the thread of a window, as it is in the program: what
/// it keeps is only touched there.
/// </summary>
[Collection(WindowsCollection.Name)]
public sealed class ThumbnailCacheTests
{
    [Fact]
    public void APhotoThatIsAskedFor_IsDecodedOnAnotherThread_AndTheAskingReturnsAtOnce()
    {
        Ui.Run(() => new Form(), async host =>
        {
            var asking = Environment.CurrentManagedThreadId;
            var decodingThread = 0;
            var loaded = new TaskCompletionSource<string>();
            using var cache = new ThumbnailCache(path =>
            {
                decodingThread = Environment.CurrentManagedThreadId;
                Thread.Sleep(300); // a photo that takes its time to read
                return new Bitmap(4, 4);
            });
            cache.Loaded += path => loaded.TrySetResult(path);
            cache.Want(["a.jpg"]);

            var watch = Stopwatch.StartNew();
            var first = cache.TryGet("a.jpg", out _);

            Assert.Null(first);
            Assert.True(watch.ElapsedMilliseconds < 200, $"asking took {watch.ElapsedMilliseconds} ms: the photo was read on the thread that asked");
            Assert.Equal("a.jpg", await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.NotEqual(asking, decodingThread);
            Assert.NotNull(cache.TryGet("a.jpg", out _));
        });
    }

    [Fact]
    public void ManyPhotosAskedForAtOnce_NoneIsReadOnTheThreadThatAsks()
    {
        Ui.Run(() => new Form(), async host =>
        {
            var asking = Environment.CurrentManagedThreadId;
            var onAskingThread = 0;
            var remaining = 40;
            var done = new TaskCompletionSource();
            using var cache = new ThumbnailCache(path =>
            {
                if (Environment.CurrentManagedThreadId == asking)
                    Interlocked.Increment(ref onAskingThread);
                return new Bitmap(2, 2);
            });
            cache.Loaded += _ =>
            {
                if (--remaining == 0)
                    done.TrySetResult();
            };
            var paths = Enumerable.Range(0, 40).Select(i => $"p{i}.jpg").ToList();
            cache.Want(paths);

            foreach (var path in paths)
                cache.TryGet(path, out _);

            await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(0, onAskingThread);
            Assert.Equal(40, cache.Count);
        });
    }

    [Fact]
    public void OnlyTheWantedPhotosAreRead()
    {
        Ui.Run(() => new Form(), async host =>
        {
            var read = new List<string>();
            var remaining = 2;
            var done = new TaskCompletionSource();
            using var cache = new ThumbnailCache(path =>
            {
                lock (read)
                    read.Add(path);
                return new Bitmap(2, 2);
            });
            cache.Loaded += _ =>
            {
                if (--remaining == 0)
                    done.TrySetResult();
            };
            cache.Want(["wanted.jpg"]);

            cache.TryGet("wanted.jpg", out _);
            cache.TryGet("other.jpg", out _); // not on the screen: skipped when its turn comes

            await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
            lock (read)
                Assert.Equal(["wanted.jpg"], read);
        });
    }
}
