namespace PhotoLiker.Stickers.Tests;

/// <summary>Where the real model is, when this computer has it (the program downloads it to its models folder the first time a sticker is made); the tests that need it do nothing where it is not there.</summary>
internal static class RealModel
{
    public static string? Path { get; } = Find();

    private static string? Find()
    {
        var folder = Environment.GetEnvironmentVariable("PHOTOLIKER_TEST_MODELS") is { Length: > 0 } custom
            ? custom
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoLiker", "models");
        var path = System.IO.Path.Combine(folder, "isnet-general-use", "isnet-general-use.onnx");
        return File.Exists(path) ? path : null;
    }
}

public class SubjectDetectorTests
{
    [Fact]
    public void The_photo_is_given_to_the_model_as_planar_normalised_colour()
    {
        // A photo of one colour (B 51, G 102, R 204) is the same at 1024 by 1024; the brightest value is 204, so red is 1, green 0.5 and blue 0.25 before the mean is taken off
        var photo = Make.Plain(8, 4, b: 51, g: 102, r: 204);

        var input = SubjectDetector.Prepare(photo, 4, [1f, 1f, 1f]);

        Assert.Equal(3 * 16, input.Length);
        Assert.All(input[..16], v => Assert.Equal(1f - 0.485f, v, 3));          // red plane
        Assert.All(input[16..32], v => Assert.Equal(0.5f - 0.456f, v, 3));      // green plane
        Assert.All(input[32..], v => Assert.Equal(0.25f - 0.406f, v, 3));       // blue plane
    }

    [Fact]
    public void The_deviation_divides_what_is_left_after_the_mean()
    {
        var photo = Make.Plain(4, 4, b: 100, g: 100, r: 100);

        var input = SubjectDetector.Prepare(photo, 2, [0.5f, 0.5f, 0.5f]);

        Assert.Equal((1f - 0.485f) / 0.5f, input[0], 3);
        Assert.Equal((1f - 0.456f) / 0.5f, input[4], 3);
        Assert.Equal((1f - 0.406f) / 0.5f, input[8], 3);
    }

    [Fact]
    public void A_black_photo_does_not_divide_by_nothing()
    {
        var photo = Make.Plain(4, 4, b: 0, g: 0, r: 0);

        var input = SubjectDetector.Prepare(photo, 2, [1f, 1f, 1f]);

        Assert.All(input, v => Assert.False(float.IsNaN(v) || float.IsInfinity(v)));
        Assert.Equal(-0.485f, input[0], 3);
    }

    [Fact]
    public void The_models_numbers_are_stretched_to_nothing_and_everything()
    {
        var stretched = SubjectDetector.Stretch([2f, 4f, 3f, 6f]);

        Assert.Equal([0f, 0.5f, 0.25f, 1f], stretched);
    }

    [Fact]
    public void A_flat_answer_is_all_nothing_and_a_nan_is_nothing()
    {
        Assert.All(SubjectDetector.Stretch([0.7f, 0.7f, 0.7f]), v => Assert.Equal(0f, v));
        var stretched = SubjectDetector.Stretch([1f, 3f, float.NaN]);
        Assert.Equal(0f, stretched[0]);
        Assert.Equal(1f, stretched[1]);
        Assert.Equal(0f, stretched[2]);
    }

    [Fact]
    public void A_model_that_is_not_there_is_said_so()
    {
        using var folder = new TempFolder();

        Assert.Throws<FileNotFoundException>(() => new SubjectDetector(folder.File("missing.onnx"), 2));
    }

