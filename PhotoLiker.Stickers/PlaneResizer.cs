namespace PhotoLiker.Stickers
{
    /// <summary>
    /// Resizes pictures of floats with a triangle filter that widens when shrinking, so that a large photo does not alias (the same kind of filter as the bilinear resize of PIL); made larger it is a
    /// plain bilinear blend. Any number of interleaved channels, so a mask (one) and a photo (three) go through the same code.
    /// </summary>
    internal static class PlaneResizer
    {
        public static float[] Resize(float[] source, int channels, int width, int height, int newWidth, int newHeight)
        {
            if (source.Length != width * height * channels)
                throw new ArgumentException("The picture does not have the size that was said.", nameof(source));
            if (width == newWidth && height == newHeight)
                return (float[])source.Clone();

            var across = Coefficients(width, newWidth);
            var down = Coefficients(height, newHeight);

            // The horizontal pass keeps all the source rows, the vertical pass then reduces them
            var rowLength = newWidth * channels;
            var wide = new float[height * rowLength];
            Parallel.For(0, height, y =>
            {
                var from = y * width * channels;
                var to = y * rowLength;
                for (var x = 0; x < newWidth; x++)
                {
                    var (start, weights) = across[x];
                    for (var c = 0; c < channels; c++)
                    {
                        float sum = 0;
                        for (var k = 0; k < weights.Length; k++)
                            sum += source[from + (start + k) * channels + c] * weights[k];
                        wide[to + x * channels + c] = sum;
                    }
                }
            });

            var result = new float[newHeight * rowLength];
            Parallel.For(0, newHeight, y =>
            {
                var (start, weights) = down[y];
                var to = y * rowLength;
                for (var k = 0; k < weights.Length; k++)
                {
                    var weight = weights[k];
                    var from = (start + k) * rowLength;
                    for (var i = 0; i < rowLength; i++)
                        result[to + i] += wide[from + i] * weight;
                }
            });
            return result;
        }

        /// <summary>Bytes in, bytes out (rounded).</summary>
        public static byte[] Resize(byte[] source, int channels, int width, int height, int newWidth, int newHeight)
        {
            var floats = new float[source.Length];
            for (var i = 0; i < floats.Length; i++)
                floats[i] = source[i];

            var resized = Resize(floats, channels, width, height, newWidth, newHeight);
            var result = new byte[resized.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = (byte)Math.Clamp((int)(resized[i] + 0.5f), 0, 255);
            return result;
        }

        /// <summary>For every output position: the first input position it reads and the weights of the ones from there on.</summary>
        private static (int Start, float[] Weights)[] Coefficients(int inputSize, int outputSize)
        {
            var scale = inputSize / (double)outputSize;
            var filterScale = Math.Max(scale, 1.0);
            var result = new (int, float[])[outputSize];

            for (var i = 0; i < outputSize; i++)
            {
                var center = (i + 0.5) * scale;
                var start = Math.Clamp((int)(center - filterScale + 0.5), 0, inputSize - 1);
                var end = Math.Clamp((int)(center + filterScale + 0.5), start + 1, inputSize);

                var weights = new float[end - start];
                double sum = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var distance = (start + k + 0.5 - center) / filterScale;
                    var weight = Math.Max(0, 1 - Math.Abs(distance));
                    weights[k] = (float)weight;
                    sum += weight;
                }

                if (sum <= 0)
                {
                    weights[0] = 1;
                }
                else
                {
                    for (var k = 0; k < weights.Length; k++)
                        weights[k] = (float)(weights[k] / sum);
                }
                result[i] = (start, weights);
            }
            return result;
        }
    }
}
