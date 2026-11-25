using System.Text;
using System.Text.RegularExpressions;
using PhotoLiker.PhotoBook;

namespace PhotoLiker.Manuscripts
{
    /// <summary>One change of a text: <paramref name="Length"/> characters from <paramref name="Index"/> become <paramref name="Replacement"/>.</summary>
    public readonly record struct TextEdit(int Index, int Length, string Replacement);

    /// <summary>Where a page stands: not scanned and nothing written, read by the scan and not changed since, or written or changed by the person.</summary>
    public enum PageState
    {
        New,
        Scanned,
        Edited,
    }

    /// <summary>What is done with the text of a page that has no window in it: the text a scan makes, whether it was changed, and joining the lines of a paragraph.</summary>
    public static class ManuscriptText
    {
        /// <summary>
        /// The characters a handwriting network writes for a word that goes on in the next line (the hyphen, and the sign that the Ukrainian model of Polyscriptor writes for it, ¬): at the end of a
        /// line they are no part of the word.
        /// </summary>
        private const string WordBreaks = "-¬­‐‑";

        /// <summary>
        /// The text of a scan, a row of the page to a line of the text (see <see cref="ManuscriptLayout"/>: lines side by side on a row are one row, with a tab between them where the page has a gap),
        /// in the order they are read, with a blank line where the page has more room than usual between two rows (a new paragraph, a letter's greeting, a signature): the lines that follow are joined by
        /// <see cref="JoinEdits"/> into paragraphs, and the blank lines stay.
        /// </summary>
        public static string FromLines(IEnumerable<PhotoLiker.Drawing.Ocr.TextLine> lines) => ManuscriptLayout.TextOf(ManuscriptLayout.Rows([.. lines]));

        /// <summary>
        /// A plain text (a line to a line, a blank line between paragraphs, tabs between columns) as a document of the book: a paragraph to a line, an empty paragraph for a blank line, as the text of a
        /// scan is laid out (see <see cref="ManuscriptLayout.ToDocument"/>), with nothing of how far the line is from the left side of the page, which only a scan that has boxes knows.
        /// </summary>
        public static RichDocument DocumentOf(string text)
        {
            var paragraphs = new List<RichParagraph>();
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                paragraphs.Add(new RichParagraph(TextAlign.Left, line.Length == 0 ? [] : [new RichRun(line, default)]));

            return new RichDocument(paragraphs);
        }

        /// <summary>The text of an RTF as plain text, a paragraph to a line; empty for no text (its pictures are not looked at).</summary>
        public static string PlainOf(string? rtf) =>
            string.IsNullOrWhiteSpace(rtf) ? string.Empty : string.Join("\n", RtfParser.Parse(rtf, readPictures: false).Paragraphs.Select(paragraph => paragraph.PlainText));

        /// <summary>Whether an RTF has a picture in it that the book can show (a PNG or a JPEG).</summary>
        public static bool HasPictures(string? rtf) =>
            !string.IsNullOrEmpty(rtf) && (rtf.Contains(@"\pngblip", StringComparison.Ordinal) || rtf.Contains(@"\jpegblip", StringComparison.Ordinal));

        /// <summary>Whether the page has anything for the book: text, or a picture.</summary>
        public static bool HasContent(ManuscriptPage page) => HasText(page) || HasPictures(page.Rtf);

        /// <summary>The text with every run of blanks (spaces and line breaks) made one space and the ends cut: two texts that differ only there are the same text.</summary>
        public static string Normalize(string? text) => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>Where a page stands, from its text and what the scan read.</summary>
        public static PageState StateOf(ManuscriptPage page)
        {
            if (page.Rtf is null && page.Scanned is null)
                return PageState.New; // a page nothing was done to (most pages of a long list): no text to look at

            var text = Normalize(PlainOf(page.Rtf));
            var scanned = Normalize(page.Scanned);
            if (text.Length == 0 && scanned.Length == 0)
                return PageState.New;

            return scanned.Length > 0 && string.Equals(text, scanned, StringComparison.Ordinal) ? PageState.Scanned : PageState.Edited;
        }

        /// <summary>Whether the page has text (more than blanks).</summary>
        public static bool HasText(ManuscriptPage page) => Normalize(PlainOf(page.Rtf)).Length > 0;

