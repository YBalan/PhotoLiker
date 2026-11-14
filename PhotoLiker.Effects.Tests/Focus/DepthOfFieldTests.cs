using System.Diagnostics;
using PhotoLiker.Effects.Depth;
using PhotoLiker.Effects.Focus;
using PhotoLiker.Effects.Motion;

namespace PhotoLiker.Effects.Tests.Focus;

public class DepthOfFieldTests
{
    private const int W = 200;
    private const int H = 120;

    /// <summary>A photo that is all fine detail (a chessboard of 2 pixel squares), so that blur shows as less difference between neighbours.</summary>
    private static uint[] Chessboard(int width = W, int height = H)
    {
        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            pixels[y * width + x] = ((x / 2 + y / 2) % 2 == 0) ? 0xFFE0E0E0u : 0xFF202020u;
        return pixels;
    }

    private static uint[] Uniform(uint colour, int width = W, int height = H) => Enumerable.Repeat(colour, width * height).ToArray();

    /// <summary>The left half near (255) and the right half far (0), on a grid of 20 by 12 points.</summary>
    private static DepthMap NearLeftFarRight() =>
        new(20, 12, Enumerable.Range(0, 240).Select(i => i % 20 < 10 ? (byte)255 : (byte)0).ToArray());

    /// <summary>The mean difference between a pixel and the one to its right, in the columns from..to (a measure of how much fine detail is left).</summary>
    private static double Detail(uint[] pixels, int from, int to, int width = W, int height = H)
    {
        double sum = 0;
        var count = 0;
        for (var y = 0; y < height; y++)
        for (var x = from; x < to - 1; x++)
        {
            sum += Math.Abs((int)(pixels[y * width + x] & 0xFF) - (int)(pixels[y * width + x + 1] & 0xFF));
            count++;
        }
        return sum / count;
    }

    private static bool Same(uint[] a, uint[] b, int fromColumn, int toColumn, int width = W, int height = H)
    {
        for (var y = 0; y < height; y++)
        for (var x = fromColumn; x < toColumn; x++)
        {
            if (a[y * width + x] != b[y * width + x])
                return false;
        }
        return true;
    }

    // ── what the blur does ──────────────────────────────────────────────────

    [Fact]
    public void WithNoBlurAndNoHaze_ThePhotoComesBackAsACopy()
    {
        var photo = Chessboard();

        var result = DepthOfField.Render(photo, W, H, NearLeftFarRight(), new FocusLook(1, 0, 0.3, 0));

        Assert.NotSame(photo, result);
        Assert.Equal(photo, result);
    }

    [Fact]
    public void FocusedOnTheNearThing_ItStaysAsItWas_AndWhatIsFarIsBlurred()
    {
        var photo = Chessboard();

        var result = DepthOfField.Render(photo, W, H, NearLeftFarRight(), new FocusLook(1, 1, 0.3, 0));

        Assert.True(Same(photo, result, 0, W * 4 / 10), "the near half is not touched (not even by the blur that is round it)");
        var before = Detail(photo, W * 6 / 10, W);
        var after = Detail(result, W * 6 / 10, W);
        Assert.True(after < before * 0.2, $"the far half has lost most of its detail: {before:0.0} -> {after:0.0}");
    }

    [Fact]
    public void FocusedOnTheFarThing_ItIsTheNearThatIsBlurred()
    {
        var photo = Chessboard();

        var result = DepthOfField.Render(photo, W, H, NearLeftFarRight(), new FocusLook(0, 1, 0.3, 0));

        Assert.True(Same(photo, result, W * 6 / 10, W));
        Assert.True(Detail(result, 0, W * 4 / 10) < Detail(photo, 0, W * 4 / 10) * 0.2);
    }

    [Fact]
    public void ABlurDoesNotMakeAPhotoDarkerOrLighter()
    {
        var uniform = Uniform(0xFF6496C8);
        var blurred = DepthOfField.Render(uniform, W, H, NearLeftFarRight(), new FocusLook(1, 1, 0, 0));
        Assert.Equal(uniform, blurred);

        // And a photo with detail keeps its average
        var photo = Chessboard();
        var result = DepthOfField.Render(photo, W, H, NearLeftFarRight(), new FocusLook(1, 1, 0, 0));
        double Mean(uint[] pixels) => pixels.Average(p => (double)(p & 0xFF));
        Assert.InRange(Mean(result), Mean(photo) * 0.99, Mean(photo) * 1.01);
    }

    [Fact]
    public void AMoreBlurIsAWiderBlur()
    {
        var photo = Chessboard();
        var map = NearLeftFarRight();

        var weak = DepthOfField.Render(photo, W, H, map, new FocusLook(1, 0.2, 0.3, 0));
        var strong = DepthOfField.Render(photo, W, H, map, new FocusLook(1, 1, 0.3, 0));

        Assert.True(Detail(strong, W * 6 / 10, W) < Detail(weak, W * 6 / 10, W));
        Assert.True(Detail(weak, W * 6 / 10, W) < Detail(photo, W * 6 / 10, W));
    }

