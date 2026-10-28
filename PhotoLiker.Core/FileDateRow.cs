using System.Globalization;

namespace PhotoLiker.Core
{
    /// <summary>
    /// The "File Date" row of the metadata grid: the dates of the file on the disk next to the date the camera wrote into the photo,
    /// and whether the two agree. Written the way the dates of the photo are (see <see cref="DateText"/>), so that they can be compared at a glance.
    /// The date it is compared with is the time of shooting (DateTimeOriginal, else DateTimeDigitized), never the date the photo was last saved
    /// (<c>Date Last Saved</c>, which a program that turned or edited the photo changes), and the row names the one it used.
    /// </summary>
    public static class FileDateRow
    {
        private const string CreatedModifiedFormat = "created {0}, modified {1}";
        private const string AgreesFormat = " (= {0})";
        private const string DiffersFormat = " (≠ {0})";

        private const int OriginalTag = 0x9003;
        private const int DigitizedTag = 0x9004;

        /// <summary>Dates closer than this are the same (a network share or a FAT disk rounds to two seconds).</summary>
        private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

        /// <summary>The row for a photo on the disk, or null if the file can not be looked at. <paramref name="exif"/> are its EXIF entries.</summary>
        public static MetadataEntry? Create(string path, IReadOnlyList<MetadataEntry> exif)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return null;

                var taken = TakenWithName(exif);
                return new MetadataEntry(
                    ExifTagMap.FileDateTagId, ExifTagMap.Tags[ExifTagMap.FileDateTagId],
                    Describe(info.CreationTime, info.LastWriteTime, taken?.Date, taken?.Name ?? ExifTagMap.Tags[OriginalTag]));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <param name="taken">The time of shooting the camera wrote into the photo; without it the dates of the file are shown as they are.</param>
        /// <param name="takenName">The name of the row that holds it in the grid, which the note names so that it can be found there.</param>
        public static string Describe(DateTime created, DateTime modified, DateTime? taken, string takenName = "Date Taken (Original)")
        {
            var text = (created - modified).Duration() <= Tolerance
                ? DateText.Display(modified)
                : string.Format(CultureInfo.InvariantCulture, CreatedModifiedFormat, DateText.Display(created), DateText.Display(modified));

            if (taken is not { } date)
                return text;

            var agrees = (created - date).Duration() <= Tolerance && (modified - date).Duration() <= Tolerance;
            return text + string.Format(CultureInfo.InvariantCulture, agrees ? AgreesFormat : DiffersFormat, takenName);
        }

        /// <summary>When the picture was taken (DateTimeOriginal, else DateTimeDigitized) among the entries, if it is there and a date.</summary>
        public static DateTime? TakenOf(IReadOnlyList<MetadataEntry> exif) => TakenWithName(exif)?.Date;

        private static (DateTime Date, string Name)? TakenWithName(IReadOnlyList<MetadataEntry> exif)
        {
            foreach (var tag in new[] { OriginalTag, DigitizedTag })
            {
                var entry = exif.FirstOrDefault(e => e.TagId == tag);
                if (entry is not null && DateText.TryParse(entry.Value, out var date))
                    return (date, ExifTagMap.Tags[tag]);
            }
            return null;
        }
    }
}
