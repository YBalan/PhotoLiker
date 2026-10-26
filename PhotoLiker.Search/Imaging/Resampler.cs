namespace PhotoLiker.Search
{
    /// <summary>Resizes pictures with a triangle filter that widens when shrinking, so large photos don't alias.</summary>
    internal static class Resampler
    {
        /// <summary>Resizes interleaved BGR bytes (the same kind of filter as PIL's bilinear resize).</summary>
        public static byte[] ResizeBgr(byte[] source, int width, int height, int newWidth, int newHeight)
        {
            if (width == newWidth && height == newHeight)
                return (byte[])source.Clone();

            var across = Coefficients(width, newWidth);
            var down = Coefficients(height, newHeight);

            // Horizontal pass keeps all the source rows, the vertical pass then reduces them
            var rowBytes = newWidth * 3;
            var wide = new float[height * rowBytes];
            for (var y = 0; y < height; y++)
            {
                var from = y * width * 3;
                var to = y * rowBytes;
                for (var x = 0; x < newWidth; x++)
                {
                    var (start, weights) = across[x];
                    float b = 0, g = 0, r = 0;
                    for (var k = 0; k < weights.Length; k++)
                    {
                        var p = from + (start + k) * 3;
                        var w = weights[k];
                        b += source[p] * w;
                        g += source[p + 1] * w;
                        r += source[p + 2] * w;
                    }
                    var o = to + x * 3;
                    wide[o] = b;
                    wide[o + 1] = g;
                    wide[o + 2] = r;
                }
            }

            var result = new byte[newHeight * rowBytes];
            var row = new float[rowBytes];
            for (var y = 0; y < newHeight; y++)
            {
                var (start, weights) = down[y];
                Array.Clear(row);
                for (var k = 0; k < weights.Length; k++)
                {
                    var w = weights[k];
                    var from = (start + k) * rowBytes;
                    for (var i = 0; i < rowBytes; i++)
                        row[i] += wide[from + i] * w;
                }
                for (var i = 0; i < rowBytes; i++)
                    result[y * rowBytes + i] = (byte)Math.Clamp((int)(row[i] + 0.5f), 0, 255);
            }
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