    [Fact]
    public void ADeeperRange_KeepsMoreOfThePhotoSharp()
    {
        var photo = Chessboard();
        var gradient = new DepthMap(20, 12, Enumerable.Range(0, 240).Select(i => (byte)(i % 20 * 255 / 19)).ToArray()); // far on the left, near on the right

        int Sharp(FocusLook look)
        {
            var result = DepthOfField.Render(photo, W, H, gradient, look);
            var columns = 0;
            for (var x = 0; x < W; x++)
            {
                if (Enumerable.Range(0, H).All(y => result[y * W + x] == photo[y * W + x]))
                    columns++;
            }
            return columns;
        }

        var thin = Sharp(new FocusLook(0.5, 1, 0, 0));
        var deep = Sharp(new FocusLook(0.5, 1, 1, 0));

        Assert.InRange(thin, 1, W / 3);
        Assert.True(deep > thin * 2, $"{thin} columns sharp with a thin zone, {deep} with a deep one");
    }

    // ── the haze ────────────────────────────────────────────────────────────

    [Fact]
    public void TheHaze_LiesOverWhatIsFar_AndNotOverWhatIsNear()
    {
        var photo = Uniform(0xFF646464);

        var result = DepthOfField.Render(photo, W, H, NearLeftFarRight(), new FocusLook(1, 0, 0.3, 1));

        Assert.True(Same(photo, result, 0, W * 4 / 10), "the nearest thing has no haze");
        var far = result[H / 2 * W + W * 9 / 10];
        Assert.True(((far >> 16) & 0xFF) > 150, "the far side is lost in a light mist");
        Assert.True((far & 0xFF) > 150);
    }

    [Fact]
    public void TheHaze_HasTheColourOfTheFarParts_Lightened()
    {
        // A warm far side (a sunset) gives a warm haze, a cold one a cold haze
        var warm = DepthOfField.Render(Uniform(0xFFC86428), W, H, NearLeftFarRight(), new FocusLook(1, 0, 0.3, 1))[H / 2 * W + W * 9 / 10];
        var cold = DepthOfField.Render(Uniform(0xFF2864C8), W, H, NearLeftFarRight(), new FocusLook(1, 0, 0.3, 1))[H / 2 * W + W * 9 / 10];

        Assert.True(((warm >> 16) & 0xFF) > (warm & 0xFF), "warm: more red than blue");
        Assert.True((cold & 0xFF) > ((cold >> 16) & 0xFF), "cold: more blue than red");
    }

    // ── where to focus ──────────────────────────────────────────────────────

    [Fact]
    public void WithoutFaces_TheFocusIsOnTheMiddleOfThePhoto()
    {
        // Far all round, near in the middle: what a photograph is of
        var values = new byte[20 * 12];
        for (var y = 0; y < 12; y++)
        for (var x = 0; x < 20; x++)
            values[y * 20 + x] = x is >= 5 and < 15 && y is >= 3 and < 9 ? (byte)230 : (byte)40;
        var map = new DepthMap(20, 12, values);

        Assert.InRange(DepthOfField.AutoFocus(map, []), 0.85, 0.95);
    }

    [Fact]
    public void WithFaces_TheFocusIsOnTheLargestFace_NotOnTheMiddle()
    {
        var map = NearLeftFarRight();
        var farLarge = new PhotoRegion(0.65, 0.3, 0.2, 0.3);   // in the far half
        var nearSmall = new PhotoRegion(0.1, 0.3, 0.05, 0.08); // in the near half

        Assert.InRange(DepthOfField.AutoFocus(map, [nearSmall, farLarge]), 0, 0.05);
        Assert.InRange(DepthOfField.AutoFocus(map, [nearSmall]), 0.95, 1);
        Assert.InRange(DepthOfField.AutoFocus(map, [new PhotoRegion(double.NaN, 0, 0.1, 0.1), nearSmall]), 0.95, 1); // a face that is none is not looked at
    }

    [Fact]
    public void ThePlaceThePersonPointsAt_SaysWhereTheFocusIs()
    {
        var map = NearLeftFarRight();

        Assert.InRange(DepthOfField.FocusAt(map, 0.15, 0.5), 0.95, 1);
        Assert.InRange(DepthOfField.FocusAt(map, 0.85, 0.5), 0, 0.05);
        Assert.InRange(DepthOfField.FocusAt(map, -3, 9), 0, 1); // beyond the photo: its edge
    }

    // ── what is asked for that makes no sense ───────────────────────────────

    [Fact]
    public void ALookThatIsOutOfRange_IsBroughtIn_AndOneThatIsNoneTakesTheUsualValues()
    {
        var clamped = new FocusLook(3, -2, 9, double.PositiveInfinity).Clamped();
        Assert.Equal(new FocusLook(1, 0, 1, 1), clamped);

        var none = new FocusLook(double.NaN, double.NaN, double.NaN, double.NaN).Clamped();
        Assert.Equal(new FocusLook(0.5, FocusLook.UsualBlur, FocusLook.UsualRange, FocusLook.UsualHaze), none);
    }

