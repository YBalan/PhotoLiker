using System.Drawing;
using PhotoLikerUI;

namespace PhotoLiker.Tests;

/// <summary>Where the box round a face and its name go on a frame of the slideshow.</summary>
public class SlideFaceMarksTests
{
    // ── sizes ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1080, 3f)]   // a 360th of the height ...
    [InlineData(2160, 6f)]
    [InlineData(360, 2f)]    // ... but never thinner than 2 pixels
    [InlineData(100, 2f)]
    public void TheLine_IsAThreeHundredAndSixtiethOfTheHeight_NotLessThanTwo(int height, float expected) =>
        Assert.Equal(expected, SlideFaceMarks.LineWidth(height));

    [Theory]
    [InlineData(1080, 24f)]  // a 45th of the height ...
    [InlineData(2160, 48f)]
    [InlineData(360, 12f)]   // ... but never smaller than 12 pixels
    [InlineData(200, 12f)]
    public void TheLetters_AreAFortyFifthOfTheHeight_NotSmallerThanTwelve(int height, float expected) =>
        Assert.Equal(expected, SlideFaceMarks.FontPixels(height));

    // ── the box ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheBox_IsTheFractionsOfTheFrame_InPixels()
    {
        var box = SlideFaceMarks.BoxOf(0.25f, 0.5f, 0.1f, 0.2f, 1000, 800);

        Assert.Equal(250f, box.Left);
        Assert.Equal(400f, box.Top);
        Assert.Equal(100f, box.Width, 0.001);
        Assert.Equal(160f, box.Height, 0.001);
    }

    [Fact]
    public void ABoxThatReachesOutOfThePhoto_IsCutAtTheEdges()
    {
        // The detector may put a face a little over the edge of the photo
        var box = SlideFaceMarks.BoxOf(-0.05f, 0.95f, 0.2f, 0.2f, 1000, 800);

        Assert.Equal(0f, box.Left);
        Assert.Equal(760f, box.Top);
        Assert.Equal(150f, box.Right, 0.001);
        Assert.Equal(800f, box.Bottom);
    }

    [Fact]
    public void ABoxWhollyOutsideThePhoto_IsNothing_NotInsideOut()
    {
        var box = SlideFaceMarks.BoxOf(1.2f, 0.4f, 0.1f, 0.1f, 1000, 800);

        Assert.Equal(0f, box.Width);
        Assert.True(box.Height >= 0);
    }

    // ── the name ────────────────────────────────────────────────────────────

    private static readonly SizeF Label = new(120, 30);
    private static readonly RectangleF Room = new(0, 0, 1000, 800); // the part of the photo that is on the screen

    [Fact]
    public void TheName_StandsOnTheBox_AtItsLeftEdge()
    {
        var place = SlideFaceMarks.LabelPlace(new RectangleF(300, 200, 100, 140), Label, Room);

        Assert.Equal(new PointF(300, 170), place);
    }

    [Fact]
    public void ABoxTooNearTheTop_HasItsNameUnderIt()
    {
        var place = SlideFaceMarks.LabelPlace(new RectangleF(300, 10, 100, 140), Label, Room);

        Assert.Equal(new PointF(300, 150), place);
    }

    [Fact]
    public void AFaceThatIsMostOfThePhoto_HasItsNameInsideOnTheTop_WhenThereIsNoRoomAbove_OrUnder()
    {
        var place = SlideFaceMarks.LabelPlace(new RectangleF(100, 10, 400, 780), Label, Room);

        Assert.Equal(new PointF(100, 10), place);
    }

    [Fact]
    public void ANameThatWouldRunOffTheRight_IsMovedLeft_SoThatItEndsAtTheEdge()
    {
        var place = SlideFaceMarks.LabelPlace(new RectangleF(950, 200, 50, 60), Label, Room);

        Assert.Equal(880f, place.X); // 1000 - 120
        Assert.Equal(170f, place.Y);
    }

    [Fact]
    public void ANameWiderThanTheRoom_StartsAtTheLeftEdge()
    {
        var place = SlideFaceMarks.LabelPlace(new RectangleF(50, 200, 40, 40), new SizeF(900, 30), new RectangleF(0, 0, 800, 600));

        Assert.Equal(0f, place.X);
    }

    [Fact]
    public void ABoxAtTheVeryTop_WithTheNameUnder_StillHasRoomToTheBottom()
    {
        // Nothing above (the box starts at 0), the frame is tall enough below the box
        var place = SlideFaceMarks.LabelPlace(new RectangleF(200, 0, 80, 100), Label, Room);

        Assert.Equal(new PointF(200, 100), place);
    }

    [Fact]
    public void WhenThePhotoIsMagnifiedOffTheScreen_TheNameStaysInTheVisiblePart()
    {
        // A box near the left edge of a photo that has drifted so that its left part is off the screen: the room starts at the screen's edge, not the photo's
        var room = new RectangleF(0, 0, 1000, 800);
        var place = SlideFaceMarks.LabelPlace(new RectangleF(-40, 200, 100, 140), Label, room);

        Assert.Equal(0f, place.X);
    }

    // ── where a frame lies ──────────────────────────────────────────────────

    [Fact]
    public void AFrameAtTheMiddleOfTheScreenAsItWasMade_PutsABoxWhereItIsInTheFrame_PlusTheMargin()
    {
        // A 960 x 720 frame on a 1280 x 720 screen: its left edge is at 160
        var place = new SlidePlace(640, 360, 1f);

        var box = SlideFaceMarks.OnScreen(new RectangleF(100, 50, 200, 300), place, 960, 720);

        Assert.Equal(new RectangleF(260, 50, 200, 300), box);
    }

    [Fact]
    public void AMagnifiedFrame_GrowsTheBoxFromTheMiddleOfTheFrame()
    {
        // Magnified 2 times with its middle at the middle of the screen: a point 100 left of the middle of the frame is 200 left of the middle of the screen
        var place = new SlidePlace(640, 360, 2f);

        var box = SlideFaceMarks.OnScreen(new RectangleF(380, 300, 50, 60), place, 960, 720); // 100 left of the middle (480), 60 above (360)

        Assert.Equal(440f, box.Left);   // 640 + (380 - 480) * 2
        Assert.Equal(240f, box.Top);    // 360 + (300 - 360) * 2
        Assert.Equal(100f, box.Width);
        Assert.Equal(120f, box.Height);
    }

    [Fact]
    public void AFrameThatHasMoved_CarriesItsBoxesWithIt()
    {
        var still = SlideFaceMarks.OnScreen(new RectangleF(100, 100, 50, 50), new SlidePlace(640, 360, 1f), 960, 720);
        var moved = SlideFaceMarks.OnScreen(new RectangleF(100, 100, 50, 50), new SlidePlace(540, 400, 1f), 960, 720);

        Assert.Equal(still.Left - 100, moved.Left);
        Assert.Equal(still.Top + 40, moved.Top);
    }

    [Fact]
    public void TheFrameItself_IsTheSameRectangleAsTheBoxesAreMeasuredIn()
    {
        var onScreen = SlideFaceMarks.OnScreen(new RectangleF(0, 0, 960, 720), new SlidePlace(640, 360, 1f), 960, 720);

        Assert.Equal(new RectangleF(160, 0, 960, 720), onScreen);
    }
}
