using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>
    /// The rules for giving files a date taken by hand ("Use a date..." of the Align dates tool), apart from the window that asks for it, so that they can be tested without one: who can be given a
    /// date, what planning a run does with a given date, and when it is written into the file itself.
    /// </summary>
    public static class GivenDates
    {
        /// <summary>A file that could be read can be given a date, whether it has a usable one or not: the person may want it dated by another rule than the rest.</summary>
        public static bool CanBeGiven(PhotoDateFacts facts) => facts.Problem is null;

        /// <summary>A file that could be read and has no date taken that is believed: it waits for a date (these are what "All" means in the question).</summary>
        public static bool WaitsForDate(PhotoDateFacts facts) => facts.Problem is null && facts.Taken is null;

        /// <summary>
        /// Whether the given date is also written into the file itself. Not if the person did not ask (the tick). A photo gets it as its Date Taken (Original), also where it has no place for
        /// it yet. A video gets it as the date it was recorded, in place, where its kind keeps one (<see cref="VideoContainerDates.CanWrite"/>), and not when it says that date already.
        /// </summary>
        public static bool WritesInside(PhotoDateFacts facts, DateTime given, bool tick)
        {
            if (!tick)
                return false;
            if (!MediaKinds.IsVideo(facts.Path))
                return true;
            return VideoContainerDates.CanWrite(facts.Path) && !(facts.Taken is { } recorded && DateAlignment.SameMoment(recorded, given));
        }

        /// <summary>
        /// What the options (and the date given to the file, if one was) mean for one file. A video has no EXIF, so "dates inside the photo too" is for photos only: of a video the
        /// dates of the file are changed, and the date it was recorded only where <see cref="WritesInside"/> says so.
        /// </summary>
        public static DateAlignmentEntry Plan(PhotoDateFacts facts, DateAlignmentOptions options, (DateTime Date, bool WriteTaken)? given)
        {
            var own = MediaKinds.IsVideo(facts.Path) ? options with { ExifFields = false } : options;
            return given is { } chosen
                ? DateAlignment.Plan(facts, own, chosen.Date, WritesInside(facts, chosen.Date, chosen.WriteTaken))
                : DateAlignment.Plan(facts, own);
        }

        /// <summary>
        /// How many of the files cannot get the date written into them, however the tick stands: with the tick on, the videos of a kind that keeps no date; with it off, the photos that have
        /// no place for one (which only get their file dates), whatever the options say about the dates inside.
        /// </summary>
        public static int WithoutPlace(IEnumerable<PhotoDateFacts> scope, bool tick) => tick
            ? scope.Count(f => MediaKinds.IsVideo(f.Path) && !VideoContainerDates.CanWrite(f.Path))
            : scope.Count(f => !MediaKinds.IsVideo(f.Path) && f.Exif.OriginalOffset is null && f.Exif.DigitizedOffset is null && f.Exif.ModifiedOffset is null);
    }
}
