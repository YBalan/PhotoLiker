namespace PhotoLiker.Search
{
    /// <summary>
    /// Cuts a face out of a photo the way the ArcFace model expects it: turned, scaled and moved so that the eyes, nose
    /// and mouth corners land on fixed places of a 112 x 112 picture.
    /// </summary>
    public static class FaceAligner
    {
        public const int Size = 112;
        public const int Length = 3 * Size * Size;

        /// <summary>Where the five landmarks belong in the 112 x 112 picture (InsightFace's arcface template).</summary>
        internal static readonly Point2[] Template =
        [
            new(38.2946f, 51.6963f), new(73.5318f, 51.5014f), new(56.0252f, 71.7366f),
            new(41.5493f, 92.3655f), new(70.7299f, 92.2041f),
        ];

        /// <summary>
        /// Landmarks guessed from a face box, for a face that the detector did not find. The proportions are those of the
        /// boxes the detector draws, so this is close enough for the recognizer.
        /// </summary>
        public static Point2[] LandmarksFromBox(float x, float y, float width, float height) =>
        [
            new(x + 0.30f * width, y + 0.37f * height), new(x + 0.70f * width, y + 0.37f * height),
            new(x + 0.50f * width, y + 0.58f * height),
            new(x + 0.34f * width, y + 0.78f * height), new(x + 0.66f * width, y + 0.78f * height),
        ];

        /// <summary>The aligned face as model input: planar RGB, (value - 127.5) / 127.5.</summary>
        public static float[] Align(DecodedPhoto photo, IReadOnlyList<Point2> landmarks)
        {
            var (a, b, tx, ty) = EstimateSimilarity(landmarks, Template);
            var determinant = a * a + b * b;

            var plane = Size * Size;
            var data = new float[Length];
            for (var v = 0; v < Size; v++)
            {
                for (var u = 0; u < Size; u++)
                {
                    // Where this pixel of the aligned face comes from in the photo (the transform, inverted)
                    var dx = u - tx;
                    var dy = v - ty;
                    var sx = (a * dx + b * dy) / determinant;
                    var sy = (-b * dx + a * dy) / determinant;

                    var i = v * Size + u;
                    Sample(photo, sx, sy, out var blue, out var green, out var red);
                    data[i] = (red - 127.5f) / 127.5f;
                    data[plane + i] = (green - 127.5f) / 127.5f;
                    data[2 * plane + i] = (blue - 127.5f) / 127.5f;
                }
            }
            return data;
        }

        /// <summary>
        /// The rotation, scale and shift that bring <paramref name="source"/> closest to <paramref name="target"/>
        /// (least squares): x' = a x - b y + tx, y' = b x + a y + ty.
        /// </summary>
        internal static (double A, double B, double Tx, double Ty) EstimateSimilarity(IReadOnlyList<Point2> source, IReadOnlyList<Point2> target)
        {
            var count = source.Count;
            double sourceX = source.Average(p => p.X), sourceY = source.Average(p => p.Y);
            double targetX = target.Average(p => p.X), targetY = target.Average(p => p.Y);

            double dot = 0, cross = 0, energy = 0;
            for (var i = 0; i < count; i++)
            {
                var sx = source[i].X - sourceX;
                var sy = source[i].Y - sourceY;
                var tx = target[i].X - targetX;
                var ty = target[i].Y - targetY;
                dot += sx * tx + sy * ty;
                cross += sx * ty - sy * tx;
                energy += sx * sx + sy * sy;
            }

            var a = dot / energy;
            var b = cross / energy;
            return (a, b, targetX - (a * sourceX - b * sourceY), targetY - (b * sourceX + a * sourceY));
        }

        /// <summary>Bilinear sample; outside the photo it is black, like the border of the reference implementation.</summary>
        private static void Sample(DecodedPhoto photo, double x, double y, out float blue, out float green, out float red)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = (float)(x - x0);
            var fy = (float)(y - y0);

            blue = green = red = 0;
            for (var j = 0; j < 2; j++)
            {
                for (var i = 0; i < 2; i++)
                {
                    var px = x0 + i;
                    var py = y0 + j;
                    if (px < 0 || py < 0 || px >= photo.Width || py >= photo.Height)
                        continue;

                    var weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    var p = (py * photo.Width + px) * 3;
                    blue += photo.Bgr[p] * weight;
                    green += photo.Bgr[p + 1] * weight;
                    red += photo.Bgr[p + 2] * weight;
                }
            }
        }
    }
}
