using PhotoLiker.Search;

namespace PhotoLiker.Video
{
    /// <summary>When a video was recorded: the date written into the video, else the date of the file, by the same rules as for photos.</summary>
    public static class VideoDates
    {
        /// <summary>
        /// The date the video was recorded. The date inside the video wins (a camera writes it when it starts recording; the date of the file
        /// is when it was copied, a day later in the library this was made for), unless <see cref="PhotoDates"/> does not believe it (an
        /// old camera that lost its clock): then the date the file was created is used, then the one it was changed.
        /// </summary>
        public static PhotoDate Resolve(VideoInfo info, DateTime createdUtc, DateTime modifiedUtc, DateTime now) =>
            PhotoDates.Resolve(new ExifDates(info.Recorded, null, null), PhotoDates.FileTime(createdUtc), PhotoDates.FileTime(modifiedUtc), now);
    }
}
