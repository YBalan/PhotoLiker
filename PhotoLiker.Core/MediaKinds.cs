namespace PhotoLiker.Core
{
    /// <summary>
    /// Tells a video, or a 3D model, from a photo by the extension of its file. A folder lists videos next to its photos when <see cref="Settings.SupportVideo"/> is on
    /// (and models when <see cref="Settings.SupportModels"/> is); which extensions are videos is the <see cref="Settings.VideoExtensions"/> setting of the open folder,
    /// and which are models is <see cref="Settings.ModelExtensions"/>, handed over with <see cref="UseVideoExtensions"/> and <see cref="UseModelExtensions"/> because the
    /// places that ask (the photo cache, the thumbnail strip, the key handling) only have a path.
    /// </summary>
    public static class MediaKinds
    {
        /// <summary>
        /// What a family archive holds: cameras write AVI (Canon: MJPEG), MOV, MP4, 3GP and MTS (camcorders); phones write MP4 and MOV; downloads add
        /// MKV, WebM and WMV.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultVideoExtensions =
            [".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".mts", ".m2ts", ".mpg", ".mpeg", ".wmv", ".webm", ".flv"];

        /// <summary>The 3D models: STL, the mesh a CAD program writes and a slicer reads.</summary>
        public static readonly IReadOnlyList<string> DefaultModelExtensions = [".stl"];

        private static volatile HashSet<string> _video = new(DefaultVideoExtensions, StringComparer.OrdinalIgnoreCase);
        private static volatile HashSet<string> _model = new(DefaultModelExtensions, StringComparer.OrdinalIgnoreCase);
        private static int _classification;

        /// <summary>Grows by one every time <see cref="UseVideoExtensions"/> or <see cref="UseModelExtensions"/> changes what counts as a video or a model: a cached count of them is stale when this has moved.</summary>
        public static int Classification => Volatile.Read(ref _classification);

        /// <summary>Sets which extensions (with their dot, any case) count as videos; an empty list means none do.</summary>
        public static void UseVideoExtensions(IEnumerable<string>? extensions)
        {
            _video = new HashSet<string>((extensions ?? DefaultVideoExtensions).Select(Normalize).Where(e => e.Length > 1), StringComparer.OrdinalIgnoreCase);
            Interlocked.Increment(ref _classification);
        }

        /// <summary>Sets which extensions (with their dot, any case) count as 3D models; an empty list means none do.</summary>
        public static void UseModelExtensions(IEnumerable<string>? extensions)
        {
            _model = new HashSet<string>((extensions ?? DefaultModelExtensions).Select(Normalize).Where(e => e.Length > 1), StringComparer.OrdinalIgnoreCase);
            Interlocked.Increment(ref _classification);
        }

        public static bool IsModel(string? path) => !string.IsNullOrEmpty(path) && IsModel(path.AsSpan());

        public static bool IsModel(ReadOnlySpan<char> fileName)
        {
            var extension = Path.GetExtension(fileName);
            return extension.Length > 1 && _model.GetAlternateLookup<ReadOnlySpan<char>>().Contains(extension);
        }

        /// <summary>An ordinary picture: neither a video nor a 3D model. What works on the pixels of a photo (drawing on it, turning it, tracing it) works on these only.</summary>
        public static bool IsImage(string? path) => !IsVideo(path) && !IsModel(path);

        public static bool IsVideo(string? path) => !string.IsNullOrEmpty(path) && IsVideo(path.AsSpan());

        public static bool IsVideo(ReadOnlySpan<char> fileName)
        {
            var extension = Path.GetExtension(fileName);
            return extension.Length > 1 && _video.GetAlternateLookup<ReadOnlySpan<char>>().Contains(extension);
        }

        // ".MP4", "mp4" and " .mp4 " all mean the same
        private static string Normalize(string extension)
        {
            var trimmed = extension.Trim();
            return trimmed.Length == 0 ? string.Empty : trimmed.StartsWith('.') ? trimmed : "." + trimmed;
        }
    }
}
