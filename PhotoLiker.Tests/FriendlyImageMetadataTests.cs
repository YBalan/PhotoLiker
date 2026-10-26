using System.Text;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>The entries built from properties a platform decoder (GDI+ in the WinForms app) has already read.</summary>
public class FriendlyImageMetadataFromPropertiesTests
{
    private const int Ascii = 2, Short = 3;

    private static RawExifProperty Text(int id, string value) =>
        new(id, Ascii, Encoding.ASCII.GetBytes(value + "\0"));

    [Fact]
    public void KnownTag_GetsItsFriendlyName_AndFormattedValue()
    {
        var meta = FriendlyImageMetadata.FromProperties([Text(0x010F, "Canon")]);

        var entry = Assert.Single(meta.Entries);
        Assert.Equal(0x010F, entry.TagId);
        Assert.Equal("Make", entry.Name);
        Assert.Equal("Canon", entry.Value);
        Assert.Equal(Ascii, entry.Type);
    }

    [Fact]
    public void RawBytes_AreKept_ForTheGpsDecoder()
    {
        var bytes = Encoding.ASCII.GetBytes("N\0");

        var meta = FriendlyImageMetadata.FromProperties([new RawExifProperty(0x0001, Ascii, bytes)]);

        Assert.Same(bytes, meta.Entries[0].RawBytes);
    }

    [Fact]
    public void UnknownTag_IsNamedByItsId()
    {
        var meta = FriendlyImageMetadata.FromProperties([new RawExifProperty(0xBEEF, Short, BitConverter.GetBytes((ushort)7))]);

        var entry = Assert.Single(meta.Entries);
        Assert.Equal("Unknown (0xBEEF)", entry.Name);
        Assert.StartsWith(ImageHelperStrings.UnknownPrefix, entry.Name);
        Assert.Equal("7", entry.Value);
    }

    [Fact]
    public void TwoTagsWithTheSameName_KeepOnlyTheFirst()
    {
        // 0x0132 and 0x0132 again; the same name must not be listed twice
        var meta = FriendlyImageMetadata.FromProperties([Text(0x0132, "first"), Text(0x0132, "second")]);

        var entry = Assert.Single(meta.Entries);
        Assert.Equal("first", entry.Value);
    }

    [Fact]
    public void NoProperties_NoEntries()
    {
        Assert.Empty(FriendlyImageMetadata.FromProperties([]).Entries);
    }

    [Fact]
    public void GpsTags_FeedTheGpsDecoder()
    {
        byte[] Rational(params (uint num, uint den)[] values)
        {
            var bytes = new byte[values.Length * 8];
            for (var i = 0; i < values.Length; i++)
            {
                BitConverter.GetBytes(values[i].num).CopyTo(bytes, i * 8);
                BitConverter.GetBytes(values[i].den).CopyTo(bytes, i * 8 + 4);
            }
            return bytes;
        }

        var meta = FriendlyImageMetadata.FromProperties(
        [
            new RawExifProperty(0x0001, Ascii, Encoding.ASCII.GetBytes("N\0")),
            new RawExifProperty(0x0002, 5, Rational((48, 1), (51, 1), (0, 1))),
            new RawExifProperty(0x0003, Ascii, Encoding.ASCII.GetBytes("E\0")),
            new RawExifProperty(0x0004, 5, Rational((2, 1), (21, 1), (0, 1))),
        ]);

        var view = new MetadataViewModel(meta);

        Assert.NotNull(view.MapLink);
        Assert.StartsWith("https://maps.google.com/maps?q=", view.MapLink);
    }
}
