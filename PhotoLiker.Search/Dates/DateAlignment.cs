using PhotoLiker.Core;

namespace PhotoLiker.Search
{
    /// <summary>Which of the three EXIF dates of a photo are meant (or have to be written).</summary>
    [Flags]
    public enum ExifDateFields
    {
        None = 0,
        Original = 1,
        Digitized = 2,
        Modified = 4,
    }

    /// <summary>What to make equal to the date taken. The date of the file is the default, the dates inside the photo have to be asked for.</summary>
    /// <param name="Created">The date the file was created.</param>
    /// <param name="Modified">The date the file was last changed.</param>
    /// <param name="ExifFields">The other dates inside the photo (DateTimeDigitized, DateTime), which a camera writes with the same time as DateTimeOriginal.</param>
    public sealed record DateAlignmentOptions(bool Created = true, bool Modified = true, bool ExifFields = false)
    {
        public static readonly DateAlignmentOptions Default = new();

        public bool Any => Created || Modified || ExifFields;
    }

    public enum DateAlignmentStatus
    {
        /// <summary>Everything that was asked for is the date taken already.</summary>
        Aligned = 0,

        /// <summary>Something has to be changed.</summary>
        Change = 1,

        /// <summary>The photo has no EXIF date that can be believed (see <see cref="PhotoDates"/>), so there is nothing to align to; it is left alone.</summary>
        NoDate = 2,

        /// <summary>The photo could not be read.</summary>
        Unreadable = 3,
    }

    /// <summary>What a scan found out about one photo file, whatever is going to be done with it.</summary>
    /// <param name="Taken">The date taken, local time of the camera: the EXIF date that <see cref="PhotoDates"/> believes; null if there is none.</param>
    /// <param name="Problem">Why the photo could not be read, if so.</param>
    public sealed record PhotoDateFacts(string Path, ExifDateSlots Exif, DateTime? Taken, DateTime CreatedUtc, DateTime ModifiedUtc, string? Problem = null);

    /// <summary>What the options mean for one photo.</summary>
    /// <param name="Assigned">The date taken is one a person gave the photo (see <see cref="DateSources"/>), which has none that is believed or was chosen to be dated by another rule: <c>Facts.Taken</c> is that date.</param>
    public sealed record DateAlignmentEntry(PhotoDateFacts Facts, DateAlignmentStatus Status, bool SetCreated, bool SetModified, ExifDateFields SetExif, bool Assigned = false)
    {
        public string Path => Facts.Path;

        /// <summary>The date taken as the file system keeps it.</summary>
        public DateTime? TakenUtc => Facts.Taken is { } taken ? DateAlignment.ToUtc(taken) : null;
    }

    /// <summary>
    /// Makes the dates of the files of photos agree with the date the camera wrote into them. The EXIF date is what the photographer's
    /// camera knew; the date of a file is only when it was copied or saved, so in a library that was moved around it says nothing about
    /// when the photo was taken. Looking (<see cref="InspectAll"/>) and deciding (<see cref="Plan"/>) are apart from doing
    /// (<see cref="DateAligner"/>), so that a dialog can show what would happen before anything is touched.
    /// </summary>
    public static class DateAlignment
    {
        /// <summary>Dates closer than this are the same: FAT keeps even seconds only, and a network share may round.</summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

        private const int Readers = 8; // reading a header is waiting for the network, not for the processor

        public static bool SameMoment(DateTime a, DateTime b) => (a - b).Duration() <= Tolerance;

        /// <summary>The time of the camera's clock (EXIF has no time zone) as the file system keeps times: the clock of this computer is taken to be that of the camera.</summary>
        public static DateTime ToUtc(DateTime taken) => DateTime.SpecifyKind(taken, DateTimeKind.Local).ToUniversalTime();

        /// <summary>Reads the start of the photo and takes its file times; a photo that can not be read comes back with the reason.</summary>
        /// <param name="now">The present, passed in so that the rules can be tested.</param>
        public static PhotoDateFacts Inspect(PhotoFile file, DateTime now)
        {
            try
            {
                var created = file.CreationTimeUtc;
                var modified = file.LastWriteTimeUtc;
                if (created.Year <= 1700 || modified.Year <= 1700) // the listing this file comes from had no times
                {
                    var info = new FileInfo(file.OriginalFilePath);
                    created = info.CreationTimeUtc;
                    modified = info.LastWriteTimeUtc;
                }

                ExifDateSlots slots;
                using (var stream = new FileStream(
                    file.OriginalFilePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 1 << 16, FileOptions.RandomAccess))
                {
                    slots = ExifReader.Locate(stream);
                }

                var resolved = PhotoDates.Resolve(slots.Dates, PhotoDates.FileTime(created), PhotoDates.FileTime(modified), now);
                return new PhotoDateFacts(file.OriginalFilePath, slots, resolved.Source == DateSource.Exif ? resolved.Taken : null, created, modified);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new PhotoDateFacts(file.OriginalFilePath, ExifDateSlots.None, null, default, default, ex.Message);
            }
        }

        /// <summary>
        /// The dates a photo gets when the app saves it again (turned, see <c>JpegOrientation</c>): its Date Taken (Original) and the dates of
        /// its file are one and the same moment.
        /// </summary>
        /// <param name="Taken">The date taken, local time of the camera, whole seconds.</param>
        /// <param name="FileUtc">The same moment as the file system keeps it: both dates of the file become this.</param>
        /// <param name="WriteTaken">The photo does not say <paramref name="Taken"/> as its Date Taken (Original) yet, so it is written into it.</param>
        /// <param name="FromFile">The photo has no date taken that is believed, so the date of its file is the date taken.</param>
        public sealed record SavedDates(DateTime Taken, DateTime FileUtc, bool WriteTaken, bool FromFile);

