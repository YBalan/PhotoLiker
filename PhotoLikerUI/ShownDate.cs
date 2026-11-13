using PhotoLiker.Core;
using PhotoLiker.Search;
using PhotoLiker.Video;

namespace PhotoLikerUI
{
    /// <summary>
    /// The date that is written in the corner of the viewer for the file that is shown (see <c>MainForm.DateMark</c>). It is the date taken by the rules the slideshow and
    /// the search use (<see cref="PhotoDates"/>: the date of the camera if it can be believed, else the date of the file), worked out from the rows the metadata grid
    /// has already, so nothing is read from the file again. For a video it is the Recorded row, which the catalog has decided by the same rules.
    /// </summary>
    internal static class ShownDate
    {
        public static DateTime? Of(IReadOnlyList<MetadataEntry> metadata, bool video, DateTime fileCreatedUtc, DateTime fileModifiedUtc, DateTime now)
        {
            if (video)
                return Read(metadata, VideoMetadata.RecordedTagId);

            var exif = new ExifDates(Read(metadata, DateRowEdit.DateTakenTagId), Read(metadata, DateRowEdit.DigitizedTagId), Read(metadata, DateRowEdit.LastSavedTagId));
            return PhotoDates.Resolve(exif, PhotoDates.FileTime(fileCreatedUtc), PhotoDates.FileTime(fileModifiedUtc), now).Taken;
        }

        /// <summary>The date a row of the grid says (it is written the way dates are shown, or the way EXIF has them); null for a row that is not there or has no date in it.</summary>
        private static DateTime? Read(IReadOnlyList<MetadataEntry> metadata, int tagId) =>
            metadata.FirstOrDefault(row => row.TagId == tagId) is { } found && DateText.TryParse(found.Value, out var date) ? date : null;
    }
}
