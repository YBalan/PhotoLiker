namespace PhotoLikerUI
{
    using System.ComponentModel;

    public record MetadataEntry(int TagId, string Name, string Value, int Type = 0, byte[]? RawBytes = null, Attribute[]? ExtraAttributes = null);
}
