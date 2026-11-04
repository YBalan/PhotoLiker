using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <param name="Taken">When the photo was taken (see <see cref="PhotoDates"/>); null if the index has no date for it yet.</param>
    public sealed record SearchHit(string FullPath, long Length, DateTime ModifiedUtc, float Score, DateSource DateSource = DateSource.None, DateTime? Taken = null);

    /// <summary>
    /// The photos a search found, best first (a date alone: oldest first). <paramref name="Weak"/> means that nothing
    /// matched well and these are only the closest photos.
    /// </summary>
    /// <param name="Dates">The date the search was limited to, if the text had one.</param>
    /// <param name="OtherModelPhotos">Photos a text or "More like this" search could not look at because they have not been through the folder's model yet (indexing makes its vector; they may have the vectors of other models).</param>
    public sealed record SearchResult(string Description, IReadOnlyList<SearchHit> Hits, int SearchedPhotos, bool Weak = false, DateFilter? Dates = null, int OtherModelPhotos = 0)
    {
        /// <summary>Hits whose date is the date of the file because the camera's date was missing or wrong.</summary>
        public int FileDated => Hits.Count(h => h.DateSource == DateSource.File);

        /// <summary>
        /// The same hits in the order of the SortOrder setting: <see cref="PhotoSortOrder.OldestFirst"/> and <see cref="PhotoSortOrder.NewestFirst"/>
        /// by the date the photos were taken (the date the search works with, known for every indexed photo without
        /// opening it; the folder itself goes by the date the file was created because it has nothing better), photos of
        /// the same moment by their path, so the one order is exactly the other backwards. <see cref="PhotoSortOrder.ByName"/>
        /// goes by the name of the file (<see cref="PhotoNameOrder"/>), <see cref="PhotoSortOrder.Random"/> shuffles the hits (a new
        /// order each time). <see cref="PhotoSortOrder.None"/> does not sort: the search's own order stays.
        /// </summary>
        public SearchResult OrderedBy(PhotoSortOrder sortOrder)
        {
            if (sortOrder == PhotoSortOrder.None || Hits.Count < 2)
                return this;

            if (sortOrder == PhotoSortOrder.ByName)
            {
                var byName = Hits.ToList();
                byName.Sort((a, b) => PhotoNameOrder.Compare(a.FullPath, b.FullPath));
                return this with { Hits = byName };
            }

            if (sortOrder == PhotoSortOrder.Random)
            {
                var shuffled = Hits.ToList();
                Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(shuffled));
                return this with { Hits = shuffled };
            }

            // A photo that has no date yet (an index from before dates were kept) goes by the time its file was last written
            static DateTime When(SearchHit hit) => hit.Taken ?? PhotoDates.FileTime(hit.ModifiedUtc) ?? DateTime.MinValue;
            static int OldestFirst(SearchHit a, SearchHit b)
            {
                var byTaken = When(a).CompareTo(When(b));
                return byTaken != 0 ? byTaken : string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
            }

            var hits = Hits.ToList();
            hits.Sort(sortOrder == PhotoSortOrder.OldestFirst ? OldestFirst : (a, b) => OldestFirst(b, a));
            return this with { Hits = hits };
        }
    }

    /// <summary>A face of the shown photo with the name it is known by, if any.</summary>
    public sealed record FaceLabel(IndexedFace Face, string? Name, bool Confirmed, float Score);

    /// <summary>
    /// Everything about searching one photo folder: its index, its people, and the searches. The app keeps one of these
    /// for the folder that is open.
    /// </summary>
    public sealed class FolderIndex : IDisposable
    {
        /// <summary>
        /// Two copies of a photo show the same picture: their vectors are as good as the same (copies of one file give the
        /// very same vector; the margin is for rounding).
        /// </summary>
        public const float MinSameScore = 0.9999f;

        /// <summary>
        /// A text search shows photos that score at least this much (the cosine of CLIP B/32's text and photo vectors: real
        /// matches are around 0.25 to 0.35, photos that merely resemble the words 0.2 to 0.23; the other models' cosines are put on
        /// the same scale, see <see cref="ClipModelInfo.TextScore"/>), and not further below the best than the band.
        /// These are the cutoffs with the precision slider in the middle, see <see cref="SearchTuning"/>.
        /// </summary>
        public const float MinTextScore = 0.23f;
        public const float TextScoreBand = 0.06f;

        /// <summary>When nothing reaches <see cref="MinTextScore"/>, this many of the closest photos are shown anyway.</summary>
        public const int WeakResultCount = 12;

        /// <summary>"More like this" shows photos at least this alike.</summary>
        public const float MinSimilarScore = 0.65f;

        private FolderIndex(PhotoIndex index, PeopleLibrary people)
        {
            Index = index;
            People = people;
        }

        public PhotoIndex Index { get; }
        public PeopleLibrary People { get; }
        public string Folder => Index.Root;

        /// <summary>
        /// The model that matches words with photos in this folder (<see cref="Settings.SearchModel"/>): the one indexing makes vectors with and
        /// text and "More like this" searches use. They only see the photos that have a vector of this model, so after the model is changed they
        /// find nothing new until indexing has made the vectors again. Searches by date and by person do not depend on it.
        /// </summary>
        public ClipModelInfo ClipModel { get; set; } = ClipModels.Default;

        public static FolderIndex Open(string folder) => Open(folder, SearchPaths.SharedPeopleFile, SearchPaths.PeopleGroupsFile);

        /// <param name="sharedPeopleFile">Where the people of all folders are collected; null keeps the people of this folder to itself.</param>
        /// <param name="groupsFile">Where the groups of people are kept; null keeps them in memory only.</param>
        public static FolderIndex Open(string folder, string? sharedPeopleFile, string? groupsFile = null) =>
            new(PhotoIndex.Open(folder), PeopleLibrary.Load(folder, sharedPeopleFile, groupsFile));

        public Task<IndexSummary> IndexAsync(
            ModelHub models, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            IndexingJob.RunAsync(Index, models, ClipModel, excludedFolder, extensions, progress, cancellationToken);

        // ── searching ──────────────────────────────────────────────────────

        /// <summary>What a search text asks for: the people in it (none, one or several), the words that describe the picture (may be none), and the date.</summary>
        private sealed record SearchPlan(IReadOnlyList<string> Names, string Words, DateFilter? Dates);

        /// <summary>
        /// Reads a search text. The name of a person wins over everything, also one that reads like a date or a month. Otherwise a date is
        /// taken out of the text, and then the names of the known people: "Yurii playing guitar 2009" is Yurii, "playing guitar", 2009.
        /// A text in double quotes is a description, as it is.
        /// </summary>
        private SearchPlan? Plan(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return null;

            if (People.TryFindName(text, out var name))
                return new SearchPlan([name], string.Empty, null);

            var query = DateQuery.Parse(text);
            if (query.Words.Length == 0)
                return query.Dates is null ? null : new SearchPlan([], string.Empty, query.Dates);

            List<string> names = [];
            var words = DateQuery.IsQuoted(text) ? query.Words : People.TakeNames(query.Words, names);
            return new SearchPlan(names, words, query.Dates);
        }

        /// <summary>
        /// The name of a person finds the photos of that person; anything else is described in words. A name with words ("Yurii playing guitar")
        /// finds the photos of that person that fit the words, with several names ("Mom and Dad on the beach") the photos that have all of them.
        /// The name of a group ("Family") stands for any one of its people: the photos of anybody in it; "Family and Yurii" is Yurii with somebody of the family.
        /// A date in the text ("beach September 2009", "Mom 2005", "September 1") limits the search to the photos taken then; a date alone
        /// lists them, oldest first. A text in double quotes is only a description.
        /// </summary>
        /// <param name="tuning">How much the search lets through; null is the default (see <see cref="SearchTuning"/>).</param>
        public SearchResult Search(ModelHub models, string text, SearchTuning? tuning = null) =>
            Search(words => models.TextEncoder(ClipModel).Encode(words), text, tuning);

        /// <param name="encode">Makes the vector of a description with the folder's model (a test gives its own); asked only if the text has a description.</param>
        internal SearchResult Search(Func<string, float[]> encode, string text, SearchTuning? tuning = null)
        {
            if (Plan(text) is not { } plan)
                return Empty(string.Empty);

            tuning ??= SearchTuning.Default;
            return plan switch
            {
                { Names.Count: 0, Words.Length: 0 } => SearchDates(plan.Dates!),
                { Names.Count: 0 } => SearchText(encode, plan.Words, plan.Dates, tuning),
                { Names.Count: 1, Words.Length: 0 } => SearchFaces(Describe(plan.Names[0], plan.Dates), People.ExamplesOf(plan.Names[0]), tuning, plan.Dates),
                _ => SearchPeople(encode, plan, tuning),
            };
        }

        /// <summary>Whether answering a text needs the text model: a name, or nothing but a date, does not.</summary>
        public bool NeedsTextModel(string text) => Plan(text) is { Words.Length: > 0 };

        public SearchResult SearchText(ModelHub models, string text, SearchTuning? tuning = null) =>
            SearchText(words => models.TextEncoder(ClipModel).Encode(words), text, dates: null, tuning ?? SearchTuning.Default);

        private SearchResult SearchText(Func<string, float[]> encode, string text, DateFilter? dates, SearchTuning tuning)
        {
            var model = ClipModel; // the vector of the words and the vectors of the photos must be of the same model
            return RankText(Describe($"\"{text}\"", dates), encode(text), dates, tuning, model);
        }

        /// <summary>The photos whose picture fits the vector of a text, see <see cref="SearchTuning.TextCutoff"/> for how many.</summary>
        /// <param name="model">The model that made the vector of the text; null is the folder's.</param>
        /// <param name="only">Limits the search to the photos it says yes to (the photos of a person); null searches them all.</param>
        internal SearchResult RankText(string description, float[] query, DateFilter? dates, SearchTuning tuning, ClipModelInfo? model = null,
            Func<IndexedPhoto, bool>? only = null)
        {
            model ??= ClipModel;
            var indexed = WithVectorOf(model);
            var photos = OnePerPhoto(indexed.Where(p => (dates is null || dates.Matches(p.Date.Taken)) && (only is null || only(p))), model);

            var otherModel = only is null
                ? Index.PhotoCount - indexed.Count
                : Index.Snapshot().Count(p => p.HasVector && !p.Clips.ContainsKey(model.Id) && only(p));
            var scored = photos.Select(p => (Photo: p, Score: model.TextScore(VectorMath.Dot(query, p.VectorOf(model.Id)!)))).OrderByDescending(s => s.Score).ToList();
            if (scored.Count == 0)
                return new SearchResult(description, [], indexed.Count, Dates: dates, OtherModelPhotos: otherModel);

            var cutoff = tuning.TextCutoff(scored[0].Score);
            var hits = scored.TakeWhile(s => s.Score >= cutoff).Select(s => ToHit(s.Photo, s.Score)).ToList();
            if (hits.Count > 0)
                return new SearchResult(description, hits, indexed.Count, Dates: dates, OtherModelPhotos: otherModel);

            var closest = scored.Take(WeakResultCount).Select(s => ToHit(s.Photo, s.Score)).ToList();
            return new SearchResult(description, closest, indexed.Count, Weak: true, Dates: dates, OtherModelPhotos: otherModel);
        }

        /// <summary>The photos taken at a time, oldest first. No limit on the number: this is browsing, like the folder.</summary>
        private SearchResult SearchDates(DateFilter dates)
        {
            var indexed = Index.Snapshot().Where(p => p.HasVector).ToList(); // of any model: dates need no vectors
            var hits = OnePerPhoto(indexed.Where(p => dates.Matches(p.Date.Taken)), ClipModel)
                .OrderBy(p => p.Date.Taken)
                .ThenBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(p => ToHit(p, 0f))
                .ToList();
            return new SearchResult(dates.Describe(), hits, indexed.Count, Dates: dates);
        }

        private static string Describe(string what, DateFilter? dates) => dates is null ? what : $"{what}, {dates.Describe()}";

        /// <summary>Whether "More like this" works for this photo: it is in the index with a vector of the folder's model.</summary>
        public bool CanSearchSimilar(string fullPath) =>
            Index.FindFullPath(fullPath)?.VectorOf(ClipModel.Id) is not null;

        public SearchResult SearchSimilar(string fullPath, SearchTuning? tuning = null)
        {
            var model = ClipModel;
            var photo = Index.FindFullPath(fullPath);
            var title = $"like {Path.GetFileName(fullPath)}";
            if (photo?.VectorOf(model.Id) is not { } shown)
                return Empty(title);

            var minScore = (tuning ?? SearchTuning.Default).MinSimilarScore;
            var photos = WithVectorOf(model);
            var hits = OnePerPhoto(photos, model).Select(p => (Photo: p, Score: model.SimilarScore(VectorMath.Dot(shown, p.VectorOf(model.Id)!))))
                .Where(s => s.Score >= minScore)
                .OrderByDescending(s => s.Score)
                .Select(s => ToHit(s.Photo, s.Score))
                .ToList();
            return new SearchResult(title, hits, photos.Count, OtherModelPhotos: Index.PhotoCount - photos.Count);
        }

        public SearchResult SearchPerson(string name, SearchTuning? tuning = null) =>
            SearchFaces($"{name}", People.ExamplesOf(name), tuning ?? SearchTuning.Default);

        /// <summary>The photos with a face that looks like this one.</summary>
        public SearchResult SearchPerson(IndexedFace face, string? name, SearchTuning? tuning = null) =>
            SearchFaces(name ?? SearchLibStrings.ThisPerson, [face.Embedding], tuning ?? SearchTuning.Default);

        private SearchResult SearchFaces(string description, IReadOnlyList<float[]> examples, SearchTuning tuning, DateFilter? dates = null)
        {
            var photos = Index.Snapshot();
            var candidates = OnePerPhoto(photos.Where(p => dates is null || dates.Matches(p.Date.Taken)), ClipModel);

            // Every face against every example: a group has the examples of all its people, so this is the one place the cost grows with them.
            // The photos are scored on all cores, and the hits are put together in the order of the photos, so the result is always the same.
            var best = new float[candidates.Count];
            Parallel.For(0, candidates.Count, i =>
            {
                var closest = 0f;
                foreach (var face in candidates[i].Faces)
                {
                    foreach (var example in examples)
                        closest = Math.Max(closest, VectorMath.Dot(example, face.Embedding));
                }
                best[i] = closest;
            });

            var hits = new List<SearchHit>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (best[i] >= tuning.MinFaceScore)
                    hits.Add(ToHit(candidates[i], best[i]));
            }

            hits = [.. hits.OrderByDescending(h => h.Score)];
            return new SearchResult(description, hits, photos.Length, Dates: dates);
        }

        /// <summary>
        /// The photos of several people at once, or of a person that fit some words ("Yurii playing guitar"): the photos where every person has
        /// a face of their own, and then, with words, the best fitting of those the way a text search ranks (so the cutoffs are the same and the
        /// closest photos are shown when none fits well); without words the best faces first. Who is in a photo comes from the faces, which
        /// do not depend on the model; the words are matched by the folder's model, so they only see the photos that have its vector.
        /// </summary>
        private SearchResult SearchPeople(Func<string, float[]> encode, SearchPlan plan, SearchTuning tuning)
        {
            var examples = plan.Names.Select(People.ExamplesOf).ToList();
            var who = string.Join(" and ", plan.Names);

            if (plan.Words.Length > 0)
            {
                var model = ClipModel;
                return RankText(Describe($"{who}, \"{plan.Words}\"", plan.Dates), encode(plan.Words), plan.Dates, tuning, model,
                    only: photo => Together(photo, examples, tuning.MinFaceScore) is not null);
            }

            var photos = Index.Snapshot();
            var candidates = OnePerPhoto(photos.Where(p => plan.Dates is null || plan.Dates.Matches(p.Date.Taken)), ClipModel);
            var scores = new float?[candidates.Count];
            Parallel.For(0, candidates.Count, i => scores[i] = Together(candidates[i], examples, tuning.MinFaceScore)); // on all cores, like SearchFaces

            var hits = new List<SearchHit>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (scores[i] is { } score)
                    hits.Add(ToHit(candidates[i], score));
            }
            return new SearchResult(Describe(who, plan.Dates), [.. hits.OrderByDescending(h => h.Score)], photos.Length, Dates: plan.Dates);
        }

        /// <summary>
        /// How well a photo shows all of these people: every person needs a face of their own that is close enough to one of their faces
        /// (the faces are shared out so that the sum is the best), and the score is the mean of those closenesses; null if somebody is missing.
        /// </summary>
        private static float? Together(IndexedPhoto photo, IReadOnlyList<IReadOnlyList<float[]>> people, float minScore)
        {
            var faces = photo.Faces;
            if (faces.Count < people.Count)
                return null;

            // How close each face is to each person (a crowd of more than 64 faces is only looked at as far as that)
            var closeness = people
                .Select(examples => faces.Take(64).Select(face => examples.Select(e => VectorMath.Dot(e, face.Embedding)).DefaultIfEmpty(0f).Max()).ToArray())
                .ToArray();
            return Share(closeness, person: 0, taken: 0UL, minScore) / people.Count;
        }

        // The best sum of closenesses when persons from `person` on pick faces not in `taken` (a bit for each), none lower than minScore
        private static float? Share(float[][] closeness, int person, ulong taken, float minScore)
        {
            if (person == closeness.Length)
                return 0f;

            float? best = null;
            for (var face = 0; face < closeness[person].Length; face++)
            {
                if ((taken & (1UL << face)) != 0 || closeness[person][face] < minScore)
                    continue;
                if (Share(closeness, person + 1, taken | (1UL << face), minScore) is { } rest)
                    best = Math.Max(best ?? float.MinValue, closeness[person][face] + rest);
            }
            return best;
        }

        /// <summary>
        /// Every photo once. The same photo is often in the library several times (a card copied into two folders, a
        /// copy saved in a group folder), and it should be one result, not a row of the same picture. Copies are photos
        /// with the same size, the same last-write second (which is what makes the name of a copy, see <see cref="CopyName"/>)
        /// and the same picture (<see cref="MinSameScore"/>: two different photos of a burst can share size and second). Of the
        /// copies the one that is not itself a saved copy ("name [id].jpg") stays, then the first by path, so it is the same one every time.
        /// </summary>
        private static List<IndexedPhoto> OnePerPhoto(IEnumerable<IndexedPhoto> photos, ClipModelInfo model)
        {
            var kept = new List<IndexedPhoto>();
            var copiesOf = new Dictionary<(long Length, long Second), List<int>>();
            foreach (var photo in photos)
            {
                var key = (photo.Length, photo.ModifiedTicks / TimeSpan.TicksPerSecond);
                if (!copiesOf.TryGetValue(key, out var sameSize))
                    copiesOf[key] = sameSize = [];

                var twin = sameSize.FindIndex(i => IsSamePicture(kept[i], photo, model.Id));
                if (twin < 0)
                {
                    sameSize.Add(kept.Count);
                    kept.Add(photo);
                }
                else if (Prefer(photo, kept[twin]))
                {
                    kept[twin] = photo;
                }
            }
            return kept;
        }

        // Vectors of different models can not be compared: the pictures are compared by the vectors of one model that has been through both
        // (the folder's if it has), and photos that have none in common are told by size and time alone
        private static bool IsSamePicture(IndexedPhoto a, IndexedPhoto b, string preferredModelId)
        {
            var common = a.Clips.ContainsKey(preferredModelId) && b.Clips.ContainsKey(preferredModelId)
                ? preferredModelId
                : a.Clips.Keys.FirstOrDefault(b.Clips.ContainsKey);
            return common is null || VectorMath.Dot(a.Clips[common].Values, b.Clips[common].Values) >= MinSameScore;
        }

        private static bool Prefer(IndexedPhoto candidate, IndexedPhoto kept)
        {
            var candidateIsCopy = CopyName.TryGetId(Path.GetFileName(candidate.RelativePath), out _, out _);
            var keptIsCopy = CopyName.TryGetId(Path.GetFileName(kept.RelativePath), out _, out _);
            return candidateIsCopy != keptIsCopy
                ? keptIsCopy
                : string.Compare(candidate.RelativePath, kept.RelativePath, StringComparison.OrdinalIgnoreCase) < 0;
        }

        // ── faces of a photo ───────────────────────────────────────────────

        /// <summary>The faces of a photo, each with the name the user gave it or the person it looks like.</summary>
        public IReadOnlyList<FaceLabel> DescribeFaces(string fullPath)
        {
            var photo = Index.FindFullPath(fullPath);
            if (photo is null)
                return [];

            return [.. photo.Faces.Select(face =>
            {
                if (People.LabelOf(face) is { } given)
                    return new FaceLabel(face, given, Confirmed: true, 1f);
                if (People.Recognize(face.Embedding) is var (name, score))
                    return new FaceLabel(face, name, Confirmed: false, score);
                return new FaceLabel(face, null, Confirmed: false, 0f);
            })];
        }

        /// <summary>
        /// Adds a face the detector missed, marked by the user as a box (fractions of the photo). The detector gets a
        /// second try on the enlarged surroundings of the box, which usually finds it; if not, the landmarks are estimated from the box.
        /// </summary>
        public IndexedFace MarkFace(ModelHub models, string fullPath, float x, float y, float width, float height)
        {
            var photo = Index.FindFullPath(fullPath)
                ?? throw new InvalidOperationException(SearchLibStrings.PhotoNotIndexed);

            var decoded = WicPhotoDecoder.Decode(FileReading.ReadAllBytesShared(fullPath));
            float boxX = x * decoded.Width, boxY = y * decoded.Height, boxWidth = width * decoded.Width, boxHeight = height * decoded.Height;

            // Look again around the box, enlarged: a small face is found far more easily in a close-up
            var cropX = (int)Math.Max(0, boxX - boxWidth * 0.5f);
            var cropY = (int)Math.Max(0, boxY - boxHeight * 0.5f);
            var cropRight = (int)Math.Min(decoded.Width, boxX + boxWidth * 1.5f);
            var cropBottom = (int)Math.Min(decoded.Height, boxY + boxHeight * 1.5f);
            var crop = decoded.Crop(cropX, cropY, cropRight - cropX, cropBottom - cropY);

            var (tensor, scale) = FaceDetector.Prepare(crop);
            var found = models.Detector.Detect(tensor, scale, threshold: 0.3f)
                .Select(f => f with
                {
                    X = f.X + cropX,
                    Y = f.Y + cropY,
                    Landmarks = [.. f.Landmarks.Select(p => new Point2(p.X + cropX, p.Y + cropY))],
                })
                .Select(f => (Face: f, Overlap: Overlap(f, boxX, boxY, boxWidth, boxHeight)))
                .Where(f => f.Overlap >= 0.3f)
                .OrderByDescending(f => f.Overlap)
                .Select(f => f.Face)
                .FirstOrDefault();

            var landmarks = found?.Landmarks ?? FaceAligner.LandmarksFromBox(boxX, boxY, boxWidth, boxHeight);
            var embedding = models.Embedder.Embed(FaceAligner.Align(decoded, landmarks), 1)[0];

            var face = new IndexedFace(photo, x, y, width, height, found?.Score ?? 0f, manual: true, embedding);
            Index.AddFace(face);
            return face;
        }

        public void NameFace(IndexedFace face, string name) => People.Label(name, face);

        public void ClearName(IndexedFace face) => People.Unlabel(face);

        /// <summary>Removes a face that is not one (or that the user does not want marked), and its name.</summary>
        public void DeleteFace(IndexedFace face)
        {
            People.Unlabel(face);
            Index.RemoveFace(face);
        }

        // ── helpers ────────────────────────────────────────────────────────

        /// <summary>The photos that have a vector of this model, i.e. the ones a text search can look through.</summary>
        private List<IndexedPhoto> WithVectorOf(ClipModelInfo model) =>
            [.. Index.Snapshot().Where(p => p.Clips.ContainsKey(model.Id))];

        private SearchHit ToHit(IndexedPhoto photo, float score) =>
            new(Index.ToFullPath(photo.RelativePath), photo.Length, new DateTime(photo.ModifiedTicks, DateTimeKind.Utc), score, photo.Date.Source, photo.Date.Taken);

        private static SearchResult Empty(string description) => new(description, [], 0);

        private static float Overlap(DetectedFace face, float x, float y, float width, float height)
        {
            var overlapWidth = Math.Min(face.X + face.Width, x + width) - Math.Max(face.X, x);
            var overlapHeight = Math.Min(face.Y + face.Height, y + height) - Math.Max(face.Y, y);
            if (overlapWidth <= 0 || overlapHeight <= 0)
                return 0;

            var intersection = overlapWidth * overlapHeight;
            return intersection / (face.Width * face.Height + width * height - intersection);
        }

        public void Dispose() => Index.Dispose();
    }
}
