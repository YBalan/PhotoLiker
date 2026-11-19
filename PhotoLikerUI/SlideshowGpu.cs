using PhotoLiker.Effects.Gpu;

namespace PhotoLikerUI
{
    /// <summary>
    /// The graphics chip of one slideshow (see <see cref="GpuRenderer"/>): started on a thread of the pool when the show is made (the device and the shaders take 50 to 100 ms), used by the windows of the show
    /// from the moment it is there, let go of when the show is over. There is no renderer when the setting is off, when the computer has no usable chip (a software one is not used: it is no quicker than the
    /// processor), and from the moment a step fails (a chip that is reset, a driver that goes): the processor does everything then, and the show does not notice.
    /// </summary>
    internal sealed class SlideshowGpu : IDisposable
    {
        private readonly Task<GpuRenderer?> _start;
        private volatile bool _failed;
        private int _steps;

        /// <param name="enabled">The setting <c>SlideshowGpu</c>.</param>
        /// <param name="allowSoftware">Use the software chip (WARP) too, which the tests do.</param>
        public SlideshowGpu(bool enabled, bool allowSoftware = false)
        {
            _start = enabled ? Task.Run(() => GpuRenderer.TryCreate(allowSoftware)) : Task.FromResult<GpuRenderer?>(null);
        }

        /// <summary>The renderer, once it has started and as long as it works; null before, when there is none, and after a failure.</summary>
        public GpuRenderer? Renderer => _failed || !_start.IsCompletedSuccessfully ? null : _start.Result;

        /// <summary>Completes with the renderer when it has started (null when there is none).</summary>
        public Task<GpuRenderer?> Ready => _start;

        /// <summary>Waits for the renderer to start (the tests do; a show does not) and says whether there is one.</summary>
        public bool WaitUntilStarted(int milliseconds = 10_000) => _start.Wait(milliseconds) && Renderer is not null;

        /// <summary>How many steps of effects the chip has drawn.</summary>
        public int Steps => Volatile.Read(ref _steps);

        /// <summary>Why the chip is not used: a failed step, or what it was that kept it from starting; null while it works (or while it starts).</summary>
        public string? Problem { get; private set; }

        public void Counted() => Interlocked.Increment(ref _steps);

        /// <summary>A step failed: the chip is not used again in this show.</summary>
        public void Failed(Exception ex)
        {
            Problem = ex.Message;
            _failed = true;
        }

        public void Dispose()
        {
            _failed = true;
            _ = _start.ContinueWith(task =>
            {
                try
                {
                    if (task.IsCompletedSuccessfully)
                        task.Result?.Dispose();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // the chip is gone already
                }
            }, TaskScheduler.Default);
        }
    }
}
