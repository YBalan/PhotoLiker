namespace PhotoLiker.VideoToGif
{
    /// <summary>Why a GIF could not be made.</summary>
    public enum GifFailure
    {
        /// <summary>There is no ffmpeg to run: the one that comes with the program is gone and there is none on the PATH.</summary>
        FfmpegNotFound,

        /// <summary>ffmpeg ran and stopped with an error (a video it cannot read, a place it cannot write to); <see cref="GifException.Detail"/> is its last message.</summary>
        FfmpegFailed,

        /// <summary>ffmpeg said it was done, but there is no GIF (the part asked for has no frames in it).</summary>
        NoOutput,
    }

    /// <summary>
    /// A GIF that could not be made. The message is English and meant for a log; the front end words <see cref="Reason"/> for the person and shows <see cref="Detail"/> as it is (ffmpeg's own words).
    /// Stopping the work is not this: that is an <see cref="OperationCanceledException"/>.
    /// </summary>
    public sealed class GifException(GifFailure reason, string detail, Exception? inner = null) : Exception(MessageOf(reason, detail), inner)
    {
        public GifFailure Reason { get; } = reason;

        /// <summary>The path of the ffmpeg that was not found, or the last message ffmpeg wrote, or the path of the GIF that did not come.</summary>
        public string Detail { get; } = detail;

        private static string MessageOf(GifFailure reason, string detail) => reason switch
        {
            GifFailure.FfmpegNotFound => $"ffmpeg was not found ({detail}).",
            GifFailure.FfmpegFailed => $"ffmpeg stopped: {detail}",
            _ => $"ffmpeg made no GIF ({detail}).",
        };
    }
}
