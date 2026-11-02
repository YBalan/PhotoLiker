using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoLiker.Core
{
    /// <summary>How the noun after a number takes its form (see <see cref="Plural"/>).</summary>
    public enum PluralRule
    {
        /// <summary>Two forms: 1, and the rest. English, German, ...</summary>
        OneOther,

        /// <summary>Three forms: 1, 21, 31 ...; 2-4, 22-24 ...; and the rest (also 11-14). Ukrainian, Russian, ...</summary>
        EastSlavic,
    }

    /// <summary>
    /// A language of the program. English is built in (its texts are the ones the program was written with, see <see cref="EnglishTexts"/>);
    /// any other language is a text file, <c>photo-liker-ua.txt</c> (see <see cref="LanguageFile"/>), found in the <c>Languages</c> folder next to the
    /// program or in the one under <c>%LocalAppData%\PhotoLiker</c>. A file for English is not needed, but there may be one: what it has replaces the
    /// built-in text. A text a language does not have, or has with other {0} {1} ... than English, is shown in English.
    /// </summary>
    public sealed class AppLanguage
    {
        /// <summary>The variable that names one more folder to look in (the first one looked in: the tests point it at the files of the repository).</summary>
        public const string FolderVariable = "PHOTOLIKER_LANGUAGES_DIR";

        public const string EnglishCode = "en";

        private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);

        private readonly Dictionary<string, string> _texts;
        private readonly ConcurrentDictionary<string, string> _resolved = new(StringComparer.Ordinal);

        private AppLanguage(string code, string nativeName, CultureInfo culture, PluralRule plural, Dictionary<string, string> texts, IReadOnlyList<string> files, IReadOnlyList<string> problems)
        {
            Code = code;
            NativeName = nativeName;
            Culture = culture;
            Plural = plural;
            _texts = texts;
            Files = files;
            FileProblems = problems;
        }

        /// <summary>The code of the language: the "ua" of <c>photo-liker-ua.txt</c>; "en" for English.</summary>
        public string Code { get; }

        /// <summary>The language in itself, as the settings grid lists it ("English", "Українська").</summary>
        public string NativeName { get; }

        /// <summary>The culture of the language: the names of months, and the language of what .NET says. The invariant culture if the file names none.</summary>
        public CultureInfo Culture { get; }

        public PluralRule Plural { get; }

        /// <summary>The files this language was read from (none for English without a file of its own).</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>Lines of the files that could not be read.</summary>
        public IReadOnlyList<string> FileProblems { get; }

        public override string ToString() => NativeName;

        // ── texts ──────────────────────────────────────────────────────────────

        /// <summary>The text of a name ("AboutStrings.Heading") in this language: English for what the language has not, or has wrong; the name in brackets if there is no such text.</summary>
        public string Resolve(string name) => _resolved.GetOrAdd(name, Compute);

        private string Compute(string name)
        {
            var english = EnglishTexts.TryGetValue(name, out var builtIn) ? builtIn : null;
            if (_texts.TryGetValue(name, out var own) && (english is null || SamePlaceholders(english, own)))
                return own;

            return english ?? $"[{name}]";
        }

        private static bool SamePlaceholders(string english, string other) =>
            Placeholder.Matches(english).Select(m => m.Value).Order(StringComparer.Ordinal)
                .SequenceEqual(Placeholder.Matches(other).Select(m => m.Value).Order(StringComparer.Ordinal));

        /// <summary>What is wrong with the language, for the person who translates it: texts it has not, texts English has not, texts with other placeholders.</summary>
        public LanguageReport Check()
        {
            var missing = EnglishTexts.Keys.Where(name => !_texts.ContainsKey(name)).Order(StringComparer.Ordinal).ToList();
            var unknown = _texts.Keys.Where(name => !EnglishTexts.ContainsKey(name)).Order(StringComparer.Ordinal).ToList();
            var wrong = _texts.Where(pair => EnglishTexts.TryGetValue(pair.Key, out var english) && !SamePlaceholders(english, pair.Value))
                .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
            return new LanguageReport(missing, unknown, wrong, FileProblems);
        }

        // ── English, built in ─────────────────────────────────────────────────

        /// <summary>
        /// The texts of the program in English: <c>photo-liker-en.txt</c> of the repository, which is built into this assembly, so that the program
        /// has words without any file. Every other language is measured against it.
        /// </summary>
        public static IReadOnlyDictionary<string, string> EnglishTexts => EnglishBuiltIn.Value;

        private static readonly Lazy<Dictionary<string, string>> EnglishBuiltIn = new(() =>
        {
            using var stream = typeof(AppLanguage).Assembly.GetManifestResourceStream(LanguageFile.FileNameOf(EnglishCode));
            if (stream is null)
                return [];

            using var reader = new StreamReader(stream);
            return LanguageFile.Parse(reader.ReadToEnd()).Texts;
        });

        // ── the languages that are there ──────────────────────────────────────

        private static readonly object Gate = new();
        private static readonly Dictionary<string, (string Signature, AppLanguage Language)> Known = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Where language files are looked for, the first one weakest: the folder of <see cref="FolderVariable"/> (if set), the <c>Languages</c> folder
        /// next to the program, then the one in the app data folder, so that what a person puts there wins and is kept when a new version is installed.
        /// </summary>
        public static IReadOnlyList<string> Folders()
        {
            var folders = new List<string>();
            if (Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } custom)
                folders.Add(custom);

            folders.Add(Path.Combine(AppContext.BaseDirectory, "Languages"));
            folders.Add(Path.Combine(AppPaths.DataFolder, "Languages"));
            return folders;
        }

        /// <summary>English, and every other language that has a file: the list of the Language row. Looks at the files each time, so a file that was added is there.</summary>
        public static IReadOnlyList<AppLanguage> Available()
        {
            lock (Gate)
            {
                var files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in Folders())
                {
                    if (!Directory.Exists(folder))
                        continue;

                    IEnumerable<string> found;
                    try
                    {
                        found = Directory.EnumerateFiles(folder, LanguageFile.Prefix + "*" + LanguageFile.Extension).ToList();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var file in found)
                    {
                        if (LanguageFile.CodeOf(file) is { } code)
                        {
                            if (!files.TryGetValue(code, out var list))
                                files[code] = list = [];
                            list.Add(file);
                        }
                    }
                }

                files.TryAdd(EnglishCode, []);

                var languages = new List<AppLanguage>();
                foreach (var (code, list) in files.OrderBy(pair => pair.Key == EnglishCode ? 0 : 1).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var signature = string.Join('|', list.Select(Signature));
                    if (!Known.TryGetValue(code, out var known) || known.Signature != signature)
                    {
                        known = (signature, Read(code, list));
                        Known[code] = known;
                    }

                    languages.Add(known.Language);
                }

                // A file that was taken away is not a language any more
                foreach (var gone in Known.Keys.Where(code => !files.ContainsKey(code)).ToList())
                    Known.Remove(gone);

                return languages;
            }
        }

        /// <summary>The language of a code (case does not matter); English for an empty one or one that has no file.</summary>
        public static AppLanguage Find(string? code)
        {
            var languages = Available();
            return languages.FirstOrDefault(language => string.Equals(language.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? languages[0];
        }

        /// <summary>English: the built-in texts, with what a file <c>photo-liker-en.txt</c> says on top, if there is one.</summary>
        public static AppLanguage English => Find(EnglishCode);

        private static string Signature(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return path;
            }
        }

        private static AppLanguage Read(string code, List<string> files)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();
            var readFiles = new List<string>();

            foreach (var file in files)
            {
                var content = LanguageFile.ReadAll(file);
                if (content is null)
                {
                    problems.Add($"{Path.GetFileName(file)} could not be read");
                    continue;
                }

                var fileProblems = new List<string>();
                var (fileHeader, fileTexts) = LanguageFile.Parse(content, fileProblems);
                problems.AddRange(fileProblems.Select(problem => $"{Path.GetFileName(file)}, {problem}"));
                readFiles.Add(file);

                foreach (var (key, value) in fileHeader)
                    header[key] = value;
                foreach (var (key, value) in fileTexts)
                    texts[key] = value;
            }

            var isEnglish = string.Equals(code, EnglishCode, StringComparison.OrdinalIgnoreCase);
            var nativeName = header.TryGetValue("Name", out var name) && name.Length > 0 ? name : isEnglish ? "English" : code.ToUpperInvariant();
            var culture = CultureOf(header.GetValueOrDefault("Culture"), isEnglish ? "en" : null);
            var plural = header.GetValueOrDefault("Plural")?.Trim().ToLowerInvariant() switch
            {
                "east-slavic" => PluralRule.EastSlavic,
                "one-other" => PluralRule.OneOther,
                _ => PluralRule.OneOther,
            };

            return new AppLanguage(code.ToLowerInvariant(), nativeName, culture, plural, texts, readFiles, problems);
        }

        private static CultureInfo CultureOf(string? name, string? fallback)
        {
            foreach (var candidate in new[] { name, fallback })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                try
                {
                    return CultureInfo.GetCultureInfo(candidate.Trim());
                }
                catch (CultureNotFoundException)
                {
                    // a culture this system does not know: the next one
                }
            }

            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>What <see cref="AppLanguage.Check"/> found.</summary>
    public sealed record LanguageReport(IReadOnlyList<string> Missing, IReadOnlyList<string> Unknown, IReadOnlyList<string> WrongPlaceholders, IReadOnlyList<string> FileProblems)
    {
        public bool IsClean => Missing.Count == 0 && Unknown.Count == 0 && WrongPlaceholders.Count == 0 && FileProblems.Count == 0;

        public int Count => Missing.Count + Unknown.Count + WrongPlaceholders.Count + FileProblems.Count;
    }
}
