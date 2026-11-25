using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>
    /// Reads a page with an AI service that can see (see <see cref="CloudChat"/>): the photo of the page goes to it, with instructions on how to write the text down, and the text of the page comes
    /// back, a line to a line. It reads handwriting and print alike, in any of the languages, and it is far better at cursive and at pages the models of this computer cannot read; what it cannot do is
    /// say where on the photo each line is, so a page read this way has no boxes. The photo leaves the computer: the caller asks the person first. The text that comes back is the service's reading
    /// and nothing more is made of it (<see cref="Clean"/> only takes off the wrapping a service puts round an answer).
    /// </summary>
    public sealed class CloudReader
    {
        /// <summary>The longest side the photo is sent with: a page of handwriting is read well at this size, and more pixels are only a longer upload (the services shrink a larger photo themselves).</summary>
        public const int MaximumSide = 2000;

        private readonly CloudChat _chat;
        private readonly ScanLanguage _language;
        private readonly string? _context;

        /// <param name="chat">The service, with its key and its model.</param>
        /// <param name="language">The language of the page, which the instructions say.</param>
        /// <param name="context">The text of the pages before this one, to go by (names, words, spelling), or null for none.</param>
        public CloudReader(CloudChat chat, ScanLanguage language, string? context = null)
        {
            _chat = chat;
            _language = language;
            _context = string.IsNullOrWhiteSpace(context) ? null : context;
        }

        /// <summary>The text of the page in a photo (the bytes of a JPEG file, no more than <see cref="MaximumSide"/> on a side).</summary>
        /// <exception cref="AiServiceException">The service did not give a text.</exception>
        public async Task<string> ReadAsync(byte[] jpeg, CancellationToken cancel = default)
        {
            ChatTurn[] turns = [new(false, UserText(_context), new ChatImage("image/jpeg", jpeg))];
            var answer = await _chat.CompleteAsync(SystemText(_language), turns, cancel).ConfigureAwait(false);
            return Clean(answer);
        }

        /// <summary>
        /// What the service is told. A reader that is only asked to "transcribe" corrects what it reads (it makes a misspelling into the right word, an old spelling into the new one), puts a heading
        /// over the text and explains what it was not sure of: the instructions say what a transcription of a manuscript is, and that nothing else is wanted.
        /// </summary>
        internal static string SystemText(ScanLanguage language) =>
            "You read pages of manuscripts and printed documents from photos and write down their text. Write exactly what is on the page, the way the writer wrote it: keep the spelling, the punctuation, "
            + "the capital letters and the line breaks, a line of text for each line of the page. Put a blank line where the page has more room between two lines (a new paragraph, a greeting, a signature), "
            + "and a tab between the columns of a table or between a word and its translation. "
            + $"The text is in {LlmCorrector.LanguageName(language)}. "
            + "Do not correct, modernize, translate or explain anything. A word you cannot read is written as [?]; a word you are not sure of is written as your best reading of it. "
            + "Leave out what is not text (drawings, stains, the edge of the paper, the desk). Answer with the text of the page only: no comments, no title, no quotation marks, no code block.";

        /// <summary>What is said with the photo: to read it, and, when the pages before it are given, to go by them and not to write them again.</summary>
        internal static string UserText(string? context) =>
            string.IsNullOrWhiteSpace(context)
                ? "Read this page."
                : "Read this page.\n\nFor reference, here is the text of the pages that come before it (names, words and the way things are spelled in this manuscript). Do not write it again: read only the page in the photo.\n\n" + context;

        /// <summary>The answer as the text of the page: lines ended by line feeds, no spaces at the ends of the lines, no blank lines at the ends of the text or more than one in a row, and no code block round it.</summary>
        internal static string Clean(string answer)
        {
            // The text is trimmed first (and again by what takes the code block off), so it has no blank line at either end; only the second blank line in a row is left to take out
            var text = CloudCorrector.WithoutFence(answer.Replace("\r\n", "\n").Replace('\r', '\n').Trim());
            var cleaned = new List<string>();
            foreach (var line in text.Split('\n').Select(line => line.TrimEnd(' ')))
            {
                if (line.Length == 0 && cleaned.Count > 0 && cleaned[^1].Length == 0)
                    continue;

                cleaned.Add(line);
            }

            return string.Join('\n', cleaned);
        }
    }
}
