namespace PhotoLiker.Stickers
{
    /// <summary>
    /// The guided filter (He, Sun and Tang): a mask is smoothed, but not across the edges of a picture that guides it, so a soft mask comes to lie along the edge that is in the photo.
    /// The model sees a photo made 1024 pixels across, so its mask is soft over a few pixels of the photo (more for a large photo); this is what draws it back to the edge of the subject.
    /// </summary>
    internal static class GuidedFilter
    {
        /// <summary>
        /// The mask <paramref name="input"/> as a guide of one channel (0 to 1) says it should be. <paramref name="radius"/> is the size of the neighbourhood a fit is made over, in points;
        /// <paramref name="epsilon"/> is how much of a change of the guide counts as an edge (small = the mask follows even faint edges; large = a plain blur).
        /// </summary>
        public static float[] Filter(float[] guide, float[] input, int width, int height, int radius, float epsilon)
        {
            var count = width * height;
            if (guide.Length != count || input.Length != count)
                throw new ArgumentException("The guide and the mask must have the size that was said.");

            var meanGuide = Box(guide, width, height, radius);
            var meanInput = Box(input, width, height, radius);

            var guideGuide = new float[count];
            var guideInput = new float[count];
            for (var i = 0; i < count; i++)
            {
                guideGuide[i] = guide[i] * guide[i];
                guideInput[i] = guide[i] * input[i];
            }
            var meanGuideGuide = Box(guideGuide, width, height, radius);
            var meanGuideInput = Box(guideInput, width, height, radius);

            // In each neighbourhood the mask is a line of the guide: input = a * guide + b
            var a = new float[count];
            var b = new float[count];
            for (var i = 0; i < count; i++)
            {
                var variance = meanGuideGuide[i] - meanGuide[i] * meanGuide[i];
                var covariance = meanGuideInput[i] - meanGuide[i] * meanInput[i];
                a[i] = covariance / (variance + epsilon);
                b[i] = meanInput[i] - a[i] * meanGuide[i];
            }

            var meanA = Box(a, width, height, radius);
            var meanB = Box(b, width, height, radius);
            var result = new float[count];
            for (var i = 0; i < count; i++)
                result[i] = Math.Clamp(meanA[i] * guide[i] + meanB[i], 0f, 1f);
            return result;
        }

        /// <summary>The average of the points within <paramref name="radius"/> of each point (a square of 2 * radius + 1; at the edge of the picture only the points that are there count).</summary>
        public static float[] Box(float[] source, int width, int height, int radius)
        {
            // Running sums along the rows, then down the columns: the work does not depend on the radius
            var across = new float[source.Length];
            Parallel.For(0, height, y =>
            {
                var row = y * width;
                double sum = 0;
                for (var x = 0; x <= Math.Min(radius, width - 1); x++)
                    sum += source[row + x];

                for (var x = 0; x < width; x++)
                {
                    var left = Math.Max(0, x - radius);
                    var right = Math.Min(width - 1, x + radius);
                    across[row + x] = (float)(sum / (right - left + 1));

                    if (x + radius + 1 < width)
                        sum += source[row + x + radius + 1];
                    if (x - radius >= 0)
                        sum -= source[row + x - radius];
                }
            });

            var result = new float[source.Length];
            Parallel.For(0, width, x =>
            {
                double sum = 0;
                for (var y = 0; y <= Math.Min(radius, height - 1); y++)
                    sum += across[y * width + x];

                for (var y = 0; y < height; y++)
                {
                    var top = Math.Max(0, y - radius);
                    var bottom = Math.Min(height - 1, y + radius);
                    result[y * width + x] = (float)(sum / (bottom - top + 1));

                    if (y + radius + 1 < height)
                        sum += across[(y + radius + 1) * width + x];
                    if (y - radius >= 0)
                        sum -= across[(y - radius) * width + x];
                }
            });
            return result;
        }
    }
}
