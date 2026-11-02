using System.Runtime.CompilerServices;

namespace PhotoLiker.Core
{
    /// <summary>
    /// What a row of the metadata grid means, in a sentence or two for the help pane under the grid: one for every tag that
    /// <see cref="ExifTagMap"/> has a name for, for the rows decoded from the GPS tags, for the debug row, and a general one for tags
    /// that have no name. Numbers that a tag stores as codes are explained, since the grid shows them as they are. The texts are in the
    /// resources (<c>MetadataDescriptions.Tag0100</c> is the one for tag 0x0100), in the language that is on; the names of the rows
    /// they speak of ('Date Taken (Original)') stay English, as the grid shows them.
    /// </summary>
    public static class MetadataDescriptions
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(MetadataDescriptions), key);

        public static string AllMetadata => T();

        private static string UnknownTagFormat => T();

        private static string NamedByReaderFormat => T();

        /// <summary>The description of a raw metadata entry (a row of the EXIF, Image, Thumbnail, GPS or Unknown category).</summary>
        public static string For(MetadataEntry entry)
        {
            if (TagIds.Contains(entry.TagId) && ExifTagMap.Tags.ContainsKey(entry.TagId))
                return OfTag(entry.TagId);

            return entry.Name.StartsWith(ImageHelperStrings.UnknownPrefix, StringComparison.Ordinal)
                ? string.Format(UnknownTagFormat, entry.TagId)
                : string.Format(NamedByReaderFormat, entry.Name, entry.TagId);
        }

        /// <summary>The description of a row of the "GPS Decoded" category, by its name; null for a name that is not one of those.</summary>
        public static string? ForDecoded(string name) => DecodedKeys.TryGetValue(name, out var key) ? Loc.Get(nameof(MetadataDescriptions), key) : null;

        /// <summary>The description of every row of the "GPS Decoded" category, by its name, in the language that is on.</summary>
        public static IReadOnlyDictionary<string, string> Decoded =>
            DecodedKeys.ToDictionary(pair => pair.Key, pair => Loc.Get(nameof(MetadataDescriptions), pair.Value));

        /// <summary>The description of every tag that <see cref="ExifTagMap.Tags"/> names, by tag id, in the language that is on.</summary>
        public static IReadOnlyDictionary<int, string> Tags => TagIds.ToDictionary(id => id, OfTag);

        private static string OfTag(int tagId) => Loc.Get(nameof(MetadataDescriptions), $"Tag{tagId:X4}");

        // Which texts there are: the order is the one of the resource files
        private static readonly int[] TagIds =
        [
            ExifTagMap.FileDateTagId,
            0x0100,
            0x0101,
            0x010F,
            0x0110,
            0x0112,
            0x011A,
            0x011B,
            0x0128,
            0x0131,
            0x0132,
            0x0201,
            0x0202,
            0x0213,
            0x0000,
            0x0001,
            0x0002,
            0x0003,
            0x0004,
            0x0005,
            0x0006,
            0x0007,
            0x000C,
            0x000D,
            0x001B,
            0x001D,
            0x829A,
            0x829D,
            0x8822,
            0x8827,
            0x8830,
            0x8832,
            0x8895,
            0x889A,
            0x889D,
            0x889E,
            0x889F,
            0x88A3,
            0x8773,
            0x9000,
            0x9003,
            0x9004,
            0x9010,
            0x9011,
            0x9012,
            0x9101,
            0x9201,
            0x9202,
            0x9203,
            0x9204,
            0x9205,
            0x9207,
            0x9208,
            0x9209,
            0x920A,
            0x927C,
            0x9290,
            0x9291,
            0x9292,
            0x9999,
            0xA000,
            0xA001,
            0xA002,
            0xA003,
            0xA402,
            0xA403,
            0xA404,
            0xA405,
            0xA406,
            0xA408,
            0xA409,
            0xA40A,
            0xA20E,
            0xA20F,
            0xA210,
            0xA401,
            0xA661,
            0x5023,
            0x5026,
            0x5027,
            0x5029,
            0x502D,
            0x502E,
            0x5030,
            0x5033,
            0x5039,
            0x5041,
            0x5042,
            0x5090,
            0x5091,
            0x501B
        ];

        private static readonly Dictionary<string, string> DecodedKeys = new()
        {
            [ImageHelperStrings.GpsLatitude] = nameof(DecodedGpsLatitude),
            [ImageHelperStrings.GpsLongitude] = nameof(DecodedGpsLongitude),
            [ImageHelperStrings.GpsLatitudeDMS] = nameof(DecodedGpsLatitudeDMS),
            [ImageHelperStrings.GpsLongitudeDMS] = nameof(DecodedGpsLongitudeDMS),
            [ImageHelperStrings.GpsCoordinates] = nameof(DecodedGpsCoordinates),
            [ImageHelperStrings.GpsAltitudeLabel] = nameof(DecodedGpsAltitudeLabel),
            [ImageHelperStrings.GpsSpeedLabel] = nameof(DecodedGpsSpeedLabel),
            [ImageHelperStrings.GpsDateTimeLabel] = nameof(DecodedGpsDateTimeLabel),
            [ImageHelperStrings.GpsMapLink] = nameof(DecodedGpsMapLink)
        };

        private static string DecodedGpsLatitude => T();
        private static string DecodedGpsLongitude => T();
        private static string DecodedGpsLatitudeDMS => T();
        private static string DecodedGpsLongitudeDMS => T();
        private static string DecodedGpsCoordinates => T();
        private static string DecodedGpsAltitudeLabel => T();
        private static string DecodedGpsSpeedLabel => T();
        private static string DecodedGpsDateTimeLabel => T();
        private static string DecodedGpsMapLink => T();
    }
}
