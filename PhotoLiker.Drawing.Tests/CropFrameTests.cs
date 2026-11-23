using System.Drawing;

namespace PhotoLiker.Drawing.Tests;

public sealed class CropFrameTests
{
    private static readonly Size Picture = new(200, 100);

    [Fact]
    public void A_new_frame_is_the_whole_picture_and_is_no_crop()
    {
        var frame = new CropFrame(Picture);

        Assert.Equal(new Rectangle(0, 0, 200, 100), frame.Area);
        Assert.False(frame.IsCrop);
    }

    [Fact]
    public void Dragging_makes_a_frame_in_either_direction()
    {
        var frame = new CropFrame(Picture);
        frame.Draw(new Point(120, 70), new Point(40, 20));

        Assert.Equal(new Rectangle(40, 20, 80, 50), frame.Area);
        Assert.True(frame.IsCrop);
    }

    [Fact]
    public void A_frame_dragged_out_of_the_picture_is_cut_to_it()
    {
        Assert.Equal(new Rectangle(150, 80, 50, 20), CropFrame.FromDrag(new Point(150, 80), new Point(900, 900), Picture));
        Assert.Equal(new Rectangle(0, 0, 30, 30), CropFrame.FromDrag(new Point(-50, -50), new Point(30, 30), Picture));
    }

    [Fact]
    public void A_frame_is_never_smaller_than_the_minimum()
    {
        var frame = new CropFrame(Picture);
        frame.Draw(new Point(50, 50), new Point(52, 51));

        Assert.Equal(CropFrame.MinimumSide, frame.Area.Width);
        Assert.Equal(CropFrame.MinimumSide, frame.Area.Height);
    }

    [Fact]
    public void A_small_frame_in_a_corner_stays_in_the_picture()
    {
        var frame = new CropFrame(Picture);
        frame.Draw(new Point(199, 99), new Point(200, 100));

        Assert.True(new Rectangle(Point.Empty, Picture).Contains(frame.Area));
        Assert.Equal(CropFrame.MinimumSide, frame.Area.Width);
    }

    [Fact]
    public void Hit_tests_find_the_corners_the_edges_and_the_inside()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60)); // 50..150, 20..80

        Assert.Equal(CropHandle.TopLeft, frame.HitTest(new Point(51, 21), 4));
        Assert.Equal(CropHandle.TopRight, frame.HitTest(new Point(149, 19), 4));
        Assert.Equal(CropHandle.BottomRight, frame.HitTest(new Point(151, 82), 4));
        Assert.Equal(CropHandle.BottomLeft, frame.HitTest(new Point(48, 79), 4));
        Assert.Equal(CropHandle.Top, frame.HitTest(new Point(100, 22), 4));
        Assert.Equal(CropHandle.Bottom, frame.HitTest(new Point(100, 78), 4));
        Assert.Equal(CropHandle.Left, frame.HitTest(new Point(52, 50), 4));
        Assert.Equal(CropHandle.Right, frame.HitTest(new Point(148, 50), 4));
        Assert.Equal(CropHandle.Inside, frame.HitTest(new Point(100, 50), 4));
        Assert.Equal(CropHandle.None, frame.HitTest(new Point(10, 10), 4));
        Assert.Equal(CropHandle.None, frame.HitTest(new Point(100, 95), 4));
    }

    [Fact]
    public void An_edge_is_moved_and_the_others_stay()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));
        frame.Drag(CropHandle.Right, new Size(20, 99));

        Assert.Equal(new Rectangle(50, 20, 120, 60), frame.Area);

        frame.Drag(CropHandle.Top, new Size(77, 10));
        Assert.Equal(new Rectangle(50, 30, 120, 50), frame.Area);
    }

    [Fact]
    public void A_corner_moves_two_edges()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));
        frame.Drag(CropHandle.TopLeft, new Size(-10, -5));

        Assert.Equal(new Rectangle(40, 15, 110, 65), frame.Area);
    }

    [Fact]
    public void An_edge_cannot_be_dragged_through_the_other_one_or_out_of_the_picture()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));

        frame.Drag(CropHandle.Left, new Size(500, 0)); // far past the right edge
        Assert.Equal(CropFrame.MinimumSide, frame.Area.Width);
        Assert.Equal(150, frame.Area.Right);

        frame.Drag(CropHandle.Right, new Size(900, 0));
        Assert.Equal(200, frame.Area.Right);

        frame.Drag(CropHandle.Top, new Size(0, -900));
        Assert.Equal(0, frame.Area.Top);
    }

    [Fact]
    public void The_right_and_bottom_edges_cannot_be_dragged_through_the_left_and_top_ones()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));

        frame.Drag(CropHandle.Right, new Size(-900, 0));
        Assert.Equal(50, frame.Area.Left);
        Assert.Equal(CropFrame.MinimumSide, frame.Area.Width);

        frame.Drag(CropHandle.Bottom, new Size(0, -900));
        Assert.Equal(20, frame.Area.Top);
        Assert.Equal(CropFrame.MinimumSide, frame.Area.Height);
    }

    [Fact]
    public void The_inside_moves_the_whole_frame_and_keeps_it_in_the_picture()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));
        frame.Drag(CropHandle.Inside, new Size(10, 5));
        Assert.Equal(new Rectangle(60, 25, 100, 60), frame.Area);

        frame.Drag(CropHandle.Inside, new Size(500, 500));
        Assert.Equal(new Rectangle(100, 40, 100, 60), frame.Area);

        frame.Drag(CropHandle.Inside, new Size(-500, -500));
        Assert.Equal(new Rectangle(0, 0, 100, 60), frame.Area);
    }

    [Fact]
    public void Dragging_nothing_does_nothing()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));
        frame.Drag(CropHandle.None, new Size(30, 30));

        Assert.Equal(new Rectangle(50, 20, 100, 60), frame.Area);
    }

    [Fact]
    public void Reset_makes_the_frame_the_whole_picture_again()
    {
        var frame = new CropFrame(Picture, new Rectangle(50, 20, 100, 60));
        frame.Reset();

        Assert.False(frame.IsCrop);
    }
}
