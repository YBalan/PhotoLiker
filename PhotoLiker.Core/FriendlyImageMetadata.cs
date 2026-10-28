namespace PhotoLiker.Core
{
    using MetadataExtractor;
    using MetadataExtractor.Formats.Exif;
    using MetadataExtractor.Formats.Jpeg;

    /// <summary>A raw EXIF property as a platform image decoder (e.g. GDI+ <c>PropertyItem</c>) hands it out.</summary>
    public readonly record struct RawExifProperty(int Id, int Type, byte[]? Value);

    /// <summary>
    /// The EXIF entries of a photo, read either from the file with MetadataExtractor (no System.Drawing dependency)
    /// or from properties a platform image decoder has already read, see <see cref="FromProperties"/>.
    /// </summary>
    public class FriendlyImageMetadata
    {
        public List<MetadataEntry> Entries { get; } = [];

        private FriendlyImageMetadata() { }

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
                        string name = ExifTagMap.GetName(tagId, tag.Name);

                        if (!seen.Add(name)) continue;

                        string value = DateText.Reformat(tag.Description ?? string.Empty);
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

        /// <summary>Builds the entries from properties that were read by the platform's own image decoder.</summary>
        public static FriendlyImageMetadata FromProperties(IEnumerable<RawExifProperty> properties)
        {
            var meta = new FriendlyImageMetadata();
            var seen = new HashSet<string>();

            foreach (var property in properties)
            {
                string name = ExifTagMap.GetName(property.Id);
                if (!seen.Add(name)) continue;

                string value = DateText.Reformat(ExifValueFormatter.Format(property.Type, property.Value));
                meta.Entries.Add(new MetadataEntry(property.Id, name, value, property.Type, property.Value));
            }

            return meta;
        }
    }
}
