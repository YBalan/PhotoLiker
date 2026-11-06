using System.Globalization;

namespace PhotoLiker.Video
{
    /// <summary>A place in a video as text: how it is written, and what may be typed to jump to one.</summary>
    public static class VideoTime
    {
        /// <summary>"1:17" for a minute and 17 seconds, "1:02:03" from an hour on; the seconds are cut, not rounded, so that the time shown never runs ahead of the picture.</summary>
        public static string Format(TimeSpan time)
        {
            var total = (long)Math.Max(0, Math.Floor(time.TotalSeconds));
            var hours = total / 3600;
            var minutes = total / 60 % 60;
            var seconds = total % 60;
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, seconds);
        }

        public static string Format(long milliseconds) => Format(TimeSpan.FromMilliseconds(milliseconds));

        /// <summary>
        /// What a person types to jump to a place: seconds ("83", "83.5", "83,5"), minutes and seconds ("1:23"), hours too ("1:02:03"), or the
        /// units spelled ("1h2m3s", "1m23s", "90s"). Spaces do not matter. Nothing that is negative, empty or not a time.
        /// </summary>
        public static bool TryParse(string? text, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var clean = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.').ToLowerInvariant();

            if (clean.Contains(':'))
            {
                var parts = clean.Split(':');
                if (parts.Length is < 2 or > 3)
                    return false;

                double total = 0;
                foreach (var part in parts)
                {
                    if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number < 0)
                        return false;
                    total = total * 60 + number;
                }
                return Done(total, out time);
            }

            if (clean.IndexOfAny(['h', 'm', 's']) >= 0)
            {
                double total = 0, number = 0;
                var digits = new System.Text.StringBuilder();
                foreach (var c in clean)
                {
                    if (char.IsDigit(c) || c == '.')
                    {
                        digits.Append(c);
                        continue;
                    }

                    var unit = c switch { 'h' => 3600, 'm' => 60, 's' => 1, _ => 0 };
                    if (unit == 0 || !double.TryParse(digits.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
                        return false;
                    total += number * unit;
                    digits.Clear();
                }
                return digits.Length == 0 && Done(total, out time);
            }

            return double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && Done(seconds, out time);
        }

        private static bool Done(double seconds, out TimeSpan time)
        {
            time = default;
            if (seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds / 2 || double.IsNaN(seconds))
                return false;

            time = TimeSpan.FromSeconds(seconds);
            return true;
        }
    }
}
