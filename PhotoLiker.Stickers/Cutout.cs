namespace PhotoLiker.Stickers
{
    /// <summary>
    /// From what the model thinks of a photo (a coarse map of how likely each point is subject) to the cutout: a mask the size of the photo, 0 for what is cut away and 1 for what stays, soft for a
    /// pixel or so at the edge. The steps are the ones that turn a raw mask into a clean one: it is enlarged to the photo's size, drawn to the edges that are in the photo, cut where the person
    /// said, and then the patches the model found in the background are dropped and the small holes in the subject are filled.
    /// </summary>
    public static class Cutout
    {
        /// <summary>How soft the cut is, in the model's certainty (0 to 1): the mask goes from nothing to all over this much of it, round the cut.</summary>
        internal const float Softness = 0.12f;

        /// <summary>A hole in the subject (a part of the background that has the subject all round it) up to this fraction of the subject's area is filled: such a hole is the model's doubt, not a gap between an arm and a body.</summary>
        internal const double SmallHole = 0.004;

        /// <summary>The cutout of a photo, as a mask (0 to 1, row after row) of the photo's size.</summary>
        /// <param name="map">What the model found; not needed (and not looked at) when the whole picture is the sticker.</param>
        public static float[] Shape(PhotoPixels photo, SubjectMap? map, StickerSettings settings)
        {
            ArgumentNullException.ThrowIfNull(photo);
            ArgumentNullException.ThrowIfNull(settings);
            settings = settings.Normalized();

            if (settings.Cutout == CutoutMode.WholePicture)
                return RoundedRectangle(photo.Width, photo.Height);

            ArgumentNullException.ThrowIfNull(map);

            var mask = Enlarge(map, photo.Width, photo.Height);
            if (settings.FollowEdges > 0)
                mask = SnapToEdges(photo, mask, settings.FollowEdges);

            mask = Cut(mask, 1f - settings.Sensitivity / 100f);
            mask = DropSmallParts(mask, photo.Width, photo.Height, settings.SmallParts / 100.0);
            return FillSmallHoles(mask, photo.Width, photo.Height);
        }

        /// <summary>The model's map made as large as the photo (a smooth blend between its points).</summary>
        internal static float[] Enlarge(SubjectMap map, int width, int height)
        {
            var mask = PlaneResizer.Resize(map.Values, 1, map.Width, map.Height, width, height);
            for (var i = 0; i < mask.Length; i++)
                mask[i] = Math.Clamp(mask[i], 0f, 1f);
            return mask;
        }

        /// <summary>The mask pulled to the edges of the photo (see <see cref="GuidedFilter"/>); <paramref name="strength"/> is 1 to 100, and 100 follows even the faintest edge.</summary>
        internal static float[] SnapToEdges(PhotoPixels photo, float[] mask, int strength)
        {
            var count = photo.Width * photo.Height;
            var brightness = new float[count];
            for (var i = 0; i < count; i++)
                brightness[i] = (0.114f * photo.Bgr[i * 3] + 0.587f * photo.Bgr[i * 3 + 1] + 0.299f * photo.Bgr[i * 3 + 2]) / 255f;

            // The model's mask is soft over about one point of its own grid (the photo's longer side divided by the side of that grid), so the neighbourhood the fit is made over is a few of those
            var radius = Math.Max(2, (int)Math.Round(Math.Max(photo.Width, photo.Height) / (double)SubjectDetector.ModelSide * 2));
            var epsilon = (float)(0.1 * Math.Pow(10, -3 * strength / 100.0));
            return GuidedFilter.Filter(brightness, mask, photo.Width, photo.Height, radius, epsilon);
        }

        /// <summary>The mask with a hard-ish cut at <paramref name="cutoff"/>: below it nothing stays, above it all does, and a narrow band round it is soft.</summary>
        internal static float[] Cut(float[] mask, float cutoff)
        {
            var result = new float[mask.Length];
            var low = cutoff - Softness;
            for (var i = 0; i < mask.Length; i++)
                result[i] = Math.Clamp((mask[i] - low) / (2 * Softness), 0f, 1f);
            return result;
        }

        /// <summary>
        /// The pieces of the cutout (points that are more than half in) that are smaller than <paramref name="fraction"/> of the biggest piece are removed, with the soft fringe round them.
        /// 0 keeps every piece. Nothing is removed from a cutout that has no piece at all.
        /// </summary>
        internal static float[] DropSmallParts(float[] mask, int width, int height, double fraction)
        {
            if (fraction <= 0)
                return mask;

            var inside = new bool[mask.Length];
            for (var i = 0; i < inside.Length; i++)
                inside[i] = mask[i] >= 0.5f;

            var parts = ConnectedParts.Find(inside, width, height, eightNeighbours: true);
            if (parts.Count <= 1)
                return mask;

            var biggest = 0;
            for (var label = 1; label <= parts.Count; label++)
                biggest = Math.Max(biggest, parts.Areas[label]);

            var kept = new bool[mask.Length];
            var dropped = false;
            for (var i = 0; i < kept.Length; i++)
            {
                var label = parts.Labels[i];
                if (label == 0)
                    continue;
                if (parts.Areas[label] >= biggest * fraction)
                    kept[i] = true;
                else
                    dropped = true;
            }
            if (!dropped)
                return mask;

            // The soft fringe of a piece that stays is only a pixel or two wide: what is farther from every kept point than that belongs to a dropped piece
            var near = Grow(kept, width, height, 3);
            var result = new float[mask.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = near[i] ? mask[i] : 0f;
            return result;
        }

        /// <summary>The cutout with the small holes in it filled (see <see cref="SmallHole"/>). A part of the background that touches the edge of the photo is the outside, not a hole.</summary>
        internal static float[] FillSmallHoles(float[] mask, int width, int height)
        {
            var outside = new bool[mask.Length];
            var area = 0;
            for (var i = 0; i < outside.Length; i++)
            {
                outside[i] = mask[i] < 0.5f;
                if (!outside[i])
                    area++;
            }
            if (area == 0)
                return mask;

            // The holes are 4-connected: two patches of background that touch only at a corner are two holes, as the subject round them is one piece
            var parts = ConnectedParts.Find(outside, width, height, eightNeighbours: false);
            var touching = parts.TouchingEdge(width, height);
            var limit = area * SmallHole;

            List<int>? fill = null;
            for (var label = 1; label <= parts.Count; label++)
            {
                if (parts.Areas[label] <= limit && !touching.Contains(label))
                    (fill ??= []).Add(label);
            }
            if (fill is null)
                return mask;

            var filled = new HashSet<int>(fill);
            var result = (float[])mask.Clone();
            for (var i = 0; i < result.Length; i++)
            {
                if (parts.Labels[i] != 0 && filled.Contains(parts.Labels[i]))
                    result[i] = 1f;
            }
            return result;
        }

        /// <summary>The set grown by <paramref name="distance"/> points in every direction (a square neighbourhood, which is all a fringe a few points wide needs).</summary>
        internal static bool[] Grow(bool[] set, int width, int height, int distance)
        {
            const int Far = 1_000_000_000; // 'no point of the set yet': far, but not so far that a difference of positions overflows
            var across = new bool[set.Length];
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                var last = -Far;
                for (var x = 0; x < width; x++)
                {
                    if (set[row + x])
                        last = x;
                    across[row + x] = x - last <= distance;
                }

                last = Far;
                for (var x = width - 1; x >= 0; x--)
                {
                    if (set[row + x])
                        last = x;
                    if (last - x <= distance)
                        across[row + x] = true;
                }
            }

            var result = new bool[set.Length];
            for (var x = 0; x < width; x++)
            {
                var last = -Far;
                for (var y = 0; y < height; y++)
                {
                    if (across[y * width + x])
                        last = y;
                    result[y * width + x] = y - last <= distance;
                }

                last = Far;
                for (var y = height - 1; y >= 0; y--)
                {
                    if (across[y * width + x])
                        last = y;
                    if (last - y <= distance)
                        result[y * width + x] = true;
                }
            }
            return result;
        }

        /// <summary>The whole picture as a cutout: a rectangle with rounded corners (the radius is a twelfth of the shorter side), its edge one point soft.</summary>
        internal static float[] RoundedRectangle(int width, int height)
        {
            var radius = Math.Min(width, height) / 12.0;
            var mask = new float[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // How far the point (the middle of the pixel) is from the shape: negative inside. The corners are circles, the sides straight.
                    var px = Math.Abs(x + 0.5 - width / 2.0) - (width / 2.0 - radius);
                    var py = Math.Abs(y + 0.5 - height / 2.0) - (height / 2.0 - radius);
                    var outsideX = Math.Max(px, 0);
                    var outsideY = Math.Max(py, 0);
                    var distance = Math.Sqrt(outsideX * outsideX + outsideY * outsideY) + Math.Min(Math.Max(px, py), 0) - radius;
                    mask[y * width + x] = (float)Math.Clamp(0.5 - distance, 0.0, 1.0);
                }
            }
            return mask;
        }
    }
}
