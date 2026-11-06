using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>
    /// Searching what the videos of a folder show. A video is a list of moments (see <see cref="VideoCatalog"/>), and a search looks at the moments with the same
    /// vectors and the same model as the photos, so what is typed in the one search box finds both. The answer is the videos (each once), best first, each with
    /// the place in it that fits best, where playing starts, and every stretch of it that fits (<see cref="SearchHit.Moments"/>); <see cref="Merge"/> puts
    /// them in one list with the photos. People are found by their faces in the moments, with the same examples and the same closeness as in photos.
    /// </summary>
    public static class VideoSearch
    {
        /// <summary>Moments this close together (milliseconds) are one stretch of the video: only the best of them is a place to go to.</summary>
        public const long StretchGapMs = 6_000;

        /// <summary>
        /// The videos that fit the plan: the words (what the pictures show), the people (who is in them), the date (when it was recorded). A date alone lists the videos of
        /// that time, oldest first, whether or not they have moments for this model.
        /// </summary>
        /// <param name="encode">Makes the vector of the words with the model; asked only if there are words.</param>
        /// <param name="examplesOf">The face vectors that stand for a person (or a group of people) by name; without it a plan that names people finds no video.</param>
        /// <param name="speechModelId">The speech model whose transcripts are searched for the words too (what is said in the videos); null looks at the pictures only.</param>
        public static SearchResult Search(
            VideoCatalog catalog, ClipModelInfo model, FolderIndex.SearchPlan plan, Func<string, float[]> encode, SearchTuning tuning,
            Func<string, IReadOnlyList<float[]>>? examplesOf = null, string? speechModelId = null)
        {
            var pictures = SearchMoments(catalog, model, plan, encode, tuning, examplesOf);

            // The words are looked for in what is said as well: a word that is said finds the video whatever the pictures show
            if (speechModelId is null || plan.DateOnly || plan.Names.Count > 0 || plan.Words.Length == 0)
                return pictures;

            var spoken = catalog.SpeechSets(speechModelId).Where(s => plan.Dates is null || plan.Dates.Matches(s.Video.Date.Taken)).ToList();
            var heard = SpeechSearch.Find(spoken, plan.Words, tuning);
            return heard.Count == 0 ? pictures : WithSpeech(pictures, heard, spoken.Count);
        }

        // The hits of the pictures and the hits of the speech, one hit for each video: a video that is found both ways has the better score, starts where the speech is, and has every place of both
        private static SearchResult WithSpeech(SearchResult pictures, IReadOnlyList<SpeechMatch> heard, int speechVideos)
        {
            var hits = pictures.Weak ? [] : pictures.Hits.ToList();
            foreach (var match in heard)
            {
                var score = SpeechSearch.ScoreBase + SpeechSearch.ScoreSpan * match.Fraction;
                var existing = hits.FindIndex(h => string.Equals(h.FullPath, match.Set.FullPath, StringComparison.OrdinalIgnoreCase));
                if (existing < 0)
                {
                    hits.Add(HitOf(match.Set.FullPath, match.Set.Video, score, match.BestMs, match.Places));
                    continue;
                }

                var seen = hits[existing];
                var places = (seen.Moments ?? []).Concat(match.Places).Order().ToList();
                hits[existing] = seen with { Score = Math.Max(seen.Score, score), TimeMs = match.BestMs, Moments = places };
            }

            return pictures with
            {
                Hits = [.. hits.OrderByDescending(h => h.Score)],
                Weak = false,
                SearchedVideos = Math.Max(pictures.SearchedVideos, speechVideos),
            };
        }

        private static SearchResult SearchMoments(
            VideoCatalog catalog, ClipModelInfo model, FolderIndex.SearchPlan plan, Func<string, float[]> encode, SearchTuning tuning,
            Func<string, IReadOnlyList<float[]>>? examplesOf)
        {
            if (plan.DateOnly)
            {
                var dated = catalog.AnalyzedRecords().Where(r => plan.Dates!.Matches(r.Date.Taken)).OrderBy(r => r.Date.Taken).ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
                return new SearchResult(string.Empty, [.. dated.Select(r => HitOf(catalog.FullPathOf(r), r, 0f, 0, [0]))], 0, Dates: plan.Dates, SearchedVideos: dated.Count);
            }

            var sets = catalog.MomentSets(model.Id).Where(s => plan.Dates is null || plan.Dates.Matches(s.Video.Date.Taken)).ToList();
            if (sets.Count == 0)
                return new SearchResult(string.Empty, [], 0, Dates: plan.Dates, SearchedVideos: 0);

            // The people: only the moments where every one of them is. Nobody to compare with (an unknown name) finds nothing.
            IReadOnlyList<IReadOnlyList<float[]>>? people = null;
            if (plan.Names.Count > 0)
            {
                people = [.. plan.Names.Select(name => examplesOf?.Invoke(name) ?? [])];
                if (people.Any(examples => examples.Count == 0))
                    return new SearchResult(string.Empty, [], 0, Dates: plan.Dates, SearchedVideos: sets.Count);
            }

            var query = plan.Words.Length > 0 ? encode(plan.Words) : null;
            var scored = sets.Select(set => (Set: set, Scores: ScoresOf(set, model, query, people, tuning))).Where(s => s.Scores.Any(score => score is not null)).ToList();
            if (scored.Count == 0)
                return new SearchResult(string.Empty, [], 0, Dates: plan.Dates, SearchedVideos: sets.Count);

            // The words decide how many fit (as they do for photos, only a little stricter); who is in the picture is a yes or no, and the closer face is the better
            var best = scored.Max(s => s.Scores.Max()!.Value);
            var cutoff = query is null ? tuning.MinFaceScore : tuning.VideoTextCutoff(best);
            var ranked = scored
                .Select(s => (s.Set, s.Scores, Best: BestOf(s.Scores)))
                .OrderByDescending(s => s.Scores[s.Best]!.Value)
                .ToList();

            var strong = ranked.TakeWhile(s => s.Scores[s.Best]!.Value >= cutoff).ToList();
            if (strong.Count > 0)
            {
                return new SearchResult(string.Empty,
                    [.. strong.Select(s => HitOf(s.Set.FullPath, s.Set.Video, s.Scores[s.Best]!.Value, s.Set.TimesMs[s.Best], Stretches(s.Set.TimesMs, s.Scores, cutoff)))],
                    0, Dates: plan.Dates, SearchedVideos: sets.Count);
            }

            if (query is null)
                return new SearchResult(string.Empty, [], 0, Dates: plan.Dates, SearchedVideos: sets.Count);

            // Nothing fits well: the closest few are shown, as they are for photos, and said to be only that
            var closest = ranked.Take(FolderIndex.WeakResultCount).Select(s => HitOf(s.Set.FullPath, s.Set.Video, s.Scores[s.Best]!.Value, s.Set.TimesMs[s.Best], [s.Set.TimesMs[s.Best]])).ToList();
            return new SearchResult(string.Empty, closest, 0, Weak: true, Dates: plan.Dates, SearchedVideos: sets.Count);
        }

        // How well each moment fits; null for a moment that is out (somebody asked for is not in it)
        private static float?[] ScoresOf(MomentSet set, ClipModelInfo model, float[]? query, IReadOnlyList<IReadOnlyList<float[]>>? people, SearchTuning tuning)
        {
            var scores = new float?[set.Vectors.Length];
            for (var i = 0; i < scores.Length; i++)
            {
                float? together = null;
                if (people is not null)
                {
                    together = FolderIndex.Together([.. set.Faces[i].Select(face => face.Embedding)], people, tuning.MinFaceScore);
                    if (together is null)
                        continue;
                }

                scores[i] = query is not null ? model.TextScore(VectorMath.Dot(query, set.Vectors[i])) : together;
            }
            return scores;
        }

        private static int BestOf(float?[] scores)
        {
            var best = -1;
            for (var i = 0; i < scores.Length; i++)
            {
                if (scores[i] is { } score && (best < 0 || score > scores[best]!.Value))
                    best = i;
            }
            return best;
        }

        /// <summary>
        /// The places in a video that fit: the moments that score at least the cutoff, with the moments that fit and follow one another closely counted as one
        /// stretch (the best of the stretch stands for it). In order of time.
        /// </summary>
        internal static IReadOnlyList<long> Stretches(long[] times, float[] scores, float cutoff) =>
            Stretches(times, [.. scores.Select(s => (float?)s)], cutoff);

        internal static IReadOnlyList<long> Stretches(long[] times, float?[] scores, float cutoff)
        {
            var stretches = new List<(long Time, float Score)>();
            var latest = long.MinValue; // the last moment that fits: how far the next one is from it tells whether it is the same stretch
            for (var i = 0; i < times.Length; i++)
            {
                if (scores[i] is not { } score || score < cutoff)
                    continue;

                if (stretches.Count > 0 && times[i] - latest <= StretchGapMs)
                {
                    if (score > stretches[^1].Score)
                        stretches[^1] = (times[i], score);
                }
                else
                {
                    stretches.Add((times[i], score));
                }
                latest = times[i];
            }
            return [.. stretches.Select(s => s.Time)];
        }

        private static SearchHit HitOf(string fullPath, VideoRecord video, float score, long timeMs, IReadOnlyList<long> moments) =>
            new(fullPath, video.Length, new DateTime(video.ModifiedTicks, DateTimeKind.Utc), score, video.Date.Source, video.Date.Taken, timeMs, moments);

        /// <summary>
        /// The photos and the videos that fit one search, as one list: by how well they fit (the same scale), or oldest first for a date alone. If there are hits
        /// that fit well, only they are shown; if neither photos nor videos fit well, the closest of both together are shown and said to be only that.
        /// </summary>
        public static SearchResult Merge(SearchResult photos, SearchResult videos, FolderIndex.SearchPlan plan)
        {
            var strongPhotos = photos.Weak ? [] : photos.Hits;
            var strongVideos = videos.Weak ? [] : videos.Hits;

            IReadOnlyList<SearchHit> hits;
            var weak = false;
            if (strongPhotos.Count + strongVideos.Count > 0)
            {
                hits = Order([.. strongPhotos, .. strongVideos], plan);
            }
            else
            {
                hits = [.. photos.Hits.Concat(videos.Hits).OrderByDescending(h => h.Score).Take(FolderIndex.WeakResultCount)];
                weak = hits.Count > 0 && (photos.Weak || videos.Weak);
            }

            return photos with
            {
                Hits = hits,
                Weak = weak,
                SearchedVideos = videos.SearchedVideos,
            };
        }

        /// <summary>
        /// The result of a photo search with its photos taken out, for a search that looks through the videos only (to be given to <see cref="Merge"/>): what was searched
        /// for stays, but no photo is a hit and none counts as looked through, so nothing is said about photos that were left out of the search.
        /// </summary>
        public static SearchResult WithoutPhotos(SearchResult photos) => photos with { Hits = [], Weak = false, SearchedPhotos = 0, OtherModelPhotos = 0 };

        private static IReadOnlyList<SearchHit> Order(IEnumerable<SearchHit> hits, FolderIndex.SearchPlan plan) =>
            plan.DateOnly
                ? [.. hits.OrderBy(h => h.Taken).ThenBy(h => h.FullPath, StringComparer.OrdinalIgnoreCase)]
                : [.. hits.OrderByDescending(h => h.Score)];
    }
}
