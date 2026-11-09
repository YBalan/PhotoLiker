namespace PhotoLiker.Video
{
    /// <summary>A language the speech model can hear: the code it is asked with and its name in English.</summary>
    public sealed record SpeechLanguage(string Code, string Name)
    {
        public override string ToString() => $"{Code} - {Name}";
    }

    /// <summary>
    /// The languages Whisper knows (its 99 codes), for choosing the language a video is heard in. The ones this program's users most likely need come first, then the rest by name.
    /// "auto" is not one of them: it is the model's own guess, and the choice of it is made by the caller.
    /// </summary>
    public static class SpeechLanguages
    {
        /// <summary>The code that leaves the language to the model.</summary>
        public const string Auto = "auto";

        private static readonly string[] Likely = ["ru", "uk", "en"];

        private static readonly (string Code, string Name)[] Known =
        [
            ("af", "Afrikaans"), ("sq", "Albanian"), ("am", "Amharic"), ("ar", "Arabic"), ("hy", "Armenian"), ("as", "Assamese"), ("az", "Azerbaijani"), ("ba", "Bashkir"),
            ("eu", "Basque"), ("be", "Belarusian"), ("bn", "Bengali"), ("bs", "Bosnian"), ("br", "Breton"), ("bg", "Bulgarian"), ("my", "Burmese"), ("ca", "Catalan"),
            ("zh", "Chinese"), ("hr", "Croatian"), ("cs", "Czech"), ("da", "Danish"), ("nl", "Dutch"), ("en", "English"), ("et", "Estonian"), ("fo", "Faroese"),
            ("fi", "Finnish"), ("fr", "French"), ("gl", "Galician"), ("ka", "Georgian"), ("de", "German"), ("el", "Greek"), ("gu", "Gujarati"), ("ht", "Haitian Creole"),
            ("ha", "Hausa"), ("haw", "Hawaiian"), ("he", "Hebrew"), ("hi", "Hindi"), ("hu", "Hungarian"), ("is", "Icelandic"), ("id", "Indonesian"), ("it", "Italian"),
            ("ja", "Japanese"), ("jw", "Javanese"), ("kn", "Kannada"), ("kk", "Kazakh"), ("km", "Khmer"), ("ko", "Korean"), ("lo", "Lao"), ("la", "Latin"),
            ("lv", "Latvian"), ("ln", "Lingala"), ("lt", "Lithuanian"), ("lb", "Luxembourgish"), ("mk", "Macedonian"), ("mg", "Malagasy"), ("ms", "Malay"), ("ml", "Malayalam"),
            ("mt", "Maltese"), ("mi", "Maori"), ("mr", "Marathi"), ("mn", "Mongolian"), ("ne", "Nepali"), ("no", "Norwegian"), ("nn", "Nynorsk"), ("oc", "Occitan"),
            ("ps", "Pashto"), ("fa", "Persian"), ("pl", "Polish"), ("pt", "Portuguese"), ("pa", "Punjabi"), ("ro", "Romanian"), ("ru", "Russian"), ("sa", "Sanskrit"),
            ("sr", "Serbian"), ("sn", "Shona"), ("sd", "Sindhi"), ("si", "Sinhala"), ("sk", "Slovak"), ("sl", "Slovenian"), ("so", "Somali"), ("es", "Spanish"),
            ("su", "Sundanese"), ("sw", "Swahili"), ("sv", "Swedish"), ("tl", "Tagalog"), ("tg", "Tajik"), ("ta", "Tamil"), ("tt", "Tatar"), ("te", "Telugu"),
            ("th", "Thai"), ("bo", "Tibetan"), ("tr", "Turkish"), ("tk", "Turkmen"), ("uk", "Ukrainian"), ("ur", "Urdu"), ("uz", "Uzbek"), ("vi", "Vietnamese"),
            ("cy", "Welsh"), ("yi", "Yiddish"), ("yo", "Yoruba"),
        ];

        /// <summary>Every language, the likely ones first.</summary>
        public static IReadOnlyList<SpeechLanguage> All { get; } =
        [
            .. Likely.Select(code => Find(code)!),
            .. Known.Where(k => !Likely.Contains(k.Code)).Select(k => new SpeechLanguage(k.Code, k.Name)),
        ];

        /// <summary>The language with this code ("ru", any case), null if the model does not know it. "auto" is not a language.</summary>
        public static SpeechLanguage? Find(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var wanted = code.Trim();
            foreach (var (c, name) in Known)
            {
                if (string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase))
                    return new SpeechLanguage(c, name);
            }
            return null;
        }

        /// <summary>A language as a person reads it: "Russian (ru)"; the code alone for one that is not known, empty for none.</summary>
        public static string Describe(string? code) =>
            string.IsNullOrWhiteSpace(code) ? string.Empty : Find(code) is { } language ? $"{language.Name} ({language.Code})" : code.Trim();
    }
}
