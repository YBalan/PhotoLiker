using System.Text;

namespace PhotoLiker.PhotoBook
{
    /// <summary>How wide text is set in a style; the PDF measures with its fonts, a test with a fixed width per character.</summary>
    public interface ITextMeasurer
    {
        /// <summary>The width of the text in points. The text has no line break in it.</summary>
        double Width(string text, RichStyle style);
    }

    /// <summary>A piece of a line: a text in one style, and where it starts on the line (points from the left side of the column).</summary>
    public sealed record TextPiece(string Text, RichStyle Style, double X, double Width);

    /// <summary>A line of text set in a column: its pieces, how tall it is, and how far down its baseline is.</summary>
    public sealed record TextLine(IReadOnlyList<TextPiece> Pieces, double Height, double Baseline)
    {
        public string Text => string.Concat(Pieces.Select(piece => piece.Text));
    }

    /// <summary>
    /// Sets a <see cref="RichDocument"/> in a column of a width: the words are put on lines as long as they fit, a word that is longer than the column is cut, a line break is kept, each line is
    /// placed as its paragraph says (left, centre, right, or spread to both sides). The output is lines with the position of each piece, so a writer only has to draw.
    /// </summary>
    public static class TextLayout
    {
        /// <summary>The height of a line, as a number of times the size of its biggest text.</summary>
        public const double LineSpacing = 1.25;

        /// <summary>How far down the baseline is in a line, as a number of times the size of its biggest text (the rest is what the letters hang below it, and the room between lines).</summary>
        public const double BaselineFactor = 1.0;

        public static List<TextLine> Layout(RichDocument document, double width, ITextMeasurer measurer)
        {
            var lines = new List<TextLine>();
            foreach (var paragraph in document.WithoutTrailingBlank().Paragraphs)
                LayoutParagraph(paragraph, width, measurer, lines);

            return lines;
        }

        /// <summary>The height of lines, one under the other.</summary>
        public static double Height(IReadOnlyList<TextLine> lines) => lines.Sum(line => line.Height);

        /// <summary>Text in one style, and how wide it is set.</summary>
        private readonly record struct Part(string Text, RichStyle Style, double Width);

        /// <summary>
        /// What a line is made of: a word (one or more parts, when its letters are in more than one style: they are never split between lines), a space, or a break that ends the line.
        /// </summary>
        private sealed record Atom(List<Part> Parts, bool Space, bool Break)
        {
            public double Width => Parts.Sum(part => part.Width);
        }

        private static void LayoutParagraph(RichParagraph paragraph, double width, ITextMeasurer measurer, List<TextLine> lines)
        {
            var atoms = Tokenize(paragraph, width, measurer);
            if (atoms.Count == 0)
            {
                lines.Add(BlankLine());
                return;
            }

            var current = new List<Atom>();
            var used = 0.0;
            foreach (var atom in atoms)
            {
                if (atom.Break)
                {
                    FinishLine(current, width, paragraph.Align, lines, endsParagraph: true);
                    current = [];
                    used = 0;
                    continue;
                }

                // A space that would start a line is dropped
                if (atom.Space)
                {
                    if (current.Count > 0)
                    {
                        current.Add(atom);
                        used += atom.Width;
                    }
                    continue;
                }

                if (current.Count > 0 && used + atom.Width > width + 0.01)
                {
                    FinishLine(current, width, paragraph.Align, lines, endsParagraph: false);
                    current = [];
                    used = 0;
                }

                current.Add(atom);
                used += atom.Width;
            }

            FinishLine(current, width, paragraph.Align, lines, endsParagraph: true);
        }

        private static TextLine BlankLine() => new([], RichStyle.DefaultSize * LineSpacing, RichStyle.DefaultSize * BaselineFactor);