        /// <summary>
        /// The changes that join the lines of a paragraph into one line, for the part of <paramref name="text"/> from <paramref name="start"/> for <paramref name="length"/> characters (all of it when
        /// <paramref name="length"/> is negative), from the last to the first so that each can be done without moving the places of the ones before it. A line break between two lines of a paragraph
        /// becomes a space; a blank line (two line breaks, or a break next to a line that is only blanks) is where a paragraph ends and stays; a word that is split by a hyphen at the end of a line, and
        /// goes on in a small letter at the start of the next, is joined without the hyphen and without a space; a line that ends with a dash (a space before it) keeps the break after it as a space.
        /// </summary>
        public static List<TextEdit> JoinEdits(string text, int start = 0, int length = -1)
        {
            ArgumentNullException.ThrowIfNull(text);
            var end = length < 0 ? text.Length : Math.Min(text.Length, start + length);
            var edits = new List<TextEdit>();

            for (var i = Math.Max(0, start); i < end; i++)
            {
                if (text[i] != '\n')
                    continue;

                // The paragraph ends here when a blank line is next to this break, or the break is at an end of the text
                var before = LineBefore(text, i);
                var after = LineAfter(text, i);
                if (before.Trim().Length == 0 || after.Trim().Length == 0)
                    continue;

                var trimmedBefore = before.TrimEnd();
                var last = trimmedBefore[^1];
                var firstOfNext = after.TrimStart()[0];
                var wordBroken = WordBreaks.Contains(last)
                    && trimmedBefore.Length >= 2
                    && char.IsLetter(trimmedBefore[^2])
                    && char.IsLower(firstOfNext);

                if (wordBroken)
                {
                    // The hyphen (and any blanks after it), the break and the blanks that start the next line all go
                    var hyphenAt = i - (before.Length - trimmedBefore.Length) - 1;
                    var nextStart = i + 1 + (after.Length - after.TrimStart().Length);
                    edits.Add(new TextEdit(hyphenAt, nextStart - hyphenAt, string.Empty));
                }
                else
                {
                    var blanksBefore = before.Length - trimmedBefore.Length;
                    var blanksAfter = after.Length - after.TrimStart().Length;
                    edits.Add(new TextEdit(i - blanksBefore, blanksBefore + 1 + blanksAfter, " "));
                }
            }

            edits.Reverse();
            return edits;
        }

        /// <summary>The text with the edits done (they are as <see cref="JoinEdits"/> gives them: last first).</summary>
        public static string Apply(string text, IEnumerable<TextEdit> edits)
        {
            var result = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(edit => edit.Index))
                result.Remove(edit.Index, edit.Length).Insert(edit.Index, edit.Replacement);
            return result.ToString();
        }

        /// <summary>Where each line of the text that is not blank starts, and how long it is (without its line break), in the order of the text.</summary>
        public static List<(int Start, int Length)> LineSpans(string text)
        {
            var spans = new List<(int Start, int Length)>();
            var start = 0;
            while (start <= text.Length)
            {
                var end = text.IndexOf('\n', start);
                var length = (end < 0 ? text.Length : end) - start;
                if (text.AsSpan(start, length).Trim().Length > 0)
                    spans.Add((start, length));
                if (end < 0)
                    break;
                start = end + 1;
            }

            return spans;
        }

        /// <summary>The number (in <paramref name="spans"/>) of the line that the character at <paramref name="index"/> is in, the break at the end of a line counting as part of it; -1 on a blank line.</summary>
        public static int LineAt(IReadOnlyList<(int Start, int Length)> spans, int index)
        {
            for (var i = 0; i < spans.Count; i++)
            {
                if (index >= spans[i].Start && index <= spans[i].Start + spans[i].Length)
                    return i;
            }

            return -1;
        }

        private static string LineBefore(string text, int breakAt)
        {
            if (breakAt == 0)
                return string.Empty;

            return text[(text.LastIndexOf('\n', breakAt - 1) + 1)..breakAt];
        }

        private static string LineAfter(string text, int breakAt)
        {
            var end = text.IndexOf('\n', breakAt + 1);
            return text[(breakAt + 1)..(end < 0 ? text.Length : end)];
        }
    }
}
