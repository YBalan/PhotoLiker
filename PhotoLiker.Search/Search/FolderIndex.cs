using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    public sealed record SearchHit(string FullPath, long Length, DateTime ModifiedUtc, float Score);

    /// <summary>
    /// The photos a search found, best first. <paramref name="Weak"/> means that nothing matched well and these are only
    /// the closest photos.
    /// </summary>
    public sealed record SearchResult(string Description, IReadOnlyList<SearchHit> Hits, int SearchedPhotos, bool Weak = false);

    /// <summary>A face of the shown photo with the name it is known by, if any.</summary>
    public sealed record FaceLabel(IndexedFace Face, string? Name, bool Confirmed, float Score);

    /// <summary>
    /// Everything about searching one photo folder: its index, its people, and the searches. The app keeps one of these
    /// for the folder that is open.
    /// </summary>
    public sealed class FolderIndex : IDisposable
    {
        public const int MaxResults = 500;

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

        public static FolderIndex Open(string folder) => new(PhotoIndex.Open(folder), PeopleLibrary.Load(folder));

        public Task<IndexSummary> IndexAsync(
            ModelHub models, string? excludedFolder, IReadOnlyCollection<string> extensions,
            IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            IndexingJob.RunAsync(Index, models, excludedFolder, extensions, progress, cancellationToken);

        // ── searching ──────────────────────────────────────────────────────

        /// <summary>The name of a person finds the photos of that person; anything else is described in words.</summary>
        public SearchResult Search(ModelHub models, string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return Empty(string.Empty);

            if (People.TryFindName(text, out var name))
                return SearchPerson(name);

            return SearchText(models, text);
        }

        public SearchResult SearchText(ModelHub models, string text)
        {
            var query = models.TextEncoder.Encode(text);
            var photos = Index.Snapshot().Where(p => p.Clip is not null).ToList();

            var scored = photos.Select(p => (Photo: p, Score: VectorMath.Dot(query, p.Clip!))).OrderByDescending(s => s.Score).ToList();
            if (scored.Count == 0)
                return Empty($"\"{text}\"");

            var cutoff = Math.Max(MinTextScore, scored[0].Score - TextScoreBand);
            var hits = scored.TakeWhile(s => s.Score >= cutoff).Take(MaxResults).Select(s => ToHit(s.Photo, s.Score)).ToList();
            if (hits.Count > 0)
                return new SearchResult($"\"{text}\"", hits, photos.Count);

            var closest = scored.Take(WeakResultCount).Select(s => ToHit(s.Photo, s.Score)).ToList();
            return new SearchResult($"\"{text}\"", closest, photos.Count, Weak: true);
        }

        public SearchResult SearchSimilar(string fullPath)
        {
            var photo = Index.FindFullPath(fullPath);
            var title = $"like {Path.GetFileName(fullPath)}";
            if (photo?.Clip is null)
                return Empty(title);

            var photos = Index.Snapshot().Where(p => p.Clip is not null).ToList();
            var hits = photos.Select(p => (Photo: p, Score: VectorMath.Dot(photo.Clip, p.Clip!)))
                .Where(s => s.Score >= MinSimilarScore)
                .OrderByDescending(s => s.Score)
                .Take(MaxResults)
                .Select(s => ToHit(s.Photo, s.Score))
                .ToList();
            return new SearchResult(title, hits, photos.Count);
        }

        public SearchResult SearchPerson(string name) =>
            SearchFaces($"{name}", People.ExamplesOf(name));

        /// <summary>The photos with a face that looks like this one.</summary>
        public SearchResult SearchPerson(IndexedFace face, string? name) =>
            SearchFaces(name ?? "this person", [face.Embedding]);

        private SearchResult SearchFaces(string description, IReadOnlyList<float[]> examples)
        {
            var photos = Index.Snapshot();
            var hits = new List<SearchHit>();
            foreach (var photo in photos)
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

            hits = [.. hits.OrderByDescending(h => h.Score).Take(MaxResults)];
            return new SearchResult(description, hits, photos.Length);
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
            new(Index.ToFullPath(photo.RelativePath), photo.Length, new DateTime(photo.ModifiedTicks, DateTimeKind.Utc), score);

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
