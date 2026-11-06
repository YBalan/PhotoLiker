using System.Text.RegularExpressions;
using Whisper.net;

namespace PhotoLiker.Video
{
    /// <summary>A stretch of speech in a video: when it starts and ends (milliseconds from the start) and what is said.</summary>
    public sealed record SpeechSegment(long StartMs, long EndMs, string Text);

    /// <summary>What was heard in a video: the language (as the recognizer says it, "ru"), and the segments in order of time.</summary>
    public sealed record SpeechTranscript(string Language, IReadOnlyList<SpeechSegment> Segments);

    /// <summary>Something that turns the sound of a file into text. Whisper is the one there is; a test gives its own.</summary>
    public interface ISpeechRecognizer : IDisposable
    {
        /// <param name="wavPath">A 16 kHz mono WAV file.</param>
        /// <param name="language">"auto" to let the model hear which language it is, else its code ("ru", "uk", "en").</param>
        Task<SpeechTranscript> TranscribeAsync(string wavPath, string language, CancellationToken cancellationToken);
    }

    /// <summary>Whisper (whisper.cpp through Whisper.net) on the processor, with the segments that are no speech taken out.</summary>
    public sealed class WhisperRecognizer : ISpeechRecognizer
    {
        /// <summary>A segment the model itself thinks is likely no speech (music, noise, silence) is dropped above this.</summary>
        public const float NoSpeechCutoff = 0.6f;

        private readonly WhisperFactory _factory;
        private readonly int _threads;

        /// <param name="threads">How many threads the model uses; it takes them all and the rest of the computer has what is left, so the default is half of them.</param>
        public WhisperRecognizer(string modelPath, int threads = 0)
        {
            _factory = WhisperFactory.FromPath(modelPath);
            _threads = threads > 0 ? threads : Math.Max(2, Environment.ProcessorCount / 2);
        }

        public async Task<SpeechTranscript> TranscribeAsync(string wavPath, string language, CancellationToken cancellationToken)
        {
            var builder = _factory.CreateBuilder().WithThreads(_threads).WithNoSpeechThreshold(NoSpeechCutoff);
            builder = string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase) ? builder.WithLanguageDetection() : builder.WithLanguage(language);
            // Not "using": Stop cancels while the model is still working, and the processor refuses to be disposed then (it throws "Cannot dispose while processing"
            // and that was what the user saw instead of "stopped"); DisposeAsync waits until the model has let go
            await using var processor = builder.Build();

            await using var stream = File.OpenRead(wavPath);
            var heard = "";
            var segments = new List<SpeechSegment>();
            await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
            {
                heard = segment.Language;
                if (segment.NoSpeechProbability >= NoSpeechCutoff || SpeechText.Clean(segment.Text) is not { Length: > 0 } text)
                    continue;

                segments.Add(new SpeechSegment((long)segment.Start.TotalMilliseconds, (long)segment.End.TotalMilliseconds, text));
            }
            return new SpeechTranscript(heard, segments);
        }

        public void Dispose() => _factory.Dispose();
    }

    /// <summary>The text Whisper writes, made fit to keep and to search.</summary>
    public static partial class SpeechText
    {
        // What the model writes for music or noise, in brackets: [музыка], (music), [Music], ♪ ... ♪
        [GeneratedRegex(@"^[\[\(\{].*[\]\)\}]$", RegexOptions.CultureInvariant)]
        private static partial Regex Bracketed();

        // What the model writes where there is nothing: the credits it learned from subtitles, not anything said
        private static readonly Regex Hallucination = new(
            @"субтитры\s+(сделал|создавал|делал|подогнал|добавил)|продолжение следует|спасибо за (просмотр|внимание)|thanks for watching|thank you for watching|subtitles by|подписывайтесь на канал|редактор субтитров|корректор",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The text of a segment as it is kept: the music notes and the spaces around it taken off; an empty string for what is no speech (a note in brackets
        /// such as [музыка], the credits of a film that the model writes for silence).
        /// </summary>
        public static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var cleaned = text.Replace('♪', ' ').Replace('♫', ' ').Trim();
            while (cleaned.Contains("  ", StringComparison.Ordinal))
                cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);

            return cleaned.Length == 0 || Bracketed().IsMatch(cleaned) || Hallucination.IsMatch(cleaned) ? string.Empty : cleaned;
        }

        /// <summary>
        /// A text as words to compare: lower case, ё and е the same, no punctuation, apostrophes kept inside a word (Ukrainian: м'яч). The words of a search and the
        /// words of what was said are made the same way.
        /// </summary>
        public static IReadOnlyList<string> Words(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            var words = new List<string>();
            var word = new System.Text.StringBuilder();
            foreach (var c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || (c is '\'' or '’' or 'ʼ' && word.Length > 0))
                    word.Append(c == 'ё' ? 'е' : c is '’' or 'ʼ' ? '\'' : c);
                else if (word.Length > 0)
                    Flush();
            }
            Flush();
            return words;

            void Flush()
            {
                var finished = word.ToString().Trim('\'');
                if (finished.Length > 0)
                    words.Add(finished);
                word.Clear();
            }
        }

        /// <summary>
        /// Whether a word that is said is the word that is searched for, in whatever form: the same word, or one that starts with the same letters and differs only in its
        /// ending (лошадка, лошадку, лошадке; плавай, плавает; baby, babies): the shared start is at least three letters and all but the last two of the shorter word, and the words are not more than three letters apart in length.
        /// Too short a word has to be the same.
        /// </summary>
        public static bool Matches(string searched, string said)
        {
            if (searched == said)
                return true;

            var shorter = Math.Min(searched.Length, said.Length);
            if (shorter < 3 || Math.Abs(searched.Length - said.Length) > 3)
                return false;

            var common = 0;
            while (common < shorter && searched[common] == said[common])
                common++;
            return common >= Math.Max(3, shorter - 2);
        }
    }
}
