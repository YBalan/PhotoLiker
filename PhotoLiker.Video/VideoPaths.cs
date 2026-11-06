using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>Where the video library keeps its files.</summary>
    public static class VideoPaths
    {
        /// <summary>
        /// The catalog of the videos of a folder: next to the photo index of the folder, but a file of its own, so that videos and photos never
        /// get in each other's way (a photo index can be deleted, exported or rebuilt without the videos knowing, and the other way round).
        /// Like the photo index it is a cache on this machine that can always be made again.
        /// </summary>
        public static string CatalogFile(string folder)
        {
            var photoIndex = SearchPaths.IndexFile(folder);
            return Path.Combine(Path.GetDirectoryName(photoIndex)!, "video-" + Path.GetFileName(photoIndex));
        }
    }
}
