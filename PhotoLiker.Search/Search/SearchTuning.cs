using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>
    /// How much a search lets through, from the one slider the user has (<see cref="Settings.SearchPrecision"/>, 0 to 100).
    /// Every kind of search has a cosine cutoff of its own; the slider moves all of them together, wide to precise.
    /// At <see cref="Settings.DefaultSearchPrecision"/> the cutoffs are the ones the search had before the slider existed
    /// (<see cref="FolderIndex.MinTextScore"/> and the rest), so nothing changes until the slider is moved.
    /// </summary>
    /// <param name="MinTextScore">A text search shows photos that score at least this much (cosine of CLIP's text and photo vectors)...</param>
    /// <param name="TextScoreBand">...and not further below the best photo than this.</param>
    /// <param name="MinSimilarScore">"More like this" shows photos at least this alike.</param>
    /// <param name="MinFaceScore">A person's photos are those with a face at least this close to one of the person's faces.</param>
    public sealed record SearchTuning(float MinTextScore, float TextScoreBand, float MinSimilarScore, float MinFaceScore)
    {
        // The cutoffs at the wide end, in the middle (the default) and at the precise end. The ends are not measured on a
        // real library yet: they are a step from what was measured at the middle (real text matches score 0.25 to 0.35,
        // photos that only resemble the words 0.20 to 0.23).
        private static readonly SearchTuning Wide = new(0.18f, 0.12f, 0.50f, 0.30f);
        private static readonly SearchTuning Precise = new(0.30f, 0.025f, 0.85f, 0.55f);

        public static SearchTuning Default { get; } = new(
            FolderIndex.MinTextScore, FolderIndex.TextScoreBand, FolderIndex.MinSimilarScore, PeopleLibrary.DefaultMatchThreshold);

        /// <summary>The cutoffs for a slider position; a position outside 0 to 100 (a hand-edited file) counts as the nearest end.</summary>
        public static SearchTuning For(int precision)
        {
            precision = Math.Clamp(precision, 0, Settings.MaxSearchPrecision);
            if (precision == Settings.DefaultSearchPrecision)
                return Default;

            var (loose, strict, fraction) = precision < Settings.DefaultSearchPrecision
                ? (Wide, Default, (float)precision / Settings.DefaultSearchPrecision)
                : (Default, Precise, (float)(precision - Settings.DefaultSearchPrecision) / (Settings.MaxSearchPrecision - Settings.DefaultSearchPrecision));

            return new SearchTuning(
                Lerp(loose.MinTextScore, strict.MinTextScore, fraction),
                Lerp(loose.TextScoreBand, strict.TextScoreBand, fraction),
                Lerp(loose.MinSimilarScore, strict.MinSimilarScore, fraction),
                Lerp(loose.MinFaceScore, strict.MinFaceScore, fraction));
        }

        /// <summary>The lowest score a text search shows when the best photo scored <paramref name="best"/>.</summary>
        public float TextCutoff(float best) => Math.Max(MinTextScore, best - TextScoreBand);

        /// <summary>
        /// A picture taken out of a video is worse than a photo (motion blur, compression, a camera in a hand), and words match it less well and more by
        /// chance: on four clips with no cat in them "a cat" scored 0.24 to 0.25 where a photo of a cat scores 0.25 to 0.35, so a frame has to score a little
        /// more than a photo to count. The same step at every position of the slider. Measured on a few clips, not on a library.
        /// </summary>
        public const float VideoMargin = 0.02f;

        /// <summary>The lowest score a text search shows for the moments of videos when the best moment scored <paramref name="best"/>.</summary>
        public float VideoTextCutoff(float best) => Math.Max(MinTextScore + VideoMargin, best - TextScoreBand);

        private static float Lerp(float from, float to, float fraction) => from + (to - from) * fraction;
    }
}
