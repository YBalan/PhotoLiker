namespace PhotoLiker.Search
{
    /// <summary>The vector one model made of a photo, and the version of the way photos are prepared that it was made with (<see cref="IndexingJob.ClipVersion"/>).</summary>
    public sealed record ClipVector(float[] Values, int Version);

    /// <summary>What the index knows about one photo. A photo that changes on disk is indexed again as a new object.</summary>
    public sealed class IndexedPhoto(string relativePath, long length, long modifiedTicks)
    {
        public long Id { get; internal set; }

        /// <summary>Path below the indexed folder, with '/' as the separator.</summary>
        public string RelativePath { get; } = relativePath;

        /// <summary>
        /// Size and timestamp of the file when it was indexed; a different one means the photo has to be indexed again. The timestamp is
        /// replaced by <see cref="PhotoIndex.UpdateStamps"/> when a program changes the dates of the file but not the picture, the size
        /// by <see cref="PhotoIndex.Turn"/> when the photo is turned.
        /// </summary>
        public long Length { get; internal set; } = length;
        public long ModifiedTicks { get; internal set; } = modifiedTicks;

        internal static readonly IReadOnlyDictionary<string, ClipVector> NoClips = new Dictionary<string, ClipVector>();

        /// <summary>
        /// The vectors for text search, one for each model that has been through the photo (by <see cref="ClipModelInfo.Id"/>); none if the photo
        /// could not be read. Vectors of different models can not be compared, which is why each is kept with its model: choosing another
        /// model only makes the vectors it lacks, and the ones of the model that was used before stay, so going back costs nothing.
        /// An index made before there was a choice of models holds the vectors of the first one, the default.
        /// </summary>
        public IReadOnlyDictionary<string, ClipVector> Clips { get; init; } = NoClips;

        /// <summary>The vector of this model, if it has been through the photo.</summary>
        public float[]? VectorOf(string clipModelId) => Clips.TryGetValue(clipModelId, out var clip) ? clip.Values : null;

        /// <summary>Whether any model has made a vector of the photo (what makes it a photo that is "in the index").</summary>
        public bool HasVector => Clips.Count > 0;

        /// <summary>Which version of the face models produced the faces (0: not done).</summary>
        public int FaceVersion { get; init; }

        /// <summary>Why the photo could not be indexed, if so. It is not tried again until the file changes.</summary>
        public string? Failure { get; init; }

        public IReadOnlyList<IndexedFace> Faces { get; internal set; } = [];

        /// <summary>When the photo was taken. Replaced as a whole, so a search running at the same time sees the old or the new one.</summary>
        public PhotoDate Date { get; internal set; } = PhotoDate.NotRead;
    }

    /// <summary>A face in a photo. The box is given as fractions of the (upright) photo, so it fits at any size.</summary>
    public sealed class IndexedFace(IndexedPhoto photo, float x, float y, float width, float height, float score, bool manual, float[] embedding)
    {
        public long Id { get; internal set; }
        public IndexedPhoto Photo { get; } = photo;
        public float X { get; internal set; } = x;
        public float Y { get; internal set; } = y;
        public float Width { get; internal set; } = width;
        public float Height { get; internal set; } = height;
        public float Score { get; } = score;

        /// <summary>Marked by hand, not found by the detector.</summary>
        public bool Manual { get; } = manual;

        public float[] Embedding { get; } = embedding;
    }
}
