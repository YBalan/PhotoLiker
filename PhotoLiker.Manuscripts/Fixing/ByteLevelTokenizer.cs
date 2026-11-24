using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoLiker.Manuscripts.Fixing
{
    /// <summary>
    /// The tokenizer of the Qwen language models (the one of GPT-2 with its own pattern for the pieces of a text): a text is cut into pieces by a pattern, each piece is written as the bytes of
    /// its UTF-8 (every byte as one printable character, so that nothing is unknown), and the pieces are put together again by a list of merges, the earliest merge first. It is read from the
    /// <c>tokenizer.json</c> of the model, and gives the same numbers as the tokenizers library of Hugging Face. A text that is encoded is plain text: what looks like a special token in it is
    /// only text (the program that builds a prompt puts the special tokens in by their numbers), so that nothing a page says can speak as the program.
    /// </summary>
    public sealed class ByteLevelTokenizer
    {
        /// <summary>The pattern of the pieces of Qwen2, taken when the file has none.</summary>
        private const string QwenPattern = @"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+";

        private static readonly char[] ByteToChar = BuildByteToChar();
        private static readonly Dictionary<char, byte> CharToByte = ByteToChar.Select((character, value) => (character, value)).ToDictionary(pair => pair.character, pair => (byte)pair.value);

        private readonly Dictionary<string, int> _ids;
        private readonly Dictionary<int, string> _tokens;
        private readonly Dictionary<(string, string), int> _ranks;
        private readonly Dictionary<string, int> _added;
        private readonly HashSet<int> _special;
        private readonly Regex _pieces;
        private readonly Dictionary<string, int[]> _cache = [];

        private ByteLevelTokenizer(Dictionary<string, int> ids, List<(string, string)> merges, Dictionary<string, int> added, HashSet<int> special, string pattern)
        {
            _ids = ids;
            _tokens = ids.ToDictionary(pair => pair.Value, pair => pair.Key);
            foreach (var (content, id) in added)
                _tokens[id] = content;

            _ranks = new Dictionary<(string, string), int>(merges.Count);
            for (var rank = 0; rank < merges.Count; rank++)
                _ranks.TryAdd(merges[rank], rank);

            _added = added;
            _special = special;
            _pieces = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        /// <summary>Reads the tokenizer of a model from its <c>tokenizer.json</c>.</summary>
        /// <exception cref="InvalidDataException">The file is not the tokenizer of a byte-level model this program knows.</exception>
        public static ByteLevelTokenizer Load(string path)
        {
            using var stream = File.OpenRead(path);
            return Parse(stream);
        }

        /// <summary>Reads the tokenizer of a model from the text of its <c>tokenizer.json</c>.</summary>
        public static ByteLevelTokenizer Parse(Stream json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var model = root.GetProperty("model");
                if (model.GetProperty("type").GetString() != "BPE")
                    throw new InvalidDataException("The tokenizer is not a BPE tokenizer.");

                var ids = new Dictionary<string, int>();
                foreach (var entry in model.GetProperty("vocab").EnumerateObject())
                    ids[entry.Name] = entry.Value.GetInt32();

                var merges = new List<(string, string)>();
                foreach (var merge in model.GetProperty("merges").EnumerateArray())
                {
                    // "a b" in the older files, ["a", "b"] in the newer ones
                    if (merge.ValueKind == JsonValueKind.Array)
                    {
                        merges.Add((merge[0].GetString()!, merge[1].GetString()!));
                    }
                    else
                    {
                        var text = merge.GetString()!;
                        var space = text.IndexOf(' ', 1);
                        merges.Add((text[..space], text[(space + 1)..]));
                    }
                }

                var added = new Dictionary<string, int>();
                var special = new HashSet<int>();
                if (root.TryGetProperty("added_tokens", out var addedTokens))
                {
                    foreach (var token in addedTokens.EnumerateArray())
                    {
                        var id = token.GetProperty("id").GetInt32();
                        added[token.GetProperty("content").GetString()!] = id;
                        if (token.TryGetProperty("special", out var isSpecial) && isSpecial.GetBoolean())
                            special.Add(id);
                    }
                }

                return new ByteLevelTokenizer(ids, merges, added, special, PatternOf(root) ?? QwenPattern);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
            {
                throw new InvalidDataException("The file is not the tokenizer of a language model this program knows.", ex);
            }
        }

        /// <summary>The number of a token that was added to the vocabulary (a special one such as <c>&lt;|im_end|&gt;</c>).</summary>
        /// <exception cref="InvalidDataException">The tokenizer has no such token.</exception>
        public int IdOf(string addedToken) =>
            _added.TryGetValue(addedToken, out var id) ? id : throw new InvalidDataException($"The tokenizer has no token {addedToken}.");

        /// <summary>Whether the tokenizer has an added token of this text.</summary>
        public bool Has(string addedToken) => _added.ContainsKey(addedToken);

        /// <summary>The numbers of the tokens of a plain text.</summary>
        public IReadOnlyList<int> Encode(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(text))
                return result;

            foreach (Match piece in _pieces.Matches(text.Normalize(NormalizationForm.FormC)))
                result.AddRange(Merge(Written(piece.Value)));

            return result;
        }

        /// <summary>The text of tokens; the special ones (the marks of a chat) are left out.</summary>
        public string Decode(IEnumerable<int> tokens)
        {
            var bytes = new List<byte>();
            foreach (var id in tokens)
            {
                if (_special.Contains(id) || !_tokens.TryGetValue(id, out var token))
                    continue;

                if (_added.ContainsKey(token))
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(token));
                    continue;
                }

                foreach (var character in token)
                    bytes.Add(CharToByte.TryGetValue(character, out var value) ? value : (byte)'?');
            }

            return Encoding.UTF8.GetString([.. bytes]);
        }

        // ── Merging ─────────────────────────────────────────────────────────

        /// <summary>The text of a piece as one character for each byte of its UTF-8.</summary>
        private static string Written(string piece)
        {
            var bytes = Encoding.UTF8.GetBytes(piece);
            return string.Create(bytes.Length, bytes, static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                    span[i] = ByteToChar[source[i]];
            });
        }

        /// <summary>The tokens of a written piece: its characters are merged by the pair that comes first in the list of merges, over and over, until no pair is in it.</summary>
        private int[] Merge(string word)
        {
            if (_cache.TryGetValue(word, out var known))
                return known;

            var parts = word.Select(character => character.ToString()).ToList();
            while (parts.Count > 1)
            {
                var best = int.MaxValue;
                var pair = default((string, string));
                for (var i = 0; i + 1 < parts.Count; i++)
                {
                    if (_ranks.TryGetValue((parts[i], parts[i + 1]), out var rank) && rank < best)
                    {
                        best = rank;
                        pair = (parts[i], parts[i + 1]);
                    }
                }

                if (best == int.MaxValue)
                    break;

                // Every place the pair is at, from the left, without overlapping
                var merged = new List<string>(parts.Count);
                for (var i = 0; i < parts.Count; i++)
                {
                    if (i + 1 < parts.Count && parts[i] == pair.Item1 && parts[i + 1] == pair.Item2)
                    {
                        merged.Add(pair.Item1 + pair.Item2);
                        i++;
                    }
                    else
                    {
                        merged.Add(parts[i]);
                    }
                }

                parts = merged;
            }

            var tokens = parts.Select(part => _ids.TryGetValue(part, out var id) ? id : throw new InvalidDataException("The tokenizer does not know a part of the text.")).ToArray();
            if (_cache.Count < 20000)
                _cache[word] = tokens;

            return tokens;
        }

        // ── The tables ──────────────────────────────────────────────────────

        /// <summary>The pattern of the pieces of the file: the one of its first <c>Split</c> that has a regular expression.</summary>
        private static string? PatternOf(JsonElement root)
        {
            if (!root.TryGetProperty("pre_tokenizer", out var pre) || pre.ValueKind != JsonValueKind.Object)
                return null;

            if (pre.TryGetProperty("pretokenizers", out var list))
            {
                foreach (var one in list.EnumerateArray())
                {
                    if (RegexOf(one) is { } found)
                        return found;
                }
            }

            return RegexOf(pre);
        }

        private static string? RegexOf(JsonElement element) =>
            element.TryGetProperty("pattern", out var pattern) && pattern.TryGetProperty("Regex", out var regex) ? regex.GetString() : null;

        /// <summary>The characters of GPT-2 for the 256 bytes: the printable ones are themselves, the others are the characters from U+0100 on.</summary>
        private static char[] BuildByteToChar()
        {
            var table = new char[256];
            var next = 256;
            for (var value = 0; value < 256; value++)
            {
                var printable = value is >= '!' and <= '~' or >= 0xA1 and <= 0xAC or >= 0xAE and <= 0xFF;
                table[value] = printable ? (char)value : (char)next++;
            }

            return table;
        }
    }
}
