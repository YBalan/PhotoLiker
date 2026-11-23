namespace PhotoLiker.Stickers.Tests;

public class CutoutTests
{
    [Fact]
    public void The_models_map_is_made_as_large_as_the_photo_and_stays_between_nothing_and_everything()
    {
        var map = Make.Map(2, 2, [0f, 1f, 1f, 0f]);

        var mask = Cutout.Enlarge(map, 8, 6);

        Assert.Equal(48, mask.Length);
        Assert.All(mask, v => Assert.InRange(v, 0f, 1f));
        Assert.True(mask[0] < 0.2f);            // the corner that is 0
        Assert.True(mask[5] > 0.8f);            // the top right corner that is 1
    }

    [Fact]
    public void A_flat_map_stays_flat_when_enlarged()
    {
        var values = new float[16];
        Array.Fill(values, 0.4f);

        var mask = Cutout.Enlarge(Make.Map(4, 4, values), 37, 23);

        Assert.All(mask, v => Assert.Equal(0.4f, v, 3));
    }

    [Fact]
    public void The_cut_leaves_nothing_below_its_band_and_everything_above_it()
    {
        float[] mask = [0f, 0.2f, 0.38f, 0.5f, 0.62f, 0.8f, 1f];

        var cut = Cutout.Cut(mask, 0.5f);

        Assert.Equal(0f, cut[0]);
        Assert.Equal(0f, cut[1]);
        Assert.Equal(0f, cut[2], 4);
        Assert.Equal(0.5f, cut[3], 4);   // exactly at the cutoff: half
        Assert.Equal(1f, cut[4], 4);
        Assert.Equal(1f, cut[5]);
        Assert.Equal(1f, cut[6]);
    }

    [Fact]
    public void The_cut_follows_the_cutoff()
    {
        float[] mask = [0.3f];

        Assert.Equal(1f, Cutout.Cut(mask, 0.1f)[0]);   // a low cutoff keeps what is 0.3 sure
        Assert.Equal(0f, Cutout.Cut(mask, 0.9f)[0]);   // a high one does not
    }

    [Fact]
    public void A_higher_sensitivity_keeps_more_of_the_subject()
    {
        // A map that rises from 0 on the left to 1 on the right: the more sensitive the cut, the further left it keeps
        const int width = 64, height = 8;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                values[y * width + x] = x / (float)(width - 1);
        }
        var photo = Make.Plain(width, height);
        var map = Make.Map(width, height, values);

        int Area(int sensitivity) => Make.Count(Cutout.Shape(photo, map, new StickerSettings { Sensitivity = sensitivity, SmallParts = 0, FollowEdges = 0 }));

