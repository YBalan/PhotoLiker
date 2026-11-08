using System.Runtime.InteropServices;

namespace PhotoLikerUI
{
    /// <summary>
    /// Asks Windows not to turn the display off and not to put the computer to sleep for as long as it lives (the way a video player does while it plays):
    /// a slideshow that is left to run on its own is looked at without anyone touching the keyboard, so the idle timers would run out. It is a request for a time, not a setting:
    /// Windows takes it back by itself when the program ends, and <see cref="Dispose"/> gives it back. The request belongs to the thread that made it, so <see cref="Dispose"/>
    /// has to be called on that thread (the window's thread, as the slideshow does). The screen saver is not held off by this: the window refuses its system command itself
    /// (see <see cref="SlideshowScreen"/>).
    /// </summary>
    internal sealed class KeepAwake : IDisposable
    {
        private const uint Continuous = 0x80000000;     // ES_CONTINUOUS: the state stays until it is changed again
        private const uint SystemRequired = 0x00000001; // ES_SYSTEM_REQUIRED: no sleep because of idleness
        private const uint DisplayRequired = 0x00000002; // ES_DISPLAY_REQUIRED: the display stays on (the screen saver is a request of its own, which the slideshow window refuses)

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);

        private bool _on;

        private KeepAwake()
        {
            _on = SetThreadExecutionState(Continuous | SystemRequired | DisplayRequired) != 0; // 0: Windows refused; nothing to give back then
        }

        /// <summary>Starts keeping the computer awake, until the result is disposed.</summary>
        public static KeepAwake Begin() => new();

        /// <summary>Whether Windows took the request.</summary>
        public bool IsOn => _on;

        public void Dispose()
        {
            if (!_on)
                return;

            _on = false;
            SetThreadExecutionState(Continuous); // the flags are cleared, and the idle timers count again
        }
    }
}
