using PhotoLiker.Effects.Colorize;
using PhotoLiker.Effects.Models;
using PhotoLiker.Search;

namespace PhotoLiker.Effects.Tests.Colorize;

/// <summary>The colour of Lab, how much colour a photo has, and putting the colours of the model on a photo (no model is needed for any of this).</summary>
public class ColorizeTests
{
    /// <summary>A picture that looks like a photograph as far as the numbers go: smooth, with light and dark places, given colour by <paramref name="colour"/> (the lightness, 0 to 255, in; blue, green and red out).</summary>
    private static DecodedPhoto Scene(int width, int height, Func<int, int, int, (byte B, byte G, byte R)> colour)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var light = (int)Math.Clamp(128 + 100 * Math.Sin(x / 17.0) * Math.Cos(y / 23.0) + 20 * Math.Sin((x + y) / 5.0), 5, 250);
            var (b, g, r) = colour(light, x, y);
            var i = (y * width + x) * 3;
            bgr[i] = b;
            bgr[i + 1] = g;
            bgr[i + 2] = r;
        }
        return new DecodedPhoto(width, height, bgr);
    }

    private static DecodedPhoto Grey(int width = 160, int height = 120) =>
        Scene(width, height, (light, _, _) => ((byte)light, (byte)light, (byte)light));

    /// <summary>The brown of an old print: the same ratios of red, green and blue at every light.</summary>
    private static DecodedPhoto Sepia(int width = 160, int height = 120) =>
        Scene(width, height, (light, _, _) => ((byte)(light * 0.66), (byte)(light * 0.84), (byte)light));

    /// <summary>A photo with many colours: a different hue in every block, a different strength in every stripe.</summary>
    private static DecodedPhoto Colourful(int width = 160, int height = 120) =>
        Scene(width, height, (light, x, y) =>
        {
            var hue = (x / 20 * 47 + y / 15 * 31) % 360;
            var strength = 0.35 + 0.65 * ((x / 7 + y / 5) % 4) / 3.0;
            return (Channel(hue, 240, light, strength), Channel(hue, 120, light, strength), Channel(hue, 0, light, strength));
        });

    private static byte Channel(double hue, double offset, double light, double strength)
    {
        var wave = 0.5 + 0.5 * Math.Cos((hue - offset) * Math.PI / 180);
        return (byte)Math.Clamp(light * (1 - strength) + 255 * wave * strength, 0, 255);
    }

    // ── Lab ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(255, 255, 255, 100.0, 0.0, 0.0)]
    [InlineData(0, 0, 0, 0.0, 0.0, 0.0)]
    [InlineData(255, 0, 0, 53.24, 80.09, 67.20)]     // pure red, the numbers of OpenCV (the convention the model was trained against)
    [InlineData(0, 255, 0, 87.73, -86.18, 83.18)]
    [InlineData(0, 0, 255, 32.30, 79.19, -107.86)]
    public void LabOfAKnownColour_IsWhatOpenCvGives(int red, int green, int blue, double l, double a, double b)
    {
        var lab = LabColor.FromBgr((byte)blue, (byte)green, (byte)red);

        Assert.Equal(l, lab.L, 0.1);
        Assert.Equal(a, lab.A, 0.2);
        Assert.Equal(b, lab.B, 0.2);
    }

    [Fact]
    public void EveryGrey_HasNoColour_AndALightnessThatGrowsWithIt()
    {
        var before = -1f;
        for (var level = 0; level <= 255; level++)
        {
            var (l, a, b) = LabColor.FromBgr((byte)level, (byte)level, (byte)level);
            Assert.InRange(a, -0.01f, 0.01f);
            Assert.InRange(b, -0.01f, 0.01f);
            Assert.True(l >= before, $"level {level}");
            before = l;
        }
    }

    [Fact]
    public void ALabColour_GoesBackToTheSameBytes_ForColoursThatCanBeShown()
    {
        for (var blue = 0; blue <= 255; blue += 17)
        for (var green = 0; green <= 255; green += 17)
        for (var red = 0; red <= 255; red += 17)
        {
            var (l, a, b) = LabColor.FromBgr((byte)blue, (byte)green, (byte)red);
            LabColor.ToBgr(l, a, b, out var blueBack, out var greenBack, out var redBack);

            Assert.InRange(blueBack, blue - 1, blue + 1);
            Assert.InRange(greenBack, green - 1, green + 1);
            Assert.InRange(redBack, red - 1, red + 1);
        }
    }

    [Fact]
    public void AColourThatCanNotBeShown_IsBroughtIntoRange_AndKeepsItsDirection()
    {
        // L 50 with a and b of 127 is far outside what a screen can show
        LabColor.ToBgr(50, 127, 127, out var blue, out var green, out var red);

        Assert.True(red > green && red > blue); // still a strong red-yellow, not noise
        Assert.InRange(blue, 0, 255);
    }

    [Fact]
    public void TheLightOfAPixel_IsOneForWhiteAndNothingForBlack()
    {
        Assert.Equal(1f, LabColor.Luminance(255, 255, 255), 0.0001f);
        Assert.Equal(0f, LabColor.Luminance(0, 0, 0));
        Assert.True(LabColor.Luminance(0, 255, 0) > LabColor.Luminance(255, 0, 0)); // green is lighter than red, red than blue
        Assert.True(LabColor.Luminance(0, 0, 255) > LabColor.Luminance(255, 0, 0));
    }

    [Fact]
    public void TheLightnessOfALight_IsTheLOfAGreyOfThatLight()
    {
        foreach (var level in new byte[] { 0, 3, 20, 77, 128, 200, 255 })
        {
            var (l, _, _) = LabColor.FromBgr(level, level, level);
            Assert.Equal(l, LabColor.LightnessOf(LabColor.Luminance(level, level, level)), 0.05f);
        }
    }

    // ── how much colour a photo has ─────────────────────────────────────────

    [Fact]
    public void AGreyPhoto_HasNoColour_AndIsBlackAndWhite()
    {
        var amount = BlackAndWhite.Measure(Grey());

        Assert.Equal(0, amount.MeanChroma, 0.01);
        Assert.True(amount.IsBlackAndWhite);
    }

    [Fact]
    public void AGreyPhotoWithACast_IsStillBlackAndWhite()
    {
        // A scan has a little of one colour in it: 3 levels more red
        var photo = Scene(160, 120, (light, _, _) => ((byte)light, (byte)light, (byte)Math.Min(255, light + 3)));

        Assert.True(BlackAndWhite.Measure(photo).IsBlackAndWhite);
    }

    [Fact]
    public void AnOldBrownPrint_IsBlackAndWhite_ThoughItHasColour()
    {
        var amount = BlackAndWhite.Measure(Sepia());

        Assert.InRange(amount.MeanChroma, 5, 30); // a real tint ...
        Assert.True(amount.MinorSpread < ColorAmount.OneTintSpread); // ... of one colour
        Assert.True(amount.IsBlackAndWhite);
    }

    [Fact]
    public void ACoolToneToo()
    {
        var photo = Scene(160, 120, (light, _, _) => ((byte)light, (byte)(light * 0.92), (byte)(light * 0.8)));

        Assert.True(BlackAndWhite.Measure(photo).IsBlackAndWhite);
    }

    [Fact]
    public void APhotoWithColours_IsNotBlackAndWhite()
    {
        var amount = BlackAndWhite.Measure(Colourful());

        Assert.True(amount.MeanChroma > 15);
        Assert.True(amount.MinorSpread > ColorAmount.OneTintSpread);
        Assert.False(amount.IsBlackAndWhite);
    }

    [Fact]
    public void AStronglyTintedPhoto_IsNotBlackAndWhite_EvenOfOneColour()
    {
        // A wall that is all red: one colour, but far stronger than any tint of paper
        var photo = Scene(160, 120, (light, _, _) => ((byte)(light * 0.1), (byte)(light * 0.2), 255));

        var amount = BlackAndWhite.Measure(photo);

        Assert.True(amount.MeanChroma > ColorAmount.MostTintedChroma);
        Assert.False(amount.IsBlackAndWhite);
    }

    [Fact]
    public void ThePhotoIsLookedAtInAGrid_SoAHugePhotoIsAsQuickAsASmallOne()
    {
        var big = Scene(3000, 2000, (light, _, _) => ((byte)light, (byte)light, (byte)light));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var amount = BlackAndWhite.Measure(big);

        Assert.True(started.ElapsedMilliseconds < 500, $"{started.ElapsedMilliseconds} ms");
        Assert.True(amount.IsBlackAndWhite);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(3, 2)]
    public void AVerySmallPhoto_CanBeMeasured(int width, int height)
    {
        var photo = Scene(width, height, (light, _, _) => ((byte)light, (byte)light, (byte)light));

        Assert.True(BlackAndWhite.Measure(photo).IsBlackAndWhite);
    }

    // ── the colours of the model on the photo ───────────────────────────────

    private static float[] Chroma(float a, float b)
    {
        var plane = Colorizer.ModelSide * Colorizer.ModelSide;
        var chroma = new float[2 * plane];
        Array.Fill(chroma, a, 0, plane);
        Array.Fill(chroma, b, plane, plane);
        return chroma;
    }

    [Fact]
    public void WithNoColourFromTheModel_AGreyPhoto_StaysGrey()
    {
        var photo = Grey();

        var result = Colorizer.Combine(photo, Chroma(0, 0), 1);

        Assert.Equal(photo.Width, result.Width);
        Assert.Equal(photo.Height, result.Height);
        for (var i = 0; i < photo.Bgr.Length; i++)
            Assert.InRange(result.Bgr[i], photo.Bgr[i] - 1, photo.Bgr[i] + 1);
    }

    [Fact]
    public void TheColoursOfTheModel_AreOnThePhoto_AndItsLightnessIsKept()
    {
        var photo = Grey();

        var result = Colorizer.Combine(photo, Chroma(25, 30), 1);

        for (var i = 0; i < photo.Width * photo.Height; i += 7)
        {
            var before = LabColor.FromBgr(photo.Bgr[i * 3], photo.Bgr[i * 3 + 1], photo.Bgr[i * 3 + 2]);
            var after = LabColor.FromBgr(result.Bgr[i * 3], result.Bgr[i * 3 + 1], result.Bgr[i * 3 + 2]);

            if (before.L is > 25 and < 75) // at the ends of the range the colour is out of what a screen can show, and is cut
            {
                Assert.Equal(before.L, after.L, 3f); // the detail of the photo is its own
                Assert.True(after.A > 10, $"a {after.A} at {i}"); // a and b come from the model
                Assert.True(after.B > 10, $"b {after.B} at {i}");
            }
        }
    }

    [Fact]
    public void TheSaturation_ScalesTheColours_AndZeroIsGrey()
    {
        var photo = Grey();
        var chroma = Chroma(30, -20);

        float Chromaness(DecodedPhoto p)
        {
            var sum = 0.0;
            for (var i = 0; i < p.Width * p.Height; i++)
            {
                var (_, a, b) = LabColor.FromBgr(p.Bgr[i * 3], p.Bgr[i * 3 + 1], p.Bgr[i * 3 + 2]);
                sum += Math.Sqrt(a * a + b * b);
            }
            return (float)(sum / (p.Width * p.Height));
        }

        var pale = Chromaness(Colorizer.Combine(photo, chroma, 0.5f));
        var normal = Chromaness(Colorizer.Combine(photo, chroma, 1f));
        var strong = Chromaness(Colorizer.Combine(photo, chroma, 1.5f));
        var none = Chromaness(Colorizer.Combine(photo, chroma, 0f));

        Assert.True(none < 0.6f);
        Assert.True(pale < normal && normal < strong);
        Assert.InRange(pale / normal, 0.35f, 0.75f); // not exactly half: the stronger colours are cut where a screen can not show them
    }

    [Fact]
    public void TheColoursOfTheModel_AreStretchedSmoothlyToThePhoto_WhateverItsSize()
    {
        // The left half of the 512 square is red-ish, the right half blue-ish: at any size the change comes in the middle, over a few points
        var plane = Colorizer.ModelSide * Colorizer.ModelSide;
        var chroma = new float[2 * plane];
        for (var y = 0; y < Colorizer.ModelSide; y++)
        for (var x = 0; x < Colorizer.ModelSide; x++)
        {
            chroma[y * Colorizer.ModelSide + x] = x < Colorizer.ModelSide / 2 ? 40 : -10;
            chroma[plane + y * Colorizer.ModelSide + x] = x < Colorizer.ModelSide / 2 ? 30 : -40;
        }

        foreach (var (width, height) in new[] { (1000, 300), (333, 777), (40, 40), (3, 2) })
        {
            var photo = new DecodedPhoto(width, height, Enumerable.Repeat((byte)128, width * height * 3).ToArray()); // a flat mid grey, which every colour can be shown on
            var result = Colorizer.Combine(photo, chroma, 1);

            var left = LabColor.FromBgr(result.Bgr[(height / 2 * width + 0) * 3], result.Bgr[(height / 2 * width + 0) * 3 + 1], result.Bgr[(height / 2 * width + 0) * 3 + 2]);
            var right = LabColor.FromBgr(
                result.Bgr[(height / 2 * width + width - 1) * 3], result.Bgr[(height / 2 * width + width - 1) * 3 + 1], result.Bgr[(height / 2 * width + width - 1) * 3 + 2]);

            Assert.True(left.A > right.A, $"{width}x{height}");
            Assert.Equal(photo.Bgr.Length, result.Bgr.Length);
        }
    }
}

