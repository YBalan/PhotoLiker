using System.Buffers;
using System.Numerics;

namespace PhotoLikerUI
{
    /// <summary>
    /// One picture for <see cref="FrameCompositor.Compose"/>: its pixels (<paramref name="Pixels"/>, row after row, each pixel 0xAARRGGBB as GDI+ keeps a
    /// Format32bppPArgb bitmap, premultiplied, so what is behind it is black), the place of its middle on the screen, how much it is magnified
    /// (<paramref name="Scale"/>) and how much of it shows (<paramref name="Weight"/>, 0 to <see cref="FrameCompositor.FullWeight"/>).
    /// </summary>
    internal readonly record struct CompositeLayer(uint[] Pixels, int Width, int Height, double CenterX, double CenterY, double Scale, int Weight);

    /// <summary>
    /// Puts the pictures of an effect of the slideshow (a dissolve, a slide, a zoom) on a screen-sized buffer, quicker than GDI+ does: it needs 13 ms to draw a
    /// full HD picture that is not wholly there and 23 to 43 ms to draw one 1.1 times larger, on a 4K screen four times that, which is not an animation. This
    /// works on the pixels, all the cores at once, and the result is put on the screen with a plain copy, which takes 1 ms (3 ms in 4K).
    ///
    /// Where the pictures overlap their weights must add up to <see cref="FullWeight"/> at most (a dissolve: w and 256 - w, so a picture and the black around it
    /// both dissolve); pictures that do not overlap (a slide) can each have it. Nothing here knows about windows, so it is tested alone.
    /// </summary>
    internal static class FrameCompositor
    {
        /// <summary>The weight of a picture that shows wholly.</summary>
        public const int FullWeight = 256;

        private const uint Opaque = 0xFF000000;
        private const int RowsPerChunk = 8;

        /// <summary>
        /// Fills <paramref name="destination"/> (<paramref name="width"/> by <paramref name="height"/>, opaque in the end) with black and the layers on it,
        /// the weights of each of them showing. A picture that is not magnified (<see cref="CompositeLayer.Scale"/> 1) is put at the nearest whole pixel and
        /// copied as it is; a magnified one is smoothed (bilinear). What falls outside the screen is left out.
        /// </summary>
        public static void Compose(uint[] destination, int width, int height, ReadOnlySpan<CompositeLayer> layers)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, width * height, nameof(destination));

            var plans = new List<LayerPlan>(layers.Length);
            foreach (var layer in layers)
            {
                if (LayerPlan.Create(layer, width, height) is { } plan)
                    plans.Add(plan);
            }

            var rowBufferLength = plans.Count == 0 ? 0 : plans.Max(plan => plan.RowBufferLength);
            var chunks = (height + RowsPerChunk - 1) / RowsPerChunk;

