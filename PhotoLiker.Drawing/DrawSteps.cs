using System.Drawing;

namespace PhotoLiker.Drawing
{
    /// <summary>One thing done to the picture. The coordinates of a step are the ones of the picture as it was when the step was taken (a crop before it moves everything).</summary>
    public abstract record DrawStep;

    /// <summary>
    /// A line drawn with a round pen: a stroke of many points (drawn freehand), or of two (a straight line). The alpha of the colour is how much of what is below shows through: a highlighter is a wide
    /// stroke with a colour that is not quite opaque, and where it crosses itself it is no darker than anywhere else.
    /// </summary>
    public sealed record StrokeStep(IReadOnlyList<PointF> Points, Color Colour, float Width) : DrawStep;

    /// <summary>A note of text: how much of each pixel its letters cover (made by the front end, which has the fonts), put at a place in the colour given.</summary>
    public sealed record TextStep(Point Position, TextMask Mask, Color Colour, string Text) : DrawStep;

    /// <summary>The picture is cut to a part of it.</summary>
    public sealed record CropStep(Rectangle Area) : DrawStep;

    /// <summary>The picture is turned clockwise by some quarters (negative: counterclockwise); what comes after it is in the pixels of the turned picture.</summary>
    public sealed record RotateStep(int QuarterTurns) : DrawStep;

    /// <summary>How much of each pixel the letters of a text cover, 0 (none) to 255 (all), row after row.</summary>
    public sealed class TextMask
    {
        public int Width { get; }

        public int Height { get; }

        public byte[] Coverage { get; }

        public TextMask(int width, int height, byte[] coverage)
        {
            ArgumentNullException.ThrowIfNull(coverage);
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "A mask has at least one pixel.");
            if (coverage.Length != checked(width * height))
                throw new ArgumentException($"A mask of {width} by {height} has {width * height} bytes, not {coverage.Length}.", nameof(coverage));

            Width = width;
            Height = height;
            Coverage = coverage;
        }
    }
}
