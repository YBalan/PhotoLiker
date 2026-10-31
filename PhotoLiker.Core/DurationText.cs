using System.Globalization;

namespace PhotoLiker.Core
{
    /// <summary>A length of time as people read it on a player: <c>3:25</c>, <c>1:02:15</c>, and <c>4.5 s</c> for a very short one.</summary>
    public static class DurationText
    {
        public static string Format(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0)
                seconds = 0;

            if (seconds < 10)
                return string.Create(CultureInfo.InvariantCulture, $"{seconds:0.0} s");

            var total = (long)Math.Round(seconds);
            var hours = total / 3600;
            var minutes = total % 3600 / 60;
            var rest = total % 60;
            return hours > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{rest:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{rest:00}");
        }
    }
}