    [Fact]
    public void APhotoThatIsTooShort_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthOfField.Render(new uint[10], W, H, NearLeftFarRight(), FocusLook.Portrait(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DepthOfField.Render(new uint[10], 0, H, NearLeftFarRight(), FocusLook.Portrait(1)));
    }

    [Fact]
    public void ThePhotoGivenIsNeverChanged()
    {
        var photo = Chessboard();
        var copy = (uint[])photo.Clone();

        DepthOfField.Render(photo, W, H, NearLeftFarRight(), new FocusLook(1, 1, 0.3, 1));

        Assert.Equal(copy, photo);
    }

    [Fact]
    public void ThePortraitLookGoesWithThePhotoAtAnySize_NotOnlyThePreview()
    {
        // The depth is a grid that says nothing of pixels: a photo twice the size has the same near and far parts
        var small = DepthOfField.Render(Chessboard(), W, H, NearLeftFarRight(), FocusLook.Portrait(1));
        var large = DepthOfField.Render(Chessboard(W * 2, H * 2), W * 2, H * 2, NearLeftFarRight(), FocusLook.Portrait(1));

        Assert.True(Same(Chessboard(), small, 0, W * 4 / 10));
        Assert.True(Same(Chessboard(W * 2, H * 2), large, 0, W * 4 / 5, W * 2, H * 2));
        Assert.True(Detail(large, W * 6 / 5, W * 2, W * 2, H * 2) < Detail(Chessboard(W * 2, H * 2), W * 6 / 5, W * 2, W * 2, H * 2) * 0.5);
    }

    // ── the blur itself ─────────────────────────────────────────────────────

    [Fact]
    public void TheBlur_SpreadsLightEvenlyAndLosesNothing()
    {
        // A light square of 11 x 11 in the middle of a dark picture
        var photo = Uniform(0xFF000000, 41, 41);
        for (var y = 15; y <= 25; y++)
        for (var x = 15; x <= 25; x++)
            photo[y * 41 + x] = 0xFFFFFFFF;

        var result = BoxBlur.Apply(photo, 41, 41, 2);

        // The light stays (an 8 bit pipeline rounds a little at each of the six passes), is the same to the left as to the right and above as below, and does not reach farther than three passes of 2 each way
        var total = result.Sum(p => (double)(p & 0xFF));
        Assert.InRange(total, 121 * 255 * 0.985, 121 * 255 * 1.015);
        for (var k = 1; k <= 12; k++)
        {
            Assert.Equal(result[20 * 41 + 20 - k], result[20 * 41 + 20 + k]);
            Assert.Equal(result[(20 - k) * 41 + 20], result[(20 + k) * 41 + 20]);
        }
        Assert.Equal(0u, result[20 * 41 + 20 + 12] & 0xFF);
        Assert.True((result[20 * 41 + 25] & 0xFF) is > 100 and < 255, "the edge of the square is soft");
    }

    [Fact]
    public void ARadiusOfNothing_IsACopy()
    {
        var photo = Chessboard();
        var copy = BoxBlur.Apply(photo, W, H, 0);

        Assert.NotSame(photo, copy);
        Assert.Equal(photo, copy);
    }

    [Fact]
    public void TheEdgeOfThePhotoIsRepeated_SoItDoesNotGoDark()
    {
        var blurred = BoxBlur.Apply(Uniform(0xFF808080, 30, 20), 30, 20, 9);

        Assert.All(blurred, pixel => Assert.Equal(0xFF808080u, pixel));
    }

    // ── what the pictures say ───────────────────────────────────────────────

    [Fact]
    public void ThePixelsOfAPhoto_GoToTheBytesOfADecodedPhoto_AndBack()
    {
        var pixels = new uint[] { 0xFF010203, 0xFF0A0B0C, 0xFFFF8000, 0xFF000000 };

        var photo = PixelBuffers.ToPhoto(pixels, 2, 2);

        Assert.Equal(new byte[] { 3, 2, 1, 0x0C, 0x0B, 0x0A, 0x00, 0x80, 0xFF, 0, 0, 0 }, photo.Bgr);
        Assert.Equal(pixels, PixelBuffers.FromPhoto(photo));
    }

    [Fact]
    public void ARealSizedPreview_IsRefocusedInReasonableTime()
    {
        // 2048 x 1365, what the viewer shows while a slider is dragged. About 0.15 s in a release build; the bound is for a debug build with the other tests running, and only
        // catches work that grows faster than the number of pixels
        const int w = 2048, h = 1365;
        var photo = Chessboard(w, h);
        var map = NearLeftFarRight();
        DepthOfField.Render(photo, w, h, map, FocusLook.Portrait(1)); // the first run pays for the thread pool

        var watch = Stopwatch.StartNew();
        DepthOfField.Render(photo, w, h, map, FocusLook.Portrait(1));
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed.TotalMilliseconds:0} ms");
    }
}
