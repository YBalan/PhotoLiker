using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>A video in which something that was searched for is said: how many of the words (a fraction), where the best of it is, every place in it, and what is said there.</summary>
    public sealed record SpeechMatch(SpeechSet Set, float Fraction, long BestMs, IReadOnlyList<long> Places, string Snippet);

    /// <summary>
    /// Searching what is said in videos: the words typed are looked for among the words of the speech, in whatever form they were said (see <see cref="SpeechText.Matches"/>),
    /// in one segment or in two that follow each other (a sentence is cut where the model cuts it). The speech is the model's guess, so it is searched loosely: with the
    /// slider at the wide end half of the words are enough, at the precise end all of them.
    /// </summary>
    public static class SpeechSearch
    {
        /// <summary>A word that was said is a sure thing, more than a picture that looks like the words: a match scores from here...</summary>
        public const float ScoreBase = 0.5f;

        /// <summary>...up to this much higher when all the words are there.</summary>
        public const float ScoreSpan = 0.1f;

        /// <summary>The fraction of the words that has to be found, from the slider: half at the wide end, all of them at the precise end.</summary>
        public static float RequiredFraction(SearchTuning tuning) =>
            0.5f + 0.5f * Math.Clamp((tuning.MinTextScore - 0.18f) / (0.30f - 0.18f), 0f, 1f);

        /// <summary>
        /// How many of the searched words have to be found: with the slider in the middle one word needs the one, two need both, three need two, four need three; at the precise
        /// end all of them, at the wide end about half.
        /// </summary>
        public static int RequiredWords(int searched, SearchTuning tuning) =>
            Math.Clamp((int)Math.Ceiling(RequiredFraction(tuning) * searched - 0.35), 1, Math.Max(1, searched));

        /// <summary>The words of a search that are worth looking for: not the one- and two-letter words (and, in, to), unless nothing else is left.</summary>
        internal static IReadOnlyList<string> SearchWords(string words)
        {
            var all = SpeechText.Words(words);
            var long_ = all.Where(w => w.Length > 2).Distinct().ToList();
            return long_.Count > 0 ? long_ : [.. all.Distinct()];
        }

        /// <summary>The videos in which the words are said, the ones that match most first.</summary>
        public static IReadOnlyList<SpeechMatch> Find(IReadOnlyList<SpeechSet> sets, string words, SearchTuning tuning)
        {
            var searched = SearchWords(words);
            if (searched.Count == 0)
                return [];

            var required = RequiredWords(searched.Count, tuning);
            var matches = new List<SpeechMatch>();
            foreach (var set in sets)
            {
                var spoken = set.Segments.Select(s => SpeechText.Words(s.Text)).ToList();
                var places = new List<(long Time, float Fraction, string Text)>();
                for (var i = 0; i < set.Segments.Count; i++)
                {
                    // the segment alone; or with the one that follows it when that is what it takes (a sentence is cut where the model cuts it), and
                    // the one that follows does not do it by itself (then it is the place, not this one)
                    var alone = Found(searched, spoken[i]);
                    var found = alone;
                    if (alone < required && i + 1 < spoken.Count && Found(searched, spoken[i + 1]) < required)
                        found = Found(searched, [.. spoken[i], .. spoken[i + 1]]);
                    if (found >= required)
                        places.Add((set.Segments[i].StartMs, found / (float)searched.Count, set.Segments[i].Text));
                }

                if (places.Count == 0)
                    continue;

                var best = places.OrderByDescending(p => p.Fraction).ThenBy(p => p.Time).First();
                matches.Add(new SpeechMatch(set, best.Fraction, best.Time, Stretches(places), best.Text));
            }
            return [.. matches.OrderByDescending(m => m.Fraction).ThenBy(m => m.Set.Video.RelativePath, StringComparer.OrdinalIgnoreCase)];
        }

        // How many of the searched words are among the spoken ones
        private static int Found(IReadOnlyList<string> searched, IReadOnlyList<string> spoken) =>
            searched.Count(word => spoken.Any(said => SpeechText.Matches(word, said)));

        // Places that follow each other closely are one stretch, shown by the first (what is said starts there)
        private static IReadOnlyList<long> Stretches(List<(long Time, float Fraction, string Text)> places)
        {
            var stretches = new List<long>();
            var latest = long.MinValue;
            foreach (var place in places.OrderBy(p => p.Time))
            {
                if (stretches.Count == 0 || place.Time - latest > VideoSearch.StretchGapMs)
                    stretches.Add(place.Time);
                latest = place.Time;
            }
            return stretches;
        }
    }
}
