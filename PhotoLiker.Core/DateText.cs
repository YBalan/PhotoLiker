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
