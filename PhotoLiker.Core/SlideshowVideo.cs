namespace PhotoLiker.Core
{
    /// <summary>How much of a video the slideshow plays.</summary>
    public enum SlideshowVideoPart
    {
        /// <summary>
        /// Only the places a search found in the video (a few seconds around each, see <see cref="SlideshowVideo.Segments"/>). A video that no search found anything in (the
        /// slideshow of a folder, not of results) has nothing to cut down to, so it is played whole.
        /// </summary>
        OnlyFoundTimeFrames,

        /// <summary>The whole video, from the first second to the last.</summary>
        FullVideo,
    }

    public sealed class SlideshowVideoPartConverter() : TolerantEnumConverter<SlideshowVideoPart>(SlideshowVideoPart.OnlyFoundTimeFrames);

    /// <summary>One stretch of a video to play: from <see cref="StartMs"/> to <see cref="EndMs"/>, or to the end of the video when that is null.</summary>
    public readonly record struct VideoSegment(long StartMs, long? EndMs);

    /// <summary>The rules of playing videos in the slideshow that do not depend on a window or a player.</summary>
    public static class SlideshowVideo
    {
        /// <summary>The seconds of a video before the place a search found that are played with it: what leads up to it.</summary>
        public const long FoundLeadMs = 2_000;

        /// <summary>How long a stretch around a found place lasts altogether (the lead and what follows).</summary>
        public const long FoundLengthMs = 6_000;

        /// <summary>By how many percent the other programs' sound is turned down while a video plays with its sound, unless the setting says otherwise.</summary>
        public const int DefaultLowerOtherAudioPercent = 50;

        /// <summary>The percentage of the setting kept between 0 (leave the others as they are) and 100 (silence them).</summary>
        public static int LowerOtherAudioOf(int percent) => Math.Clamp(percent, 0, 100);

        /// <summary>The level (0 to 1) the other programs' sound is turned to while a video plays: what is left of it after it was lowered by the percentage.</summary>
        public static float LevelOfOthers(int lowerByPercent) => 1f - LowerOtherAudioOf(lowerByPercent) / 100f;

        /// <summary>
        /// What is played of a video, in order of time. <see cref="SlideshowVideoPart.FullVideo"/>, or a video with no place found in it, is played whole (one stretch to the end).
        /// Otherwise a stretch of <see cref="FoundLengthMs"/> for each place in <paramref name="found"/> (milliseconds from the start), beginning <see cref="FoundLeadMs"/> before
        /// it; stretches that touch or overlap are one, and what lies beyond the end of the video (<paramref name="lengthMs"/>, if known) is left out.
        /// </summary>
        public static IReadOnlyList<VideoSegment> Segments(SlideshowVideoPart part, IReadOnlyList<long>? found, long? lengthMs = null)
        {
            if (part == SlideshowVideoPart.FullVideo || found is not { Count: > 0 })
                return [new VideoSegment(0, null)];

            var segments = new List<VideoSegment>();
            foreach (var place in found.Where(p => p >= 0).Distinct().Order())
            {
                var start = Math.Max(0, place - FoundLeadMs);
                var end = place + (FoundLengthMs - FoundLeadMs);
                if (lengthMs is { } length)
                {
                    if (start >= length)
                        continue;
                    end = Math.Min(end, length);
                }

                if (segments.Count > 0 && segments[^1].EndMs is { } lastEnd && start <= lastEnd)
                    segments[^1] = segments[^1] with { EndMs = Math.Max(lastEnd, end) };
                else
                    segments.Add(new VideoSegment(start, end));
            }

            return segments.Count > 0 ? segments : [new VideoSegment(0, null)];
        }
    }
}
