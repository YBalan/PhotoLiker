namespace PhotoLiker.Stickers
{
    /// <summary>
    /// The photo with its cutout drawn on it, so that a person can see what was found: what is cut away is darkened, and the line where the cut is, is drawn in a bright colour. The drawing is
    /// made at whatever size the photo is given at, so the front end makes it at the size it shows it (a line a pixel or two wide is then the same on every screen).
    /// </summary>
    public static class ContourOverlay
    {
        /// <summary>How much of its brightness what is cut away keeps (0 to 1).</summary>
        internal const float Dimmed = 0.3f;

        /// <summary>A picture of the photo and the cutout, as opaque B, G, R, A bytes the size of the photo.</summary>
        /// <param name="mask">The cutout, the size of the photo (0 to 1).</param>
        /// <param name="lineRadius">How far the line is grown from the one point on each side of the cut that it is without growing: 0 is two points wide, 1 four, and so on.</param>
        public static byte[] Draw(PhotoPixels photo, float[] mask, int lineRadius, (byte Red, byte Green, byte Blue) line)
        {
            ArgumentNullException.ThrowIfNull(photo);
            ArgumentNullException.ThrowIfNull(mask);
            if (mask.Length != photo.Width * photo.Height)
                throw new ArgumentException("The cutout must have the size of the photo.", nameof(mask));

            var width = photo.Width;
            var height = photo.Height;

            // The points on either side of the cut: a point and its right or lower neighbour that are one in and one out
            var edge = new bool[mask.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var inside = mask[i] >= StickerRenderer.Inside;
                    if (x + 1 < width && (mask[i + 1] >= StickerRenderer.Inside) != inside)
                        edge[i] = edge[i + 1] = true;
                    if (y + 1 < height && (mask[i + width] >= StickerRenderer.Inside) != inside)
                        edge[i] = edge[i + width] = true;
                }
            }
            if (lineRadius > 0)
                edge = Cutout.Grow(edge, width, height, lineRadius);

            var result = new byte[width * height * 4];
            for (var i = 0; i < mask.Length; i++)
            {
                var o = i * 4;
                if (edge[i])
                {
                    result[o] = line.Blue;
                    result[o + 1] = line.Green;
                    result[o + 2] = line.Red;
                }
                else
                {
                    var keep = Dimmed + (1 - Dimmed) * Math.Clamp(mask[i], 0f, 1f);
                    result[o] = (byte)(photo.Bgr[i * 3] * keep + 0.5f);
                    result[o + 1] = (byte)(photo.Bgr[i * 3 + 1] * keep + 0.5f);
                    result[o + 2] = (byte)(photo.Bgr[i * 3 + 2] * keep + 0.5f);
                }
                result[o + 3] = 255;
            }
            return result;
        }
    }
}
