using System.Buffers;
using System.Numerics;

namespace PhotoLiker.Effects.Compositing
{
    /// <summary>
    /// One picture for <see cref="FrameCompositor.Compose"/>: its pixels (<paramref name="Pixels"/>, row after row, each pixel 0xAARRGGBB as GDI+ keeps a
    /// Format32bppPArgb bitmap, premultiplied, so what is behind it is black), the place of its middle on the screen, how much it is magnified
    /// (<paramref name="Scale"/>) and how much of it shows (<paramref name="Weight"/>, 0 to <see cref="FrameCompositor.FullWeight"/>).
    /// </summary>
    public readonly record struct CompositeLayer(
        uint[] Pixels, int Width, int Height, double CenterX, double CenterY, double Scale, int Weight, DepthShift? Depth = null);

    /// <summary>
    /// How near each part of a picture is (<paramref name="Depth"/>: <paramref name="DepthWidth"/> by <paramref name="DepthHeight"/> bytes that cover the whole picture, 0 the farthest, 255 the
    /// nearest), and how far that moves it: a part of depth <c>d</c> is moved by <c>(d / 255 - Focus)</c> times <paramref name="ShiftX"/> sideways and <paramref name="ShiftY"/> down, in pixels
    /// of the picture (see <see cref="PhotoLiker.Effects.Motion.ParallaxView"/>). The picture is not moved as a whole: it stays where its middle is on the screen, and what it shows moves inside it.
    /// <see cref="CompositeLayer.Scale"/> magnifies what it shows about its middle first, so that moving it never reaches beyond its edge.
    /// </summary>
    public readonly record struct DepthShift(byte[] Depth, int DepthWidth, int DepthHeight, double ShiftX, double ShiftY, double Focus);

    /// <summary>
    /// Puts the pictures of an effect of the slideshow (a dissolve, a slide, a zoom) on a screen-sized buffer, quicker than GDI+ does: it needs 13 ms to draw a
    /// full HD picture that is not wholly there and 23 to 43 ms to draw one 1.1 times larger, on a 4K screen four times that, which is not an animation. This
    /// works on the pixels, all the cores at once, and the result is put on the screen with a plain copy, which takes 1 ms (3 ms in 4K).
    ///
    /// Where the pictures overlap their weights must add up to <see cref="FullWeight"/> at most (a dissolve: w and 256 - w, so a picture and the black around it
    /// both dissolve); pictures that do not overlap (a slide) can each have it. Nothing here knows about windows, so it is tested alone.
    /// </summary>
    public static class FrameCompositor
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
            try
            {

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
            finally
            {
                foreach (var plan in plans)
                    plan.Release();
            }
        }

