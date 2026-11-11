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
        /// Whose words are the captions of a video that more than one model has heard: the <paramref name="preferredModelId"/> (the SpeechModel setting) if it heard anything in it, else the model that
        /// heard the most (ties go to the smaller id, so that the choice does not change from one look to the next). Null when no model heard anything. A weaker model that was asked to hear
        /// the video again does not take the captions over from the one that is set: the setting says whose text is shown.
        /// </summary>
        public static string? ModelFor(string? preferredModelId, IEnumerable<ModelHeard> heard)
        {
            var withSpeech = heard.Where(h => h.Segments > 0).ToList();
            if (withSpeech.Count == 0)
                return null;

            if (preferredModelId is not null && withSpeech.FirstOrDefault(h => string.Equals(h.ModelId, preferredModelId, StringComparison.OrdinalIgnoreCase)) is { } own)
                return own.ModelId;

            return withSpeech.OrderByDescending(h => h.Segments).ThenBy(h => h.ModelId, StringComparer.Ordinal).First().ModelId;
        }

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
