using PhotoLiker.Imaging;

namespace PhotoLiker.Search.Tests;

public class ClipImageEncoderTests
{
    [Fact]
    public void Prepare_GivesTheCentreSquare_AsNormalizedPlanarRgb()
    {
        // A white 448 x 224 photo: after scaling to 224 high and cutting the centre every value is the same
        var white = new DecodedPhoto(448, 224, Enumerable.Repeat((byte)255, 448 * 224 * 3).ToArray());

        var input = ClipImageEncoder.Prepare(white, ClipModels.Default);

        Assert.Equal(ClipModels.Default.InputLength, input.Length);
        var plane = ClipModels.Default.ImageSize * ClipModels.Default.ImageSize;
        Assert.Equal((1f - 0.48145466f) / 0.26862954f, input[0], 3);         // red
        Assert.Equal((1f - 0.4578275f) / 0.26130258f, input[plane], 3);      // green
        Assert.Equal((1f - 0.40821073f) / 0.27577711f, input[2 * plane], 3); // blue
    }
}
