namespace PhotoLiker.Search
{
    /// <summary>A photo decoded to pixels, upright and at most a limited size: interleaved B, G, R bytes, no padding.</summary>
    public sealed record DecodedPhoto(int Width, int Height, byte[] Bgr)
    {
        /// <summary>A copy of a part of the photo. The rectangle is clamped to the photo.</summary>
        public DecodedPhoto Crop(int x, int y, int width, int height)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            width = Math.Clamp(width, 1, Width - x);
            height = Math.Clamp(height, 1, Height - y);

            var part = new byte[width * height * 3];
            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(Bgr, ((y + row) * Width + x) * 3, part, row * width * 3, width * 3);
            return new DecodedPhoto(width, height, part);
        }
    }
}
