using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>
/// Tests for GpsDecoder using raw EXIF byte encoding, matching what a real camera embeds.
/// Rational bytes are two consecutive little-endian uint32 values: numerator then denominator.
/// </summary>
public class GpsDecoderTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] ToRationalBytes(params (uint num, uint den)[] rationals)
    {
        var bytes = new byte[rationals.Length * 8];
        for (int i = 0; i < rationals.Length; i++)
        {
            BitConverter.GetBytes(rationals[i].num).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(rationals[i].den).CopyTo(bytes, i * 8 + 4);
        }
        return bytes;
    }

    private static byte[] ToAsciiBytes(string s) =>
        System.Text.Encoding.ASCII.GetBytes(s + "\0");

    private static MetadataEntry GpsEntry(int tagId, byte[] raw) =>
        new(tagId, $"GPS_{tagId:X4}", string.Empty, RawBytes: raw);

    // latitude: degrees=48, minutes=51, seconds=29.69  →  48.858247°
    private static readonly byte[] LatBytes =
        ToRationalBytes((48, 1), (51, 1), (29690, 1000));

    // longitude: degrees=2, minutes=17, seconds=40.20  →  2.294500°
    private static readonly byte[] LonBytes =
        ToRationalBytes((2, 1), (17, 1), (4020, 100));

    // Altitude: 35 m (35/1)
    private static readonly byte[] AltBytes = ToRationalBytes((35, 1));

    // Speed: 120 km/h
    private static readonly byte[] SpeedBytes = ToRationalBytes((120, 1));

    // GPS time: 10:30:45.5
    private static readonly byte[] TimeBytes =
        ToRationalBytes((10, 1), (30, 1), (455, 10));

    // ── No GPS data ─────────────────────────────────────────────────────────

    [Fact]
    public void Decode_EmptyInput_ReturnsEmpty()
    {
        var result = GpsDecoder.Decode([]);

        Assert.Empty(result);
    }

    [Fact]
    public void Decode_NoCoordinates_ReturnsEmpty()
    {
        // Entry with no raw bytes — coordinate parsing should yield nothing
        var entries = new List<MetadataEntry> { new(0x0001, "LatRef", "N") };

        var result = GpsDecoder.Decode(entries);

        Assert.Empty(result);
    }

    // ── North / East (positive) ──────────────────────────────────────────

    [Fact]
    public void Decode_NorthEast_ProducesPositiveDecimalDegrees()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, LonBytes),
        };

        var result = GpsDecoder.Decode(entries);

        var lat = result.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsLatitude);
        var lon = result.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsLongitude);

        Assert.NotNull(lat);
        Assert.NotNull(lon);
        Assert.Contains("48.", lat!.Value);   // positive latitude
        Assert.Contains("2.",  lon!.Value);   // positive longitude
        Assert.DoesNotContain("-", lat.Value);
        Assert.DoesNotContain("-", lon.Value);
    }

    // ── South / West (negative) ──────────────────────────────────────────

    [Fact]
    public void Decode_SouthWest_ProducesNegativeDecimalDegrees()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("S")),
            GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("W")),
            GpsEntry(0x0004, LonBytes),
        };

        var result = GpsDecoder.Decode(entries);

        var lat = result.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsLatitude);
        var lon = result.FirstOrDefault(e => e.Name == ImageHelperStrings.GpsLongitude);

        Assert.NotNull(lat);
        Assert.NotNull(lon);
        Assert.StartsWith("-", lat!.Value);
        Assert.StartsWith("-", lon!.Value);
    }

    // ── DMS format ──────────────────────────────────────────────────────────

    [Fact]
    public void Decode_LatLon_ProducesDmsEntries()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, LonBytes),
        };

        var result = GpsDecoder.Decode(entries);

        Assert.Contains(result, e => e.Name == ImageHelperStrings.GpsLatitudeDMS);
        Assert.Contains(result, e => e.Name == ImageHelperStrings.GpsLongitudeDMS);
    }

    [Fact]
    public void Decode_DmsLatitude_ContainsDegreeMinuteSecondAndDirection()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, LonBytes),
        };

        var dms = GpsDecoder.Decode(entries)
            .First(e => e.Name == ImageHelperStrings.GpsLatitudeDMS).Value;

        Assert.Contains("48",  dms);  // degrees
        Assert.Contains("51",  dms);  // minutes
        Assert.Contains("N",   dms);  // direction
    }

    // ── Coordinates and map link ─────────────────────────────────────────

    [Fact]
    public void Decode_LatLon_ProducesCoordinatesEntry()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, LonBytes),
        };

        var coords = GpsDecoder.Decode(entries)
            .FirstOrDefault(e => e.Name == ImageHelperStrings.GpsCoordinates);

        Assert.NotNull(coords);
    }

    [Fact]
    public void Decode_LatLon_ProducesGoogleMapsLink()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, LonBytes),
        };

        var link = GpsDecoder.Decode(entries)
            .FirstOrDefault(e => e.Name == ImageHelperStrings.GpsMapLink);

        Assert.NotNull(link);
        Assert.StartsWith("https://maps.google.com/maps?q=", link!.Value);
        Assert.Equal(new[] { "MapLink" }, link.ExtraAttributeTypes);
    }

    // ── Altitude ────────────────────────────────────────────────────────────

    [Fact]
    public void Decode_AltitudeAboveSeaLevel_ProducesAltitudeEntry()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")), GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")), GpsEntry(0x0004, LonBytes),
            GpsEntry(0x0005, [0]),               // altRef = 0 → above sea
            GpsEntry(0x0006, AltBytes),
        };

        var alt = GpsDecoder.Decode(entries)
            .FirstOrDefault(e => e.Name == ImageHelperStrings.GpsAltitudeLabel);

        Assert.NotNull(alt);
        Assert.Contains("35", alt!.Value);
        Assert.Contains(ImageHelperStrings.GpsAltitudeAboveSea, alt.Value);
    }

    [Fact]
    public void Decode_AltitudeBelowSeaLevel_ContainsBelowSeaLabel()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")), GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")), GpsEntry(0x0004, LonBytes),
            GpsEntry(0x0005, [1]),               // altRef = 1 → below sea
            GpsEntry(0x0006, AltBytes),
        };

        var alt = GpsDecoder.Decode(entries)
            .First(e => e.Name == ImageHelperStrings.GpsAltitudeLabel);

        Assert.Contains(ImageHelperStrings.GpsAltitudeBelowSea, alt.Value);
    }

    // ── Speed ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("K", "km/h")]
    [InlineData("M", "mph")]
    [InlineData("N", "knots")]
    public void Decode_SpeedUnits_ReflectSpeedRef(string speedRef, string expectedUnit)
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")), GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, LonBytes),
            GpsEntry(0x000C, ToAsciiBytes(speedRef)),
            GpsEntry(0x000D, SpeedBytes),
        };

        var speed = GpsDecoder.Decode(entries)
            .First(e => e.Name == ImageHelperStrings.GpsSpeedLabel);

        Assert.Contains(expectedUnit, speed.Value);
        Assert.Contains("120", speed.Value);
    }

    // ── Date / Time ──────────────────────────────────────────────────────────

    [Fact]
    public void Decode_TimeWithoutDate_ProducesTimeOnlyEntry()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")), GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")), GpsEntry(0x0004, LonBytes),
            GpsEntry(0x0007, TimeBytes),
        };

        var dt = GpsDecoder.Decode(entries)
            .FirstOrDefault(e => e.Name == ImageHelperStrings.GpsDateTimeLabel);

        Assert.NotNull(dt);
        Assert.Contains("10:30", dt!.Value);
    }

    [Fact]
    public void Decode_TimeWithDate_ProducesDateTimeEntry()
    {
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")), GpsEntry(0x0002, LatBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")), GpsEntry(0x0004, LonBytes),
            GpsEntry(0x0007, TimeBytes),
            GpsEntry(0x001D, ToAsciiBytes("2024:06:15")),
        };

        var dt = GpsDecoder.Decode(entries)
            .First(e => e.Name == ImageHelperStrings.GpsDateTimeLabel);

        Assert.Contains("2024-06-15", dt.Value);
        Assert.Contains("10:30", dt.Value);
    }

    // ── Zero / invalid coordinates ───────────────────────────────────────

    [Fact]
    public void Decode_ZeroCoordinates_ReturnsEmpty()
    {
        // 0/1 = 0.0 for all components
        var zeroBytes = ToRationalBytes((0, 1), (0, 1), (0, 1));
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, zeroBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, zeroBytes),
        };

        var result = GpsDecoder.Decode(entries);

        Assert.Empty(result);
    }

    [Fact]
    public void Decode_DenominatorZero_ReturnsEmpty()
    {
        // Division by zero denominator should not produce output
        var badBytes = ToRationalBytes((48, 0), (51, 0), (29, 0));
        var entries = new List<MetadataEntry>
        {
            GpsEntry(0x0001, ToAsciiBytes("N")),
            GpsEntry(0x0002, badBytes),
            GpsEntry(0x0003, ToAsciiBytes("E")),
            GpsEntry(0x0004, badBytes),
        };

        var result = GpsDecoder.Decode(entries);

        Assert.Empty(result);
    }
}
