namespace PhotoLiker.Core
{
    /// <summary>
    /// A number with its noun in the right form: "1 folder", "3 folders". The noun comes as the forms of a language separated by |, in
    /// the order of its rule (<see cref="PluralRule"/>): English "folder|folders", Ukrainian "папка|папки|папок" (one, few, many). A
    /// language gives as many forms as it needs; the last one is used for a rule that has no form of its own.
    /// </summary>
    public static class Plural
    {
        /// <summary>The number and the noun in its form: "1 folder", "1,234 folders".</summary>
        public static string Count(int number, string forms) => $"{number:N0} {Form(number, forms)}";

        /// <summary>The noun alone in the form that goes with the number.</summary>
        public static string Form(int number, string forms)
        {
            var parts = forms.Split('|');
            var index = Index(number, Loc.Language.Plural);
            return parts[Math.Min(index, parts.Length - 1)];
        }

        /// <summary>The place of the form among the forms of a language that follows <paramref name="rule"/>.</summary>
        public static int Index(int number, PluralRule rule)
        {
            var n = Math.Abs((long)number);
            if (rule != PluralRule.EastSlavic)
                return n == 1 ? 0 : 1;

            var lastDigit = n % 10;
            var lastTwo = n % 100;
            if (lastDigit == 1 && lastTwo != 11)
                return 0;

            return lastDigit is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? 1 : 2;
        }
    }
}
