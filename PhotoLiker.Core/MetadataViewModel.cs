namespace PhotoLiker.Core
{
    public class MetadataViewModel
    {
        public IReadOnlyList<MetadataEntry> Entries { get; }
        public IReadOnlyList<MetadataEntry> GpsDecoded { get; }
        public string? MapLink => GpsDecoded.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsMapLink)?.Value;

        public MetadataViewModel(FriendlyImageMetadata meta)
        {
            Entries    = meta.Entries;
            GpsDecoded = GpsDecoder.Decode(meta.Entries);
        }

        /// <summary>Flat list of all entries grouped and labelled, suitable for a MAUI CollectionView.</summary>
        public IReadOnlyList<MetadataGroup> GetGroups()
        {
            var dict = new Dictionary<string, List<MetadataEntry>>();

            foreach (var e in Entries)
            {
                string cat = e.Name.StartsWith(ImageHelperStrings.UnknownPrefix)
                    ? ImageHelperStrings.CategoryUnknown
                    : ExifTagMap.GetCategory(e.TagId).Name;
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
