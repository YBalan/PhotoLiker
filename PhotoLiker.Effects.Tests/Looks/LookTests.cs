using PhotoLiker.Core;
using PhotoLiker.Effects.Looks;

namespace PhotoLiker.Effects.Tests.Looks;

public class LookTests
{
    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;

    private static (int R, int G, int B) Parts(uint pixel) => ((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF));

    private static uint[] Uniform(uint colour, int width = 60, int height = 40) => Enumerable.Repeat(colour, width * height).ToArray();

    private static (int R, int G, int B) Mean(uint[] pixels, int width, int x0, int y0, int w, int h)
    {
        double r = 0, g = 0, b = 0;
        for (var y = y0; y < y0 + h; y++)
        for (var x = x0; x < x0 + w; x++)
        {
            var (pr, pg, pb) = Parts(pixels[y * width + x]);
            r += pr;
            g += pg;
            b += pb;
        }
        var n = w * h;
        return ((int)Math.Round(r / n), (int)Math.Round(g / n), (int)Math.Round(b / n));
    }

    private static uint[] Graded(uint colour, SlideshowLook look, int width = 60, int height = 40, int seed = 0)
    {
        var pixels = Uniform(colour, width, height);
        LookGrade.Apply(pixels, width, height, look, seed: seed);
        return pixels;
    }

    // ── the grades ──────────────────────────────────────────────────────────

    [Fact]
    public void NoLook_LeavesThePictureAsItIs_AndSoDoesALutLookWithNoTable()
    {
        var photo = Uniform(Rgb(10, 120, 240));

        LookGrade.Apply(photo, 60, 40, SlideshowLook.None);
        Assert.All(photo, pixel => Assert.Equal(Rgb(10, 120, 240), pixel));

        LookGrade.Apply(photo, 60, 40, SlideshowLook.Lut, lut: null);
        Assert.All(photo, pixel => Assert.Equal(Rgb(10, 120, 240), pixel));
    }

    [Fact]
    public void Vintage_LiftsTheBlacks_LowersTheWhites_AndIsWarm()
    {
        var black = Mean(Graded(Rgb(0, 0, 0), SlideshowLook.Vintage), 60, 25, 15, 10, 10);
        var white = Mean(Graded(Rgb(255, 255, 255), SlideshowLook.Vintage), 60, 25, 15, 10, 10);
        var grey = Mean(Graded(Rgb(128, 128, 128), SlideshowLook.Vintage), 60, 25, 15, 10, 10);

        Assert.True(black.R > 12 && black.G > 12 && black.B > 6, $"the black is lifted: {black}");
        Assert.True(white.R < 250 && white.G < 250 && white.B < 240, $"the white is lowered: {white}");
        Assert.True(grey.R > grey.B + 4, $"a grey goes warm: {grey}");
    }

    [Fact]
    public void Film_IsCoolInTheShadows_AndWarmInTheLights()
    {
        var shadow = Mean(Graded(Rgb(40, 40, 40), SlideshowLook.Film), 60, 25, 15, 10, 10);
        var light = Mean(Graded(Rgb(215, 215, 215), SlideshowLook.Film), 60, 25, 15, 10, 10);

        Assert.True(shadow.B > shadow.R, $"cool shadows: {shadow}");
        Assert.True(light.R > light.B, $"warm lights: {light}");
    }

    [Fact]
    public void Cinema_IsTealInTheShadows_AndOrangeInTheLights()
    {
        var shadow = Mean(Graded(Rgb(50, 50, 50), SlideshowLook.Cinema), 60, 25, 15, 10, 10);
        var light = Mean(Graded(Rgb(210, 210, 210), SlideshowLook.Cinema), 60, 25, 15, 10, 10);

        Assert.True(shadow.B > shadow.R + 5, $"teal shadows: {shadow}");
        Assert.True(light.R > light.B + 5, $"orange lights: {light}");
    }

    [Fact]
    public void Sepia_IsOneBrownTone_DarkerForADarkerPhoto()
    {
        var dark = Mean(Graded(Rgb(40, 90, 200), SlideshowLook.Sepia), 60, 25, 15, 10, 10);
        var light = Mean(Graded(Rgb(230, 230, 200), SlideshowLook.Sepia), 60, 25, 15, 10, 10);

        Assert.True(dark.R > dark.G && dark.G > dark.B, $"brown: {dark}");
        Assert.True(light.R > light.G && light.G > light.B, $"brown: {light}");
        Assert.True(light.R > dark.R + 40, "the light photo stays lighter");
    }

