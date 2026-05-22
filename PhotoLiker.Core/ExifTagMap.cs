namespace PhotoLiker.Core
{
    public record struct ExifCategoryInfo(int TagId, string Name, bool IsBrowsable);

    public static class ExifTagMap
    {
        public static ExifCategoryInfo GetCategory(int tagId) => tagId switch
        {
            < 0x0100 => new ExifCategoryInfo(tagId, ImageHelperStrings.CategoryGPS, IsBrowsable: false),
            < 0x5000 => new ExifCategoryInfo(tagId, ImageHelperStrings.CategoryImage, IsBrowsable: true),
            < 0x6000 => new ExifCategoryInfo(tagId, ImageHelperStrings.CategoryThumbnail, IsBrowsable: true),
            _        => new ExifCategoryInfo(tagId, ImageHelperStrings.CategoryExif, IsBrowsable: true),
        };

        public static readonly Dictionary<int, string> Tags = new()
        {
            // TIFF baseline
            [0x0100] = "Image Width",
            [0x0101] = "Image Length",
            [0x010F] = "Make",
            [0x0110] = "Model",
            [0x0112] = "Orientation",
            [0x011A] = "X Resolution",
            [0x011B] = "Y Resolution",
            [0x0128] = "Resolution Unit",
            [0x0131] = "Software",
            [0x0132] = "Date Taken",
            [0x0201] = "JPEG Thumbnail Offset",
            [0x0202] = "JPEG Thumbnail Length",
            [0x0213] = "YCbCr Positioning",

            // GPS IFD
            [0x0000] = "GPS Version ID",
            [0x0001] = "GPS Latitude Ref",
            [0x0002] = "GPS Latitude",
            [0x0003] = "GPS Longitude Ref",
            [0x0004] = "GPS Longitude",
            [0x0005] = "GPS Altitude Ref",
            [0x0006] = "GPS Altitude",
            [0x0007] = "GPS Time Stamp",
            [0x000C] = "GPS Speed Ref",
            [0x000D] = "GPS Speed",
            [0x001B] = "GPS Processing Method",
            [0x001D] = "GPS Date Stamp",

            // EXIF IFD
            [0x829A] = "Exposure Time",
            [0x829D] = "F-Number",
            [0x8822] = "Exposure Program",
            [0x8827] = "ISO Speed",
            [0x8830] = "Sensitivity Type",
            [0x8832] = "Recommended Exposure Index",
            [0x9000] = "Exif Version",
            [0x9003] = "Date/Time Original",
            [0x9004] = "Date/Time Digitized",
            [0x9201] = "Shutter Speed",
            [0x9202] = "Aperture Value",
            [0x9203] = "Brightness Value",
            [0x9204] = "Exposure Bias",
            [0x9205] = "Max Aperture",
            [0x9207] = "Metering Mode",
            [0x9208] = "Light Source",
            [0x9209] = "Flash",
            [0x920A] = "Focal Length",
            [0xA002] = "Pixel X Dimension",
            [0xA003] = "Pixel Y Dimension",
            [0xA20E] = "Focal Plane X Resolution",
            [0xA20F] = "Focal Plane Y Resolution",
            [0xA210] = "Focal Plane Resolution Unit",
            [0xA401] = "Custom Rendered",
            [0xA402] = "Exposure Mode",
            [0xA403] = "White Balance",
            [0xA404] = "Digital Zoom Ratio",
            [0xA405] = "Focal Length In 35mm Film",
            [0xA406] = "Scene Capture Type",
            [0xA408] = "Contrast",
            [0xA409] = "Saturation",
            [0xA40A] = "Sharpness",
        };
    }
}