        /// <summary>The words and spaces of a paragraph, a word that is wider than the column cut into parts that fit it.</summary>
        private static List<Atom> Tokenize(RichParagraph paragraph, double width, ITextMeasurer measurer)
        {
            var atoms = new List<Atom>();
            var word = new List<Part>(); // the parts of the word that is being read

            void EndWord()
            {
                if (word.Count == 0)
                    return;

                if (word.Sum(part => part.Width) <= width)
                    atoms.Add(new Atom([.. word], false, false));
                else
                    CutWord(word, width, measurer, atoms);

                word.Clear();
            }

            foreach (var run in paragraph.Runs)
            {
                var piece = new StringBuilder();

                void EndPiece()
                {
                    if (piece.Length == 0)
                        return;

                    var text = piece.ToString();
                    word.Add(new Part(text, run.Style, measurer.Width(text, run.Style)));
                    piece.Clear();
                }

                foreach (var c in run.Text)
                {
                    if (c == '\n')
                    {
                        EndPiece();
                        EndWord();
                        atoms.Add(new Atom([], false, true));
                    }
                    else if (c is ' ' or '\t')
                    {
                        EndPiece();
                        EndWord();
                        var space = c == '\t' ? "    " : " ";
                        atoms.Add(new Atom([new Part(space, run.Style, measurer.Width(space, run.Style))], true, false));
                    }
                    else
                    {
                        piece.Append(c);
                    }
                }

                EndPiece();
            }

            EndWord();
            return atoms;
        }

        /// <summary>A word wider than the column is cut where a line is full, as many letters on each line as fit (one at least, or it would never end).</summary>
        private static void CutWord(List<Part> parts, double width, ITextMeasurer measurer, List<Atom> atoms)
        {
            var chunk = new List<Part>();
            var used = 0.0;

            void EndChunk(bool more)
            {
                if (chunk.Count > 0)
                    atoms.Add(new Atom([.. chunk], false, false));
                if (more)
                    atoms.Add(new Atom([], false, true)); // the cut ends a line

                chunk.Clear();
                used = 0;
            }

            foreach (var part in parts)
            {
                var text = part.Text;
                while (text.Length > 0)
                {
                    var whole = measurer.Width(text, part.Style);
                    if (used + whole <= width)
                    {
                        chunk.Add(new Part(text, part.Style, whole));
                        used += whole;
                        break;
                    }

                    var length = 0;
                    while (length < text.Length && used + measurer.Width(text[..(length + 1)], part.Style) <= width)
                        length++;

                    if (length == 0)
                    {
                        if (chunk.Count > 0)
                        {
                            EndChunk(more: true); // this line is full: try again on the next
                            continue;
                        }

                        length = 1;
                    }

                    var head = text[..length];
                    var headWidth = measurer.Width(head, part.Style);
                    chunk.Add(new Part(head, part.Style, headWidth));
                    used += headWidth;
                    text = text[length..];
                    if (text.Length > 0)
                        EndChunk(more: true); // what is left of the part goes on the next line
                }
            }

            EndChunk(more: false);
        }

        private static void FinishLine(List<Atom> atoms, double width, TextAlign align, List<TextLine> lines, bool endsParagraph)
        {
            // The spaces at the end of a line are not part of it
            while (atoms.Count > 0 && atoms[^1].Space)
                atoms.RemoveAt(atoms.Count - 1);

            if (atoms.Count == 0)
            {
                lines.Add(BlankLine());
                return;
            }

            var size = atoms.SelectMany(atom => atom.Parts).Max(part => part.Style.PointSize);
            var total = atoms.Sum(atom => atom.Width);
            var spaces = atoms.Count(atom => atom.Space);

            var x = 0.0;
            var extra = 0.0; // what each space is stretched by in a justified line
            switch (align)
            {
                case TextAlign.Center:
                    x = Math.Max(0, (width - total) / 2);
                    break;
                case TextAlign.Right:
                    x = Math.Max(0, width - total);
                    break;
                case TextAlign.Justify when !endsParagraph && spaces > 0 && total < width:
                    extra = (width - total) / spaces;
                    break;
            }

            var pieces = new List<TextPiece>();
            foreach (var atom in atoms)
            {
                if (atom.Space && extra > 0)
                {
                    // The space is kept in the text of the word before it (so that a copy of the text has its spaces) and takes its stretched width
                    if (pieces.Count > 0)
                        pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + " " };
                    x += atom.Width + extra;
                    continue;
                }

                foreach (var part in atom.Parts)
                {
                    // In a line that is not spread, what follows in the same style is one text (words and the spaces between them), which is how a PDF is best copied from
                    if (extra == 0 && pieces.Count > 0 && pieces[^1].Style == part.Style && Math.Abs(pieces[^1].X + pieces[^1].Width - x) < 0.001)
                        pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + part.Text, Width = pieces[^1].Width + part.Width };
                    else
                        pieces.Add(new TextPiece(part.Text, part.Style, x, part.Width));

                    x += part.Width;
                }
            }

            lines.Add(new TextLine(pieces, size * LineSpacing, size * BaselineFactor));
        }
    }
}
