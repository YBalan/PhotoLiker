using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>Where the date of a photo comes from.</summary>
    public enum DateSource
    {
        /// <summary>The photo has no date that can be believed.</summary>
        None = 0,

        /// <summary>The date the camera wrote into the photo (EXIF).</summary>
        Exif = 1,

        /// <summary>The camera's date was missing or wrong, so the date of the file is used.</summary>
        File = 2,
    }

    /// <summary>
    /// When a photo was taken, as the index keeps it. <paramref name="Version"/> is the version of the rules in
    /// <see cref="PhotoDates"/> that worked it out; 0 means that nobody has looked yet.
    /// </summary>
    public sealed record PhotoDate(DateTime? Taken, DateSource Source, int Version)
    {
        public static readonly PhotoDate NotRead = new(null, DateSource.None, 0);
    }

    /// <summary>
    /// Decides when a photo was taken. The EXIF date of the camera wins, unless it is not believable (an old camera that
    /// lost its clock writes 2001-01-01 into every photo; see <see cref="ClockResetUntil"/> and <see cref="FileAgreement"/> for how that is told
    /// from a real New Year's photo): then the date the file was created is used, which for photos copied off a camera or
    /// a card is about the day they were copied.
    /// </summary>
    public static class PhotoDates
    {
        /// <summary>Raised when the rules change in a way that gives other dates: the dates are then read again, which is quick.</summary>
        public const int Version = 1;

        /// <summary>Digital cameras for the public are not older than this.</summary>
        public const int FirstBelievableYear = 1995;

        /// <summary>
        /// A camera whose clock was reset starts again at January 1 00:00 and counts from there, so a date on that morning
        /// looks like such a clock...
        /// </summary>
        public static readonly TimeSpan ClockResetUntil = TimeSpan.FromHours(12);

        /// <summary>
        /// ... unless the file was created about then, which confirms the date: that is a real photo of New Year's night (there are
        /// many in a family library), not a camera that lost its clock. "About" is two days because a card copied to another
        /// computer can show the time shifted by the time zone, which can move it over midnight.
        /// </summary>
        public static readonly TimeSpan FileAgreement = TimeSpan.FromDays(2);

        /// <param name="now">The present, passed in so that the rules can be tested.</param>
        public static PhotoDate Resolve(ExifDates exif, DateTime? fileCreated, DateTime? fileModified, DateTime now)
        {
            foreach (var candidate in new[] { exif.Original, exif.Digitized, exif.Modified })
            {
                if (candidate is { } date && IsBelievableExif(date, fileCreated, now))
                    return new PhotoDate(date, DateSource.Exif, Version);
            }

            foreach (var candidate in new[] { fileCreated, fileModified })
            {
                if (candidate is { } date && IsBelievableFile(date, now))
                    return new PhotoDate(date, DateSource.File, Version);
            }

            return new PhotoDate(null, DateSource.None, Version);
        }

        /// <summary>
        /// The date of a photo that has no EXIF date at hand (it is not in the index, or its date was not read yet), from the times of
        /// its file in the folder listing: the date the file was created, else the date of its last change.
        /// </summary>
        public static DateTime? FromFile(PhotoFile file, DateTime now) =>
            Resolve(ExifDates.None, FileTime(file.CreationTimeUtc), FileTime(file.LastWriteTimeUtc), now).Taken;

        /// <summary>A time of a file as the date on the clock of this computer; a file that has none (it reads as 1601) has no date.</summary>
        public static DateTime? FileTime(DateTime utc) =>
            utc.Year > 1700 ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime() : null;

        public static bool IsBelievableExif(DateTime date, DateTime? fileCreated, DateTime now) =>
            IsBelievableFile(date, now) && (!LooksLikeAResetClock(date) || AgreesWithTheFile(date, fileCreated, now));

        public static bool IsBelievableFile(DateTime date, DateTime now) =>
            date.Year >= FirstBelievableYear && date <= now.AddDays(1);

        private static bool LooksLikeAResetClock(DateTime date) =>
            date.Month == 1 && date.Day == 1 && date.TimeOfDay < ClockResetUntil;

        // Only the creation date confirms: the date of the last change is written by the same clock as the EXIF date, so it always "agrees"
        private static bool AgreesWithTheFile(DateTime date, DateTime? fileCreated, DateTime now) =>
            fileCreated is { } created && IsBelievableFile(created, now) && (created - date).Duration() <= FileAgreement;
    }
}
