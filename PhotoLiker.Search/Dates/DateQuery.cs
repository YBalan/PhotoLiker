using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoLiker.Search
{
    /// <summary>
    /// A date, a month or a range of years asked for in a search. What is left out is "any": September 1 is that day in
    /// every year, September 2009 the whole month, 2009 the whole year.
    /// </summary>
    public sealed record DateFilter(int? YearFrom, int? YearTo, int? Month, int? Day)
    {
        public bool Matches(DateTime? taken) =>
            taken is { } date
            && (YearFrom is null || date.Year >= YearFrom)
            && (YearTo is null || date.Year <= YearTo)
            && (Month is null || date.Month == Month)
            && (Day is null || date.Day == Day);

        /// <summary>The filter in words, as it is shown above the results, so that a wrong reading of the query is seen.</summary>
        public string Describe()
        {
            var month = Month is { } m ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m) : null;
            var single = YearFrom is { } start && start == YearTo ? start : (int?)null;

            if (YearFrom is not null && YearTo is not null && single is null)
                return $"{YearFrom}-{YearTo}";

            return (month, Day, single) switch
            {
                (not null, not null, { } year) => $"{month} {Day}, {year}",
                (not null, not null, null) => $"{month} {Day} (any year)",
                (not null, null, { } year) => $"{month} {year}",
                (not null, null, null) => $"{month} (any year)",
                (null, _, { } year) => year.ToString(CultureInfo.InvariantCulture),
                _ => string.Empty,
            };
        }
    }

    /// <summary>A search text split into the date in it, if any, and the rest.</summary>
    /// <param name="Words">What is left of the text without the date: a description, a name, or nothing.</param>
    public sealed record ParsedQuery(string Words, DateFilter? Dates);

    /// <summary>
    /// Finds a date in a search text ("beach September 2009", "Mom 2005", "1 sep 2009", "2009-09-01", "2009-2011").
    /// English month names only. A text in double quotes is taken as it is, without looking for a date.
    /// </summary>
    public static class DateQuery
    {
        private const string Months = "jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";
        private const string FullMonths = "january|february|march|april|may|june|july|august|september|october|november|december";
        private const string Year = @"(?:19|20)\d{2}";
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // A bare month needs its full name ("mar" or "dec" alone are more likely something else); with a day or year the short names do
        private static readonly Regex IsoDate = new(@"\b(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})\b", Options);
        private static readonly Regex YearRange = new($@"\b(?<y1>{Year})\s*[-–—]\s*(?<y2>{Year})\b", Options);
        private static readonly Regex IsoMonth = new($@"\b(?<y>{Year})-(?<m>0?[1-9]|1[0-2])\b", Options);
        private static readonly Regex MonthDay = new($@"\b(?<mon>{Months})\b\.?\s+(?<d>\d{{1,2}})(?:st|nd|rd|th)?\b(?:,?\s+(?<y>{Year})\b)?", Options);
        private static readonly Regex DayMonth = new($@"\b(?<d>\d{{1,2}})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<mon>{Months})\b\.?(?:,?\s+(?<y>{Year})\b)?", Options);
        private static readonly Regex MonthYear = new($@"\b(?<mon>{Months})\b\.?,?\s+(?<y>{Year})\b", Options);
        // Not when a day number follows: "february 30" is no date, rather than February and a stray "30"
        private static readonly Regex BareMonth = new($@"\b(?<mon>{FullMonths})\b(?!\.?\s+\d{{1,2}}(?:st|nd|rd|th)?\b)", Options);
        private static readonly Regex BareYear = new($@"\b(?<y>{Year})\b", Options);

        // Words that join a date to the rest ("beach in September 2009") and are no use to the search
        private static readonly Regex EdgeLinks = new(@"^(?:(?:on|in|at|from|during|of|around)(?:\s+|$))+|(?:\s+(?:on|in|at|from|during|of|around))+$", Options);

        private sealed record Found(int Index, int Length, DateFilter Filter);

        /// <param name="currentYear">The latest year that counts as a year; passed in so that it can be tested.</param>
        public static ParsedQuery Parse(string text, int? currentYear = null)
        {
            var year = currentYear ?? DateTime.Now.Year;
            text = text.Trim();

            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                return new ParsedQuery(text[1..^1].Trim(), null);

            var first = FindFirst(text, year, IsoDate, YearRange, IsoMonth, MonthDay, DayMonth, MonthYear, BareMonth, BareYear);
            if (first is null)
                return new ParsedQuery(text, null);

            var filter = first.Filter;
            var rest = Cut(text, first);

            // A part of the date given apart from the rest: "2009 september 1", "september 1 on the beach 2009"
            if (filter.Month is not null && filter.YearFrom is null)
            {
                if (FindFirst(rest, year, BareYear) is { } yearPart)
                {
                    filter = filter with { YearFrom = yearPart.Filter.YearFrom, YearTo = yearPart.Filter.YearTo };
                    rest = Cut(rest, yearPart);
                }
            }
            else if (filter.Month is null && filter.YearFrom is not null && filter.YearFrom == filter.YearTo)
            {
                if (FindFirst(rest, year, MonthDay, DayMonth, BareMonth) is { } monthPart)
                {
                    filter = filter with { Month = monthPart.Filter.Month, Day = monthPart.Filter.Day };
                    rest = Cut(rest, monthPart);
                }
            }

            return new ParsedQuery(Tidy(rest), filter);
        }

        /// <summary>The match nearest to the start (the longest of those that start together), or null.</summary>
        private static Found? FindFirst(string text, int currentYear, params Regex[] patterns)
        {
            Found? best = null;
            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    if (ToFilter(pattern, match, currentYear) is not { } found)
                        continue;

                    var candidate = found with { Index = match.Index, Length = match.Length };
                    if (best is null || candidate.Index < best.Index || (candidate.Index == best.Index && candidate.Length > best.Length))
                        best = candidate;
                    break; // the first valid match of a pattern is its nearest
                }
            }
            return best;
        }

        private static Found? ToFilter(Regex pattern, Match match, int currentYear)
        {
            int? Number(string group) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : null;
            bool PlausibleYear(int? y) => y is null || (y >= 1900 && y <= currentYear + 1);

            var y = Number("y");
            var d = Number("d");
            int? m = match.Groups["mon"].Success ? MonthNumber(match.Groups["mon"].Value) : Number("m");

            if (ReferenceEquals(pattern, YearRange))
            {
                var (start, end) = (Number("y1")!.Value, Number("y2")!.Value);
                if (start > end)
                    (start, end) = (end, start);
                return PlausibleYear(start) && PlausibleYear(end) ? new Found(0, 0, new DateFilter(start, end, null, null)) : null;
            }

            if (!PlausibleYear(y) || m is < 1 or > 12)
                return null;
            if (d is { } day && (day < 1 || day > DateTime.DaysInMonth(y ?? 2000, m ?? 1))) // 2000: February 29 exists when no year is given
                return null;

            return new Found(0, 0, new DateFilter(y, y, m, d));
        }

        private static int MonthNumber(string name)
        {
            var prefix = name[..3].ToLowerInvariant();
            return Array.IndexOf(["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"], prefix) + 1;
        }

        private static string Cut(string text, Found found) => text.Remove(found.Index, found.Length).Insert(found.Index, " ");

        private static string Tidy(string text)
        {
            text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', ';', '-');
            return EdgeLinks.Replace(text, string.Empty).Trim(' ', ',', ';', '-');
        }
    }
}
