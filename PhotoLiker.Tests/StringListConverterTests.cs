using System.ComponentModel;
using System.Globalization;
using PhotoLikerUI;

namespace PhotoLiker.Tests;

public class StringListConverterTests
{
    private readonly StringListConverter _converter = new();

    /// <summary>A property declared the way Settings.SavedInFolders is.</summary>
    private sealed class Sample
    {
        [ReadOnly(true)]
        [TypeConverter(typeof(StringListConverter))]
        public string[] Folders { get; set; } = [];
    }

    // ── one-line text ───────────────────────────────────────────────────────

    [Fact]
    public void ConvertTo_String_WrapsItemsInBracketsAndJoinsThemWithSemicolons()
    {
        var text = _converter.ConvertTo(null, CultureInfo.InvariantCulture, new[] { "Liked", "3 - Group", "A - Group Family" }, typeof(string));

        Assert.Equal("[Liked]; [3 - Group]; [A - Group Family]", text);
    }

    [Fact]
    public void ConvertTo_String_SingleItem_HasNoSeparator()
    {
        var text = _converter.ConvertTo(null, CultureInfo.InvariantCulture, new[] { "Liked" }, typeof(string));

        Assert.Equal("[Liked]", text);
    }

    [Fact]
    public void ConvertTo_String_NoItems_IsEmpty()
    {
        var text = _converter.ConvertTo(null, CultureInfo.InvariantCulture, Array.Empty<string>(), typeof(string));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void CanConvertTo_String_IsTrue()
    {
        Assert.True(_converter.CanConvertTo(null, typeof(string)));
    }

    [Fact]
    public void CanConvertFrom_String_IsFalse()
    {
        // the one-line text can't be edited
        Assert.False(_converter.CanConvertFrom(null, typeof(string)));
    }

    // ── expanded rows ───────────────────────────────────────────────────────

    [Fact]
    public void GetPropertiesSupported_IsTrue()
    {
        Assert.True(_converter.GetPropertiesSupported(null));
    }

    [Fact]
    public void GetProperties_ReturnsOneRowPerItem()
    {
        var items = new[] { "Liked", "3 - Group" };

        var rows = _converter.GetProperties(null, items, null)!;

        Assert.Equal(2, rows.Count);
        Assert.Equal(["[0]", "[1]"], rows.Cast<PropertyDescriptor>().Select(r => r.Name));
        Assert.Equal(["Liked", "3 - Group"], rows.Cast<PropertyDescriptor>().Select(r => (string?)r.GetValue(items)));
    }

    [Fact]
    public void GetProperties_NoItems_ReturnsNoRows()
    {
        var rows = _converter.GetProperties(null, Array.Empty<string>(), null)!;

        Assert.Empty(rows);
    }

    [Fact]
    public void GetProperties_RowsAreReadOnlyStrings()
    {
        var rows = _converter.GetProperties(null, new[] { "Liked" }, null)!;

        var row = rows[0];
        Assert.True(row.IsReadOnly);
        Assert.Equal(typeof(string), row.PropertyType);
    }

    [Fact]
    public void GetProperties_SettingARow_DoesNotChangeTheItems()
    {
        var items = new[] { "Liked" };
        var row = _converter.GetProperties(null, items, null)![0];

        row.SetValue(items, "Hacked");

        Assert.Equal(["Liked"], items);
    }

    [Fact]
    public void GetProperties_RowOfAShorterArray_HasNoValue()
    {
        var row = _converter.GetProperties(null, new[] { "a", "b" }, null)![1];

        Assert.Null(row.GetValue(new[] { "a" }));
    }

    // ── as declared on a property ───────────────────────────────────────────

    [Fact]
    public void Attribute_MakesThePropertyGridUseTheConverter()
    {
        var folders = TypeDescriptor.GetProperties(new Sample())[nameof(Sample.Folders)]!;

        Assert.IsType<StringListConverter>(folders.Converter);
        Assert.True(folders.IsReadOnly);
    }

    [Fact]
    public void Attribute_ConverterExpandsTheCurrentItems()
    {
        var sample = new Sample { Folders = ["Liked", "A - Group"] };
        var folders = TypeDescriptor.GetProperties(sample)[nameof(Sample.Folders)]!;

        var value = folders.GetValue(sample)!;
        var rows = folders.Converter.GetProperties(value)!;

        Assert.Equal("[Liked]; [A - Group]", folders.Converter.ConvertToString(value));
        Assert.Equal(2, rows.Count);
    }
}
