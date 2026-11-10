using PhotoLiker.Search;

namespace PhotoLiker.Effects.Colorize
{
    /// <summary>
    /// How much colour a photo has, for telling the photos that want colours (black and white ones, and the old ones toned brown or blue by their paper) from the photos that have them.
    /// </summary>
    /// <param name="MeanChroma">The average strength of the colour of a point (Lab: the distance of its a and b from grey). 0 is grey; a snapshot has 15 to 40.</param>
    /// <param name="MinorSpread">How far the colours of the points spread across the one way they mostly go (the deviation of a and b across their main direction). A tinted grey photo has all its colours on a line, from grey to its tint; a photo with colours has them all over.</param>
    public readonly record struct ColorAmount(float MeanChroma, float MinorSpread)
    {
        /// <summary>A mean chroma below this is a grey photo, whatever the way its few colours go: a scan has a slight cast, a JPEG a little noise.</summary>
        public const float GreyChroma = 5f;

        /// <summary>A photo with one tint (a spread across it below this) is black and white, if the tint is not stronger than <see cref="MostTintedChroma"/>. Tight on purpose: a photo of a blue sea and sky has a spread of 2 to 3, a real tinted print more than the 0.3 of one that was toned by a program; a photo that is let go by this can still be colorized by hand.</summary>
        public const float OneTintSpread = 1.5f;

        /// <summary>The strongest tint a black and white photo has: sepia is 10 to 25. A photo of a red wall has a thin spread too, and a chroma well above this.</summary>
        public const float MostTintedChroma = 30f;

        /// <summary>Whether the photo is black and white (or tinted all one colour): the ones that Colorize is for.</summary>
        public bool IsBlackAndWhite => MeanChroma < GreyChroma || (MinorSpread < OneTintSpread && MeanChroma < MostTintedChroma);
    }

    /// <summary>Measures the colour of a photo (see <see cref="ColorAmount"/>).</summary>
    public static class BlackAndWhite
    {
        /// <summary>About this many points of a photo are looked at, in a grid; more tell no more.</summary>
        private const int Samples = 6000;

        public static ColorAmount Measure(DecodedPhoto photo)
        {
            var step = Math.Max(1, (int)Math.Sqrt((double)photo.Width * photo.Height / Samples));
            double count = 0, sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0, sumChroma = 0;

            for (var y = step / 2; y < photo.Height; y += step)
            {
                for (var x = step / 2; x < photo.Width; x += step)
                {
                    var p = (y * photo.Width + x) * 3;
                    var (_, a, b) = LabColor.FromBgr(photo.Bgr[p], photo.Bgr[p + 1], photo.Bgr[p + 2]);
                    count++;
                    sumA += a;
                    sumB += b;
                    sumAA += (double)a * a;
                    sumBB += (double)b * b;
                    sumAB += (double)a * b;
                    sumChroma += Math.Sqrt((double)a * a + (double)b * b);
                }
            }

            if (count == 0)
                return new ColorAmount(0, 0);

            // The spread of the colours across their main direction is the smaller root of the covariance matrix of a and b
            var meanA = sumA / count;
            var meanB = sumB / count;
            var varA = Math.Max(0, sumAA / count - meanA * meanA);
            var varB = Math.Max(0, sumBB / count - meanB * meanB);
            var covariance = sumAB / count - meanA * meanB;
            var half = (varA + varB) / 2;
            var difference = Math.Sqrt(Math.Pow((varA - varB) / 2, 2) + covariance * covariance);
            var smaller = Math.Max(0, half - difference);

            return new ColorAmount((float)(sumChroma / count), (float)Math.Sqrt(smaller));
        }
    }
}
