namespace PhotoLiker.PhotoBook.Tests;

public class BookRotationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(270, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(-180, 180)]
    [InlineData(-360, 0)]
    [InlineData(45, 0)]    // a quarter is the least there is: it is rounded down
    [InlineData(135, 90)]
    [InlineData(-45, 270)]
    public void AnyNumber_IsBroughtIntoAQuarterTurn(int degrees, int expected) => Assert.Equal(expected, BookRotation.Normalize(degrees));

    [Theory]
    [InlineData(0, 1, 90)]
    [InlineData(270, 1, 0)]
    [InlineData(0, -1, 270)]
    [InlineData(90, -1, 0)]
    [InlineData(90, 2, 270)]
    [InlineData(180, 4, 180)]
    [InlineData(0, -5, 270)]
    public void QuarterTurns_AreAddedAround(int degrees, int quarters, int expected) => Assert.Equal(expected, BookRotation.Add(degrees, quarters));

    [Theory]
    [InlineData(0, false)]
    [InlineData(90, true)]
    [InlineData(180, false)]
    [InlineData(270, true)]
    [InlineData(-90, true)]
    public void OnlyAQuarterPutsAPhotoOnItsSide(int degrees, bool sideways) => Assert.Equal(sideways, BookRotation.IsSideways(degrees));

    [Fact]
    public void TheTurnOfAnEntry_IsKeptInAQuarter()
    {
        var entry = new BookEntry("a.jpg") { Rotation = -90 };

        Assert.Equal(270, entry.Rotation);
        entry.Rotation = 400;
        Assert.Equal(0, entry.Rotation);
        Assert.Equal(0, new BookEntry("b.jpg").Rotation);
    }
}
