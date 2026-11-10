namespace PhotoLiker.Effects.Models
{
    /// <summary>
    /// Lets an ONNX session be disposed while other threads are running on it. The session is native memory: freeing it under a run that is going on (the program is closing while the slideshow
    /// works out the depth of a photo on a thread of the pool, say) is an access violation that no <c>catch</c> can stop. Each run says it begins (<see cref="Enter"/>) and ends (<see cref="Leave"/>);
    /// <see cref="Dispose"/> frees the session at once if nothing runs, else the last run to end frees it; a run that begins after <see cref="Dispose"/> is refused with an
    /// <see cref="ObjectDisposedException"/>, which is an error like any other for whoever called it.
    /// </summary>
    internal sealed class RunGuard(Action release, string name)
    {
        private readonly object _gate = new();
        private int _running;
        private bool _disposed;
        private bool _released;

        /// <exception cref="ObjectDisposedException">The session has been disposed of: nothing more is run on it.</exception>
        public void Enter()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, name);
                _running++;
            }
        }

        public void Leave()
        {
            bool free;
            lock (_gate)
            {
                _running--;
                free = _disposed && _running == 0 && !_released;
                _released |= free;
            }

            if (free)
                release();
        }

        public void Dispose()
        {
            bool free;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                free = _running == 0;
                _released = free;
            }

            if (free)
                release();
        }
    }
}
