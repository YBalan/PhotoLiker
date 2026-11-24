namespace PhotoLiker.Manuscripts.Fixing
{
    /// <summary>Something that corrects a piece of text: it gets some lines read by a scanner and answers with the lines as they should be (or as near to it as it can).</summary>
    public interface ITextCorrector
    {
        /// <summary>The text corrected. Runs for a while (a language model), so it is meant for a thread of its own; it stops with <see cref="OperationCanceledException"/> when asked to.</summary>
        string Correct(string text, CancellationToken cancel);
    }

    /// <summary>
    /// Corrects the text of a page: it is given to a corrector in pieces (the lines that go together, as a paragraph does, of no more than <see cref="BlockLength"/> characters, so that the
    /// corrector is not lost in a long text and its answer comes in a time one can wait for), and what comes back is taken word by word (see <see cref="WordAlignment"/>). What is returned is the list of
    /// changes to make to the text: the text itself, with its lines, spaces, tabs and the marks of its pictures, is not touched, so a front end can put the changes into a rich text where the
    /// look of the words stays.
    /// </summary>
    public static class TextFixer
    {
        /// <summary>The longest piece that is given to the corrector at once (a line longer than this is a piece of its own).</summary>
        public const int BlockLength = 500;

        /// <summary>The pieces of a text to give to a corrector: where each starts and how long it is, in the order of the text. Pieces that have no letter in them (a number, a mark) are left out.</summary>
        public static IReadOnlyList<(int Start, int Length)> Blocks(string text, int maximum = BlockLength)
        {
            var blocks = new List<(int, int)>();
            var blockStart = -1;
            var blockEnd = 0;
            var position = 0;
            while (position <= text.Length)
            {
                var end = text.IndexOf('\n', position);
                if (end < 0)
                    end = text.Length;

                var line = text.AsSpan(position, end - position);
                if (line.Trim().IsEmpty)
                {
                    Close(); // a blank line ends the paragraph
                }
                else
                {
                    if (blockStart >= 0 && end - blockStart > maximum)
                        Close();
                    if (blockStart < 0)
                        blockStart = position;
                    blockEnd = end;
                }

                position = end + 1;
            }

            Close();
            return blocks;

            void Close()
            {
                if (blockStart >= 0 && HasLetter(text.AsSpan(blockStart, blockEnd - blockStart)))
                    blocks.Add((blockStart, blockEnd - blockStart));
                blockStart = -1;
            }
        }

        private static bool HasLetter(ReadOnlySpan<char> text)
        {
            foreach (var character in text)
            {
                if (char.IsLetter(character))
                    return true;
            }

            return false;
        }

        /// <summary>The changes that make the text right, from the first to the last; none when the corrector found nothing to correct.</summary>
        /// <param name="progress">Told, from the thread that runs this, how many pieces are done of how many there are.</param>
        public static IReadOnlyList<TextEdit> Fix(string text, ITextCorrector corrector, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancel = default)
        {
            var blocks = Blocks(text);
            var edits = new List<TextEdit>();
            progress?.Report((0, blocks.Count));
            for (var i = 0; i < blocks.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var (start, length) = blocks[i];
                var block = text.Substring(start, length);
                var corrected = corrector.Correct(block, cancel);
                edits.AddRange(WordAlignment.Edits(block, corrected, start));
                progress?.Report((i + 1, blocks.Count));
            }

            return edits;
        }
    }
}
