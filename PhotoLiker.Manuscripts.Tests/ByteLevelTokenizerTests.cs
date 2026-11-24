using System.Text;
using System.Text.Json;
using PhotoLiker.Manuscripts.Fixing;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>The tokenizer of the language model, on a vocabulary that is small enough to see: every byte is in it (as in the real one), and a few merges.</summary>
    public sealed class ByteLevelTokenizerTests
    {
        // In this vocabulary the token of a byte is the value of the byte; the merged tokens come after them
        private const int Bc = 300, Abc = 301, Aa = 302, Zhe = 303, SpaceA = 304, Ab = 305;

        /// <summary>The characters GPT-2 writes the bytes as (the printable ones are themselves, the others are from U+0100 on).</summary>
        private static string[] ByteCharacters()
        {
            var characters = new string[256];
            var next = 256;
            for (var value = 0; value < 256; value++)
                characters[value] = (value is >= 33 and <= 126 or >= 161 and <= 172 or >= 174 ? (char)value : (char)next++).ToString();
            return characters;
        }

        private static string Json(bool arrayMerges = false, string type = "BPE")
        {
            var bytes = ByteCharacters();
            var vocab = new Dictionary<string, int>();
            for (var i = 0; i < 256; i++)
                vocab[bytes[i]] = i;

            var a = bytes['a'];
            var b = bytes['b'];
            var c = bytes['c'];
            vocab[b + c] = Bc;
            vocab[a + b + c] = Abc;
            vocab[a + a] = Aa;
            vocab[bytes[0xD0] + bytes[0xB6]] = Zhe; // the two bytes of the Cyrillic zhe
            vocab[bytes[' '] + a] = SpaceA;
            vocab[a + b] = Ab;

            // The earliest merge goes first: b+c before a+b, so "abc" is a+bc and then abc
            (string, string)[] merges = [(b, c), (a, b), (a, b + c), (bytes[0xD0], bytes[0xB6]), (bytes[' '], a), (a, a)];
            var model = new Dictionary<string, object>
            {
                ["type"] = type,
                ["vocab"] = vocab,
                ["merges"] = merges.Select(merge => arrayMerges ? (object)new[] { merge.Item1, merge.Item2 } : merge.Item1 + " " + merge.Item2).ToArray(),
            };
            var root = new Dictionary<string, object>
            {
                ["model"] = model,
                ["added_tokens"] = new object[]
                {
                    new Dictionary<string, object> { ["id"] = 400, ["content"] = "<|im_start|>", ["special"] = true },
                    new Dictionary<string, object> { ["id"] = 401, ["content"] = "<|im_end|>", ["special"] = true },
                    new Dictionary<string, object> { ["id"] = 402, ["content"] = "<tool>", ["special"] = false },
                },
            };
            return JsonSerializer.Serialize(root);
        }

        private static ByteLevelTokenizer Tokenizer(string json) => ByteLevelTokenizer.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        [Fact]
        public void TheEarliestMergeGoesFirst_NotTheLeftmost()
        {
            // a+b is at the left, but b+c comes earlier in the list: a, bc, and then a+bc is the last merge
            Assert.Equal([Abc], Tokenizer(Json()).Encode("abc"));
        }

        [Fact]
        public void EveryPlaceOfAPair_IsMerged_FromTheLeft_WithoutOverlap()
        {
            var tokenizer = Tokenizer(Json());

            Assert.Equal([Bc, Bc], tokenizer.Encode("bcbc"));
            Assert.Equal([Aa, 'a'], tokenizer.Encode("aaa")); // aa, then a
        }

        [Fact]
        public void ThePiecesOfATextAreWrittenAsTheBytesOfItsUtf8_SoNothingIsUnknown()
        {
            var tokenizer = Tokenizer(Json());

            Assert.Equal([Zhe], tokenizer.Encode("ж")); // the two bytes of the letter, merged
            Assert.Equal([0xC3, 0xA9], tokenizer.Encode("é")); // two bytes that no merge joins
        }

        [Fact]
        public void ASpaceIsWrittenAsACharacterOfItsOwn_AndGoesWithTheLettersAfterIt()
        {
            var tokenizer = Tokenizer(Json());

            Assert.Equal(['a', SpaceA], tokenizer.Encode("a a")); // the pieces are "a" and " a"
            Assert.Equal(['a', ' ', Ab], tokenizer.Encode("a ab")); // in " ab" the pair a+b is earlier in the list than space+a
        }

        [Fact]
        public void ANewLineIsAPieceOfItsOwn_AndTheCyrillicLetterAfterItIsAnother() =>
            Assert.Equal(['a', '\n', Zhe], Tokenizer(Json()).Encode("a\nж"));

        [Fact]
        public void ADecodedText_IsTheTextThatWasEncoded()
        {
            var tokenizer = Tokenizer(Json());
            const string text = "abc a ab\nжж bcbc é";

            Assert.Equal(text, tokenizer.Decode(tokenizer.Encode(text)));
        }

        [Fact]
        public void TheSpecialTokens_AreNotInATextThatIsEncoded_AreLeftOutOfADecodedOne_AndAreFoundByTheirName()
        {
            var tokenizer = Tokenizer(Json());

            // What looks like a special token in a text is only text: a page cannot speak as the program
            var encoded = tokenizer.Encode("<|im_start|>");
            Assert.DoesNotContain(400, encoded);
            Assert.Equal("<|im_start|>", tokenizer.Decode(encoded));

            Assert.Equal(400, tokenizer.IdOf("<|im_start|>"));
            Assert.Equal(401, tokenizer.IdOf("<|im_end|>"));
            Assert.True(tokenizer.Has("<tool>"));
            Assert.False(tokenizer.Has("<|nothing|>"));
            Assert.Throws<InvalidDataException>(() => tokenizer.IdOf("<|nothing|>"));

            Assert.Equal("ж a", tokenizer.Decode([400, Zhe, 401, SpaceA]));
            Assert.Equal("<tool>", tokenizer.Decode([402])); // an added token that is not special is text
        }

        [Fact]
        public void AnAccentIsPutAfterItsLetterBeforeTheTextIsCut_LikeTheTokenizerOfTheModelDoes() =>
            Assert.Equal(Tokenizer(Json()).Encode("é"), Tokenizer(Json()).Encode("é"));

        [Fact]
        public void TheMergesInTheNewerFileFormat_ArePairsOfTokens() => Assert.Equal([Abc], Tokenizer(Json(arrayMerges: true)).Encode("abc"));

        [Fact]
        public void AnEmptyText_HasNoTokens() => Assert.Empty(Tokenizer(Json()).Encode(string.Empty));

        [Theory]
        [InlineData("this is not json")]
        [InlineData("{}")]
        [InlineData("{\"model\":{\"type\":\"Unigram\",\"vocab\":[]}}")]
        public void AFileThatIsNotATokenizer_IsRefused(string text) => Assert.Throws<InvalidDataException>(() => Tokenizer(text));

        [Fact]
        public void ATokenizerThatIsNotByteLevel_IsRefused() => Assert.Throws<InvalidDataException>(() => Tokenizer(Json(type: "WordPiece")));

        [Fact]
        public void TheTokenizerOfAFile_IsReadFromThePath()
        {
            var path = Path.Combine(Path.GetTempPath(), $"tokenizer-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, Json());
            try
            {
                Assert.Equal([Abc], ByteLevelTokenizer.Load(path).Encode("abc"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
