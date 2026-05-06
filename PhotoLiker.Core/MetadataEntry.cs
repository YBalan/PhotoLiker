namespace PhotoLiker.Core
{
    public record MetadataEntry(int TagId, string Name, string Value, int Type = 0, byte[]? RawBytes = null, string[]? ExtraAttributeTypes = null);
}