    [Fact]
    public void BlackAndWhite_HasNoColourAtAll_NotEvenFromTheGrain()
    {
        var photo = Uniform(Rgb(200, 60, 30));
        LookGrade.Apply(photo, 60, 40, SlideshowLook.BlackAndWhite, seed: 3);

        Assert.All(photo, pixel =>
        {
            var (r, g, b) = Parts(pixel);
            Assert.True(r == g && g == b, $"grey: {r},{g},{b}");
        });
    }

    [Fact]
    public void TheVignette_DarkensTheCorners_AndNotTheMiddle()
    {
        var photo = Graded(Rgb(180, 180, 180), SlideshowLook.Vintage, width: 200, height: 120);

        var middle = Mean(photo, 200, 90, 50, 20, 20);
        var corner = Mean(photo, 200, 0, 0, 12, 12);

        Assert.True(corner.G < middle.G - 20, $"the corner {corner} is darker than the middle {middle}");
    }

    [Fact]
    public void TheGrain_IsTheSameForTheSameSeed_AndAnotherForAnotherSeed()
    {
        var a = Graded(Rgb(128, 128, 128), SlideshowLook.Film, seed: 5);
        var b = Graded(Rgb(128, 128, 128), SlideshowLook.Film, seed: 5);
        var c = Graded(Rgb(128, 128, 128), SlideshowLook.Film, seed: 6);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(a.Distinct().Count() > 3, "the grain is not one tone");
    }

    [Fact]
    public void TheGrainIsEvenlyAboutZero_NotLightOrDark()
    {
        double sum = 0;
        for (var y = 0; y < 100; y++)
        for (var x = 0; x < 100; x++)
            sum += LookGrade.Noise(x, y, 1);

        Assert.InRange(Math.Abs(sum / 10000), 0, 0.05);
    }

    [Theory]
    [InlineData(SlideshowLook.Vintage)]
    [InlineData(SlideshowLook.Film)]
    [InlineData(SlideshowLook.Sepia)]
    [InlineData(SlideshowLook.BlackAndWhite)]
    [InlineData(SlideshowLook.Cinema)]
    public void EveryLook_GivesAnOpaquePicture_OfTheSameSize(SlideshowLook look)
    {
        var photo = Uniform(0x40FF8000);

        LookGrade.Apply(photo, 60, 40, look);

        Assert.Equal(60 * 40, photo.Length);
        Assert.All(photo, pixel => Assert.Equal(0xFFu, pixel >> 24));
    }

