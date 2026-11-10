namespace PhotoLiker.Effects.Colorize
{
    /// <summary>
    /// The CIE Lab colour space as the colorizing model knows it: the way OpenCV does it with floats (L from 0 to 100, a and b about -127 to 127, D65 white, sRGB with its gamma),
    /// because DDColor was trained against that. L is the lightness of a point, a and b its colour; a black and white photo has L only.
    /// </summary>
    internal static class LabColor
    {
        private const float WhiteX = 0.950456f;
        private const float WhiteZ = 1.088754f;
        private const float Epsilon = 0.008856f;     // the linear light below which Lab is a straight line
        private const float LabEpsilon = 7.9996f;    // L of that light
        private const int GammaSteps = 8192;

        /// <summary>The light of an sRGB byte (0 to 1, linear).</summary>
        private static readonly float[] ByteToLinear = BuildByteToLinear();

        /// <summary>The sRGB byte of a light, in <see cref="GammaSteps"/> steps from 0 to 1.</summary>
        private static readonly byte[] LinearToByte = BuildLinearToByte();

        private static float[] BuildByteToLinear()
        {
            var table = new float[256];
            for (var i = 0; i < 256; i++)
                table[i] = ToLinear(i / 255f);
            return table;
        }

        private static byte[] BuildLinearToByte()
        {
            var table = new byte[GammaSteps + 1];
            for (var i = 0; i <= GammaSteps; i++)
                table[i] = (byte)Math.Clamp(FromLinear(i / (float)GammaSteps) * 255f + 0.5f, 0f, 255f);
            return table;
        }

        public static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

        public static float FromLinear(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

        /// <summary>The light (luminance, 0 to 1) of a pixel given as sRGB bytes.</summary>
        public static float Luminance(byte blue, byte green, byte red) =>
            0.212671f * ByteToLinear[red] + 0.715160f * ByteToLinear[green] + 0.072169f * ByteToLinear[blue];

        /// <summary>L of a light.</summary>
        public static float LightnessOf(float luminance) =>
            luminance > Epsilon ? 116f * MathF.Cbrt(luminance) - 16f : 903.3f * luminance;

        /// <summary>L, a and b of a pixel given as sRGB bytes.</summary>
        public static (float L, float A, float B) FromBgr(byte blue, byte green, byte red)
        {
            var r = ByteToLinear[red];
            var g = ByteToLinear[green];
            var b = ByteToLinear[blue];
            var x = (0.412453f * r + 0.357580f * g + 0.180423f * b) / WhiteX;
            var y = 0.212671f * r + 0.715160f * g + 0.072169f * b;
            var z = (0.019334f * r + 0.119193f * g + 0.950227f * b) / WhiteZ;
            var fy = F(y);
            return (y > Epsilon ? 116f * fy - 16f : 903.3f * y, 500f * (F(x) - fy), 200f * (fy - F(z)));
        }

        /// <summary>The sRGB bytes of a colour given as L, a and b; a colour that cannot be shown is brought to the nearest that can, channel by channel.</summary>
        public static void ToBgr(float l, float a, float b, out byte blue, out byte green, out byte red)
        {
            float fy, y;
            if (l > LabEpsilon)
            {
                fy = (l + 16f) / 116f;
                y = fy * fy * fy;
            }
            else
            {
                y = l / 903.3f;
                fy = 7.787f * y + 16f / 116f;
            }

            var x = FInverse(fy + a / 500f) * WhiteX;
            var z = FInverse(fy - b / 200f) * WhiteZ;
            var r = 3.240479f * x - 1.537150f * y - 0.498535f * z;
            var g = -0.969256f * x + 1.875991f * y + 0.041556f * z;
            var bl = 0.055648f * x - 0.204043f * y + 1.057311f * z;

            red = Gamma(r);
            green = Gamma(g);
            blue = Gamma(bl);
        }

        private static byte Gamma(float linear) =>
            LinearToByte[(int)(Math.Clamp(linear, 0f, 1f) * GammaSteps + 0.5f)];

        private static float F(float t) => t > Epsilon ? MathF.Cbrt(t) : 7.787f * t + 16f / 116f;

        private static float FInverse(float t) => t > 0.2068966f ? t * t * t : (t - 16f / 116f) / 7.787f;
    }
}
