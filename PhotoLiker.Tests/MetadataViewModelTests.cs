using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class MetadataViewModelTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MetadataViewModel Build(IReadOnlyList<MetadataEntry> entries) =>
        new(entries);   // uses the internal constructor added for testability

    private static MetadataViewModel BuildWithGps()
    {
        byte[] latBytes = RationalBytes((48, 1), (51, 1), (29690, 1000));
        byte[] lonBytes = RationalBytes((2,  1), (17, 1), (4020,  100));

        return Build([
            new(0x0001, "GPS_0001", string.Empty, RawBytes: AsciiBytes("N")),
            new(0x0002, "GPS_0002", string.Empty, RawBytes: latBytes),
            new(0x0003, "GPS_0003", string.Empty, RawBytes: AsciiBytes("E")),
            new(0x0004, "GPS_0004", string.Empty, RawBytes: lonBytes),
        ]);
    }

    // ── GetGroups ────────────────────────────────────────────────────────────

    [Fact]
    public void GetGroups_EmptyEntries_ReturnsEmptyList()
    {
        var vm = Build([]);

        Assert.Empty(vm.GetGroups());
    }

    [Fact]
    public void GetGroups_GroupsAreSortedAlphabetically()
    {
        var vm = Build([
            new(0x0000, "Unknown (0x0000)", string.Empty),
            new(0x0100, "ImageWidth",       "100"),
        ]);

        var titles = vm.GetGroups().Select(g => g.Title).ToList();

        Assert.Equal(titles.OrderBy(t => t).ToList(), titles);
    }

    [Fact]
    public void GetGroups_UnknownTag_GoesToUnknownCategory()
    {
        var vm = Build([new(0xFFFF, "Unknown (0xFFFF)", string.Empty)]);

        var group = vm.GetGroups().FirstOrDefault(g => g.Title == ImageHelperStrings.CategoryUnknown);

        Assert.NotNull(group);
        Assert.Single(group!.Items);
    }

    [Fact]
    public void GetGroups_GpsDecodedEntries_AppearInGpsDecodedGroup()
    {
        var vm = BuildWithGps();

        var gpsGroup = vm.GetGroups()
            .FirstOrDefault(g => g.Title == ImageHelperStrings.CategoryGPSDecoded);

        Assert.NotNull(gpsGroup);
        Assert.NotEmpty(gpsGroup!.Items);
    }

    [Fact]
    public void GetGroups_MultipleEntriesSameCategory_AreInSameGroup()
    {
        // 0x010E (ImageDescription) and 0x010F (Make) both map to CategoryImage
        var vm = Build([
            new(0x010E, "ImageDescription", "photo"),
            new(0x010F, "Make",             "Canon"),
        ]);

        var group = vm.GetGroups().FirstOrDefault(g => g.Title == ImageHelperStrings.CategoryImage);

        Assert.NotNull(group);
        Assert.Equal(2, group!.Items.Count);
    }

    // ── MapLink ──────────────────────────────────────────────────────────────

    [Fact]
    public void MapLink_WithoutGpsData_IsNull()
    {
        var vm = Build([]);

        Assert.Null(vm.MapLink);
    }

    [Fact]
    public void MapLink_WithValidGps_ContainsMapsUrl()
    {
        var vm = BuildWithGps();

        Assert.NotNull(vm.MapLink);
        Assert.StartsWith("https://maps.google.com/maps?q=", vm.MapLink);
    }

    // ── MetadataGroup ────────────────────────────────────────────────────────

    [Fact]
    public void MetadataGroup_TitleAndItems_AreCorrect()
    {
        var items = new List<MetadataEntry> { new(1, "Width", "100") };
        var group = new MetadataGroup("MyGroup", items);

        Assert.Equal("MyGroup", group.Title);
        Assert.Single(group.Items);
        Assert.Equal("Width", group.Items[0].Name);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static byte[] RationalBytes(params (uint num, uint den)[] rationals)
    {
        var bytes = new byte[rationals.Length * 8];
        for (int i = 0; i < rationals.Length; i++)
        {
            BitConverter.GetBytes(rationals[i].num).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(rationals[i].den).CopyTo(bytes, i * 8 + 4);
        }
        return bytes;
    }

    private static byte[] AsciiBytes(string s) =>
        System.Text.Encoding.ASCII.GetBytes(s + "\0");
}
