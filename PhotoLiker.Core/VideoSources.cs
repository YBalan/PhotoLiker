namespace PhotoLiker.Core
{
    /// <summary>
    /// Lists the photos a video can be made of besides the loaded ones: the copies in the liked folder or in one of its group
    /// folders (see <see cref="PhotoGroups"/>). The file system work can run on any thread.
    /// </summary>
    public static class VideoSources
    {
        /// <summary>
        /// The photos directly in a folder of copies (not those of the folders inside it, so the liked folder does not list its groups);
        /// none if the folder is not there.
        /// </summary>
        public static List<PhotoFile> Copies(string folder, IReadOnlyCollection<string> extensions)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return [];

            // A copy is created at the moment it is made, but keeps the last-write time of its photo: that is the date to order by
            return [.. PhotoScanner.Scan(folder, recursive: false, extensions, excludedFolder: null)
                .Select(copy => new PhotoFile(copy.OriginalFilePath, string.Empty)
                {
                    Length = copy.Length,
                    LastWriteTimeUtc = copy.LastWriteTimeUtc,
                    CreationTimeUtc = copy.LastWriteTimeUtc,
                })];
        }
    }
}
