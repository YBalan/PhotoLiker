using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoLiker.Search
{
    /// <summary>The byte-pair tokenizer of OpenAI CLIP (vocab.json + merges.txt), a port of its simple_tokenizer.</summary>
    public sealed class ClipTokenizer
    {
        public const int StartOfText = 49406;
        public const int EndOfText = 49407;
        public const int MaxLength = 77;

        private static readonly Regex TokenPattern = new(
            @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|\p{L}+|\p{N}|[^\s\p{L}\p{N}]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly Dictionary<string, int> _vocab;
        private readonly Dictionary<(string, string), int> _ranks = [];
        private readonly Dictionary<byte, char> _byteToChar = BuildByteMap();
        private readonly Dictionary<string, List<string>> _cache = [];

        public ClipTokenizer(string vocabJsonPath, string mergesPath)
        {
            _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(vocabJsonPath))!;

            var rank = 0;
            foreach (var line in File.ReadLines(mergesPath))
            {
                if (line.StartsWith("#version", StringComparison.Ordinal)) continue;
                var parts = line.Split(' ');
                if (parts.Length == 2)
                    _ranks[(parts[0], parts[1])] = rank++;
            }
        }

        /// <summary>Token ids of the text: start of text, the tokens, end of text (cut to 77 ids at most).</summary>
        public long[] Encode(string text)
        {
            var clean = Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();

            var ids = new List<long> { StartOfText };
            foreach (Match match in TokenPattern.Matches(clean))
            {
                var mapped = new string(Encoding.UTF8.GetBytes(match.Value).Select(b => _byteToChar[b]).ToArray());
                foreach (var piece in Bpe(mapped))
                {
                    // A piece that is not in the vocabulary can only come from a broken vocabulary file
                    if (_vocab.TryGetValue(piece, out var id))
                        ids.Add(id);
                }
            }

            if (ids.Count > MaxLength - 1)
                ids.RemoveRange(MaxLength - 1, ids.Count - (MaxLength - 1));
            ids.Add(EndOfText);
            return [.. ids];
        }

        private List<string> Bpe(string token)
        {
            if (_cache.TryGetValue(token, out var cached))
                return cached;

            var word = token.Select(c => c.ToString()).ToList();
            word[^1] += "</w>";

            while (word.Count > 1)
            {
                var best = int.MaxValue;
                (string, string) bestPair = default;
                for (var i = 0; i < word.Count - 1; i++)
                {
                    if (_ranks.TryGetValue((word[i], word[i + 1]), out var r) && r < best)
                    {
                        best = r;
                        bestPair = (word[i], word[i + 1]);
                    }
                }
                if (best == int.MaxValue) break;

                var merged = new List<string>(word.Count);
                for (var i = 0; i < word.Count; i++)
                {
                    if (i < word.Count - 1 && word[i] == bestPair.Item1 && word[i + 1] == bestPair.Item2)
                    {
                        merged.Add(word[i] + word[i + 1]);
                        i++;
                    }
                    else
                    {
                        merged.Add(word[i]);
                    }
                }
                word = merged;
            }

            _cache[token] = word;
            return word;
        }

        /// <summary>GPT-2 / CLIP table that maps every byte to a printable character.</summary>
        private static Dictionary<byte, char> BuildByteMap()
        {
            var printable = new List<int>();
            printable.AddRange(Enumerable.Range('!', '~' - '!' + 1));
            printable.AddRange(Enumerable.Range(0xA1, 0xAC - 0xA1 + 1));
            printable.AddRange(Enumerable.Range(0xAE, 0xFF - 0xAE + 1));

            var map = new Dictionary<byte, char>();
            foreach (var b in printable)
                map[(byte)b] = (char)b;

            var extra = 0;
            for (var b = 0; b < 256; b++)
            {
                if (!map.ContainsKey((byte)b))
                    map[(byte)b] = (char)(256 + extra++);
            }
            return map;
        }
    }
}
