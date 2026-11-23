namespace PhotoLiker.Stickers.Tests;

public class StickerRendererTests
{
    // A photo of a blue-ish square on grey, a mask of the square, and a red outline: every layer has a colour of its own, so a pixel says which layer it is
    private static readonly (byte B, byte G, byte R) Subject = (230, 40, 10);

    /// <summary>
    /// A square subject that fills the sticker exactly: the photo is scaled by 1 (the subject, with the two points of slack the renderer adds on each side, is as wide as the room inside the padding),
    /// so that every point of the sticker is a point of the photo and the edges are one point wide, not the ramp that an enlargement makes.
    /// </summary>
    private static (PhotoPixels Photo, float[] Mask) Square(StickerSettings? settings = null, int photoSize = 600)
    {
        var size = SubjectSize(settings ?? Red(8));
        var left = (photoSize - size) / 2;
        var photo = Make.Photo(photoSize, photoSize, (x, y) => x >= left && x < left + size && y >= left && y < left + size ? Subject : ((byte)128, (byte)128, (byte)128));
        return (photo, Make.Rectangle(photoSize, photoSize, left, left, size, size));
    }

    /// <summary>How wide the square is for the photo not to be scaled: the room inside the padding, less the photo that is taken beyond it (2 points, and for an edge moved outwards the shift and one more).</summary>
    private static int SubjectSize(StickerSettings settings)
    {
        var shift = Math.Max(0, settings.EdgeShift);
        return 512 - 2 * StickerRenderer.PaddingFor(settings) - 2 * (StickerRenderer.Slack + (shift > 0 ? shift + 1 : 0));
    }

    private static StickerSettings Red(int outline, bool shadow = false, int shift = 0) =>
        new() { Outline = outline > 0, OutlineWidth = outline, OutlineColor = "FF0000", Shadow = shadow, EdgeShift = shift };

    private static bool IsSubject((byte B, byte G, byte R, byte A) p) => p is { B: 230, G: 40, R: 10, A: 255 };

    private static bool IsOutline((byte B, byte G, byte R, byte A) p) => p is { B: 0, G: 0, R: 255, A: 255 };

    /// <summary>The runs along a row that match a test: (first, length).</summary>
    private static List<(int First, int Length)> Runs(StickerPicture picture, int y, Func<(byte B, byte G, byte R, byte A), bool> test)
    {
        var runs = new List<(int, int)>();
        var start = -1;
        for (var x = 0; x <= picture.Width; x++)
        {
            var hit = x < picture.Width && test(Make.At(picture, x, y));
            if (hit && start < 0)
                start = x;
            if (!hit && start >= 0)
            {
                runs.Add((start, x - start));
                start = -1;
            }
        }
        return runs;
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(50, 100)]
    [InlineData(333, 333)]
    [InlineData(1000, 10)]
    [InlineData(10, 1000)]
    [InlineData(7, 3)]
    public void The_longer_side_is_exactly_512_and_the_shorter_is_at_most_that(int subjectWidth, int subjectHeight)
    {
        foreach (var settings in new[] { Make.Bare(), new StickerSettings(), new StickerSettings { OutlineWidth = 40, EdgeShift = 10 }, Red(20, shadow: true) })
        {
            var (width, height) = StickerRenderer.SizeFor(subjectWidth, subjectHeight, settings);

            Assert.Equal(512, Math.Max(width, height));
            Assert.InRange(Math.Min(width, height), 1, 512);
        }
    }

    [Fact]
    public void The_room_round_the_subject_is_what_the_settings_ask_for()
    {
        Assert.Equal(StickerRenderer.Margin, StickerRenderer.PaddingFor(Make.Bare()));
        Assert.Equal(StickerRenderer.Margin + 8, StickerRenderer.PaddingFor(new StickerSettings { Shadow = false }));
        Assert.Equal(StickerRenderer.Margin + 8 + StickerRenderer.ShadowRoom, StickerRenderer.PaddingFor(new StickerSettings()));
        Assert.Equal(StickerRenderer.Margin + 4, StickerRenderer.PaddingFor(new StickerSettings { Outline = false, Shadow = false, EdgeShift = 4 }));
        Assert.Equal(StickerRenderer.Margin, StickerRenderer.PaddingFor(new StickerSettings { Outline = false, Shadow = false, EdgeShift = -4 })); // a cut that is taken in needs no room
        Assert.Equal(StickerRenderer.Margin, StickerRenderer.PaddingFor(new StickerSettings { Outline = false, Shadow = false, OutlineWidth = 30 })); // the width of an outline that is off is not room
    }

