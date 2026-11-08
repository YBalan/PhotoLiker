namespace PhotoLiker.Core
{
    /// <summary>
    /// Tells a video from a photo by the extension of its file. A folder lists videos next to its photos when <see cref="Settings.SupportVideo"/> is on;
    /// which extensions are videos is the <see cref="Settings.VideoExtensions"/> setting of the open folder, handed over with
    /// <see cref="UseVideoExtensions"/> because the places that ask (the photo cache, the thumbnail strip, the key handling) only have a path.
    /// </summary>
    public static class MediaKinds
    {
        /// <summary>
        /// What a family archive holds: cameras write AVI (Canon: MJPEG), MOV, MP4, 3GP and MTS (camcorders); phones write MP4 and MOV; downloads add
        /// MKV, WebM and WMV.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultVideoExtensions =
            [".mp4", ".mov", ".avi", ".mkv", ".m4v", ".3gp", ".mts", ".m2ts", ".mpg", ".mpeg", ".wmv", ".webm", ".flv"];

        private static volatile HashSet<string> _video = new(DefaultVideoExtensions, StringComparer.OrdinalIgnoreCase);
        private static int _classification;

        /// <summary>Grows by one every time <see cref="UseVideoExtensions"/> changes what counts as a video: a cached count of videos is stale when this has moved.</summary>
        public static int Classification => Volatile.Read(ref _classification);

        /// <summary>Sets which extensions (with their dot, any case) count as videos; an empty list means none do.</summary>
        public static void UseVideoExtensions(IEnumerable<string>? extensions)
        {
            _video = new HashSet<string>((extensions ?? DefaultVideoExtensions).Select(Normalize).Where(e => e.Length > 1), StringComparer.OrdinalIgnoreCase);
            Interlocked.Increment(ref _classification);
        }

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
