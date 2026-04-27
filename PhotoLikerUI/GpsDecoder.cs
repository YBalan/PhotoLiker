namespace PhotoLikerUI
{
    using System.ComponentModel;

    public static class GpsDecoder
    {
        // EXIF GPS tag IDs
        private const int TagLatRef   = 0x0001;
        private const int TagLat      = 0x0002;
        private const int TagLonRef   = 0x0003;
        private const int TagLon      = 0x0004;
        private const int TagAltRef   = 0x0005;
        private const int TagAlt      = 0x0006;
        private const int TagTime     = 0x0007;
        private const int TagSpeedRef = 0x000C;
        private const int TagSpeed    = 0x000D;
        private const int TagDate     = 0x001D;

        /// <summary>
        /// Decodes GPS-related <see cref="MetadataEntry"/> items from a raw entry list
        /// and returns human-readable entries in the GPS Decoded category.
        /// Returns an empty list when no GPS data is available.
        /// </summary>
        public static List<MetadataEntry> Decode(IReadOnlyList<MetadataEntry> rawEntries)
        {
            var result = new List<MetadataEntry>();

            // Build a fast lookup: tagId → MetadataEntry (with RawBytes)
            var byTag = rawEntries
                .Where(e => e.RawBytes is { Length: > 0 })
                .ToDictionary(e => e.TagId);

            double? lat = ParseDegrees(byTag, TagLat);
            double? lon = ParseDegrees(byTag, TagLon);

            if (lat is null && lon is null)
                return result;

            string latRef = ReadAscii(byTag, TagLatRef);
            string lonRef = ReadAscii(byTag, TagLonRef);

            if (lat.HasValue)
            {
                double signedLat = latRef == "S" ? -lat.Value : lat.Value;
                result.Add(Decoded(ImageHelperStrings.GpsLatitude,
                    string.Format(ImageHelperStrings.GpsDecimalDegreesFormat, signedLat)));
                result.Add(Decoded(ImageHelperStrings.GpsLatitudeDMS,
                    ToDms(signedLat, latRef is "N" or "S" ? (latRef == "N" ? "N" : "S") : (signedLat >= 0 ? "N" : "S"))));
            }

            if (lon.HasValue)
            {
                double signedLon = lonRef == "W" ? -lon.Value : lon.Value;
                result.Add(Decoded(ImageHelperStrings.GpsLongitude,
                    string.Format(ImageHelperStrings.GpsDecimalDegreesFormat, signedLon)));
                result.Add(Decoded(ImageHelperStrings.GpsLongitudeDMS,
                    ToDms(signedLon, lonRef is "E" or "W" ? lonRef : (signedLon >= 0 ? "E" : "W"))));
            }

            if (lat.HasValue && lon.HasValue)
            {
                double sLat = latRef == "S" ? -lat.Value : lat.Value;
                double sLon = lonRef == "W" ? -lon.Value : lon.Value;
                string coords = string.Format(ImageHelperStrings.GpsCoordFormat,
                    string.Format(ImageHelperStrings.GpsDecimalDegreesFormat, sLat),
                    string.Format(ImageHelperStrings.GpsDecimalDegreesFormat, sLon));
                result.Add(Decoded(ImageHelperStrings.GpsCoordinates, coords));
                result.Add(Decoded(ImageHelperStrings.GpsMapLink,
                    string.Format(ImageHelperStrings.GpsMapLinkFormat, sLat, sLon),
                    [new EditorAttribute(typeof(UrlLauncherEditor), typeof(System.Drawing.Design.UITypeEditor))]));
            }

            // Altitude
            double? alt = ParseRational(byTag, TagAlt);
            if (alt.HasValue)
            {
                int altRef = byTag.TryGetValue(TagAltRef, out var aRefEntry) && aRefEntry.RawBytes?.Length > 0
                    ? aRefEntry.RawBytes[0] : 0;
                string altLabel = altRef == 1
                    ? ImageHelperStrings.GpsAltitudeBelowSea
                    : ImageHelperStrings.GpsAltitudeAboveSea;
                result.Add(Decoded(ImageHelperStrings.GpsAltitudeLabel,
                    string.Format(ImageHelperStrings.GpsAltitudeFormat, alt.Value, altLabel)));
            }

            // Speed
            double? speed = ParseRational(byTag, TagSpeed);
            if (speed.HasValue)
            {
                string speedRef = ReadAscii(byTag, TagSpeedRef);
                string unit = speedRef switch
                {
                    "M" => ImageHelperStrings.GpsSpeedMph,
                    "N" => ImageHelperStrings.GpsSpeedKnots,
                    _   => ImageHelperStrings.GpsSpeedKmh,
                };
                result.Add(Decoded(ImageHelperStrings.GpsSpeedLabel,
                    string.Format(ImageHelperStrings.GpsSpeedFormat, speed.Value, unit)));
            }

            // Date/Time UTC
            string date = ReadAscii(byTag, TagDate);
            double? h = ParseRationalAt(byTag, TagTime, 0);
            double? m = ParseRationalAt(byTag, TagTime, 1);
            double? s = ParseRationalAt(byTag, TagTime, 2);
            if (h.HasValue && m.HasValue && s.HasValue)
            {
                string time = $"{(int)h.Value:D2}:{(int)m.Value:D2}:{s.Value:00.##}";
                string datetime = string.IsNullOrWhiteSpace(date)
                    ? time
                    : string.Format(ImageHelperStrings.GpsTimestampFormat,
                        date.Replace(':', '-'), time);
                result.Add(Decoded(ImageHelperStrings.GpsDateTimeLabel, datetime));
            }

            return result;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static MetadataEntry Decoded(string name, string value, Attribute[]? extraAttributes = null) =>
            new(0, name, value, ExtraAttributes: extraAttributes);

        private static string ToDms(double decimalDeg, string direction)
        {
            double abs = Math.Abs(decimalDeg);
            int deg    = (int)abs;
            int min    = (int)((abs - deg) * 60);
            double sec = ((abs - deg) * 60 - min) * 60;
            return string.Format(ImageHelperStrings.GpsDmsFormat, deg, min, sec, direction);
        }

        private static string ReadAscii(Dictionary<int, MetadataEntry> entries, int tagId)
        {
            if (!entries.TryGetValue(tagId, out var e) || e.RawBytes is null) return string.Empty;
            return System.Text.Encoding.ASCII.GetString(e.RawBytes).Trim('\0', ' ');
        }

        /// <summary>Reads the first rational value (8 bytes) from a GPS tag.</summary>
        private static double? ParseRational(Dictionary<int, MetadataEntry> entries, int tagId) =>
            ParseRationalAt(entries, tagId, 0);

        /// <summary>Reads rational at <paramref name="index"/> (each rational is 8 bytes).</summary>
        private static double? ParseRationalAt(Dictionary<int, MetadataEntry> entries, int tagId, int index)
        {
            if (!entries.TryGetValue(tagId, out var e) || e.RawBytes is null) return null;
            int offset = index * 8;
            if (e.RawBytes.Length < offset + 8) return null;
            uint num = BitConverter.ToUInt32(e.RawBytes, offset);
            uint den = BitConverter.ToUInt32(e.RawBytes, offset + 4);
            return den == 0 ? null : (double)num / den;
        }

        /// <summary>Reads three rationals (deg, min, sec) and converts to decimal degrees.</summary>
        private static double? ParseDegrees(Dictionary<int, MetadataEntry> entries, int tagId)
        {
            double? deg = ParseRationalAt(entries, tagId, 0);
            double? min = ParseRationalAt(entries, tagId, 1);
            double? sec = ParseRationalAt(entries, tagId, 2);
            if (deg is null || min is null || sec is null) return null;
            return deg.Value + min.Value / 60.0 + sec.Value / 3600.0;
        }
    }
}
