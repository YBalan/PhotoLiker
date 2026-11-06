namespace PhotoLiker.Video
{
    /// <summary>What a video file says about itself (the header of the file, read by ffmpeg); nothing of the pictures is looked at.</summary>
    /// <param name="DurationSeconds">0 when the file does not say.</param>
    /// <param name="Width">Of the stored pictures; see <see cref="DisplayWidth"/> for what is shown (a phone video held upright is stored sideways and turned by <paramref name="Rotation"/>).</param>
    /// <param name="Rotation">0, 90, 180 or 270 degrees the player turns the stored picture.</param>
    /// <param name="Audio">The first sound track, as "pcm_s16le, 44100 Hz, mono"; null if the video has none.</param>
    /// <param name="BitsPerSecond">Of the whole file; 0 when the file does not say.</param>
    /// <param name="Recorded">The date the camera or phone wrote into the file (local time), as written: the rules that decide whether it is believed are <see cref="VideoDates"/>.</param>
    /// <param name="Latitude">Where it was recorded, if the phone wrote that into the file.</param>
    public sealed record VideoInfo(
        double DurationSeconds, int Width, int Height, int Rotation, double FramesPerSecond, string VideoCodec, string? Audio,
        long BitsPerSecond, DateTime? Recorded, double? Latitude, double? Longitude, string? Encoder)
    {
        /// <summary>A picture that is shown on its side: width and height trade places.</summary>
        public bool Turned => Rotation is 90 or 270;

        public int DisplayWidth => Turned ? Height : Width;

        public int DisplayHeight => Turned ? Width : Height;

        public TimeSpan Duration => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
    }
}
