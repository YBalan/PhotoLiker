namespace PhotoLiker.Core
{
    /// <param name="Description">What the row means, for the help pane under the grid, when the code that made the entry knows better than <see cref="MetadataDescriptions"/> (the rows of a video).</param>
    public record MetadataEntry(int TagId, string Name, string Value, int Type = 0, byte[]? RawBytes = null, string[]? ExtraAttributeTypes = null, string? Description = null)
    {
        /// <summary>The tag ids from here up are not EXIF tags but rows describing a video (duration, frame size ...): they are listed in the video category.</summary>
        public const int VideoTagBase = 0x10000;
    }

    /// <summary>
    /// Values of <see cref="MetadataEntry.ExtraAttributeTypes"/>: what a front end should do with an entry besides
    /// showing its text. Each front end maps them to its own controls (e.g. the WinForms grid to an editor).
    /// </summary>
    public static class MetadataExtras
    {
        /// <summary>The value is a URL that can be opened.</summary>
        public const string MapLink = "MapLink";
    }
}
