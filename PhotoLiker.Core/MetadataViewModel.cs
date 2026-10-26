namespace PhotoLiker.Core
{
    public class MetadataViewModel
    {
        public IReadOnlyList<MetadataEntry> Entries { get; }
        public IReadOnlyList<MetadataEntry> GpsDecoded { get; }
        public string? MapLink => GpsDecoded.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsMapLink)?.Value;

        /// <summary>Every raw entry as one "Name: Value" line, e.g. for a debug view or the clipboard.</summary>
        public string AllEntriesText => string.Join(
            ImageHelperStrings.AllMetadataSeparator,
            Entries.Select(e => string.Format(ImageHelperStrings.AllMetadataLineFormat, e.Name, e.Value)));

        public MetadataViewModel(FriendlyImageMetadata meta)
            : this(meta.Entries) { }

        public MetadataViewModel(IReadOnlyList<MetadataEntry> entries)
        {
            Entries    = entries;
            GpsDecoded = GpsDecoder.Decode(entries);
        }

        /// <summary>Flat list of all entries grouped and labelled, suitable for a MAUI CollectionView.</summary>
        public IReadOnlyList<MetadataGroup> GetGroups()
        {
            var dict = new Dictionary<string, List<MetadataEntry>>();

            foreach (var e in Entries)
            {
                string cat = ExifTagMap.GetCategory(e).Name;
                if (!dict.TryGetValue(cat, out var list))
                    dict[cat] = list = [];
                list.Add(e);
            }

            foreach (var e in GpsDecoded)
            {
                if (!dict.TryGetValue(ImageHelperStrings.CategoryGPSDecoded, out var list))
                    dict[ImageHelperStrings.CategoryGPSDecoded] = list = [];
                list.Add(e);
            }

            return dict
                .OrderBy(kv => kv.Key)
                .Select(kv => new MetadataGroup(kv.Key, kv.Value))
                .ToList();
        }
    }

    public class MetadataGroup(string title, IReadOnlyList<MetadataEntry> items)
    {
        public string Title { get; } = title;
        public IReadOnlyList<MetadataEntry> Items { get; } = items;
    }
}
