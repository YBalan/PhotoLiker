using System.Globalization;

namespace PhotoLiker.Core
{
    /// <summary>
    /// How dates are written. A file stores a date the way EXIF does ("2025:08:14 09:12:07"); what a person reads in the grids and
    /// dialogs has the month as a word ("14 Aug 2025 09:12:07"), which can not be mistaken for a day. The month is always English, as the rest of the app.
    /// </summary>
    public static class DateText
    {
        /// <summary>The way EXIF writes a date, as it is in the file.</summary>
        public const string ExifFormat = "yyyy:MM:dd HH:mm:ss";

        /// <summary>The way a date is shown. This and <see cref="DisplayDateFormat"/> are the only places that decide it.</summary>
        public const string DisplayFormat = "dd MMM yyyy HH:mm:ss";

        public const string DisplayDateFormat = "dd MMM yyyy";

        private const string GpsDateFormat = "yyyy:MM:dd";

        public static string Display(DateTime date) => date.ToString(DisplayFormat, CultureInfo.InvariantCulture);

        /// <summary>A date written either way (the one of the file, or the one that is shown), or false.</summary>
        public static bool TryParse(string? text, out DateTime date) =>
            DateTime.TryParseExact(text?.Trim(), [ExifFormat, DisplayFormat], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private static readonly string[] TypedFormats =
        [
            ExifFormat, DisplayFormat, "dd MMM yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss",
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm",
        ];

        private static readonly string[] TypedDayFormats = [DisplayDateFormat, "yyyy-MM-dd", "yyyy:MM:dd", "dd.MM.yyyy"];

        /// <summary>
        /// A date a person typed: either way a date is written in the files and the grids, with or without the seconds, <c>2015-05-01 14:30</c>
        /// or <c>01.05.2015 14:30</c>. A day alone gets noon, the time of day farthest from a midnight that a time zone could move it over.
        /// Always day first or year first: <c>05/01/2015</c> is not a date here, since it can be read two ways.
        /// </summary>
        public static bool TryParseTyped(string? text, out DateTime date)
        {
            var trimmed = text?.Trim();
            if (DateTime.TryParseExact(trimmed, TypedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;

            if (DateTime.TryParseExact(trimmed, TypedDayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                date = day.AddHours(12);
                return true;
            }
            return false;
        }

        /// <summary>The first date in a text that has a shown date somewhere in it ("created 14 Aug 2025 09:12:07, modified ..."), or false.</summary>
        public static bool TryFindDisplayed(string? text, out DateTime date)
        {
            date = default;
            if (string.IsNullOrEmpty(text))
                return false;

            for (var at = 0; at + DisplayFormat.Length <= text.Length; at++)
            {
                if (char.IsDigit(text[at]) && TryParse(text.Substring(at, DisplayFormat.Length), out date))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A value of a metadata entry for showing: a date in the EXIF way becomes the shown way; everything else (a blank date, text
        /// that only looks a little like one) stays as it is.
        /// </summary>
        public static string Reformat(string value) =>
            DateTime.TryParseExact(value.Trim(), ExifFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? Display(date) : value;

        /// <summary>The date of a GPS fix ("2024:06:15") for showing; a text that is not such a date only gets its colons turned into dashes.</summary>
        public static string ReformatGpsDate(string value) =>
            DateTime.TryParseExact(value.Trim(), GpsDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString(DisplayDateFormat, CultureInfo.InvariantCulture)
                : value.Replace(':', '-');
    }
}
