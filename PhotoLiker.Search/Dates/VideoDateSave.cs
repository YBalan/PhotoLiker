using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>What <see cref="VideoDateSave.Save"/> left on the disk.</summary>
    /// <param name="WrittenInside">The date was written into the video (its header or its <c>IDIT</c> chunk), and the video now says it was recorded then.</param>
    /// <param name="NoPlaceInside">The video has no place for a date (a kind that cannot hold one, or an AVI with no <c>IDIT</c>): only the dates of its file were set.</param>
    /// <param name="DatesKept">The drive kept the dates of the file (a network drive may say yes and keep nothing).</param>
    public sealed record VideoDateSaved(long Length, DateTime CreatedUtc, DateTime LastWriteUtc, bool WrittenInside, bool NoPlaceInside, bool DatesKept);

    /// <summary>
    /// Gives one video another date of recording by hand (the "Recorded" row of the metadata grid): the date is written where the video keeps it (see
    /// <see cref="VideoContainerDates"/>; no byte moves, the size stays) and the created and modified dates of the file are made the same moment, as for a photo
    /// whose Date Taken is edited. Unlike the Align dates tool nothing is logged: it is one video that the person named.
    /// </summary>
    public static class VideoDateSave
    {
        /// <summary>
        /// Sets the date of the video. <paramref name="taken"/> is the time of the camera's clock (the clock of this computer is taken to be it, as everywhere else); the seconds are kept, the rest is dropped.
        /// A video that has no place for a date still gets the dates of its file.
        /// </summary>
        /// <exception cref="InvalidDataException">The date is too early or too late for the video to hold.</exception>
        /// <exception cref="IOException">The video could not be read or written (it is open in a player, say), or another program changed it meanwhile. It is left as it was.</exception>
        public static VideoDateSaved Save(string path, DateTime taken)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException(SearchLibStrings.PhotoGone);

            taken = new DateTime(taken.Ticks - taken.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
            var takenUtc = DateAlignment.ToUtc(taken);
            if (takenUtc < DateTime.FromFileTimeUtc(0))
                throw new InvalidDataException(SearchLibStrings.VideoDateOutOfRange); // Windows cannot keep a file date before 1601

            IReadOnlyList<VideoDatePatch>? patches = null;
            if (VideoContainerDates.CanWrite(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                patches = VideoContainerDates.Plan(stream, taken);
            }

            DateAligner.WithWritableFile(path, () =>
            {
                if (patches is { Count: > 0 })
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    foreach (var patch in patches)
                    {
                        var current = new byte[patch.Old.Length];
                        stream.Seek(patch.Offset, SeekOrigin.Begin);
                        stream.ReadExactly(current);
                        if (!current.AsSpan().SequenceEqual(patch.Old))
                            throw new IOException(SearchLibStrings.VideoChangedMeanwhile);
                    }

                    foreach (var patch in patches)
                    {
                        stream.Seek(patch.Offset, SeekOrigin.Begin);
                        stream.Write(patch.New);
                    }
                    stream.Flush(flushToDisk: true);

                    // Through the handle that wrote, which is what keeps a network share from moving the date of change to now a moment later (see FileDates)
                    FileDates.SetOnHandle(stream, takenUtc, takenUtc);
                }

                // Also for a video that was not written, and for a drive that did not take the dates through the handle
                if (!DateAlignment.SameMoment(new FileInfo(path).CreationTimeUtc, takenUtc))
                    File.SetCreationTimeUtc(path, takenUtc);
                if (!DateAlignment.SameMoment(File.GetLastWriteTimeUtc(path), takenUtc))
                    File.SetLastWriteTimeUtc(path, takenUtc);
            });

            var after = new FileInfo(path);
            var kept = DateAlignment.SameMoment(after.CreationTimeUtc, takenUtc) && DateAlignment.SameMoment(after.LastWriteTimeUtc, takenUtc);
            var inside = patches is not null; // an empty list: it said so already
            return new VideoDateSaved(after.Length, after.CreationTimeUtc, after.LastWriteTimeUtc, inside, NoPlaceInside: !inside, kept);
        }
    }
}
