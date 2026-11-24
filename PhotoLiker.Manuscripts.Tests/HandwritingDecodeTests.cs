using PhotoLiker.Manuscripts.Handwriting;

namespace PhotoLiker.Manuscripts.Tests
{
    /// <summary>How the answers of a handwriting network become text (CTC): the likeliest answer of each column, repeats counted once, the blank dropped.</summary>
    public sealed class HandwritingDecodeTests
    {
        private static readonly string[] Alphabet = ["", " ", "а", "б"];

        /// <summary>The answers of columns, each column as scores for blank, space, а, б: the answer a column is for is the one that gets the high score.</summary>
        private static float[] Columns(params int[] likeliest)
        {
            var scores = new float[likeliest.Length * Alphabet.Length];
            for (var t = 0; t < likeliest.Length; t++)
            {
                for (var c = 0; c < Alphabet.Length; c++)
                    scores[t * Alphabet.Length + c] = c == likeliest[t] ? 5f : 0f;
            }

            return scores;
        }

        [Fact]
        public void ARepeatOfTheSameAnswerInTheNextColumn_IsOneLetter_ButTheBlankBetweenTwoMakesTwo()
        {
            // а а [blank] а б б -> "аaб" with the second а after a blank: а, а, б
            var (text, _) = HandwritingReader.Decode(Columns(2, 2, 0, 2, 3, 3), 6, Alphabet);

            Assert.Equal("ааб", text);
        }

        [Fact]
        public void ACharacterThatIsNotAllowed_IsNeverTheAnswer_TheLikeliestOfTheOthersIs()
        {
            // б is the likeliest in the second column but may not be an answer: the third column has а as the next, the blank is the answer of the others
            bool[] allowed = [true, true, true, false];
            var scores = new float[3 * Alphabet.Length];
            scores[0 * 4 + 2] = 5; // а
            scores[1 * 4 + 3] = 5; // б, not allowed
            scores[1 * 4 + 0] = 1; // so the blank is the answer of the column
            scores[2 * 4 + 3] = 5; // б
            scores[2 * 4 + 2] = 3; // а is next

            var (text, _) = HandwritingReader.Decode(scores, 3, Alphabet, allowed);

            Assert.Equal("аа", text);
        }

        [Fact]
        public void TheSureness_OfAMaskedAnswer_IsCountedAmongTheAllowedAnswersOnly()
        {
            var scores = new float[Alphabet.Length]; // one column: а is likeliest, б is as likely and not allowed
            scores[2] = 4;
            scores[3] = 4;

            var (_, withBoth) = HandwritingReader.Decode(scores, 1, Alphabet);
            var (text, masked) = HandwritingReader.Decode(scores, 1, Alphabet, [true, true, true, false]);

            Assert.Equal("а", text);
            Assert.True(masked > withBoth, "the answer that may not be given takes none of the chance");
        }

        [Fact]
        public void TheSpace_IsAnAnswerLikeTheLetters()
        {
            var (text, _) = HandwritingReader.Decode(Columns(2, 1, 3), 3, Alphabet);

            Assert.Equal("а б", text);
        }

        [Fact]
        public void OnlyBlanks_AreNoTextAndNoConfidence()
        {
            var (text, confidence) = HandwritingReader.Decode(Columns(0, 0, 0), 3, Alphabet);

            Assert.Equal(string.Empty, text);
            Assert.Equal(0f, confidence);
        }

        [Fact]
        public void TheConfidence_IsTheMeanChanceOfTheLettersThatWereTaken()
        {
            // Column 0 is sure of "а" (a score of 5 against three zeros: e^5 / (e^5 + 3)); column 1 is nearly even, and its likeliest answer is "а" again, which is the same letter and counts once
            var scores = new float[8];
            scores[2] = 5f;
            scores[6] = 0.01f;
            var (text, confidence) = HandwritingReader.Decode(scores, 2, Alphabet);

            Assert.Equal("а", text);
            var first = Math.Exp(5) / (Math.Exp(5) + 3);
            Assert.InRange(confidence, 0.9f, 1f);
            Assert.Equal((float)first, confidence, 3);
        }

        [Fact]
        public void ACharacterThatIsEmpty_IsNeverAnswered()
        {
            // The checkpoint has no character for an answer: the answer is skipped, not written as nothing between the letters
            string[] alphabet = ["", "", "а"];
            var scores = new float[] { 0, 5, 0, 0, 0, 5 };

            var (text, _) = HandwritingReader.Decode(scores, 2, alphabet);

            Assert.Equal("а", text);
        }
    }
}
