using PhotoLiker.Search;

namespace PhotoLiker.Effects.Focus
{
    /// <summary>The two ways the program keeps a picture, one for the other: the bytes of a decoded photo (blue, green, red) and the 0xAARRGGBB pixels the slideshow and <see cref="DepthOfField"/> work on.</summary>
    public static class PixelBuffers
    {
        /// <summary>The pixels of a decoded photo, opaque.</summary>
        public static uint[] FromPhoto(DecodedPhoto photo)
        {
            var pixels = new uint[photo.Width * photo.Height];
            var bgr = photo.Bgr;
            for (int i = 0, b = 0; i < pixels.Length; i++, b += 3)
                pixels[i] = 0xFF000000u | ((uint)bgr[b + 2] << 16) | ((uint)bgr[b + 1] << 8) | bgr[b];
            return pixels;
        }

        /// <summary>The decoded photo of pixels (the alpha is left out).</summary>
        public static DecodedPhoto ToPhoto(uint[] pixels, int width, int height)
        {
            var bgr = new byte[width * height * 3];
            for (int i = 0, b = 0; i < width * height; i++, b += 3)
            {
                var pixel = pixels[i];
                bgr[b] = (byte)pixel;
                bgr[b + 1] = (byte)(pixel >> 8);
                bgr[b + 2] = (byte)(pixel >> 16);
            }
            return new DecodedPhoto(width, height, bgr);
        }
    }
}
