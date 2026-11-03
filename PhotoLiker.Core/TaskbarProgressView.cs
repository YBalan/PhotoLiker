namespace PhotoLiker.Core
{
    /// <summary>What one progress bar says right now: it has no known end (a marquee), or <paramref name="Done"/> of <paramref name="Total"/>.</summary>
    public readonly record struct ProgressSample(bool Indeterminate, double Done = 0, double Total = 0);

    public enum TaskbarProgressState
    {
        /// <summary>The button of the program shows nothing.</summary>
        None,

        /// <summary>A bar that runs to and fro: something is going on, the end is not known.</summary>
        Indeterminate,

        /// <summary>A green bar filled to <see cref="TaskbarProgressView.Permille"/>.</summary>
        Normal,
    }

    /// <summary>
    /// What the taskbar button of the program shows (the green bar that fills up under the icon, as when a browser downloads a file): decided from the
    /// progress bars of the program, in a form that can be tested without a window.
    /// </summary>
    public readonly record struct TaskbarProgressView(TaskbarProgressState State, int Permille = 0)
    {
        /// <summary>The bar is filled to <see cref="Permille"/> parts of this many.</summary>
        public const int Scale = 1000;

        public static readonly TaskbarProgressView None = new(TaskbarProgressState.None);

        /// <summary>How a bar looks on the button. A bar whose end is not known, or that has nothing to fill (a total of 0), runs to and fro.</summary>
        public static TaskbarProgressView Of(ProgressSample sample)
        {
            if (sample.Indeterminate || !(sample.Total > 0))
                return new TaskbarProgressView(TaskbarProgressState.Indeterminate);

            var share = double.IsNaN(sample.Done) ? 0 : Math.Clamp(sample.Done / sample.Total, 0, 1);
            var permille = (int)Math.Round(share * Scale, MidpointRounding.AwayFromZero);
            return new TaskbarProgressView(TaskbarProgressState.Normal, permille);
        }

        /// <summary>
        /// The one that goes on the button when several things are running: the bar that was started last, which is the one in front of the person (a dialog opened
        /// over the window, rather than the bar of the window). <paramref name="newestFirst"/> has a null for a bar that is not showing; it is read only as far as it must be.
        /// </summary>
        public static TaskbarProgressView Choose(IEnumerable<ProgressSample?> newestFirst)
        {
            foreach (var sample in newestFirst)
            {
                if (sample is { } running)
                    return Of(running);
            }
            return None;
        }
    }
}
