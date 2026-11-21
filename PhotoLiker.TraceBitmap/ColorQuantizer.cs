namespace PhotoLiker.TraceBitmap
{
    /// <summary>A picture reduced to a few colours: the palette, darkest colour first, and for each pixel the number of its colour in the palette.</summary>
    internal sealed record ReducedColors(TraceColor[] Palette, byte[] Indices);

    /// <summary>
    /// Reduces the colours of a picture with the median cut: the colours the picture has (counted in a histogram of 5 bits for each of red, green and blue) are put in one box, and the box that has the
    /// most pixels is cut in two, across its widest side at the median, until there are as many boxes as colours were asked for. The colour of a box is the average of its pixels.
    /// </summary>
    internal static class ColorQuantizer
    {
        private sealed class Bin
        {
            public int Key;
            public long Count;
            public long SumR, SumG, SumB;
        }

        private sealed class Box(List<Bin> bins)
        {
            public List<Bin> Bins { get; } = bins;

            public long Count { get; } = bins.Sum(bin => bin.Count);
        }

        public static ReducedColors Reduce(PixelImage image, int colors)
        {
            var pixels = image.Width * image.Height;
            var bgr = image.Bgr;

            var histogram = new Dictionary<int, Bin>();
            var keys = new int[pixels];
            for (var i = 0; i < pixels; i++)
            {
                int b = bgr[i * 3], g = bgr[i * 3 + 1], r = bgr[i * 3 + 2];
                var key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                keys[i] = key;
                if (!histogram.TryGetValue(key, out var bin))
                    histogram[key] = bin = new Bin { Key = key };

                bin.Count++;
                bin.SumR += r;
                bin.SumG += g;
                bin.SumB += b;
            }

            var boxes = new List<Box> { new([.. histogram.Values]) };
            while (boxes.Count < colors)
            {
                // The box with the most pixels that can still be cut (it has two colours or more)
                Box? biggest = null;
                foreach (var box in boxes)
                {
                    if (box.Bins.Count > 1 && (biggest is null || box.Count > biggest.Count))
                        biggest = box;
                }

                if (biggest is null)
                    break;

                boxes.Remove(biggest);
                var (low, high) = Cut(biggest);
                boxes.Add(low);
                boxes.Add(high);
            }

            // The colour of each box, then the boxes darkest first
            var palette = boxes
                .Select(box =>
                {
                    var count = Math.Max(1, box.Count);
                    return new TraceColor(
                        (byte)Math.Clamp((int)Math.Round(box.Bins.Sum(bin => bin.SumR) / (double)count), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(box.Bins.Sum(bin => bin.SumG) / (double)count), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(box.Bins.Sum(bin => bin.SumB) / (double)count), 0, 255));
                })
                .OrderBy(color => color.Brightness)
                .ThenBy(color => color.R).ThenBy(color => color.G).ThenBy(color => color.B)
                .ToArray();

            // Each colour of the histogram goes to the nearest colour of the palette (looked at once for each colour of the picture, not for each pixel)
            var nearest = new Dictionary<int, byte>(histogram.Count);
            foreach (var bin in histogram.Values)
            {
                double r = bin.SumR / (double)bin.Count, g = bin.SumG / (double)bin.Count, b = bin.SumB / (double)bin.Count;
                var best = 0;
                var bestDistance = double.MaxValue;
                for (var p = 0; p < palette.Length; p++)
                {
                    var dr = palette[p].R - r;
                    var dg = palette[p].G - g;
                    var db = palette[p].B - b;
                    var distance = dr * dr + dg * dg + db * db;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = p;
                    }
                }

                nearest[bin.Key] = (byte)best;
            }

            var indices = new byte[pixels];
            for (var i = 0; i < pixels; i++)
                indices[i] = nearest[keys[i]];

            return new ReducedColors(palette, indices);
        }

        /// <summary>Cuts a box in two across the channel in which its colours are the most spread out, where half of its pixels are on each side.</summary>
        private static (Box, Box) Cut(Box box)
        {
            int Channel(Bin bin, int channel) => channel switch
            {
                0 => (bin.Key >> 10) & 31,
                1 => (bin.Key >> 5) & 31,
                _ => bin.Key & 31,
            };

            var widest = 0;
            var widestRange = -1;
            for (var channel = 0; channel < 3; channel++)
            {
                var min = box.Bins.Min(bin => Channel(bin, channel));
                var max = box.Bins.Max(bin => Channel(bin, channel));
                if (max - min > widestRange)
                {
                    widestRange = max - min;
                    widest = channel;
                }
            }

            var ordered = box.Bins.OrderBy(bin => Channel(bin, widest)).ThenBy(bin => bin.Key).ToList();
            long half = box.Count / 2;
            long seen = 0;
            var cut = 1;
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                seen += ordered[i].Count;
                cut = i + 1;
                if (seen >= half)
                    break;
            }

            return (new Box(ordered.GetRange(0, cut)), new Box(ordered.GetRange(cut, ordered.Count - cut)));
        }
    }
}
