using System.Buffers;
using System.Runtime.CompilerServices;
using PhotoLiker.Effects.Compositing;

namespace PhotoLiker.Effects.Transitions
{
    /// <summary>
    /// The changes of photo that can not be done by laying two pictures over each other (<see cref="SlideshowEffects.UsesMixer"/>): a wipe, an iris, the squares of a mosaic, waves, a turning box, a blur, a dip to
    /// black or white. The two pictures are the whole screen as it is for each of the photos at that moment (what <see cref="FrameCompositor"/> made of each: with the drift of the photo, its card, the black
    /// round it), and every pixel of the result is worked out from them. All the cores at once, and nothing here knows about windows, so it is tested alone.
    ///
    /// At progress 0 the result is the first picture exactly, and at 1 the second, whatever the change: it begins and ends on the photos themselves. The progress is eased inside
    /// (<see cref="SlideshowEffects.Ease"/>), as <see cref="SlideshowEffects.Transition"/> does for the others.
    /// </summary>
    public static class TransitionMixer
    {
        private const int RowsPerChunk = 8;
        private const uint Opaque = 0xFF000000;
        private const uint Black = Opaque;
        private const uint White = 0xFFFFFFFF;

        /// <summary>How far the soft edge of the wipe reaches, as a share of the width; and that of the iris as a share of the distance to the corner.</summary>
        public const float WipeSoftness = 0.15f, IrisSoftness = 0.12f;

        /// <summary>The largest squares of the mosaic, as a share of the height (about 60 pixels on a full HD screen).</summary>
        public const float MaxBlockShare = 0.055f;

        /// <summary>The waves of the ripple: how far they bend the picture at the most and how far apart their crests are, as shares of the height.</summary>
        public const float RippleAmplitude = 0.011f, RippleWavelength = 0.12f;

        /// <summary>How far away the camera is from the screen, in half widths, for the turning box.</summary>
        public const float CubeFocalLength = 2.2f;

        /// <summary>How many pixels of the picture a pixel of the blurred copy is made of, as a share of the height (at least 4 pixels): the blur is as wide as that.</summary>
        public const float BlurShare = 1f / 70f;

        /// <summary>
        /// Puts the change <paramref name="progress"/> of the way through (0 = <paramref name="from"/>, 1 = <paramref name="to"/>) into <paramref name="destination"/>. The three are <paramref name="width"/> by
        /// <paramref name="height"/> opaque pixels, row after row, and <paramref name="destination"/> must be none of the other two. A transition that is not one of those made here is a dissolve.
        /// </summary>
        public static void Mix(uint[] destination, uint[] from, uint[] to, int width, int height, SlideshowTransition transition, double progress)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            var length = width * height;
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, length, nameof(destination));
            ArgumentOutOfRangeException.ThrowIfLessThan(from.Length, length, nameof(from));
            ArgumentOutOfRangeException.ThrowIfLessThan(to.Length, length, nameof(to));

            var p = double.IsNaN(progress) ? 0 : Math.Clamp(progress, 0, 1);
            if (p <= 0)
            {
                Array.Copy(from, destination, length);
                return;
            }
            if (p >= 1)
            {
                Array.Copy(to, destination, length);
                return;
            }

