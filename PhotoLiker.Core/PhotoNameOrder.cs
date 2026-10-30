namespace PhotoLiker.Core
{
    /// <summary>
    /// The order of <see cref="PhotoSortOrder.ByName"/>: the way Explorer lists files, so <c>IMG_2</c> comes before <c>IMG_10</c>.
    /// The same everywhere it is used (the folder, a search, a video), and the same on every platform because it does not depend
    /// on the culture of the machine.
    /// </summary>
    public static class PhotoNameOrder
    {
        /// <summary>
        /// By folder, then by file name. Case is ignored, and a run of digits counts as the number it makes (<c>2</c> before <c>10</c>,
        /// <c>007</c> the same as <c>7</c>). The photos of a folder come before the photos of its subfolders. Two paths that
        /// are not the same never compare equal, so a list sorted this way has one order only.
        /// </summary>
        public static int Compare(string pathA, string pathB)
        {
            var byFolder = Natural(Path.GetDirectoryName(pathA.AsSpan()), Path.GetDirectoryName(pathB.AsSpan()));
            if (byFolder != 0)
                return byFolder;

            var byName = Natural(Path.GetFileName(pathA.AsSpan()), Path.GetFileName(pathB.AsSpan()));
            return byName != 0 ? byName : string.CompareOrdinal(pathA, pathB);
        }

        private static int Natural(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
                {
                    var numberA = TakeDigits(a, ref i);
                    var numberB = TakeDigits(b, ref j);

                    // The longer number is the bigger one, and of the same length the first different digit decides
                    var byNumber = numberA.Length != numberB.Length ? numberA.Length.CompareTo(numberB.Length) : numberA.CompareTo(numberB, StringComparison.Ordinal);
                    if (byNumber != 0)
                        return byNumber;
                }
                else
                {
                    var byChar = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (byChar != 0)
                        return byChar;

                    i++;
                    j++;
                }
            }

            // One is the start of the other: the shorter comes first
            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>The digits that start at <paramref name="at"/> without their leading zeros; <paramref name="at"/> moves behind them.</summary>
        private static ReadOnlySpan<char> TakeDigits(ReadOnlySpan<char> text, ref int at)
        {
            var start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
                at++;

            return text[start..at].TrimStart('0');
        }
    }
}
