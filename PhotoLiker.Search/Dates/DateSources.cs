using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoLiker.Search
{
    /// <summary>Where the date taken of a photo that has no EXIF date that can be believed comes from.</summary>
    public enum DateChoice
    {
        /// <summary>The EXIF date the photo has, which the person knows to be right (a real photo of New Year's night that looks like a camera that lost its clock).</summary>
        ExifAsItIs = 0,

        /// <summary>The date in the name of the folder the photo is in ("2015-05-01", "20150501", "01.05.2015 Anna").</summary>
        FolderName = 1,

        /// <summary>The date of the file: when it was created, else when it was last changed (for a copied photo that is about the day of the copy).</summary>
        FileDate = 2,

        /// <summary>A date the person typed.</summary>
        Typed = 3,
    }

    /// <summary>
    /// Works out the date taken of a photo that has none that <see cref="PhotoDates"/> believes, from where the person says it can be had. Such a photo is then
    /// handled like any other by <see cref="DateAlignment"/>: its file dates, and the dates inside it if asked, are set to it.
    /// </summary>
    public static class DateSources
    {
        /// <summary>The time of day for a date that has none: noon is the farthest from a midnight that a time zone could move it over.</summary>
        public static readonly TimeSpan NoTime = TimeSpan.FromHours(12);

        private static readonly Regex YearFirst = new(@"(?<!\d)((?:19|20)\d{2})[-._ ]?(0[1-9]|1[0-2])[-._ ]?(0[1-9]|[12]\d|3[01])(?!\d)", RegexOptions.CultureInvariant);
        private static readonly Regex DayFirst = new(@"(?<!\d)(0[1-9]|[12]\d|3[01])[-._ ](0[1-9]|1[0-2])[-._ ]((?:19|20)\d{2})(?!\d)", RegexOptions.CultureInvariant);

        /// <summary>The date from this source for the photo, or null if it has none from there.</summary>
        /// <param name="root">The photo folder: only the folders below it are looked at for a date.</param>
        /// <param name="typed">The date for <see cref="DateChoice.Typed"/>.</param>
        /// <param name="now">The present, passed in so that the rules can be tested.</param>
        public static DateTime? Resolve(DateChoice choice, PhotoDateFacts facts, string root, DateTime? typed, DateTime now) => choice switch
        {
            DateChoice.ExifAsItIs => FromExif(facts.Exif.Dates),
            DateChoice.FolderName => FromFolderName(root, facts.Path, FromExif(facts.Exif.Dates)),
            DateChoice.FileDate => FromFile(facts, now),
            DateChoice.Typed => typed,
            _ => null,
        };

        /// <summary>The date the camera wrote, believed or not: DateTimeOriginal, else DateTimeDigitized, else DateTime. Null if it wrote none (or only blanks).</summary>
        public static DateTime? FromExif(ExifDates dates) => dates.Original ?? dates.Digitized ?? dates.Modified;

        /// <summary>The date of the file: when it was created, else when it was last changed, as long as it is believable. Null for a file that has neither.</summary>
        public static DateTime? FromFile(PhotoDateFacts facts, DateTime now)
        {
            foreach (var utc in new[] { facts.CreatedUtc, facts.ModifiedUtc })
            {
                if (PhotoDates.FileTime(utc) is { } local && PhotoDates.IsBelievableFile(local, now))
                    return local;
            }
            return null;
        }

        /// <summary>
        /// The date in the name of the folder the photo is in (the innermost folder below the photo folder that has one), at the time of day of
        /// <paramref name="timeOf"/> if the photo has one, else at noon. Null if no folder has a date in its name.
        /// </summary>
        public static DateTime? FromFolderName(string root, string photoPath, DateTime? timeOf)
        {
            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(photoPath) ?? string.Empty);
            if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                return null;

            foreach (var name in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                if (TryParseFolderDate(name, out var date))
                    return date + (timeOf?.TimeOfDay ?? NoTime);
            }
            return null;
        }

        /// <summary>A day in the name of a folder: <c>2015-05-01</c>, <c>2015_05_01</c>, <c>20150501</c>, <c>01.05.2015</c>, with anything after or before. A month alone, or a year, is not a day.</summary>
        public static bool TryParseFolderDate(string name, out DateTime date)
        {
            date = default;
            if (YearFirst.Match(name) is { Success: true } first && Day(first.Groups[1].Value, first.Groups[2].Value, first.Groups[3].Value, out date))
                return true;
            return DayFirst.Match(name) is { Success: true } second && Day(second.Groups[3].Value, second.Groups[2].Value, second.Groups[1].Value, out date);
        }

        private static bool Day(string year, string month, string day, out DateTime date) =>
            DateTime.TryParseExact($"{year}-{month}-{day}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
