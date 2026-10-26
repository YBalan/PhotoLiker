namespace PhotoLiker.Core
{
    /// <summary>
    /// Turns the raw value of an EXIF property (its type code and bytes, as GDI+ hands them out) into text.
    /// </summary>
    public static class ExifValueFormatter
    {
        public static string Format(int type, byte[]? value)
        {
            var bytes = value ?? [];
            try
            {
                return type switch
                {
                    2  => System.Text.Encoding.ASCII.GetString(bytes).Trim('\0'), // ASCII
                    3  => BitConverter.ToUInt16(bytes, 0).ToString(),             // Short
                    4  => BitConverter.ToUInt32(bytes, 0).ToString(),             // Long
                    5  => FormatRational(bytes),                                  // Rational
                    10 => FormatSignedRational(bytes),                            // SRational
                    _  => BitConverter.ToString(bytes)
                };
            }
            catch
            {
                return BitConverter.ToString(bytes);
            }
        }

        private static string FormatRational(byte[] bytes)
        {
            uint num = BitConverter.ToUInt32(bytes, 0);
            uint den = BitConverter.ToUInt32(bytes, 4);
            if (den == 0) return ImageHelperStrings.Infinity;
            return string.Format(ImageHelperStrings.RationalFormat, num, den, (double)num / den);
        }

        private static string FormatSignedRational(byte[] bytes)
        {
            int num = BitConverter.ToInt32(bytes, 0);
            int den = BitConverter.ToInt32(bytes, 4);
            if (den == 0) return ImageHelperStrings.Infinity;
            return string.Format(ImageHelperStrings.RationalFormat, num, den, (double)num / den);
        }
    }
}
