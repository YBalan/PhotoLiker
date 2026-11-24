using System.Drawing;
using PhotoLiker.Drawing.Ocr;

namespace PhotoLiker.Drawing.Tests;

public sealed class CharacterSetTests
{
    /// <summary>The answers of a model for a line, one slice for each index: the likeliest answer of the slice is the index given (probability 0.9, the rest shared).</summary>
    private static float[] Answers(int count, params int[] likeliest)
    {
        var answers = new float[likeliest.Length * count];
        for (var t = 0; t < likeliest.Length; t++)
        {
            for (var c = 0; c < count; c++)
                answers[t * count + c] = 0.1f / (count - 1);
            answers[t * count + likeliest[t]] = 0.9f;
        }
        return answers;
    }

    [Fact]
    public void The_set_has_nothing_the_letters_and_the_space()
    {
        var set = new CharacterSet("a\nb\nc\n");

        Assert.Equal(5, set.Count);
    }

    [Fact]
    public void A_dictionary_with_windows_line_breaks_is_the_same_set()
    {
        Assert.Equal(new CharacterSet("a\nb\nc\n").Count, new CharacterSet("a\r\nb\r\nc\r\n").Count);
        Assert.Equal(new CharacterSet("a\nb\nc").Count, new CharacterSet("a\nb\nc\n").Count); // with or without the break at the end
    }

    [Fact]
    public void Letters_that_follow_each_other_are_read_and_nothing_is_dropped()
    {
        var set = new CharacterSet("a\nb\nc\n"); // 0 nothing, 1 a, 2 b, 3 c, 4 space

        var (text, _) = set.Decode(Answers(5, 1, 2, 3), 3);

        Assert.Equal("abc", text);
    }

    [Fact]
    public void A_letter_that_is_the_answer_of_several_slices_in_a_row_counts_once_unless_nothing_is_between()
    {
        var set = new CharacterSet("a\nb\nc\n");

        // a a (nothing) a b b  ->  a a b
        var (text, _) = set.Decode(Answers(5, 1, 1, 0, 1, 2, 2), 6);

        Assert.Equal("aab", text);
    }

    [Fact]
    public void The_last_answer_is_the_space()
    {
        var set = new CharacterSet("a\nb\nc\n");

        var (text, _) = set.Decode(Answers(5, 1, 4, 2), 3);

        Assert.Equal("a b", text);
    }

    [Fact]
    public void A_line_of_nothing_reads_as_no_text_with_no_confidence()
    {
        var set = new CharacterSet("a\nb\n");

        var (text, confidence) = set.Decode(Answers(4, 0, 0, 0), 3);

        Assert.Equal(string.Empty, text);
        Assert.Equal(0f, confidence);
    }

    [Fact]
    public void The_confidence_is_the_mean_chance_of_the_letters_taken()
    {
        var set = new CharacterSet("a\nb\n");
        var answers = new float[] { 0.05f, 0.9f, 0.025f, 0.025f, 0.2f, 0.1f, 0.6f, 0.1f }; // slice 0: a at 0.9, slice 1: b at 0.6

        var (text, confidence) = set.Decode(answers, 2);

        Assert.Equal("ab", text);
        Assert.Equal(0.75f, confidence, 3);
    }

    [Fact]
    public void Scores_that_are_no_chances_are_turned_into_chances()
    {
        var set = new CharacterSet("a\nb\n");
        // Raw scores: far from adding up to one. 'a' is the clear winner (10 against 0)
        var answers = new float[] { 0f, 10f, 0f, 0f };

        var (text, confidence) = set.Decode(answers, 1);

        Assert.Equal("a", text);
        Assert.InRange(confidence, 0.99f, 1f);
    }

    [Fact]
    public void The_wrong_number_of_answers_is_refused()
    {
        var set = new CharacterSet("a\nb\n");

        Assert.Throws<ArgumentException>(() => set.Decode(new float[7], 2));
    }

    [Fact]
    public void A_letter_that_is_not_allowed_is_never_taken_the_likeliest_of_the_others_is()
    {
        var set = new CharacterSet("a\nb\nc\n"); // 0 nothing, 1 a, 2 b, 3 c, 4 space
        var allowed = set.Allowing(letter => letter != "b");

        // The model is surest of b in the middle slice, and next surest of c
        float[] answers =
        [
            0.01f, 0.90f, 0.02f, 0.03f, 0.04f,
            0.01f, 0.02f, 0.60f, 0.30f, 0.07f,
            0.01f, 0.01f, 0.02f, 0.90f, 0.06f,
        ];

        Assert.Equal("abc", set.Decode(answers, 3).Text);
        var (text, confidence) = set.Decode(answers, 3, allowed);

        Assert.Equal("ac", text); // b is not allowed: c is taken in its place, and the c after it is the same answer again
        Assert.InRange(confidence, 0.5f, 1f);
    }

