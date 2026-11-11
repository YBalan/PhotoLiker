using PhotoLiker.GenerateVideo;

namespace PhotoLiker.GenerateVideo.Tests;

/// <summary>The codecs of the Generate Video window and the quality (CRF) each starts with: the same look needs a higher number for H.265 than for H.264.</summary>
public class VideoCodecsTests
{
    [Theory]
    [InlineData("libx264", 23)]
    [InlineData("libx265", 28)]
    [InlineData("LIBX265", 28)]
    [InlineData("  libx265 ", 28)]
    [InlineData("libvpx", 23)]   // a codec this does not know starts as H.264 does
    [InlineData("", 23)]
    [InlineData(null, 23)]
    public void EachCodec_StartsWithItsUsualQuality(string? codec, int expected) => Assert.Equal(expected, VideoCodecs.DefaultCrf(codec));

    [Fact]
    public void TheCodecAWindowStartsWith_IsH265_WithItsOwnQuality()
    {
        Assert.Equal("libx265", VideoCodecs.Default);
        Assert.Equal(28, VideoCodecs.DefaultCrf(VideoCodecs.Default));
    }

    [Theory]
    [InlineData("libx264", "libx265", 23, 28)]   // nobody touched it: it goes along with the codec
    [InlineData("libx265", "libx264", 28, 23)]
    [InlineData("libx264", "libx265", 18, 18)]   // set by hand: it stays
    [InlineData("libx265", "libx264", 31, 31)]
    [InlineData("libx265", "libx264", 23, 23)]   // 23 is not what H.265 starts with, so it was chosen (and is what H.264 starts with anyway)
    [InlineData("libx264", "libx265", 28, 28)]   // chosen, and what H.265 starts with anyway
    [InlineData("libx264", "libx264", 23, 23)]   // no change of codec
    [InlineData("libx265", "libx265", 28, 28)]
    [InlineData("LIBX264", "LibX265", 23, 28)]
    public void WhenTheCodecChanges_TheQualityGoesAlong_OnlyIfNobodySetIt(string from, string to, int crf, int expected) =>
        Assert.Equal(expected, VideoCodecs.CrfAfterCodecChange(from, to, crf));

    [Fact]
    public void TheQualityIsWithinTheRangeFfmpegTakes()
    {
        Assert.InRange(VideoCodecs.H264Crf, VideoCodecs.MinCrf, VideoCodecs.MaxCrf);
        Assert.InRange(VideoCodecs.H265Crf, VideoCodecs.MinCrf, VideoCodecs.MaxCrf);
    }
}
