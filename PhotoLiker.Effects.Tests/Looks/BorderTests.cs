using PhotoLiker.Core;
using PhotoLiker.Effects.Looks;

namespace PhotoLiker.Effects.Tests.Looks;

/// <summary>
/// The borders of the slideshow (Polaroid, Frame, Film, Stamp, Glow, Shadow): which there are and in what order, where the photo and the date go on each, that every one keeps the picture's size and
/// shape of the photo, and what makes each one itself (the gilt moulding lit from the top left, the sprocket holes, the perforation, a glow in the photo's colour, a shadow that falls down and right).
/// </summary>
public class BorderTests
{
    private static readonly uint Blue = Rgb(10, 120, 240);

    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;

    private static (int R, int G, int B) Parts(uint pixel) => ((int)((pixel >> 16) & 0xFF), (int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF));

    private static int Brightness(uint pixel) => Parts(pixel) is var (r, g, b) ? r + g + b : 0;

    private static uint[] Uniform(uint colour, int width, int height) => Enumerable.Repeat(colour, width * height).ToArray();

    private static uint[] Made(SlideshowBorder border, uint colour, int width, int height)
    {
        var pixels = Uniform(colour, width, height);
        Borders.Apply(border, pixels, width, height);
        return pixels;
    }

    public static TheoryData<SlideshowBorder> EveryStyle() => [.. Borders.Styles];

    // ── which borders there are ─────────────────────────────────────────────

    [Fact]
    public void TheStyles_AreEveryBorderButNoneAndRandom_InTheOrderTheBKeyGoesThem()
    {
        Assert.Equal(
            [SlideshowBorder.Polaroid, SlideshowBorder.Frame, SlideshowBorder.Film, SlideshowBorder.Stamp, SlideshowBorder.Glow, SlideshowBorder.Shadow],
            Borders.Styles);

        // A new value of the setting that is not given a style here would be a border that does nothing
        var withoutStyle = Enum.GetValues<SlideshowBorder>().Where(border => border is not (SlideshowBorder.None or SlideshowBorder.Random) && !Borders.Styles.Contains(border));
        Assert.Empty(withoutStyle);
        Assert.All(Borders.Styles, style => Assert.True(Borders.IsStyle(style)));
        Assert.False(Borders.IsStyle(SlideshowBorder.None));
        Assert.False(Borders.IsStyle(SlideshowBorder.Random));
        Assert.False(Borders.IsStyle((SlideshowBorder)99));
    }

    [Fact]
    public void TheBKey_GoesFromNoneThroughEveryStyleToRandom_AndRoundToNone()
    {
        var seen = new List<SlideshowBorder>();
        var border = SlideshowBorder.None;
        do
        {
            seen.Add(border);
            border = Borders.Next(border);
        }
        while (border != SlideshowBorder.None);

        Assert.Equal([SlideshowBorder.None, .. Borders.Styles, SlideshowBorder.Random], seen);
        Assert.Equal(SlideshowBorder.None, Borders.Next((SlideshowBorder)99)); // a value nobody knows is started over
    }

    // ── where the photo and the date go ─────────────────────────────────────

