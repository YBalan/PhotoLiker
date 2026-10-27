using System.Globalization;

namespace PhotoLiker.Core
{
    /// <summary>
    /// PageUp / PageDown: from the shown photo to the first photo of the next / the previous month. The list is cut into
    /// runs of photos of the same month, in the order of the list: a run ends where the month changes, so a list that is not
    /// in date order (a folder of camera counters) simply has more runs. Photos without a date form runs of their own.
    /// </summary>
    public static class MonthNavigation
    {
        /// <summary>A month as one number (September 2009 is 200909), so that months are compared and listed in order; null if there is no date.</summary>
        public static int? MonthOf(DateTime? date) => date is { } d ? d.Year * 100 + d.Month : null;

        /// <summary>"September 2009", or "no date" for photos without one.</summary>
        public static string Describe(int? month) =>
            month is { } m ? new DateTime(m / 100, m % 100, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture) : "no date";

        /// <summary>
        /// The index of the first photo of the next (<paramref name="forward"/>) or the previous run of the same month, counted
        /// from <paramref name="current"/>; -1 if there is none (the shown photo is in the last / the first run).
        /// </summary>
        public static int FindMonthStart<T>(IReadOnlyList<T> items, int current, Func<T, int?> monthOf, bool forward)
        {
            if (current < 0 || current >= items.Count)
                return -1;

            var month = monthOf(items[current]);

            if (forward)
            {
                for (var i = current + 1; i < items.Count; i++)
                {
                    if (monthOf(items[i]) != month)
                        return i;
                }
                return -1;
            }

            var start = current;
            while (start > 0 && monthOf(items[start - 1]) == month)
                start--;
            if (start == 0)
                return -1;

            var previous = monthOf(items[start - 1]);
            var first = start - 1;
            while (first > 0 && monthOf(items[first - 1]) == previous)
                first--;
            return first;
        }
    }
}
