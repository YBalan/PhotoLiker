namespace PhotoLiker.Core
{
    /// <summary>
    /// What it means to edit one of the date rows of the metadata grid: the date taken, the date digitized, the date last saved (all three
    /// are inside the photo) and the File Date (the dates of the file on the disk). The date taken and the dates of the file are meant to be
    /// one moment (the Rotate button and the Align dates tool make them so), so a new date taken is also given to the file; the others
    /// change only the one thing they name.
    /// </summary>
    public sealed record DateRowEdit(ExifChange Change, DateTime? FileDatesUtc)
    {
        private const int DateTakenTag = 0x9003;
        private const int DigitizedTag = 0x9004;
        private const int LastSavedTag = 0x0132;

        /// <summary>Whether the row with this tag id can be edited.</summary>
        public static bool IsEditable(int tagId) => tagId is DateTakenTag or DigitizedTag or LastSavedTag or ExifTagMap.FileDateTagId;

        /// <summary>The change for a new date (the time of the camera's clock, local) in the row, or null for a row that has no date to edit.</summary>
        public static DateRowEdit? For(int tagId, DateTime date)
        {
            var whole = new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
            var utc = DateTime.SpecifyKind(whole, DateTimeKind.Local).ToUniversalTime();
            return tagId switch
            {
                DateTakenTag => new DateRowEdit(new ExifChange { Original = whole }, utc),
                DigitizedTag => new DateRowEdit(new ExifChange { Digitized = whole }, null),
                LastSavedTag => new DateRowEdit(new ExifChange { LastSaved = whole }, null),
                ExifTagMap.FileDateTagId => new DateRowEdit(new ExifChange(), utc),
                _ => null,
            };
        }
    }
}
