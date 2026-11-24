namespace PhotoLiker.Manuscripts.Fixing
{
    /// <summary>
    /// Which words of a text a model that was asked to correct it really corrected. A language model that is asked to rewrite a text may also leave words out, add some, join two lines or answer
    /// with something of its own; so its answer is not taken as the text. Its words are laid against the words of the text, and only a word that was put in the place of a word that is
    /// much like it (a letter or two of it different: the way a scanner misreads) is taken; a word that came from nowhere, a word that was lost, the lines and the spaces of the text stay as they were.
    /// </summary>
    public static class WordAlignment
    {
        /// <summary>What a word may be changed by: no more than this share of its length (the letters that are put in or taken out or changed) -- more is another word.</summary>
        private const double MaximumChange = 0.4;

        /// <summary>What the text of a Rich Text Box has in the place of a picture (the object replacement character): a blank between two words.</summary>
        private const char PictureMark = (char)0xFFFC;

        /// <summary>The changes that make <paramref name="original"/> the words of <paramref name="corrected"/>, as far as they are corrections of the words (see the class). The places are those of <paramref name="original"/> plus <paramref name="offset"/>, from the first to the last.</summary>
        public static IReadOnlyList<TextEdit> Edits(string original, string corrected, int offset = 0)
        {
            var words = Split(original);
            var answer = Split(corrected);
            if (words.Count == 0 || answer.Count == 0)
                return [];

            var edits = new List<TextEdit>();
            foreach (var (from, to) in Pairs(words, answer))
            {
                var word = words[from];
                var replacement = Replacement(word.Text, answer[to].Text);
                if (replacement is not null && replacement != word.Text)
                    edits.Add(new TextEdit(offset + word.Start, word.Text.Length, replacement));
            }

            return edits;
        }

        /// <summary>The words of a text: what is between blanks (the blank, the new line, the tab, the mark a picture leaves), and where each is.</summary>
        internal static IReadOnlyList<(int Start, string Text)> Split(string text)
        {
            var words = new List<(int, string)>();
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var blank = i == text.Length || char.IsWhiteSpace(text[i]) || text[i] == PictureMark;
                if (!blank && start < 0)
                {
                    start = i;
                }
                else if (blank && start >= 0)
                {
                    words.Add((start, text[start..i]));
                    start = -1;
                }
            }

            return words;
        }

        // ── Laying the words against each other ─────────────────────────────

        /// <summary>
        /// The words that go together: the lowest cost of laying the answer against the text, where a word is much like the word it is laid against (cost 0 for the same word, up to two fifths for one that
        /// was corrected), and a word of one that has no word of the other to go with costs one, so that a word is not made another by a pair of words that are nothing alike.
        /// </summary>
        private static List<(int From, int To)> Pairs(IReadOnlyList<(int Start, string Text)> words, IReadOnlyList<(int Start, string Text)> answer)
        {
            int n = words.Count, m = answer.Count;
            var cost = new double[n + 1, m + 1];
            for (var i = 1; i <= n; i++)
                cost[i, 0] = i;
            for (var j = 1; j <= m; j++)
                cost[0, j] = j;

            var pair = new double?[n, m];
            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var best = Math.Min(cost[i - 1, j], cost[i, j - 1]) + 1;
                    var together = Distance(words[i - 1].Text, answer[j - 1].Text);
                    pair[i - 1, j - 1] = together;
                    if (together is { } c)
                        best = Math.Min(best, cost[i - 1, j - 1] + c);
                    cost[i, j] = best;
                }
            }

            var result = new List<(int, int)>();
            for (int i = n, j = m; i > 0 && j > 0;)
            {
                if (pair[i - 1, j - 1] is { } c && Math.Abs(cost[i, j] - (cost[i - 1, j - 1] + c)) < 1e-9)
                {
                    result.Add((i - 1, j - 1));
                    i--;
                    j--;
                }
                else if (Math.Abs(cost[i, j] - (cost[i - 1, j] + 1)) < 1e-9)
                {
                    i--;
                }
                else
                {
                    j--;
                }
            }

            result.Reverse();
            return result;
        }

        /// <summary>How much a corrected word differs from the word it replaces, or null when it is another word and not a correction of it.</summary>
        private static double? Distance(string word, string corrected)
        {
            if (word == corrected)
                return 0;

            var (_, core, _) = Parts(word);
            var (_, correctedCore, _) = Parts(corrected);
            if (core.Length == 0 || correctedCore.Length == 0)
                return null; // no word of letters in one of them: no correction

            if (core == correctedCore)
                return 0.05; // the same word, with other marks (a comma, a full stop) round it

            // The numbers of a text are not corrected
            if (!Digits(core).SequenceEqual(Digits(correctedCore)))
                return null;

            // One letter of a word of two ("ot" for "of") is half of it, which is over the limit, but it is the commonest slip of a scan, so it is allowed; a word of one letter is never changed
            var distance = Levenshtein(core, correctedCore);
            var length = Math.Max(core.Length, correctedCore.Length);
            var change = (double)distance / length;
            return change <= MaximumChange || (length == 2 && distance == 1) ? change : null;
        }

        /// <summary>What a corrected word is put in the text as: its letters, with the marks round it that the answer put there, or that the text had when the answer has none (an answer that leaves a full stop out is not taken to have taken it away).</summary>
        private static string? Replacement(string word, string corrected)
        {
            var (leading, core, trailing) = Parts(word);
            var (correctedLeading, correctedCore, correctedTrailing) = Parts(corrected);
            if (correctedCore.Length == 0)
                return null;

            return (correctedLeading.Length > 0 ? correctedLeading : leading) + correctedCore + (correctedTrailing.Length > 0 ? correctedTrailing : trailing);
        }

        /// <summary>A word as the marks before it, its letters and numbers (an apostrophe or a hyphen inside it is one of them), and the marks after it.</summary>
        internal static (string Leading, string Core, string Trailing) Parts(string word)
        {
            var start = 0;
            while (start < word.Length && !char.IsLetterOrDigit(word[start]))
                start++;

            var end = word.Length;
            while (end > start && !char.IsLetterOrDigit(word[end - 1]))
                end--;

            return (word[..start], word[start..end], word[end..]);
        }

        private static IEnumerable<char> Digits(string text) => text.Where(char.IsDigit);

        /// <summary>The number of letters that are put in, taken out or changed to make one text the other.</summary>
        internal static int Levenshtein(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++)
                row[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                var diagonal = row[0];
                row[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }

            return row[b.Length];
        }
    }
}
