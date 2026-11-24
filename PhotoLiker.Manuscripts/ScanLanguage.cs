namespace PhotoLiker.Manuscripts
{
    /// <summary>What kind of writing a page is: written by hand, or printed (a typed or a typeset page).</summary>
    public enum ScanKind
    {
        Handwriting,
        Printed,
    }

    /// <summary>
    /// The language of the text of a page. The handwriting networks are Ukrainian and Russian; the Ukrainian one also knows the Latin letters, so it reads English (held to Latin letters) and a page that
    /// has more than one language on it (<see cref="Multilingual"/>: held to nothing). Print is read in any of them.
    /// </summary>
    public enum ScanLanguage
    {
        Ukrainian,
        Russian,
        English,

        /// <summary>Ukrainian, Russian and English on one page (a vocabulary table): the reader is held to no language.</summary>
        Multilingual,
    }

    /// <summary>What the language of a page means for reading it.</summary>
    public static class ScanLanguages
    {
        /// <summary>The languages a kind of writing can be read in: all of them, handwriting as well as print (see <see cref="ScanLanguage"/>).</summary>
        public static IReadOnlyList<ScanLanguage> For(ScanKind kind) => [ScanLanguage.Ukrainian, ScanLanguage.Russian, ScanLanguage.English, ScanLanguage.Multilingual];

        public static bool IsSupported(ScanKind kind, ScanLanguage language) => For(kind).Contains(language);

        /// <summary>The language whose handwriting network reads a language: Russian has its own, the Ukrainian one reads all the others (it knows the Latin letters too).</summary>
        public static ScanLanguage HandwritingModelOf(ScanLanguage language) => language == ScanLanguage.Russian ? ScanLanguage.Russian : ScanLanguage.Ukrainian;

        /// <summary>The code the language of a book is written as in an EPUB: the names this program gives the languages everywhere ("ua" for Ukrainian), and "mul" for a book of more than one language.</summary>
        public static string Code(ScanLanguage language) => language switch
        {
            ScanLanguage.Ukrainian => "ua",
            ScanLanguage.Russian => "ru",
            ScanLanguage.Multilingual => "mul",
            _ => "en",
        };

        /// <summary>
        /// Whether a language lets a reader answer with a character. The model that reads print knows Ukrainian, Russian, Belarusian, Bulgarian and English at once, and a Ukrainian "и" and a Russian "ы"
        /// look alike enough for it to choose the wrong one: a page that is said to be Ukrainian cannot have the letters only Russian (and Belarusian) has, a page that is said to be Russian cannot
        /// have the letters only Ukrainian has, and English text has no Cyrillic at all. Digits, marks and Latin letters are always allowed (a Ukrainian text has its brands and its formulas).
        /// </summary>
        public static bool Allows(ScanLanguage language, string character)
        {
            foreach (var c in character)
            {
                var allowed = language switch
                {
                    ScanLanguage.Ukrainian => !"ыэъёЫЭЪЁўЎ".Contains(c),
                    ScanLanguage.Russian => !"іїєґІЇЄҐўЎ".Contains(c),
                    ScanLanguage.Multilingual => true,
                    _ => c is not (>= 'Ѐ' and <= 'ԯ'),
                };
                if (!allowed)
                    return false;
            }

            return true;
        }

        /// <summary>The Latin letters that look like a Cyrillic letter, and the Cyrillic letter each stands for.</summary>
        private static readonly (char Latin, char Cyrillic)[] Lookalikes =
        [
            ('a', 'а'), ('c', 'с'), ('e', 'е'), ('o', 'о'), ('p', 'р'), ('x', 'х'), ('y', 'у'),
            ('A', 'А'), ('B', 'В'), ('C', 'С'), ('E', 'Е'), ('H', 'Н'), ('K', 'К'), ('M', 'М'), ('O', 'О'), ('P', 'Р'), ('T', 'Т'), ('X', 'Х'),
        ];

        /// <summary>
        /// A text read in Ukrainian or Russian (or in more than one language), with the Latin letters that only look like Cyrillic ones put right: a word that has a Cyrillic letter in it is Cyrillic
        /// through (a Latin "o" or "a" among Cyrillic letters is a misreading, whatever the reader thinks; so is the Latin "i" in a Ukrainian word), and a lone Latin "i" between words of a Ukrainian text is
        /// the Ukrainian "і" (in a text of more than one language it may be the English "I"). Other text, and a word that is Latin through, is left alone.
        /// </summary>
        public static string Normalize(ScanLanguage language, string text)
        {
            if (language == ScanLanguage.English || string.IsNullOrEmpty(text))
                return text;

            var words = text.Split(' ');
            for (var i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (word.Any(IsCyrillic))
                    words[i] = new string([.. word.Select(c => Swap(c, language))]);
                else if (language == ScanLanguage.Ukrainian && words.Length > 1 && word is "i" or "I")
                    words[i] = word == "i" ? "і" : "І";
            }

            return string.Join(' ', words);
        }

        private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

        private static char Swap(char c, ScanLanguage language)
        {
            foreach (var (latin, cyrillic) in Lookalikes)
            {
                if (c == latin)
                    return cyrillic;
            }

            // The Ukrainian "і" is a Latin "i" with a dot as far as a reader can tell; the Russian text has no such letter, so there it stays as it is
            return language is ScanLanguage.Ukrainian or ScanLanguage.Multilingual ? c switch { 'i' => 'і', 'I' => 'І', _ => c } : c;
        }
    }
}
