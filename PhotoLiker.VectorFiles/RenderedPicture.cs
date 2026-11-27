namespace PhotoLiker.VectorFiles
{
    /// <summary>A finished picture: 4 bytes a pixel in the order blue, green, red, alpha (alpha is always 255), the top row first, no padding between the rows.</summary>
    public sealed class RenderedPicture(int width, int height, byte[] bgra)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Bgra { get; } = bgra;

        /// <summary>The colour 0xRRGGBB of a pixel.</summary>
        public int ColorAt(int x, int y)
        {
            var i = (y * Width + x) * 4;
            return (Bgra[i + 2] << 16) | (Bgra[i + 1] << 8) | Bgra[i];
        }
    }
}
