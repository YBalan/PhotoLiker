using System.Drawing;

namespace PhotoLiker.Drawing.Tests;

public sealed class DrawingSessionTests
{
    /// <summary>White as the pixels say it (a named colour is not equal to the same numbers).</summary>
    private static readonly Color Plain = Color.FromArgb(255, 255, 255, 255);

    private static Bitmap32 White(int width = 40, int height = 30) => Bitmap32.Filled(width, height, Color.White);

    private static StrokeStep Dash(float y, Color colour) => new([new PointF(5, y), new PointF(30, y)], colour, 3);

    [Fact]
    public void A_new_session_shows_the_photo_and_has_nothing_to_undo()
    {
        var photo = White();
        var session = new DrawingSession(photo);

        Assert.Same(photo, session.Picture);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.False(session.IsChanged);
    }

    [Fact]
    public void A_step_changes_the_picture_and_never_the_photo()
    {
        var photo = White();
        var session = new DrawingSession(photo);
        session.Add(Dash(10.5f, Color.Red));

        Assert.NotSame(photo, session.Picture);
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), session.Picture.At(15, 10));
        Assert.Equal(Plain, photo.At(15, 10)); // the photo is as it was
        Assert.True(session.IsChanged);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void Undo_takes_the_last_step_back_and_redo_brings_it_back()
    {
        var session = new DrawingSession(White());
        session.Add(Dash(5.5f, Color.Red));
        session.Add(Dash(15.5f, Color.Blue));

        Assert.True(session.Undo());
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), session.Picture.At(15, 5));
        Assert.Equal(Plain, session.Picture.At(15, 15));
        Assert.True(session.CanRedo);

        Assert.True(session.Redo());
        Assert.Equal(Color.FromArgb(255, 0, 0, 255), session.Picture.At(15, 15));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Undoing_everything_gives_back_the_photo_itself()
    {
        var photo = White();
        var session = new DrawingSession(photo);
        session.Add(Dash(5.5f, Color.Red));
        session.Add(Dash(15.5f, Color.Blue));

        session.UndoAll();

        Assert.Same(photo, session.Picture);
        Assert.False(session.IsChanged);
        Assert.False(session.Undo());
    }

    [Fact]
    public void A_new_step_ends_the_way_back_to_the_steps_that_were_taken_back()
    {
        var session = new DrawingSession(White());
        session.Add(Dash(5.5f, Color.Red));
        session.Undo();
        Assert.True(session.CanRedo);

        session.Add(Dash(15.5f, Color.Blue));

        Assert.False(session.CanRedo);
        Assert.False(session.Redo());
        Assert.Equal(Plain, session.Picture.At(15, 5));
    }

    [Fact]
    public void Redo_with_nothing_taken_back_does_nothing()
    {
        var session = new DrawingSession(White());
        Assert.False(session.Redo());
    }

    [Fact]
    public void A_crop_makes_the_picture_smaller_and_undo_gives_the_size_back()
    {
        var session = new DrawingSession(White(40, 30));
        session.Add(new CropStep(new Rectangle(10, 5, 20, 10)));

        Assert.Equal(20, session.Width);
        Assert.Equal(10, session.Height);

        session.Undo();
        Assert.Equal(40, session.Width);
        Assert.Equal(30, session.Height);

        session.Redo();
        Assert.Equal(20, session.Width);
    }

    [Fact]
    public void A_step_after_a_crop_is_in_the_pixels_of_the_cropped_picture()
    {
        var session = new DrawingSession(White(40, 30));
        session.Add(new CropStep(new Rectangle(10, 5, 20, 10)));
        session.Add(new StrokeStep([new PointF(0, 2.5f), new PointF(19, 2.5f)], Color.Black, 1));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), session.Picture.At(10, 2));

        // Taking the crop back (after the stroke): the stroke goes with it; they come back together, in order
        session.Undo();
        session.Undo();
        Assert.Equal(40, session.Width);
        session.Redo();
        session.Redo();
        Assert.Equal(20, session.Width);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), session.Picture.At(10, 2));
    }

    [Fact]
    public void Taking_back_only_the_step_after_a_crop_leaves_the_photo_cropped()
    {
        var session = new DrawingSession(White(40, 30));
        session.Add(new CropStep(new Rectangle(10, 5, 20, 10)));
        session.Add(new StrokeStep([new PointF(0, 2.5f), new PointF(19, 2.5f)], Color.Black, 1));

        session.Undo(); // the stroke: the picture is made again from the photo, and the crop is in the steps that stay

        Assert.Equal(20, session.Width);
        Assert.Equal(10, session.Height);
        Assert.Equal(Plain, session.Picture.At(10, 2));
    }

    [Fact]
    public void Replaying_after_an_undo_gives_the_same_picture_as_drawing_it_once()
    {
        var steps = new DrawStep[]
        {
            Dash(5.5f, Color.Red),
            new TextStep(new Point(3, 3), new TextMask(2, 2, [255, 255, 255, 255]), Color.Green, "t"),
            new CropStep(new Rectangle(2, 2, 30, 20)),
            Dash(8.5f, Color.Blue),
        };

        var once = new DrawingSession(White());
        foreach (var step in steps)
            once.Add(step);

        var again = new DrawingSession(White());
        foreach (var step in steps)
            again.Add(step);
        again.Undo();
        again.Undo();
        again.Redo();
        again.Redo();

        Assert.Equal(once.Picture.Width, again.Picture.Width);
        Assert.Equal(once.Picture.Bgra, again.Picture.Bgra);
    }

    [Fact]
    public void A_turn_makes_the_picture_as_wide_as_it_was_high_and_undo_gives_it_back()
    {
        var photo = White(40, 30);
        var session = new DrawingSession(photo);
        session.Add(new RotateStep(1));

        Assert.Equal(30, session.Width);
        Assert.Equal(40, session.Height);
        Assert.True(session.IsChanged);
        Assert.Equal(40, photo.Width); // the photo is as it was

        session.Undo();
        Assert.Same(photo, session.Picture);

        session.Redo();
        Assert.Equal(30, session.Width);
    }

    [Fact]
    public void A_step_after_a_turn_is_in_the_pixels_of_the_turned_picture_and_a_replay_keeps_the_turn()
    {
        var session = new DrawingSession(White(40, 30));
        session.Add(new RotateStep(1)); // 30 wide, 40 high
        session.Add(new StrokeStep([new PointF(5, 35.5f), new PointF(25, 35.5f)], Color.Black, 1)); // a row that exists only in the turned picture
        session.Add(new StrokeStep([new PointF(5, 5.5f), new PointF(25, 5.5f)], Color.Black, 1));

        Assert.Equal(Color.FromArgb(255, 0, 0, 0), session.Picture.At(10, 35));

        session.Undo(); // only the last stroke: the picture is made again with the turn in it
        Assert.Equal(30, session.Width);
        Assert.Equal(40, session.Height);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0), session.Picture.At(10, 35));
        Assert.Equal(Plain, session.Picture.At(10, 5));
    }

    [Fact]
    public void Turns_and_crops_come_in_the_order_they_were_taken()
    {
        var session = new DrawingSession(White(40, 30));
        session.Add(new CropStep(new Rectangle(0, 0, 20, 10)));
        session.Add(new RotateStep(-1));

        Assert.Equal((10, 20), (session.Width, session.Height));
        session.UndoAll();
        Assert.Equal((40, 30), (session.Width, session.Height));
    }

    [Fact]
    public void A_crop_that_is_refused_changes_nothing()
    {
        var session = new DrawingSession(White());
        session.Add(Dash(5.5f, Color.Red));

        Assert.Throws<ArgumentException>(() => session.Add(new CropStep(new Rectangle(500, 500, 10, 10))));

        Assert.Single(session.Steps);
        Assert.Equal(40, session.Width);
    }

    [Fact]
    public void The_steps_are_listed_in_the_order_they_were_taken()
    {
        var session = new DrawingSession(White());
        var first = Dash(5.5f, Color.Red);
        var second = new CropStep(new Rectangle(0, 0, 30, 20));
        session.Add(first);
        session.Add(second);

        Assert.Equal<DrawStep>([first, second], session.Steps);
    }
}
