using PhotoLiker.Core;

namespace PhotoLiker.Tests;

public class DurationTextTests
{
    [Theory]
    [InlineData(0, "0.0 s")]
    [InlineData(0.5, "0.5 s")]
    [InlineData(4.54, "4.5 s")]
    [InlineData(9.94, "9.9 s")]
    [InlineData(10, "0:10")]
    [InlineData(59.4, "0:59")]
    [InlineData(59.6, "1:00")]
    [InlineData(65, "1:05")]
    [InlineData(205.12, "3:25")]
    [InlineData(3599.4, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3735, "1:02:15")]
    [InlineData(8000, "2:13:20")]
    [InlineData(90000, "25:00:00")]
    public void Format_ReadsLikeAPlayer(double seconds, string expected)
    {
        Assert.Equal(expected, DurationText.Format(seconds));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    public void Format_NotALength_IsZero(double seconds)
    {
        Assert.Equal("0.0 s", DurationText.Format(seconds));
    }

    [Fact]
    public void Format_UsesADotWhateverTheCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("4.5 s", DurationText.Format(4.5));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
