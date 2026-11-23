namespace PhotoLiker.Stickers.Tests;

public class DistanceTransformTests
{
    [Fact]
    public void A_single_point_gives_the_straight_line_distance_to_it()
    {
        var set = new bool[7 * 5];
        set[2 * 7 + 3] = true; // x 3, y 2

        var distance = DistanceTransform.To(set, 7, 5);

        Assert.Equal(0f, distance[2 * 7 + 3]);
        Assert.Equal(1f, distance[2 * 7 + 4], 4);
        Assert.Equal(3f, distance[2 * 7 + 6], 4);
        Assert.Equal(2f, distance[0 * 7 + 3], 4);
        Assert.Equal(Math.Sqrt(2), distance[3 * 7 + 4], 4);       // a diagonal neighbour
        Assert.Equal(Math.Sqrt(9 + 4), distance[0 * 7 + 0], 4);   // the corner: 3 across, 2 down
    }

    [Fact]
    public void The_distance_is_to_the_nearest_of_several_points()
    {
        var set = new bool[10];
        set[1] = true;
        set[8] = true;

        var distance = DistanceTransform.To(set, 10, 1);

        Assert.Equal([1f, 0f, 1f, 2f, 3f, 3f, 2f, 1f, 0f, 1f], distance);
    }

    [Fact]
    public void A_set_that_is_empty_has_no_distance_anywhere()
    {
        var distance = DistanceTransform.To(new bool[6 * 4], 6, 4);

        Assert.All(distance, d => Assert.Equal(DistanceTransform.None, d));
    }

    [Fact]
    public void A_set_that_is_everything_has_distance_zero_everywhere()
    {
        var all = new bool[5 * 3];
        Array.Fill(all, true);

        Assert.All(DistanceTransform.To(all, 5, 3), d => Assert.Equal(0f, d));
    }

    [Fact]
    public void It_agrees_with_a_brute_force_search_on_random_sets()
    {
        var random = new Random(1234);
        const int width = 23, height = 17;
        for (var round = 0; round < 6; round++)
        {
            var set = new bool[width * height];
            for (var i = 0; i < set.Length; i++)
                set[i] = random.NextDouble() < 0.04 + round * 0.03;
            if (!set.Any(v => v))
                set[random.Next(set.Length)] = true;

            var fast = DistanceTransform.To(set, width, height);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var best = double.MaxValue;
                    for (var sy = 0; sy < height; sy++)
                    {
                        for (var sx = 0; sx < width; sx++)
                        {
                            if (set[sy * width + sx])
                                best = Math.Min(best, Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (double)(y - sy)));
                        }
                    }
                    Assert.Equal(best, fast[y * width + x], 3);
                }
            }
        }
    }

    [Fact]
    public void A_wrong_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => DistanceTransform.To(new bool[5], 2, 2));
    }
}

public class ConnectedPartsTests
{
    private static bool[] Grid(params string[] rows) => rows.SelectMany(row => row.Select(c => c == '#')).ToArray();

    [Fact]
    public void Separate_blobs_are_separate_parts_with_their_areas()
    {
        var on = Grid(
            "##..#",
            "##..#",
            "....#",
            ".#...");

        var parts = ConnectedParts.Find(on, 5, 4, eightNeighbours: true);

        Assert.Equal(3, parts.Count);
        Assert.Equal(4, parts.Areas[parts.Labels[0]]);   // the square
        Assert.Equal(3, parts.Areas[parts.Labels[4]]);   // the column on the right
        Assert.Equal(1, parts.Areas[parts.Labels[16]]);  // the lone point (x 1, y 3)
        Assert.Equal(20 - 8, parts.Areas[0]);            // the points that are off
        Assert.Equal(0, parts.Labels[2]);
    }

    [Fact]
    public void Points_that_touch_at_a_corner_are_one_part_only_with_eight_neighbours()
    {
        var on = Grid(
            "#.",
            ".#");

        Assert.Equal(1, ConnectedParts.Find(on, 2, 2, eightNeighbours: true).Count);
        Assert.Equal(2, ConnectedParts.Find(on, 2, 2, eightNeighbours: false).Count);
    }

    [Fact]
    public void A_snake_that_winds_is_one_part()
    {
        var on = Grid(
            "#####",
            "....#",
            "#####",
            "#....",
            "#####");

        var parts = ConnectedParts.Find(on, 5, 5, eightNeighbours: false);

        Assert.Equal(1, parts.Count);
        Assert.Equal(5 + 1 + 5 + 1 + 5, parts.Areas[1]);
    }

    [Fact]
    public void Nothing_on_means_no_parts()
    {
        var parts = ConnectedParts.Find(new bool[12], 4, 3, eightNeighbours: true);

        Assert.Equal(0, parts.Count);
        Assert.Equal(12, parts.Areas[0]);
    }

    [Fact]
    public void The_parts_that_touch_the_edge_are_named()
    {
        var on = Grid(
            "#....",
            ".....",
            "..#..",
            ".....",
            "....#");

        var parts = ConnectedParts.Find(on, 5, 5, eightNeighbours: true);
        var touching = parts.TouchingEdge(5, 5);

        Assert.Equal(2, touching.Count);
        Assert.Contains(parts.Labels[0], touching);
        Assert.Contains(parts.Labels[24], touching);
        Assert.DoesNotContain(parts.Labels[12], touching);
    }

    [Fact]
    public void A_large_picture_does_not_overflow_the_stack()
    {
        // Filling is done with a stack of its own, not by calling itself: a part that is a million points long is no trouble
        var on = new bool[1000 * 1000];
        Array.Fill(on, true);

        var parts = ConnectedParts.Find(on, 1000, 1000, eightNeighbours: false);

        Assert.Equal(1, parts.Count);
        Assert.Equal(1_000_000, parts.Areas[1]);
    }
}