    [Fact]
    public void The_sticker_has_the_cut_subject_in_its_own_colours_and_nothing_round_it()
    {
        var (photo, mask) = Square(Make.Bare());

        var sticker = StickerRenderer.Render(photo, mask, Make.Bare());

        Assert.Equal(512, Math.Max(sticker.Width, sticker.Height));
        Assert.True(IsSubject(Make.At(sticker, sticker.Width / 2, sticker.Height / 2)));
        Assert.Equal(0, Make.At(sticker, 0, 0).A);
        Assert.Equal(0, Make.At(sticker, sticker.Width - 1, sticker.Height - 1).A);

        // The grey of the photo outside the square is not in the sticker at all
        for (var i = 0; i < sticker.Bgra.Length; i += 4)
        {
            if (sticker.Bgra[i + 3] != 0)
                Assert.NotEqual((byte)128, sticker.Bgra[i]);
        }
    }

    [Fact]
    public void A_square_subject_gives_a_square_sticker_and_a_wide_one_a_wide_sticker()
    {
        var (photo, mask) = Square(Make.Bare());
        var square = StickerRenderer.Render(photo, mask, Make.Bare());

        var wide = StickerRenderer.Render(Make.Plain(200, 100), Make.Rectangle(200, 100, 20, 20, 160, 40), Make.Bare());

        Assert.Equal(square.Width, square.Height);
        Assert.Equal(512, wide.Width);
        Assert.InRange(wide.Height, 136, 144);   // 160 x 40 is 4 : 1: 508 across (the room inside the margin) is about 127 down, plus the margin
    }

    [Fact]
    public void A_cutout_with_nothing_in_it_is_refused_with_its_reason()
    {
        var ex = Assert.Throws<StickerException>(() => StickerRenderer.Render(Make.Plain(40, 40), new float[40 * 40], new StickerSettings()));

        Assert.Equal(StickerFailure.NoSubject, ex.Reason);
    }

    [Fact]
    public void A_cutout_that_is_only_a_soft_trace_is_nothing()
    {
        var faint = new float[40 * 40];
        Array.Fill(faint, 0.3f);

        Assert.Throws<StickerException>(() => StickerRenderer.Render(Make.Plain(40, 40), faint, new StickerSettings()));
    }