    [Fact]
    public void Nothing_and_the_space_cannot_be_forbidden_away_the_model_has_to_be_able_to_say_nothing()
    {
        var set = new CharacterSet("a\nb\n");

        var allowed = set.Allowing(_ => false);

        Assert.True(allowed[0]);
        Assert.False(allowed[1]);
        Assert.Equal(string.Empty, set.Decode(new float[] { 0.1f, 0.8f, 0.05f, 0.05f }, 1, allowed).Text);
    }

    [Fact]
    public void The_allowed_letters_must_be_one_for_each_answer()
    {
        var set = new CharacterSet("a\nb\n");

        Assert.Throws<ArgumentException>(() => set.Decode(new float[4], 1, new bool[3]));
    }

    [Fact]
    public void A_language_keeps_the_east_slavic_reader_from_the_letters_it_does_not_have()
    {
        var set = CharacterSet.EastSlavic;
        var ukrainian = set.Allowing(letter => !"ыэъё".Contains(letter));

        var answers = new float[set.Count];
        var yeru = Enumerable.Range(1, set.Count - 2).First(i => set.Decode(Answers(set.Count, i), 1).Text == "ы");
        var ukrainianI = Enumerable.Range(1, set.Count - 2).First(i => set.Decode(Answers(set.Count, i), 1).Text == "и");
        answers[yeru] = 0.7f;
        answers[ukrainianI] = 0.2f;
        answers[0] = 0.1f;

        Assert.Equal("ы", set.Decode(answers, 1).Text);
        Assert.Equal("и", set.Decode(answers, 1, ukrainian).Text);
    }

    [Fact]
    public void The_dictionary_that_comes_with_the_program_fits_the_model_and_has_the_ukrainian_letters()
    {
        var set = CharacterSet.EastSlavic;

        Assert.Equal(519, set.Count); // the answers of the PP-OCRv5 East Slavic model for a slice

        var letters = new HashSet<string>();
        for (var i = 1; i < set.Count - 1; i++)
            letters.Add(set.Decode(Answers(set.Count, i), 1).Text);
        foreach (var letter in new[] { "і", "ї", "є", "ґ", "І", "Ї", "Є", "Ґ", "я", "Ш", "A", "z", "0", "9" })
            Assert.Contains(letter, letters);
    }
}

public sealed class GeometryTests
{
    [Fact]
    public void The_hull_leaves_out_the_points_inside()
    {
        var hull = Geometry.ConvexHull([new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(5, 5), new(3, 4), new(10, 5)]);

        Assert.Equal(4, hull.Count);
        Assert.DoesNotContain(new PointF(5, 5), hull);
    }

    [Fact]
    public void The_smallest_rectangle_of_an_upright_one_is_itself()
    {
        var (centre, length, height, angle) = Geometry.SmallestRectangle([new(10, 20), new(70, 20), new(70, 40), new(10, 40)]);

        Assert.Equal(40f, centre.X, 2);
        Assert.Equal(30f, centre.Y, 2);
        Assert.Equal(60f, length, 2);
        Assert.Equal(20f, height, 2);
        Assert.Equal(0f, angle, 3);
    }

    [Fact]
    public void The_smallest_rectangle_of_a_turned_one_has_its_turn()
    {
        var corners = TextRegion.FromCentre(new PointF(100, 80), 80, 20, 0.35f).Corners; // about 20 degrees

        var (centre, length, height, angle) = Geometry.SmallestRectangle(corners);

        Assert.Equal(100f, centre.X, 1);
        Assert.Equal(80f, centre.Y, 1);
        Assert.Equal(80f, length, 1);
        Assert.Equal(20f, height, 1);
        Assert.Equal(0.35f, angle, 2);
    }

