namespace PhotoLiker.Stickers.Tests;

public class PlaneResizerTests
{
    [Fact]
    public void The_same_size_gives_a_copy()
    {
        float[] source = [1, 2, 3, 4, 5, 6];

        var copy = PlaneResizer.Resize(source, 1, 3, 2, 3, 2);

        Assert.Equal(source, copy);
        Assert.NotSame(source, copy);
    }

    [Fact]
    public void A_flat_picture_stays_flat_at_any_size()
    {
        var flat = new float[10 * 7 * 3];
        Array.Fill(flat, 123.5f);

        foreach (var (w, h) in new[] { (3, 2), (10, 7), (31, 19), (1, 1) })
        {
            var resized = PlaneResizer.Resize(flat, 3, 10, 7, w, h);
            Assert.Equal(w * h * 3, resized.Length);
            Assert.All(resized, v => Assert.Equal(123.5f, v, 3));
        }
    }

    [Fact]
    public void Shrinking_averages_the_points_it_covers()
    {
        // A row of 0 0 0 0 100 100 100 100 shrunk to 2 points: the filter is twice as wide as the step, so each point is mostly its own half
        float[] row = [0, 0, 0, 0, 100, 100, 100, 100];

        var half = PlaneResizer.Resize(row, 1, 8, 1, 2, 1);

        Assert.InRange(half[0], 10f, 20f);
        Assert.InRange(half[1], 80f, 90f);
        Assert.Equal(100f, half[0] + half[1], 3);   // what one loses the other has

        var one = PlaneResizer.Resize(row, 1, 8, 1, 1, 1);
        Assert.Equal(50, one[0], 1);
    }

    [Fact]
    public void Enlarging_blends_between_the_points()
    {
        float[] row = [0, 100];

        var wide = PlaneResizer.Resize(row, 1, 2, 1, 4, 1);

        // The middle points are between the two and rise from left to right; the ends are the points themselves
        Assert.True(wide[0] < wide[1] && wide[1] < wide[2] && wide[2] < wide[3]);
        Assert.Equal(0, wide[0], 3);
        Assert.Equal(100, wide[3], 3);
        Assert.Equal(25, wide[1], 3);
        Assert.Equal(75, wide[2], 3);
    }

    [Fact]
    public void The_channels_do_not_mix()
    {
        // Two channels: the first is 0 on the left half and 200 on the right; the second is 50 everywhere
        var picture = new float[4 * 2];
        for (var x = 0; x < 4; x++)
        {
            picture[x * 2] = x < 2 ? 0 : 200;
            picture[x * 2 + 1] = 50;
        }

        var small = PlaneResizer.Resize(picture, 2, 4, 1, 2, 1);

        Assert.True(small[0] < small[2], "the first channel still goes from low to high");
        Assert.Equal(50, small[1], 3);   // the second is not touched by the first
        Assert.Equal(50, small[3], 3);
    }

    [Fact]
    public void Bytes_are_rounded_to_the_nearest()
    {
        byte[] pair = [0, 255];

        var one = PlaneResizer.Resize(pair, 1, 2, 1, 1, 1);

        Assert.Equal(128, one[0]); // 127.5 rounds up
    }

    [Fact]
    public void A_picture_that_is_not_the_size_that_was_said_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PlaneResizer.Resize(new float[5], 1, 3, 2, 2, 2));
    }
}

public class GuidedFilterTests
{
    [Fact]
    public void The_box_filter_averages_the_neighbourhood_and_only_the_points_that_are_there()
    {
        // 1 row of 5: 0 10 20 30 40, radius 1
        float[] row = [0, 10, 20, 30, 40];

        var box = GuidedFilter.Box(row, 5, 1, 1);

        Assert.Equal(5f, box[0], 4);    // (0 + 10) / 2: the edge has only two points
        Assert.Equal(10f, box[1], 4);   // (0 + 10 + 20) / 3
        Assert.Equal(20f, box[2], 4);
        Assert.Equal(30f, box[3], 4);
        Assert.Equal(35f, box[4], 4);   // (30 + 40) / 2
    }

    [Fact]
    public void The_box_filter_works_down_the_columns_too()
    {
        // 3 x 3 with a single 9 in the middle: every point is within a radius of 1 of it, so each is the average of its own neighbourhood
        var picture = new float[9];
        picture[4] = 9;

        var box = GuidedFilter.Box(picture, 3, 3, 1);

        Assert.Equal(1f, box[4], 4);       // 9 / 9
        Assert.Equal(9f / 6f, box[1], 4);  // the middle of the top edge has a neighbourhood of 6
        Assert.Equal(9f / 4f, box[0], 4);  // the corner has 4
    }

    [Fact]
    public void The_box_filter_does_not_depend_on_a_radius_larger_than_the_picture()
    {
        float[] row = [3, 6, 9];

        var box = GuidedFilter.Box(row, 3, 1, 50);

        Assert.All(box, v => Assert.Equal(6f, v, 4));
    }

    [Fact]
    public void A_mask_that_is_flat_stays_flat()
    {
        var guide = new float[12 * 12];
        for (var i = 0; i < guide.Length; i++)
            guide[i] = (i % 12) / 12f;
        var mask = new float[12 * 12];
        Array.Fill(mask, 0.7f);

        var result = GuidedFilter.Filter(guide, mask, 12, 12, 2, 0.01f);

        Assert.All(result, v => Assert.Equal(0.7f, v, 3));
    }

    [Fact]
    public void A_soft_mask_is_drawn_to_the_edge_of_the_guide()
    {
        // The guide has a sharp edge between x = 19 and x = 20; the mask has its edge there too but is soft over eight points
        const int width = 40, height = 12;
        var guide = new float[width * height];
        var mask = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                guide[y * width + x] = x < 20 ? 0.1f : 0.9f;
                mask[y * width + x] = Math.Clamp((x - 16) / 8f, 0f, 1f); // 0 at x 16, 1 at x 24
            }
        }

        var result = GuidedFilter.Filter(guide, mask, width, height, 4, 1e-4f);

        float Steepest(float[] values, out int at)
        {
            var best = 0f;
            at = 0;
            for (var x = 0; x < width - 1; x++)
            {
                var step = Math.Abs(values[5 * width + x + 1] - values[5 * width + x]);
                if (step > best)
                {
                    best = step;
                    at = x;
                }
            }
            return best;
        }

        var before = Steepest(mask, out _);
        var after = Steepest(result, out var where);
        Assert.Equal(0.125f, before, 3);
        Assert.True(after > 0.4f, $"the soft slope is a step now: {after}");
        Assert.Equal(19, where);                                          // between x = 19 and x = 20: at the edge of the guide
    }

    [Fact]
    public void The_result_stays_between_nothing_and_everything()
    {
        var random = new Random(7);
        var guide = new float[20 * 20];
        var mask = new float[20 * 20];
        for (var i = 0; i < guide.Length; i++)
        {
            guide[i] = (float)random.NextDouble();
            mask[i] = random.NextDouble() < 0.5 ? 0 : 1;
        }

        var result = GuidedFilter.Filter(guide, mask, 20, 20, 3, 1e-5f);

        Assert.All(result, v => Assert.InRange(v, 0f, 1f));
    }

    [Fact]
    public void A_guide_or_mask_of_the_wrong_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => GuidedFilter.Filter(new float[4], new float[5], 2, 2, 1, 0.01f));
    }
}
