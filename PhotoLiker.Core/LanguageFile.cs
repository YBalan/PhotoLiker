using System.Text;

namespace PhotoLiker.Core
{
    /// <summary>
    /// The text format of a language: <c>photo-liker-en.txt</c>, <c>photo-liker-ua.txt</c>. A line is <c>Name = text</c>; empty lines and lines that
    /// begin with # are ignored. A name without a dot (Name, Culture, Plural) describes the language; a name with one (<c>AboutStrings.Heading</c>)
    /// is a text, named by the class that offers it and its member. In a text <c>\n</c> is a new line, <c>\t</c> a tab, <c>\\</c> a backslash and
    /// <c>\s</c> a space; the spaces at the start and the end of a text are cut off, so a space meant to be there is written <c>\s</c>. Any other
    /// backslash stays as it is (<c>Deleted\Duplicated</c>).
    /// </summary>
    public static class LanguageFile
    {
        /// <summary>The prefix and the suffix of the name of a language file: <c>photo-liker-ua.txt</c> is the file of the language "ua".</summary>
        public const string Prefix = "photo-liker-";
        public const string Extension = ".txt";

        public static string FileNameOf(string code) => Prefix + code + Extension;

        /// <summary>The code in the name of a language file ("ua" for <c>photo-liker-ua.txt</c>), or null if the name is not one.</summary>
        public static string? CodeOf(string path)
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                return null;

            var code = name[Prefix.Length..^Extension.Length].Trim().ToLowerInvariant();
            return code.Length > 0 && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? code : null;
        }

        /// <summary>What a file says about the language, and its texts by name; <paramref name="problems"/> hears of the lines that could not be read.</summary>
        public static (Dictionary<string, string> Header, Dictionary<string, string> Texts) Parse(string content, List<string>? problems = null)
        {
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);

            var number = 0;
            foreach (var raw in content.Split('\n'))
            {
                number++;
                var line = raw.Trim('\r', '\uFEFF', ' ', '\t');
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    problems?.Add($"line {number}: no \"Name = text\": {Shorten(line)}");
                    continue;
                }

                var name = line[..equals].Trim();
                var value = Unescape(line[(equals + 1)..].Trim(' ', '\t'));
                var target = name.Contains('.') ? texts : header;
                if (!target.TryAdd(name, value))
                {
                    problems?.Add($"line {number}: {name} is there twice, the last one counts");
                    target[name] = value;
                }
            }

            return (header, texts);
        }

        /// <summary>Reads a file; null if it can not be read.</summary>
        public static string? ReadAll(string path)
        {
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The text of a line as the program uses it: the escapes made into what they stand for.</summary>
        public static string Unescape(string value)
        {
            if (!value.Contains('\\'))
                return value;

            var result = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    result.Append(value[i]);
                    continue;
                }

                switch (value[i + 1])
                {
                    case 'n': result.Append('\n'); i++; break;
                    case 't': result.Append('\t'); i++; break;
                    case 's': result.Append(' '); i++; break;
                    case '\\': result.Append('\\'); i++; break;
                    default: result.Append('\\'); break; // a backslash of a path
                }
            }

            return result.ToString();
        }

        /// <summary>The line form of a text, as <see cref="Unescape"/> reads it back (what a tool that writes a language file needs).</summary>
        public static string Escape(string text)
        {
            var escaped = text.Replace("\\", "\\\\").Replace("\r", string.Empty).Replace("\n", "\\n").Replace("\t", "\\t");
            var start = escaped.Length - escaped.TrimStart(' ').Length;
            var end = escaped.Length - escaped.TrimEnd(' ').Length;
            if (start == escaped.Length)
                return string.Concat(Enumerable.Repeat("\\s", start));

            return string.Concat(Enumerable.Repeat("\\s", start)) + escaped.Trim(' ') + string.Concat(Enumerable.Repeat("\\s", end));
        }

        private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "...";
    }
}
