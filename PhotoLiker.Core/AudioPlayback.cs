namespace PhotoLiker.Core
{
    /// <summary>How the audio of a video goes with its pictures.</summary>
    public enum AudioPlayback
    {
        /// <summary>The audio is played over and over, and the pictures end the video: a longer audio is cut where they end.</summary>
        Loop,

        /// <summary>
        /// The audio is played once, and the pictures end the video: an audio that is shorter is followed by silence, a longer one is cut
        /// where the pictures end.
        /// </summary>
        OnceThenSilence,

        /// <summary>The audio is played once and ends the video: the pictures are longer than it (they were repeated for it).</summary>
        OnceEndsVideo,
    }
}
