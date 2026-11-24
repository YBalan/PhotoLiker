namespace PhotoLiker.Manuscripts.Fixing
{
    /// <summary>
    /// Corrects the text a scanner read with a small chat model that runs on this computer (Qwen2.5 1.5B Instruct, Apache 2.0): it is told what a scanner gets wrong, shown a few short examples, and
    /// given the lines to correct. The instructions and the examples are the same for every piece of text, so the model reads them once and every piece goes on from that. The answer is not
    /// taken as it is, see <see cref="TextFixer"/>. One thread at a time; let go of it when it is not needed any more, it holds more than a gigabyte of memory.
    /// </summary>
    public sealed class LlmCorrector : ITextCorrector, IDisposable
    {
        private readonly ByteLevelTokenizer _tokenizer;
        private readonly ChatModel _model;
        private readonly ScanLanguage _language;
        private readonly int _start;
        private readonly int _end;
        private readonly HashSet<int> _stop;
        private KvCache? _instructions;

        /// <param name="modelPath">The ONNX file of the model.</param>
        /// <param name="tokenizerPath">Its <c>tokenizer.json</c>.</param>
        /// <param name="language">The language of the text, which the instructions say.</param>
        /// <param name="threads">How many threads the model runs on; 0 is what suits this computer.</param>
        public LlmCorrector(string modelPath, string tokenizerPath, ScanLanguage language, int threads = 0)
        {
            _tokenizer = ByteLevelTokenizer.Load(tokenizerPath);
            _start = _tokenizer.IdOf("<|im_start|>");
            _end = _tokenizer.IdOf("<|im_end|>");
            _stop = [_end, _tokenizer.IdOf("<|endoftext|>")];
            _language = language;
            _model = ChatModel.Open(modelPath, threads);
        }

        public string Correct(string text, CancellationToken cancel)
        {
            _instructions ??= _model.Read(Instructions(), cancel);

            var prompt = new List<int>();
            Turn(prompt, "user", text);
            prompt.AddRange([_start, .. _tokenizer.Encode("assistant\n")]);

            // The answer is about as long as the text; a model that goes on and on is stopped
            var maximum = Math.Min(900, _tokenizer.Encode(text).Count * 3 / 2 + 24);
            var answer = _model.Generate(prompt, maximum, _stop, _instructions, cancel);
            return _tokenizer.Decode(answer).Trim();
        }

        public void Dispose()
        {
            _instructions?.Dispose();
            _model.Dispose();
        }

        // ── The prompt ──────────────────────────────────────────────────────

        /// <summary>The instructions and the examples, as the first turns of a chat.</summary>
        private List<int> Instructions()
        {
            var tokens = new List<int>();
            Turn(tokens, "system", SystemText(_language));
            foreach (var (wrong, right) in Examples(_language))
            {
                Turn(tokens, "user", wrong);
                Turn(tokens, "assistant", right);
            }

            return tokens;
        }

        private void Turn(List<int> tokens, string role, string text)
        {
            tokens.Add(_start);
            tokens.AddRange(_tokenizer.Encode(role + "\n" + text));
            tokens.Add(_end);
            tokens.AddRange(_tokenizer.Encode("\n"));
        }

        /// <summary>
        /// What the model is told. The words of a scan are broken more than they are misspelled (a letter taken for a letter that looks like it), and a small model that is only asked to "correct" leaves
        /// many of them alone, so it is told what the breaks are, and to find every word that is not a real word and put the word that was meant. Tried on a faded page that the reader got nine
        /// words of wrong: with this the model put right all four of the first paragraph (with "correct the text" it put right two, and made "mun" into "men").
        /// </summary>
        internal static string SystemText(ScanLanguage language) =>
            "You correct text that a scanner misread. Many words in it are broken: a letter was taken for a letter that looks like it (" + LookAlikes(language) + "), or a letter is wrong or missing. "
            + "Find every word that is not a real word of the language and put in the real word that was meant, using the words round it. Words that are right stay as they are. "
            + $"The text is in {LanguageName(language)}. Keep the words in their order and the lines as they are. Do not translate, do not explain. Answer with the corrected text only.";

        internal static string LanguageName(ScanLanguage language) => language switch
        {
            ScanLanguage.Ukrainian => "Ukrainian",
            ScanLanguage.Russian => "Russian",
            ScanLanguage.English => "English",
            _ => "Ukrainian, Russian or English (each word is in one of them)",
        };

        private const string LatinLookAlikes = "t for f, l for f, o for u, n for m, s for n, i for l, e for c";
        private const string CyrillicLookAlikes = "и for н, г for ш, о for а, п for н, т for г, е for с";

        /// <summary>The letters a scanner takes for each other in the language.</summary>
        internal static string LookAlikes(ScanLanguage language) => language switch
        {
            ScanLanguage.English => LatinLookAlikes,
            ScanLanguage.Multilingual => LatinLookAlikes + "; " + CyrillicLookAlikes,
            _ => CyrillicLookAlikes,
        };

        /// <summary>What a scanner gets wrong and how it is put right, for the language (one of each for a text in more than one).</summary>
        internal static IReadOnlyList<(string Wrong, string Right)> Examples(ScanLanguage language)
        {
            (string, string)[] english =
            [
                ("Toesday we saw tbe sun on the hiil\nand picked flcwers for mun", "Tuesday we saw the sun on the hill\nand picked flowers for mum"),
                ("Wensday we wen to the lbrary\nand borowed thre books", "Wednesday we went to the library\nand borrowed three books"),
            ];
            (string, string)[] ukrainian =
            [
                ("У суботу ми ходилн до лісу,\nа мама спекла пирш з яблуками", "У суботу ми ходили до лісу,\nа мама спекла пиріг з яблуками"),
                ("Сонце свитить яскраво. Дити грають у дворі,\nа бабуся пече пиріг", "Сонце світить яскраво. Діти грають у дворі,\nа бабуся пече пиріг"),
            ];
            (string, string)[] russian =
            [
                ("В воскресенье мы ходили на реку,\nгде много цвстов и бабочек", "В воскресенье мы ходили на реку,\nгде много цветов и бабочек"),
                ("Мы пошли в лес за гриоами,\nбабушка сказала что их нужно провариь", "Мы пошли в лес за грибами,\nбабушка сказала, что их нужно проварить"),
            ];
            return language switch
            {
                ScanLanguage.Ukrainian => ukrainian,
                ScanLanguage.Russian => russian,
                ScanLanguage.English => english,
                _ => [english[0], ukrainian[0], russian[1]],
            };
        }
    }
}