    [Theory]
    [InlineData(-80)]
    [InlineData(-60)]
    [InlineData(-30)]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(70)]
    [InlineData(85)]
    public void The_rectangle_of_any_turn_is_found_with_its_length_along_the_direction_nearest_to_horizontal(int degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var corners = TextRegion.FromCentre(new PointF(100, 80), 80, 20, radians).Corners; // 80 long along the turn, 20 across

        var (_, length, height, angle) = Geometry.SmallestRectangle(corners);

        if (Math.Abs(degrees) < 45)
        {
            Assert.Equal(80f, length, 1);
            Assert.Equal(20f, height, 1);
            Assert.Equal(radians, angle, 0.02f);
        }
        else
        {
            // The long side is nearer to vertical: the side across is the one that is as nearly horizontal as it gets
            var across = radians + (degrees > 0 ? -MathF.PI / 2f : MathF.PI / 2f);
            Assert.Equal(20f, length, 1);
            Assert.Equal(80f, height, 1);
            Assert.Equal(across, angle, 0.02f);
        }
        Assert.InRange(angle, -MathF.PI / 4f - 0.001f, MathF.PI / 4f + 0.001f);
    }

    [Fact]
    public void A_tall_rectangle_is_given_with_its_long_side_as_the_height()
    {
        var (_, length, height, angle) = Geometry.SmallestRectangle([new(10, 0), new(16, 0), new(16, 60), new(10, 60)]);

        Assert.Equal(6f, length, 2);
        Assert.Equal(60f, height, 2);
        Assert.InRange(angle, -MathF.PI / 4, MathF.PI / 4);
    }

    [Fact]
    public void One_point_and_no_points_are_no_rectangle()
    {
        Assert.Equal(0f, Geometry.SmallestRectangle([new(3, 4)]).Length);
        Assert.Equal(0f, Geometry.SmallestRectangle([]).Length);
    }

    [Fact]
    public void A_box_knows_its_length_height_centre_and_bounds()
    {
        var box = TextRegion.FromCentre(new PointF(50, 40), 30, 10, 0);

        Assert.Equal(30f, box.Length, 3);
        Assert.Equal(10f, box.Height, 3);
        Assert.Equal(50f, box.Centre.X, 3);
        Assert.Equal(new Rectangle(35, 35, 30, 10), box.Bounds);
    }

    [Fact]
    public void A_box_is_moved_and_scaled_corner_by_corner()
    {
        var box = TextRegion.FromCentre(new PointF(50, 40), 30, 10, 0).Transformed(2, 3, 5, 7);

        Assert.Equal(new PointF(75, 112), box.TopLeft); // (35, 35): 35 * 2 + 5 and 35 * 3 + 7
    }
}

public sealed class LineFinderTests
{
    private static float[] Map(int width, int height, params (int Left, int Top, int Width, int Height, float Value)[] shapes)
    {
        var map = new float[width * height];
        foreach (var (left, top, w, h, value) in shapes)
        {
            for (var y = top; y < top + h; y++)
            {
                for (var x = left; x < left + w; x++)
                    map[y * width + x] = value;
            }
        }
        return map;
    }

    [Fact]
    public void A_bar_the_model_is_sure_of_is_a_line_made_larger_by_the_unclip()
    {
        var map = Map(100, 40, (10, 15, 50, 10, 0.9f));

        var lines = LineFinder.Find(map, 100, 40, new FinderSettings());

        var (box, score) = Assert.Single(lines);
        Assert.Equal(0.9f, score, 3);
        // 50 by 10: each side moves by 50 * 10 * 1.5 / (2 * 60) = 6.25
        Assert.Equal(62.5f, box.Length, 1);
        Assert.Equal(22.5f, box.Height, 1);
        Assert.Equal(35f, box.Centre.X, 1);
        Assert.Equal(20f, box.Centre.Y, 1);
    }

