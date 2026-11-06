namespace PhotoLiker.Video.Tests;

public class VideoTimeTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59.9, "0:59")] // cut, not rounded: the time shown never runs ahead of the picture
    [InlineData(77, "1:17")]
    [InlineData(600, "10:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3723, "1:02:03")]
    [InlineData(36000, "10:00:00")]
    [InlineData(-5, "0:00")]
    public void ATime_IsWrittenAsMinutesAndSeconds_OrWithHours(double seconds, string expected) =>
        Assert.Equal(expected, VideoTime.Format(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("83", 83)]
    [InlineData("83.5", 83.5)]
    [InlineData("83,5", 83.5)]
    [InlineData("0", 0)]
    [InlineData("1:23", 83)]
    [InlineData("01:23", 83)]
    [InlineData("1:02:03", 3723)]
    [InlineData(" 1 : 23 ", 83)]
    [InlineData("1:23.5", 83.5)]
    [InlineData("1h2m3s", 3723)]
    [InlineData("1m23s", 83)]
    [InlineData("90s", 90)]
    [InlineData("2M", 120)]
    public void WhatIsTyped_IsATime(string text, double seconds)
    {
        Assert.True(VideoTime.TryParse(text, out var time));
        Assert.Equal(seconds, time.TotalSeconds, 0.0001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1:2:3:4")]
    [InlineData("1:-5")]
    [InlineData("1m30")]
    [InlineData("1x30s")]
    [InlineData("1:xx")]
    public void WhatIsNotATime_IsRefused(string? text) => Assert.False(VideoTime.TryParse(text, out _));

    [Fact]
    public void AWrittenTime_CanBeTypedBack()
    {
        foreach (var seconds in new[] { 0, 5, 77, 600, 3723, 36000 })
        {
            Assert.True(VideoTime.TryParse(VideoTime.Format(TimeSpan.FromSeconds(seconds)), out var time));
            Assert.Equal(seconds, time.TotalSeconds);
        }
    }
}
