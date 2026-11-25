using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>
    /// Corrects the text a scanner read with an AI service over the Internet (see <see cref="CloudChat"/>): it is told what a scanner gets wrong and shown the same few examples as the language model on
    /// this computer is (<see cref="LlmCorrector"/>), then given the lines to correct. What comes back is not taken as it is: <see cref="TextFixer"/> takes it word by word, so a service that
    /// explains itself, rewrites a sentence or drops a line changes nothing it should not. It sends the text and nothing else (not the photo). Runs on a thread of its own: it waits for the answer.
    /// </summary>
    public sealed class CloudCorrector : ITextCorrector
    {
        private readonly CloudChat _chat;
        private readonly string _system;
        private readonly List<ChatTurn> _examples = [];

        public CloudCorrector(CloudChat chat, ScanLanguage language)
        {
            _chat = chat;
            _system = LlmCorrector.SystemText(language);
            foreach (var (wrong, right) in LlmCorrector.Examples(language))
            {
                _examples.Add(new ChatTurn(false, wrong));
                _examples.Add(new ChatTurn(true, right));
            }
        }

        /// <exception cref="AiServiceException">The service did not give a text.</exception>
        public string Correct(string text, CancellationToken cancel)
        {
            List<ChatTurn> turns = [.. _examples, new ChatTurn(false, text)];

            // Called from a thread of its own, which has nothing to keep free: waiting for the answer here is what is wanted
            var answer = _chat.CompleteAsync(_system, turns, cancel).GetAwaiter().GetResult();
            return WithoutFence(answer.Trim());
        }

        /// <summary>A text a service wrapped in a code block (three backticks, a line each) as it does when it is asked for "only the text"; the text itself otherwise.</summary>
        internal static string WithoutFence(string answer)
        {
            const string Fence = "```";
            if (!answer.StartsWith(Fence, StringComparison.Ordinal) || !answer.EndsWith(Fence, StringComparison.Ordinal) || answer.Length < 2 * Fence.Length)
                return answer;

            var inside = answer[Fence.Length..^Fence.Length];
            var firstLine = inside.IndexOf('\n');

            // What follows the opening backticks on their line is the name of a language ("text", "plaintext"), not a part of the answer
            if (firstLine >= 0 && inside[..firstLine].Trim().All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '+'))
                inside = inside[(firstLine + 1)..];

            return inside.Trim();
        }
    }
}