    [Fact]
    public void Two_bars_are_two_lines()
    {
        var map = Map(100, 60, (5, 10, 40, 8, 0.9f), (5, 40, 60, 8, 0.8f));

        var lines = LineFinder.Find(map, 100, 60, new FinderSettings());

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void A_shape_the_model_is_not_sure_enough_of_is_not_a_line()
    {
        var map = Map(100, 40, (10, 15, 50, 10, 0.45f)); // over the point threshold (0.3), under the shape threshold (0.6)

        Assert.Empty(LineFinder.Find(map, 100, 40, new FinderSettings()));
    }

    [Fact]
    public void Points_under_the_point_threshold_are_nothing()
    {
        var map = Map(100, 40, (10, 15, 50, 10, 0.2f));

        Assert.Empty(LineFinder.Find(map, 100, 40, new FinderSettings()));
    }

    [Fact]
    public void Dust_is_not_a_line()
    {
        var map = Map(100, 40, (10, 15, 2, 2, 0.95f), (40, 15, 40, 2, 0.95f)); // a speck, and a hair that is 2 points high

        Assert.Empty(LineFinder.Find(map, 100, 40, new FinderSettings()));
    }

    [Fact]
    public void A_turned_band_is_found_with_its_turn()
    {
        const int width = 160, height = 100;
        var map = new float[width * height];
        var angle = 20 * MathF.PI / 180;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // The band: its middle line runs through (30, 30) at 20 degrees; 90 long and 8 across
                var dx = x + 0.5f - 30;
                var dy = y + 0.5f - 30;
                var along = dx * MathF.Cos(angle) + dy * MathF.Sin(angle);
                var across = -dx * MathF.Sin(angle) + dy * MathF.Cos(angle);
                if (along is >= 0 and <= 90 && Math.Abs(across) <= 4)
                    map[y * width + x] = 0.9f;
            }
        }

        var box = Assert.Single(LineFinder.Find(map, width, height, new FinderSettings())).Box;

        var turn = MathF.Atan2(box.TopRight.Y - box.TopLeft.Y, box.TopRight.X - box.TopLeft.X);
        Assert.Equal(angle, turn, 0.05f);
        Assert.True(box.Length > 90);
        Assert.True(box.Height > 8);
    }

    [Fact]
    public void A_map_of_the_wrong_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => LineFinder.Find(new float[10], 5, 5, new FinderSettings()));
    }

    [Fact]
    public void Shapes_at_the_border_of_the_map_are_found()
    {
        var map = Map(60, 30, (0, 0, 60, 8, 0.9f));

        Assert.Single(LineFinder.Find(map, 60, 30, new FinderSettings()));
    }
}

public sealed class SamplerTests
{
    private static readonly float[] Mean = [0f, 0f, 0f];
    private static readonly float[] One = [1f, 1f, 1f];

    [Fact]
    public void A_picture_made_another_size_keeps_its_colour_in_planes_of_blue_green_red()
    {
        var picture = Bitmap32.Filled(40, 20, Color.FromArgb(255, 255, 128, 0)); // red 255, green 128, blue 0

        var planes = Sampler.Resized(picture, 8, 4, Mean, One);

        Assert.Equal(3 * 32, planes.Length);
        Assert.All(planes[..32], v => Assert.Equal(0f, v, 3));          // blue
        Assert.All(planes[32..64], v => Assert.Equal(128 / 255f, v, 3)); // green
        Assert.All(planes[64..], v => Assert.Equal(1f, v, 3));          // red
    }

    [Fact]
    public void The_mean_is_taken_off_and_the_rest_divided()
    {
        var picture = Bitmap32.Filled(8, 8, Color.FromArgb(255, 255, 255, 255));

        var planes = Sampler.Resized(picture, 4, 4, [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f]);

        Assert.All(planes, v => Assert.Equal(1f, v, 3)); // (1 - 0.5) / 0.5
    }

    [Fact]
    public void A_see_through_picture_is_read_as_if_it_lay_on_white()
    {
        var picture = Bitmap32.Filled(8, 8, Color.Transparent);

        var planes = Sampler.Resized(picture, 4, 4, Mean, One);

        Assert.All(planes, v => Assert.Equal(1f, v, 3));
    }

    [Fact]
    public void A_picture_made_much_smaller_is_averaged_not_picked_from()
    {
        // Lines of one pixel, every fourth row black on white: picked at every 16th row they would all be white or all black; averaged they are 3/4 white
        var picture = Bitmap32.Filled(64, 64, Color.White);
        for (var y = 0; y < 64; y += 4)
        {
            for (var x = 0; x < 64; x++)
            {
                var i = (y * 64 + x) * 4;
                picture.Bgra[i] = picture.Bgra[i + 1] = picture.Bgra[i + 2] = 0;
            }
        }

        var planes = Sampler.Resized(picture, 4, 4, Mean, One);

        Assert.All(planes[..16], v => Assert.InRange(v, 0.7f, 0.8f));
    }

    [Fact]
    public void Shrink_averages_the_blocks()
    {
        var picture = Bitmap32.Filled(4, 2, Color.Black);
        picture.Bgra[0] = picture.Bgra[1] = picture.Bgra[2] = 200; // one pixel of the first block of 2 by 2

        var smaller = Sampler.Shrink(picture, 2);

        Assert.Equal(2, smaller.Width);
        Assert.Equal(1, smaller.Height);
        Assert.Equal(50, smaller.Bgra[0]);
        Assert.Equal(0, smaller.Bgra[4]);
    }

