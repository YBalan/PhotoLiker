namespace PhotoLikerUI
{
    /// <summary>
    /// A fully decoded photo with its thumbnail and metadata. It is built on a background thread while
    /// nothing else can see it; once handed to <see cref="PhotoCache"/> it is only touched on the UI thread,
    /// so GDI+ never sees the same <see cref="Image"/> from two threads.
    /// </summary>
    internal sealed class PhotoEntry : IDisposable
    {
        // Only set for animated images, which keep decoding lazily from this in-memory stream.
        private readonly Stream? _backingStream;

        public PhotoEntry(Image full, Bitmap thumb, int thumbSize, IReadOnlyList<MetadataEntry> metadata, Stream? backingStream)
        {
            Full = full;
            Thumb = thumb;
            ThumbSize = thumbSize;
            Metadata = metadata;
            Bytes = (long)full.Width * full.Height * 4;
            _backingStream = backingStream;
        }

        public Image Full { get; }
        public Bitmap? Thumb { get; private set; }
        public int ThumbSize { get; private set; }
        public IReadOnlyList<MetadataEntry> Metadata { get; }

        /// <summary>Approximate decoded size of <see cref="Full"/>, used for the cache memory budget.</summary>
        public long Bytes { get; }

        public void SetThumb(Bitmap thumb, int size)
        {
            Thumb?.Dispose();
            Thumb = thumb;
            ThumbSize = size;
        }

        public void DropThumb()
        {
            Thumb?.Dispose();
            Thumb = null;
            ThumbSize = 0;
        }

        public void Dispose()
        {
            Full.Dispose();
            DropThumb();
            _backingStream?.Dispose();
        }
    }
}
