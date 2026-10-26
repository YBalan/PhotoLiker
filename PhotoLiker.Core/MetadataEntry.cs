namespace PhotoLiker.Core
{
    public record MetadataEntry(int TagId, string Name, string Value, int Type = 0, byte[]? RawBytes = null, string[]? ExtraAttributeTypes = null);

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
