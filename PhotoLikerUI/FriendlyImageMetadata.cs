namespace PhotoLikerUI
{
    using System.Drawing.Imaging;

    public class FriendlyImageMetadata
    {
        public List<MetadataEntry> Entries { get; } = [];

        public FriendlyImageMetadata(Image image)
        {
            var seen = new HashSet<string>();
            foreach (var prop in image.PropertyItems)
            {
                string name = ExifTagMap.Tags.TryGetValue(prop.Id, out var friendly)
                    ? friendly
                    : string.Format(ImageHelperStrings.UnknownTagFormat, prop.Id);

                if (!seen.Add(name)) continue;

                string value = ParseExifValue(prop);
                Entries.Add(new MetadataEntry(prop.Id, name, value, prop.Type, prop.Value));
            }
        }

        private static string ParseExifValue(PropertyItem? item)
        {
            if (item is null) return string.Empty;
            var itemValue = item.Value ?? [];
            try
            {
                return item.Type switch
                {
                    2  => System.Text.Encoding.ASCII.GetString(itemValue).Trim('\0'), // ASCII
                    3  => BitConverter.ToUInt16(itemValue, 0).ToString(),             // Short
                    4  => BitConverter.ToUInt32(itemValue, 0).ToString(),             // Long
                    5  => GetRational(item),                                           // Rational
                    10 => GetSRational(item),                                          // SRational
                    _  => BitConverter.ToString(itemValue)
                };
            }
            catch
            {
                return BitConverter.ToString(itemValue);
            }
        }

        private static string GetRational(PropertyItem? item)
        {
            if (item is null) return string.Empty;
            var itemValue = item.Value ?? [];
            uint num = BitConverter.ToUInt32(itemValue, 0);
            uint den = BitConverter.ToUInt32(itemValue, 4);
            if (den == 0) return ImageHelperStrings.Infinity;
            return string.Format(ImageHelperStrings.RationalFormat, num, den, (double)num / den);
        }

        private static string GetSRational(PropertyItem? item)
        {
            if (item is null) return string.Empty;
            var itemValue = item.Value ?? [];
            int num = BitConverter.ToInt32(itemValue, 0);
            int den = BitConverter.ToInt32(itemValue, 4);
            if (den == 0) return ImageHelperStrings.Infinity;
            return string.Format(ImageHelperStrings.RationalFormat, num, den, (double)num / den);
        }
    }
}
