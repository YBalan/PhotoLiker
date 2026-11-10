namespace PhotoLiker.Video
{
    /// <summary>
    /// The words that are on the screen as a caption at one moment of a video: the viewer writes them under its picture, the slideshow over the picture.
    /// </summary>
    public static class SpeechCaptions
    {
        /// <summary>How long before the model's segment begins its words are shown: the model puts a segment where the voice starts, and the eye wants the words a moment earlier.</summary>
        public const long LeadMs = 300;

        /// <summary>How long after the segment ends its words stay: long enough to read the last of them.</summary>
        public const long LingerMs = 1200;

        /// <summary>
        /// The caption at <paramref name="timeMs"/> (milliseconds from the start of the video): the words of the first segment that covers the moment (a little before it begins to a little after it
        /// ends, see <see cref="LeadMs"/> and <see cref="LingerMs"/>), on one line (the lines the model broke are joined). Null when nothing is said then, or the speech was not read.
        /// </summary>
        public static string? At(IReadOnlyList<SpeechSegment>? speech, long timeMs)
        {
            if (speech is null || speech.Count == 0)
                return null;

            foreach (var segment in speech)
            {
                if (timeMs < segment.StartMs - LeadMs || timeMs > segment.EndMs + LingerMs)
                    continue;

                var text = string.Join(' ', segment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (text.Length > 0)
                    return text;
            }
            return null;
        }
    }
}
