namespace PhotoLiker.Search
{
    /// <summary>What the index knows about one photo. A photo that changes on disk is indexed again as a new object.</summary>
    public sealed class IndexedPhoto(string relativePath, long length, long modifiedTicks)
    {
        public long Id { get; internal set; }

        /// <summary>Path below the indexed folder, with '/' as the separator.</summary>
        public string RelativePath { get; } = relativePath;

        /// <summary>Size and timestamp of the file when it was indexed; a different one means the photo has to be indexed again.</summary>
        public long Length { get; } = length;
        public long ModifiedTicks { get; } = modifiedTicks;

        /// <summary>The CLIP vector for text search; null if the photo could not be read.</summary>
        public float[]? Clip { get; init; }

        /// <summary>Which version of the models produced the vector and the faces (0: not done).</summary>
        public int ClipVersion { get; init; }
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
        public float X { get; } = x;
        public float Y { get; } = y;
        public float Width { get; } = width;
        public float Height { get; } = height;
        public float Score { get; } = score;

        /// <summary>Marked by hand, not found by the detector.</summary>
        public bool Manual { get; } = manual;

        public float[] Embedding { get; } = embedding;
    }
}