    [Fact]
    public void A_cutout_of_another_size_than_the_photo_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StickerRenderer.Render(Make.Plain(40, 40), new float[10], new StickerSettings()));
    }

    /// <summary>How many points of a row, from <paramref name="from"/> going in <paramref name="step"/> (1 or -1), are not see-through before the first that is.</summary>
    private static int Extent(StickerPicture picture, int from, int y, int step)
    {
        var length = 0;
        for (var x = from; x >= 0 && x < picture.Width && Make.At(picture, x, y).A > 0; x += step)
            length++;
        return length;
    }

    [Fact]
    public void The_outline_is_as_wide_as_it_was_asked_to_be()
    {
        var settings = Red(8);
        var (photo, mask) = Square(settings);
        var sticker = StickerRenderer.Render(photo, mask, settings);

        var y = sticker.Height / 2;
        var subject = Runs(sticker, y, IsSubject);
        Assert.Single(subject);
        var left = subject[0].First - 1;
        var right = subject[0].First + subject[0].Length;

        // From the subject outwards, one point to a pixel since the photo is not scaled: exactly 8 points of outline, then nothing
        Assert.Equal(8, Extent(sticker, left, y, -1));
        Assert.Equal(8, Extent(sticker, right, y, 1));
        Assert.True(IsOutline(Make.At(sticker, left - 3, y)));
        Assert.True(IsOutline(Make.At(sticker, right + 3, y)));
        Assert.Equal(0, Make.At(sticker, left - 12, y).A);
        Assert.Equal(0, Make.At(sticker, right + 12, y).A);
    }

    [Fact]
    public void The_outline_is_as_wide_above_and_below_as_at_the_sides()
    {
        var settings = Red(12);
        var (photo, mask) = Square(settings);
        var sticker = StickerRenderer.Render(photo, mask, settings);

        var x = sticker.Width / 2;
        var y = sticker.Height / 2;
        while (IsSubject(Make.At(sticker, x, y)))
            y--;
        var above = 0;
        for (var at = y; at >= 0 && Make.At(sticker, x, at).A > 0; at--)
            above++;

        var down = sticker.Height / 2;
        while (IsSubject(Make.At(sticker, x, down)))
            down++;
        var below = 0;
        for (var at = down; at < sticker.Height && Make.At(sticker, x, at).A > 0; at++)
            below++;

        Assert.Equal(12, above);
        Assert.Equal(12, below);
    }

    [Fact]
    public void Without_an_outline_there_is_none_and_the_width_that_is_set_does_not_matter()
    {
        var settings = new StickerSettings { Outline = false, OutlineWidth = 30, OutlineColor = "FF0000", Shadow = false };
        var (photo, mask) = Square(settings);

        var sticker = StickerRenderer.Render(photo, mask, settings);

        var y = sticker.Height / 2;
        Assert.Empty(Runs(sticker, y, IsOutline));
        var subject = Runs(sticker, y, IsSubject)[0];
        Assert.InRange(Extent(sticker, subject.First - 1, y, -1), 0, 2);   // only the soft point of the edge
    }

    [Fact]
    public void An_outline_of_width_zero_is_no_outline()
    {
        var (photo, mask) = Square(Red(0));

        var sticker = StickerRenderer.Render(photo, mask, Red(0));

        Assert.Empty(Runs(sticker, sticker.Height / 2, IsOutline));
    }

    [Fact]
    public void The_outline_is_the_colour_that_was_chosen()
    {
        var settings = new StickerSettings { OutlineWidth = 8, OutlineColor = "1090F0", Shadow = false };
        var (photo, mask) = Square(settings);

        var sticker = StickerRenderer.Render(photo, mask, settings);

        var y = sticker.Height / 2;
        var edge = Runs(sticker, y, IsSubject)[0].First - 5;   // in the middle of the outline
        var (b, g, r, a) = Make.At(sticker, edge, y);
        Assert.Equal((byte)0xF0, b);
        Assert.Equal((byte)0x90, g);
        Assert.Equal((byte)0x10, r);
        Assert.Equal((byte)255, a);
    }

    [Fact]
    public void A_notch_that_is_too_narrow_for_the_outline_to_follow_is_filled_so_the_line_is_smooth()
    {
        // Two blocks of one subject with a gap between them that is wider than twice the outline (16) by more than a little: a plain grow leaves a slit of 6 points, and the outline of a sticker has none
        // (the smoothing closes what is up to twice its own width, 8, so a closing that smooths less than the half of the outline would leave this one open)
        const int width = 560, height = 160;
        var photo = Make.Photo(width, height, (x, y) => (x is >= 20 and < 270 || x is >= 294 and < 540) && y is >= 30 and < 130 ? Subject : ((byte)128, (byte)128, (byte)128));
        var mask = new float[width * height];
        for (var y = 30; y < 130; y++)
        {
            for (var x = 20; x < 540; x++)
                mask[y * width + x] = x is < 270 or >= 294 ? 1 : 0;
        }

        var sticker = StickerRenderer.Render(photo, mask, Red(8));

        var y2 = sticker.Height / 2;
        var subject = Runs(sticker, y2, IsSubject);
        Assert.Equal(2, subject.Count);
        var gap = subject[1].First - (subject[0].First + subject[0].Length);
        Assert.InRange(gap, 21, 26);                                    // 24 points, a little less in the sticker (the photo is scaled by 0.94): wider than the two outlines (16) by 6
        for (var x = subject[0].First + subject[0].Length + 1; x < subject[1].First - 1; x++) // (not the soft point beside each block)
            Assert.True(IsOutline(Make.At(sticker, x, y2)), $"point {x} of the gap is not outline: {Make.At(sticker, x, y2)}");
    }

    [Fact]
    public void A_wide_gap_stays_open()
    {
        // The same, with the gap far wider than the outline: there is transparent room between the two
        const int width = 560, height = 160;
        var photo = Make.Plain(width, height);
        var mask = new float[width * height];
        for (var y = 30; y < 130; y++)
        {
            for (var x = 20; x < 540; x++)
                mask[y * width + x] = x is < 200 or >= 360 ? 1 : 0;
        }

        var sticker = StickerRenderer.Render(photo, mask, Red(8));

        var row = sticker.Height / 2;
        var middle = sticker.Width / 2;
        Assert.Equal(0, Make.At(sticker, middle, row).A);
    }

    [Fact]
    public void The_shadow_is_under_the_sticker_and_darker_below_it_than_above()
    {
        var settings = new StickerSettings { Outline = false, Shadow = true };
        var (photo, mask) = Square(settings);
        var sticker = StickerRenderer.Render(photo, mask, settings);

        var x = sticker.Width / 2;
        var top = 0;
        while (!IsSubject(Make.At(sticker, x, top)))
            top++;
        var bottom = sticker.Height - 1;
        while (!IsSubject(Make.At(sticker, x, bottom)))
            bottom--;

        var below = Make.At(sticker, x, bottom + 4);
        var above = Make.At(sticker, x, top - 4);
        Assert.True(below.A > 0, "there is a shadow below");
        Assert.True(below.A > above.A, $"it is darker below ({below.A}) than above ({above.A})");
        Assert.True(below.A <= (int)(StickerRenderer.ShadowOpacity * 255) + 1, "and never darker than its opacity");
        Assert.Equal((0, 0, 0), (below.B, below.G, below.R));              // black
    }

    [Fact]
    public void Without_a_shadow_there_is_nothing_round_the_subject()
    {
        var (photo, mask) = Square(new StickerSettings { Outline = false, Shadow = false });

        var sticker = StickerRenderer.Render(photo, mask, new StickerSettings { Outline = false, Shadow = false });

        var y = sticker.Height / 2;
        var subject = Runs(sticker, y, IsSubject)[0];
        Assert.Equal(0, Make.At(sticker, subject.First - 2, y).A);
        Assert.Equal(0, Make.At(sticker, subject.First + subject.Length + 1, y).A);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(-4)]
    public void A_shift_of_the_edge_moves_it_by_that_many_pixels_of_the_sticker_on_each_side(int shift)
    {
        var settings = Red(0, shift: shift);
        var (photo, mask) = Square(settings);
        var size = SubjectSize(settings);   // how wide the subject is in the sticker, since the photo is not scaled

        var sticker = StickerRenderer.Render(photo, mask, settings);

        var y = sticker.Height / 2;
        var opaque = Enumerable.Range(0, sticker.Width).Count(x => Make.At(sticker, x, y).A >= 128);
        Assert.InRange(opaque - size, 2 * shift - 1, 2 * shift + 1);
    }

    [Fact]
    public void A_shift_grows_the_sticker_into_the_photo_it_took_the_subject_from()
    {
        // The grown edge shows the colour that the photo has there: the grey that was round the square
        var settings = Red(0, shift: 6);
        var (photo, mask) = Square(settings);

        var sticker = StickerRenderer.Render(photo, mask, settings);

        var y = sticker.Height / 2;
        var subject = Runs(sticker, y, IsSubject)[0];
        var (b, g, r, a) = Make.At(sticker, subject.First - 3, y);
        Assert.Equal((byte)255, a);
        Assert.Equal((128, 128, 128), (b, g, r));
    }

    [Fact]
    public void A_shift_that_takes_the_edge_in_takes_the_fringe_off()
    {
        var settings = Red(0, shift: -3);
        var (photo, mask) = Square(settings);

        var sticker = StickerRenderer.Render(photo, mask, settings);

        // What remains is all subject: no grey from the photo's background is left at the edge
        var y = sticker.Height / 2;
        for (var x = 0; x < sticker.Width; x++)
        {
            var p = Make.At(sticker, x, y);
            if (p.A == 255)
                Assert.True(IsSubject(p));
        }
    }

    [Fact]
    public void A_soft_edge_keeps_the_colour_of_the_photo_while_it_is_half_see_through()
    {
        // The alpha is straight, not multiplied into the colour: a point that is half in is the photo's colour at half strength
        const int size = 100;
        var photo = Make.Photo(size, size, (_, _) => Subject);
        var mask = new float[size * size];
        for (var y = 30; y < 70; y++)
        {
            for (var x = 30; x < 70; x++)
                mask[y * size + x] = 1;
        }
        for (var y = 25; y < 75; y++)
        {
            for (var x = 25; x < 75; x++)
            {
                if (mask[y * size + x] == 0)
                    mask[y * size + x] = 0.5f;   // a soft ring of 5 points
            }
        }

        var sticker = StickerRenderer.Render(photo, mask, Make.Bare());

        var middle = sticker.Height / 2;
        var half = Enumerable.Range(0, sticker.Width).Select(x => Make.At(sticker, x, middle)).Where(p => p.A is > 20 and < 240).ToList();
        Assert.NotEmpty(half);
        Assert.All(half, p => Assert.Equal(Subject, (p.B, p.G, p.R)));
    }

    [Fact]
    public void Points_that_are_not_there_have_no_colour_either()
    {
        var settings = new StickerSettings();
        var (photo, mask) = Square(settings);

        var sticker = StickerRenderer.Render(photo, mask, settings);

        for (var i = 0; i < sticker.Bgra.Length; i += 4)
        {
            if (sticker.Bgra[i + 3] == 0)
                Assert.Equal(0, sticker.Bgra[i] + sticker.Bgra[i + 1] + sticker.Bgra[i + 2]);
        }
    }

    [Fact]
    public void A_subject_at_the_edge_of_the_photo_still_has_its_outline()
    {
        // The people in a photo are often cut by its edge; the sticker still has room for the outline all round them
        var photo = Make.Photo(100, 100, (_, _) => Subject);
        var mask = Make.Rectangle(100, 100, 0, 0, 100, 100);

        var sticker = StickerRenderer.Render(photo, mask, new StickerSettings { OutlineColor = "FF0000", Shadow = false });

        Assert.Equal(512, Math.Max(sticker.Width, sticker.Height));
        var x = sticker.Width / 2;
        var y = 0;
        while (!IsSubject(Make.At(sticker, x, y)))
            y++;
        Assert.True(IsOutline(Make.At(sticker, x, y - 8)), "the outline is above the subject, where the photo ends");   // (the photo is enlarged five times, so its edge is a ramp of about five points)

        var row = sticker.Height / 2;
        var edge = 0;
        while (!IsSubject(Make.At(sticker, edge, row)))
            edge++;
        Assert.True(IsOutline(Make.At(sticker, edge - 8, row)), "and at its side");
        Assert.Equal(0, Make.At(sticker, 0, 0).A);
    }

    [Fact]
    public void The_bounds_of_a_cutout_are_its_points_that_are_in()
    {
        var mask = Make.Rectangle(30, 20, 4, 6, 10, 5);
        mask[1 * 30 + 1] = 0.4f;   // a faint point is not in

        Assert.Equal((4, 6, 13, 10), StickerRenderer.BoundsOf(mask, 30, 20));
        Assert.Null(StickerRenderer.BoundsOf(new float[600], 30, 20));
    }

    [Fact]
    public void The_whole_picture_as_a_sticker_is_its_rounded_rectangle()
    {
        var photo = Make.Photo(160, 100, (_, _) => Subject);

        var sticker = StickerRenderer.Render(photo, Cutout.Shape(photo, null, new StickerSettings { Cutout = CutoutMode.WholePicture }), Make.Bare());

        Assert.Equal(512, sticker.Width);
        Assert.True(IsSubject(Make.At(sticker, sticker.Width / 2, sticker.Height / 2)));
        Assert.True(Make.At(sticker, StickerRenderer.Margin, StickerRenderer.Margin).A < 30);   // the corner is rounded off
    }
}
