using System.Runtime.CompilerServices;
namespace PhotoLiker.Core
{
    public static class ImageHelperStrings
    {
        private static string T([CallerMemberName] string key = "") => Loc.Get(nameof(ImageHelperStrings), key);

        public const string UnknownTagFormat      = "Unknown (0x{0:X4})";
        public const string RationalFormat        = "{0}/{1} ({2:0.###})";
        public const string Infinity              = "∞";
        public const string AllMetadataProperty   = "All Metadata";
        public const string AllMetadataLineFormat = "{0}: {1}";
        public const string AllMetadataSeparator  = "\r\n";
        public const string CategoryVideo         = "0. Video";
        public const string CategoryGPS           = "3. GPS";
        public const string CategoryGPSDecoded    = "2. GPS Decoded";
        public const string CategoryImage         = "1. Image";
        public const string CategoryExif          = "4. EXIF";
        public const string CategoryThumbnail     = "5. Thumbnail";
        public const string CategoryUnknown       = "6. Unknown";
        public const string CategoryDebug         = "7. Debug";
        public const string UnknownPrefix         = "Unknown";
        public static string NotADateFormat => T();
        public static string NoDateShown => T();

        // GPS decoded format strings
        public const string GpsDecimalDegreesFormat = "{0:0.######}°";
        public const string GpsDmsFormat            = "{0}° {1}\' {2:\"0.##}\" {3}";
        public const string GpsCoordFormat          = "{0}, {1}";
        public const string GpsAltitudeFormat       = "{0:0.##} m {1}";
        public static string GpsAltitudeAboveSea => T();
        public static string GpsAltitudeBelowSea => T();
        public const string GpsSpeedFormat          = "{0:0.##} {1}";
        public static string GpsSpeedKmh => T();
        public static string GpsSpeedMph => T();
        public static string GpsSpeedKnots => T();
        public const string GpsTimestampFormat      = "{0} {1}";
        public const string GpsMapLinkFormat        = "https://maps.google.com/maps?q={0},{1}";
        public const string GpsLatitude             = "Latitude (decimal)";
        public const string GpsLongitude            = "Longitude (decimal)";
        public const string GpsLatitudeDMS          = "Latitude (DMS)";
        public const string GpsLongitudeDMS         = "Longitude (DMS)";
        public const string GpsCoordinates          = "Coordinates";
        public const string GpsAltitudeLabel        = "Altitude";
        public const string GpsSpeedLabel           = "Speed";
        public const string GpsDateTimeLabel        = "Date/Time (UTC)";
        public const string GpsMapLink              = "Map Link";
    }
}