        /// <summary>
        /// What the Date Taken (Original) and the dates of the file of a photo should be when it is saved again: the date taken the photo has
        /// (the EXIF date that <see cref="PhotoDates"/> believes), else the date of its file (created, else changed, as long as it is
        /// believable) which is then also written into the photo as its date taken. Null when the photo can not be read, or has no date at all
        /// to go by: it is saved and keeps its dates.
        /// </summary>
        /// <param name="now">The present, passed in so that the rules can be tested.</param>
        public static SavedDates? DatesForSaving(PhotoDateFacts facts, DateTime now)
        {
            if (facts.Problem is not null)
                return null;

            if (facts.Taken is { } taken)
            {
                var whole = WholeSeconds(taken);
                return new SavedDates(whole, ToUtc(whole), WriteTaken: facts.Exif.Dates.Original != whole, FromFile: false);
            }

            if (DateSources.FromFile(facts, now) is not { } fromFile)
                return null;
            var date = WholeSeconds(fromFile);
            return new SavedDates(date, ToUtc(date), WriteTaken: true, FromFile: true);
        }

        /// <summary>Looks at the photo at <paramref name="path"/> (the start of the file is read) and decides as <see cref="DatesForSaving(PhotoDateFacts, DateTime)"/>.</summary>
        public static SavedDates? DatesForSaving(string path, DateTime now) =>
            DatesForSaving(Inspect(new PhotoFile(path, string.Empty), now), now);

        private static DateTime WholeSeconds(DateTime date) => new(date.Ticks - date.Ticks % TimeSpan.TicksPerSecond, date.Kind);

        /// <summary>
        /// Looks at all the photos, in the order they are given. A stop throws <see cref="OperationCanceledException"/>.
        /// </summary>
        public static PhotoDateFacts[] InspectAll(IReadOnlyList<PhotoFile> files, DateTime now, IProgress<int>? progress, CancellationToken token)
        {
            var facts = new PhotoDateFacts[files.Count];
            var done = 0;
            Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Readers, CancellationToken = token }, i =>
            {
                facts[i] = Inspect(files[i], now);
                var count = Interlocked.Increment(ref done);
                if (count % 50 == 0 || count == files.Count)
                    progress?.Report(count);
            });
            return facts;
        }

        /// <summary>What has to be done to the photo for the options: nothing, a change, or why it is left alone.</summary>
        /// <param name="assigned">The date taken to use instead of what the photo says: a date a person gave it, from where it said. A photo that has no date that is believed gets it
        /// to have one; a photo that has one gets it in place of that one (the person chose these files to be changed by another rule).</param>
        /// <param name="writeTaken">Write that date into the photo as its Date Taken (Original), also where the photo has no place for it yet (the Exif block is made larger), whatever the options say.</param>
        public static DateAlignmentEntry Plan(PhotoDateFacts facts, DateAlignmentOptions options, DateTime? assigned = null, bool writeTaken = false)
        {
            if (facts.Problem is not null)
                return new DateAlignmentEntry(facts, DateAlignmentStatus.Unreadable, false, false, ExifDateFields.None);
            var given = false;
            if (assigned is { } date)
            {
                // From here on the photo has a date taken like any other; what is written inside it (even a date that is not believed) is replaced when asked for
                facts = facts with { Taken = date };
                given = true;
            }
            if (facts.Taken is not { } taken)
                return new DateAlignmentEntry(facts, DateAlignmentStatus.NoDate, false, false, ExifDateFields.None);

            var takenUtc = ToUtc(taken);
            var created = options.Created && !SameMoment(facts.CreatedUtc, takenUtc);
            var modified = options.Modified && !SameMoment(facts.ModifiedUtc, takenUtc);
            var exif = options.ExifFields ? ExifToWrite(facts.Exif, taken) : ExifDateFields.None;
            if (given && writeTaken && facts.Exif.Dates.Original != WholeSeconds(taken))
                exif |= ExifDateFields.Original;

            var status = created || modified || exif != ExifDateFields.None ? DateAlignmentStatus.Change : DateAlignmentStatus.Aligned;
            return new DateAlignmentEntry(facts, status, created, modified, exif, given);
        }

        // Only a date that has a place in the file can be written, and only when it differs (a blank one differs too)
        private static ExifDateFields ExifToWrite(ExifDateSlots slots, DateTime taken)
        {
            var fields = ExifDateFields.None;
            if (slots.OriginalOffset is not null && slots.Dates.Original != taken)
                fields |= ExifDateFields.Original;
            if (slots.DigitizedOffset is not null && slots.Dates.Digitized != taken)
                fields |= ExifDateFields.Digitized;
            if (slots.ModifiedOffset is not null && slots.Dates.Modified != taken)
                fields |= ExifDateFields.Modified;
            return fields;
        }

        /// <summary>The photo as it is after the change was made and read back: its times from the disk, and the EXIF dates written.</summary>
        public static PhotoDateFacts After(PhotoDateFacts facts, ExifDateFields written, DateTime createdUtc, DateTime modifiedUtc)
        {
            var dates = facts.Exif.Dates;
            if (facts.Taken is { } taken)
            {
                dates = new ExifDates(
                    written.HasFlag(ExifDateFields.Original) ? taken : dates.Original,
                    written.HasFlag(ExifDateFields.Digitized) ? taken : dates.Digitized,
                    written.HasFlag(ExifDateFields.Modified) ? taken : dates.Modified);
            }
            return facts with { Exif = facts.Exif with { Dates = dates }, CreatedUtc = createdUtc, ModifiedUtc = modifiedUtc };
        }
    }
}