    [Fact]
    public void The_real_model_finds_a_bright_disc_on_a_dark_ground()
    {
        if (RealModel.Path is null)
            return;

        using var detector = new SubjectDetector(RealModel.Path, 4);
        var photo = Make.Photo(240, 180, (x, y) =>
        {
            var inside = (x - 120) * (x - 120) + (y - 90) * (y - 90) <= 60 * 60;
            return inside ? ((byte)30, (byte)60, (byte)230) : ((byte)40, (byte)40, (byte)40);
        });

        var map = detector.Detect(photo);

        Assert.Equal((SubjectDetector.ModelSide, SubjectDetector.ModelSide), (map.Width, map.Height));
        Assert.All(map.Values, v => Assert.InRange(v, 0f, 1f));
        var centre = map.Values[(map.Height / 2) * map.Width + map.Width / 2];
        var corner = map.Values[0];
        Assert.True(centre > 0.7f, $"centre {centre}");
        Assert.True(corner < 0.3f, $"corner {corner}");
    }

    [Fact]
    public void A_detector_that_is_let_go_of_refuses_more_work_and_letting_go_twice_is_fine()
    {
        if (RealModel.Path is null)
            return;

        var detector = new SubjectDetector(RealModel.Path, 2);
        detector.Dispose();
        detector.Dispose();

        Assert.Throws<ObjectDisposedException>(() => detector.Detect(Make.Plain(16, 16)));
    }
}

public class ContourOverlayTests
{
    [Fact]
    public void What_is_cut_away_is_darkened_and_what_stays_is_not()
    {
        var photo = Make.Plain(20, 20, b: 100, g: 200, r: 50);
        var mask = Make.Rectangle(20, 20, 5, 5, 10, 10);

        var picture = ContourOverlay.Draw(photo, mask, 0, (255, 0, 255));

        var inside = (10 * 20 + 10) * 4;
        var outside = (1 * 20 + 1) * 4;
        Assert.Equal([100, 200, 50, 255], picture[inside..(inside + 4)]);
        Assert.Equal((byte)(100 * ContourOverlay.Dimmed + 0.5f), picture[outside]);
        Assert.Equal((byte)(200 * ContourOverlay.Dimmed + 0.5f), picture[outside + 1]);
        Assert.Equal(255, picture[outside + 3]);
    }

    [Fact]
    public void The_cut_line_is_drawn_on_both_sides_of_the_edge_in_the_colour_given()
    {
        var photo = Make.Plain(20, 20);
        var mask = Make.Rectangle(20, 20, 5, 5, 10, 10);

        var picture = ContourOverlay.Draw(photo, mask, 0, (10, 20, 30));

        // Along the row through the middle: the last point outside and the first point inside the left edge are the line, the points beside them are not
        byte[] Pixel(int x, int y) => picture[((y * 20 + x) * 4)..((y * 20 + x) * 4 + 4)];
        Assert.Equal([30, 20, 10, 255], Pixel(4, 10));
        Assert.Equal([30, 20, 10, 255], Pixel(5, 10));
        Assert.Equal([30, 20, 10, 255], Pixel(14, 10));
        Assert.Equal([30, 20, 10, 255], Pixel(15, 10));
        Assert.NotEqual([30, 20, 10, 255], Pixel(6, 10));
        Assert.NotEqual([30, 20, 10, 255], Pixel(3, 10));
    }

    [Fact]
    public void A_thicker_line_is_wider_by_the_radius()
    {
        var photo = Make.Plain(30, 30);
        var mask = Make.Rectangle(30, 30, 10, 10, 10, 10);

        int LineWidth(int radius)
        {
            var picture = ContourOverlay.Draw(photo, mask, radius, (1, 2, 3));
            return Enumerable.Range(0, 30).Count(x => picture[(15 * 30 + x) * 4] == 3 && picture[(15 * 30 + x) * 4 + 2] == 1);
        }

        // Two edges, each two points wide at radius 0 and two more on each side for each step of the radius
        Assert.Equal(4, LineWidth(0));
        Assert.Equal(12, LineWidth(2));
    }

    [Fact]
    public void A_cutout_of_the_wrong_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ContourOverlay.Draw(Make.Plain(10, 10), new float[5], 0, (0, 0, 0)));
    }
}
