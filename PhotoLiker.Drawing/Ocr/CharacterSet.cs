using System.Reflection;

namespace PhotoLiker.Drawing.Ocr
{
    /// <summary>
    /// The letters the reading model knows, and how its answer is turned into text. The model gives, for each slice of the line from left to right, how likely each letter is to be there: the first answer
    /// is "nothing here", then the letters of the dictionary one after another, and the last is the space. Reading the line is taking the likeliest answer of each slice, dropping "nothing", and
    /// letting a letter that is the answer of several slices in a row count once (CTC, connectionist temporal classification).
    /// </summary>
    internal sealed class CharacterSet
    {
        private readonly string[] _symbols; // index 0 is the answer "nothing"

        /// <summary>Makes the set from the dictionary: one letter to a line (a line break may be \n or \r\n).</summary>
        public CharacterSet(string dictionary)
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            var letters = dictionary.Replace("\r", string.Empty).Split('\n').ToList();
            if (letters.Count > 0 && letters[^1].Length == 0)
                letters.RemoveAt(letters.Count - 1); // the line break at the end of the file

            _symbols = new string[letters.Count + 2];
            _symbols[0] = string.Empty;
            for (var i = 0; i < letters.Count; i++)
                _symbols[i + 1] = letters[i];
            _symbols[^1] = " ";
        }

        /// <summary>The set that comes with the program: East Slavic (Ukrainian, Russian, Belarusian, Bulgarian) and English, as the model of that name was trained on it.</summary>
        public static CharacterSet EastSlavic { get; } = FromResource("PhotoLiker.Drawing.Ocr.eslav_dict.txt");

        /// <summary>How many answers the model gives for each slice: "nothing", every letter, and the space.</summary>
        public int Count => _symbols.Length;

        /// <summary>For each answer of the model, whether <paramref name="allow"/> accepts its character; "nothing" (the first answer) is always allowed, the model has to be able to say it.</summary>
        public bool[] Allowing(Func<string, bool> allow)
        {
            var allowed = new bool[_symbols.Length];
            allowed[0] = true;
            for (var i = 1; i < _symbols.Length; i++)
                allowed[i] = allow(_symbols[i]);
            return allowed;
        }

        private static CharacterSet FromResource(string name)
        {
            using var stream = typeof(CharacterSet).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The dictionary {name} is not in the program.");
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return new CharacterSet(reader.ReadToEnd());
        }

        /// <summary>
        /// The text that the answers of the model make, and how sure it is (the mean of how likely the letters that were taken are). <paramref name="answers"/> is slices by <see cref="Count"/> numbers;
        /// a model that gives scores that are not chances (that do not add up to one for a slice) is turned into chances first.
        /// </summary>
        /// <param name="allowed">For each answer, whether the model may take it (see <see cref="Allowing"/>); null for all. The likeliest answer of the ones that are allowed is the one that is taken.</param>
        public (string Text, float Confidence) Decode(ReadOnlySpan<float> answers, int slices, bool[]? allowed = null)
        {
            if (allowed is not null && allowed.Length != Count)
                throw new ArgumentException($"{Count} answers can be allowed or not, not {allowed.Length}.", nameof(allowed));

            if (answers.Length != slices * Count)
                throw new ArgumentException($"{slices} slices of {Count} answers are {slices * Count} numbers, not {answers.Length}.", nameof(answers));

            var text = new System.Text.StringBuilder();
            double sure = 0;
            var taken = 0;
            var last = -1;
            for (var t = 0; t < slices; t++)
            {
                var row = answers.Slice(t * Count, Count);
                var best = 0;
                var sum = 0f;
                var largest = row[0];
                for (var c = 0; c < row.Length; c++)
                {
                    sum += row[c];
                    largest = Math.Max(largest, row[c]);
                    if ((allowed is null || allowed[c]) && row[c] > row[best])
                        best = c; // the answer 0 is where it starts and is always allowed
                }

                // A softmax adds up to one; anything else is scores that have to be made into chances (their largest is the likeliest the same way)
                var chance = row[best];
                if (sum is < 0.98f or > 1.02f)
                {
                    double total = 0;
                    for (var c = 0; c < row.Length; c++)
                        total += Math.Exp(row[c] - largest);
                    chance = (float)(Math.Exp(row[best] - largest) / total);
                }

                if (best != 0 && best != last)
                {
                    text.Append(_symbols[best]);
                    sure += chance;
                    taken++;
                }
                last = best;
            }

            return (text.ToString(), taken == 0 ? 0f : (float)(sure / taken));
        }
    }
}
