using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// The catalog of the videos of the folder that is open (<see cref="VideoCatalog"/>), where the photo cache can find it: it decodes the pictures
    /// of the photos and the posters of the videos on pool threads and only has the path of the file. The window opens it with the folder, when videos
    /// are on (<see cref="Settings.SupportVideo"/>), and closes it with the folder.
    /// </summary>
    internal static class VideoLibrary
    {
        private static readonly object Gate = new();
        private static VideoCatalog? _current;

        public static VideoCatalog? Current => Volatile.Read(ref _current);

        /// <summary>Opens the catalog of this folder (the one that is open already if it is the same folder).</summary>
        public static VideoCatalog Open(string folder)
        {
            lock (Gate)
            {
                var wanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
                if (_current is { } open && string.Equals(open.Root, wanted, StringComparison.OrdinalIgnoreCase))
                    return open;

                _current?.Dispose();
                return _current = VideoCatalog.Open(folder);
            }
        }

        public static void Close()
        {
            lock (Gate)
            {
                _current?.Dispose();
                _current = null;
            }
        }
    }
}
