using System.Diagnostics;
using PhotoLiker.Effects.Colorize;
using PhotoLiker.Search;
using Xunit.Abstractions;

namespace PhotoLiker.Effects.Tests.Colorize;

/// <summary>What a person changes in the colours of the model after it has run (see <see cref="ColorTuning"/>): no model is needed, the colours are made up.</summary>
public class ColorTuningTests(ITestOutputHelper output)
{
    private static ModelColours Colours(float a, float b)
    {
        var plane = Colorizer.ModelSide * Colorizer.ModelSide;
        var values = new float[2 * plane];
        Array.Fill(values, a, 0, plane);
        Array.Fill(values, b, plane, plane);
        return new ModelColours(values);
    }

    private static DecodedPhoto Flat(byte level, int width = 40, int height = 30)
    {
        var bgr = new byte[width * height * 3];
        Array.Fill(bgr, level);
        return new DecodedPhoto(width, height, bgr);
    }

    private static (float L, float A, float B) LabAt(DecodedPhoto photo, int x = 0, int y = 0)
    {
        var i = (y * photo.Width + x) * 3;
        return LabColor.FromBgr(photo.Bgr[i], photo.Bgr[i + 1], photo.Bgr[i + 2]);
    }

    [Fact]
    public void WithNoChange_TheColoursAreTheOnesTheModelGave()
    {
        var photo = Flat(128);
        var colours = Colours(20, -15);

        var plain = Colorizer.Combine(photo, colours.Values, 1f);
        var applied = Colorizer.Apply(photo, colours, ColorTuning.AsGiven);

        Assert.Equal(plain.Bgr, applied.Bgr);
    }

    [Fact]
    public void ASaturationInTheTuning_IsTheSaturationOfTheOldWay()
    {
        var photo = Flat(150);
        var colours = Colours(25, 30);

        Assert.Equal(
            Colorizer.Combine(photo, colours.Values, 0.6f).Bgr,
            Colorizer.Apply(photo, colours, new ColorTuning(Saturation: 0.6f)).Bgr);
    }

    [Fact]
    public void Warmth_MovesAGreyTowardsYellowAndRed_AndCoolnessTowardsBlue()
    {
        var photo = Flat(128);
        var none = Colours(0, 0);

        var warm = LabAt(Colorizer.Apply(photo, none, new ColorTuning(Warmth: 1f)));
        var cool = LabAt(Colorizer.Apply(photo, none, new ColorTuning(Warmth: -1f)));
        var plain = LabAt(Colorizer.Apply(photo, none, ColorTuning.AsGiven));

        Assert.True(warm.B > plain.B + 10, $"warm b {warm.B}");
        Assert.True(warm.A > plain.A + 2, $"warm a {warm.A}");
        Assert.True(cool.B < plain.B - 10, $"cool b {cool.B}");
        Assert.True(cool.A < plain.A - 2, $"cool a {cool.A}");
    }

    [Fact]
    public void Warmth_IsStrongerTheMoreIsAsked()
    {
        var photo = Flat(128);
        var none = Colours(0, 0);

        var small = LabAt(Colorizer.Apply(photo, none, new ColorTuning(Warmth: 0.3f))).B;
        var great = LabAt(Colorizer.Apply(photo, none, new ColorTuning(Warmth: 0.9f))).B;

        Assert.InRange(small, 3f, 9f);
        Assert.True(great > small * 2f, $"{small} then {great}");
    }

    [Fact]
    public void Warmth_LeavesBlackAndWhiteAlone()
    {
        var none = Colours(0, 0);
        foreach (var (level, name) in new[] { ((byte)0, "black"), ((byte)255, "white") })
        {
            var result = Colorizer.Apply(Flat(level), none, new ColorTuning(Warmth: 1f));

            var lab = LabAt(result);
            Assert.True(Math.Abs(lab.A) < 2.5f && Math.Abs(lab.B) < 2.5f, $"{name} got a {lab.A}, b {lab.B}");
        }
    }

    [Fact]
    public void WithNoSaturationAndSomeWarmth_APhotoIsToned_OneColourAllOver()
    {
        var bgr = new byte[80 * 60 * 3];
        for (var i = 0; i < 80 * 60; i++)
            Array.Fill(bgr, (byte)(40 + i * 170 / (80 * 60)), i * 3, 3);
        var photo = new DecodedPhoto(80, 60, bgr);

        var result = Colorizer.Apply(photo, Colours(30, -40), new ColorTuning(Saturation: 0f, Warmth: 0.7f));

        var amount = BlackAndWhite.Measure(result);
        Assert.True(amount.MeanChroma > ColorAmount.GreyChroma, $"it has a tone: {amount.MeanChroma}");
        Assert.True(amount.IsBlackAndWhite, "a tone of one colour is still a black and white photo");
        Assert.True(LabAt(result, 40, 30).B > 5f, "the tone is warm");
    }

    [Fact]
    public void TheSameColours_FitAPictureOfAnotherSize()
    {
        var colours = Colours(18, 24);
        var large = LabAt(Colorizer.Apply(Flat(120, 400, 300), colours, new ColorTuning(1.2f, 0.4f)), 200, 150);
        var small = LabAt(Colorizer.Apply(Flat(120, 40, 30), colours, new ColorTuning(1.2f, 0.4f)), 20, 15);

        Assert.Equal(large.A, small.A, 1f);
        Assert.Equal(large.B, small.B, 1f);
    }

    [Fact]
    public void ColoursOfTheWrongSize_AreRefused() =>
        Assert.Throws<ArgumentException>(() => new ModelColours(new float[10]));

    [Fact]
    public void ChangingTheColours_IsQuick_AlsoOnALargePhoto()
    {
        const int width = 4000;
        const int height = 3000;
        var bgr = new byte[width * height * 3];
        for (var i = 0; i < bgr.Length; i++)
            bgr[i] = (byte)(i / 3 % 251);
        var photo = new DecodedPhoto(width, height, bgr);
        var colours = Colours(15, 20);
        Colorizer.Apply(photo, colours, ColorTuning.AsGiven); // the first time warms things up

        var clock = Stopwatch.StartNew();
        const int runs = 5;
        for (var i = 0; i < runs; i++)
            Colorizer.Apply(photo, colours, new ColorTuning(1f + i / 10f, 0.5f));
        var each = clock.Elapsed.TotalMilliseconds / runs;

        output.WriteLine($"12 megapixels: {each:0} ms for a change of the colours");
        Assert.True(each < 2000, $"{each} ms");
    }
}
