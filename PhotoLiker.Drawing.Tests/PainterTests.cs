using System.Drawing;

namespace PhotoLiker.Drawing.Tests;

public sealed class PainterTests
{
    /// <summary>White as the pixels say it (a named colour is not equal to the same numbers).</summary>
    private static readonly Color Plain = Color.FromArgb(255, 255, 255, 255);

    private static Bitmap32 White(int width = 40, int height = 30) => Bitmap32.Filled(width, height, Color.White);

    private static StrokeStep Line(float x1, float y1, float x2, float y2, Color colour, float width) => new([new PointF(x1, y1), new PointF(x2, y2)], colour, width);

    [Fact]
    public void A_horizontal_line_colours_its_row_and_leaves_the_rest()
    {
        var picture = White();
        Painter.Draw(picture, Line(5, 10.5f, 30, 10.5f, Color.Red, 4));

        Assert.Equal(Color.FromArgb(255, 255, 0, 0), picture.At(15, 10));
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), picture.At(15, 9));
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), picture.At(15, 11));
        Assert.Equal(Plain, picture.At(15, 14));
        Assert.Equal(Plain, picture.At(15, 6));
        Assert.Equal(Plain, picture.At(2, 10)); // before the start (the end is round, a pixel or two past it at most)
    }

    [Fact]
    public void The_edge_of_a_line_is_soft()
    {
        var picture = White();
        Painter.Draw(picture, Line(5, 10, 30, 10, Color.Black, 3)); // 3 wide on the middle of two rows: the rows beside the 3 are partly covered

        var edge = picture.At(15, 8); // 1.5 away from the middle of the line, the edge of the pen
        Assert.InRange(edge.R, 1, 254);
    }

    [Fact]
    public void The_end_of_a_line_is_round()
    {
        var picture = White();
        Painter.Draw(picture, Line(10.5f, 10.5f, 20.5f, 10.5f, Color.Black, 8));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(8, 10)); // a pen of 8 reaches 4 before the start
        Assert.Equal(Plain, picture.At(5, 10));
        Assert.True(picture.At(7, 7).R > 150, "the corner of a square end would be black; a round end only just touches it (4.2 from the start, the pen reaches 4)");
    }

    [Fact]
    public void A_single_point_makes_a_dot()
    {
        var picture = White();
        Painter.Draw(picture, new StrokeStep([new PointF(20.5f, 15.5f)], Color.Blue, 6));

        Assert.Equal(Color.FromArgb(255, 0, 0, 255), picture.At(20, 15));
        Assert.Equal(Plain, picture.At(30, 15));
    }

    [Fact]
    public void A_stroke_outside_the_picture_changes_nothing_and_does_not_fail()
    {
        var picture = White();
        Painter.Draw(picture, Line(-50, -50, -10, -20, Color.Black, 5));
        Painter.Draw(picture, Line(500, 500, 600, 600, Color.Black, 5));

        Assert.All(picture.Bgra, value => Assert.Equal(255, value));
    }

    [Fact]
    public void A_stroke_that_runs_off_the_picture_is_drawn_as_far_as_the_picture_goes()
    {
        var picture = White();
        Painter.Draw(picture, Line(-20, 10.5f, 60, 10.5f, Color.Black, 3));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(0, 10));
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(39, 10));
    }

    [Fact]
    public void A_highlighter_lets_what_is_below_show_and_is_no_darker_where_it_crosses_itself()
    {
        var picture = White();
        var yellow = Color.FromArgb(100, 255, 255, 0);
        var crossing = new StrokeStep([new PointF(5, 10.5f), new PointF(35, 10.5f), new PointF(35, 20.5f), new PointF(5, 20.5f), new PointF(5, 10.5f), new PointF(35, 10.5f)], yellow, 5);
        Painter.Draw(picture, crossing);

        var once = picture.At(20, 10);
        Assert.Equal(255, once.R);
        Assert.Equal(255, once.G);
        Assert.InRange(once.B, 140, 170); // white lightly yellowed: 255 * (1 - 100/255)
        // The corner where the stroke goes over itself is the same colour as the middle of the line
        Assert.Equal(once, picture.At(5, 10));
        Assert.Equal(once, picture.At(35, 10));
    }

    [Fact]
    public void A_stroke_of_many_segments_is_drawn_as_one_not_once_for_each_segment()
    {
        // The same highlighter stroke drawn as one polyline and as the sum of its segments on their own: where the segments meet (or cross) the second is darker, the first is not
        var colour = Color.FromArgb(100, 0, 0, 255);
        PointF[] points = [new(5, 10.5f), new(25, 10.5f), new(25, 20.5f), new(5, 20.5f), new(5, 10.5f)];
        var whole = White();
        Painter.Draw(whole, new StrokeStep(points, colour, 6));

        var apart = White();
        for (var i = 0; i < points.Length - 1; i++)
            Painter.Draw(apart, new StrokeStep([points[i], points[i + 1]], colour, 6));

        Assert.True(whole.At(5, 10).R > apart.At(5, 10).R, "the corner where the segments meet is darker when each is drawn alone");
        Assert.Equal(whole.At(15, 10), whole.At(5, 10)); // in one stroke the corner is as the middle of a line
    }

    [Fact]
    public void A_highlighter_over_dark_text_keeps_the_text_dark()
    {
        var picture = Bitmap32.Filled(20, 10, Color.Black);
        Painter.Draw(picture, Line(0, 5, 20, 5, Color.FromArgb(100, 255, 255, 0), 6));

        var pixel = picture.At(10, 5);
        Assert.InRange(pixel.R, 90, 110); // 255 * 100 / 255 over black
        Assert.Equal(0, pixel.B);
    }

    [Fact]
    public void A_see_through_picture_gets_the_colour_and_its_own_alpha()
    {
        var picture = Bitmap32.Filled(10, 10, Color.Transparent);
        Painter.Draw(picture, Line(0, 5, 10, 5, Color.FromArgb(128, 0, 0, 255), 4));

        var pixel = picture.At(5, 5);
        Assert.Equal(0, pixel.R);
        Assert.Equal(255, pixel.B); // the colour itself, not mixed with the nothing that was there
        Assert.InRange(pixel.A, 126, 130);
    }

    [Fact]
    public void A_colour_with_no_alpha_draws_nothing()
    {
        var picture = White();
        Painter.Draw(picture, Line(0, 10, 40, 10, Color.FromArgb(0, 255, 0, 0), 5));

        Assert.All(picture.Bgra, value => Assert.Equal(255, value));
    }

    [Fact]
    public void A_line_narrower_than_a_pixel_is_drawn_a_pixel_wide()
    {
        var picture = White();
        Painter.Draw(picture, Line(5, 10.5f, 30, 10.5f, Color.Black, 0.1f));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(15, 10));
    }

    [Fact]
    public void A_slanting_line_is_continuous()
    {
        var picture = White(200, 100);
        Painter.Draw(picture, Line(2, 2, 197, 97, Color.Black, 3));

        // Every column has the line in it somewhere; a gap would be a column that is all white
        for (var x = 2; x < 197; x++)
        {
            var dark = Enumerable.Range(0, picture.Height).Any(y => picture.At(x, y).R < 128);
            Assert.True(dark, $"column {x} has no line in it");
        }
    }

    [Fact]
    public void A_long_slanting_stroke_touches_only_the_pixels_near_it()
    {
        var picture = White(300, 300);
        Painter.Draw(picture, Line(0, 0, 299, 299, Color.Black, 4));

        Assert.Equal(Plain, picture.At(280, 20));
        Assert.Equal(Plain, picture.At(20, 280));
        Assert.NotEqual(Color.White, picture.At(150, 150));
    }

    [Fact]
    public void A_note_is_drawn_where_its_mask_says_in_its_colour()
    {
        var picture = White();
        var mask = new TextMask(3, 2, [255, 0, 128, 0, 255, 0]);
        Painter.Draw(picture, new TextStep(new Point(10, 5), mask, Color.Black, "x"));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(10, 5));
        Assert.Equal(Plain, picture.At(11, 5));
        Assert.InRange(picture.At(12, 5).R, 126, 129);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(11, 6));
    }

    [Fact]
    public void A_note_that_runs_off_the_picture_is_drawn_as_far_as_it_goes()
    {
        var picture = White(10, 10);
        var mask = new TextMask(4, 4, Enumerable.Repeat((byte)255, 16).ToArray());
        Painter.Draw(picture, new TextStep(new Point(8, -2), mask, Color.Black, "x"));
        Painter.Draw(picture, new TextStep(new Point(-3, 8), mask, Color.Black, "x"));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(9, 1));
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), picture.At(0, 9));
        Assert.Equal(Plain, picture.At(5, 5));
    }

    [Fact]
    public void A_mask_must_have_a_byte_for_each_pixel()
    {
        Assert.Throws<ArgumentException>(() => new TextMask(3, 3, new byte[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextMask(0, 3, []));
    }

    [Fact]
    public void A_crop_gives_the_part_of_the_picture_as_a_picture()
    {
        var picture = White(10, 10);
        picture.Bgra[(4 * 10 + 3) * 4] = 7; // the blue of the pixel at 3, 4
        var cropped = Painter.Apply(picture, new CropStep(new Rectangle(3, 4, 5, 2)));

        Assert.Equal(5, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(7, cropped.Bgra[0]);
        Assert.NotSame(picture, cropped);
    }

    /// <summary>A picture of 3 by 2 whose pixels are told apart by their blue: row 0 is 0 1 2, row 1 is 3 4 5.</summary>
    private static Bitmap32 Numbered()
    {
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < 6; i++)
        {
            pixels[i * 4] = (byte)i;
            pixels[i * 4 + 3] = 255;
        }
        return new Bitmap32(3, 2, pixels);
    }

    private static string Blues(Bitmap32 picture) => string.Join(" ", Enumerable.Range(0, picture.Width * picture.Height).Select(i => picture.Bgra[i * 4]));

    [Fact]
    public void A_quarter_turn_clockwise_puts_the_left_edge_on_top_and_the_picture_is_as_wide_as_it_was_high()
    {
        var turned = Numbered().Rotate(1);

        Assert.Equal(2, turned.Width);
        Assert.Equal(3, turned.Height);
        // 0 1 2 / 3 4 5 turned clockwise reads 3 0 / 4 1 / 5 2
        Assert.Equal("3 0 4 1 5 2", Blues(turned));
    }

    [Fact]
    public void A_half_turn_puts_the_picture_upside_down()
    {
        var turned = Numbered().Rotate(2);

        Assert.Equal(3, turned.Width);
        Assert.Equal(2, turned.Height);
        Assert.Equal("5 4 3 2 1 0", Blues(turned));
    }

    [Fact]
    public void Three_quarters_clockwise_is_a_quarter_counterclockwise()
    {
        var turned = Numbered().Rotate(3);

        Assert.Equal(2, turned.Width);
        Assert.Equal(3, turned.Height);
        Assert.Equal("2 5 1 4 0 3", Blues(turned));
        Assert.Equal(Blues(turned), Blues(Numbered().Rotate(-1)));
    }

    [Fact]
    public void Four_quarters_give_the_picture_back_and_a_turn_is_never_drawn_on_the_one_it_was_given()
    {
        var picture = Numbered();
        var back = picture.Rotate(1).Rotate(1).Rotate(1).Rotate(1);

        Assert.Equal(Blues(picture), Blues(back));
        Assert.Equal(Blues(picture), Blues(picture.Rotate(0)));
        Assert.NotSame(picture, picture.Rotate(0));
        Assert.Equal("0 1 2 3 4 5", Blues(picture)); // the picture that was turned is as it was
        Assert.Equal(Blues(picture), Blues(picture.Rotate(4)));
        Assert.Equal(Blues(picture.Rotate(1)), Blues(picture.Rotate(5)));
    }

    [Fact]
    public void A_turn_keeps_every_channel_of_every_pixel()
    {
        var picture = new Bitmap32(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);

        var turned = picture.Rotate(1);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, turned.Bgra); // 2 by 1 turned a quarter is 1 by 2: the same two pixels, the first above the second
        Assert.Equal((1, 2), (turned.Width, turned.Height));
    }

    [Fact]
    public void A_crop_with_nothing_of_the_picture_in_it_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Painter.Apply(White(10, 10), new CropStep(new Rectangle(20, 20, 5, 5))));
    }

    [Fact]
    public void A_crop_is_cut_to_the_picture()
    {
        var cropped = White(10, 10).Crop(new Rectangle(6, 6, 20, 20));

        Assert.Equal(4, cropped.Width);
        Assert.Equal(4, cropped.Height);
    }

    [Fact]
    public void A_picture_has_to_have_its_pixels()
    {
        Assert.Throws<ArgumentException>(() => new Bitmap32(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bitmap32(0, 2, []));
    }
}