    [Fact]
    public void A_straight_line_is_cut_out_as_wide_as_its_proportions_say()
    {
        var picture = Bitmap32.Filled(300, 100, Color.White);
        var box = TextRegion.FromCentre(new PointF(150, 50), 200, 40, 0); // 5 times as long as high

        var (planes, width) = Sampler.Line(picture, box, 48, 3200, Mean, One);

        Assert.Equal(240, width); // 48 * 5
        Assert.Equal(3 * 48 * 240, planes.Length);
    }

    [Fact]
    public void A_line_is_cut_out_whatever_its_turn()
    {
        // A black bar on white; the line round it is turned 30 degrees: after being straightened the cut-out is black in its middle band and white above and below
        var picture = Bitmap32.Filled(300, 200, Color.White);
        const float angle = 30 * MathF.PI / 180;
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                var dx = x + 0.5f - 150;
                var dy = y + 0.5f - 100;
                var along = dx * MathF.Cos(angle) + dy * MathF.Sin(angle);
                var across = -dx * MathF.Sin(angle) + dy * MathF.Cos(angle);
                if (Math.Abs(along) <= 100 && Math.Abs(across) <= 10)
                {
                    var i = (y * 300 + x) * 4;
                    picture.Bgra[i] = picture.Bgra[i + 1] = picture.Bgra[i + 2] = 0;
                }
            }
        }

        var box = TextRegion.FromCentre(new PointF(150, 100), 160, 60, angle); // 160 long, 60 high: the bar (20 high) is the middle third
        var (planes, width) = Sampler.Line(picture, box, 48, 3200, Mean, One);

        var row = (int y) => planes[y * width + width / 2];
        Assert.True(row(24) < 0.1f, "the middle of the line is the bar");
        Assert.True(row(2) > 0.9f, "the top of the line is above the bar");
        Assert.True(row(45) > 0.9f, "the bottom of the line is below the bar");
    }

    [Fact]
    public void A_line_on_its_side_is_turned_to_be_read()
    {
        var picture = Bitmap32.Filled(100, 300, Color.White);
        var box = TextRegion.FromCentre(new PointF(50, 150), 20, 200, 0); // much higher than long

        var (_, width) = Sampler.Line(picture, box, 48, 3200, Mean, One);

        Assert.Equal(480, width); // read along its 200 with 20 across: 48 * 200 / 20
    }

    [Fact]
    public void A_very_long_line_is_squeezed_to_the_limit()
    {
        var picture = Bitmap32.Filled(5000, 100, Color.White);
        var box = TextRegion.FromCentre(new PointF(2500, 50), 4800, 20, 0);

        var (_, width) = Sampler.Line(picture, box, 48, 3200, Mean, One);

        Assert.Equal(3200, width);
    }
}

public sealed class ReadingOrderTests
{
    private static TextLine Line(string text, float centreX, float centreY, float length = 80, float height = 20) =>
        new(text, 0.9f, TextRegion.FromCentre(new PointF(centreX, centreY), length, height, 0));

    [Fact]
    public void Lines_are_read_from_the_top()
    {
        var ordered = TextScanner.InReadingOrder([Line("third", 50, 300), Line("first", 50, 20), Line("second", 50, 150)]);

        Assert.Equal(["first", "second", "third"], ordered.Select(l => l.Text));
    }

    [Fact]
    public void Lines_on_one_row_are_read_from_the_left_even_when_one_is_a_little_higher()
    {
        var ordered = TextScanner.InReadingOrder([Line("right", 300, 52), Line("left", 50, 58), Line("below", 50, 200)]);

        Assert.Equal(["left", "right", "below"], ordered.Select(l => l.Text));
    }

    [Fact]
    public void The_text_has_a_line_for_each_row_and_a_space_between_the_lines_of_a_row()
    {
        var result = new ScanResult(TextScanner.InReadingOrder([Line("Price:", 50, 20), Line("125", 150, 24), Line("Open 9-18", 60, 100)]));

        Assert.Equal("Price: 125\nOpen 9-18", result.Text);
    }

    [Fact]
    public void A_scan_of_nothing_is_empty_text()
    {
        var result = new ScanResult([]);

        Assert.True(result.IsEmpty);
        Assert.Equal(string.Empty, result.Text);
    }
}
