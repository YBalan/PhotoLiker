namespace PhotoLiker.Stickers
{
    /// <summary>
    /// Makes the picture of a sticker from a photo and its cutout: the subject is cut out, scaled so that the picture is 512 pixels on its longer side (what Telegram wants of a static sticker),
    /// and given a coloured outline all round, as die-cut stickers have, and a soft shadow. The outline is the points that are within its width of the subject, with the narrow notches in it
    /// filled, so that it runs round fingers and hair as one smooth line.
    /// </summary>
    public static class StickerRenderer
    {
        /// <summary>The longer side of a sticker, in pixels: exactly this. The shorter side is this or less.</summary>
        public const int Side = 512;

        /// <summary>The picture is this many pixels bigger than the subject on every side, so that the outline of a subject that fills it is not cut off at the edge.</summary>
        internal const int Margin = 2;

        /// <summary>How far down the shadow is moved, and how far it is blurred (the standard deviation of a Gaussian), in pixels of the sticker.</summary>
        internal const int ShadowOffset = 3;
        internal const double ShadowSigma = 3;

        /// <summary>The room the shadow needs round the subject: its offset plus three standard deviations of the blur, with a pixel to spare.</summary>
        internal const int ShadowRoom = ShadowOffset + (int)(3 * ShadowSigma) + 1;

        /// <summary>How dark the shadow is where it is darkest (0 to 1).</summary>
        internal const double ShadowOpacity = 0.35;

        /// <summary>The cutout counts as there where its mask is at least this.</summary>
        internal const float Inside = 0.5f;

        /// <summary>How much more of the photo than the cutout's bounds is cut out of it on every side, for the soft fringe of the cutout, which is not inside by itself.</summary>
        internal const int Slack = 2;

        /// <summary>The empty room round the subject that the settings ask for: the outline, the shadow, and what a grown edge adds.</summary>
        public static int PaddingFor(StickerSettings settings)
        {
            settings = settings.Normalized();
            return Margin + (settings.Outline ? settings.OutlineWidth : 0) + Math.Max(0, settings.EdgeShift) + (settings.Shadow ? ShadowRoom : 0);
        }

        /// <summary>The size of the picture for a cutout of <paramref name="subjectWidth"/> by <paramref name="subjectHeight"/> points: its longer side is <see cref="Side"/>.</summary>
        public static (int Width, int Height) SizeFor(int subjectWidth, int subjectHeight, StickerSettings settings)
        {
            var padding = PaddingFor(settings);
            var room = Side - 2 * padding;
            var scale = room / (double)Math.Max(subjectWidth, subjectHeight);
            return (Math.Max(1, (int)Math.Round(subjectWidth * scale)) + 2 * padding, Math.Max(1, (int)Math.Round(subjectHeight * scale)) + 2 * padding);
        }

        /// <summary>The smallest rectangle (left, top, right, bottom, all inclusive) that has all the points of the cutout, or null when there is none.</summary>
        public static (int Left, int Top, int Right, int Bottom)? BoundsOf(float[] mask, int width, int height)
        {
            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (mask[y * width + x] < Inside)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = y;
                }
            }
            return right < 0 ? null : (left, top, right, bottom);
        }

        /// <summary>The picture of the sticker.</summary>
        /// <param name="mask">The cutout (see <see cref="Cutout.Shape"/>): the size of the photo, 0 to 1.</param>
        /// <exception cref="StickerException">The cutout is empty.</exception>
        public static StickerPicture Render(PhotoPixels photo, float[] mask, StickerSettings settings)
        {
            ArgumentNullException.ThrowIfNull(photo);
            ArgumentNullException.ThrowIfNull(mask);
            ArgumentNullException.ThrowIfNull(settings);
            if (mask.Length != photo.Width * photo.Height)
                throw new ArgumentException("The cutout must have the size of the photo.", nameof(mask));
            settings = settings.Normalized();

            var bounds = BoundsOf(mask, photo.Width, photo.Height) ?? throw new StickerException(StickerFailure.NoSubject, "the cutout is empty");
            var padding = PaddingFor(settings);

            // A little more of the photo than the cutout on every side: for the soft fringe of the cutout, and for an edge that is moved outwards, which shows the photo that is there. A shift is in
            // pixels of the sticker, so the room for it is worked out with the scale the cutout alone would have (a little more than the scale it will have with the room, so it is enough).
            var boundsScale = (Side - 2 * padding) / (double)Math.Max(bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1);
            var slack = Slack + (settings.EdgeShift > 0 ? (int)Math.Ceiling(settings.EdgeShift / boundsScale) + 1 : 0);
            var left = bounds.Left - slack;
            var top = bounds.Top - slack;
            var subjectWidth = bounds.Right + slack - left + 1;
            var subjectHeight = bounds.Bottom + slack - top + 1;

            var (width, height) = SizeFor(subjectWidth, subjectHeight, settings);
            var contentWidth = width - 2 * padding;
            var contentHeight = height - 2 * padding;

            // The photo and its mask cut to the subject and made the size they have in the sticker. Outside the photo (a subject that the edge of the photo cuts) the colour is that of the nearest
            // edge, which is what an edge that is moved outwards shows there, and the cutout is nothing.
            var colours = new float[subjectWidth * subjectHeight * 3];
            var alpha = new float[subjectWidth * subjectHeight];
            for (var y = 0; y < subjectHeight; y++)
            {
                var photoY = top + y;
                for (var x = 0; x < subjectWidth; x++)
                {
                    var photoX = left + x;
                    var to = y * subjectWidth + x;
                    var from = Math.Clamp(photoY, 0, photo.Height - 1) * photo.Width + Math.Clamp(photoX, 0, photo.Width - 1);
                    colours[to * 3] = photo.Bgr[from * 3];
                    colours[to * 3 + 1] = photo.Bgr[from * 3 + 1];
                    colours[to * 3 + 2] = photo.Bgr[from * 3 + 2];
                    alpha[to] = photoX >= 0 && photoX < photo.Width && photoY >= 0 && photoY < photo.Height ? mask[photoY * photo.Width + photoX] : 0f;
                }
            }
            colours = PlaneResizer.Resize(colours, 3, subjectWidth, subjectHeight, contentWidth, contentHeight);
            alpha = PlaneResizer.Resize(alpha, 1, subjectWidth, subjectHeight, contentWidth, contentHeight);

            // Put on the canvas, which has the room for the outline and the shadow all round
            var count = width * height;
            var subject = new float[count];
            var colour = new float[count * 3];
            for (var y = 0; y < contentHeight; y++)
            {
                for (var x = 0; x < contentWidth; x++)
                {
                    var from = y * contentWidth + x;
                    var to = (y + padding) * width + x + padding;
                    subject[to] = Math.Clamp(alpha[from], 0f, 1f);
                    colour[to * 3] = colours[from * 3];
                    colour[to * 3 + 1] = colours[from * 3 + 1];
                    colour[to * 3 + 2] = colours[from * 3 + 2];
                }
            }

            if (settings.EdgeShift != 0)
                subject = MoveEdge(subject, width, height, settings.EdgeShift);

            var outline = settings.Outline && settings.OutlineWidth > 0 ? Outline(subject, width, height, settings.OutlineWidth) : null;
            var shadow = settings.Shadow ? Shadow(outline ?? subject, width, height) : null;
            return Compose(colour, subject, outline, shadow, settings.OutlineRgb, width, height);
        }

        /// <summary>
        /// The cutout moved <paramref name="shift"/> pixels at its edge (more takes in more, less cuts more away, and the cutout keeps the soft edge it has inside): the edge is where the cutout is half in,
        /// and its distance to every point is what says how far a point is from the new edge.
        /// </summary>
        internal static float[] MoveEdge(float[] subject, int width, int height, int shift)
        {
            var inside = new bool[subject.Length];
            for (var i = 0; i < inside.Length; i++)
                inside[i] = subject[i] >= Inside;

            var result = new float[subject.Length];
            if (shift > 0)
            {
                var distance = DistanceTransform.To(inside, width, height); // from every point to the subject
                for (var i = 0; i < result.Length; i++)
                    result[i] = Math.Max(subject[i], Math.Clamp(shift + 1 - distance[i], 0f, 1f));
            }
            else
            {
                var outside = new bool[subject.Length];
                for (var i = 0; i < outside.Length; i++)
                    outside[i] = !inside[i];
                var distance = DistanceTransform.To(outside, width, height); // from every point to what is not the subject
                for (var i = 0; i < result.Length; i++)
                    result[i] = Math.Min(subject[i], Math.Clamp(distance[i] + shift, 0f, 1f));
            }
            return result;
        }

        /// <summary>
        /// The outline: how much of each point (0 to 1) is within <paramref name="outlineWidth"/> of the subject. It is grown a little more than that, and then taken in again by the same little (a
        /// closing), so that notches narrower than that are filled and the line is smooth, as a blade that cuts a sticker out would leave it.
        /// </summary>
        internal static float[] Outline(float[] subject, int width, int height, int outlineWidth)
        {
            // The grown shape reaches the width plus the smoothing from the subject, which is more than the room the sticker has: the working grid has that room, so that the taking in sees the
            // outside beyond the edge of the sticker as the outside (and not as the end of the world, which would leave the outline that much wider there)
            var smoothing = Math.Max(1, outlineWidth / 2);
            var wideWidth = width + 2 * smoothing;
            var wideHeight = height + 2 * smoothing;

            var inside = new bool[wideWidth * wideHeight];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    inside[(y + smoothing) * wideWidth + x + smoothing] = subject[y * width + x] >= Inside;
            }

            var fromSubject = DistanceTransform.To(inside, wideWidth, wideHeight);
            var outsideOutline = new bool[inside.Length];
            for (var i = 0; i < outsideOutline.Length; i++)
                outsideOutline[i] = fromSubject[i] > outlineWidth + smoothing;

            var fromOutside = DistanceTransform.To(outsideOutline, wideWidth, wideHeight);
            var result = new float[subject.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    result[y * width + x] = Math.Max(subject[y * width + x], Math.Clamp(fromOutside[(y + smoothing) * wideWidth + x + smoothing] - smoothing, 0f, 1f));
            }
            return result;
        }

        /// <summary>The shadow: the shape moved down and blurred, as how much of each point is in shadow (0 to <see cref="ShadowOpacity"/>).</summary>
        internal static float[] Shadow(float[] shape, int width, int height)
        {
            var moved = new float[shape.Length];
            for (var y = ShadowOffset; y < height; y++)
                Array.Copy(shape, (y - ShadowOffset) * width, moved, y * width, width);

            var radius = (int)Math.Ceiling(3 * ShadowSigma);
            var kernel = new float[2 * radius + 1];
            float total = 0;
            for (var k = -radius; k <= radius; k++)
            {
                kernel[k + radius] = (float)Math.Exp(-k * (double)k / (2 * ShadowSigma * ShadowSigma));
                total += kernel[k + radius];
            }
            for (var k = 0; k < kernel.Length; k++)
                kernel[k] /= total;

            var across = new float[shape.Length];
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    float sum = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var at = x + k;
                        if (at >= 0 && at < width)
                            sum += moved[y * width + at] * kernel[k + radius];
                    }
                    across[y * width + x] = sum;
                }
            });

            var result = new float[shape.Length];
            Parallel.For(0, width, x =>
            {
                for (var y = 0; y < height; y++)
                {
                    float sum = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var at = y + k;
                        if (at >= 0 && at < height)
                            sum += across[at * width + x] * kernel[k + radius];
                    }
                    result[y * width + x] = sum * (float)ShadowOpacity;
                }
            });
            return result;
        }

        /// <summary>The layers put on one another: the shadow (black), the outline (the colour), the subject (the photo), the last on top. What is made is the picture with a straight alpha.</summary>
        private static StickerPicture Compose(float[] colour, float[] subject, float[]? outline, float[]? shadow, (byte Red, byte Green, byte Blue) outlineColour, int width, int height)
        {
            var bgra = new byte[width * height * 4];
            Parallel.For(0, height, y =>
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;

                    // Premultiplied: the colour already multiplied by how much of the point it covers
                    float a = 0, b = 0, g = 0, r = 0;
                    if (shadow is not null)
                        a = shadow[i]; // black: no colour to add

                    if (outline is not null)
                    {
                        var o = outline[i];
                        b = outlineColour.Blue * o + b * (1 - o);
                        g = outlineColour.Green * o + g * (1 - o);
                        r = outlineColour.Red * o + r * (1 - o);
                        a = o + a * (1 - o);
                    }

                    var s = subject[i];
                    b = colour[i * 3] * s + b * (1 - s);
                    g = colour[i * 3 + 1] * s + g * (1 - s);
                    r = colour[i * 3 + 2] * s + r * (1 - s);
                    a = s + a * (1 - s);

                    var p = i * 4;
                    if (a < 0.5f / 255f)
                        continue; // nothing there: transparent black

                    bgra[p] = (byte)Math.Clamp((int)(b / a + 0.5f), 0, 255);
                    bgra[p + 1] = (byte)Math.Clamp((int)(g / a + 0.5f), 0, 255);
                    bgra[p + 2] = (byte)Math.Clamp((int)(r / a + 0.5f), 0, 255);
                    bgra[p + 3] = (byte)Math.Clamp((int)(a * 255f + 0.5f), 0, 255);
                }
            });
            return new StickerPicture(width, height, bgra);
        }
    }
}
