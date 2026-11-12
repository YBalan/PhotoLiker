using System.Text.Json;
using PhotoLiker.Core;

namespace PhotoLiker.Tests;

/// <summary>Where the panel with the property grids is kept in the config of the program: the places, and what an older or a hand-edited file says.</summary>
public class PanelPlaceTests
{
    [Theory]
    [InlineData("Left", "Left")]
    [InlineData("Right", "Right")]
    [InlineData("Bottom", "Bottom")]
    [InlineData("Float", "Float")]
    [InlineData("right", "Right")]
    [InlineData("  BOTTOM ", "Bottom")]
    [InlineData("float", "Float")]
    public void ANameOfAPlace_ReadsAsThatPlace_WhateverItsLetterCase(string text, string place) => Assert.Equal(place, PanelPlace.Normalize(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Top")]
    [InlineData("diagonal")]
    public void AnythingElse_ReadsAsTheLeft_WhereThePanelStarts(string? text) => Assert.Equal(PanelPlace.Left, PanelPlace.Normalize(text));

    [Fact]
    public void AConfigFromBeforeThePanelCouldMove_HasThePanelAtTheLeft_AtItsUsualSize_WithTheNamesHidden()
    {
        var config = JsonSerializer.Deserialize<GlobalConfig>("""{ "LastFolder": "D:\\Photos", "Language": "ua" }""")!;

        Assert.Equal("D:\\Photos", config.LastFolder);
        Assert.Equal(PanelPlace.Left, PanelPlace.Normalize(config.PropertiesPlace));
        Assert.False(config.PropertiesCollapsed);
        Assert.False(config.RailExpanded);
        Assert.Equal(PanelPlace.DefaultWidth, config.PropertiesWidth);
        Assert.Equal(PanelPlace.DefaultHeight, config.PropertiesHeight);
        Assert.Equal(0, config.FloatWidth); // never floated: the window finds its own place
    }

    [Fact]
    public void WhereThePanelWas_SurvivesTheFileItIsKeptIn()
    {
        var config = new GlobalConfig
        {
            RailExpanded = true,
            PropertiesPlace = PanelPlace.Float,
            PropertiesCollapsed = true,
            PropertiesWidth = 411,
            PropertiesHeight = 222,
            FloatLeft = 30,
            FloatTop = 40,
            FloatWidth = 500,
            FloatHeight = 600,
        };

        var back = JsonSerializer.Deserialize<GlobalConfig>(JsonSerializer.Serialize(config))!;

        Assert.True(back.RailExpanded);
        Assert.Equal(PanelPlace.Float, back.PropertiesPlace);
        Assert.True(back.PropertiesCollapsed);
        Assert.Equal((411, 222), (back.PropertiesWidth, back.PropertiesHeight));
        Assert.Equal((30, 40, 500, 600), (back.FloatLeft, back.FloatTop, back.FloatWidth, back.FloatHeight));
    }

    [Fact]
    public void ThePlaces_AreWithinWhatTheWindowCanHold()
    {
        Assert.True(PanelPlace.MinWidth < PanelPlace.DefaultWidth);
        Assert.True(PanelPlace.MinHeight < PanelPlace.DefaultHeight);
    }
}
