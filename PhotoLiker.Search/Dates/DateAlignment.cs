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
    /// <param name="Assigned">The date taken is one a person gave the photo (see <see cref="DateSources"/>), which has none that is believed: <c>Facts.Taken</c> is that date.</param>
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
        /// <param name="assigned">The date taken to use if the photo has none that is believed: a date a person gave it, from where it said.</param>
        public static DateAlignmentEntry Plan(PhotoDateFacts facts, DateAlignmentOptions options, DateTime? assigned = null)
        {
            if (facts.Problem is not null)
                return new DateAlignmentEntry(facts, DateAlignmentStatus.Unreadable, false, false, ExifDateFields.None);
            var given = false;
            if (facts.Taken is null && assigned is { } date)
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
