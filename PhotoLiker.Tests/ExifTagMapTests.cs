using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class ExifTagMapTests
{
    // ── GetName ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetName_KnownTag_IsTheFriendlyName_EvenIfTheReaderHasAnother()
    {
        Assert.Equal("Make", ExifTagMap.GetName(0x010F, "Manufacturer"));
    }

    [Fact]
    public void GetName_UnknownTag_UsesTheNameOfTheReader()
    {
        Assert.Equal("Lens Serial", ExifTagMap.GetName(0xBEEF, "Lens Serial"));
    }

    [Fact]
    public void GetName_UnknownTagWithoutReaderName_IsNamedByItsId()
    {
        Assert.Equal("Unknown (0xBEEF)", ExifTagMap.GetName(0xBEEF));
    }

    [Theory]
    [InlineData(0x9003, "Date Taken (Original)")]
    [InlineData(0xA002, "Width")]
    [InlineData(0xA401, "Custom Rendered")]   // only the MAUI app's map had these...
    [InlineData(0xA409, "Saturation")]
    [InlineData(0x8895, "Lens Info")]         // ...and only the WinForms app's map had these
    [InlineData(0x927C, "Maker Note")]
    public void GetName_CoversTheTagsOfBothFormerMaps(int tagId, string name)
    {
        Assert.Equal(name, ExifTagMap.GetName(tagId));
    }

    // ── GetCategory(int) ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x0002, "3. GPS", false)]
    [InlineData(0x0110, "1. Image", true)]
    [InlineData(0x5023, "5. Thumbnail", true)]
    [InlineData(0x9003, "4. EXIF", true)]
    public void GetCategory_FollowsTheTagIdRange(int tagId, string name, bool browsable)
    {
        var category = ExifTagMap.GetCategory(tagId);

        Assert.Equal(name, category.Name);
        Assert.Equal(browsable, category.IsBrowsable);
    }

    // ── GetCategory(MetadataEntry) ──────────────────────────────────────────

    [Fact]
    public void GetCategory_Entry_NamedLikeAKnownTag_FollowsTheTagId()
    {
        var category = ExifTagMap.GetCategory(new MetadataEntry(0x0110, "Model", "X"));

        Assert.Equal(ImageHelperStrings.CategoryImage, category.Name);
        Assert.True(category.IsBrowsable);
    }

    [Fact]
    public void GetCategory_Entry_WithoutFriendlyName_IsListedAsUnknown()
    {
        var category = ExifTagMap.GetCategory(new MetadataEntry(0x9003, "Unknown (0x9003)", "X"));

        Assert.Equal(ImageHelperStrings.CategoryUnknown, category.Name);
    }

    [Fact]
    public void GetCategory_Entry_UnknownGpsRangeTag_StaysHidden()
    {
        // The name decides the category, the tag id still decides whether the row can be browsed
        var category = ExifTagMap.GetCategory(new MetadataEntry(0x0042, "Unknown (0x0042)", "X"));

        Assert.Equal(ImageHelperStrings.CategoryUnknown, category.Name);
        Assert.False(category.IsBrowsable);
    }
}