/// <summary>The colorizing model itself: runs where its file is (see <see cref="RealModels.Folder"/>).</summary>
public class ColorizerModelTests
{
    private static bool ModelPresent => ToolModels.Colorizer.IsPresentIn(RealModels.Folder);

    /// <summary>A grey picture with shapes in it, so that the model has something to give colours to: a bright disc on a darker, graded ground.</summary>
    private static DecodedPhoto GreyDisc(int width, int height)
    {
        var bgr = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var dx = (x - width / 2.0) / (width * 0.25);
            var dy = (y - height / 2.0) / (height * 0.35);
            var light = dx * dx + dy * dy < 1 ? 200 - 60 * (dx * dx + dy * dy) : 70 + 80.0 * y / height;
            var level = (byte)Math.Clamp(light + 10 * Math.Sin(x / 5.0), 0, 255);
            Array.Fill(bgr, level, (y * width + x) * 3, 3);
        }
        return new DecodedPhoto(width, height, bgr);
    }

    [Fact]
    public void TheModel_GivesColoursToAGreyPhoto_AndKeepsItsLightness()
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(4);
        var photo = GreyDisc(640, 400);

        var result = colorizer.Colorize(photo);

        Assert.Equal(640, result.Width);
        Assert.Equal(400, result.Height);
        Assert.False(BlackAndWhite.Measure(result).IsBlackAndWhite, "it has colours now");
        Assert.True(BlackAndWhite.Measure(result).MeanChroma > 4);

        // The lightness of the middle part of the range is the photo's own, point by point
        var checkedPoints = 0;
        for (var i = 0; i < photo.Width * photo.Height; i += 13)
        {
            var before = LabColor.FromBgr(photo.Bgr[i * 3], photo.Bgr[i * 3 + 1], photo.Bgr[i * 3 + 2]);
            if (before.L is < 30 or > 70)
                continue;

            var after = LabColor.FromBgr(result.Bgr[i * 3], result.Bgr[i * 3 + 1], result.Bgr[i * 3 + 2]);
            Assert.Equal(before.L, after.L, 4f);
            checkedPoints++;
        }
        Assert.True(checkedPoints > 500);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(100, 700)]
    [InlineData(1200, 90)]
    public void TheModel_WorksOnPhotosOfAnyShape(int width, int height)
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(2);

        var result = colorizer.Colorize(GreyDisc(width, height));

        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
        Assert.Equal(width * height * 3, result.Bgr.Length);
    }

    [Fact]
    public void TheModel_GivesTheSameColoursTwice()
    {
        if (!ModelPresent) return;
        using var hub = new EffectModels(RealModels.Folder);
        using var colorizer = hub.CreateColorizer(2);
        var photo = GreyDisc(200, 150);

        Assert.Equal(colorizer.Colorize(photo).Bgr, colorizer.Colorize(photo).Bgr);
    }
}
