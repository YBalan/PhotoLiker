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
    public sealed record SearchResult(string Description, IReadOnlyList<SearchHit> Hits, int SearchedPhotos, bool Weak = false, DateFilter? Dates = null)
    {
        /// <summary>Hits whose date is the date of the file because the camera's date was missing or wrong.</summary>
        public int FileDated => Hits.Count(h => h.DateSource == DateSource.File);

        /// <summary>
        /// The same hits in the order of the SortOrder setting: <see cref="PhotoSortOrder.OldestFirst"/> and <see cref="PhotoSortOrder.NewestFirst"/>
        /// by the date the photos were taken (the date the search works with, known for every indexed photo without
        /// opening it; the folder itself goes by the date the file was created because it has nothing better), photos of
        /// the same moment by their path, so the one order is exactly the other backwards. <see cref="PhotoSortOrder.None"/>
        /// does not sort: the search's own order stays.
        /// </summary>
        public SearchResult OrderedBy(PhotoSortOrder sortOrder)
        {
            if (sortOrder == PhotoSortOrder.None || Hits.Count < 2)
                return this;

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
        /// A text search shows photos that score at least this much (the cosine of CLIP's text and photo vectors: real
        /// matches are around 0.25 to 0.35, photos that merely resemble the words 0.2 to 0.23), and not further below the best than the band.
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

        public static FolderIndex Open(string folder) => Open(folder, SearchPaths.SharedPeopleFile);

        /// <param name="sharedPeopleFile">Where the people of all folders are collected; null keeps the people of this folder to itself.</param>
        public static FolderIndex Open(string folder, string? sharedPeopleFile) =>
            new(PhotoIndex.Open(folder), PeopleLibrary.Load(folder, sharedPeopleFile));

        public Task<IndexSummary> IndexAsync(
            ModelHub models, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            IndexingJob.RunAsync(Index, models, excludedFolder, extensions, progress, cancellationToken);

        // ── searching ──────────────────────────────────────────────────────

        /// <summary>
        /// The name of a person finds the photos of that person; anything else is described in words. A date in the text
        /// ("beach September 2009", "Mom 2005", "September 1") limits the search to the photos taken then; a date alone lists them, oldest first.
        /// </summary>
        public SearchResult Search(ModelHub models, string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return Empty(string.Empty);

            // A name wins over everything, also one that reads like a date or a month
            if (People.TryFindName(text, out var name))
                return SearchPerson(name);

            var query = DateQuery.Parse(text);
            if (query.Dates is not { } dates)
                return SearchText(models, text);

            if (query.Words.Length == 0)
                return SearchDates(dates);
            if (People.TryFindName(query.Words, out name))
                return SearchFaces(Describe(name, dates), People.ExamplesOf(name), dates);
            return SearchText(models, query.Words, dates);
        }

        /// <summary>Whether answering a text needs the text model: a name, or nothing but a date, does not.</summary>
        public bool NeedsTextModel(string text)
        {
            text = text.Trim();
            if (text.Length == 0 || People.TryFindName(text, out _))
                return false;

            var words = DateQuery.Parse(text).Words;
            return words.Length > 0 && !People.TryFindName(words, out _);
        }

        public SearchResult SearchText(ModelHub models, string text) => SearchText(models, text, dates: null);

        private SearchResult SearchText(ModelHub models, string text, DateFilter? dates)
        {
            var description = Describe($"\"{text}\"", dates);
            var query = models.TextEncoder.Encode(text);
            var indexed = Index.Snapshot().Where(p => p.Clip is not null).ToList();
            var photos = OnePerPhoto(indexed.Where(p => dates is null || dates.Matches(p.Date.Taken)));

            var scored = photos.Select(p => (Photo: p, Score: VectorMath.Dot(query, p.Clip!))).OrderByDescending(s => s.Score).ToList();
            if (scored.Count == 0)
                return new SearchResult(description, [], indexed.Count, Dates: dates);

            var cutoff = Math.Max(MinTextScore, scored[0].Score - TextScoreBand);
            var hits = scored.TakeWhile(s => s.Score >= cutoff).Select(s => ToHit(s.Photo, s.Score)).ToList();
            if (hits.Count > 0)
                return new SearchResult(description, hits, indexed.Count, Dates: dates);

            var closest = scored.Take(WeakResultCount).Select(s => ToHit(s.Photo, s.Score)).ToList();
            return new SearchResult(description, closest, indexed.Count, Weak: true, Dates: dates);
        }

        /// <summary>The photos taken at a time, oldest first. No limit on the number: this is browsing, like the folder.</summary>
        private SearchResult SearchDates(DateFilter dates)
        {
            var indexed = Index.Snapshot().Where(p => p.Clip is not null).ToList();
            var hits = OnePerPhoto(indexed.Where(p => dates.Matches(p.Date.Taken)))
                .OrderBy(p => p.Date.Taken)
                .ThenBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(p => ToHit(p, 0f))
                .ToList();
            return new SearchResult(dates.Describe(), hits, indexed.Count, Dates: dates);
        }

        private static string Describe(string what, DateFilter? dates) => dates is null ? what : $"{what}, {dates.Describe()}";

        public SearchResult SearchSimilar(string fullPath)
        {
            var photo = Index.FindFullPath(fullPath);
            var title = $"like {Path.GetFileName(fullPath)}";
            if (photo?.Clip is null)
                return Empty(title);

            var photos = Index.Snapshot().Where(p => p.Clip is not null).ToList();
            var hits = OnePerPhoto(photos).Select(p => (Photo: p, Score: VectorMath.Dot(photo.Clip, p.Clip!)))
                .Where(s => s.Score >= MinSimilarScore)
                .OrderByDescending(s => s.Score)
                .Select(s => ToHit(s.Photo, s.Score))
                .ToList();
            return new SearchResult(title, hits, photos.Count);
        }

        public SearchResult SearchPerson(string name) =>
            SearchFaces($"{name}", People.ExamplesOf(name));

        /// <summary>The photos with a face that looks like this one.</summary>
        public SearchResult SearchPerson(IndexedFace face, string? name) =>
            SearchFaces(name ?? "this person", [face.Embedding]);

        private SearchResult SearchFaces(string description, IReadOnlyList<float[]> examples, DateFilter? dates = null)
        {
            var photos = Index.Snapshot();
            var hits = new List<SearchHit>();
            foreach (var photo in OnePerPhoto(photos.Where(p => dates is null || dates.Matches(p.Date.Taken))))
            {
                var best = 0f;
                foreach (var face in photo.Faces)
                {
                    foreach (var example in examples)
                        best = Math.Max(best, VectorMath.Dot(example, face.Embedding));
                }
                if (best >= PeopleLibrary.DefaultMatchThreshold)
                    hits.Add(ToHit(photo, best));
            }

            hits = [.. hits.OrderByDescending(h => h.Score)];
            return new SearchResult(description, hits, photos.Length, Dates: dates);
        }

        /// <summary>
        /// Every photo once. The same photo is often in the library several times (a card copied into two folders, a
        /// copy saved in a group folder), and it should be one result, not a row of the same picture. Copies are photos
        /// with the same size, the same last-write second (which is what makes the name of a copy, see <see cref="CopyName"/>)
        /// and the same picture (<see cref="MinSameScore"/>: two different photos of a burst can share size and second). Of the
        /// copies the one that is not itself a saved copy ("name [id].jpg") stays, then the first by path, so it is the same one every time.
        /// </summary>
        private static List<IndexedPhoto> OnePerPhoto(IEnumerable<IndexedPhoto> photos)
        {
            var kept = new List<IndexedPhoto>();
            var copiesOf = new Dictionary<(long Length, long Second), List<int>>();
            foreach (var photo in photos)
            {
                var key = (photo.Length, photo.ModifiedTicks / TimeSpan.TicksPerSecond);
                if (!copiesOf.TryGetValue(key, out var sameSize))
                    copiesOf[key] = sameSize = [];

                var twin = sameSize.FindIndex(i => IsSamePicture(kept[i], photo));
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

        private static bool IsSamePicture(IndexedPhoto a, IndexedPhoto b) =>
            a.Clip is null || b.Clip is null || VectorMath.Dot(a.Clip, b.Clip) >= MinSameScore;

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
                ?? throw new InvalidOperationException("This photo is not in the index yet, index the folder first.");

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