    [Fact]
    public void APictureThatIsTooShort_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LookGrade.Apply(new uint[5], 60, 40, SlideshowLook.Film));
        Assert.Throws<ArgumentOutOfRangeException>(() => LookGrade.Apply(new uint[5], 0, 40, SlideshowLook.Film));
    }

    [Fact]
    public void TheNextLook_GoesDownTheList_AndRoundAtTheEnd_LeavingOutTheLutLookWhenThereIsNoTable()
    {
        var all = Enum.GetValues<SlideshowLook>();
        for (var i = 0; i < all.Length; i++)
            Assert.Equal(all[(i + 1) % all.Length], LookGrade.Next(all[i], hasTable: true));

        Assert.Equal(SlideshowLook.None, LookGrade.Next(SlideshowLook.Cinema, hasTable: false)); // Lut would do nothing
        Assert.Equal(SlideshowLook.Vintage, LookGrade.Next(SlideshowLook.None, hasTable: false));
        Assert.Equal(SlideshowLook.None, LookGrade.Next(SlideshowLook.Lut, hasTable: false));    // a look that has no table goes on at once
    }

    // ── the Polaroid card ───────────────────────────────────────────────────

    [Fact]
    public void TheCard_HasTheMarginOfAnInstantPrint_AndThePhotoOnIt()
    {
        const int w = 200, h = 100;
        var pixels = Uniform(Rgb(10, 120, 240), w, h);

        PolaroidCard.Apply(pixels, w, h);

        Assert.Equal(PolaroidCard.CardColour, pixels[0]);                      // the corners are the card
        Assert.Equal(PolaroidCard.CardColour, pixels[(h - 1) * w + w - 1]);
        Assert.Equal(PolaroidCard.CardColour, pixels[(h - 6) * w + w / 2]);     // the margin below
        Assert.Equal(Rgb(10, 120, 240), pixels[(h / 2) * w + w / 2]);           // the photo in the middle
        Assert.Equal(Rgb(10, 120, 240), pixels[(int)(h * 0.10) * w + (int)(w * 0.10)]); // just inside its corner

        var (x, y, width, height) = PolaroidCard.Inner;
        Assert.Equal(0.08f, x, 4);
        Assert.Equal(0.04f, y, 4);
        Assert.Equal(0.84f, width, 4);
        Assert.Equal(0.84f, height, 4);
        var (_, dateTop, _, dateHeight) = PolaroidCard.DateArea;
        Assert.Equal(y + height, dateTop, 4);
        Assert.True(dateHeight > y * 2, "the margin below is wider than the one above");
    }

    [Fact]
    public void ThePhotoOnTheCard_KeepsItsShape_AndItsWholePicture()
    {
        // Left half red, right half blue: the card shows both, in the same places, smaller
        const int w = 200, h = 120;
        var pixels = new uint[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            pixels[y * w + x] = x < w / 2 ? Rgb(255, 0, 0) : Rgb(0, 0, 255);

        PolaroidCard.Apply(pixels, w, h);

        var (r1, _, _) = Parts(pixels[h / 2 * w + (int)(w * 0.30)]);
        var (_, _, b2) = Parts(pixels[h / 2 * w + (int)(w * 0.70)]);
        Assert.True(r1 > 250 && b2 > 250, "red on the left, blue on the right");
        Assert.Equal(PolaroidCard.CardColour, pixels[h / 2 * w + (int)(w * 0.04)]); // the side margin
    }

    [Fact]
    public void TheCard_NeverChangesTheSizeOfThePicture_NorBreaksOnATinyOne()
    {
        foreach (var (w, h) in new[] { (1, 1), (3, 2), (7, 40), (300, 5) })
        {
            var pixels = Uniform(Rgb(1, 2, 3), w, h);
            PolaroidCard.Apply(pixels, w, h);
            Assert.Equal(w * h, pixels.Length);
            Assert.All(pixels, pixel => Assert.Equal(0xFFu, pixel >> 24));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => PolaroidCard.Apply(new uint[3], 10, 10));
    }

    // ── .cube tables ────────────────────────────────────────────────────────

    private static string IdentityCube(int size)
    {
        var lines = new List<string> { "TITLE \"Identity\"", $"LUT_3D_SIZE {size}" };
        for (var b = 0; b < size; b++)
        for (var g = 0; g < size; g++)
        for (var r = 0; r < size; r++)
            lines.Add(string.Join(' ', new[] { r, g, b }.Select(v => (v / (double)(size - 1)).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))));
        return string.Join('\n', lines);
    }

    private static CubeLut Parse(string text) => CubeLut.Parse(new StringReader(text));

    [Fact]
    public void AnIdentityCube_LeavesEveryColourAsItIs()
    {
        var lut = Parse(IdentityCube(5));
        var random = new Random(4);

        Assert.True(lut.IsCube);
        Assert.Equal(5, lut.Size);
        Assert.Equal("Identity", lut.Title);
        for (var i = 0; i < 500; i++)
        {
            float r = random.NextSingle(), g = random.NextSingle(), b = random.NextSingle();
            var (mr, mg, mb) = lut.Map(r, g, b);
            Assert.InRange(mr, r - 1e-4f, r + 1e-4f);
            Assert.InRange(mg, g - 1e-4f, g + 1e-4f);
            Assert.InRange(mb, b - 1e-4f, b + 1e-4f);
        }
    }

    [Fact]
    public void ACubeThatInverts_InvertsThePhoto()
    {
        // Red changes fastest in the list: this is the identity with every number turned round
        var size = 3;
        var lines = new List<string> { $"LUT_3D_SIZE {size}" };
        for (var b = 0; b < size; b++)
        for (var g = 0; g < size; g++)
        for (var r = 0; r < size; r++)
            lines.Add($"{1 - r / 2.0:0.0#} {1 - g / 2.0:0.0#} {1 - b / 2.0:0.0#}");
        var lut = Parse(string.Join('\n', lines));
        var photo = Uniform(Rgb(255, 0, 100));

        LookGrade.Apply(photo, 60, 40, SlideshowLook.Lut, lut);

        Assert.All(photo, pixel =>
        {
            var (r, g, b) = Parts(pixel);
            Assert.InRange(r, 0, 1);
            Assert.InRange(g, 254, 255);
            Assert.InRange(b, 154, 156);
        });
    }

    [Fact]
    public void ARedThatChangesFastest_IsTheOrderOfTheFile()
    {
        // A cube of 2: the colour of the second point in the list is the one of red 1, green 0, blue 0
        var lut = Parse("LUT_3D_SIZE 2\n0 0 0\n1 0.5 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1");

        var (r, g, b) = lut.Map(1, 0, 0);

        Assert.Equal((1f, 0.5f, 0f), (r, g, b));
    }

    [Fact]
    public void AOneDimensionalTable_IsACurveForEachColour()
    {
        var lut = Parse("LUT_1D_SIZE 2\n0.1 0.2 0.3\n0.9 0.8 0.7");

        Assert.False(lut.IsCube);
        var (r, g, b) = lut.Map(0.5f, 0.5f, 0.5f);
        Assert.Equal(0.5f, r, 4);
        Assert.Equal(0.5f, g, 4);
        Assert.Equal(0.5f, b, 4);
        var (r0, g0, b0) = lut.Map(0, 0, 0);
        Assert.Equal((0.1f, 0.2f, 0.3f), (r0, g0, b0));
    }

    [Fact]
    public void TheDomain_IsHonoured_AndColoursOutsideItAreKept()
    {
        var lut = Parse("DOMAIN_MIN 0 0 0\nDOMAIN_MAX 0.5 0.5 0.5\nLUT_1D_SIZE 2\n0 0 0\n1 1 1");

        Assert.Equal(1f, lut.Map(0.5f, 0.5f, 0.5f).R, 4);   // the top of the domain is the top of the curve
        Assert.Equal(0.5f, lut.Map(0.25f, 0.25f, 0.25f).G, 4);
        Assert.Equal(1f, lut.Map(0.9f, 0.9f, 0.9f).B, 4);   // beyond it is the top
    }

    [Fact]
    public void CommentsAndKeywordsThatAreNotUsed_AreSkipped()
    {
        var lut = Parse("# made by a program\nTITLE \"Warm\"\nLUT_3D_INPUT_RANGE 0 1\n\nLUT_1D_SIZE 2\n# the curve\n0 0 0\n1 1 1\n");

        Assert.Equal("Warm", lut.Title);
        Assert.Equal(2, lut.Size);
    }

    [Theory]
    [InlineData("")]                                                         // nothing
    [InlineData("0 0 0\n1 1 1")]                                             // colours before the size
    [InlineData("LUT_3D_SIZE 1\n0 0 0")]                                     // too small
    [InlineData("LUT_3D_SIZE 500\n0 0 0")]                                   // too large
    [InlineData("LUT_3D_SIZE x\n0 0 0")]                                     // not a number
    [InlineData("LUT_1D_SIZE 2\n0 0 0")]                                     // one colour short
    [InlineData("LUT_1D_SIZE 2\n0 0 0\n1 1 1\n1 1 1")]                       // one too many
    [InlineData("LUT_1D_SIZE 2\n0 0\n1 1 1")]                                // two numbers in a colour
    [InlineData("LUT_1D_SIZE 2\n0 0 0\n1 NaN 1")]                            // not a finite number
    [InlineData("LUT_1D_SIZE 2\nLUT_1D_SIZE 3\n0 0 0")]                      // the size twice
    [InlineData("DOMAIN_MAX 0 0 0\nLUT_1D_SIZE 2\n0 0 0\n1 1 1")]            // nothing between the ends of the domain
    [InlineData("DOMAIN_MIN 0 0\nLUT_1D_SIZE 2\n0 0 0\n1 1 1")]              // a domain of two numbers
    public void AFileThatIsNotACubeTable_IsRefused_WithAReasonAndNeverAnotherKindOfError(string text) =>
        Assert.Throws<FormatException>(() => Parse(text));

    [Fact]
    public void ACubeIsReadFromAFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("grade.cube");
        File.WriteAllText(path, IdentityCube(3));

        var lut = CubeLut.Load(path);

        Assert.Equal(3, lut.Size);
        Assert.Throws<FileNotFoundException>(() => CubeLut.Load(folder.File("missing.cube")));
    }
}
