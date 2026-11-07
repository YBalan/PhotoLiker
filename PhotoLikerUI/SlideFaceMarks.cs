using System.Drawing;

namespace PhotoLikerUI
{
    /// <summary>
    /// Where a frame lies on the screen at this moment: the point of the screen its middle is at, and how much it is magnified (1 is as it was made). It is what an effect
    /// moves: a photo that drifts (Ken Burns), slides or grows while it comes has a place that changes from one picture to the next.
    /// </summary>
    internal readonly record struct SlidePlace(float CenterX, float CenterY, float Scale);

    /// <summary>
    /// Where the box round a face and the name above it go on the screen, whatever place the photo is at. It has no drawing in it (see <see cref="SlideFaces"/>), only the
    /// places, so that what is awkward (a face at the edge of the photo, a label wider than the room, a photo that is magnified so that its edge is off the screen) is the same
    /// everywhere and can be tested.
    /// </summary>
    internal static class SlideFaceMarks
    {
        /// <summary>The thickness of the line of a box: a 360th of the height of the screen (3 pixels at full HD), not less than 2.</summary>
        public static float LineWidth(int screenHeight) => Math.Max(2f, screenHeight / 360f);

        /// <summary>The size of the letters of a name: a 45th of the height of the screen (24 pixels at full HD), not less than 12.</summary>
        public static float FontPixels(int screenHeight) => Math.Max(12f, screenHeight / 45f);

        /// <summary>The box of a face, which the index keeps as fractions of the upright photo, in the pixels of a frame of <paramref name="frameWidth"/> x <paramref name="frameHeight"/>; never outside it.</summary>
        public static RectangleF BoxOf(float x, float y, float width, float height, int frameWidth, int frameHeight)
        {
            var left = Math.Clamp(x * frameWidth, 0, frameWidth);
            var top = Math.Clamp(y * frameHeight, 0, frameHeight);
            var right = Math.Clamp((x + width) * frameWidth, left, frameWidth);
            var bottom = Math.Clamp((y + height) * frameHeight, top, frameHeight);
            return RectangleF.FromLTRB(left, top, right, bottom);
        }

        /// <summary>A rectangle in the pixels of a frame of <paramref name="frameWidth"/> x <paramref name="frameHeight"/> (the box of a face, or the frame itself), as it is on the screen when the frame lies at <paramref name="place"/>.</summary>
        public static RectangleF OnScreen(RectangleF inFrame, SlidePlace place, int frameWidth, int frameHeight) =>
            new(place.CenterX + (inFrame.X - frameWidth / 2f) * place.Scale,
                place.CenterY + (inFrame.Y - frameHeight / 2f) * place.Scale,
                inFrame.Width * place.Scale,
                inFrame.Height * place.Scale);

        /// <summary>
        /// The top left corner of the band with a name (<paramref name="label"/> is its size), which has to stay inside <paramref name="room"/> (the part of the photo that is on the screen).
        /// It stands on the box, at its left edge, as it does in the main window; when the box is too near the top it goes under the box, and when there is no room there either
        /// (a face that is most of the photo), on the top edge inside the box. It is moved to the left as far as it needs not to run out at the right (and is at the left edge if
        /// it is wider than the room).
        /// </summary>
        public static PointF LabelPlace(RectangleF box, SizeF label, RectangleF room)
        {
            var left = Math.Clamp(box.Left, room.Left, Math.Max(room.Left, room.Right - label.Width));

            var top = box.Top - label.Height;
            if (top < room.Top)
                top = box.Bottom + label.Height <= room.Bottom ? box.Bottom : Math.Max(room.Top, box.Top);

            return new PointF(left, top);
        }
    }
}
