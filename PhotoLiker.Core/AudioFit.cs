namespace PhotoLiker.Core
{
    /// <summary>What happens to the audio of a video that plays its photos once: it is played over and over when it is shorter, and cut where the video ends when it is longer.</summary>
    public enum AudioFit
    {
        /// <summary>The audio is longer than the video: it is cut.</summary>
        Cut,

        /// <summary>The audio is shorter than the video: it is played over and over.</summary>
        Repeated,

        /// <summary>The audio is about as long as the video.</summary>
        Same,
    }
}