            Parallel.For(
                0, chunks,
                () => rowBufferLength > 0 ? ArrayPool<uint>.Shared.Rent(rowBufferLength) : Array.Empty<uint>(),
                (chunk, _, rowBuffer) =>
                {
                    var last = Math.Min(height, (chunk + 1) * RowsPerChunk);
                    for (var y = chunk * RowsPerChunk; y < last; y++)
                        ComposeRow(destination, y, width, plans, rowBuffer);
                    return rowBuffer;
                },
                rowBuffer =>
                {
                    if (rowBuffer.Length > 0)
                        ArrayPool<uint>.Shared.Return(rowBuffer);
                });
        }

        private static void ComposeRow(uint[] destination, int y, int width, List<LayerPlan> plans, uint[] rowBuffer)
        {
            var row = destination.AsSpan(y * width, width);
            row.Fill(Opaque);

            foreach (var plan in plans)
            {
                if (y < plan.Top || y >= plan.Bottom)
                    continue;

                if (plan.Exact)
                    DrawExactRow(row, y, plan);
                else
                    DrawScaledRow(row, y, plan, rowBuffer);
            }
        }

        /// <summary>A row of a picture that is only moved: the pixels as they are.</summary>
        private static void DrawExactRow(Span<uint> row, int y, LayerPlan plan)
        {
            var count = plan.Right - plan.Left;
            var source = plan.Pixels.AsSpan((y - plan.OffsetY) * plan.SourceWidth + (plan.Left - plan.OffsetX), count);
            var target = row.Slice(plan.Left, count);

            if (plan.Weight >= FullWeight)
                CopyOpaque(source, target);
            else
                AddWeighted(source, target, plan.Weight);
        }

        /// <summary>A row of a picture that is magnified: the two rows of the picture the row falls between are blended into one, then the pixels are taken from that.</summary>
        private static void DrawScaledRow(Span<uint> row, int y, LayerPlan plan, uint[] rowBuffer)
        {
            var sourceY = Math.Clamp((y + 0.5 - plan.TopEdge) / plan.Scale - 0.5, 0, plan.SourceHeight - 1);
            var upper = Math.Min((int)sourceY, plan.SourceHeight - 1);
            var lower = Math.Min(upper + 1, plan.SourceHeight - 1);
            var fraction = (uint)((sourceY - upper) * 256);

            var count = plan.MaxSourceX - plan.MinSourceX + 1;
            var upperRow = plan.Pixels.AsSpan(upper * plan.SourceWidth + plan.MinSourceX, count);
            if (fraction == 0 || lower == upper)
            {
                upperRow.CopyTo(rowBuffer);
            }
            else
            {
                var lowerRow = plan.Pixels.AsSpan(lower * plan.SourceWidth + plan.MinSourceX, count);
                for (var i = 0; i < count; i++)
                    rowBuffer[i] = Lerp(upperRow[i], lowerRow[i], fraction);
            }

            var weight = (uint)plan.Weight;
            for (var k = 0; k < plan.Columns.Length; k++)
            {
                var pixel = Lerp(rowBuffer[plan.Columns[k]], rowBuffer[plan.NextColumns[k]], plan.Fractions[k]);
                var x = plan.Left + k;
                row[x] = weight >= FullWeight ? pixel | Opaque : row[x] + Weighted(pixel, weight);
            }
        }

        private static void CopyOpaque(ReadOnlySpan<uint> source, Span<uint> target)
        {
            var i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                var opaque = new Vector<uint>(Opaque);
                for (; i <= source.Length - Vector<uint>.Count; i += Vector<uint>.Count)
                    (new Vector<uint>(source[i..]) | opaque).CopyTo(target[i..]);
            }

            for (; i < source.Length; i++)
                target[i] = source[i] | Opaque;
        }

        private static void AddWeighted(ReadOnlySpan<uint> source, Span<uint> target, int weight)
        {
            var w = (uint)weight;
            for (var i = 0; i < source.Length; i++)
                target[i] += Weighted(source[i], w);
        }

        /// <summary>The red, green and blue of <paramref name="pixel"/> times weight / 256, the alpha left out (it is made opaque by the black the row began with). Two channels at a time.</summary>
        private static uint Weighted(uint pixel, uint weight)
        {
            var redBlue = ((pixel & 0x00FF00FF) * weight >> 8) & 0x00FF00FF;
            var green = ((pixel & 0x0000FF00) * weight >> 8) & 0x0000FF00;
            return redBlue | green;
        }

        /// <summary>The pixel between two pixels: <paramref name="fraction"/> / 256 of the way from <paramref name="a"/> to <paramref name="b"/>, every channel (two at a time, with room between them for the carry).</summary>
        private static uint Lerp(uint a, uint b, uint fraction)
        {
            var inverse = 256 - fraction;
            var redBlue = ((a & 0x00FF00FF) * inverse + (b & 0x00FF00FF) * fraction >> 8) & 0x00FF00FF;
            var alphaGreen = (((a >> 8) & 0x00FF00FF) * inverse + ((b >> 8) & 0x00FF00FF) * fraction) & 0xFF00FF00;
            return redBlue | alphaGreen;
        }

        /// <summary>What <see cref="Compose"/> works out once for a layer, so that the rows (done in parallel) only read it.</summary>
        private sealed class LayerPlan
        {
            public required uint[] Pixels { get; init; }
            public required int SourceWidth { get; init; }
            public required int SourceHeight { get; init; }
            public required int Weight { get; init; }
            public required bool Exact { get; init; }

            /// <summary>The columns and rows of the screen the picture covers: from Left up to but not including Right.</summary>
            public required int Left { get; init; }
            public required int Right { get; init; }
            public required int Top { get; init; }
            public required int Bottom { get; init; }

            /// <summary>Exact: where the picture's first pixel falls on the screen, so screen x is picture x + OffsetX.</summary>
            public int OffsetX { get; init; }
            public int OffsetY { get; init; }

            /// <summary>Magnified: the screen position of the top and left edge of the picture, the magnification, and for every column the pixels it is made of.</summary>
            public double TopEdge { get; init; }
            public double Scale { get; init; }
            public int MinSourceX { get; init; }
            public int MaxSourceX { get; init; }
            public int[] Columns { get; init; } = [];     // the left pixel, counted from MinSourceX
            public int[] NextColumns { get; init; } = []; // the right one
            public uint[] Fractions { get; init; } = [];  // how far from the left one to the right one, in 256ths

            public int RowBufferLength => Exact ? 0 : MaxSourceX - MinSourceX + 1;

            /// <summary>The plan for a layer; null if nothing of it shows (no weight, nothing on the screen, no pixels).</summary>
            public static LayerPlan? Create(CompositeLayer layer, int width, int height)
            {
                var weight = Math.Clamp(layer.Weight, 0, FullWeight);
                if (weight == 0 || layer.Width <= 0 || layer.Height <= 0 || layer.Pixels.Length < layer.Width * layer.Height ||
                    double.IsNaN(layer.Scale) || layer.Scale <= 0 || double.IsNaN(layer.CenterX) || double.IsNaN(layer.CenterY))
                    return null;

                return Math.Abs(layer.Scale - 1) < 1e-9
                    ? CreateExact(layer, weight, width, height)
                    : CreateScaled(layer, weight, width, height);
            }

            private static LayerPlan? CreateExact(CompositeLayer layer, int weight, int width, int height)
            {
                var offsetX = (int)Math.Floor(layer.CenterX - layer.Width / 2.0 + 0.5);
                var offsetY = (int)Math.Floor(layer.CenterY - layer.Height / 2.0 + 0.5);
                var left = Math.Max(0, offsetX);
                var right = Math.Min(width, offsetX + layer.Width);
                var top = Math.Max(0, offsetY);
                var bottom = Math.Min(height, offsetY + layer.Height);
                if (left >= right || top >= bottom)
                    return null;

                return new LayerPlan
                {
                    Pixels = layer.Pixels, SourceWidth = layer.Width, SourceHeight = layer.Height, Weight = weight, Exact = true,
                    Left = left, Right = right, Top = top, Bottom = bottom, OffsetX = offsetX, OffsetY = offsetY,
                };
            }

            private static LayerPlan? CreateScaled(CompositeLayer layer, int weight, int width, int height)
            {
                var scale = layer.Scale;
                var leftEdge = layer.CenterX - layer.Width * scale / 2;
                var topEdge = layer.CenterY - layer.Height * scale / 2;

                // A pixel of the screen is covered when the middle of it is inside the picture
                var left = Math.Max(0, (int)Math.Ceiling(leftEdge - 0.5));
                var right = Math.Min(width, (int)Math.Ceiling(leftEdge + layer.Width * scale - 0.5));
                var top = Math.Max(0, (int)Math.Ceiling(topEdge - 0.5));
                var bottom = Math.Min(height, (int)Math.Ceiling(topEdge + layer.Height * scale - 0.5));
                if (left >= right || top >= bottom)
                    return null;

                var first = new int[right - left];
                var next = new int[right - left];
                var fractions = new uint[right - left];
                var last = layer.Width - 1;
                for (var k = 0; k < first.Length; k++)
                {
                    var sourceX = Math.Clamp((left + k + 0.5 - leftEdge) / scale - 0.5, 0, last);
                    var x = Math.Min((int)sourceX, last);
                    first[k] = x;
                    next[k] = Math.Min(x + 1, last);
                    fractions[k] = (uint)((sourceX - x) * 256);
                }

                var min = first[0];
                var max = next[^1];
                for (var k = 0; k < first.Length; k++)
                {
                    first[k] -= min;
                    next[k] -= min;
                }

                return new LayerPlan
                {
                    Pixels = layer.Pixels, SourceWidth = layer.Width, SourceHeight = layer.Height, Weight = weight, Exact = false,
                    Left = left, Right = right, Top = top, Bottom = bottom, TopEdge = topEdge, Scale = scale,
                    MinSourceX = min, MaxSourceX = max, Columns = first, NextColumns = next, Fractions = fractions,
                };
            }
        }
    }
}
