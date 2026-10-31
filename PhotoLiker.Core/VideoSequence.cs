namespace PhotoLiker.Core
{
    /// <summary>
    /// What goes into a video, picture by picture: the files in their order, the time each is shown (cover first, when there is one), and
    /// how the audio goes with them. Made by <see cref="VideoGenerationService.BuildSequence"/>.
    /// </summary>
    /// <param name="Files">The pictures to list for ffmpeg, in order; photos that are repeated are listed again each time.</param>
    /// <param name="Durations">The time of each file, one for each of <paramref name="Files"/>.</param>
    /// <param name="ExpectedSeconds">How long the video will be (what the progress bar goes by).</param>
    /// <param name="Audio">What the audio does (it is of no matter when the video has none).</param>
    public sealed record VideoSequence(IReadOnlyList<string> Files, IReadOnlyList<double> Durations, double ExpectedSeconds, AudioPlayback Audio)
    {
        /// <summary>The photos are repeated until they are longer than the audio, which is played once and ends the video.</summary>
        public bool AudioEndsVideo => Audio == AudioPlayback.OnceEndsVideo;

        /// <summary>
        /// Where the audio ends the video (the photos are listed past it, and are cut there), or null when the photos end it.
        /// </summary>
        public double? AudioEndSeconds => AudioEndsVideo ? ExpectedSeconds : null;
    }
}
