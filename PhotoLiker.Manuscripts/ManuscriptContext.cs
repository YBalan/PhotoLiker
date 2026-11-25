namespace PhotoLiker.Manuscripts
{
    /// <summary>
    /// The context an AI service is given with a page: the text of the pages before it, which the service goes by (the names of the people and places of this manuscript, the words the writer uses,
    /// the way things are spelled) when it corrects a text or reads a photo. It is the end of the text before the page, nearest page last, so that it reads on into the page; only pages that are in the book and
    /// have text count.
    /// </summary>
    public static class ManuscriptContext
    {
        /// <summary>How many characters of text of the pages before are given (about a thousand words): the service is paid by what it is sent, and the pages nearest are what counts.</summary>
        public const int MaximumCharacters = 3000;

        /// <summary>How many pages before the page the text is taken from.</summary>
        public const int MaximumPages = 3;

        /// <summary>The text of the pages before the page <paramref name="index"/>, in their order, pages told apart by a blank line; empty when no page before it has text.</summary>
        /// <param name="pages">The pages of the manuscript, in their order.</param>
        /// <param name="index">The page that is corrected or read.</param>
        public static string Before(IReadOnlyList<ManuscriptPage> pages, int index, int maximumCharacters = MaximumCharacters, int maximumPages = MaximumPages)
        {
            var found = new List<string>();
            var length = 0;
            for (var i = Math.Min(index, pages.Count) - 1; i >= 0 && found.Count < maximumPages && length < maximumCharacters; i--)
            {
                if (pages[i].LeftOut == true)
                    continue; // it is not in the book

                var text = TextOf(pages[i]);
                if (text.Length == 0)
                    continue;

                // The blank line between two pages is part of what is given
                var separator = found.Count > 0 ? PageSeparator.Length : 0;
                var room = maximumCharacters - length - separator;
                if (room <= 0)
                    break;

                if (text.Length > room)
                    text = FromLineStart(text, text.Length - room);

                found.Insert(0, text);
                length += text.Length + separator;
            }

            return string.Join(PageSeparator, found);
        }

        private const string PageSeparator = "\n\n";

        /// <summary>The text of a page with nothing of its look (its pictures are not text), the lines ending with line feeds, no blank line at the ends or more than one in a row; what is only blanks is nothing.</summary>
        public static string TextOf(ManuscriptPage page)
        {
            var text = ManuscriptText.PlainOf(page.Rtf).Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.TrimEnd();
                if (trimmed.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0))
                    continue; // a blank line at the start, or a second one in a row

                lines.Add(trimmed);
            }

            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            return string.Join('\n', lines);
        }

        /// <summary>The text from the first whole line at or after a place: a page is cut at the start of a line, not in a word.</summary>
        private static string FromLineStart(string text, int start)
        {
            if (start <= 0)
                return text;

            var lineStart = text.IndexOf('\n', start - 1);
            return lineStart < 0 || lineStart + 1 >= text.Length ? text[start..].TrimStart() : text[(lineStart + 1)..];
        }
    }
}