            var e = (float)SlideshowEffects.Ease(p);
            switch (transition)
            {
                case SlideshowTransition.Wipe: Wipe(destination, from, to, width, height, e); break;
                case SlideshowTransition.Iris: Iris(destination, from, to, width, height, e); break;
                case SlideshowTransition.Pixelate: Pixelate(destination, from, to, width, height, e); break;
                case SlideshowTransition.Ripple: Ripple(destination, from, to, width, height, e); break;
                case SlideshowTransition.Cube: Cube(destination, from, to, width, height, e); break;
                case SlideshowTransition.BlurDissolve: BlurDissolve(destination, from, to, width, height, e); break;
                case SlideshowTransition.DipBlack: Dip(destination, from, to, width, height, e, Black); break;
                case SlideshowTransition.DipWhite: Dip(destination, from, to, width, height, e, White); break;
                default: Dissolve(destination, from, to, width, height, e); break;
            }
        }

        private static void ForRows(int height, Action<int> row)
        {
            var chunks = (height + RowsPerChunk - 1) / RowsPerChunk;
            Parallel.For(0, chunks, chunk =>
            {
                var last = Math.Min(height, (chunk + 1) * RowsPerChunk);
                for (var y = chunk * RowsPerChunk; y < last; y++)
                    row(y);
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Smooth(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3 - 2 * t);
        }

        /// <summary>The pixel <paramref name="share"/> (0 to 256) of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Blend(uint a, uint b, int share) =>
            share <= 0 ? a : share >= 256 ? b : FrameCompositor.Lerp(a, b, (uint)share);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Share(float t) => (int)(Math.Clamp(t, 0f, 1f) * 256f + 0.5f);

        private static void Dissolve(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            var share = Share(e);
            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                    destination[row + x] = Blend(from[row + x], to[row + x], share);
            });
        }

        /// <summary>The next picture comes in from the left behind a soft edge.</summary>
        private static void Wipe(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            // How much of the next picture shows in each column: the same in all the rows
            var shares = new int[width];
            var front = e * (1 + WipeSoftness);
            for (var x = 0; x < width; x++)
                shares[x] = Share(Smooth((front - (x + 0.5f) / width) / WipeSoftness));

            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                    destination[row + x] = Blend(from[row + x], to[row + x], shares[x]);
            });
        }

        /// <summary>The next picture shows through a circle that grows from the middle of the screen until the corners are inside it.</summary>
        private static void Iris(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            float centerX = width / 2f, centerY = height / 2f;
            var corner = MathF.Sqrt(centerX * centerX + centerY * centerY);
            var soft = Math.Max(1f, corner * IrisSoftness);
            var radius = e * (corner + soft);

            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var row = y * width;
                var dy = y + 0.5f - centerY;
                for (var x = 0; x < width; x++)
                {
                    var dx = x + 0.5f - centerX;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    destination[row + x] = Blend(from[row + x], to[row + x], Share(Smooth((radius - distance) / soft)));
                }
            });
        }

        /// <summary>
        /// Squares grow over the picture (every square has the colour of the pixel in its middle) to the largest size, where the next picture takes the place of the last, and shrink to the next picture.
        /// </summary>
        private static void Pixelate(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            var maxBlock = Math.Max(8f, height * MaxBlockShare);
            var block = Math.Max(1, (int)MathF.Round(1 + (maxBlock - 1) * MathF.Sin(MathF.PI * e)));
            var share = Share(Smooth((e - 0.35f) / 0.3f));

            var columns = new int[width];
            for (var x = 0; x < width; x++)
                columns[x] = Math.Min(width - 1, x / block * block + block / 2);

            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var source = Math.Min(height - 1, y / block * block + block / 2) * width;
                var row = y * width;
                for (var x = 0; x < width; x++)
                    destination[row + x] = Blend(from[source + columns[x]], to[source + columns[x]], share);
            });
        }

        /// <summary>Waves run out from the middle and bend both pictures (the same way), while the one dissolves into the other.</summary>
        private static void Ripple(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            float centerX = width / 2f, centerY = height / 2f;
            var amplitude = RippleAmplitude * height * MathF.Sin(MathF.PI * e);
            var wavelength = Math.Max(4f, RippleWavelength * height);
            var phase = 2.5f * e;
            var share = Share(Smooth(e));

            // How far the wave moves a place, by its distance from the middle (a quarter of a pixel apart, smoothed between): the same for all the places that far, so it is worked out once, not for every pixel
            var steps = (int)MathF.Ceiling(MathF.Sqrt(centerX * centerX + centerY * centerY)) * 4 + 8;
            var wave = new float[steps + 1];
            for (var i = 0; i <= steps; i++)
                wave[i] = amplitude * MathF.Sin(2 * MathF.PI * (i / 4f / wavelength - phase));

            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var row = y * width;
                var dy = y + 0.5f - centerY;
                for (var x = 0; x < width; x++)
                {
                    var dx = x + 0.5f - centerX;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    var at = Math.Min(distance * 4, steps - 1);
                    var index = (int)at;
                    var moved = wave[index] + (wave[index + 1] - wave[index]) * (at - index);
                    var bend = distance < 1e-3f ? 0f : moved / distance;
                    var fx = x + dx * bend;
                    var fy = y + dy * bend;
                    destination[row + x] = Blend(Bilinear(from, width, height, fx, fy), Bilinear(to, width, height, fx, fy), share);
                }
            });
        }

        /// <summary>
        /// The two pictures are the front and the right face of a box (as wide as the screen, as high, and as deep as it is wide) that turns a quarter about its vertical axis, seen from a camera in front of
        /// the middle of it: every pixel is a ray from the camera, and the face it meets first is where its colour comes from (a face that is turned away is darker). The ray is worked out in the
        /// coordinates of the box, where the faces are the planes z = -1 (the front) and x = 1 (the right one); outside the box it is black.
        /// </summary>
        private static void Cube(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            var angle = e * MathF.PI / 2;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            var focal = CubeFocalLength;
            var depth = focal + 1; // the middle of the box: its front face stands on the screen at the start
            var half = width / 2f;
            var aspect = (float)height / width; // the half height of the faces, in half widths
            var frontShade = Share(1 - 0.45f * e);
            var rightShade = Share(0.55f + 0.45f * e);

            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var row = y * width;
                var yn = (y + 0.5f - height / 2f) / half;
                for (var x = 0; x < width; x++)
                {
                    var xn = (x + 0.5f - half) / half;
                    var colour = Black;
                    var nearest = float.MaxValue;

                    // The front face (z = -1 in the box's own coordinates): seen from the front when the ray goes against its normal
                    var facing = xn * sin + focal * cos;
                    if (facing > 1e-6f)
                    {
                        var t = (depth * cos - 1) / facing;
                        var lx = t * (xn * cos - focal * sin) + depth * sin;
                        var ly = t * yn;
                        if (t > 0 && lx >= -1 && lx <= 1 && ly >= -aspect && ly <= aspect)
                        {
                            nearest = t;
                            colour = Shaded(Bilinear(from, width, height, (lx + 1) / 2 * width - 0.5f, (ly / aspect + 1) / 2 * height - 0.5f), frontShade);
                        }
                    }

                    // The right face (x = 1)
                    var across = xn * cos - focal * sin;
                    if (across < -1e-6f)
                    {
                        var t = (1 - depth * sin) / across;
                        var lz = t * (xn * sin + focal * cos) - depth * cos;
                        var ly = t * yn;
                        if (t > 0 && t < nearest && lz >= -1 && lz <= 1 && ly >= -aspect && ly <= aspect)
                            colour = Shaded(Bilinear(to, width, height, (lz + 1) / 2 * width - 0.5f, (ly / aspect + 1) / 2 * height - 0.5f), rightShade);
                    }

                    destination[row + x] = colour;
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Shaded(uint pixel, int shade) => Opaque | FrameCompositor.Weighted(pixel, (uint)Math.Clamp(shade, 0, 256));

        /// <summary>
        /// Both pictures are blurred (a copy of each made small, which is spread over the picture again smoothly) more and more until half way, where one dissolves into the other,
        /// and come into focus as the blur goes. The blur is as wide as <see cref="BlurShare"/> of the height.
        /// </summary>
        private static void BlurDissolve(uint[] destination, uint[] from, uint[] to, int width, int height, float e)
        {
            var blur = Share(MathF.Pow(MathF.Sin(MathF.PI * e), 0.8f));
            var share = Share(Smooth((e - 0.3f) / 0.4f));
            var factor = Math.Max(4, (int)MathF.Round(height * BlurShare));
            var smallWidth = (width + factor - 1) / factor;
            var smallHeight = (height + factor - 1) / factor;
            var length = smallWidth * smallHeight;

            var smallFrom = ArrayPool<uint>.Shared.Rent(length);
            var smallTo = ArrayPool<uint>.Shared.Rent(length);
            var scratch = ArrayPool<uint>.Shared.Rent(length);
            try
            {
                Shrink(from, width, height, factor, smallFrom, smallWidth, smallHeight);
                Shrink(to, width, height, factor, smallTo, smallWidth, smallHeight);
                Soften(smallFrom, scratch, smallWidth, smallHeight);
                Soften(smallTo, scratch, smallWidth, smallHeight);

                ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
                {
                    var row = y * width;
                    var fy = (y + 0.5f) / factor - 0.5f;
                    for (var x = 0; x < width; x++)
                    {
                        var fx = (x + 0.5f) / factor - 0.5f;
                        var a = Blend(from[row + x], Bilinear(smallFrom, smallWidth, smallHeight, fx, fy), blur);
                        var b = Blend(to[row + x], Bilinear(smallTo, smallWidth, smallHeight, fx, fy), blur);
                        destination[row + x] = Blend(a, b, share);
                    }
                });
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(smallFrom);
                ArrayPool<uint>.Shared.Return(smallTo);
                ArrayPool<uint>.Shared.Return(scratch);
            }
        }

        /// <summary>A copy of the picture <paramref name="factor"/> times smaller each way: every pixel is the average of the square of the picture it stands for.</summary>
        private static void Shrink(uint[] source, int width, int height, int factor, uint[] small, int smallWidth, int smallHeight)
        {
            Parallel.For(0, smallHeight, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int sy) =>
            {
                var top = sy * factor;
                var bottom = Math.Min(height, top + factor);
                for (var sx = 0; sx < smallWidth; sx++)
                {
                    var left = sx * factor;
                    var right = Math.Min(width, left + factor);
                    int red = 0, green = 0, blue = 0;
                    for (var y = top; y < bottom; y++)
                    {
                        var row = y * width;
                        for (var x = left; x < right; x++)
                        {
                            var pixel = source[row + x];
                            red += (int)((pixel >> 16) & 0xFF);
                            green += (int)((pixel >> 8) & 0xFF);
                            blue += (int)(pixel & 0xFF);
                        }
                    }

                    var count = (bottom - top) * (right - left);
                    small[sy * smallWidth + sx] = Opaque | (uint)(red / count) << 16 | (uint)(green / count) << 8 | (uint)(blue / count);
                }
            });
        }

        /// <summary>Smooths a small picture in place (a 3 by 3 average), so that spreading it over a large one does not show the squares it is made of. <paramref name="scratch"/> is as large as it.</summary>
        private static void Soften(uint[] small, uint[] scratch, int width, int height)
        {
            Parallel.For(0, height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                for (var x = 0; x < width; x++)
                {
                    int red = 0, green = 0, blue = 0, count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var yy = y + dy;
                        if (yy < 0 || yy >= height)
                            continue;
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var xx = x + dx;
                            if (xx < 0 || xx >= width)
                                continue;
                            var pixel = small[yy * width + xx];
                            red += (int)((pixel >> 16) & 0xFF);
                            green += (int)((pixel >> 8) & 0xFF);
                            blue += (int)(pixel & 0xFF);
                            count++;
                        }
                    }
                    scratch[y * width + x] = Opaque | (uint)(red / count) << 16 | (uint)(green / count) << 8 | (uint)(blue / count);
                }
            });
            Array.Copy(scratch, small, width * height);
        }

        /// <summary>The last picture fades into <paramref name="colour"/> in the first half, and the next one comes out of it in the second.</summary>
        private static void Dip(uint[] destination, uint[] from, uint[] to, int width, int height, float e, uint colour)
        {
            var first = e < 0.5f;
            var picture = first ? from : to;
            var share = Share(first ? 1 - 2 * e : 2 * e - 1); // how much of the picture shows

            ForRows(height, [MethodImpl(MethodImplOptions.AggressiveOptimization)] (int y) =>
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                    destination[row + x] = Blend(colour, picture[row + x], share);
            });
        }

        /// <summary>The colour at a place given in pixels (a pixel's middle is at a whole number), smoothed between the four pixels round it; outside the picture it is the nearest edge.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static uint Bilinear(uint[] pixels, int width, int height, float fx, float fy)
        {
            fx = Math.Clamp(fx, 0f, width - 1f);
            fy = Math.Clamp(fy, 0f, height - 1f);
            var x0 = (int)fx;
            var y0 = (int)fy;
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var wx = (uint)((fx - x0) * 256);
            var wy = (uint)((fy - y0) * 256);

            var top = FrameCompositor.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], wx);
            var bottom = FrameCompositor.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], wx);
            return FrameCompositor.Lerp(top, bottom, wy);
        }
    }
}