        private static void ComposeRow(uint[] destination, int y, int width, List<LayerPlan> plans, uint[] rowBuffer)
        {
            var row = destination.AsSpan(y * width, width);
            row.Fill(Opaque);

            foreach (var plan in plans)
            {
                if (y < plan.Top || y >= plan.Bottom)
                    continue;

                if (plan.Deep)
                    DrawDepthRow(row, y, plan);
                else if (plan.Exact)
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

        /// <summary>How many times the place a point comes from is corrected by the depth found there (it starts at the point itself; the depth at the place it was moved to is not the depth where it started).</summary>
        private const int DepthIterations = 2;

        /// <summary>
        /// A row of a picture that is moved by its depth. Every point of the screen is taken from the place of the picture it would be taken from without the depth (the picture magnified about its
        /// middle), moved by the displacement found for that place (see <see cref="LayerPlan.BuildField"/>: worked out once for the whole picture on the grid of the depth, and smoothed between its
        /// points here), and the colour is smoothed between the four points of the picture round it (bilinear). A place beyond the edge of the picture takes the edge.
        /// </summary>
        private static void DrawDepthRow(Span<uint> row, int y, LayerPlan plan)
        {
            var width = plan.SourceWidth;
            var height = plan.SourceHeight;
            var pixels = plan.Pixels;
            var fieldWidth = plan.DepthWidth;
            var fieldX = plan.FieldX;
            var fieldY = plan.FieldY;
            var weight = (uint)plan.Weight;
            var lastX = width - 1f;
            var lastY = height - 1f;

            // Where this row is on the grid of the displacements: the two rows of it that the row falls between, and how far
            var gridY = Math.Clamp((y - plan.OffsetY + 0.5f) * plan.DepthHeight / height - 0.5f, 0f, plan.DepthHeight - 1f);
            var upperRow = (int)gridY * fieldWidth;
            var lowerRow = Math.Min((int)gridY + 1, plan.DepthHeight - 1) * fieldWidth;
            var rowFraction = gridY - (int)gridY;
            var qy = plan.HalfHeight + (y - plan.OffsetY + 0.5f - plan.HalfHeight) * plan.ZoomInverse;

            for (var k = 0; k < plan.Columns.Length; k++)
            {
                var left = plan.Columns[k];
                var right = Math.Min(left + 1, fieldWidth - 1);
                var across = plan.GridFractions[k];

                // The displacement here: smoothed between the four points of the grid round the point (a + (b - a) * w, so that a field that is the same everywhere is the same exactly)
                var upperX = fieldX[upperRow + left] + (fieldX[upperRow + right] - fieldX[upperRow + left]) * across;
                var lowerX = fieldX[lowerRow + left] + (fieldX[lowerRow + right] - fieldX[lowerRow + left]) * across;
                var upperY = fieldY[upperRow + left] + (fieldY[upperRow + right] - fieldY[upperRow + left]) * across;
                var lowerY = fieldY[lowerRow + left] + (fieldY[lowerRow + right] - fieldY[lowerRow + left]) * across;

                var fx = Math.Clamp(plan.Qx[k] + upperX + (lowerX - upperX) * rowFraction - 0.5f, 0f, lastX);
                var fy = Math.Clamp(qy + upperY + (lowerY - upperY) * rowFraction - 0.5f, 0f, lastY);
                var x0 = (int)fx;
                var y0 = (int)fy;
                var x1 = Math.Min(x0 + 1, width - 1);
                var y1 = Math.Min(y0 + 1, height - 1);
                var wx = (uint)((fx - x0) * 256);
                var wy = (uint)((fy - y0) * 256);

                var upper = Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], wx);
                var lower = Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], wx);
                var pixel = Lerp(upper, lower, wy);
                var x = plan.Left + k;
                row[x] = weight >= FullWeight ? pixel | Opaque : row[x] + Weighted(pixel, weight);
            }
        }

        /// <summary>The depth (0 to 255) at a place given in points of the depth grid, smoothed between the four points round it; outside the grid it is the nearest edge.</summary>
        private static float DepthAt(byte[] depth, int width, int height, float x, float y)
        {
            x = Math.Clamp(x, 0f, width - 1f);
            y = Math.Clamp(y, 0f, height - 1f);
            var x0 = (int)x;
            var y0 = (int)y;
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var wx = x - x0;
            var wy = y - y0;

            float a = depth[y0 * width + x0];
            float b = depth[y0 * width + x1];
            float c = depth[y1 * width + x0];
            float d = depth[y1 * width + x1];
            var top = a + (b - a) * wx;
            var bottom = c + (d - c) * wx;
            return top + (bottom - top) * wy;
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

            /// <summary>Moved by its depth (see <see cref="DepthShift"/>): drawn at the place and size of an exact picture, what it shows magnified about its own middle by <see cref="Scale"/>.</summary>
            public bool Deep { get; init; }
            public byte[] Depth { get; init; } = [];
            public int DepthWidth { get; init; }
            public int DepthHeight { get; init; }
            public double ShiftX { get; init; }
            public double ShiftY { get; init; }
            public double Focus { get; init; }

            /// <summary>Deep: how far each point of the grid of the depth (in the place it has on the screen) is moved, sideways and down, in pixels of the picture; and the picture's middle and 1 / magnification.</summary>
            public float[] FieldX { get; set; } = [];
            public float[] FieldY { get; set; } = [];
            public float HalfWidth { get; init; }
            public float HalfHeight { get; init; }
            public float ZoomInverse { get; init; }

            /// <summary>Deep: for every column of the screen the picture covers: where it is in the picture before the depth moves it, the point of the grid of displacements to its left (in <see cref="Columns"/>), and how far on to the next.</summary>
            public float[] Qx { get; set; } = [];
            public float[] GridFractions { get; set; } = [];

            /// <summary>Gives back what the plan borrowed (the displacements of a deep layer).</summary>
            public void Release()
            {
                if (!Deep)
                    return;

                ArrayPool<float>.Shared.Return(FieldX);
                ArrayPool<float>.Shared.Return(FieldY);
                FieldX = FieldY = [];
            }

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
            public int[] Columns { get; set; } = [];     // the left pixel, counted from MinSourceX
            public int[] NextColumns { get; init; } = []; // the right one
            public uint[] Fractions { get; init; } = [];  // how far from the left one to the right one, in 256ths

            public int RowBufferLength => Exact || Deep ? 0 : MaxSourceX - MinSourceX + 1;

            /// <summary>The plan for a layer; null if nothing of it shows (no weight, nothing on the screen, no pixels).</summary>
            public static LayerPlan? Create(CompositeLayer layer, int width, int height)
            {
                var weight = Math.Clamp(layer.Weight, 0, FullWeight);
                if (weight == 0 || layer.Width <= 0 || layer.Height <= 0 || layer.Pixels.Length < layer.Width * layer.Height ||
                    double.IsNaN(layer.Scale) || layer.Scale <= 0 || double.IsNaN(layer.CenterX) || double.IsNaN(layer.CenterY))
                    return null;

                if (layer.Depth is { } depth)
                {
                    // A depth that does not fit (no points, fewer than it says, or a number that is none) leaves the picture as it is
                    return depth.DepthWidth > 0 && depth.DepthHeight > 0 && depth.Depth.Length >= depth.DepthWidth * depth.DepthHeight
                        && !double.IsNaN(depth.ShiftX) && !double.IsNaN(depth.ShiftY) && !double.IsNaN(depth.Focus)
                        ? CreateDeep(layer, depth, weight, width, height)
                        : CreateExact(layer with { Scale = 1 }, weight, width, height);
                }

                return Math.Abs(layer.Scale - 1) < 1e-9
                    ? CreateExact(layer, weight, width, height)
                    : CreateScaled(layer, weight, width, height);
            }

            /// <summary>The picture stays where it is on the screen (its size, not magnified: what is magnified is what it shows) and every point of it is drawn from another place of the picture.</summary>
            private static LayerPlan? CreateDeep(CompositeLayer layer, DepthShift depth, int weight, int width, int height)
            {
                var offsetX = (int)Math.Floor(layer.CenterX - layer.Width / 2.0 + 0.5);
                var offsetY = (int)Math.Floor(layer.CenterY - layer.Height / 2.0 + 0.5);
                var left = Math.Max(0, offsetX);
                var right = Math.Min(width, offsetX + layer.Width);
                var top = Math.Max(0, offsetY);
                var bottom = Math.Min(height, offsetY + layer.Height);
                if (left >= right || top >= bottom)
                    return null;

                var plan = new LayerPlan
                {
                    Pixels = layer.Pixels, SourceWidth = layer.Width, SourceHeight = layer.Height, Weight = weight, Exact = false, Deep = true,
                    Left = left, Right = right, Top = top, Bottom = bottom, OffsetX = offsetX, OffsetY = offsetY, Scale = layer.Scale,
                    Depth = depth.Depth, DepthWidth = depth.DepthWidth, DepthHeight = depth.DepthHeight,
                    ShiftX = depth.ShiftX, ShiftY = depth.ShiftY, Focus = depth.Focus,
                    HalfWidth = layer.Width / 2f, HalfHeight = layer.Height / 2f, ZoomInverse = (float)(1 / layer.Scale),
                };
                plan.BuildField();
                plan.BuildColumns();
                return plan;
            }

            /// <summary>
            /// Works out, once for the picture, how far every point of the grid of the depth is moved. The place of the picture a point is taken from is the place it would be taken from without the
            /// depth, moved by the depth there times the shift; the depth to use is that of the place it comes from, so it is looked for in <see cref="DepthIterations"/> steps, starting at the point
            /// itself. This is done on the grid of the depth (about 500 points across: 170,000 points for a photo) and not for every point of the screen (2 million at full HD, 8 million at 4K), which
            /// is the half of the work of a frame; the displacement is smooth (the depth is) so the points of the screen between those of the grid are given theirs by interpolation.
            /// </summary>
            public void BuildField()
            {
                var fieldWidth = DepthWidth;
                var fieldHeight = DepthHeight;
                var x = FieldX = ArrayPool<float>.Shared.Rent(fieldWidth * fieldHeight);
                var y = FieldY = ArrayPool<float>.Shared.Rent(fieldWidth * fieldHeight);

                var shiftX = (float)ShiftX;
                var shiftY = (float)ShiftY;
                var focus = (float)Focus;
                var perPixelX = fieldWidth / (float)SourceWidth;
                var perPixelY = fieldHeight / (float)SourceHeight;
                var zoomInverse = ZoomInverse;
                var depth = Depth;
                var halfWidth = HalfWidth;
                var halfHeight = HalfHeight;
                var sourceWidth = SourceWidth;
                var sourceHeight = SourceHeight;

                Parallel.For(0, fieldHeight, gridY =>
                {
                    // The place of this row of the grid in the picture on the screen, and where it is taken from before the depth moves it
                    var qy = halfHeight + ((gridY + 0.5f) * sourceHeight / fieldHeight - halfHeight) * zoomInverse;
                    for (var gridX = 0; gridX < fieldWidth; gridX++)
                    {
                        var qx = halfWidth + ((gridX + 0.5f) * sourceWidth / fieldWidth - halfWidth) * zoomInverse;
                        float moveX = 0, moveY = 0;
                        for (var i = 0; i < DepthIterations; i++)
                        {
                            var move = DepthAt(depth, fieldWidth, fieldHeight, (qx + moveX) * perPixelX - 0.5f, (qy + moveY) * perPixelY - 0.5f) * (1f / 255f) - focus;
                            moveX = shiftX * move;
                            moveY = shiftY * move;
                        }
                        x[gridY * fieldWidth + gridX] = moveX;
                        y[gridY * fieldWidth + gridX] = moveY;
                    }
                });
            }

            /// <summary>The columns of the screen the picture covers: where each is in the picture before the depth moves it, and where it falls on the grid of the displacements.</summary>
            public void BuildColumns()
            {
                var count = Right - Left;
                var places = Qx = new float[count];
                var lefts = new int[count];
                var fractions = GridFractions = new float[count];
                for (var k = 0; k < count; k++)
                {
                    var picture = Left + k - OffsetX + 0.5f;
                    places[k] = HalfWidth + (picture - HalfWidth) * ZoomInverse;
                    var grid = Math.Clamp(picture * DepthWidth / SourceWidth - 0.5f, 0f, DepthWidth - 1f);
                    lefts[k] = (int)grid;
                    fractions[k] = grid - (int)grid;
                }
                Columns = lefts;
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