        Assert.True(Area(80) > Area(50), "more sensitive: more is kept");
        Assert.True(Area(50) > Area(20));
        Assert.InRange(Area(50), width * height / 2 - 40, width * height / 2 + 40); // half way: about half of the map is more than half sure
    }

    [Fact]
    public void A_small_part_is_dropped_with_its_fringe_and_a_big_one_stays()
    {
        const int width = 60, height = 40;
        var mask = Make.Rectangle(width, height, 5, 5, 20, 20);                    // the big part: 400 points
        for (var y = 30; y < 33; y++)
        {
            for (var x = 45; x < 48; x++)
                mask[y * width + x] = 1;                                           // a small one: 9 points
        }
        mask[31 * width + 49] = 0.3f;                                              // a soft point next to the small one
        mask[10 * width + 25] = 0.3f;                                              // and one next to the big one

        var result = Cutout.DropSmallParts(mask, width, height, 0.1);              // under 40 points goes

        Assert.Equal(400, Make.Count(result));
        Assert.Equal(0f, result[31 * width + 46]);
        Assert.Equal(0f, result[31 * width + 49]);
        Assert.Equal(1f, result[10 * width + 10]);
        Assert.Equal(0.3f, result[10 * width + 25]);                               // the fringe of the part that stays stays
    }

    [Fact]
    public void A_part_is_kept_when_it_is_exactly_the_fraction_of_the_biggest()
    {
        const int width = 60, height = 40;
        var mask = Make.Rectangle(width, height, 2, 2, 10, 10);                    // 100 points
        var small = Make.Rectangle(width, height, 40, 25, 5, 5);                   // 25 points: a quarter of the biggest
        for (var i = 0; i < mask.Length; i++)
            mask[i] = Math.Max(mask[i], small[i]);

        Assert.Equal(125, Make.Count(Cutout.DropSmallParts(mask, width, height, 0.25)));  // exactly a quarter: kept
        Assert.Equal(100, Make.Count(Cutout.DropSmallParts(mask, width, height, 0.26)));  // a little more than a quarter: dropped
    }

    [Fact]
    public void Nothing_is_dropped_when_the_fraction_is_zero_or_there_is_one_part()
    {
        const int width = 30, height = 20;
        var two = Make.Rectangle(width, height, 2, 2, 12, 12);
        two[15 * width + 25] = 1;
        var one = Make.Rectangle(width, height, 2, 2, 12, 12);

        Assert.Same(two, Cutout.DropSmallParts(two, width, height, 0));
        Assert.Same(one, Cutout.DropSmallParts(one, width, height, 0.9));
        Assert.Equal(Make.Count(two), Make.Count(Cutout.DropSmallParts(two, width, height, -1)));
    }

    [Fact]
    public void Parts_that_are_half_as_big_as_the_biggest_stay_at_the_default_fraction()
    {
        // Two people side by side: the second is half the size of the first, and both are the sticker
        const int width = 80, height = 30;
        var mask = Make.Rectangle(width, height, 2, 2, 20, 20);
        var second = Make.Rectangle(width, height, 40, 2, 14, 14);
        for (var i = 0; i < mask.Length; i++)
            mask[i] = Math.Max(mask[i], second[i]);

        var result = Cutout.DropSmallParts(mask, width, height, StickerSettings.DefaultSmallParts / 100.0);

        Assert.Equal(400 + 196, Make.Count(result));
    }

    [Fact]
    public void A_small_hole_in_the_subject_is_filled_and_a_big_one_is_not()
    {
        const int width = 60, height = 60;
        var mask = Make.Rectangle(width, height, 5, 5, 50, 50);                    // 2500 points: a hole may be up to 10
        mask[20 * width + 20] = 0;                                                 // a hole of 1 point
        for (var y = 40; y < 44; y++)
        {
            for (var x = 40; x < 44; x++)
                mask[y * width + x] = 0;                                           // a hole of 16 points
        }

        var result = Cutout.FillSmallHoles(mask, width, height);

        Assert.Equal(1f, result[20 * width + 20]);
        Assert.Equal(0f, result[41 * width + 41]);
        Assert.Equal(0f, result[0]);                                               // the outside is not a hole
    }

    [Fact]
    public void Background_that_reaches_the_edge_of_the_photo_is_not_a_hole()
    {
        // A channel of 18 points that is open to the left edge of the photo, in a subject of 10000 (a hole may be up to 40): small enough to be a hole, but it is the outside
        const int width = 120, height = 120;
        var mask = Make.Rectangle(width, height, 0, 5, 100, 100);
        for (var y = 50; y < 53; y++)
        {
            for (var x = 0; x < 6; x++)
                mask[y * width + x] = 0;
        }

        var result = Cutout.FillSmallHoles(mask, width, height);

        Assert.Equal(0f, result[51 * width + 2]);
        Assert.Equal(Make.Count(mask), Make.Count(result));
    }

    [Fact]
    public void Holes_that_touch_only_at_a_corner_are_two_holes()
    {
        // Two holes of 6 points that touch at a corner, in a subject of 2500 (a hole may be up to 10): as two holes each is small and is filled; if they were one hole it would be 12 and stay
        const int width = 60, height = 60;
        var mask = Make.Rectangle(width, height, 5, 5, 50, 50);
        for (var y = 20; y < 22; y++)
        {
            for (var x = 20; x < 23; x++)
                mask[y * width + x] = 0;       // x 20..22, y 20..21
        }
        for (var y = 22; y < 24; y++)
        {
            for (var x = 23; x < 26; x++)
                mask[y * width + x] = 0;       // x 23..25, y 22..23: touches the first at the corner (22, 21) - (23, 22)
        }

        var result = Cutout.FillSmallHoles(mask, width, height);

        Assert.Equal(1f, result[20 * width + 20]);
        Assert.Equal(1f, result[23 * width + 25]);
        Assert.Equal(Make.Count(mask) + 12, Make.Count(result));
    }

    [Fact]
    public void A_cutout_with_nothing_in_it_is_left_as_it_is()
    {
        var empty = new float[20 * 20];

        Assert.Same(empty, Cutout.FillSmallHoles(empty, 20, 20));
        Assert.Same(empty, Cutout.DropSmallParts(empty, 20, 20, 0.5));
    }

    [Fact]
    public void Growing_a_set_adds_a_square_of_the_distance_round_it()
    {
        const int width = 11, height = 11;
        var set = new bool[width * height];
        set[5 * width + 5] = true;

        var grown = Cutout.Grow(set, width, height, 2);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                Assert.Equal(Math.Abs(x - 5) <= 2 && Math.Abs(y - 5) <= 2, grown[y * width + x]);
        }
    }

    [Fact]
    public void Growing_a_set_at_the_edge_of_the_picture_does_not_overflow()
    {
        var set = new bool[5 * 5];
        set[0] = true;

        var grown = Cutout.Grow(set, 5, 5, 1);

        Assert.Equal(4, grown.Count(v => v));
        Assert.DoesNotContain(true, Cutout.Grow(new bool[25], 5, 5, 3)); // nothing grows from nothing
    }

    [Fact]
    public void The_whole_picture_is_a_rounded_rectangle()
    {
        var mask = Cutout.RoundedRectangle(120, 60);   // the corners have a radius of 5

        Assert.True(mask[0] < 0.01f);                  // the corner point is outside the curve
        Assert.Equal(1f, mask[30 * 120 + 60]);         // the middle
        Assert.Equal(1f, mask[0 * 120 + 60]);          // the middle of the top edge is inside: the sides are straight
        Assert.Equal(1f, mask[30 * 120 + 0]);          // and the middle of the left edge
        Assert.True(mask[5 * 120 + 5] > 0.5f);         // a corner pixel that is five points in is inside
        Assert.Equal(mask[0], mask[119], 5);           // the same at the other corner
        Assert.Equal(mask[0], mask[59 * 120], 5);
        Assert.Equal(mask[0], mask[59 * 120 + 119], 5);
    }

    [Fact]
    public void The_whole_picture_needs_no_map_and_a_map_that_is_given_is_not_looked_at()
    {
        var photo = Make.Plain(40, 30);
        var settings = new StickerSettings { Cutout = CutoutMode.WholePicture };

        var without = Cutout.Shape(photo, null, settings);
        var with = Cutout.Shape(photo, Make.Map(2, 2, [0f, 0f, 0f, 0f]), settings);

        Assert.Equal(without, with);
        Assert.Equal(1f, without[15 * 40 + 20]);
    }

    [Fact]
    public void Finding_the_subject_needs_the_map()
    {
        Assert.Throws<ArgumentNullException>(() => Cutout.Shape(Make.Plain(10, 10), null, new StickerSettings()));
    }

    [Fact]
    public void The_edge_snap_draws_a_soft_mask_to_the_edge_in_the_photo()
    {
        // The photo is dark on the left and bright from x = 30; the mask is soft over four points (as the model's is, once it is enlarged) and centred on that edge
        const int width = 60, height = 20;
        var photo = Make.Photo(width, height, (x, _) => x < 30 ? ((byte)20, (byte)20, (byte)20) : ((byte)230, (byte)230, (byte)230));
        var soft = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                soft[y * width + x] = Math.Clamp((x - 28) / 4f, 0f, 1f);
        }

        var snapped = Cutout.SnapToEdges(photo, soft, 100);

        float Steepest(float[] values)
        {
            var best = 0f;
            for (var x = 0; x < width - 1; x++)
                best = Math.Max(best, Math.Abs(values[10 * width + x + 1] - values[10 * width + x]));
            return best;
        }

        Assert.Equal(0.25f, Steepest(soft), 3);
        Assert.True(Steepest(snapped) > 0.45f, $"the slope is steeper at the edge of the photo: {Steepest(snapped)}");
    }

    [Fact]
    public void The_subject_that_is_a_disc_comes_out_as_a_disc_of_the_same_size()
    {
        // A white disc on a black photo, and the model's map of it on a grid a fifth the size, blurred a little as a model's map is
        const int width = 200, height = 160, coarse = 40;
        var photo = Make.Photo(width, height, (x, y) =>
            (x - 100) * (x - 100) + (y - 80) * (y - 80) <= 50 * 50 ? ((byte)240, (byte)240, (byte)240) : ((byte)15, (byte)15, (byte)15));
        var coarseHeight = height / 5;                  // 40 x 32
        var values = new float[coarse * coarseHeight];
        for (var y = 0; y < coarseHeight; y++)
        {
            for (var x = 0; x < coarse; x++)
            {
                var distance = Math.Sqrt((x + 0.5 - 20) * (x + 0.5 - 20) + (y + 0.5 - 16) * (y + 0.5 - 16));
                values[y * coarse + x] = (float)Math.Clamp((10 - distance) / 3 + 0.5, 0, 1);
            }
        }

        var mask = Cutout.Shape(photo, Make.Map(coarse, coarseHeight, values), new StickerSettings());

        var area = Make.Count(mask);
        Assert.InRange(area, (int)(Math.PI * 50 * 50 * 0.93), (int)(Math.PI * 50 * 50 * 1.07));
        Assert.Equal(1f, mask[80 * width + 100], 2);
        Assert.Equal(0f, mask[0], 2);
        Assert.Equal(0f, mask[(height - 1) * width + width - 1], 2);
    }
}
