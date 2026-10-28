namespace PhotoLiker.Core
{
    /// <summary>
    /// What a row of the metadata grid means, in a sentence or two for the help pane under the grid: one for every tag that
    /// <see cref="ExifTagMap"/> has a name for, for the rows decoded from the GPS tags, for the debug row, and a general one for tags
    /// that have no name. Numbers that a tag stores as codes are explained, since the grid shows them as they are.
    /// </summary>
    public static class MetadataDescriptions
    {
        public const string AllMetadata =
            "Every metadata tag of the photo as 'Name: value' lines, the ones that have a row of their own and the ones that are not shown. Click the '...' button to read it in a window or copy it.";

        private const string UnknownTagFormat =
            "A tag that PhotoLiker has no name for (0x{0:X4}). Its value is shown as the metadata reader gives it. Most of these are private data of the camera maker.";

        private const string NamedByReaderFormat =
            "A tag of the photo (0x{1:X4}) that PhotoLiker has no description for; '{0}' is the name the metadata reader gives it, and the value is shown as it is read from the file.";

        /// <summary>The description of a raw metadata entry (a row of the EXIF, Image, Thumbnail, GPS or Unknown category).</summary>
        public static string For(MetadataEntry entry)
        {
            if (Tags.TryGetValue(entry.TagId, out var description) && ExifTagMap.Tags.ContainsKey(entry.TagId))
                return description;

            return entry.Name.StartsWith(ImageHelperStrings.UnknownPrefix, StringComparison.Ordinal)
                ? string.Format(UnknownTagFormat, entry.TagId)
                : string.Format(NamedByReaderFormat, entry.Name, entry.TagId);
        }

        /// <summary>The description of a row of the "GPS Decoded" category, by its name; null for a name that is not one of those.</summary>
        public static string? ForDecoded(string name) => Decoded.GetValueOrDefault(name);

        public static readonly IReadOnlyDictionary<string, string> Decoded = new Dictionary<string, string>
        {
            [ImageHelperStrings.GpsLatitude] = "Where the photo was taken, north-south, as decimal degrees: positive north of the equator, negative south. Worked out from the raw GPS tags.",
            [ImageHelperStrings.GpsLongitude] = "Where the photo was taken, east-west, as decimal degrees: positive east of Greenwich, negative west. Worked out from the raw GPS tags.",
            [ImageHelperStrings.GpsLatitudeDMS] = "The latitude as degrees, minutes and seconds with N (north) or S (south), the way maps and navigation devices write it.",
            [ImageHelperStrings.GpsLongitudeDMS] = "The longitude as degrees, minutes and seconds with E (east) or W (west), the way maps and navigation devices write it.",
            [ImageHelperStrings.GpsCoordinates] = "Latitude and longitude in decimal degrees, ready to paste into a map or a search box.",
            [ImageHelperStrings.GpsAltitudeLabel] = "Height of the place in metres, above or below sea level, as measured by the GPS (it can be off by tens of metres).",
            [ImageHelperStrings.GpsSpeedLabel] = "How fast the camera was moving when the photo was taken, in km/h, mph or knots as the camera says.",
            [ImageHelperStrings.GpsDateTimeLabel] = "Date and time of the GPS fix in UTC. The GPS clock is exact, so this shows the true time of shooting when the camera clock is wrong (but it is not local time).",
            [ImageHelperStrings.GpsMapLink] = "A link that shows the place on Google Maps. Click the '...' button to open it in the browser (also the Maps link at the right of the status bar).",
        };

        /// <summary>The description of every tag that <see cref="ExifTagMap.Tags"/> names, by tag id.</summary>
        public static readonly IReadOnlyDictionary<int, string> Tags = new Dictionary<int, string>
        {
            [ExifTagMap.FileDateTagId] = "The date of the file on the disk (created, and modified if different), not of the photo. '= Date Taken (Original)' means it is the time of shooting that the camera wrote into the photo, '≠ Date Taken (Original)' that it is not: a copied file gets the day of the copy. The 'Align dates' button of the toolbar sets it right.",

            // TIFF baseline
            [0x0100] = "Width of the photo in pixels, as written into the file header.",
            [0x0101] = "Height of the photo in pixels (EXIF calls the height 'length').",
            [0x010F] = "The company that made the camera or phone.",
            [0x0110] = "The model of the camera or phone.",
            [0x0112] = "Tells viewers how to turn the stored pixels to show the photo upright: 1 = not at all, 3 = 180 degrees, 6 = 90 degrees clockwise, 8 = 90 degrees counter-clockwise (2, 4, 5, 7 are the mirrored versions).",
            [0x011A] = "Pixels per resolution unit across the photo (usually 72 or 300). It only matters for printing, not for how big the photo is on the screen.",
            [0x011B] = "Pixels per resolution unit down the photo (usually 72 or 300). It only matters for printing.",
            [0x0128] = "The unit of X Resolution and Y Resolution: 2 = inch, 3 = centimetre.",
            [0x0131] = "The program that made or last saved the file: the firmware of the camera, or an editor such as Photoshop.",
            [0x0132] = "When the photo was last saved by the camera or a program (EXIF 'DateTime'), no time zone. A photo that was turned or edited later, in Windows Photo Viewer for one, shows that day here; the time of shooting is 'Date Taken (Original)'.",
            [0x0201] = "Where the small preview picture (thumbnail) starts inside the EXIF block, in bytes.",
            [0x0202] = "Size of the small preview picture (thumbnail) in bytes.",
            [0x0213] = "Where the colour samples sit relative to the brightness samples in the JPEG (1 = centred, 2 = co-sited). A technical detail with no visible effect.",

            // GPS IFD
            [0x0000] = "Version of the GPS block of the photo, for example 2.2.0.0.",
            [0x0001] = "N or S: whether the latitude is north or south of the equator.",
            [0x0002] = "Latitude of the place as degrees, minutes and seconds (without the direction; see 'GPS Latitude Ref'). 'GPS Decoded' shows it as one number.",
            [0x0003] = "E or W: whether the longitude is east or west of Greenwich.",
            [0x0004] = "Longitude of the place as degrees, minutes and seconds (without the direction; see 'GPS Longitude Ref'). 'GPS Decoded' shows it as one number.",
            [0x0005] = "Whether the altitude is above (0) or below (1) sea level.",
            [0x0006] = "Height of the place in metres, from the GPS (see 'GPS Altitude Ref' for above or below sea level).",
            [0x0007] = "The UTC time of the GPS fix: hours, minutes, seconds.",
            [0x000C] = "The unit of the GPS speed: K = km/h, M = mph, N = knots.",
            [0x000D] = "How fast the camera was moving, in the unit of 'GPS Speed Ref'.",
            [0x001B] = "How the position was found: GPS (satellites), NETWORK (mobile towers or Wi-Fi), FUSED or MANUAL (typed in).",
            [0x001D] = "The UTC date of the GPS fix.",

            // EXIF IFD
            [0x829A] = "How long the shutter was open, in seconds (1/250 is a 250th of a second). A longer time lets in more light but blurs movement.",
            [0x829D] = "The opening of the lens as an f-number (f/2.8). The smaller the number the wider the opening: more light, and a blurrier background.",
            [0x8822] = "How the camera chose the exposure: 1 = manual, 2 = normal program, 3 = aperture priority, 4 = shutter priority, 5 = creative, 6 = action, 7 = portrait, 8 = landscape.",
            [0x8827] = "The sensitivity of the sensor (ISO 100, 800, ...). A higher value gives a brighter photo in poor light, with more noise.",
            [0x8830] = "Tells which of the sensitivity values in the photo was used: standard output sensitivity, recommended exposure index or ISO speed.",
            [0x8832] = "The ISO value the camera recommends for this exposure; used when 'Sensitivity Type' says so.",
            [0x8895] = "What the camera recorded about the lens: the range of focal lengths and apertures it covers.",
            [0x889A] = "The name of the owner of the camera as it was set in the camera or phone.",
            [0x889D] = "Location data written by the camera app of a Xiaomi phone, in a private format of the maker. Shown as stored.",
            [0x889E] = "Camera information written by the camera app of a Xiaomi phone, in a private format of the maker. Shown as stored.",
            [0x889F] = "Extra data written by the camera app of a Xiaomi phone, in a private format of the maker. Shown as stored.",
            [0x88A3] = "AI-related data written by the camera app of a Xiaomi phone, in a private format of the maker. Shown as stored.",
            [0x8773] = "The colour profile embedded in the photo, so that programs show its colours right (sRGB, Adobe RGB, ...). Shown as raw bytes.",
            [0x9000] = "The version of the EXIF standard the camera followed: 0230 means 2.30.",
            [0x9003] = "When the picture was taken, by the camera clock (no time zone). PhotoLiker uses it as the date of the photo (search by date, PageUp / PageDown) unless it is not believable.",
            [0x9004] = "When the picture was stored as a digital file. For a digital camera the same as when it was taken; for a scan of a film photo, the time of the scan.",
            [0x9010] = "The time zone of 'Date Last Saved' as the difference from UTC, for example +02:00.",
            [0x9011] = "The time zone of the time of shooting ('Date Taken (Original)') as the difference from UTC, for example +02:00.",
            [0x9012] = "The time zone of 'Date Time Digitized' as the difference from UTC, for example +02:00.",
            [0x9101] = "Which colour channels the JPEG image data holds, and in what order (normally Y, Cb, Cr). No visible effect.",
            [0x9201] = "The shutter speed as an APEX number (a log2 scale: 8 is 1/256 s). 'Exposure Time' says the same in seconds.",
            [0x9202] = "The opening of the lens as an APEX number (a log2 scale: 3 is about f/2.8). 'F-Number' says the same as an f-number.",
            [0x9203] = "How bright the scene was, as the camera measured it (APEX units).",
            [0x9204] = "How much brighter (+) or darker (-) than its own choice the camera was told to expose, in EV steps. The 'exposure compensation' dial.",
            [0x9205] = "The widest opening the lens has at the zoom setting used (APEX units).",
            [0x9207] = "How the camera measured the light: 1 = average, 2 = centre-weighted, 3 = spot, 4 = multi-spot, 5 = pattern (matrix), 6 = partial.",
            [0x9208] = "The kind of light the camera assumed or found: 0 = unknown, 1 = daylight, 2 = fluorescent, 3 = tungsten, 4 = flash, 9 = fine weather, 10 = cloudy, 11 = shade.",
            [0x9209] = "Whether the flash fired: 0 = no, 1 = yes, 16 = flash off, 24 = auto and it did not fire, 25 = auto and it fired (other values add red-eye reduction and return-light details).",
            [0x920A] = "The focal length of the lens in millimetres when the photo was taken. A smaller number is a wider view, a larger one zooms in.",
            [0x927C] = "Private data of the camera maker (settings, focus points, lens, ...), in a different format for every brand. Shown as raw bytes.",
            [0x9290] = "Fractions of a second for 'Date Last Saved'.",
            [0x9291] = "Fractions of a second for the time of shooting ('Date Taken (Original)').",
            [0x9292] = "Fractions of a second for 'Date Time Digitized'.",
            [0x9999] = "Scene settings written by the camera app of a Xiaomi phone, in a private format of the maker. Shown as stored.",
            [0xA000] = "The version of the FlashPix format the file is compatible with (always 0100).",
            [0xA001] = "The colour space of the photo: 1 = sRGB, 65535 = not specified (often Adobe RGB; see the ICC profile).",
            [0xA002] = "Width of the photo in pixels, as the camera recorded it for the image data (EXIF 'PixelXDimension').",
            [0xA003] = "Height of the photo in pixels, as the camera recorded it for the image data (EXIF 'PixelYDimension').",
            [0xA402] = "How the exposure was set: 0 = automatic, 1 = manual, 2 = auto bracketing (several photos with different exposures).",
            [0xA403] = "How the colours were balanced for the light: 0 = automatic, 1 = set by hand.",
            [0xA404] = "How much digital zoom was used (the picture is cropped and enlarged). 1 or 0 means none.",
            [0xA405] = "The focal length as it would be on a 35 mm film camera, to compare cameras: about 50 is a normal view, less is wider, more is a telephoto.",
            [0xA406] = "The type of scene the camera was set to: 0 = standard, 1 = landscape, 2 = portrait, 3 = night.",
            [0xA408] = "The contrast setting of the camera: 0 = normal, 1 = low (soft), 2 = high (hard).",
            [0xA409] = "The colour saturation setting of the camera: 0 = normal, 1 = low, 2 = high.",
            [0xA40A] = "The sharpness setting of the camera: 0 = normal, 1 = soft, 2 = hard.",
            [0xA20E] = "Pixels per unit across the width of the sensor; with the unit it tells how big the sensor is.",
            [0xA20F] = "Pixels per unit along the height of the sensor; with the unit it tells how big the sensor is.",
            [0xA210] = "The unit of the focal plane resolution: 2 = inch, 3 = centimetre, 4 = millimetre.",
            [0xA401] = "Whether the camera processed the picture specially: 0 = normal, 1 = custom (for example an HDR or beauty mode of a phone).",
            [0xA661] = "More data written by the camera app of a Xiaomi phone, in a private format of the maker. Shown as stored.",

            // Thumbnail / Interoperability
            [0x5023] = "How the small preview picture (thumbnail) inside the file is stored, for example as JPEG.",
            [0x5026] = "The maker of the camera, as recorded for the preview picture.",
            [0x5027] = "The model of the camera, as recorded for the preview picture.",
            [0x5029] = "How the camera was held, as recorded for the preview picture (see 'Orientation').",
            [0x502D] = "Pixels per unit across the preview picture. Only matters for printing.",
            [0x502E] = "Pixels per unit down the preview picture. Only matters for printing.",
            [0x5030] = "The unit of the resolution of the preview picture: 2 = inch, 3 = centimetre.",
            [0x5033] = "The date and time recorded for the preview picture.",
            [0x5039] = "How the preview picture is compressed (6 means JPEG).",
            [0x5041] = "Which compatibility rules the file follows: R98 = standard EXIF with sRGB colours, R03 = Adobe RGB, THM = a thumbnail file.",
            [0x5042] = "The version of those compatibility rules, for example 0100.",
            [0x5090] = "Numbers for converting the stored YCbCr colours of the JPEG into RGB. A technical detail with no effect on how the photo looks.",
            [0x5091] = "Where the colour samples sit relative to the brightness samples in the preview picture. A technical detail.",
            [0x501B] = "The picture data of the small preview picture itself, as raw bytes.",
        };
    }
}
