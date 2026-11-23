namespace PhotoLiker.Stickers
{
    /// <summary>A photo as pixels: interleaved B, G, R bytes, row after row, no padding, upright. What PhotoLiker.Imaging decodes a photo to.</summary>
    public sealed record PhotoPixels
    {
        public PhotoPixels(int width, int height, byte[] bgr)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(bgr);
            if (bgr.Length != checked(width * height * 3))
                throw new ArgumentException($"A photo of {width} x {height} has {width * height * 3} bytes, not {bgr.Length}.", nameof(bgr));

            Width = width;
            Height = height;
            Bgr = bgr;
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Bgr { get; }
    }

    /// <summary>A picture with a see-through background: interleaved B, G, R, A bytes (the colours not multiplied by the alpha), row after row, no padding.</summary>
    public sealed record StickerPicture
    {
        public StickerPicture(int width, int height, byte[] bgra)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(bgra);
            if (bgra.Length != checked(width * height * 4))
                throw new ArgumentException($"A picture of {width} x {height} has {width * height * 4} bytes, not {bgra.Length}.", nameof(bgra));

            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Bgra { get; }
    }

    /// <summary>What the model thinks of a photo: for every point of a grid it looks at (1024 by 1024 for IS-Net) how likely the point is part of the subject, 0 to 1.</summary>
    public sealed record SubjectMap
    {
        public SubjectMap(int width, int height, float[] values)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(values);
            if (values.Length != checked(width * height))
                throw new ArgumentException($"A map of {width} x {height} has {width * height} values, not {values.Length}.", nameof(values));

            Width = width;
            Height = height;
            Values = values;
        }

        public int Width { get; }

        public int Height { get; }

        public float[] Values { get; }
    }
}
