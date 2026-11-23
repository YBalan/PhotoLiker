namespace PhotoLiker.Stickers
{
    /// <summary>Why a sticker could not be made.</summary>
    public enum StickerFailure
    {
        /// <summary>Nothing of the photo was found to be the subject (the cutout is empty), so there is nothing to make a sticker of.</summary>
        NoSubject,

        /// <summary>There is no ffmpeg to write a WebP with: the one that comes with the program is gone and there is none on the PATH.</summary>
        FfmpegNotFound,

        /// <summary>ffmpeg ran and stopped with an error; <see cref="StickerException.Detail"/> is its last message.</summary>
        FfmpegFailed,

        /// <summary>ffmpeg said it was done, but there is no WebP.</summary>
        NoOutput,
    }

    /// <summary>
    /// A sticker that could not be made. The message is English and meant for a log; the front end words <see cref="Reason"/> for the person and shows <see cref="Detail"/> as it is (ffmpeg's own words).
    /// Stopping the work is not this: that is an <see cref="OperationCanceledException"/>.
    /// </summary>
    public sealed class StickerException(StickerFailure reason, string detail, Exception? inner = null) : Exception(MessageOf(reason, detail), inner)
    {
        public StickerFailure Reason { get; } = reason;

        /// <summary>The path of the ffmpeg that was not found, or the last message ffmpeg wrote.</summary>
        public string Detail { get; } = detail;

        private static string MessageOf(StickerFailure reason, string detail) => reason switch
        {
            StickerFailure.NoSubject => "No subject was found in the photo.",
            StickerFailure.FfmpegNotFound => $"ffmpeg was not found ({detail}).",
            StickerFailure.FfmpegFailed => $"ffmpeg stopped: {detail}",
            _ => $"ffmpeg made no WebP ({detail}).",
        };
    }
}