    [Fact]
    public void NoneAndRandom_HaveNoLayout_AndDoNothingToAPicture()
    {
        Assert.Null(Borders.LayoutOf(SlideshowBorder.None));
        Assert.Null(Borders.LayoutOf(SlideshowBorder.Random));
        Assert.All([SlideshowBorder.None, SlideshowBorder.Random], border => Assert.Equal(Uniform(Blue, 20, 10), Made(border, Blue, 20, 10)));
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void ThePhotoIsInsideTheFrame_WithTheSameShareEachWay_SoItKeepsItsShape(SlideshowBorder border)
    {
        var layout = Borders.LayoutOf(border)!.Value;

        Assert.Equal(layout.Width, layout.Height, 4);
        Assert.InRange(layout.Width, 0.5f, 0.95f);
        Assert.InRange(layout.X, 0f, 1 - layout.Width + 0.0001f);
        Assert.InRange(layout.Y, 0f, 1 - layout.Height + 0.0001f);
        Assert.True(layout.X > 0 && layout.Y > 0, "there is a border on every side");
    }

    [Fact]
    public void TheDate_HasAPlaceOnPolaroidFilmAndStamp_AndNotOnTheOthers_WhichIsInTheFrame()
    {
        Assert.Equal(DateInk.Handwriting, Borders.LayoutOf(SlideshowBorder.Polaroid)!.Value.Date!.Value.Ink);
        Assert.Equal(DateInk.FilmStamp, Borders.LayoutOf(SlideshowBorder.Film)!.Value.Date!.Value.Ink);
        Assert.Equal(DateInk.Capitals, Borders.LayoutOf(SlideshowBorder.Stamp)!.Value.Date!.Value.Ink);
        Assert.Null(Borders.LayoutOf(SlideshowBorder.Frame)!.Value.Date);
        Assert.Null(Borders.LayoutOf(SlideshowBorder.Glow)!.Value.Date);
        Assert.Null(Borders.LayoutOf(SlideshowBorder.Shadow)!.Value.Date);

        foreach (var border in Borders.Styles)
        {
            if (Borders.LayoutOf(border)!.Value.Date is not { } date)
                continue;

            Assert.InRange(date.X, 0f, 1f);
            Assert.InRange(date.Y, 0f, 1f);
            Assert.True(date.X + date.Width <= 1.0001f && date.Y + date.Height <= 1.0001f, $"{border}: the date is in the frame");
            Assert.True(date.Width > 0.1f && date.Height > 0.03f, $"{border}: there is room to write it");
        }
    }

    [Fact]
    public void TheDateOfAPolaroidAndOfAStamp_IsInTheMarginBelowThePhoto_AndTheFilmsOnThePhoto()
    {
        foreach (var border in new[] { SlideshowBorder.Polaroid, SlideshowBorder.Stamp })
        {
            var layout = Borders.LayoutOf(border)!.Value;
            Assert.True(layout.Date!.Value.Y >= layout.Y + layout.Height - 0.0001f, $"{border}: below the photo");
        }

        var film = Borders.LayoutOf(SlideshowBorder.Film)!.Value;
        var date = film.Date!.Value;
        Assert.True(date.X >= film.X && date.X + date.Width <= film.X + film.Width + 0.0001f, "within the photo across");
        Assert.True(date.Y >= film.Y && date.Y + date.Height <= film.Y + film.Height + 0.0001f, "within the photo down");
    }

    // ── what every border does to a picture ─────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void EveryBorder_KeepsTheSizeOfThePicture_StaysOpaque_AndDoesNotBreakOnATinyOne(SlideshowBorder border)
    {
        foreach (var (w, h) in new[] { (1, 1), (2, 2), (3, 2), (7, 40), (300, 5), (40, 40), (801, 457) })
        {
            var pixels = Made(border, Rgb(1, 2, 3), w, h);
            Assert.Equal(w * h, pixels.Length);
            Assert.All(pixels, pixel => Assert.Equal(0xFFu, pixel >> 24));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Borders.Apply(border, new uint[3], 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Borders.Apply(border, new uint[4], 0, 4));
        Assert.Throws<ArgumentNullException>(() => Borders.Apply(border, null!, 4, 4));
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void TheMiddleOfThePhoto_IsThePhoto_AndTheCornersOfThePictureAreNot(SlideshowBorder border)
    {
        const int w = 400, h = 300;
        var pixels = Made(border, Blue, w, h);

        Assert.Equal(Blue, pixels[h / 2 * w + w / 2]);
        Assert.NotEqual(Blue, pixels[0]);
        Assert.NotEqual(Blue, pixels[w - 1]);
        Assert.NotEqual(Blue, pixels[(h - 1) * w]);
        Assert.NotEqual(Blue, pixels[(h - 1) * w + w - 1]);

        // The photo is where the layout says: the pixel in the middle of its box is the photo's, and one just outside the box is not
        var layout = Borders.LayoutOf(border)!.Value;
        var insideX = (int)((layout.X + layout.Width * 0.5f) * w);
        Assert.Equal(Blue, pixels[(int)((layout.Y + layout.Height * 0.5f) * h) * w + insideX]);
        Assert.NotEqual(Blue, pixels[(int)((layout.Y * h) - 3) * w + insideX]);
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void ThePhoto_KeepsItsShape_AndItsWholePicture(SlideshowBorder border)
    {
        // Left half red, right half blue: the border shows both, in the same places, smaller
        const int w = 400, h = 240;
        var pixels = new uint[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            pixels[y * w + x] = x < w / 2 ? Rgb(255, 0, 0) : Rgb(0, 0, 255);

        Borders.Apply(border, pixels, w, h);

        var layout = Borders.LayoutOf(border)!.Value;
        var y0 = (int)((layout.Y + layout.Height * 0.5f) * h);
        var (r1, _, _) = Parts(pixels[y0 * w + (int)((layout.X + layout.Width * 0.25f) * w)]);
        var (_, _, b2) = Parts(pixels[y0 * w + (int)((layout.X + layout.Width * 0.75f) * w)]);
        Assert.True(r1 > 250 && b2 > 250, $"{border}: red on the left of the photo, blue on the right");

        // The edge between the colours is in the middle of the photo
        var middle = (int)((layout.X + layout.Width * 0.5f) * w);
        Assert.Equal(Rgb(255, 0, 0), pixels[y0 * w + middle - 2]);
        Assert.Equal(Rgb(0, 0, 255), pixels[y0 * w + middle + 2]);
    }

    [Theory]
    [MemberData(nameof(EveryStyle))]
    public void ABorder_IsTheSameEveryTime(SlideshowBorder border) =>
        Assert.Equal(Made(border, Blue, 320, 200), Made(border, Blue, 320, 200));

    // ── the picture frame ───────────────────────────────────────────────────

    [Fact]
    public void TheFrame_HasAGiltMouldingLitFromTheTopLeft_AndACreamMatInsideIt()
    {
        const int w = 400, h = 300;
        var pixels = Made(SlideshowBorder.Frame, Blue, w, h);

        // Mid-moulding on each side (14 pixels thick: 7 in): gold, brighter at the top than at the left, than at the right, than at the bottom
        var top = pixels[7 * w + w / 2];
        var left = pixels[h / 2 * w + 7];
        var right = pixels[h / 2 * w + w - 1 - 7];
        var bottom = pixels[(h - 1 - 7) * w + w / 2];
        foreach (var gold in new[] { top, left, right, bottom })
        {
            var (r, g, b) = Parts(gold);
            Assert.True(r > g && g > b && r > b * 2, "the moulding is gold: red above green above blue");
        }
        Assert.True(Brightness(top) > Brightness(left) && Brightness(left) > Brightness(right) && Brightness(right) > Brightness(bottom), "lit from the top left");

        // The moulding is a ridge: brightest in the middle, darker at its two edges
        Assert.True(Brightness(pixels[7 * w + w / 2]) > Brightness(pixels[0 * w + w / 2]), "the outer edge is darker than the middle");
        Assert.True(Brightness(pixels[7 * w + w / 2]) > Brightness(pixels[13 * w + w / 2]), "the inner edge is darker than the middle");

        // Inside it, the mat: cream, well clear of the moulding and of the photo (the photo starts at 44, 33)
        Assert.Equal(PictureFrame.MatColour, pixels[h / 2 * w + 25]);
        Assert.Equal(PictureFrame.MatColour, pixels[(h - 1 - 20) * w + w / 2]);
    }

    [Fact]
    public void TheFrame_MitresItsCorners_SoTheTopAndLeftBarsMeetOnTheDiagonal()
    {
        const int w = 400, h = 300;
        var pixels = Made(SlideshowBorder.Frame, Blue, w, h);

        // In the top left corner square, a point above the diagonal is lit as the top bar, one below it as the left bar
        var above = pixels[3 * w + 9];   // nearer to the top edge
        var below = pixels[9 * w + 3];   // nearer to the left edge
        Assert.True(Brightness(above) > Brightness(below));
    }

    [Fact]
    public void ThePhotoInTheFrame_HasAShadowAlongItsTopAndLeftEdge_AndNoneInsideIt()
    {
        const int w = 400, h = 300;
        var pixels = Made(SlideshowBorder.Frame, Blue, w, h);

        var photoTop = 33;
        var photoLeft = 44;
        var edge = Brightness(pixels[photoTop * w + w / 2]);
        var deep = Brightness(pixels[(photoTop + 30) * w + w / 2]);
        Assert.True(edge < deep * 0.8, "the first row of the photo is darker than the picture further in");
        Assert.True(Brightness(pixels[(h / 2) * w + photoLeft]) < deep * 0.8, "and so is its first column");
        Assert.Equal(Blue, pixels[(photoTop + 30) * w + photoLeft + 30]);
    }

    // ── the film ────────────────────────────────────────────────────────────

    [Fact]
    public void TheFilm_IsBlackWithARowOfLightSprocketHolesAboveAndBelowThePhoto()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Film, Blue, w, h);

        // 40-pixel bands, 12 holes 50 pixels apart, in the middle of the band
        Assert.Equal(FilmStrip.HoleColour, pixels[20 * w + 25]);          // the middle of the first hole, above
        Assert.Equal(FilmStrip.HoleColour, pixels[20 * w + 25 + 50 * 5]); // and of the sixth
        Assert.Equal(FilmStrip.HoleColour, pixels[380 * w + 25]);         // below
        Assert.Equal(FilmStrip.FilmColour, pixels[20 * w + 50]);          // between two holes
        Assert.Equal(FilmStrip.FilmColour, pixels[5 * w + 25]);           // above a hole, in the black
        Assert.Equal(FilmStrip.FilmColour, pixels[200 * w + 10]);         // the side of the strip, where the photo is not
        Assert.Equal(FilmStrip.FilmColour, pixels[200 * w + w - 10]);
        Assert.Equal(Blue, pixels[200 * w + 300]);

        // The holes are rounded: the corner of a hole's box is black
        Assert.Equal(FilmStrip.FilmColour, pixels[(20 - 9) * w + 25 - 13]);
    }

    [Fact]
    public void TheHolesOfTheFilm_AreEvenlySpaced_AlongTheWholeWidth()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Film, Blue, w, h);

        var starts = new List<int>();
        for (var x = 0; x < w; x++)
        {
            if (pixels[20 * w + x] == FilmStrip.HoleColour && (x == 0 || pixels[20 * w + x - 1] != FilmStrip.HoleColour))
                starts.Add(x);
        }

        Assert.Equal(12, starts.Count);
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, 49, 51));
        Assert.InRange(starts[0], 5, 20);   // starts a little in from the left edge
        Assert.InRange(w - starts[^1], 20, 45);
    }

    // ── the postage stamp ───────────────────────────────────────────────────

    [Fact]
    public void TheStamp_IsCreamPaperWithRoundNotchesAlongItsEdge_ButNotAtTheCorners()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Stamp, Blue, w, h);

        Assert.Equal(0xFF000000u, pixels[0 * w + 15]);                 // a notch in the top edge (15 pixels from the corner, 15 apart)
        Assert.Equal(0xFF000000u, pixels[0 * w + 15 + 15 * 8]);
        Assert.Equal(0xFF000000u, pixels[(h - 1) * w + 15]);           // and in the bottom one
        Assert.Equal(0xFF000000u, pixels[15 * w + 0]);                 // and the left
        Assert.Equal(0xFF000000u, pixels[15 * w + w - 1]);             // and the right
        Assert.Equal(PostageStamp.PaperColour, pixels[0]);             // the corner itself is paper
        Assert.Equal(PostageStamp.PaperColour, pixels[w - 1]);
        Assert.Equal(PostageStamp.PaperColour, pixels[(h - 1) * w + w - 1]);
        Assert.Equal(PostageStamp.PaperColour, pixels[0 * w + 7]);     // between two notches
        Assert.Equal(PostageStamp.PaperColour, pixels[8 * w + 8]);     // a little in from the edge: paper, not a hole
        Assert.Equal(PostageStamp.PaperColour, pixels[(h - 1 - 20) * w + w / 2]); // the margin below the photo
    }

    [Fact]
    public void TheStamp_HasAFineLineRoundThePhoto_AFewPixelsOut()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Stamp, Blue, w, h);

        // The photo begins at 42, 16 (516 by 344); the line is 3 pixels out (0.7% of 400)
        var line = pixels[(16 - 3) * w + w / 2];
        Assert.NotEqual(PostageStamp.PaperColour, line);
        Assert.NotEqual(Blue, line);
        Assert.True(Brightness(line) < Brightness(PostageStamp.PaperColour) - 100, "the line is dark against the paper");
        Assert.Equal(PostageStamp.PaperColour, pixels[(16 - 8) * w + w / 2]);
    }

    [Fact]
    public void TheStamp_LeavesOutTheNotches_WhenThereIsNoRoomForThem()
    {
        // A picture so small that a notch would be less than a pixel and a half: all paper along the edge
        var pixels = Made(SlideshowBorder.Stamp, Blue, 60, 40);

        Assert.DoesNotContain(0xFF000000u, pixels);
    }

    // ── the glow ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(250, 20, 20, 'R')]
    [InlineData(20, 220, 30, 'G')]
    [InlineData(20, 40, 250, 'B')]
    public void TheGlow_IsTheColourThePhotoHasMostOf(int r, int g, int b, char strongest)
    {
        var glow = Parts(GlowBorder.GlowOf(Uniform(Rgb(r, g, b), 120, 80), 120, 80));
        var channels = new[] { glow.R, glow.G, glow.B };
        var expected = strongest == 'R' ? 0 : strongest == 'G' ? 1 : 2;

        Assert.Equal(255, channels.Max());
        Assert.Equal(channels.Max(), channels[expected]);
        Assert.True(channels.Where((_, i) => i != expected).All(other => other < 120), "the others are weak: it is a vivid colour");
    }

    [Fact]
    public void ThePhotoWithNoColour_GlowsCoolWhiteBlue_AndSoDoesAnEmptyOne()
    {
        foreach (var grey in new[] { Rgb(128, 128, 128), Rgb(255, 255, 255), Rgb(0, 0, 0), Rgb(30, 31, 30) })
            Assert.Equal(GlowBorder.NeutralGlow, GlowBorder.GlowOf(Uniform(grey, 80, 60), 80, 60));
    }

    [Fact]
    public void TheGlow_IsBrightestAtThePhotoAndDiesAwayFromIt_InThePhotosColour()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Glow, Rgb(250, 20, 20), w, h);

        // The photo begins at 42, 28. Just above its top edge, then further, then far
        var near = pixels[(28 - 1) * w + w / 2];
        var middle = pixels[(28 - 6) * w + w / 2];
        var far = pixels[0];
        Assert.True(Brightness(near) > Brightness(middle) && Brightness(middle) > Brightness(far), "fades out");
        Assert.Equal(GlowBorder.GroundColour, far);

        var (r, g, b) = Parts(middle);
        Assert.True(r > g + 40 && r > b + 40, "the glow of a red photo is red");

        // Round a corner it is round, not square: the diagonal point is dimmer than the point at the same distance from the edge in a straight line
        var corner = pixels[(28 - 7) * w + 42 - 7];
        var straight = pixels[(28 - 7) * w + w / 2];
        Assert.True(Brightness(corner) < Brightness(straight));
    }

    // ── the shadow ──────────────────────────────────────────────────────────

    [Fact]
    public void TheShadow_FallsDownAndRight_OfAPhotoThatLiesOnLightPaper()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Shadow, Blue, w, h);

        // The photo is 516 by 344, at 42, 24; its bottom edge is at 367 and its right edge at 557
        var above = pixels[(24 - 3) * w + w / 2];
        var below = pixels[(24 + 344 + 3) * w + w / 2];
        var leftOf = pixels[h / 2 * w + 42 - 3];
        var rightOf = pixels[h / 2 * w + 42 + 516 + 3];
        Assert.True(Brightness(below) < Brightness(above) - 20, "darker below the photo than above it");
        Assert.True(Brightness(rightOf) < Brightness(leftOf) - 20, "darker to the right of the photo than to the left");

        // Light paper, which gets darker towards the corners
        Assert.True(Brightness(pixels[0]) > 600, "light paper");
        Assert.True(Brightness(pixels[0]) < Brightness(ShadowBorder.PaperColour), "a little darker in the corner than in the middle of the paper");
    }

    [Fact]
    public void ThePhotoOnTheShadowPaper_HasRoundedCorners_AndIsCutCleanlyElsewhere()
    {
        const int w = 600, h = 400;
        var pixels = Made(SlideshowBorder.Shadow, Blue, w, h);

        // The first pixel of the photo, at its corner, is paper; a few pixels in along the edge, and in from the corner, it is the photo
        Assert.NotEqual(Blue, pixels[24 * w + 42]);
        Assert.Equal(Blue, pixels[24 * w + 42 + 40]);
        Assert.Equal(Blue, pixels[(24 + 40) * w + 42]);
        Assert.Equal(Blue, pixels[(24 + 12) * w + 42 + 12]);
        Assert.NotEqual(Blue, pixels[(24 + 344 - 1) * w + 42 + 516 - 1]);   // the bottom right corner is rounded too
    }

    // ── a border by chance ──────────────────────────────────────────────────

    [Fact]
    public void ARandomBorder_IsNoneForTwoPhotosInThree_AndForTheRestAnyStyle()
    {
        var keys = Enumerable.Range(0, 3000).Select(i => $"folder\\photo{i}.jpg").ToList();
        var borders = keys.Select(key => RandomStyle.Border(key, 5)).ToList();

        Assert.All(borders, border => Assert.True(border == SlideshowBorder.None || Borders.IsStyle(border)));
        Assert.InRange(borders.Count(border => border != SlideshowBorder.None), 900, 1100);   // one in three

        foreach (var style in Borders.Styles)
            Assert.InRange(borders.Count(border => border == style), 100, 260);               // each of the six about a sixth of that (166)

        // Whether a photo has a border at all is what it was before there were several (the Card rule), and which one it has does not move that
        for (var i = 0; i < keys.Count; i++)
            Assert.Equal(RandomStyle.Card(keys[i], 5), borders[i] != SlideshowBorder.None);
    }

    [Fact]
    public void ARandomBorder_IsTheSameForTheSameFileInTheSameShow_AndAnotherShowChoosesAgain()
    {
        var keys = Enumerable.Range(0, 400).Select(i => $"photo{i}.jpg").ToList();

        Assert.Equal(keys.Select(key => RandomStyle.Border(key, 11)), keys.Select(key => RandomStyle.Border(key, 11)));
        Assert.NotEqual(keys.Select(key => RandomStyle.Border(key, 11)), keys.Select(key => RandomStyle.Border(key, 12)));
    }
}
