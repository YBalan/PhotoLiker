namespace PhotoLiker.Stickers.Tests;

public class StickerSettingsTests
{
    [Fact]
    public void The_defaults_are_the_sticker_that_Telegram_users_know()
    {
        var settings = new StickerSettings();

        Assert.Equal(CutoutMode.Detect, settings.Cutout);
        Assert.Equal(50, settings.Sensitivity);
        Assert.Equal(10, settings.SmallParts);
        Assert.Equal(40, settings.FollowEdges);
        Assert.Equal(0, settings.EdgeShift);
        Assert.True(settings.Outline);
        Assert.Equal(8, settings.OutlineWidth);
        Assert.Equal("FFFFFF", settings.OutlineColor);
        Assert.True(settings.Shadow);
        Assert.Equal(StickerFormat.WebP, settings.Format);
        Assert.Equal(85, settings.Quality);
    }

    [Fact]
    public void Every_value_is_brought_into_its_range()
    {
        var wild = new StickerSettings
        {
            Cutout = (CutoutMode)99,
            Sensitivity = 500,
            SmallParts = -4,
            FollowEdges = 900,
            EdgeShift = -50,
            OutlineWidth = 400,
            OutlineColor = "not a colour",
            Format = (StickerFormat)7,
            Quality = 0,
        }.Normalized();

        Assert.Equal(CutoutMode.Detect, wild.Cutout);
        Assert.Equal(99, wild.Sensitivity);
        Assert.Equal(0, wild.SmallParts);
        Assert.Equal(100, wild.FollowEdges);
        Assert.Equal(-10, wild.EdgeShift);
        Assert.Equal(40, wild.OutlineWidth);
        Assert.Equal("FFFFFF", wild.OutlineColor);
        Assert.Equal(StickerFormat.WebP, wild.Format);
        Assert.Equal(1, wild.Quality);

        var low = new StickerSettings { Sensitivity = -3, EdgeShift = 50, OutlineWidth = -1, Quality = 1000, SmallParts = 1000, FollowEdges = -1 }.Normalized();
        Assert.Equal(1, low.Sensitivity);
        Assert.Equal(10, low.EdgeShift);
        Assert.Equal(0, low.OutlineWidth);
        Assert.Equal(100, low.Quality);
        Assert.Equal(100, low.SmallParts);
        Assert.Equal(0, low.FollowEdges);
    }

    [Fact]
    public void Normalizing_leaves_the_values_that_are_in_range_alone()
    {
        var settings = new StickerSettings
        {
            Cutout = CutoutMode.WholePicture, Sensitivity = 70, SmallParts = 33, FollowEdges = 12, EdgeShift = -3, Outline = false, OutlineWidth = 17,
            OutlineColor = "ff8800", Shadow = false, Format = StickerFormat.Png, Quality = 61,
        };

        var same = settings.Normalized();

        Assert.Equal(CutoutMode.WholePicture, same.Cutout);
        Assert.Equal(70, same.Sensitivity);
        Assert.Equal(33, same.SmallParts);
        Assert.Equal(12, same.FollowEdges);
        Assert.Equal(-3, same.EdgeShift);
        Assert.False(same.Outline);
        Assert.Equal(17, same.OutlineWidth);
        Assert.Equal("FF8800", same.OutlineColor);
        Assert.False(same.Shadow);
        Assert.Equal(StickerFormat.Png, same.Format);
        Assert.Equal(61, same.Quality);
    }

    [Theory]
    [InlineData("ff8800", "FF8800")]
    [InlineData("#00ff7f", "00FF7F")]
    [InlineData("  #123abc ", "123ABC")]
    [InlineData("FFF", "FFFFFF")]
    [InlineData("12345g", "FFFFFF")]
    [InlineData("1234567", "FFFFFF")]
    [InlineData("", "FFFFFF")]
    [InlineData(null, "FFFFFF")]
    public void A_colour_is_six_hexadecimal_digits_or_white(string? given, string expected) =>
        Assert.Equal(expected, StickerSettings.NormalizeColor(given));

    [Fact]
    public void The_outline_colour_is_read_as_red_green_and_blue()
    {
        var (red, green, blue) = new StickerSettings { OutlineColor = "FF8001" }.OutlineRgb;

        Assert.Equal(0xFF, red);
        Assert.Equal(0x80, green);
        Assert.Equal(0x01, blue);
        Assert.Equal("FF8001", StickerSettings.ColorOf(0xFF, 0x80, 0x01));
    }

    [Fact]
    public void The_settings_are_kept_in_a_file_and_come_back_the_same()
    {
        using var folder = new TempFolder();
        var path = folder.File("sub\\stickers.json");
        var settings = new StickerSettings
        {
            Cutout = CutoutMode.WholePicture, Sensitivity = 61, SmallParts = 5, FollowEdges = 70, EdgeShift = 2, Outline = true, OutlineWidth = 12, OutlineColor = "FFD700",
            Shadow = false, Format = StickerFormat.Png, Quality = 77,
        };

        Assert.True(settings.Save(path));
        var back = StickerSettings.Load(path);

        Assert.Equal(CutoutMode.WholePicture, back.Cutout);
        Assert.Equal(61, back.Sensitivity);
        Assert.Equal(5, back.SmallParts);
        Assert.Equal(70, back.FollowEdges);
        Assert.Equal(2, back.EdgeShift);
        Assert.Equal(12, back.OutlineWidth);
        Assert.Equal("FFD700", back.OutlineColor);
        Assert.False(back.Shadow);
        Assert.Equal(StickerFormat.Png, back.Format);
        Assert.Equal(77, back.Quality);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("WholePicture", File.ReadAllText(path)); // the names of the choices, so that a person can read the file
    }

    [Fact]
    public void A_file_that_is_missing_empty_or_damaged_gives_the_defaults()
    {
        using var folder = new TempFolder();
        var damaged = folder.File("damaged.json");
        File.WriteAllText(damaged, "{ this is not json");
        var empty = folder.File("empty.json");
        File.WriteAllText(empty, "");

        foreach (var path in new[] { folder.File("missing.json"), damaged, empty })
        {
            var settings = StickerSettings.Load(path);
            Assert.Equal(50, settings.Sensitivity);
            Assert.Equal(StickerFormat.WebP, settings.Format);
        }
    }

    [Fact]
    public void A_file_edited_by_hand_is_brought_into_range_when_it_is_read()
    {
        using var folder = new TempFolder();
        var path = folder.File("edited.json");
        File.WriteAllText(path, """{ "Sensitivity": 400, "OutlineWidth": -9, "OutlineColor": "zzz", "Quality": 0 }""");

        var settings = StickerSettings.Load(path);

        Assert.Equal(99, settings.Sensitivity);
        Assert.Equal(0, settings.OutlineWidth);
        Assert.Equal("FFFFFF", settings.OutlineColor);
        Assert.Equal(1, settings.Quality);
    }

    [Fact]
    public void Saving_to_a_place_that_can_not_be_written_says_so_and_does_not_throw()
    {
        using var folder = new TempFolder();
        var blocker = folder.File("blocker");
        File.WriteAllText(blocker, "a file where a folder is wanted");

        Assert.False(new StickerSettings().Save(Path.Combine(blocker, "stickers.json")));
    }
}
