using System.Drawing;
using System.Text;
using PhotoLiker.Drawing.Ocr;
using PhotoLiker.PhotoBook;
using TextLine = PhotoLiker.Drawing.Ocr.TextLine;

namespace PhotoLiker.Manuscripts
{
    /// <summary>
    /// One row of the text of a page as the page has it: the lines of the scan that stand side by side on it (a table has several to a row, a poem one), what they say (separated by tabs where the
    /// page has a gap between them), how far the row is from the left side of the text, where its tabs stop, and whether the page has more room than usual above it (a new paragraph).
    /// </summary>
    /// <param name="Lines">The numbers (in the list of lines the rows were made from) of the lines of the row, from the left.</param>
    /// <param name="Indent">Points of a column of <see cref="RichDocument.ReferenceWidth"/>, which the width of the text of the page is made as wide as; 0 for a row at the left side.</param>
    /// <param name="TabStops">Where the tabs of the row stop (in the same points); the columns of a table are at the same places in all its rows.</param>
    /// <param name="CentreY">Where the middle of the row is on the page, in its pixels.</param>
    public sealed record LayoutRow(IReadOnlyList<int> Lines, string Text, double Indent, IReadOnlyList<double> TabStops, bool BlankBefore, double CentreY);

    /// <summary>
    /// Makes the text of a page like the page: the lines of a scan that are on one row of the page are one row of the text, with a tab where the page has a gap between them (and the tab stops where
    /// the columns of the page are); a row that does not start at the left side of the text is indented as far as it is on the page; more room than usual between two rows is a blank line. It
    /// is what is done with the lines a reader gave, nothing is read again: the text and the boxes on the photo stay together (a row of the text is the lines of a row of the page).
    /// </summary>
    public static class ManuscriptLayout
    {
        /// <summary>A gap between two rows of the page this many times the usual gap is where a paragraph ends.</summary>
        private const double ParagraphGap = 1.6;

        /// <summary>A row that starts less than this share of the width of the text from its left side is not indented (hands are not as straight as that; the first line of a paragraph is further in than 8 %).</summary>
        private const double MinimumIndent = 0.05;

        /// <summary>
        /// Two lines on a row with more room than this share of the width of the text between them are in two columns: a tab; with less, a space (measured on real pages: the space between two words
        /// of cursive is up to 5 % of the width of the page, the space between the columns of a table is more than 9 %).
        /// </summary>
        private const double TabGap = 0.08;

        /// <summary>Tab stops that are nearer than this share of the width of the text are one column.</summary>
        private const double ColumnTolerance = 0.025;

        /// <summary>Indents are made a multiple of this many points: a hand that drifts a little still has one indent for a paragraph.</summary>
        private const double IndentStep = 4;

        /// <summary>
        /// The rows of a page, from the lines of its scan (in reading order, as a scan gives them).
        /// </summary>
        /// <param name="pageWidth">The width of the page in its pixels, when it is known: the text of a page that is only a line or two is not taken to be narrower than a share of it.</param>
        public static IReadOnlyList<LayoutRow> Rows(IReadOnlyList<TextLine> lines, double pageWidth = 0)
        {
            if (lines.Count == 0)
                return [];

            // The lines that are on one row of the page, from the left
            var groups = new List<List<int>>();
            for (var i = 0; i < lines.Count; i++)
            {
                if (groups.Count > 0 && SameRow(lines[groups[^1][^1]], lines[i]))
                    groups[^1].Add(i);
                else
                    groups.Add([i]);
            }

            var bounds = lines.Select(line => line.Box.Bounds).ToArray();
            var rowLeft = groups.Select(group => group.Min(i => (double)bounds[i].Left)).ToList();
            var rowRight = groups.Select(group => group.Max(i => (double)bounds[i].Right)).ToList();

            // The left side of the text: where most rows start (a note in the margin is not where the text starts); its width
            var left = Percentile(rowLeft, 0.1);
            var width = Math.Max(rowRight.Max() - left, Math.Max(1, pageWidth * 0.4));
            double Points(double pixels) => pixels / width * RichDocument.ReferenceWidth;

            var texts = new List<string>();
            var rawStops = new List<List<double>>();
            foreach (var group in groups)
            {
                var text = new StringBuilder(lines[group[0]].Text);
                var stops = new List<double>();
                for (var k = 1; k < group.Count; k++)
                {
                    var gap = bounds[group[k]].Left - bounds[group[k - 1]].Right;
                    if (gap >= TabGap * width)
                    {
                        text.Append('\t');
                        stops.Add(Points(bounds[group[k]].Left - left));
                    }
                    else
                    {
                        text.Append(' ');
                    }

                    text.Append(lines[group[k]].Text);
                }

                texts.Add(text.ToString());
                rawStops.Add(stops);
            }

            var columns = Columns(rawStops.SelectMany(stops => stops), ColumnTolerance * RichDocument.ReferenceWidth);

            // Where the rows are, up and down, and how much room is usual between two
            var centres = groups.Select(group => group.Average(i => (double)lines[i].Box.Centre.Y)).ToList();
            var heights = groups.Select(group => group.Min(i => (double)lines[i].Box.Height)).ToList();
            var gaps = new List<double>();
            for (var i = 0; i + 1 < groups.Count; i++)
            {
                var gap = centres[i + 1] - centres[i];
                if (gap > Math.Min(heights[i], heights[i + 1]) * 0.5)
                    gaps.Add(gap);
            }

            var usual = gaps.Count >= 2 ? gaps.Order().ElementAt(gaps.Count / 2) : double.MaxValue;

            var rows = new List<LayoutRow>();
            for (var i = 0; i < groups.Count; i++)
            {
                var indent = Points(rowLeft[i] - left);
                indent = indent < MinimumIndent * RichDocument.ReferenceWidth ? 0 : Math.Round(indent / IndentStep) * IndentStep;
                var stops = rawStops[i].Select(raw => Nearest(columns, raw)).Distinct().Order().ToList();
                rows.Add(new LayoutRow(groups[i], texts[i], indent, stops, i > 0 && centres[i] - centres[i - 1] > ParagraphGap * usual, centres[i]));
            }

            return rows;
        }

