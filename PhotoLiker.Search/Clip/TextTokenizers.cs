using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace PhotoLiker.Search
{
    /// <summary>Turns a text into the ids the text half of a model takes (with the start and end marks, and the padding the model needs).</summary>
    public interface ITextTokenizer
    {
        long[] Encode(string text);
    }

    /// <summary>
    /// SigLIP 2's tokenizer: the Gemma SentencePiece vocabulary, on text that is cleaned the way OpenCLIP trained it (lower case, the ASCII
    /// punctuation removed, one space between words). Always 64 ids: the text, an end mark (1), then padding (0), because SigLIP reads
    /// the vector from the last position.
    /// </summary>
    public sealed class SigLipTokenizer : ITextTokenizer
    {
        public const int ContextLength = 64;
        public const int EndOfText = 1;
        public const int Padding = 0;

        private readonly SentencePieceTokenizer _pieces;

        public SigLipTokenizer(string modelPath)
        {
            using var stream = File.OpenRead(modelPath);
            _pieces = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);
        }

        public long[] Encode(string text)
        {
            var ids = _pieces.EncodeToIds(Clean(text), considerPreTokenization: false, considerNormalization: true);

            var result = new long[ContextLength];
            var count = Math.Min(ids.Count, ContextLength - 1);
            for (var i = 0; i < count; i++)
                result[i] = ids[i];
            result[count] = EndOfText;
            return result; // the rest is 0, the padding
        }

        /// <summary>OpenCLIP's "canonicalize": underscores are spaces, ASCII punctuation goes, lower case, single spaces.</summary>
        internal static string Clean(string text)
        {
            var kept = new string(text.Replace('_', ' ').Where(c => !IsAsciiPunctuation(c)).ToArray());
            return Regex.Replace(kept.ToLowerInvariant(), @"\s+", " ").Trim();
        }

        /// <summary>The 32 characters of Python's string.punctuation.</summary>
        private static bool IsAsciiPunctuation(char c) => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';
    }

    /// <summary>
    /// XLM-RoBERTa's tokenizer (SentencePiece, about a hundred languages, case kept): a start mark (0), the text, an end mark (2), padded
    /// with 1 to 77 ids. XLM-R's ids are SentencePiece's plus one (the start mark, padding, end mark and unknown take the first four).
    /// </summary>
    public sealed class XlmRobertaTokenizer : ITextTokenizer
    {
        public const int ContextLength = 77;
        public const int StartOfText = 0;
        public const int Padding = 1;
        public const int EndOfText = 2;
        public const int Unknown = 3;

        private readonly SentencePieceTokenizer _pieces;

        public XlmRobertaTokenizer(string modelPath)
        {
            using var stream = File.OpenRead(modelPath);
            _pieces = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);
        }

        public long[] Encode(string text)
        {
            var ids = _pieces.EncodeToIds(text, considerPreTokenization: false, considerNormalization: true);

            var result = new long[ContextLength];
            Array.Fill(result, Padding);
            result[0] = StartOfText;
            var count = Math.Min(ids.Count, ContextLength - 2);
            for (var i = 0; i < count; i++)
                result[i + 1] = ids[i] == 0 ? Unknown : ids[i] + 1;
            result[count + 1] = EndOfText;
            return result;
        }
    }
}
