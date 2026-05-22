namespace PhotoLiker.Core
{
    using MetadataExtractor;
    using MetadataExtractor.Formats.Exif;
    using MetadataExtractor.Formats.Jpeg;

    /// <summary>
    /// Cross-platform EXIF reader using MetadataExtractor (no System.Drawing dependency).
    /// </summary>
    public class FriendlyImageMetadata
    {
        public List<MetadataEntry> Entries { get; } = [];

        public FriendlyImageMetadata(string imagePath)
        {
            var seen = new HashSet<string>();

            try
            {
                var directories = ImageMetadataReader.ReadMetadata(imagePath);
                foreach (var directory in directories)
                {
                    foreach (var tag in directory.Tags)
                    {
                        int tagId = tag.Type;
                        string name = ExifTagMap.Tags.TryGetValue(tagId, out var friendly)
                            ? friendly
                            : tag.Name ?? string.Format(ImageHelperStrings.UnknownTagFormat, tagId);

                        if (!seen.Add(name)) continue;

                        string value = tag.Description ?? string.Empty;
                        byte[]? rawBytes = directory.GetByteArray(tagId);
                        int type = 0;

                        // Map MetadataExtractor type info to legacy type codes where needed
                        Entries.Add(new MetadataEntry(tagId, name, value, type, rawBytes));
                    }
                }
            }
            catch
            {
                // Unreadable file — return empty entries
            }
        }
    }
}