        /// <summary>
        /// Rows for a text that was not read from boxes (an AI service gave it): a row for each line that is not blank, spread evenly over the height of the page. Nothing says where on the page
        /// each line really is, but pictures that were found on the page are put between the lines by where they are, and this is the best guess there is: a picture low on the page comes after most of the text.
        /// </summary>
        public static IReadOnlyList<LayoutRow> EvenRows(string text, double pageHeight)
        {
            var lines = new List<(string Text, bool BlankBefore)>();
            var blank = false;
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (line.Trim().Length == 0)
                {
                    blank = lines.Count > 0;
                    continue;
                }

                lines.Add((line, blank));
                blank = false;
            }

            return [.. lines.Select((line, i) => new LayoutRow([], line.Text, 0, [], line.BlankBefore, pageHeight * (i + 0.5) / lines.Count))];
        }

        /// <summary>The text of the rows, a row to a line, with a blank line where the page has more room than usual.</summary>
        public static string TextOf(IReadOnlyList<LayoutRow> rows)
        {
            var text = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                {
                    text.Append('\n');
                    if (rows[i].BlankBefore)
                        text.Append('\n');
                }

                text.Append(rows[i].Text);
            }

            return text.ToString();
        }

        /// <summary>The rows as a document: a paragraph to a row, indented and tabbed as the row is, and an empty paragraph where there is a blank line.</summary>
        public static RichDocument ToDocument(IReadOnlyList<LayoutRow> rows)
        {
            var paragraphs = new List<RichParagraph>();
            foreach (var row in rows)
            {
                if (row.BlankBefore)
                    paragraphs.Add(new RichParagraph(TextAlign.Left, []));

                paragraphs.Add(new RichParagraph(TextAlign.Left, [new RichRun(row.Text, default)]) { LeftIndent = row.Indent, TabStops = [.. row.TabStops] });
            }

            return new RichDocument(paragraphs);
        }

        /// <summary>The number of the row each line is in.</summary>
        public static int[] RowOfLine(IReadOnlyList<LayoutRow> rows, int lineCount)
        {
            var result = new int[lineCount];
            Array.Fill(result, -1);
            for (var r = 0; r < rows.Count; r++)
            {
                foreach (var line in rows[r].Lines)
                {
                    if (line >= 0 && line < lineCount)
                        result[line] = r;
                }
            }

            return result;
        }

        /// <summary>
        /// Two lines are on one row when their middles are about level (a third of a line apart at most), the second is to the right of the first, and they are side by side: the boxes of the lines of a
        /// scan are taller than the writing, so two lines one under the other can be level by that measure, and are told apart by being over the same part of the page.
        /// </summary>
        private static bool SameRow(TextLine a, TextLine b)
        {
            if (Math.Abs(a.Box.Centre.Y - b.Box.Centre.Y) > Math.Min(a.Box.Height, b.Box.Height) / 3f || b.Box.Centre.X <= a.Box.Centre.X)
                return false;

            Rectangle first = a.Box.Bounds, second = b.Box.Bounds;
            var overlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
            return overlap <= 0.1 * Math.Min(first.Width, second.Width);
        }

        private static double Percentile(List<double> values, double share)
        {
            var sorted = values.Order().ToList();
            return sorted[(int)Math.Floor((sorted.Count - 1) * share)];
        }

        /// <summary>The places that tab stops are around: stops no further apart than <paramref name="tolerance"/> are one place, the middle of them.</summary>
        private static List<double> Columns(IEnumerable<double> stops, double tolerance)
        {
            var columns = new List<double>();
            var group = new List<double>();
            foreach (var stop in stops.Order())
            {
                if (group.Count > 0 && stop - group[^1] > tolerance)
                {
                    columns.Add(group[group.Count / 2]);
                    group.Clear();
                }

                group.Add(stop);
            }

            if (group.Count > 0)
                columns.Add(group[group.Count / 2]);

            return columns;
        }

        private static double Nearest(List<double> columns, double stop) => columns.MinBy(column => Math.Abs(column - stop));
    }
}
