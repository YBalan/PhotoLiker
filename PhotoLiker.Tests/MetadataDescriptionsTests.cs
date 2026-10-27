using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class MetadataDescriptionsTests
{
    [Fact]
    public void EveryTagThatHasAName_HasADescription()
    {
        var missing = ExifTagMap.Tags.Where(t => !MetadataDescriptions.Tags.ContainsKey(t.Key)).Select(t => $"0x{t.Key:X4} {t.Value}").ToList();

        Assert.True(missing.Count == 0, "no description for: " + string.Join(", ", missing));
    }

    [Fact]
    public void ADescription_ForATagThatHasNoName_IsAMistake()
    {
        var orphans = MetadataDescriptions.Tags.Keys.Where(id => !ExifTagMap.Tags.ContainsKey(id)).Select(id => $"0x{id:X4}").ToList();

        Assert.True(orphans.Count == 0, "described but not named: " + string.Join(", ", orphans));
    }

    [Fact]
    public void TheDescriptions_AreSentencesOfTheirOwn()
    {
        foreach (var (id, text) in MetadataDescriptions.Tags)
        {
            Assert.True(text.Length >= 20, $"0x{id:X4} is too short to explain anything: '{text}'");
            Assert.True(text.EndsWith('.'), $"0x{id:X4} is not a sentence: '{text}'");
        }

        // Two tags with the same text would be a copy that was not changed
        Assert.Equal(MetadataDescriptions.Tags.Count, MetadataDescriptions.Tags.Values.Distinct().Count());
    }

    [Fact]
    public void ARowOfAKnownTag_GetsTheDescriptionOfItsTag()
    {
        var make = new MetadataEntry(0x010F, ExifTagMap.GetName(0x010F), "Canon");

        Assert.Equal(MetadataDescriptions.Tags[0x010F], MetadataDescriptions.For(make));
        Assert.Contains("camera", MetadataDescriptions.For(make));
    }

    [Fact]
    public void ARowOfAnUnknownTag_SaysSoAndGivesItsNumber()
    {
        var text = MetadataDescriptions.For(new MetadataEntry(0xABCD, ExifTagMap.GetName(0xABCD), "12 34"));

        Assert.Contains("0xABCD", text);
        Assert.Contains("no name", text);
    }

    [Fact]
    public void ARowOfATagOnlyTheReaderKnows_GivesItsNameAndNumber()
    {
        var text = MetadataDescriptions.For(new MetadataEntry(0xBEEF, "Lens Serial Number", "123"));

        Assert.Contains("Lens Serial Number", text);
        Assert.Contains("0xBEEF", text);
    }

    [Fact]
    public void EveryRowThatTheGpsDecoderMakes_HasADescription()
    {
        string[] names =
        [
            ImageHelperStrings.GpsLatitude, ImageHelperStrings.GpsLongitude, ImageHelperStrings.GpsLatitudeDMS, ImageHelperStrings.GpsLongitudeDMS,
            ImageHelperStrings.GpsCoordinates, ImageHelperStrings.GpsAltitudeLabel, ImageHelperStrings.GpsSpeedLabel, ImageHelperStrings.GpsDateTimeLabel,
            ImageHelperStrings.GpsMapLink,
        ];

        foreach (var name in names)
            Assert.False(string.IsNullOrWhiteSpace(MetadataDescriptions.ForDecoded(name)), $"no description for '{name}'");

        Assert.Equal(names.Length, MetadataDescriptions.Decoded.Count);
        Assert.Null(MetadataDescriptions.ForDecoded("Something else"));
    }

    [Fact]
    public void TheDebugRow_IsDescribed()
    {
        Assert.Contains("Every metadata tag", MetadataDescriptions.AllMetadata);
    }
}
